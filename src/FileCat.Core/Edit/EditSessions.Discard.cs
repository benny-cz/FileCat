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
            // The existing legacy composition remains separate until explicit native adoption is qualified.
            Discard(record);return new(true,true);
        }
        if(nativeOpen is null)throw new NotSupportedException("FileCat cannot safely discard this edit in the current session. Its files are kept.");
        if(reviewed.State==EditState.Unavailable||reviewed.State!=EditState.Missing&&
            (reviewed.Sha256 is not {Length:64} digest||!digest.All(char.IsAsciiHexDigit)))
            throw new IOException("Review the complete working copy before discarding this edit. Its files are kept.");
        using var root=OpenNativeRoot(ct,false);
        if(root.Identity!=record.StorageRootIdentity)throw new IOException("The edit store was replaced. Its files are kept.");
        if(!root.CanDiscardReviewedSession)throw new NotSupportedException("FileCat cannot safely discard this edit on this system. Its files are kept.");
        using var admission=EditStorageAdmission.Acquire(root,ct);
        using var session=root.OpenDiscardDirectory(record.Id,record.SessionDirectoryIdentity,ct);
        RequireStorage(record,root,session);
        bool attempted=false;int entries=0;long payloadBytes=0,recordBytes=0;
        void Check(){ct.ThrowIfCancellationRequested();check?.Invoke();admission.Review(ct);}
        try
        {
            Check();var tree=ReviewSession();Check();
            Remove(session,tree);return new(true,true);
        }
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or NotSupportedException or
            OperationCanceledException or JsonException or ArgumentException or InvalidOperationException)
        {
            if(error is OperationCanceledException&&!attempted)throw;
            string message=attempted?"Discard stopped after attempting to remove reviewed data. Remaining files are kept at "+session.Path:
                "The edit could not be discarded safely. Its files are kept at "+session.Path;
            return new(false,attempted,message+". "+error.Message);
        }

        DiscardNode ReviewSession()
        {
            var node=Node(session,record.Id);var names=node.Names;
            var anchorFile=Read(session,RecordFile,true);var anchor=ParseRecord(anchorFile.Bytes!,session.Path);
            if(!anchor.IsSupported||anchor.Version!=EditSessionRecord.NativeStorageVersion||anchor.StorageRevision!=0||
                anchor.PreviousRecordSha256.Length!=0)throw new IOException("The initial edit record cannot be reviewed.");
            RequireRecord(anchor);
            var history=new Dictionary<string,EditSessionRecord>(StringComparer.Ordinal);
            string previous=Digest(anchorFile.Bytes!);history.Add(previous,anchor with{StoredRecordSha256=previous});
            node.Files.Add(anchorFile);
            if(record.StorageRevision>4096)throw new IOException("The edit history exceeds the discard review budget.");
            var expectedNames=new HashSet<string>(StringComparer.Ordinal){RecordFile,CreationMarker,"files"};
            for(long revision=1;revision<=record.StorageRevision;revision++)
            {
                Check();string name=RevisionName(revision);expectedNames.Add(name);var file=Read(session,name,true);
                var state=ParseRecord(file.Bytes!,session.Path);RequireRecord(state);
                if(state.StorageRevision!=revision||state.PreviousRecordSha256!=previous)
                    throw new IOException("The edit history changed before discard.");
                previous=Digest(file.Bytes!);history.Add(previous,state with{StoredRecordSha256=previous});node.Files.Add(file);
            }
            if(previous!=record.StoredRecordSha256)throw new IOException("The edit state changed. Reload it before discarding.");
            var marker=Read(session,CreationMarker,true);RequireMarker(marker.Bytes!);node.Files.Add(marker);
            string[] captures=names.Where(name=>name.StartsWith("commit-",StringComparison.Ordinal)).ToArray();
            if(captures.Length>MaximumRecoveryCaptures)throw new IOException("The retained copies exceed the discard review budget.");
            foreach(string name in captures)
            {
                if(name.Length!=39||!Guid.TryParseExact(name.AsSpan(7),"N",out _))throw new IOException("An unknown captured folder is kept for review.");
                expectedNames.Add(name);using var capture=session.OpenStorageDirectory(name,ct);
                node.Children.Add(ReviewCaptureForDiscard(capture,name,history));
            }
            if(!names.Order(StringComparer.Ordinal).SequenceEqual(expectedNames.Order(StringComparer.Ordinal)))
                throw new IOException("The edit folder contains unexpected entries.");
            // Captures go first; the user's reviewed working bytes remain until their preflight and removal finish.
            using(var files=session.OpenStorageDirectory("files",ct))
            {
                if(files.Identity!=record.WorkingDirectoryIdentity)throw new IOException("The working directory was replaced.");
                var working=Node(files,"files");string leaf=Path.GetFileName(record.WorkingPath);
                if(reviewed.State==EditState.Missing)
                {if(working.Names.Length!=0)throw new IOException("The missing working copy was replaced while discard was reviewed.");}
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
            if(!value.IsSupported||value.Version!=EditSessionRecord.NativeStorageVersion||value.Id!=record.Id||value.Kind!=record.Kind||
                value.StorageRootIdentity!=root.Identity||value.SessionDirectoryIdentity!=session.Identity||
                value.WorkingDirectoryIdentity!=record.WorkingDirectoryIdentity||value.WorkingPath!=record.WorkingPath)
                throw new IOException("The edit record does not identify this native working namespace.");
        }
        void RequireMarker(byte[] bytes)
        {
            _=ArchiveWritePolicy.Utf8.GetCharCount(bytes);using var json=JsonDocument.Parse(bytes,new(){MaxDepth=4});var value=json.RootElement;
            if(value.ValueKind!=JsonValueKind.Object||!value.EnumerateObject().Select(p=>p.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[]{"Id","Version","WorkingPath"}.Order(StringComparer.Ordinal))||
                value.GetProperty("Id").GetString()!=record.Id||value.GetProperty("WorkingPath").GetString()!=record.WorkingPath||
                !value.GetProperty("Version").TryGetInt32(out int version)||version!=int.MaxValue)
                throw new IOException("The initial native creation marker cannot be reviewed.");
        }
        DiscardNode Node(EditDirectoryLease directory,string name)
        {
            Check();if(++entries>8192)throw new IOException("The edit namespace exceeds the discard entry budget.");
            return new(name,directory.Identity,directory.Names(ct,8192-entries).ToArray());
        }
        DiscardFile Read(EditDirectoryLease directory,string name,bool metadata,int maximum=MaxRecordBytes)
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
            directory.ReviewRemoval(file,name,expected,ct);Check();return new(name,expected,bytes);
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
        {if(!node.Names.Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal)))throw new IOException("An edit folder contains unexpected entries.");}
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
