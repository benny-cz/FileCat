using System.Text.Json;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

/// <summary>Deletion facts for the explicitly reviewed edit; a partial discard never promises that every file remains.</summary>
public sealed record EditDiscardResult(bool Complete,bool MayHaveRemovedFiles,string? Issue=null);

public sealed partial class EditSessionStore
{
    /// <summary>Bind native discard to current bytes and fresh object ownership; legacy sessions retain their separate composition.</summary>
    public EditDiscardResult DiscardReviewed(EditSessionRecord record,EditWorkingReview reviewed,CancellationToken ct=default,Action? check=null)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();check?.Invoke();
        if(!record.IsSupported)throw new NotSupportedException("Unsupported edit-session directories are kept for recovery.");
        if(record.LegacyAdoption is not null)
            throw new NotSupportedException("An adopted edit and its old recovery folder need separate exact-object retirement review. Both are kept.");
        if(record.StorageRootIdentity.Length==0)
        {
            // Legacy deletion remains a pathname operation pending native adoption. Do not discard a
            // newer editor save or a replaced session from an earlier confirmation snapshot.
            void CheckLegacy(){ct.ThrowIfCancellationRequested();check?.Invoke();}
            CheckLegacy();
            // Only this store's own legacy records count: a record copied into the native root loads as unsupported
            // (it belongs to another storage root), and such a stray must not make this id ambiguous.
            var current=LoadAll().Where(row=>row.IsSupported&&row.Id==record.Id&&row.StorageRootIdentity.Length==0).Take(2).ToArray();
            if(current.Length!=1||current[0]!=record)
                throw new IOException("The old edit record changed. Reload it before discarding; its files are kept.");
            if(reviewed.State==EditState.Unavailable||ReviewWorking(record,new FileCat.Core.FileSystem.LocalFileSystemProvider(),CheckLegacy)!=reviewed)
                throw new IOException("The working copy changed after discard review. Review it again; its files are kept.");
            CheckLegacy();
            Discard(record);return new(true,true);
        }
        if(nativeOpen is null)throw new NotSupportedException("FileCat cannot safely discard this edit in the current session. Its files are kept.");
        // A commit of this edit is reading its captured copy in this process: no platform's file sharing shows that on macOS.
        if(HasLiveCapture(record.Id))throw new IOException("This edit is being committed. Discard it after that operation finishes. Its files are kept.");
        if(reviewed.State==EditState.Unavailable||reviewed.State!=EditState.Missing&&
            (reviewed.Sha256 is not {Length:64} digest||!digest.All(char.IsAsciiHexDigit)))
            throw new IOException("Review the complete working copy before discarding this edit. Its files are kept.");
        return DiscardNative(record,reviewed,null,ct,check);
    }

    /// <summary>Whether this store can remove a reviewed retained commit copy of a native edit (the restart recovery action).</summary>
    public bool SupportsRetainedCaptureRemoval=>nativeOpen is not null;

    /// <summary>
    /// The restart recovery action for one retained commit copy (a "commit-" folder that a stopped commit or cleanup left):
    /// after a fresh exact review of the edit's record history and of that copy (its records, its payload, or an
    /// interrupted copy's partial payload), removes only that copy, object by object, as a discard does. The edit, its
    /// working copy and every other retained copy stay. A copy that a saved copy still refers to, or that does not match
    /// its records, is kept for review.
    /// </summary>
    public EditDiscardResult RemoveRetainedCapture(EditSessionRecord record,string captureDirectory,CancellationToken ct=default,Action? check=null)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();check?.Invoke();
        if(nativeOpen is null||!record.IsSupported||record.StorageRootIdentity.Length==0)
            throw new NotSupportedException("This edit has no native retained copies that FileCat can remove. Its files are kept.");
        string full=Path.TrimEndingDirectorySeparator(Path.GetFullPath(captureDirectory)),name=Path.GetFileName(full);
        if(name.Length!=39||!name.StartsWith("commit-",StringComparison.Ordinal)||!Guid.TryParseExact(name.AsSpan(7),"N",out _)||
            Path.GetFileName(Path.GetDirectoryName(full))!=record.Id)
            throw new IOException("This folder is not a retained copy of this edit. Its files are kept.");
        if(HasLiveCapture(record.Id))
            throw new IOException("This edit is being committed. Remove its retained copies after that operation finishes. Their files are kept.");
        return DiscardNative(record,null,name,ct,check);
    }

    // A whole reviewed native edit (capture null), or only one of its retained commit copies.
    private EditDiscardResult DiscardNative(EditSessionRecord record,EditWorkingReview? reviewed,string? capture,CancellationToken ct,Action? check)
    {
        using var root=OpenNativeRoot(ct,false);
        if(root.Identity!=record.StorageRootIdentity)throw new IOException("The edit store was replaced. Its files are kept.");
        if(!root.CanDiscardReviewedSession)throw new NotSupportedException("FileCat cannot safely discard this edit on this system. Its files are kept.");
        using var admission=EditStorageAdmission.Acquire(root,ct);
        // Removing one retained copy needs removal authority only for that copy, which the session grants per child.
        using var session=capture is null?root.OpenDiscardDirectory(record.Id,record.SessionDirectoryIdentity,ct):
            root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);
        RequireStorage(record,root,session);
        bool attempted=false;int entries=0;long payloadBytes=0,recordBytes=0;
        void Check(){ct.ThrowIfCancellationRequested();check?.Invoke();admission.Review(ct);}
        try
        {
            Check();
            if(capture is null){var tree=ReviewSession();Check();Remove(session,tree);}
            else
            {
                var node=ReviewOneCapture(capture);Check();
                using var held=session.OpenDiscardDirectory(capture,node.Identity,ct);Remove(held,node);
            }
            return new(true,true);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or NotSupportedException or
            OperationCanceledException or JsonException or ArgumentException or InvalidOperationException)
        {
            if(error is OperationCanceledException&&!attempted)throw;
            string at=capture is null?session.Path:Path.Combine(session.Path,capture);
            string message=capture is null
                ?attempted?"Discard stopped after attempting to remove reviewed data. Remaining files are kept at "+at:
                    "The edit could not be discarded safely. Its files are kept at "+at
                :attempted?"Removing the retained copy stopped part way. Its remaining files are kept at "+at:
                    "The retained copy could not be removed safely. Its files are kept at "+at;
            return new(false,attempted,message+". "+error.Message);
        }

        DiscardNode ReviewSession()
        {
            var node=Node(session,record.Id);var names=node.Names;
            var expectedNames=new HashSet<string>(StringComparer.Ordinal){RecordFile,CreationMarker,"files"};
            var history=ReviewHistory(node,expectedNames);
            string[] captures=names.Where(name=>name.StartsWith("commit-",StringComparison.Ordinal)).ToArray();
            if(captures.Length>MaximumRecoveryCaptures)throw new IOException("The retained copies exceed the discard review budget.");
            foreach(string name in captures)
            {
                if(name.Length!=39||!Guid.TryParseExact(name.AsSpan(7),"N",out _))throw new IOException("An unknown captured folder is kept for review.");
                expectedNames.Add(name);using var capture=session.OpenStorageDirectory(name,ct);
                node.Children.Add(ReviewCaptureForDiscard(capture,name,history));
            }
            if(!names.Order(StringComparer.Ordinal).SequenceEqual(expectedNames.Order(StringComparer.Ordinal)))
                throw Unexpected("This edit's folder",names,expectedNames);
            // Captures go first; the user's reviewed working bytes remain until their preflight and removal finish.
            using(var files=session.OpenStorageDirectory("files",ct))
            {
                if(files.Identity!=record.WorkingDirectoryIdentity)throw new IOException("The working directory was replaced.");
                var working=Node(files,"files");string leaf=Path.GetFileName(record.WorkingPath);
                if(reviewed!.State==EditState.Missing)
                {
                    // The copy's name is gone. Anything left in its folder is either FileCat's own interrupted work (a
                    // set-aside name from a removal that stopped) or a file FileCat did not make; the shared message
                    // tells them apart and says what to do, where "replaced" said something untrue of both.
                    if(working.Names.Length!=0)throw Unexpected("The folder \u201cfiles\u201d",working.Names,[]);
                }
                else
                {
                    RequireLayout(working,[leaf]);var file=Read(files,leaf,false);
                    if(file.Expected.Version.Sha256!=reviewed.Sha256)throw new IOException("The working copy changed while discard was reviewed.");
                    working.Files.Add(file);
                }
                node.Children.Add(working);
            }
            RequireNames(session,node.Names);return node;
        }
        // The anchor, every revision and the creation marker, chained to the record the user reviewed. With a node (a
        // whole-edit discard) they are also removed; for one retained copy they only bind it to a state of this edit.
        Dictionary<string,EditSessionRecord> ReviewHistory(DiscardNode? node,HashSet<string> expectedNames)
        {
            // An adopted edit starts from a pending anchor; its later states follow the forward-only adoption transitions.
            var anchorFile=Read(session,RecordFile,true,removing:node is not null);var anchor=ParseRecord(anchorFile.Bytes!,session.Path,allowPendingAdoption:true);
            if(!(anchor.IsSupported||anchor.IsPendingAdoption)||anchor.Version!=EditSessionRecord.NativeStorageVersion||anchor.StorageRevision!=0||
                anchor.PreviousRecordSha256.Length!=0)throw new IOException("The initial edit record cannot be reviewed.");
            RequireRecord(anchor);
            var history=new Dictionary<string,EditSessionRecord>(StringComparer.Ordinal);
            string previous=Digest(anchorFile.Bytes!);history.Add(previous,anchor with{StoredRecordSha256=previous});
            node?.Files.Add(anchorFile);
            if(record.StorageRevision>4096)throw new IOException("The edit history exceeds the discard review budget.");
            var prior=anchor;
            for(long revision=1;revision<=record.StorageRevision;revision++)
            {
                Check();string name=RevisionName(revision);expectedNames.Add(name);var file=Read(session,name,true,removing:node is not null);
                var state=ParseRecord(file.Bytes!,session.Path,allowPendingAdoption:true);RequireRecord(state);
                if(state.StorageRevision!=revision||state.PreviousRecordSha256!=previous||!AdoptionTransition(prior,state))
                    throw new IOException("The edit history changed before discard.");
                prior=state;previous=Digest(file.Bytes!);history.Add(previous,state with{StoredRecordSha256=previous});node?.Files.Add(file);
            }
            if(previous!=record.StoredRecordSha256)throw new IOException("The edit state changed. Reload it before discarding.");
            var marker=Read(session,CreationMarker,true,removing:node is not null);RequireMarker(marker.Bytes!);node?.Files.Add(marker);
            return history;
        }
        DiscardNode ReviewOneCapture(string name)
        {
            var listed=Node(session,record.Id).Names;
            if(!listed.Contains(name,StringComparer.Ordinal))throw new IOException("The retained copy is no longer in this edit's folder.");
            // As for the read-only review: only the edit's current record speaks for it.
            if(listed.Contains(RevisionName(record.StorageRevision+1),StringComparer.Ordinal))
                throw new IOException("The edit record changed. Reload it before removing its retained copies.");
            var history=ReviewHistory(null,new HashSet<string>(StringComparer.Ordinal));
            using var held=session.OpenStorageDirectory(name,ct);
            var node=ReviewCaptureForDiscard(held,name,history);
            // A saved copy's intent is that copy's recovery evidence (an interrupted Save copy is resumed or reviewed from it).
            if(node.Names.Contains(EditSaveCopyIntent.FileName,StringComparer.Ordinal))
                throw new IOException("A saved copy refers to this retained copy and needs its own review first.");
            return node;
        }
        DiscardNode ReviewCaptureForDiscard(EditDirectoryLease capture,string name,Dictionary<string,EditSessionRecord> history)
        {
            var node=Node(capture,name);bool complete=node.Names.Contains("captured.json",StringComparer.Ordinal);
            bool linked=node.Names.Contains(EditSaveCopyIntent.FileName,StringComparer.Ordinal);
            RequireLayout(node,complete?linked?["capture.json","captured.json","files",EditSaveCopyIntent.FileName]:
                ["capture.json","captured.json","files"]:["capture.json","files"]);
            var header=Read(capture,"capture.json",true,MaximumCaptureRecordBytes);var start=ParseCaptureRecord(header.Bytes!,false);node.Files.Add(header);
            if(!history.TryGetValue(start.SessionRecord,out var originating))throw new IOException("A captured copy belongs to an unknown edit state.");
            RequireCaptureBinding(start,capture,originating);
            using var files=capture.OpenStorageDirectory("files",ct);
            if(files.Identity!=start.Files)throw new IOException("The captured file directory was replaced.");
            var data=Node(files,"files");
            if(data.Names.Length!=0)RequireLayout(data,[start.Leaf]);
            if(complete&&data.Names.Length!=1)throw new IOException("The captured payload is missing.");
            DiscardFile? payload=data.Names.Length==1?Read(files,start.Leaf,false):null;
            if(payload is not null)data.Files.Add(payload);
            if(complete)
            {
                var receipt=Read(capture,"captured.json",true,MaximumCaptureRecordBytes);node.Files.Add(receipt);var end=ParseCaptureRecord(receipt.Bytes!,true);
                RequireCaptureBinding(end,capture,originating);
                var observed=payload!.Expected;
                if(start.Files!=end.Files||observed.Version.Identity!=end.Identity||observed.Version.Length!=end.Length||
                    observed.Version.Sha256!=end.Sha256||observed.MetadataDigest!=end.Metadata||observed.Origin.Digest!=end.Origin)
                    throw new IOException("The retained copy differs from its complete capture record.");
                if(linked)
                {
                    var saved=Read(capture,EditSaveCopyIntent.FileName,true,EditSaveCopyIntent.MaximumBytes);node.Files.Add(saved);
                    EditSaveCopyIntent.Decode(saved.Bytes!).RequireSource(originating,capture.Identity,observed);
                }
            }
            node.Children.Add(data);RequireNames(capture,node.Names);return node;
        }
        void RequireRecord(EditSessionRecord value)
        {
            if(!(value.IsSupported||value.IsPendingAdoption)||value.Version!=EditSessionRecord.NativeStorageVersion||value.Id!=record.Id||value.Kind!=record.Kind||
                value.StorageRootIdentity!=root.Identity||value.SessionDirectoryIdentity!=session.Identity||
                value.WorkingDirectoryIdentity!=record.WorkingDirectoryIdentity||value.WorkingPath!=record.WorkingPath)
                throw new IOException("The edit record does not identify this native working namespace.");
        }
        void RequireMarker(byte[] bytes)
        {
            // The marker as this build writes it (the folders it was made in and its admitted length), or as an earlier
            // native build wrote it (its id, version and working path only).
            _=ArchiveWritePolicy.Utf8.GetCharCount(bytes);using var json=JsonDocument.Parse(bytes,new(){MaxDepth=4});var value=json.RootElement;
            if(value.ValueKind!=JsonValueKind.Object)throw new IOException("The initial native creation marker cannot be reviewed.");
            var fields=value.EnumerateObject().Select(p=>p.Name).Order(StringComparer.Ordinal).ToArray();
            bool current=fields.SequenceEqual(CreationMarkerFields.Order(StringComparer.Ordinal));
            if(!current&&!fields.SequenceEqual(EarlierCreationMarkerFields.Order(StringComparer.Ordinal))||
                value.GetProperty("Id").GetString()!=record.Id||value.GetProperty("WorkingPath").GetString()!=record.WorkingPath||
                !value.GetProperty("Version").TryGetInt32(out int version)||version!=int.MaxValue||
                current&&(value.GetProperty("SessionDirectoryIdentity").GetString()!=record.SessionDirectoryIdentity||
                    value.GetProperty("WorkingDirectoryIdentity").GetString()!=record.WorkingDirectoryIdentity||
                    value.GetProperty("Length") is not { ValueKind:JsonValueKind.Number } admitted||!admitted.TryGetInt64(out long length)||
                    length is <0 or >MaxMemberBytes))
                throw new IOException("The initial native creation marker cannot be reviewed.");
        }
        DiscardNode Node(EditDirectoryLease directory,string name)
        {
            Check();if(++entries>8192)throw new IOException("The edit namespace exceeds the discard entry budget.");
            return new(name,directory.Identity,directory.Names(ct,8192-entries).ToArray());
        }
        DiscardFile Read(EditDirectoryLease directory,string name,bool metadata,int maximum=MaxRecordBytes,bool removing=true)
        {
            Check();if(++entries>8192)throw new IOException("The edit namespace exceeds the discard entry budget.");
            using var file=directory.OpenFile(name,null,ct);
            long length=file.Length;
            if(length<0||metadata&&(length>maximum||length>32L*1024*1024-recordBytes)||
                !metadata&&length>8L*1024*1024*1024-payloadBytes)
                throw new IOException("The stored edit exceeds the discard byte budget.");
            if(metadata)recordBytes+=length;else payloadBytes+=length;
            var expected=directory.ObserveSnapshot(file,name,ct,metadata?maximum:MaxMemberBytes);
            if(metadata&&expected.Origin.State!=ArchiveOriginState.Absent)
                throw new IOException("An edit metadata record has unexpected origin data. Its files are kept.");
            byte[]? bytes=null;
            if(metadata)
            {
                bytes=new byte[checked((int)expected.Version.Length)];file.ReadExactly(bytes);
                if(file.ReadByte()!=-1||Digest(bytes)!=expected.Version.Sha256)throw new IOException("An edit record changed during review.");
            }
            // Only a file this action removes is probed for removal (another reader of a kept record is no obstacle).
            if(removing)directory.ReviewRemoval(file,name,expected,ct);Check();return new(name,expected,bytes);
        }
        void Remove(EditDirectoryLease directory,DiscardNode node)
        {
            Check();if(directory.Identity!=node.Identity)throw new IOException("The reviewed edit folder was replaced.");
            RequireNames(directory,node.Names);
            foreach(var child in node.Children)
            {
                using var held=directory.OpenDiscardDirectory(child.Name,child.Identity,ct);Remove(held,child);
            }
            foreach(var item in node.Files)
            {
                Check();using var file=directory.OpenDiscardFile(item.Name,item.Expected.Version.Identity,ct);
                directory.ReviewRemoval(file,item.Name,item.Expected,ct);Check();attempted=true;
                directory.DeleteSnapshot(file,item.Name,item.Expected,ct);
            }
            Check();attempted=true;directory.RemoveEmpty(ct);
        }
        static void RequireLayout(DiscardNode node,string[] names)
        {
            if(node.Names.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)))return;
            throw Unexpected("The folder \u201c"+node.Name+"\u201d",node.Names,names);
        }

        // What a folder holds that its discard did not expect, said as it is: a file FileCat left behind unfinished (a
        // record it was publishing, a name an interrupted removal set aside), a file FileCat never made, or one of its own
        // that is gone. Nothing is removed in any of those cases.
        static IOException Unexpected(string folder,IReadOnlyList<string> present,IReadOnlyCollection<string> expected)
        {
            var extra=present.Except(expected,StringComparer.Ordinal).ToArray();
            if(extra.Length==0)
                return new IOException(folder+" no longer holds "+Listing(expected.Except(present,StringComparer.Ordinal).ToArray())+
                    ", so FileCat cannot review what is left of it. Its files are kept.");
            var left=extra.Where(name=>IsRecordStage(name)||IsSetAside(name)).ToArray();
            var foreign=extra.Except(left,StringComparer.Ordinal).ToArray();
            string what=left.Length==0?Listing(foreign)+", which FileCat did not make"
                :foreign.Length==0?Listing(left)+", which FileCat left there when work on this edit was interrupted"
                :Listing(foreign)+", which FileCat did not make, and "+Listing(left)+", which it left there when work on this edit was interrupted";
            return new IOException(folder+" also holds "+what+". Its files are kept: remove "+(extra.Length==1?"it":"them")+
                " yourself if not needed, then try again.");
        }
        void RequireNames(EditDirectoryLease directory,string[] names)
        {if(!directory.Names(ct,8192).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)))throw new IOException("The reviewed edit folder changed. Unreviewed entries are kept.");}
    }
    private sealed record DiscardFile(string Name,ArchiveFileObservation Expected,byte[]? Bytes);
    private sealed record DiscardNode(string Name,string Identity,string[] Names)
    {
        internal List<DiscardFile> Files{get;}=[];
        internal List<DiscardNode> Children{get;}=[];
    }
}
