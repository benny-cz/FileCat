using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Core.Edit;

/// <summary>Path-free, read-only evidence binding a native copy to the retained old edit.</summary>
public sealed record EditLegacyAdoption
{
    public int SourceVersion { get; init; }
    public string SourceId { get; init; } = "";
    public string RootIdentity { get; init; } = "";
    public string SessionIdentity { get; init; } = "";
    public string FilesIdentity { get; init; } = "";
    public string RecordIdentity { get; init; } = "";
    public string RecordSha256 { get; init; } = "";
    public string WorkingIdentity { get; init; } = "";
    public long WorkingLength { get; init; }
    public string WorkingSha256 { get; init; } = "";
    public string OriginDigest { get; init; } = "";

    internal bool IsValid => SourceVersion is 1 or EditSessionRecord.CurrentVersion &&
        Guid.TryParseExact(SourceId,"N",out _) &&
        new[]{RootIdentity,SessionIdentity,FilesIdentity,RecordIdentity,WorkingIdentity}.All(Identity) &&
        new[]{RecordSha256,WorkingSha256,OriginDigest}.All(Digest) &&
        WorkingLength is >=0 and <=EditSessionStore.MaxMemberBytes;
    private static bool Identity(string value)=>value is { Length: >0 and <=256 } &&
        value.All(c=>c is >= '!' and <= '~');
    private static bool Digest(string value)=>value is { Length:64 } &&
        value.All(c=>c is >= '0' and <= '9' or >= 'A' and <= 'F');
}

/// <summary>
/// One external edit of an archive member or a file on a server (plan §14.2, NET-003): its origin and version
/// evidence, a private working copy, and what was last written back. It survives navigation, tab closing, and
/// restarts; only an explicit Commit writes back and only Discard deletes the working copy.
/// </summary>
public sealed record EditSessionRecord
{
    public const int CurrentVersion = 2;
    public const int NativeStorageVersion = 3;
    public const string ArchiveKind = "archive";
    public const string RemoteKind = "sftp";
    public int Version { get; init; } = CurrentVersion;
    public string Id { get; init; } = "";
    /// <summary><see cref="ArchiveKind"/> (the default of records written before servers had sessions) or <see cref="RemoteKind"/>.</summary>
    public string Kind { get; init; } = ArchiveKind;
    public DateTime CreatedUtc { get; init; }
    public string ArchivePath { get; init; } = "";
    public ArchiveWriteFormat ArchiveFormat { get; init; } = ArchiveWriteFormat.Zip;
    public ArchiveWriteProfile? ArchiveProfile { get; init; }
    public string ArchiveIdentity { get; init; } = "";
    public string ArchiveParentIdentity { get; init; } = "";
    public string ArchiveMetadataDigest { get; init; } = "";
    public string MemberCatalogRevision { get; init; } = "";
    public int MemberOccurrence { get; init; }
    public string MemberMetadataSha256 { get; init; } = "";
    public string WorkingIdentity { get; init; } = "";
    public string WorkingDirectoryIdentity { get; init; } = "";
    public string StorageRootIdentity { get; init; } = "";
    public string SessionDirectoryIdentity { get; init; } = "";
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] public EditLegacyAdoption? LegacyAdoption { get; init; }
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public bool LegacyAdoptionReady { get; init; }
    /// <summary>
    /// The user chose to retire the old folder of this ready adopted copy: from this revision on, this copy is the edit and
    /// the old folder's reviewed objects may be removed (UNIX_PRIVATE_CLEANUP_DECISION.md). A later revision drops the
    /// relationship once the old folder is gone.
    /// </summary>
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)] public bool LegacyRetiring { get; init; }
    public long StorageRevision { get; init; }
    public string PreviousRecordSha256 { get; init; } = "";
    [JsonIgnore] internal string StoredRecordSha256 { get; init; } = "";
    [JsonIgnore] public string? UnsupportedReason { get; init; }
    // A ZIP session recorded with a worker profile (a composed ZIP route) is native; every ZIP session without one keeps the
    // route it was made with.
    [JsonIgnore] public bool IsLegacyZip => !IsRemote && (Version == 1 || ArchiveFormat == ArchiveWriteFormat.Zip && ArchiveProfile is null);
    [JsonIgnore] public bool IsNativeArchive => !IsRemote && !IsLegacyZip;
    [JsonIgnore] public bool IsSupported => HasValidShape && (LegacyAdoption is null || LegacyAdoptionReady);
    [JsonIgnore] internal bool IsPendingAdoption => HasValidShape && LegacyAdoption is not null && !LegacyAdoptionReady;
    [JsonIgnore] private bool HasValidShape => UnsupportedReason is null && Version is 1 or CurrentVersion or NativeStorageVersion &&
        ArchivePath is not null && MemberPath is not null && WorkingPath is not null && BaseSha256 is not null && Baseline is not null &&
        ProfileId is not null && ServerDisplay is not null && RemotePath is not null &&
        StorageRootIdentity is not null && SessionDirectoryIdentity is not null && PreviousRecordSha256 is not null && StorageRevision>=0 &&
        (Version!=NativeStorageVersion||!string.IsNullOrEmpty(StorageRootIdentity)&&!string.IsNullOrEmpty(SessionDirectoryIdentity)&&
            !string.IsNullOrEmpty(WorkingDirectoryIdentity)&&!string.IsNullOrEmpty(WorkingIdentity))&&
        (LegacyAdoption is null && !LegacyAdoptionReady || Version==NativeStorageVersion&&LegacyAdoption is { IsValid:true }&&LegacyAdoption.SourceId!=Id) &&
        (!LegacyRetiring || LegacyAdoption is not null && LegacyAdoptionReady) &&
        Kind is ArchiveKind or RemoteKind && (!IsNativeArchive ||
        (ArchiveFormat is ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.TarBzip2 or ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Rar5 or ArchiveWriteFormat.SevenZip or ArchiveWriteFormat.Zip ||
            ArchiveStandalone.IsStandalone(ArchiveFormat) && MemberPath == ArchiveStandalone.MemberName) &&
        ArchiveProfile is not null && ArchiveProfile.Format == ArchiveFormat && MemberOccurrence == 0 &&
        !string.IsNullOrEmpty(ArchiveIdentity) && !string.IsNullOrEmpty(ArchiveParentIdentity) && !string.IsNullOrEmpty(WorkingIdentity) && !string.IsNullOrEmpty(WorkingDirectoryIdentity) &&
        IsDigest(ArchiveMetadataDigest) && IsDigest(MemberCatalogRevision) && IsDigest(MemberMetadataSha256) && IsDigest(BaseSha256) &&
        MemberLength is >= 0 and <= EditSessionStore.MaxMemberBytes);
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');
    /// <summary>For a server file: the connection profile, its display ("user@host"), and the absolute remote path.</summary>
    public string ProfileId { get; init; } = "";
    public string ServerDisplay { get; init; } = "";
    public string RemotePath { get; init; } = "";
    /// <summary>For a server file: the revision the working copy is based on (updated after each commit).</summary>
    public ContentRevision RemoteBaseline { get; init; }

    public bool IsRemote => Kind == RemoteKind;

    /// <summary>The edited file's own name.</summary>
    public string DisplayName => IsRemote ? RemotePath[(RemotePath.LastIndexOf('/') + 1)..] :
        MemberPath.Length != 0 && !IsStandaloneMember ? Path.GetFileName(MemberPath) : Path.GetFileName(WorkingPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    /// <summary>
    /// The member as the user knows it. A standalone .gz/.bz2/.zst file's one member has the synthetic raw name
    /// <see cref="ArchiveStandalone.MemberName"/>; it is shown by the name it had when the edit started (AW7).
    /// </summary>
    [JsonIgnore] public string MemberDisplay => IsStandaloneMember ? DisplayName : MemberPath;
    [JsonIgnore] private bool IsStandaloneMember => !IsRemote && ArchiveStandalone.IsStandalone(ArchiveFormat) && MemberPath == ArchiveStandalone.MemberName;

    /// <summary>Where it lives: the member in its archive, or the server and path.</summary>
    public string DisplayContainer => IsRemote ? $"{ServerDisplay}:{RemotePath}" : $"{MemberDisplay} in {ArchivePath}";

    /// <summary>What a commit writes to: the archive's file name, or the server.</summary>
    public string DisplayTarget => IsRemote ? ServerDisplay : Path.GetFileName(ArchivePath);
    public string MemberPath { get; init; } = "";
    /// <summary>The archive version the working copy is based on (updated after each commit).</summary>
    public ArchiveBaseline Baseline { get; init; } = new(0, 0, "");
    /// <summary>The member as extracted (or as last committed): its CRC-32 in the archive and the content hash.</summary>
    public uint MemberCrc32 { get; init; }
    public long MemberLength { get; init; }
    public string BaseSha256 { get; init; } = "";
    public string WorkingPath { get; init; } = "";
    public DateTime? LastCommitUtc { get; init; }
}

/// <summary>What the working copy holds compared with its base.</summary>
public enum EditState { Unchanged, Modified, Missing, Unavailable }

/// <summary>A complete working-file read, used to bind a discard decision to the reviewed bytes.</summary>
public sealed record EditWorkingReview(EditState State, string? Sha256);

/// <summary>Why a commit cannot simply replace the member.</summary>
public enum CommitCheck
{
    /// <summary>The archive is still the version the edit started from.</summary>
    Ready,
    /// <summary>The archive changed, but the member itself did not: the edit can be rebased explicitly.</summary>
    ArchiveChanged,
    /// <summary>Someone else changed or removed the member: committing would overwrite their change.</summary>
    MemberChanged,
    /// <summary>The archive is gone, moved, or unreadable.</summary>
    ArchiveMissing,
    /// <summary>The archive is protected by Windows EFS and the legacy commit cannot preserve that protection.</summary>
    ArchiveProtectedByEfs,
}

/// <summary>The archive version reviewed before an explicit overwrite/rebase decision.</summary>
public sealed record EditCommitReview(CommitCheck Check, ArchiveBaseline? Baseline);

/// <summary>Owned commit bytes, independent of subsequent editor saves. Dispose only after the job has returned.</summary>
public sealed class EditCommitCopy : IDisposable
{
    private FileStream? _pin;
    internal NativeEditSnapshot? Native { get; }
    public string Path { get; }
    public string Sha256 { get; }
    public uint Crc32 { get; }
    public long Length { get; }
    internal Microsoft.Win32.SafeHandles.SafeFileHandle HeldFile =>
        (_pin ?? throw new ObjectDisposedException(nameof(EditCommitCopy))).SafeFileHandle;
    private readonly IFileSystemOperations? _files;
    private readonly string? _identity;
    private readonly IDisposable? _protection;
    internal EditCommitCopy(string path, string sha256, uint crc32, long length, IFileSystemOperations? files = null,
        string? expectedIdentity = null, Action? sealCheckpoint = null)
    {
        Path = path; Sha256 = sha256; Crc32 = crc32; Length = length;
        var pin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            const string refusal = ArchiveOuterEfs.EditSnapshotUnavailable;
            ArchiveOuterEfs.RequireClear(pin.SafeFileHandle, refusal);
            _files = files;
            // Record the actual held identity, so later cleanup cannot use another file at this path.
            _identity = files is IEditCopyPublisher ? files.GetOpenedFileIdentity(pin.SafeFileHandle) : files?.GetFileIdentity(path);
            if (expectedIdentity is not null && _identity != expectedIdentity)
                throw new IOException("The temporary edit copy was replaced before its protection was held.");
            if (sealCheckpoint is not null && files is IEditCopyPublisher native)
            {
                _protection = native.HoldClearEditInput(pin.SafeFileHandle, refusal);
                if (_protection is not null) VerifySealedPayload(pin, sha256, length, sealCheckpoint);
            }
            _pin = pin;
        }
        catch { try { _protection?.Dispose(); } finally { pin.Dispose(); } throw; }
    }

    private static void VerifySealedPayload(FileStream pin, string sha256, long length, Action check)
    {
        if (pin.Length != length) throw new IOException("The temporary edit copy changed before its protection was held.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[(int)Math.Min(1024 * 1024, length)];
        long remaining = length;
        while (remaining > 0)
        {
            check();
            int count = pin.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, remaining)));
            if (count == 0) throw new IOException("The temporary edit copy ended during verification.");
            hash.AppendData(buffer.AsSpan(0, count));
            remaining -= count;
        }
        check();
        if (pin.Length != length || Convert.ToHexString(hash.GetHashAndReset()) != sha256)
            throw new IOException("The temporary edit copy changed before its protection was held.");
        pin.Position = 0;
    }
    private Action? _released;
    internal EditCommitCopy(NativeEditSnapshot snapshot,uint crc32,Action? released=null)
    {Native=snapshot;Path=snapshot.SourcePath;Sha256=snapshot.Expected.Version.Sha256;Length=snapshot.Expected.Version.Length;Crc32=crc32;_released=released;}
    public void Dispose()
    {
        if(Native is { } native){try{native.Dispose();}finally{Interlocked.Exchange(ref _released,null)?.Invoke();}return;}
        var pin=Interlocked.Exchange(ref _pin,null);
        if(pin is null)return; // A previously closed copy has no authority over a later pathname replacement.
        try { _protection?.Dispose(); } finally { pin.Dispose(); }
        // This GUID folder contains only this owned snapshot; the published working copy is elsewhere. Remove the exact
        // snapshot (a replaced name is kept and logged), then the folder once empty.
        if (_files is null) { Diagnostics.AppLog.Warn("FileCat kept its edit snapshot " + Path + ": it has no file-operations owner."); return; }
        if (EditPrivateCleanup.DeleteOwnedFile(_files, Path, _identity, requireClearEfs: _files is IEditCopyPublisher) is null)
            EditPrivateCleanup.RemoveEmptyFolder(System.IO.Path.GetDirectoryName(Path)!);
    }
}

/// <summary>
/// Protected session records and working copies under the user's local data (one folder per session, so the editor sees
/// the member's own file name). Temporary copies are removed only on Discard; deletion is not secure erasure.
/// </summary>
public sealed partial class EditSessionStore
{
    private readonly IFileSystemOperations fs;
    private readonly Func<string,bool,CancellationToken,EditDirectoryLease>? nativeOpen;
    private readonly Func<string,CancellationToken,EditSaveCopyDestination>? nativeCopyOpen;
    private readonly EditStorageReviewLimits storageLimits;
    private readonly string nativeRoot;
    public EditSessionStore(string root,IFileSystemOperations fs):this(root,fs,null) { }
    internal EditSessionStore(string root,IFileSystemOperations fs,Func<string,bool,CancellationToken,EditDirectoryLease>? nativeOpen):this(root,fs,nativeOpen,null) { }
    internal EditSessionStore(string root,IFileSystemOperations fs,Func<string,bool,CancellationToken,EditDirectoryLease>? nativeOpen,
        Func<string,CancellationToken,EditSaveCopyDestination>? nativeCopyOpen):this(root,fs,nativeOpen,nativeCopyOpen,null) { }
    internal EditSessionStore(string root,IFileSystemOperations fs,Func<string,bool,CancellationToken,EditDirectoryLease>? nativeOpen,
        Func<string,CancellationToken,EditSaveCopyDestination>? nativeCopyOpen,EditStorageReviewLimits? storageLimits)
        :this(root,fs,nativeOpen,nativeCopyOpen,storageLimits,null) { }
    internal EditSessionStore(string root,IFileSystemOperations fs,Func<string,bool,CancellationToken,EditDirectoryLease>? nativeOpen,
        Func<string,CancellationToken,EditSaveCopyDestination>? nativeCopyOpen,EditStorageReviewLimits? storageLimits,string? nativeRoot)
    {
        Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        this.nativeRoot=Path.TrimEndingDirectorySeparator(Path.GetFullPath(nativeRoot??root));
        this.fs=fs;this.nativeOpen=nativeOpen;this.nativeCopyOpen=nativeCopyOpen;
        this.storageLimits=storageLimits??new();this.storageLimits.Validate();
        if(nativeOpen is null&&this.nativeRoot!=Root)
            throw new ArgumentException("A separate native edit root requires native storage support.",nameof(nativeRoot));
    }
    public const long MaxMemberBytes = 1L * 1024 * 1024 * 1024;
    private const string RecordFile = "session.json";
    internal const int MaxRecordBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string Root { get; }
    internal string NativeRoot => nativeRoot;

    public IReadOnlyList<EditSessionRecord> LoadAll()
    {
        if (WindowsTokenAuthority.IsHighIntegrity) return [];
        var sessions = new List<EditSessionRecord>();
        // One private root lease for the whole native pass, and the storage gate tried at most once: once it is busy, no
        // other folder waits for it.
        using var pass = new NativePass(this);
        foreach (var (storeRoot,isNative) in nativeRoot==Root
                     ? new[]{(Root,true)} : new[]{(Root,false),(nativeRoot,true)})
        {
            if(!Directory.Exists(storeRoot))continue;
            foreach (var dir in Directory.EnumerateDirectories(storeRoot))
            {
                // A creation this process is still making is not an edit until it returns: it publishes its record or
                // removes what it made (its folder handles may also refuse another review meanwhile).
                if(isNative&&IsCreating(Path.GetFileName(dir)))continue;
                // On Windows a folder another writer holds for a moment (an edit just made, discarded or retired in another
                // FileCat process) refuses a review with a sharing violation: such a refusal is retried briefly.
                for(int attempt=0;;attempt++)
                {
                    try{if(LoadOne(dir,isNative,pass) is { } loaded)sessions.Add(loaded);break;}
                    catch(IOException error)when(OperatingSystem.IsWindows()&&(error.HResult&0xFFFF)==32&&attempt<10)
                    {Thread.Sleep(20);}
                    catch (Exception ex) when (IsStorageFailure(ex) || ex is ArgumentException)
                    { sessions.Add(UnsupportedSession(dir, "This edit record cannot be read completely. Its working folder is kept.")); break; }
                }
            }
        }
        return sessions.OrderBy(s => s.CreatedUtc).ToList();
    }

    // What the native pass of one session list shares: its private root lease (opened once, only where a folder without a
    // record needs it) and whether the storage gate was already found busy.
    private sealed class NativePass(EditSessionStore store):IDisposable
    {
        private EditDirectoryLease? root;
        internal bool Busy;
        internal EditDirectoryLease Root()=>root??=store.OpenNativeRoot(default,false);
        public void Dispose()=>root?.Dispose();
    }

    // One session folder as the session list shows it, or null when there is nothing to show. A record published while the
    // folder was judged is read once more (again).
    private EditSessionRecord? LoadOne(string dir,bool isNative,NativePass pass,bool again=false)
    {
        FileStream opened;
        try { opened = new FileStream(Path.Combine(dir, RecordFile), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
        // Only the record's own absence leads to the creation marker: a record whose folders are incomplete is unreadable.
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return LoadWithoutRecord(dir,isNative,pass,again); }
        using (var stream = opened)
        {
            long recordLength=stream.Length;
            if (recordLength > MaxRecordBytes)
                return UnsupportedSession(dir, "This edit record is larger than the supported metadata budget. Its working folder is kept.");
            byte[] bytes = new byte[checked((int)recordLength)];
            stream.ReadExactly(bytes);
            var loaded=stream.ReadByte() != -1
                ? UnsupportedSession(dir, "This edit record changed while it was read. Its working folder is kept.")
                : ReadStoredRecord(bytes, dir);
            if(nativeRoot!=Root&&loaded.IsSupported&&
               (isNative != (loaded.StorageRootIdentity.Length!=0)))
                loaded=UnsupportedSession(dir,"This edit record belongs to a different storage root. Its files are kept.");
            return loaded;
        }
    }

    // A folder without an edit record. Native creation leaves a private recovery marker before any working bytes are
    // copied. A creation another FileCat process still runs holds its folder; one nobody holds is an interrupted copy, kept
    // for review.
    private EditSessionRecord? LoadWithoutRecord(string dir,bool isNative,NativePass pass,bool again)
    {
        if(!isNative||nativeOpen is null)return null;
        // FileCat's own edit folders are named by a GUID: anything else in the store is nobody's edit, and its name may not
        // even be one a private lease can hold, so it is not opened at all.
        if(!Guid.TryParseExact(Path.GetFileName(dir),"N",out _))return null;
        EditSessionRecord? judged;bool published;
        try
        {
            // A read-only review: on Windows it shares deletion, so a creation's or a removal's folder handle does not
            // refuse it.
            var nativeRoot=pass.Root();using var session=nativeRoot.OpenStorageDirectory(Path.GetFileName(dir),default);
            byte[]? markerBytes=null;
            try{markerBytes=ReadRecordBytes(session,CreationMarker,default);}
            // A creation still before its marker, one that ended there, or one whose removal already took it: an unfinished
            // copy, kept for review, never a silent folder.
            catch(Exception missing)when(IsMissing(missing)){}
            var marker=markerBytes is null?null:ParseRecord(markerBytes,dir);
            if(marker is not null&&marker.UnsupportedReason!=UnsupportedVersionReason)return marker;
            // A folder that holds anything but what a creation makes needs no gate: it lost its record otherwise. (A record
            // published while this folder was read is judged under the gate, which reads it again.)
            var listed=session.Names(default,8192);
            if(!listed.Contains(RecordFile,StringComparer.Ordinal)&&!CreationShaped(listed))
                return marker is null?UnsupportedSession(dir,IncompleteEditReason):marker with{UnsupportedReason=IncompleteEditReason};
            (string? reason,published)=CreationState(nativeRoot,session,pass);
            judged=reason is null?null:marker is null?UnsupportedSession(dir,reason):marker with{UnsupportedReason=reason};
        }
        catch(Exception markerError)when(IsMissing(markerError)){return null;} // The folder itself went away.
        // On Windows a creation holds its marker open for writing until it ends, which refuses another reader; so does a
        // folder another program holds.
        catch(IOException markerError)when(OperatingSystem.IsWindows()&&(markerError.HResult&0xFFFF)==32)
        {return UnsupportedSession(dir,CopyingElsewhereReason);}
        // On Linux another FileCat process removing the marker holds its lease, which refuses this read (EWOULDBLOCK).
        catch(IOException markerError)when(OperatingSystem.IsLinux()&&markerError.HResult==11)
        {return UnsupportedSession(dir,CopyingElsewhereReason);}
        catch(Exception markerError)when(IsStorageFailure(markerError))
        {return UnsupportedSession(dir,"This interrupted edit's recovery record cannot be read. Its working folder is kept.");}
        // A record published meanwhile is read again, once, after this review: what that read finds is the record's own.
        if(published)return again?UnsupportedSession(dir,CopyingElsewhereReason):LoadOne(dir,isNative,pass,again:true);
        return judged;
    }

    private static EditSessionRecord UnsupportedSession(string directory, string reason, int version = int.MaxValue, string? working = null) =>
        new() { Version = version, Id = Path.GetFileName(directory), WorkingPath = SafeRecoveryPath(directory, working), UnsupportedReason = reason };

    // A usable record's working copy is a file strictly inside its own session folder, never the folder itself: that is
    // only the recovery fallback location.
    private static bool IsWorkingFile(string directory, string working) =>
        SafeRecoveryPath(directory, working) is var safe && safe == working && safe != directory;

    private static string SafeRecoveryPath(string directory, string? working)
    {
        try
        {
            if (working is not null && Path.IsPathFullyQualified(working) && !Path.EndsInDirectorySeparator(working) && Path.GetFullPath(working).StartsWith(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return Path.GetFullPath(working);
        }
        catch (ArgumentException) { }
        return directory;
    }

    private const string UnsupportedVersionReason = "This edit-session version or archive profile is not supported. Its working files are kept.";
    internal const string InterruptedCreationReason =
        "This edit is incomplete: it was interrupted while FileCat was making or removing it. What is left of it is kept for review.";
    internal const string CopyingElsewhereReason =
        "This edit's folder is in use right now (another FileCat window may still be copying it, or another program may have it open). Look again in a moment.";
    internal const string UncheckedReason =
        "This edit could not be checked just now (the edit storage or its folder was busy or could not be read). Its files are kept; look again in a moment.";
    internal const string IncompleteEditReason =
        "This edit's folder holds files of an edit but not the record that speaks for it, so FileCat cannot tell what they are. They are kept: open the working folder to review them.";

    // Whether a FileCat process still makes the edit in this folder: on Unix the creation's exclusive lock on the folder,
    // which a process that ends releases (this test takes a shared one and gives it back at once); on Windows the creation's
    // own open marker, which shares no writing.
    private static bool CreationHeldElsewhere(EditDirectoryLease session)
    {
        if(!OperatingSystem.IsWindows())
        {
            if(!FileCat.Core.Archives.Unix.UnixArchiveNative.TryLockEditCreation(session.Handle,exclusive:false))return true;
            FileCat.Core.Archives.Unix.UnixArchiveNative.UnlockEditCreation(session.Handle);return false;
        }
        try{using var marker=session.OpenFile(CreationMarker,null,default);return false;}
        catch(IOException error)when((error.HResult&0xFFFF)==32){return true;}
        catch(Exception missing)when(IsMissing(missing)){return false;}
    }

    // What a folder without an edit record holds: a creation another FileCat process still runs (in use), or one that
    // ended (interrupted); Reason null once the folder is gone, Published when its record was published meanwhile (read it
    // again). A creation makes its folder, takes its hold and writes its marker under the storage gate, publishes its record
    // under it, and is removed under it, so a folder nobody holds is judged under the gate: a creation still before its
    // hold has taken it by then. A busy gate leaves the folder in use for now.
    private static (string? Reason,bool Published) CreationState(EditDirectoryLease root,EditDirectoryLease session,NativePass pass)
    {
        if(CreationHeldElsewhere(session))return (CopyingElsewhereReason,false);
        // A busy gate (once per list) or one that cannot be reviewed leaves the folder undecided for now, not in use.
        if(pass.Busy)return (UncheckedReason,false);
        EditStorageAdmission gate;
        try{gate=EditStorageAdmission.Acquire(root,default,TimeSpan.FromMilliseconds(250));}
        catch(EditStorageBusyException){pass.Busy=true;return (UncheckedReason,false);}
        catch(Exception error)when(IsStorageFailure(error)){return (UncheckedReason,false);}
        using(gate)
        {
            string[] names;
            try{session.Review(default);names=session.Names(default,8192).ToArray();}
            catch(Exception error)when(IsMissing(error)){return (null,false);}
            catch(Exception error)when(IsStorageFailure(error)){return (UncheckedReason,false);}
            if(names.Contains(RecordFile,StringComparer.Ordinal))return (null,true);
            if(CreationHeldElsewhere(session))return (CopyingElsewhereReason,false);
            if(!CreationShaped(names))return (IncompleteEditReason,false);
            if(!names.Contains(CreationMarker,StringComparer.Ordinal))return (InterruptedCreationReason,false);
            // A copy a creation did not finish is shorter than its marker says it was admitted to be; one that is already
            // that long either was being published (a record stage is there) or is a whole copy whose record went some other
            // way, which FileCat keeps whole: it cannot tell such a copy from an edit of the user's.
            try
            {
                var (admitted,leaf,recorded,read)=Admitted(session);
                if(!read)return (UncheckedReason,false); // Its marker could not be read just now.
                // Read, but naming no working file: FileCat never writes such a marker, so the folder is kept whole.
                if(leaf is null)return (IncompleteEditReason,false);
                var (written,measured)=Written(session,leaf,default);
                if(!measured)return (UncheckedReason,false); // Its copy could not be measured just now.
                if(written is null)return (InterruptedCreationReason,false); // No copy: nothing of the user's.
                if(!recorded)return (IncompleteEditReason,false); // An earlier build's marker does not say how long.
                // Only a creation's own record stage says a copy was being published; any other record's (a revision's) does
                // not. An empty copy holds nothing of anyone's, whatever its marker records.
                return (written==0||written<admitted||names.Any(IsCreationStage)?InterruptedCreationReason:IncompleteEditReason,false);
            }
            catch(Exception error)when(IsStorageFailure(error)){return (UncheckedReason,false);}
        }
    }

    // Whether this folder holds only what a creation makes, and nothing but that: its marker, its working folder, records it
    // was publishing, names an interrupted removal set aside. An empty folder is a creation's first step, so it counts; one
    // that holds only the Finder's own .DS_Store is nobody's work, so it does not.
    private static bool CreationShaped(IReadOnlyList<string> names)=>
        (names.Count==0||names.Any(name=>name==CreationMarker||name=="files"||IsRecordStage(name)||IsSetAside(name)))&&
        names.All(name=>name==CreationMarker||name=="files"||IsRecordStage(name)||IsSetAside(name)||IsDesktopArtifact(name));

    // What reading or holding private edit storage can fail with: the platform's own calls (Windows' NT and security calls
    // raise Win32Exception) as well as .NET's I/O and access errors.
    private static bool IsStorageFailure(Exception error)=>error is IOException or UnauthorizedAccessException or
        System.ComponentModel.Win32Exception;

    // Not there: .NET's own exceptions, or a Unix native open's ENOENT.
    private static bool IsMissing(Exception error)=>error is FileNotFoundException or DirectoryNotFoundException||
        !OperatingSystem.IsWindows()&&error is IOException{HResult:2};

    // Only bounded bytes and the store-owned directory name enter the parser. Unknown shapes never choose .NET types.
    internal static EditSessionRecord ParseRecord(byte[] bytes, string directory,bool allowPendingAdoption=false)
    {
        const string unsupported = UnsupportedVersionReason;
        if (bytes.Length > MaxRecordBytes) return UnsupportedSession(directory, "This edit record exceeds the metadata budget. Its working folder is kept.");
        try
        {
            _=ArchiveWritePolicy.Utf8.GetCharCount(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
            var value = document.RootElement;
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(nameof(EditSessionRecord.Id), out var id) ||
                id.ValueKind != JsonValueKind.String || id.GetString() != Path.GetFileName(directory))
                return UnsupportedSession(directory, "This edit record does not identify its working folder. The folder is kept for recovery.");
            int version = value.TryGetProperty(nameof(EditSessionRecord.Version), out var versionValue)
                ? versionValue.ValueKind == JsonValueKind.Number && versionValue.TryGetInt32(out int parsed) ? parsed : int.MaxValue : 1;
            string? working = value.TryGetProperty(nameof(EditSessionRecord.WorkingPath), out var pathValue) && pathValue.ValueKind == JsonValueKind.String
                ? pathValue.GetString() : null;
            if (version is not (1 or EditSessionRecord.CurrentVersion or EditSessionRecord.NativeStorageVersion)) return UnsupportedSession(directory, unsupported, version, working);
            try
            {
                var record = JsonSerializer.Deserialize<EditSessionRecord>(bytes, Json);
                if (record is not null)
                {
                    record = record with { Version = version };
                    if ((record.IsSupported || allowPendingAdoption && record.IsPendingAdoption) && IsWorkingFile(directory, record.WorkingPath))
                        return version == 1 && !record.IsRemote ? record with { ArchiveFormat = ArchiveWriteFormat.Zip, ArchiveProfile = null } : record;
                }
            }
            catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException or ArchiveWriteException) { }
            return UnsupportedSession(directory, unsupported, version, working);
        }
        catch (Exception error) when(error is JsonException or System.Text.DecoderFallbackException or InvalidOperationException)
        { return UnsupportedSession(directory, "This edit record is malformed or too deeply nested. Its working folder is kept for recovery."); }
    }

    public EditSessionRecord? Find(string archivePath, string memberPath) =>
        Active(LoadAll()).Where(s => s.IsSupported && s.IsLegacyZip && string.Equals(s.ArchivePath, Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase) &&
                             s.MemberPath == memberPath).OrderBy(Route).FirstOrDefault();

    public EditSessionRecord? FindRemote(string profileId, string remotePath) =>
        Active(LoadAll()).Where(s => s.IsSupported && s.IsRemote && s.ProfileId == profileId && s.RemotePath == remotePath)
            .OrderBy(Route).FirstOrDefault();

    /// <summary>
    /// The edits that speak for themselves: an old record whose adopted copy is retiring its folder is no longer the edit
    /// (the copy is), even while its objects wait for their exact removal.
    /// </summary>
    public static IReadOnlyList<EditSessionRecord> Active(IReadOnlyList<EditSessionRecord> rows)
    {
        var retiring=rows.Where(row=>row.LegacyRetiring&&row.LegacyAdoption is not null).Select(row=>row.LegacyAdoption!.SourceId)
            .ToHashSet(StringComparer.Ordinal);
        return retiring.Count==0?rows:rows.Where(row=>!(row.StorageRootIdentity.Length==0&&retiring.Contains(row.Id))).ToArray();
    }
    // The old edit stays the route while its adopted copy is only retained; a retiring copy is the route.
    private static int Route(EditSessionRecord row)=>row.LegacyAdoption is null||row.LegacyRetiring?0:1;

    /// <summary>
    /// Copies a server file into a new private working copy, marked as coming from the server, with the revision it was
    /// read at as the base that commits are checked against.
    /// </summary>
    public EditSessionRecord CreateRemote(string profileId, string serverDisplay, string remotePath, IContentSource source, ContentRevision revision,
        string? originMark, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateLength(revision.Length);
        if (source.GetRevision() != revision) throw new IOException("The file's revision is unavailable or changed before its edit could be copied.");
        string name = remotePath[(remotePath.LastIndexOf('/') + 1)..];
        return CreateSession(name, (working,open) => WriteWorkingCopy(source, working, ct, revision,outputFactory:open), (working, copy) =>
        {
            if (originMark is not null) PreserveOriginMark(working, originMark);
            return new EditSessionRecord
            {
                Kind = EditSessionRecord.RemoteKind,
                ProfileId = profileId,
                ServerDisplay = serverDisplay,
                RemotePath = remotePath,
                RemoteBaseline = revision,
                BaseSha256 = copy.Sha256,
                WorkingPath = working,
            };
        }, ct);
    }

    /// <summary>Owns the server content through a complete copy and closes it before publishing the session.</summary>
    public EditSessionRecord CreateRemoteFrom(string profileId, string serverDisplay, string remotePath, ResourceProvider provider, ItemRef item,
        string? originMark, CancellationToken ct = default)
    {
        ContentRevision revision = default;
        string name = remotePath[(remotePath.LastIndexOf('/') + 1)..];
        return CreateSession(name, (working,open) =>
        {
            var source = provider.OpenContent(item) ?? throw new IOException("This item has no content to edit.");
            return ReadOwned(source, () =>
            {
                revision = source.GetRevision() ?? throw new IOException("The file's revision is unavailable; copy it with F5 instead.");
                ValidateLength(revision.Length);
                if (source.GetRevision() != revision) throw new IOException("The file's revision is unavailable or changed before its edit could be copied.");
                return WriteWorkingCopy(source, working, ct, revision,outputFactory:open);
            });
        }, (working, copy) =>
        {
            if (originMark is not null) PreserveOriginMark(working, originMark);
            return new EditSessionRecord
            {
                Kind = EditSessionRecord.RemoteKind,
                ProfileId = profileId,
                ServerDisplay = serverDisplay,
                RemotePath = remotePath,
                RemoteBaseline = revision,
                BaseSha256 = copy.Sha256,
                WorkingPath = working,
            };
        }, ct);
    }

    /// <summary>
    /// Whether a commit would replace exactly the server file the edit started from; <paramref name="now"/> is its
    /// revision now, or null when it is gone.
    /// </summary>
    public static CommitCheck CheckRemote(EditSessionRecord record, ContentRevision? now) =>
        now is not { } current ? CommitCheck.ArchiveMissing
        : current.Length == record.RemoteBaseline.Length && current.ModifiedTicks == record.RemoteBaseline.ModifiedTicks ? CommitCheck.Ready
        : CommitCheck.MemberChanged;

    /// <summary>After a successful commit to the server: what was written and its new revision become the base.</summary>
    public EditSessionRecord CommittedRemote(EditSessionRecord record, string committedSha256, ContentRevision revision)
    {
        var updated = record with { RemoteBaseline = revision, BaseSha256 = committedSha256, LastCommitUtc = DateTime.UtcNow };
        return Save(updated);
    }

    /// <summary>A session folder with the working copy under the edited file's own name (or a safe stand-in).</summary>
    private sealed record WorkingCopy(string Sha256, uint Crc, long Length);

    private void PreserveOriginMark(string path, string mark)
    {
        if (!fs.WriteOriginMark(path, mark))
            throw new IOException("The download mark could not be kept on the private edit copy.");
    }

    private EditSessionRecord CreateSession(string fileName, Func<string,Func<long,FileStream>?,WorkingCopy> write,
        Func<string, WorkingCopy, EditSessionRecord> describe, CancellationToken ct) =>
        CreateSession(fileName,(path,open,_)=>write(path,open),describe,ct);

    /// <summary>
    /// A new edit in either store. The copy is given its working folder's own lease where there is one (native storage), so
    /// that whatever it writes beside the working file goes through the folder FileCat holds, never through its path.
    /// </summary>
    private EditSessionRecord CreateSession(string fileName, Func<string,Func<long,FileStream>?,Func<EditDirectoryLease>?,WorkingCopy> write,
        Func<string, WorkingCopy, EditSessionRecord> describe, CancellationToken ct)
    {
        RefuseElevatedSession();
        ct.ThrowIfCancellationRequested();
        if(nativeOpen is not null)return CreateNativeSession(fileName,(path,open,files)=>write(path,open,files),describe,ct);
        string id = Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Root, id);
        CreatePrivateDirectory(dir);
        // Keep metadata outside the editor's file namespace, including a member named session.json.
        string files = Path.Combine(dir, "files");
        string fallback = "file" + Path.GetExtension(fileName);
        string safeName = SafeNames.Validate(fileName) is null ? fileName : SafeNames.Validate(fallback) is null ? fallback : "file";
        string working = Path.Combine(files, safeName);
        try
        {
            Directory.CreateDirectory(files);
            var copy = write(working,null,null);
            ct.ThrowIfCancellationRequested();
            var record = describe(working, copy) with { Id = id, CreatedUtc = DateTime.UtcNow };
            ct.ThrowIfCancellationRequested();
            return Save(record);
        }
        catch
        {
            EditPrivateCleanup.DeleteOwnedFolder(fs, dir); // A folder it cannot prove is kept and logged.
            throw;
        }
    }

    private static void ValidateLength(long length)
    {
        if (length < 0) throw new IOException("The file's complete length is unavailable; copy it with F5 instead of starting an edit session.");
        if (length > MaxMemberBytes) throw new IOException("Files over 1 GiB are not edited through sessions; copy the file with F5 instead.");
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(path);
        else Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void CheckComplete(IContentSource source)
    {
        if (source is IPartialContent partial && (partial.MissingRanges.Count > 0 || partial.Caveat is not null))
            throw new IOException("This source contains lost or uncertain bytes and cannot be used as an edit-session baseline.");
    }

    /// <summary>The caller owns the source. Nothing is published unless exact bytes, EOF and available revisions agree.</summary>
    private static WorkingCopy WriteWorkingCopy(IContentSource source, string working, CancellationToken ct, ContentRevision? expected = null,
        Action? check = null, Action<FileStream>? finalize = null,Func<long,FileStream>? outputFactory=null)
    {
        void Check() { ct.ThrowIfCancellationRequested(); check?.Invoke(); }
        Check();
        long length = source.Length; ValidateLength(length); CheckComplete(source);
        var before = source.GetRevision();
        Check();
        if (before is { } revision && revision.Length != length || expected is { } pinned && before != pinned)
            throw new IOException("The file changed before its edit could be copied.");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var output = outputFactory?.Invoke(length) ?? new FileStream(working, options);
        var outputHandle = OperatingSystem.IsWindows() ? output.SafeFileHandle : null;
        void CheckOutput()
        {
            if (outputHandle is not null) ArchiveOuterEfs.RequireClear(outputHandle, ArchiveOuterEfs.EditTemporaryOutputUnavailable);
        }
        CheckOutput();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        uint crc = 0; long offset = 0;
        while (true)
        {
            Check(); CheckOutput(); CheckComplete(source);
            // A one-byte read at the declared end checks actual EOF (and archive checksum validation) without
            // accepting or writing bytes beyond either the declared size or the edit-session size limit.
            int want = (int)Math.Min(buffer.Length, length - offset + 1);
            int n = source.Read(offset, buffer.AsSpan(0, want));
            Check(); CheckOutput(); CheckComplete(source);
            if (n < 0 || n > want) throw new InvalidDataException("The edit source returned an invalid byte count.");
            if (n == 0)
            {
                if (offset != length) throw new IOException("The file ended before its complete edit could be copied.");
                break;
            }
            if (n > length - offset) throw new IOException("The file grew beyond its declared edit-session length.");
            output.Write(buffer, 0, n);
            hash.AppendData(buffer, 0, n); crc = Crc32.Append(crc, buffer.AsSpan(0, n));
            offset += n;
        }
        Check();
        if (source.Length != length || source.GetRevision() != before) throw new IOException("The file's length or revision changed while its edit was copied.");
        Check(); CheckComplete(source);
        CheckOutput();
        if (finalize is not null) { output.Flush(flushToDisk: true); finalize(output); }
        Check(); CheckOutput();
        return new WorkingCopy(Convert.ToHexString(hash.GetHashAndReset()), crc, offset);
    }

    /// <summary>
    /// Extracts <paramref name="member"/> into a new private working copy. The copy carries the archive's download mark,
    /// so programs that open it apply the same checks as for the archive.
    /// </summary>
    public EditSessionRecord Create(ResourceProvider zip, ItemRef member, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (member.Parent.Scheme != Schemes.Zip)
            throw new NotSupportedException("Edit sessions work on members of archives in folders.");
        if (member.Parent.Container is not { IsFileSystem: true } archiveFile)
            throw new NotSupportedException(ZipProvider.NestedEditUnavailable);
        string archive = Path.GetFullPath(archiveFile.Path);
        string memberPath = member.Parent.Path.Length == 0 ? member.Name : member.Parent.Path + "/" + member.Name;
        using var archiveBytes = new ArchiveMutationReadStream(archive, ArchiveOuterEfs.EditUnavailable);
        using var protection = HoldClearEditInput(archiveBytes.Handle, ArchiveOuterEfs.EditUnavailable);
        string? heldIdentity = fs is IEditCopyPublisher ? fs.GetOpenedFileIdentity(archiveBytes.Handle)
            ?? throw new IOException("The archive's held identity could not be read.") : null;
        var baseline = ArchiveBaseline.OfHeld(archiveBytes, ct.ThrowIfCancellationRequested);
        uint expectedCrc; long expectedLength;
        using (var catalog = new System.IO.Compression.ZipArchive(archiveBytes, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            if (catalog.Entries.Count > ZipProvider.MaxEntries) throw new InvalidDataException("Too many archive members to prepare an edit.");
            System.IO.Compression.ZipArchiveEntry? found = null;
            foreach (var entry in catalog.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (ArchivePaths.Normalize(entry.FullName) != memberPath) continue;
                if (found is not null) throw new IOException("This name appears more than once in the archive; extract the copy you want with F5.");
                found = entry;
            }
            if (found is null || found.FullName.EndsWith('/')) throw new FileNotFoundException("The archive member no longer exists.");
            expectedCrc = found.Crc32; expectedLength = found.Length; ValidateLength(expectedLength);
        }
        if (!baseline.MatchesForMutation(archive, ct.ThrowIfCancellationRequested, ArchiveOuterEfs.EditUnavailable))
            throw new IOException("The archive changed while its edit was being prepared.");
        return CreateSession(member.Name, (working,open) =>
        {
            ct.ThrowIfCancellationRequested();
            var content = zip is ZipProvider provider ? provider.OpenEditContent(member, heldIdentity is null ? null : VerifyOpenedArchive)
                : zip.OpenContent(member);
            var source = Content.ProgressiveContent.Sequential(content) ?? throw new NotSupportedException("This member is encrypted and cannot be edited here.");
            return ReadOwned(source, () =>
            {
                var copy = WriteWorkingCopy(source, working, ct,outputFactory:open);
                if (copy.Length != expectedLength || copy.Crc != expectedCrc ||
                    !baseline.MatchesForMutation(archive, ct.ThrowIfCancellationRequested, ArchiveOuterEfs.EditUnavailable))
                    throw new IOException("The archive member changed or is damaged; no edit session was created.");
                return copy;
            });
        }, (working, copy) =>
        {
            if (fs.ReadOriginMark(archive) is { } mark) PreserveOriginMark(working, mark);
            return new EditSessionRecord
            {
                ArchivePath = archive,
                MemberPath = memberPath,
                Baseline = baseline,
                MemberCrc32 = copy.Crc,
                MemberLength = copy.Length,
                BaseSha256 = copy.Sha256,
                WorkingPath = working,
            };
        }, ct);

        void VerifyOpenedArchive(Microsoft.Win32.SafeHandles.SafeFileHandle opened)
        {
            if (fs.GetOpenedFileIdentity(opened) != heldIdentity)
                throw new IOException("The edit reader opened a different archive from the held source.");
        }
    }

    // A close failure prevents publication. When the copy already failed, retain its primary error.
    private static WorkingCopy ReadOwned(IContentSource source, Func<WorkingCopy> read)
    {
        WorkingCopy copy;
        try { copy = read(); }
        catch
        {
            try { source.Dispose(); }
            catch (Exception ex) { Diagnostics.AppLog.Warn("Could not close edit content after copying failed", ex); }
            throw;
        }
        source.Dispose();
        return copy;
    }

    public EditSessionRecord Save(EditSessionRecord record)
    {
        RefuseElevatedSession();
        if (!record.IsSupported) throw new NotSupportedException("Unsupported edit-session records are kept without rewriting them.");
        if(record.StorageRootIdentity.Length!=0)return SaveNativeRevision(record);
        string dir = SessionDirectory(record);
        string destination = Path.Combine(dir, RecordFile);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(record, Json);
        // Readers share deletion so a record can be replaced atomically. On
        // Windows, another process can still hold the destination briefly
        // (for example while scanning a newly written session). Give that
        // transient lock a bounded chance to clear, without ever reporting a
        // committed baseline before its record is safely published.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            string temp = Path.Combine(dir, RecordFile + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false; string? tempIdentity = null;
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true; tempIdentity = fs.GetFileIdentity(temp);
                    stream.Write(bytes);
                }
                fs.Move(temp, destination, replaceExisting: true);
                return record;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < 5)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
            finally
            {
                // A refused replacement must not leave session metadata in
                // disposable sibling files. A cleanup denial is itself a
                // failure, rather than permission to create more copies.
                if (created && EditPrivateCleanup.DeleteOwnedFile(fs, temp, tempIdentity) is { } kept) throw new IOException(kept);
            }
        }
        throw new IOException("The edit record could not be published.");
    }

    public EditState StateOf(EditSessionRecord record) => StateOf(record, new LocalFileSystemProvider());

    /// <summary>Only a complete bounded read establishes whether the working copy matches its base.</summary>
    public EditState StateOf(EditSessionRecord record, Resources.ResourceProvider provider, Action? check = null) => ReviewWorking(record, provider, check).State;

    public EditWorkingReview ReviewWorking(EditSessionRecord record, Resources.ResourceProvider provider, Action? check = null)
    {
        void Check() => check?.Invoke();
        bool opened = false;
        try
        {
            Check();
            // A native working copy is read sharing writing: an editor may save meanwhile, and the revision check below
            // then reports the copy as unavailable for now (a later review reads it again).
            using var source = record.StorageRootIdentity.Length!=0?ObservedNativeWorking(()=>OpenNativeWorking(record,default,review:true)):
                Content.ProgressiveContent.Sequential(provider.OpenContent(ItemRef.ForFileSystemPath(record.WorkingPath, EntryKind.File)));
            Check();
            if (source is null) return new(EditState.Unavailable, null);
            opened = true;
            long length = source.Length; Check(); ValidateLength(length); CheckComplete(source);
            var before = source.GetRevision(); Check();
            if (before is { } revision && revision.Length != length) return new(EditState.Unavailable, null);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024]; long offset = 0;
            while (true)
            {
                Check(); CheckComplete(source);
                int want = (int)Math.Min(buffer.Length, length - offset + 1);
                int n = source.Read(offset, buffer.AsSpan(0, want));
                Check(); CheckComplete(source);
                if (n < 0 || n > want || n > length - offset) return new(EditState.Unavailable, null);
                if (n == 0) { if (offset != length) return new(EditState.Unavailable, null); break; }
                hash.AppendData(buffer, 0, n); offset += n;
            }
            Check(); long afterLength = source.Length; Check(); var after = source.GetRevision(); Check(); CheckComplete(source);
            if (afterLength != length || after != before) return new(EditState.Unavailable, null);
            string sha256 = Convert.ToHexString(hash.GetHashAndReset());
            return new(sha256 == record.BaseSha256 ? EditState.Unchanged : EditState.Modified, sha256);
        }
        // A name that goes once the copy is open is an editor's save in progress (delete, then create): unavailable for now.
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        { Check(); return new(opened ? EditState.Unavailable : EditState.Missing, null); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        { Check(); return new(EditState.Unavailable, null); }
    }

    /// <summary>Whether a commit would replace exactly the member the edit started from.</summary>
    public CommitCheck Check(EditSessionRecord record) => ReviewCommit(record).Check;

    public EditCommitReview ReviewCommit(EditSessionRecord record, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            var baseline = ArchiveBaseline.OfForMutation(record.ArchivePath, ct.ThrowIfCancellationRequested);
            if (baseline == record.Baseline) return new(CommitCheck.Ready, baseline);
            using var source = new ArchiveMutationReadStream(record.ArchivePath);
            using var archive = new System.IO.Compression.ZipArchive(source, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > ZipProvider.MaxEntries) throw new InvalidDataException("Too many archive members to review an edit commit.");
            System.IO.Compression.ZipArchiveEntry? found = null; bool duplicate = false;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (ArchivePaths.Normalize(entry.FullName) != record.MemberPath) continue;
                duplicate |= found is not null; found = entry;
            }
            if (!baseline.MatchesForMutation(record.ArchivePath, ct.ThrowIfCancellationRequested))
                throw new IOException("The archive changed while its edit commit was reviewed.");
            var check = !duplicate && found is not null && !found.FullName.EndsWith('/') && found.Crc32 == record.MemberCrc32 && found.Length == record.MemberLength
                ? CommitCheck.ArchiveChanged : CommitCheck.MemberChanged;
            return new(check, baseline);
        }
        catch (ArchiveWriteException error) when (error.Reason == ArchiveWriteReason.PublicationUnavailable)
        { return new(CommitCheck.ArchiveProtectedByEfs, null); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return new(CommitCheck.ArchiveMissing, null); }
    }

    /// <summary>
    /// Capture one complete bounded working file before queueing, with hash/CRC from those same bytes.
    /// <paramref name="check"/> is called as the copy proceeds, for a caller that wants to stop or stay responsive while
    /// a large member is copied (the native copy runs without the storage gate).
    /// </summary>
    public EditCommitCopy PrepareCommit(EditSessionRecord record, string tempRoot, CancellationToken ct = default, Action? check = null)
    {
        RefuseElevatedSession();
        ct.ThrowIfCancellationRequested();
        if(record.StorageRootIdentity.Length!=0)return PrepareNativeSnapshot(record,ct,check);
        // The held file denies ordinary writers while its approved content is captured.
        using var source = new CommitContent(record.WorkingPath);
        using var protection = HoldClearEditInput(source.HeldFile, ArchiveOuterEfs.WorkingCopyUnavailable);
        void CheckSource()
        {
            check?.Invoke();
            source.RequireClearWorkingFile();
        }
        void SealCheckpoint() { ct.ThrowIfCancellationRequested(); CheckSource(); }
        string directory = Path.Combine(tempRoot, "edit-commit-" + Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(directory);
        string path = Path.Combine(directory, Path.GetFileName(record.WorkingPath));
        bool bindOutput = fs is IEditCopyPublisher;
        string? createdIdentity = null;
        FileStream CreateBoundOutput(long _)
        {
            var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            try
            {
                createdIdentity = fs.GetOpenedFileIdentity(output.SafeFileHandle)
                    ?? throw new IOException("The temporary edit copy's identity could not be recorded.");
                return output;
            }
            catch { output.Dispose(); throw; }
        }
        try
        {
            // Editors that already hold a write handle cause a conservative sharing refusal on Windows.
            // Unix sharing is advisory: length/time checks still apply; this is not a hostile-writer snapshot guarantee.
            var copy = WriteWorkingCopy(source, path, ct, check: CheckSource, outputFactory: bindOutput ? CreateBoundOutput : null);
            File.SetLastWriteTimeUtc(path, source.ModifiedUtc);
            if (fs.ReadOriginMark(record.WorkingPath) is { } mark) PreserveOriginMark(path, mark);
            SealCheckpoint();
            return new EditCommitCopy(path, copy.Sha256, copy.Crc, copy.Length, fs, createdIdentity, SealCheckpoint);
        }
        catch
        {
            if (!bindOutput) EditPrivateCleanup.DeleteOwnedFolder(fs, directory);
            else if (EditPrivateCleanup.DeleteOwnedFile(fs, path, createdIdentity, requireClearEfs: true) is null)
                EditPrivateCleanup.RemoveEmptyFolder(directory);
            throw;
        }
    }

    /// <summary>Capture a save-copy source completely on its caller's admitted worker, before any destination replacement.</summary>
    public EditCommitCopy PrepareSaveCopy(EditSessionRecord record, string tempRoot, ResourceProvider provider, Action check)
    {
        RefuseElevatedSession();
        check();
        if(record.StorageRootIdentity.Length!=0)return PrepareNativeSnapshot(record,default,check);
        return CaptureSaveCopy(record, tempRoot, provider, check);
    }

    /// <summary>
    /// The Save copy source the application publishes with <see cref="PublishSaveCopy"/> (a verified stage beside the
    /// destination, which then replaces it), for every edit alike: a complete copy in FileCat's own temporary folder, made on
    /// its caller's admitted worker before any destination is touched. A native edit's working file is read through its held
    /// private namespace, and the download mark kept is the one that exact object carries; a legacy edit's through the
    /// provider. Nothing of it stays in the native store (the native destination workflow, <see cref="PrepareSaveCopy"/> with
    /// <c>SaveNewNativeCopy</c>, remains an explicit composition).
    /// </summary>
    public EditCommitCopy CaptureSaveCopy(EditSessionRecord record, string tempRoot, ResourceProvider provider, Action check)
    {
        RefuseElevatedSession();
        check();
        using var legacyPin = OperatingSystem.IsWindows() && record.StorageRootIdentity.Length == 0
            ? new CommitContent(record.WorkingPath) : null;
        using var protection = legacyPin is null ? null : HoldClearEditInput(legacyPin.HeldFile, ArchiveOuterEfs.WorkingCopyUnavailable);
        string directory = Path.Combine(tempRoot, "edit-commit-" + Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(directory);
        string path = Path.Combine(directory, Path.GetFileName(record.WorkingPath));
        try
        {
            WorkingCopy bytes; string? mark;
            if (record.StorageRootIdentity.Length != 0)
            {
                NativeWorkingContent? held = null;
                try
                {
                    using (var source = ObservedNativeWorking(() => held = OpenNativeWorking(record, default), borrowed: true))
                    {
                        check();
                        bytes = WriteWorkingCopy(source, path, default, check: check);
                    }
                    check();
                    mark = OriginMark((held ?? throw new IOException("The working copy was not read.")).ReadOrigin());
                }
                finally { held?.Dispose(); }
            }
            else
            {
                using var source = Content.ProgressiveContent.Sequential(provider.OpenContent(ItemRef.ForFileSystemPath(record.WorkingPath, EntryKind.File)))
                    ?? throw new IOException("The working copy cannot be read completely.");
                if (legacyPin is not null && source is FileContentSource file && fs is IEditCopyPublisher &&
                    (fs.GetOpenedFileIdentity(legacyPin.HeldFile) is not { } heldIdentity || fs.GetOpenedFileIdentity(file.HeldFile) != heldIdentity))
                    throw new IOException("The working copy's opened identity changed before it could be captured.");
                void CheckSource()
                {
                    check();
                    legacyPin?.RequireClearWorkingFile();
                    if (OperatingSystem.IsWindows() && source is FileContentSource local)
                        ArchiveOuterEfs.RequireClear(local.HeldFile, ArchiveOuterEfs.WorkingCopyUnavailable);
                }
                bytes = WriteWorkingCopy(source, path, default, check: CheckSource);
                CheckSource();
                mark = fs.ReadOriginMark(record.WorkingPath);
            }
            // A copy of a marked edit is not made without its mark: a temporary area that cannot keep one refuses the Save
            // copy up front, as it refuses a commit's copy (EditCommitCopyTests). Only a destination that cannot keep the
            // mark, where the copy is already made, is reported instead.
            if (mark is not null) PreserveOriginMark(path, mark);
            check(); return new EditCommitCopy(path, bytes.Sha256, bytes.Crc, bytes.Length, fs);
        }
        catch
        {
            EditPrivateCleanup.DeleteOwnedFolder(fs, directory);
            throw;
        }
    }

    /// <summary>Copy owned bytes into a unique sibling first. Publish only after the native copy returns and demand remains active.</summary>
    /// <returns>Null, or what the saved copy could not keep: its download mark, where the destination cannot store it.</returns>
    public static string? PublishSaveCopy(EditCommitCopy copy, string destination, IFileSystemOperations files, Action check)
    {
        RefuseElevatedSession();
        check();
        if(copy.Native is not null)throw new NotSupportedException("The private edit snapshot is kept until native save-copy destination publication is qualified.");
        if (files is IEditCopyPublisher native)
            return native.PublishEditCopy(copy.HeldFile, copy.Sha256, copy.Length, destination, check);
        string staged = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".filecat-edit-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
        string? stagedIdentity = null;
        try
        {
            // The stage's identity is read by name as soon as the copy ends, after a failure as after success, so a partial
            // stage is removed under the same rules as a complete one (risk R21); an unreadable identity keeps it.
            try { files.CopyFile(copy.Path, staged, new FileCopyOptions { FlushDestination = true }, null, default); }
            catch { stagedIdentity = StageIdentity(files, staged); throw; }
            stagedIdentity = files.GetFileIdentity(staged);
            check();
            // The download mark goes with the copy, as an F5 copy carries it (a Linux copy carries no user attributes of
            // its own); where the destination cannot store it (FAT or exFAT have no streams or extended attributes, some
            // network shares refuse them), the copy is saved and the user is told, as F5 tells.
            string? lost = null;
            if (files.ReadOriginMark(copy.Path) is { } mark && !KeepMark(files, staged, mark))
                lost = "Its download mark was not kept: the destination cannot store it, so the system may not warn when the copy is opened.";
            check();
            VerifySaveCopy(copy, staged, check);
            check();
            files.Move(staged, destination, replaceExisting: true, writeThrough: true);
            check();
            return lost;
        }
        finally
        {
            // Never remove the destination. A failed copy may have left part of this stage: remove only that exact object
            // (handle-bound where available); in a shared folder on Unix it is kept and named instead (risk R21).
            if (files.GetFileIdentity(staged) is not null)
            {
                string? kept = EditPrivateCleanup.DeleteOwnedFile(files, staged, stagedIdentity, inFileCatData: false);
                if (kept is not null) Diagnostics.AppLog.Warn(kept);
            }
        }
    }

    // Whether the stage carries the mark: copied with it, or written now. A destination that cannot hold one is not an error.
    private static bool KeepMark(IFileSystemOperations files, string staged, string mark)
    {
        try { return files.ReadOriginMark(staged) is not null || files.WriteOriginMark(staged, mark); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or InvalidDataException)
        { return false; }
    }

    private static string? StageIdentity(IFileSystemOperations files, string staged)
    {
        try { return files.GetFileIdentity(staged); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void VerifySaveCopy(EditCommitCopy copy, string path, Action check)
    {
        using var source = new CommitContent(path);
        if (source.Length != copy.Length) throw new IOException("The saved copy's length does not match the complete edit.");
        var revision = source.GetRevision();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024]; long offset = 0;
        while (true)
        {
            check();
            int want = (int)Math.Min(buffer.Length, copy.Length - offset + 1);
            int count = source.Read(offset, buffer.AsSpan(0, want));
            check();
            if (count < 0 || count > want || count > copy.Length - offset) throw new IOException("The saved copy changed while it was verified.");
            if (count == 0)
            {
                if (offset != copy.Length) throw new IOException("The saved copy ended before the complete edit.");
                break;
            }
            hash.AppendData(buffer, 0, count); offset += count;
        }
        if (source.Length != copy.Length || source.GetRevision() != revision || Convert.ToHexString(hash.GetHashAndReset()) != copy.Sha256)
            throw new IOException("The saved copy does not match the complete edit.");
        check();
    }

    private sealed class CommitContent : IContentSource
    {
        private readonly FileStream _stream; private readonly string _path;
        public CommitContent(string path)
        {
            _path = path;
            _stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.RandomAccess);
        }
        internal Microsoft.Win32.SafeHandles.SafeFileHandle HeldFile => _stream.SafeFileHandle;
        internal void RequireClearWorkingFile() => ArchiveOuterEfs.RequireClear(HeldFile, ArchiveOuterEfs.WorkingCopyUnavailable);
        public string DisplayName => _path;
        public long Length => _stream.Length;
        public bool CanSeek => true;
        public string? LocalPath => _path;
        public DateTime ModifiedUtc => File.GetLastWriteTimeUtc(_path);
        public ContentRevision? GetRevision() => new ContentRevision(Length, ModifiedUtc.Ticks);
        public int Read(long offset, Span<byte> buffer) => RandomAccess.Read(_stream.SafeFileHandle, buffer, offset);
        public void Dispose() => _stream.Dispose();
    }

    /// <summary>The update that writes the working copy back; <paramref name="rebase"/> accepts the archive's current version.</summary>
    public ArchivePlan CommitPlan(EditSessionRecord record, bool rebase) =>
        new(record.ArchivePath, rebase ? ArchiveBaseline.OfForMutation(record.ArchivePath) : record.Baseline,
            [new ArchiveChange(ArchiveChangeKind.Replace, record.MemberPath, record.WorkingPath)]) { LegacyEditCommit = true };

    internal IRetainedEditUpload? RemoteCommitInput(EditSessionRecord record,EditCommitCopy copy)
    {
        if(!record.IsRemote)throw new IOException("This edit does not belong to a server file.");
        copy.Native?.RequireSession(record);return copy.Native;
    }

    public ArchivePlan CommitPlan(EditSessionRecord record, EditCommitReview review, EditCommitCopy copy)
    {
        copy.Native?.RequireSession(record);
        var plan=new ArchivePlan(record.ArchivePath,review.Baseline??throw new IOException("The reviewed archive is unavailable."),
            [new ArchiveChange(ArchiveChangeKind.Replace,record.MemberPath,copy.Path)]){EditInput=copy.Native,LegacyEditCommit=true};
        copy.Native?.RequireRequest(plan);return plan;
    }

    /// <summary>After a successful commit: the archive's new version and the committed content become the base.</summary>
    public EditSessionRecord Committed(EditSessionRecord record, string committedSha256)
    {
        return CommittedArchive(record, committedSha256, null);
    }

    public EditSessionRecord Committed(EditSessionRecord record, EditCommitCopy copy) => CommittedArchive(record, copy.Sha256, copy);

    private EditSessionRecord CommittedArchive(EditSessionRecord record, string committedSha256, EditCommitCopy? copy)
    {
        var baseline = ArchiveBaseline.OfForMutation(record.ArchivePath);
        using var source = new ArchiveMutationReadStream(record.ArchivePath);
        using var archive = new System.IO.Compression.ZipArchive(source, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > ZipProvider.MaxEntries) throw new InvalidDataException("Too many archive members to confirm an edit commit.");
        var matches = archive.Entries.Where(e => ArchivePaths.Normalize(e.FullName) == record.MemberPath).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].FullName.EndsWith('/')) throw new IOException("The committed archive member is missing or ambiguous; the previous edit baseline is kept.");
        var member = matches[0];
        if (copy is not null && (member.Crc32 != copy.Crc32 || member.Length != copy.Length) || !baseline.MatchesForMutation(record.ArchivePath))
            throw new IOException("The archive changed after the commit; the previous edit baseline is kept.");
        var updated = record with
        {
            Baseline = baseline,
            BaseSha256 = committedSha256,
            MemberCrc32 = member.Crc32,
            MemberLength = member.Length,
            LastCommitUtc = DateTime.UtcNow,
        };
        return Save(updated);
    }

    public void Discard(EditSessionRecord record)
    {
        RefuseElevatedSession();
        if (!record.IsSupported) throw new NotSupportedException("Unsupported edit-session directories are kept for manual recovery.");
        if(record.StorageRootIdentity.Length!=0)
            throw new NotSupportedException("This edit needs native cleanup ownership before it can be discarded. Its working files are kept.");
        // The target is this store's legacy root and a validated id, so a native root's existence changes nothing about
        // it: an edit made before the native store was listed stays discardable, exactly as it was before the listing.
        // The removal is the legacy store's own (R21: EditPrivateCleanup): on Windows through verified directory
        // handles bound to what FileCat made; on Unix, which has no conditional unlink of a held inode, by pathname
        // inside a folder only this user can change (UNIX_PRIVATE_CLEANUP_DECISION.md). A native record is refused
        // above, because its removal is exact and belongs to the native store.
        if (EditPrivateCleanup.DeleteOwnedFolder(fs, SessionDirectory(record)) is { } kept) throw new IOException(kept);
    }

    public string SessionDirectory(EditSessionRecord record)
    {
        if (string.IsNullOrEmpty(record.Id) || record.Id is "." or ".." || Path.GetFileName(record.Id) != record.Id ||
            record.Id.IndexOfAny(['/', '\\']) >= 0)
            throw new IOException("The edit session does not name an owned session directory.");
        if(!record.IsSupported&&nativeRoot!=Root)
        {
            string nativeSession=Path.Combine(nativeRoot,record.Id);
            string recovered=record.WorkingPath;
            if(recovered==nativeSession||recovered.StartsWith(nativeSession+Path.DirectorySeparatorChar,StringComparison.Ordinal))
                return nativeSession;
        }
        return Path.Combine(record.StorageRootIdentity.Length!=0?nativeRoot:Root, record.Id);
    }

    private static void RefuseElevatedSession()
    {
        if (WindowsTokenAuthority.IsHighIntegrity)
            throw new IOException("FileCat does not read or write user-profile edit sessions while running as administrator. Reopen it without elevation.");
    }

    public static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

}
