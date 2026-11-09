using System.Text.Json;
using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

public enum EditCaptureRecoveryState { Complete, Interrupted, NeedsReview }

/// <summary>A bounded read-only observation. Saved records and paths never authorize deletion or publication.</summary>
public sealed record EditCaptureRecovery(string Directory,EditCaptureRecoveryState State,string Reason,long? Length=null,string? Sha256=null)
{public EditSavedCopyRecovery? SavedCopy { get; init; }}

public sealed partial class EditSessionStore
{
    internal const int MaximumRecoveryCaptures=1024;
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
        string[] completeNames=linked?["capture.json","captured.json","files",EditSaveCopyIntent.FileName]:["capture.json","captured.json","files"];
        bool complete=names.SequenceEqual(completeNames.Order(StringComparer.Ordinal));
        if(!complete&&!names.SequenceEqual(new[]{"capture.json","files"}))throw new IOException("Unknown capture entries.");
        using var header=new CaptureRecord(capture,"capture.json",ct);
        var start=ParseCaptureRecord(header.Bytes,false);
        if(!history.TryGetValue(start.SessionRecord,out var record))
            throw new IOException("The capture does not match a reviewed edit state.");
        RequireCaptureBinding(start,capture,record);
        using var files=capture.OpenDirectory("files",start.Files,ct);
        var fileNames=files.Names(ct);
        if(fileNames.Count>1||fileNames.Count==1&&fileNames[0]!=start.Leaf)throw new IOException("Unknown captured files.");
        if(!complete)
        {
            header.Review(ct);files.Review(ct);capture.Review(ct);
            if(!capture.Names(ct).Order(StringComparer.Ordinal).SequenceEqual(names)||!files.Names(ct).SequenceEqual(fileNames))
                throw new IOException("The interrupted copy changed during review.");
            return new(capture.Path,EditCaptureRecoveryState.Interrupted,"This copy was interrupted before verification. Its files are kept.");
        }
        if(fileNames.Count!=1)throw new IOException("Missing captured payload.");
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
        return new(capture.Path,EditCaptureRecoveryState.Complete,"The complete retained copy matches its verification record. Its files are kept.",end.Length,end.Sha256)
            {SavedCopy=saved};
    }

    private static void RequireCaptureBinding(CaptureFacts facts,EditDirectoryLease capture,EditSessionRecord record)
    {
        if(facts.Session!=record.Id||facts.SessionRecord!=record.StoredRecordSha256||facts.Directory!=capture.Identity||
            facts.Leaf!=Path.GetFileName(record.WorkingPath))throw new IOException("The capture belongs to a different edit or state.");
    }

    private sealed record CaptureFacts(string Session,string SessionRecord,string Directory,string Files,string Leaf,
        string? Identity=null,long? Length=null,string? Sha256=null,string? Metadata=null,string? Origin=null);

    private static CaptureFacts ParseCaptureRecord(byte[] bytes,bool complete)
    {
        _=ArchiveWritePolicy.Utf8.GetCharCount(bytes);
        using var document=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=4});var value=document.RootElement;
        string[] expected=complete?["Version","Session","SessionRecord","Directory","Files","Leaf","Identity","Length","Sha256","Metadata","Origin"]:
            ["Version","Session","SessionRecord","Directory","Files","Leaf"];
        if(value.ValueKind!=JsonValueKind.Object)throw new IOException("Unknown capture record.");
        var names=value.EnumerateObject().Select(property=>property.Name).ToArray();
        if(!names.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal))||
            value.GetProperty("Version").ValueKind!=JsonValueKind.Number||!value.GetProperty("Version").TryGetInt32(out int version)||version!=1)throw new IOException("Unknown capture record.");
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
        if(!complete)return facts;
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
