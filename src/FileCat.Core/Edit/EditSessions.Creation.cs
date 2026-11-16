using FileCat.Core.Archives;
using FileCat.Core.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    /// <summary>
    /// The <see cref="Exception.Data"/> key under which a stopped or failed native creation names what it had to keep
    /// (whatever could not be removed exactly); absent when everything it made was removed.
    /// </summary>
    public const string RetainedCreationKey = "FileCat.Edit.RetainedCreation";

    // Native creations this process is making, by session id. Until the creation returns it is not an edit: the session
    // list leaves it out, and this process's storage reviews charge it at the size it was admitted with (and the free space
    // its copy still needs), because its bytes are copied without holding the storage gate (a slow server copy does not
    // hold up other edits). After a crash nothing is registered any more, so the creation marker shows the interrupted copy.
    private static readonly Dictionary<string,CreationReservation> Creating=new(StringComparer.Ordinal);
    private sealed class CreationReservation(string? rootIdentity,long payload,long working,SafeFileHandle? output)
    {
        internal string? RootIdentity{get;}=rootIdentity;
        internal long Payload{get;}=payload;
        // The bytes its working copy still has to receive: the admitted length less what the held output already has.
        internal long Pending
        {
            get
            {
                if(output is null)return working;
                // The copy has ended (its handle is closed): its bytes are on disk, or about to be removed. Any other
                // failure to measure it leaves all of its copy counted as still to be written.
                try{return Math.Max(0,working-RandomAccess.GetLength(output));}
                catch(ObjectDisposedException){return 0;}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException or
                    NotSupportedException or InvalidOperationException){return working;}
            }
        }
    }

    /// <summary>Whether this process is still making the native edit with this id (it is not an edit yet).</summary>
    internal static bool IsCreating(string id){lock(Creating)return Creating.ContainsKey(id);}

    private static CreationReservation? CreatingIn(string id,string rootIdentity)
    {lock(Creating)return Creating.TryGetValue(id,out var value)&&value.RootIdentity==rootIdentity?value:null;}

    // Another program holds a file FileCat is removing for a moment (a scanner, another FileCat window's review): Windows
    // refuses the removal's open with a sharing violation, Linux the removal's lease. Each attempt is exact on its own.
    private static bool HeldForNow(Exception error)=>error is EditFileInUseException||
        OperatingSystem.IsWindows()&&error is IOException&&(error.HResult&0xFFFF)==32;

    /// <summary>
    /// The objects one native creation made, and their exact removal when the creation does not complete: stopped,
    /// cancelled or failed (plan risk R21, UNIX_EDIT_EXACT_REMOVAL_BOUNDARY.md). A file is removed only through a handle
    /// bound to the identity recorded when FileCat created it (on Unix its name only while that name still binds the held
    /// object, which then has no link left); a folder only once empty and while its name binds it. Nothing is removed by
    /// pathname. Whatever cannot be removed exactly is kept, logged and named in the failure; the creation marker goes
    /// last, so anything kept still shows in the session list as an interrupted edit. While the creation runs another FileCat
    /// process sees it as running: on Unix through the creation's lock on its folder, held until the creation ends; on
    /// Windows through its open marker (and its folders' deletion handles); before either exists, through the storage gate.
    /// </summary>
    private sealed class NativeCreation:IDisposable
    {
        private readonly EditDirectoryLease root;
        private readonly string id,leaf;
        private readonly List<CreatedRecord> records=[];
        private string? workingIdentity;
        private bool complete,foldersAttempted,copied,workingRemoved;
        internal EditDirectoryLease? Session{get;private set;}
        internal EditDirectoryLease? Files{get;private set;}

        internal NativeCreation(EditDirectoryLease root,string id,string leaf)
        {
            this.root=root;this.id=id;this.leaf=leaf;
            // Registered before its folder exists, so no session list ever sees it half made.
            lock(Creating)Creating.Add(id,new(null,0,0,null));
        }

        /// <summary>Charges this creation at its admitted size in this process's storage reviews (under the gate).</summary>
        internal void Reserve(long payload,long working){lock(Creating)Creating[id]=new(root.Identity,payload,working,null);}

        // Each object is made only after the token is checked, and then without it: a cancellation can no longer interrupt
        // the call after the object exists and before this creation holds it (an untracked object could not be removed).
        internal void CreateFolders(CancellationToken ct)
        {
            // Created removable: these leases may remove their own reviewed entries, as a discard or a commit copy does (on
            // Windows they hold deletion access, which other processes' opens for use or removal do not share; their
            // read-only inventory reviews share everything).
            ct.ThrowIfCancellationRequested();foldersAttempted=true;Session=root.CreateDirectory(id,CancellationToken.None,removable:true);
            // On Unix another FileCat process then sees a creation still running through this creation's exclusive lock on
            // its folder (a process that ends releases it); until the lock is taken, the storage gate this creation holds
            // keeps their judgement open. Their test takes a shared lock for a moment, so the lock is retried briefly.
            for(int attempt=0;!OperatingSystem.IsWindows()&&!FileCat.Core.Archives.Unix.UnixArchiveNative.TryLockEditCreation(Session.Handle,exclusive:true);attempt++)
            {
                if(attempt>=400)throw new IOException("The new edit's folder could not be held."); // Two seconds, under the gate.
                ct.ThrowIfCancellationRequested();Thread.Sleep(5);
            }
            ct.ThrowIfCancellationRequested();Files=Session.CreateDirectory("files",CancellationToken.None,removable:true);
        }

        internal FileStream CreateWorking(long length,CancellationToken ct)
        {
            var files=Files??throw new IOException("The native working directory is unavailable.");
            ct.ThrowIfCancellationRequested();
            var stream=files.CreateFile(leaf,CancellationToken.None);
            try
            {
                workingIdentity=files.Identify(stream.SafeFileHandle);
                lock(Creating)if(Creating[id] is { } reserved)Creating[id]=new(reserved.RootIdentity,reserved.Payload,length,stream.SafeFileHandle);
                return stream;
            }
            catch{stream.Dispose();throw;}
        }

        internal string WorkingIdentity=>workingIdentity??throw new IOException("The native working file was not created.");

        /// <summary>
        /// Publishes one record under a new name; its object is tracked from creation, published or not. The creation
        /// marker stays open while the creation runs (on Windows that handle shares no writing, so another FileCat process
        /// sees the copy as one still being made). The edit's own record completes the creation as soon as its name is
        /// published: nothing of it is removed after, and its handles are given up at once.
        /// </summary>
        internal void PublishRecord(string name,byte[] bytes,CancellationToken ct,bool hold=false,bool completes=false)
        {
            if(bytes.Length>MaxRecordBytes)throw new IOException("The edit record exceeds its metadata budget.");
            var session=Session??throw new IOException("The native session folder is unavailable.");
            // Named apart from the stage any other record publication makes: only a creation's own stage says that a copy
            // was being published when FileCat ended.
            var record=new CreatedRecord(CreationStagePrefix+Guid.NewGuid().ToString("N")+".tmp",name);
            ct.ThrowIfCancellationRequested();
            var stream=session.CreateFile(record.Temporary,CancellationToken.None);
            record.Stream=stream;records.Add(record);
            record.Identity=session.Identify(stream.SafeFileHandle);
            stream.Write(bytes);stream.Flush(true);
            // The publication reviews the new name and its bytes itself; after it returns, the record is there.
            session.PublishNewFile(stream,record.Temporary,name,new(record.Identity,bytes.Length,Digest(bytes)),ct);
            // Complete: its handles go at once, and it is an edit from here (the session list no longer leaves it out).
            if(completes){complete=true;Release();lock(Creating)Creating.Remove(id);return;}
            if(!hold){stream.Dispose();record.Stream=null;}
        }

        /// <summary>
        /// Removes what this creation made, under the storage gate and without the cancelled token. When the creation failed
        /// while it held the gate (<paramref name="held"/>: its admission or its publication), the records it made go under
        /// that gate before it is given up, so a record it published is gone before anyone else can take the gate; when it
        /// had made no working copy yet, everything goes under that gate (no one sees its folder without its hold). It does
        /// not throw for its cleanup's I/O, access or handle failures (what they keep is said), and always gives
        /// <paramref name="held"/> up: what is kept is logged and named on <paramref name="primary"/> under
        /// <see cref="RetainedCreationKey"/>.
        /// </summary>
        internal void Abandon(Exception primary,EditStorageAdmission? held=null)
        {
            try
            {
                if(complete)return;
                string folder=Path.Combine(root.Path,id),text;string? kept=null;
                if(Session is null)
                {
                    // Its folder may have been made although no lease could hold it (its private review refused it): nothing
                    // then binds that folder to this creation, so it is kept and said, never removed by name.
                    if(!foldersAttempted)return;
                    bool? listed=Listed();
                    if(listed==false)return;
                    text=listed is null
                        ?"FileCat may have kept a folder for the unfinished copy of “"+leaf+"” at "+folder+": it could not hold that folder, "+
                            "nor check whether it was made. If it was, it is listed in File → Edit sessions for review."
                        :"FileCat kept a folder for the unfinished copy of “"+leaf+"” at "+folder+": it could not hold that folder. "+
                            "It is listed in File → Edit sessions for review.";
                }
                else
                {
                    folder=Session.Path;
                    try
                    {
                        if(held is not null)
                        {
                            held.Review(CancellationToken.None);
                            kept=workingIdentity is null?Remove(null):RemoveRecords();
                            held.Dispose();held=null;
                        }
                        if(kept is null&&Session is not null)
                        {
                            // The working copy is read once before the gate is taken (nothing writes it any more); its removal
                            // under the gate reviews it once more under its own exclusion, so the gate is held for one read of
                            // it, not two. The copy is over: its removal may wait for another writer longer than a copy would.
                            var working=ObserveWorking();
                            using var gate=EditStorageAdmission.Acquire(root,CancellationToken.None,TimeSpan.FromMinutes(2));
                            kept=Remove(working);
                        }
                    }
                    catch(Exception error)when(Cleanup(error)){kept=error.Message;}
                    if(kept is null)return;
                    // What is left decides what the user is told: a whole edit (its record still there), the copy without
                    // the record that speaks for it, or an unfinished copy.
                    text=RecordKept()
                        ?"FileCat could not complete the edit of “"+leaf+"” and could not remove its record, so the edit is kept at "+folder+
                            ": "+Sentence(kept)+" Review it in File → Edit sessions."
                        :!WorkingKept()&&workingRemoved
                        ?"FileCat removed the copy it had made of “"+leaf+"” but kept its folder at "+folder+": "+Sentence(kept)+
                            " Review it in File → Edit sessions."
                        :WorkingKept()&&copied
                        // A whole copy with no record: FileCat cannot tell such a copy from an edit of the user's, so it
                        // keeps it whole and the list offers no removal for it. The way out is said here instead of
                        // sending the user to a row that cannot act.
                        ?"FileCat could not complete the edit of “"+leaf+"” and could not remove the copy it had made, so that copy is kept at "+
                            folder+": "+Sentence(kept)+" FileCat lists it in File → Edit sessions and keeps it; open its folder from there to see "+
                            "the copy, which is yours to delete once you no longer need it."
                        :workingIdentity is null
                        ?"FileCat kept the folder it had started for the copy of “"+leaf+"” at "+folder+": "+Sentence(kept)+
                            " It is listed in File → Edit sessions for review."
                        :"FileCat kept the unfinished copy of “"+leaf+"” at "+folder+": "+Sentence(kept)+
                            " It is listed in File → Edit sessions for review.";
                }
                AppLog.Warn(text);
                try{primary.Data[RetainedCreationKey]=text;}
                catch(Exception error)when(error is ArgumentException or NotSupportedException){}
            }
            finally{held?.Dispose();}
        }

        // What a cleanup step may fail with (it is then kept and said, never thrown past the creation's own failure).
        private static bool Cleanup(Exception error)=>error is IOException or UnauthorizedAccessException or NotSupportedException or
            ArgumentException or InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception;

        // Whether the root lists this creation's folder; null when the listing fails.
        private bool? Listed()
        {
            try{return root.Names(CancellationToken.None,8192).Contains(id,StringComparer.Ordinal);}
            catch(Exception error)when(Cleanup(error)){return null;}
        }

        // Whether the edit's record is still in its folder (false when that cannot be seen).
        private bool RecordKept()
        {
            try{return Session is { } session&&session.Names(CancellationToken.None,8192).Contains(RecordFile,StringComparer.Ordinal);}
            catch(Exception error)when(Cleanup(error)){return false;}
        }

        // Whether the copy this creation made is still there (false when that cannot be seen).
        private bool WorkingKept()
        {
            try{return Files is { } files&&files.Names(CancellationToken.None,8192).Contains(leaf,StringComparer.Ordinal);}
            catch(Exception error)when(Cleanup(error)){return false;}
        }

        /// <summary>Said when the copy has received every byte: an unfinished copy is shorter, and is said to be.</summary>
        internal void Copied()=>copied=true;

        private static string Sentence(string text)
        {
            text=text.TrimEnd();
            return text.EndsWith('.')||text.EndsWith('!')||text.EndsWith('?')?text:text+".";
        }

        // The working copy this creation made, as it is now, while its name binds it; null when it cannot be read here (its
        // removal then reviews it itself).
        private ArchiveFileObservation? ObserveWorking()
        {
            if(Files is not { } files||workingIdentity is null)return null;
            try
            {
                if(!files.Names(CancellationToken.None,8192).Contains(leaf,StringComparer.Ordinal))return null;
                using var file=files.OpenDiscardFile(leaf,workingIdentity,CancellationToken.None);
                return files.ObserveSnapshot(file,leaf,CancellationToken.None,MaxMemberBytes);
            }
            catch(Exception error)when(Cleanup(error)){return null;}
        }

        // The records this creation made except its marker, a published edit record first: if anything after it is kept,
        // what remains is an interrupted copy, never an edit whose working copy is missing. Null when they are gone.
        private string? RemoveRecords()
        {
            var order=records.Where(record=>record.Name==RecordFile).Concat(records.Where(record=>record.Name!=RecordFile&&record.Name!=CreationMarker))
                .ToList();
            foreach(var record in order)if(RemoveRecord(record) is { } kept)return kept;
            return null;
        }

        // Null when everything was removed, otherwise why something was kept: the records, then the working copy, its
        // folder, the creation marker (whose handle this creation keeps until its own removal) and the session folder.
        private string? Remove(ArchiveFileObservation? working)
        {
            if(RemoveRecords() is { } recordKept)return recordKept;
            if(Files is { } files)
            {
                if(workingIdentity is not null)
                {
                    if(RemoveFile(files,[leaf],workingIdentity,MaxMemberBytes,working) is { } kept)return kept;
                    workingRemoved=true; // Its own object is gone; whatever is kept after this is not the user's copy.
                }
                files.RemoveEmpty(CancellationToken.None);Files=null; // The removed folder's lease ends with it.
            }
            foreach(var record in records.Where(record=>record.Name==CreationMarker))if(RemoveRecord(record) is { } kept)return kept;
            Session!.RemoveEmpty(CancellationToken.None);Session=null;
            return null;
        }

        private string? RemoveRecord(CreatedRecord record)
        {
            // This creation's own handle closes first: a Windows removal opens the object again for deletion.
            record.Stream?.Dispose();record.Stream=null;
            if(record.Identity is not null)return RemoveFile(Session!,[record.Name,record.Temporary],record.Identity,MaxRecordBytes);
            // Nothing binds this record to the object FileCat made, so it is kept and named, never removed by its name alone.
            try
            {
                var present=Session!.Names(CancellationToken.None,8192);
                return present.Contains(record.Temporary,StringComparer.Ordinal)||present.Contains(record.Name,StringComparer.Ordinal)
                    ?"the record “"+record.Name+"” could not be identified when FileCat made it, so it is kept":null;
            }
            catch(Exception error)when(Cleanup(error)){return error.Message;}
        }

        // The object recorded at creation, under whichever of its names now binds it (reviewed already when observed is
        // given). Null when it was removed or no such name binds it (a folder left non-empty then keeps the rest); otherwise
        // why it was kept.
        private static string? RemoveFile(EditDirectoryLease directory,string[] names,string identity,long maximum,
            ArchiveFileObservation? observed=null)
        {
            var present=directory.Names(CancellationToken.None,8192);string? problem=null;
            foreach(string name in names.Distinct(StringComparer.Ordinal))
            {
                if(!present.Contains(name,StringComparer.Ordinal))continue;
                for(int attempt=0;;attempt++)
                {
                    try
                    {
                        using var file=directory.OpenDiscardFile(name,identity,CancellationToken.None);
                        var reviewed=observed??directory.ObserveSnapshot(file,name,CancellationToken.None,maximum);
                        if(reviewed.Version.Identity!=identity)throw new IOException("Another object has the name “"+name+"”.");
                        directory.DeleteSnapshot(file,name,reviewed,CancellationToken.None);
                        return null;
                    }
                    // A scanner (or another FileCat's review) may hold a file FileCat has just written for a moment; a lasting
                    // refusal keeps it.
                    catch(IOException error)when(attempt<HeldRetries&&HeldForNow(error)){Thread.Sleep(50);}
                    catch(Exception error)when(error is IOException or UnauthorizedAccessException)
                    {problem??=error.Message;break;}
                }
            }
            return problem;
        }

        // A complete creation's handles: its records' streams, then its folders' leases.
        private void Release()
        {
            try
            {
                foreach(var record in records){record.Stream?.Dispose();record.Stream=null;}
                try{Files?.Dispose();}finally{Session?.Dispose();}
            }
            finally{Files=null;Session=null;}
        }

        public void Dispose()
        {
            try{Release();}
            finally{lock(Creating)Creating.Remove(id);} // A completed creation is already gone from the register.
        }

        private sealed class CreatedRecord(string temporary,string name)
        {
            internal string Temporary{get;}=temporary;
            internal string Name{get;}=name;
            internal FileStream? Stream;
            internal string? Identity;
        }
    }

    // How often a removal tries again while another program holds the file for a moment (each 50 ms apart: 5 s in all).
    private const int HeldRetries=100;
}
