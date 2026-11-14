using System.Text.Json;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

public enum EditCaptureRecoveryState { Complete, Interrupted, NeedsReview }

/// <summary>A bounded read-only observation. Saved records and paths never authorize deletion or publication.</summary>
public sealed record EditCaptureRecovery(string Directory,EditCaptureRecoveryState State,string Reason,long? Length=null,string? Sha256=null)
{
    public EditSavedCopyRecovery? SavedCopy { get; init; }
    /// <summary>
    /// Whether FileCat's own unfinished work is beside this copy: the stage of a record it was publishing, or a name an
    /// interrupted removal set aside. The copy itself is still verified, and its removal takes those with it, but
    /// saving a verified copy out of it is refused while they are there (a stage may be a half-published save of its
    /// own), so that action is not offered for one.
    /// </summary>
    public bool Unfinished { get; init; }
}

public sealed partial class EditSessionStore
{
    internal const int MaximumRecoveryCaptures=1024;
    // A copy with no receipt: it never finished being verified, however much of its payload is there.
    private const string InterruptedReason="This copy was interrupted before verification. Its files are kept.";
    // A verified copy whose payload a stopped removal already took: nothing of the user's was ever in it (the payload
    // was FileCat's own copy of the member), and what is left are this copy's own records, which its removal takes.
    private const string PayloadGoneReason=
        "This copy's captured payload has already been removed, and what is left is FileCat's own. Its files are kept.";
    // A copy FileCat left without its own record: made and not recorded, or recorded and then half removed.
    private const string RecordGoneReason=
        "This copy's own record is gone, so nothing can be recovered from it, and what is left is FileCat's. Its files are kept.";
    // A copy a saved copy still refers to is that saved copy's own recovery evidence, and removing that one copy on its
    // own is refused while it is. The list offers a removal only for a verified or an interrupted copy, so saying
    // "interrupted" of one would offer an action that can only refuse; the whole edit's discard still removes it, with
    // the record that refers to it.
    private const string SavedCopyReason=
        "A saved copy refers to this retained copy and needs its own review first. Its files are kept.";
    private const int MaximumCaptureRecordBytes=16384;

    /// <summary>
    /// A cheap read-only startup hint. The names are only potential retained copies; the full
    /// receipt, payload and state-chain review still happens when the user opens one.
    /// </summary>
    public bool HasRetainedNativeCaptures(EditSessionRecord record,CancellationToken ct=default)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();
        if(nativeOpen is null||!record.IsSupported||record.StorageRootIdentity.Length==0)return false;
        using var root=OpenNativeRoot(ct,false);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);
        RequireStorage(record,root,session);
        return session.Names(ct).Any(name=>name.StartsWith("commit-",StringComparison.Ordinal));
    }

    /// <summary>Inspect retained native commit copies after restart without modifying any working or captured file.</summary>
    public IReadOnlyList<EditCaptureRecovery> ReviewRetainedCaptures(EditSessionRecord record,CancellationToken ct=default)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();
        if(nativeOpen is null||record.StorageRootIdentity.Length==0||!record.IsSupported)
            throw new NotSupportedException("This edit does not have native capture recovery support. Its files are kept.");
        using var root=OpenNativeRoot(ct,false);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);RequireStorage(record,root,session);
        byte[] anchorBytes=ReadRecordBytes(session,RecordFile,ct);
        var anchor=ParseRecord(anchorBytes,session.Path,allowPendingAdoption:true);
        if(!anchor.IsSupported&&!anchor.IsPendingAdoption)throw new IOException("The edit record cannot be reviewed. Its captured files are kept.");
        var history=new Dictionary<string,EditSessionRecord>(StringComparer.Ordinal);
        var current=ReadNativeLatest(anchor,anchorBytes,root,session,ct,history);
        if(!current.IsSupported||string.IsNullOrEmpty(record.StoredRecordSha256)||current.StoredRecordSha256!=record.StoredRecordSha256)
            throw new IOException("The edit record changed. Reload it before reviewing its captured files.");
        var names=session.Names(ct).Where(name=>name.StartsWith("commit-",StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        if(names.Length>MaximumRecoveryCaptures)
            return [new(session.Path,EditCaptureRecoveryState.NeedsReview,"This edit has too many retained copies to review at once. All files are kept.")];
        var result=new List<EditCaptureRecovery>(names.Length);long remainingBytes=EditSnapshotAdmission.MaximumActiveBytes;
        foreach(string name in names)
        {
            ct.ThrowIfCancellationRequested();string path=Path.Combine(session.Path,name);
            if(name.Length!=39||!Guid.TryParseExact(name.AsSpan(7),"N",out _))
            {result.Add(new(path,EditCaptureRecoveryState.NeedsReview,"This captured folder has an unknown name. Its files are kept."));continue;}
            try{result.Add(ReviewCapture(session,name,history,ref remainingBytes,ct));}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
            {result.Add(new(path,EditCaptureRecoveryState.NeedsReview,"This retained copy is in use, changed, or cannot be read completely. Its files are kept."));}
        }
        session.Review(ct);
        if(!ReadRecordBytes(session,RecordFile,ct).AsSpan().SequenceEqual(anchorBytes)||
            ReadNativeLatest(anchor,anchorBytes,root,session,ct).StoredRecordSha256!=current.StoredRecordSha256)
            throw new IOException("The edit changed during capture recovery review. Reload it before continuing.");
        return result;
    }

    private EditCaptureRecovery ReviewCapture(EditDirectoryLease session,string name,
        IReadOnlyDictionary<string,EditSessionRecord> history,ref long remainingBytes,CancellationToken ct)
    {
        using var capture=session.OpenDirectory(name,null,ct);
        var names=capture.Names(ct).Order(StringComparer.Ordinal).ToArray();
        bool linked=names.Contains(EditSaveCopyIntent.FileName,StringComparer.Ordinal);
        // What FileCat may have left inside one retained copy, which its discard reviews and removes with it: the stage
        // of a record this commit was publishing, and a name an interrupted exact removal set aside. The payload's own
        // folder may be gone as well, because a removal takes it before this copy's records.
        string[] left=names.Where(item=>IsRecordStage(item)||IsSetAside(item)).ToArray();
        // More of them than one removal reviews: the removal says so itself, and this says the same rather than
        // offering a removal that would refuse.
        if(left.Length>MaximumInterruptedLeftovers)
            throw new IOException("This retained copy holds more files FileCat left behind unfinished than one review reads.");
        string[] core=names.Except(left,StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        bool stored=core.Contains("files",StringComparer.Ordinal);
        // A copy whose own record is gone holds nothing that can be bound to a state of this edit, so there is nothing
        // to recover from it; the discard reviews it as FileCat's own and removes it with the edit.
        if(!core.Contains("capture.json",StringComparer.Ordinal))
        {
            var allowed=new List<string>();
            if(core.Contains("captured.json",StringComparer.Ordinal))allowed.Add("captured.json");
            if(linked)allowed.Add(EditSaveCopyIntent.FileName);
            if(stored)allowed.Add("files");
            if(!core.SequenceEqual(allowed.Order(StringComparer.Ordinal).ToArray()))throw new IOException("Unknown capture entries.");
            // Read by the same rule the removal reads it by, so this never calls removable a folder whose removal
            // refuses: only one of this copy's own records names its payload, and a removal takes that payload before
            // either record, so with no record of its own the folder holds the receipt's name or nothing but what an
            // interrupted removal set aside. Anything else is unknown content, kept by both.
            string? named=null;
            if(core.Contains("captured.json",StringComparer.Ordinal))
            {
                using var recorded=new CaptureRecord(capture,"captured.json",ct);
                named=ParseCaptureRecord(recorded.Bytes,true).Leaf;
            }
            if(stored)
            {
                using var stage=capture.OpenDirectory("files",null,ct);
                var held=stage.Names(ct);
                string[] rest=held.Where(item=>!IsSetAside(item)).ToArray();
                if(rest.Length>1||rest.Length==1&&rest[0]!=named)throw new IOException("Unknown captured files.");
                stage.Review(ct);
            }
            capture.Review(ct);
            if(!capture.Names(ct).Order(StringComparer.Ordinal).SequenceEqual(names))
                throw new IOException("The interrupted copy changed during review.");
            return linked?new(capture.Path,EditCaptureRecoveryState.NeedsReview,SavedCopyReason)
                :new(capture.Path,EditCaptureRecoveryState.Interrupted,RecordGoneReason);
        }
        string[] completeNames=linked?["capture.json","captured.json","files",EditSaveCopyIntent.FileName]:["capture.json","captured.json","files"];
        bool complete=core.SequenceEqual(completeNames.Order(StringComparer.Ordinal));
        if(!complete)
        {
            var allowed=new List<string>{"capture.json"};
            if(core.Contains("captured.json",StringComparer.Ordinal))allowed.Add("captured.json");
            if(linked)allowed.Add(EditSaveCopyIntent.FileName);
            if(stored)allowed.Add("files");
            if(!core.SequenceEqual(allowed.Order(StringComparer.Ordinal).ToArray()))throw new IOException("Unknown capture entries.");
        }
        using var header=new CaptureRecord(capture,"capture.json",ct);
        var start=ParseCaptureRecord(header.Bytes,false);
        if(!history.TryGetValue(start.SessionRecord,out var record))
            throw new IOException("The capture does not match a reviewed edit state.");
        RequireCaptureBinding(start,capture,record);
        // What an unfinished copy is reported as, decided in one place: a copy a saved copy refers to needs that
        // copy's own review, and the list offers no removal for one that needs review.
        EditCaptureRecovery Left(string reason)=>linked
            ?new(capture.Path,EditCaptureRecoveryState.NeedsReview,SavedCopyReason)
            :new(capture.Path,EditCaptureRecoveryState.Interrupted,reason);
        if(!stored)
        {
            // A removal stopped once the payload and its folder were gone: what is left is this copy's own records. A
            // complete layout always names that folder, so what tells the two apart here is the receipt alone.
            header.Review(ct);capture.Review(ct);
            if(!capture.Names(ct).Order(StringComparer.Ordinal).SequenceEqual(names))
                throw new IOException("The interrupted copy changed during review.");
            return Left(core.Contains("captured.json",StringComparer.Ordinal)?PayloadGoneReason:InterruptedReason);
        }
        using var files=capture.OpenDirectory("files",start.Files,ct);
        var fileNames=files.Names(ct);
        string[] asideFiles=fileNames.Where(IsSetAside).ToArray();
        string[] payloadNames=fileNames.Except(asideFiles,StringComparer.Ordinal).ToArray();
        if(payloadNames.Length>1||payloadNames.Length==1&&payloadNames[0]!=start.Leaf)throw new IOException("Unknown captured files.");
        if(!complete||payloadNames.Length!=1)
        {
            header.Review(ct);files.Review(ct);capture.Review(ct);
            if(!capture.Names(ct).Order(StringComparer.Ordinal).SequenceEqual(names)||!files.Names(ct).SequenceEqual(fileNames))
                throw new IOException("The interrupted copy changed during review.");
            // A copy with no receipt never finished, however much of it is there; one whose receipt is there but whose
            // payload is not had that payload removed.
            return Left(complete?PayloadGoneReason:InterruptedReason);
        }
        using var receipt=new CaptureRecord(capture,"captured.json",ct);
        var end=ParseCaptureRecord(receipt.Bytes,true);RequireCaptureBinding(end,capture,record);
        if(start.Session!=end.Session||start.SessionRecord!=end.SessionRecord||start.Directory!=end.Directory||start.Files!=end.Files||start.Leaf!=end.Leaf)
            throw new IOException("The capture records do not agree.");
        using var payload=files.OpenFile(start.Leaf,end.Identity,ct);
        long cost=checked(payload.Length*(linked?8:2)+(linked?16L*EditSaveCopyIntent.MaximumBytes:0));
        if(cost>remainingBytes)return new(capture.Path,EditCaptureRecoveryState.NeedsReview,
            "This copy needs a separate review because the byte limit for this batch has been reached. Its files are kept.");
        remainingBytes-=cost;
        var observed=files.ObserveSnapshot(payload,start.Leaf,ct);
        if(observed.Version.Length!=end.Length||observed.Version.Sha256!=end.Sha256||observed.MetadataDigest!=end.Metadata||observed.Origin.Digest!=end.Origin)
            throw new IOException("The capture no longer matches its complete receipt.");
        EditSavedCopyRecovery? saved=null;
        if(linked)
        {
            using var intentRecord=new CaptureRecord(capture,EditSaveCopyIntent.FileName,ct,EditSaveCopyIntent.MaximumBytes);
            var intent=EditSaveCopyIntent.Decode(intentRecord.Bytes);intent.RequireSource(record,capture.Identity,observed);
            saved=ReviewSavedCopy(intent,ct);intentRecord.Review(ct);
        }
        header.Review(ct);receipt.Review(ct);files.Review(ct);capture.Review(ct);
        if(!capture.Names(ct).Order(StringComparer.Ordinal).SequenceEqual(names)||!files.Names(ct).SequenceEqual(fileNames)||
            !files.ObserveSnapshot(payload,start.Leaf,ct).Matches(observed))throw new IOException("The retained copy changed during review.");
        bool unfinished=left.Length!=0||asideFiles.Length!=0;
        return new(capture.Path,EditCaptureRecoveryState.Complete,"The complete retained copy matches its verification record. Its files are kept."+
            (unfinished?" FileCat also left unfinished work of its own beside it, which goes with it when it is removed.":""),end.Length,end.Sha256)
            {SavedCopy=saved,Unfinished=unfinished};
    }

    private static void RequireCaptureBinding(CaptureFacts facts,EditDirectoryLease capture,EditSessionRecord record)
    {
        if(facts.Session!=record.Id||facts.SessionRecord!=record.StoredRecordSha256||facts.Directory!=capture.Identity||
            facts.Leaf!=Path.GetFileName(record.WorkingPath))throw new IOException("The capture belongs to a different edit or state.");
    }

    private sealed record CaptureFacts(string Session,string SessionRecord,string Directory,string Files,string Leaf,
        string? Identity=null,long? Length=null,string? Sha256=null,string? Metadata=null,string? Origin=null,long? Admitted=null);

    private static CaptureFacts ParseCaptureRecord(byte[] bytes,bool complete)
    {
        _=ArchiveWritePolicy.Utf8.GetCharCount(bytes);
        using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=4});var value=document.RootElement;
        // A capture's own record as this build writes it (with the length its copy was admitted to be, which the
        // storage accounting charges while the copy is made without the gate), or as an earlier build wrote it.
        string[] expected=complete?["Version","Session","SessionRecord","Directory","Files","Leaf","Identity","Length","Sha256","Metadata","Origin"]:
            ["Version","Session","SessionRecord","Directory","Files","Leaf","Admitted"];
        string[] earlier=["Version","Session","SessionRecord","Directory","Files","Leaf"];
        if(value.ValueKind!=JsonValueKind.Object)throw new IOException("Unknown capture record.");
        var names=value.EnumerateObject().Select(property=>property.Name).Order(StringComparer.Ordinal).ToArray();
        bool admittedRecorded=!complete&&names.SequenceEqual(expected.Order(StringComparer.Ordinal));
        if(!(admittedRecorded||names.SequenceEqual((complete?expected:earlier).Order(StringComparer.Ordinal)))||
            value.GetProperty("Version").ValueKind!=JsonValueKind.Number||!value.GetProperty("Version").TryGetInt32(out int version)||
            version!=(admittedRecorded?2:1))throw new IOException("Unknown capture record.");
        string Text(string key,int maximum=1024)
        {
            var item=value.GetProperty(key);
            if(item.ValueKind!=JsonValueKind.String||item.GetString() is not {Length:>0} text||text.Length>maximum||text.Any(char.IsControl))
                throw new IOException("Invalid captured text.");
            return text;
        }
        string DigestText(string key)
        {
            string text=Text(key,64);if(text.Length!=64||text.Any(c=>c is not(>='0' and <='9') and not(>='A' and <='F')))
                throw new IOException("Invalid captured digest.");return text;
        }
        string leaf=Text("Leaf",255);EditDirectoryLease.ValidateLeaf(leaf);
        var facts=new CaptureFacts(Text("Session",32),DigestText("SessionRecord"),Text("Directory"),Text("Files"),leaf);
        if(!complete)
        {
            if(!admittedRecorded)return facts;
            if(value.GetProperty("Admitted").ValueKind!=JsonValueKind.Number||!value.GetProperty("Admitted").TryGetInt64(out long room)||
                room<0||room>MaxMemberBytes)throw new IOException("Invalid captured length.");
            return facts with{Admitted=room};
        }
        if(value.GetProperty("Length").ValueKind!=JsonValueKind.Number||!value.GetProperty("Length").TryGetInt64(out long length)||length<0||length>MaxMemberBytes)throw new IOException("Invalid captured length.");
        return facts with{Identity=Text("Identity"),Length=length,Sha256=DigestText("Sha256"),Metadata=DigestText("Metadata"),Origin=DigestText("Origin")};
    }

    private sealed class CaptureRecord:IDisposable
    {
        private readonly EditDirectoryLease directory;
        private readonly FileStream file;
        private readonly string name;
        private readonly ArchiveFileObservation expected;
        private readonly int maximum;
        internal byte[] Bytes{get;}
        internal CaptureRecord(EditDirectoryLease directory,string name,CancellationToken ct,int maximum=MaximumCaptureRecordBytes)
        {
            if(maximum<=0||maximum>MaxRecordBytes)throw new ArgumentOutOfRangeException(nameof(maximum));
            this.directory=directory;this.name=name;this.maximum=maximum;file=directory.OpenFile(name,null,ct);
            try
            {
                if(file.Length>maximum)throw new IOException("The capture record exceeds its review budget.");
                expected=directory.ObserveSnapshot(file,name,ct,maximum);Bytes=new byte[checked((int)expected.Version.Length)];file.ReadExactly(Bytes);
                if(file.ReadByte()!=-1)throw new IOException("The capture record grew during review.");Review(ct);
            }
            catch{file.Dispose();throw;}
        }
        internal void Review(CancellationToken ct)
        {if(!directory.ObserveSnapshot(file,name,ct,maximum).Matches(expected))throw new IOException("The capture record changed during review.");}
        public void Dispose()=>file.Dispose();
    }
}
