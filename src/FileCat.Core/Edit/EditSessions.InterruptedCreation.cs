using System.Text.Json;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    // A creation marker names its edit, its working file, the two folders the creation made and the working length it was
    // admitted with. Markers of earlier native builds name only the first three.
    private static readonly string[] CreationMarkerFields=["Id","Version","WorkingPath","SessionDirectoryIdentity","WorkingDirectoryIdentity","Length"];
    private static readonly string[] EarlierCreationMarkerFields=["Id","Version","WorkingPath"];

    /// <summary>Whether this store can remove an interrupted native copy (the review action for one).</summary>
    public bool SupportsInterruptedCopyRemoval=>nativeOpen is not null;

    /// <summary>
    /// The review action for an interrupted native copy: an edit whose copy never completed, because FileCat ended while it
    /// made it, or kept what it could not remove exactly. Its folder is reviewed afresh: the creation marker (this folder's
    /// id, the folders it was made in and a working file directly in its working folder), the working folder with at most
    /// that partly copied file, unpublished record stages and names an interrupted exact removal set aside; a folder the
    /// creation ended in before it wrote its marker holds no working file, and a working file larger than the length its
    /// marker admitted is not a partial copy. Each file is read once before the storage gate is taken, and refused if another
    /// program holds it (on Windows and Linux). Under the gate the folder must still hold exactly what was reviewed,
    /// with no creation holding it, and each reviewed object is then removed exactly, as a discard removes a session: a file
    /// only through a handle bound to the identity reviewed, its content reviewed again under its own exclusion (on Unix its
    /// name only while it binds that object), a folder only once empty while its name binds it. A folder holding anything
    /// else, a published record, or a copy another FileCat process is still making, is kept whole.
    /// </summary>
    public EditDiscardResult RemoveInterruptedCopy(EditSessionRecord row,CancellationToken ct=default,Action? check=null)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();check?.Invoke();
        if(nativeOpen is null||row.IsSupported||row.UnsupportedReason!=InterruptedCreationReason)
            throw new NotSupportedException("This edit is not an interrupted copy FileCat can remove. Its files are kept.");
        if(IsCreating(row.Id))throw new IOException("This copy is still being made. Its files are kept.");
        using var root=OpenNativeRoot(ct,false);
        if(!root.CanDiscardReviewedSession)throw new NotSupportedException("FileCat cannot safely remove this copy on this system. Its files are kept.");
        EditDirectoryLease opened;
        try{opened=root.OpenDiscardDirectory(row.Id,null,ct);}
        catch(Exception error)when(IsMissing(error)){return new(true,false);} // Already gone: nothing of it is left.
        // On Windows a folder another process holds for deletion refuses this open (a creation it still runs, a removal).
        catch(IOException error)when(OperatingSystem.IsWindows()&&(error.HResult&0xFFFF)==32)
        {return new(false,false,"This edit's folder is in use (another FileCat window may still be copying it). Its files are kept.");}
        using var session=opened;
        EditStorageAdmission? admission=null;EditDirectoryLease? files=null;
        void Check(){ct.ThrowIfCancellationRequested();check?.Invoke();admission?.Review(ct);}
        bool attempted=false;
        try
        {
            Check();
            // On Unix a creation that another FileCat process still runs holds its folder's lock.
            if(CreationHeldElsewhere(session))throw new IOException("Another FileCat window is still copying this edit.");
            var names=session.Names(ct,8192).ToArray();
            bool marked=names.Contains(CreationMarker,StringComparer.Ordinal);
            // Anything else (an editor's backup, say) is named for the user to remove first: FileCat removes only
            // objects it can review as its own, and the desktop's own file is not one - it is written for anyone to
            // read, which the removal's review refuses. FileCat's advice to open the working folder is what puts it
            // there, so it is named for what it is, with the way out, instead of as something unknown.
            var other=names.Where(name=>name!=CreationMarker&&name!="files"&&!IsRecordStage(name)&&!IsSetAside(name)).ToArray();
            if(other.Length!=0)
                throw new IOException(other.All(IsDesktopArtifact)
                    ?"This folder also holds "+Listing(other)+", which the desktop wrote when the folder was opened, and FileCat removes only what it "+
                        "made. Delete "+(other.Length==1?"that file":"those files")+" in the Finder (Command-Shift-Period shows hidden files), then try "+
                        "again. Its files are kept."
                    :"This folder also holds "+Listing(other)+", which "+(other.Length==1?"is":"are")+" not part of the unfinished copy: remove "+
                        (other.Length==1?"it":"them")+" yourself if not needed, then try again. Its files are kept.");
            (string Name,ArchiveFileObservation Observed)? marker=null;string? leaf=null,filesIdentity=null;long? admitted=null;
            if(marked)
            {
                var observed=ReviewFile(session,CreationMarker,MaxRecordBytes,out byte[] markerBytes);
                (leaf,string? sessionIdentity,filesIdentity,admitted)=RequireCreationMarker(markerBytes,row.Id);
                if(sessionIdentity is not null&&session.Identity!=sessionIdentity)
                    throw new IOException("The interrupted copy's folder is not the one its creation made.");
                marker=(CreationMarker,observed);
            }
            var loose=names.Where(name=>IsRecordStage(name)||IsSetAside(name))
                .Select(name=>(Name:name,Observed:ReviewFile(session,name,IsRecordStage(name)?MaxRecordBytes:MaxMemberBytes,out _))).ToArray();
            var inside=new List<(string Name,ArchiveFileObservation Observed)>();string[] entries=[];
            if(names.Contains("files",StringComparer.Ordinal))
            {
                files=session.OpenDiscardDirectory("files",filesIdentity,ct);
                entries=files.Names(ct,8192).ToArray();
                // The partly copied working file (none without a marker) and what an interrupted removal set aside.
                var extra=entries.Where(name=>name!=leaf&&!IsSetAside(name)).ToArray();
                if(extra.Length!=0)
                    throw new IOException(extra.All(IsDesktopArtifact)
                        ?"The unfinished copy's working folder also holds "+Listing(extra)+", which the desktop wrote when the folder was opened, and "+
                            "FileCat removes only what it made. Delete "+(extra.Length==1?"that file":"those files")+" in the Finder "+
                            "(Command-Shift-Period shows hidden files), then try again. Its files are kept."
                        :"The unfinished copy's working folder also holds "+Listing(extra)+": remove "+(extra.Length==1?"it":"them")+
                            " yourself if not needed, then try again. Its files are kept.");
                foreach(string name in entries)inside.Add((name,ReviewFile(files,name,MaxMemberBytes,out _)));
                // A copy a creation did not finish is shorter than it was admitted to be. One that is already that long (or
                // longer, so changed since; or whose marker does not say) is only an unfinished copy while its record was
                // being published; otherwise FileCat cannot tell it from an edit of the user's and keeps it whole.
                if(inside.FirstOrDefault(file=>file.Name==leaf) is { Name: not null } working&&!names.Any(IsCreationStage)&&
                    working.Observed.Version.Length!=0&&!(admitted is { } most&&working.Observed.Version.Length<most))
                    throw new IOException(admitted is null
                        ?"This copy may be complete, and its record does not say how long it was made to be, so FileCat cannot tell it from an edit "+
                            "of yours: its files are kept. Open the working folder to review them."
                        :"This copy is already as long as the file it was made from, so FileCat cannot tell it from an edit of yours: "+
                            "its files are kept. Open the working folder to review them.");
            }
            // Removal takes the gate: no creation is then between its folder and its hold, and no other removal runs. The
            // folder must still be exactly what was reviewed, and no creation may hold it.
            admission=EditStorageAdmission.Acquire(root,ct);
            try{session.Review(ct);}catch(Exception error)when(IsMissing(error)){return new(true,false);} // Removed meanwhile.
            Check();
            if(CreationHeldElsewhere(session))throw new IOException("Another FileCat window is still copying this edit.");
            if(!Same(session.Names(ct,8192),names)||files is not null&&!Same(files.Names(ct,8192),entries))
                throw new IOException("The interrupted copy's folder changed while it was reviewed. Try again.");
            // The working folder's files first, then the folder, the loose files, the marker and the folder itself.
            foreach(var file in inside){attempted=true;Remove(files!,file.Name,file.Observed);}
            if(files is not null){Check();attempted=true;files.RemoveEmpty(ct);files=null;}
            foreach(var file in loose){attempted=true;Remove(session,file.Name,file.Observed);}
            if(marker is { } held){attempted=true;Remove(session,held.Name,held.Observed);}
            Check();attempted=true;session.RemoveEmpty(ct);
            return new(true,true);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or NotSupportedException or OperationCanceledException or
            JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if(error is OperationCanceledException&&!attempted)throw;
            string message=attempted?"Removing the interrupted copy stopped part way. Its remaining files are kept at "+session.Path:
                "The interrupted copy could not be removed safely. Its files are kept at "+session.Path;
            return new(false,attempted,message+". "+error.Message);
        }
        finally{try{files?.Dispose();}finally{admission?.Dispose();}}

        // One complete read binds each file to the object reviewed, and a file another program holds is refused before
        // anything is removed (on Linux the removal's lease must be granted); the removal reviews it once more under its own
        // exclusion before the name goes.
        ArchiveFileObservation ReviewFile(EditDirectoryLease directory,string name,long maximum,out byte[] bytes)
        {
            byte[] read=[];
            var observed=Briefly(()=>
            {
                Check();
                using var file=directory.OpenDiscardFile(name,null,ct);
                var seen=directory.ObserveSnapshot(file,name,ct,maximum);
                read=name==CreationMarker?MarkerContent(file,seen):[];
                directory.RequireRemovable(file,name);
                return seen;
            });
            bytes=read;
            return observed;
        }
        void Remove(EditDirectoryLease directory,string name,ArchiveFileObservation observed)=>Briefly(()=>
        {
            Check();
            using var file=directory.OpenDiscardFile(name,observed.Version.Identity,ct);
            directory.DeleteSnapshot(file,name,observed,ct);
            return true;
        });
        // A program that holds a file for a moment (a scanner, another FileCat window's review) is waited for, briefly; each
        // attempt is exact on its own.
        T Briefly<T>(Func<T> step)
        {
            for(int attempt=0;;attempt++)
            {
                try{return step();}
                catch(IOException error)when(attempt<HeldRetries&&HeldForNow(error))
                {if(ct.WaitHandle.WaitOne(50))ct.ThrowIfCancellationRequested();}
            }
        }
    }

    // Up to three names, quoted, for a message.
    private static string Listing(string[] names)=>string.Join(", ",names.Take(3).Select(name=>"\u201c"+name+"\u201d"))+
        (names.Length>3?" and "+(names.Length-3)+" more":"");

    // The Finder's own folder settings (macOS writes one into a folder a user opens): no part of an edit. A folder holding
    // one is still listed as an unfinished copy; its removal names the file for the user to remove first, since FileCat
    // removes only what it can review as its own (a private file of this user).
    private static bool IsDesktopArtifact(string name)=>name==".DS_Store";

    private static bool Same(IReadOnlyList<string> now,string[] reviewed)=>
        now.Order(StringComparer.Ordinal).SequenceEqual(reviewed.Order(StringComparer.Ordinal),StringComparer.Ordinal);

    // The marker's bytes exactly as observed.
    private static byte[] MarkerContent(FileStream file,ArchiveFileObservation observed)
    {
        if(observed.Version.Length>MaxRecordBytes)throw new IOException("An interrupted copy's marker exceeds its budget.");
        byte[] content=new byte[checked((int)observed.Version.Length)];file.Position=0;file.ReadExactly(content);
        if(file.ReadByte()!=-1||Digest(content)!=observed.Version.Sha256)throw new IOException("An interrupted copy's marker changed during review.");
        return content;
    }

    internal const string CreationStagePrefix=".record-create-";

    // A record FileCat was publishing when it ended: a creation's own (".record-create-<guid>.tmp") or any other record's
    // (".record-<guid>.tmp", a revision's say).
    private static bool IsRecordStage(string name)=>IsCreationStage(name)||
        name.Length==44&&name.StartsWith(".record-",StringComparison.Ordinal)&&
        name.EndsWith(".tmp",StringComparison.Ordinal)&&Guid.TryParseExact(name.AsSpan(8,32),"N",out _);

    // A creation's own record stage: only this says that a copy was being published, so that a complete working copy beside
    // some other record's stage (a revision's, interrupted) is never taken for an unfinished copy.
    private static bool IsCreationStage(string name)=>name.Length==CreationStagePrefix.Length+36&&
        name.StartsWith(CreationStagePrefix,StringComparison.Ordinal)&&name.EndsWith(".tmp",StringComparison.Ordinal)&&
        Guid.TryParseExact(name.AsSpan(CreationStagePrefix.Length,32),"N",out _);

    // ".filecat-removing-<guid>": a name an exact removal set aside when FileCat ended (UnixExactRemoval).
    private static bool IsSetAside(string name)=>name.Length==50&&name.StartsWith(".filecat-removing-",StringComparison.Ordinal)&&
        Guid.TryParseExact(name.AsSpan(18,32),"N",out _);

    // The creation marker exactly as a creation writes it: this folder's id, the identities of the folders it was made in,
    // its admitted length, and a working file directly in its working folder. An earlier build's marker has neither. The
    // working path is read for its own two names, never compared with the store's path: the same store may be reached by
    // another spelling, or moved with the data folder, and its markers still speak for it.
    private static (string Leaf,string? SessionIdentity,string? FilesIdentity,long? Length) RequireCreationMarker(byte[] bytes,string id)
    {
        _=ArchiveWritePolicy.Utf8.GetCharCount(bytes);
        using var json=JsonDocument.Parse(bytes,new(){MaxDepth=4});var value=json.RootElement;
        if(value.ValueKind!=JsonValueKind.Object)throw new IOException("The interrupted copy's creation marker cannot be reviewed.");
        var fields=value.EnumerateObject().Select(p=>p.Name).Order(StringComparer.Ordinal).ToArray();
        bool current=fields.SequenceEqual(CreationMarkerFields.Order(StringComparer.Ordinal));
        if(!current&&!fields.SequenceEqual(EarlierCreationMarkerFields.Order(StringComparer.Ordinal))||
            value.GetProperty("Id").GetString()!=id||!value.GetProperty("Version").TryGetInt32(out int version)||version!=int.MaxValue||
            value.GetProperty("WorkingPath").GetString() is not { } working||
            Path.GetFileName(Path.GetDirectoryName(working))!="files")
            throw new IOException("The interrupted copy's creation marker cannot be reviewed.");
        string leaf=Path.GetFileName(working);EditDirectoryLease.ValidateLeaf(leaf);
        if(!current)return (leaf,null,null,null);
        if(value.GetProperty("SessionDirectoryIdentity").GetString() is not { Length:>0 } session||
            value.GetProperty("WorkingDirectoryIdentity").GetString() is not { Length:>0 } files||
            value.GetProperty("Length") is not { ValueKind:JsonValueKind.Number } admitted||!admitted.TryGetInt64(out long length)||
            length is <0 or >MaxMemberBytes)
            throw new IOException("The interrupted copy's creation marker cannot be reviewed.");
        return (leaf,session,files,length);
    }
}
