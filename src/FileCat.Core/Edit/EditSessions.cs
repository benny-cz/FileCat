using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Edit;

/// <summary>
/// One external edit of an archive member or a file on a server (plan §14.2, NET-003): its origin and version
/// evidence, a private working copy, and what was last written back. It survives navigation, tab closing, and
/// restarts; only an explicit Commit writes back and only Discard deletes the working copy.
/// </summary>
public sealed record EditSessionRecord
{
    public const int CurrentVersion = 1;
    public const string ArchiveKind = "archive";
    public const string RemoteKind = "sftp";
    public int Version { get; init; } = CurrentVersion;
    public string Id { get; init; } = "";
    /// <summary><see cref="ArchiveKind"/> (the default of records written before servers had sessions) or <see cref="RemoteKind"/>.</summary>
    public string Kind { get; init; } = ArchiveKind;
    public DateTime CreatedUtc { get; init; }
    public string ArchivePath { get; init; } = "";
    /// <summary>For a server file: the connection profile, its display ("user@host"), and the absolute remote path.</summary>
    public string ProfileId { get; init; } = "";
    public string ServerDisplay { get; init; } = "";
    public string RemotePath { get; init; } = "";
    /// <summary>For a server file: the revision the working copy is based on (updated after each commit).</summary>
    public ContentRevision RemoteBaseline { get; init; }

    public bool IsRemote => Kind == RemoteKind;

    /// <summary>The edited file's own name.</summary>
    public string DisplayName => IsRemote ? RemotePath[(RemotePath.LastIndexOf('/') + 1)..] : Path.GetFileName(MemberPath);

    /// <summary>Where it lives: the member in its archive, or the server and path.</summary>
    public string DisplayContainer => IsRemote ? $"{ServerDisplay}:{RemotePath}" : $"{MemberPath} in {ArchivePath}";

    /// <summary>What a commit writes to: the archive's file name, or the server.</summary>
    public string DisplayTarget => IsRemote ? ServerDisplay : Path.GetFileName(ArchivePath);
    public string MemberPath { get; init; } = "";
    /// <summary>The archive version the working copy is based on (updated after each commit).</summary>
    public ArchiveBaseline Baseline { get; init; } = new(0, 0);
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
}

/// <summary>The archive version reviewed before an explicit overwrite/rebase decision.</summary>
public sealed record EditCommitReview(CommitCheck Check, ArchiveBaseline? Baseline);

/// <summary>Owned commit bytes, independent of subsequent editor saves. Dispose only after the job has returned.</summary>
public sealed class EditCommitCopy : IDisposable
{
    private FileStream? _pin;
    public string Path { get; }
    public string Sha256 { get; }
    public uint Crc32 { get; }
    public long Length { get; }
    internal EditCommitCopy(string path, string sha256, uint crc32, long length)
    {
        Path = path; Sha256 = sha256; Crc32 = crc32; Length = length;
        _pin = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    public void Dispose()
    {
        Interlocked.Exchange(ref _pin, null)?.Dispose();
        // This GUID folder contains only this owned snapshot; the published working copy is elsewhere.
        if (File.Exists(Path)) File.Delete(Path);
        string directory = System.IO.Path.GetDirectoryName(Path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory);
    }
}

/// <summary>
/// Protected session records and working copies under the user's local data (one folder per session, so the editor sees
/// the member's own file name). Temporary copies are removed only on Discard; deletion is not secure erasure.
/// </summary>
public sealed class EditSessionStore(string root, IFileSystemOperations fs)
{
    public const long MaxMemberBytes = 1L * 1024 * 1024 * 1024;
    private const string RecordFile = "session.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public string Root { get; } = root;

    public IReadOnlyList<EditSessionRecord> LoadAll()
    {
        if (!Directory.Exists(Root)) return [];
        var sessions = new List<EditSessionRecord>();
        foreach (var dir in Directory.EnumerateDirectories(Root))
        {
            try
            {
                var record = JsonSerializer.Deserialize<EditSessionRecord>(File.ReadAllBytes(Path.Combine(dir, RecordFile)), Json);
                if (record is { Version: EditSessionRecord.CurrentVersion } && record.Id == Path.GetFileName(dir)) sessions.Add(record);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return sessions.OrderBy(s => s.CreatedUtc).ToList();
    }

    public EditSessionRecord? Find(string archivePath, string memberPath) =>
        LoadAll().FirstOrDefault(s => !s.IsRemote && string.Equals(s.ArchivePath, Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase) &&
                                      s.MemberPath == memberPath);

    public EditSessionRecord? FindRemote(string profileId, string remotePath) =>
        LoadAll().FirstOrDefault(s => s.IsRemote && s.ProfileId == profileId && s.RemotePath == remotePath);

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
        return CreateSession(name, working => WriteWorkingCopy(source, working, ct, revision), (working, copy) =>
        {
            if (originMark is not null) fs.WriteOriginMark(working, originMark);
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
        Save(updated);
        return updated;
    }

    /// <summary>A session folder with the working copy under the edited file's own name (or a safe stand-in).</summary>
    private sealed record WorkingCopy(string Sha256, uint Crc, long Length);

    private EditSessionRecord CreateSession(string fileName, Func<string, WorkingCopy> write, Func<string, WorkingCopy, EditSessionRecord> describe, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string id = Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Root, id);
        Directory.CreateDirectory(dir);
        string working = Path.Combine(dir, SafeNames.Validate(fileName) is null ? fileName : "file" + Path.GetExtension(fileName));
        try
        {
            var copy = write(working);
            ct.ThrowIfCancellationRequested();
            var record = describe(working, copy) with { Id = id, CreatedUtc = DateTime.UtcNow };
            ct.ThrowIfCancellationRequested();
            Save(record);
            return record;
        }
        catch
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void ValidateLength(long length)
    {
        if (length < 0) throw new IOException("The file's complete length is unavailable; copy it with F5 instead of starting an edit session.");
        if (length > MaxMemberBytes) throw new IOException("Files over 1 GiB are not edited through sessions; copy the file with F5 instead.");
    }

    private static void CheckComplete(IContentSource source)
    {
        if (source is IPartialContent partial && (partial.MissingRanges.Count > 0 || partial.Caveat is not null))
            throw new IOException("This source contains lost or uncertain bytes and cannot be used as an edit-session baseline.");
    }

    /// <summary>The caller owns the source. Nothing is published unless exact bytes, EOF and available revisions agree.</summary>
    private static WorkingCopy WriteWorkingCopy(IContentSource source, string working, CancellationToken ct, ContentRevision? expected = null, Action? check = null)
    {
        void Check() { ct.ThrowIfCancellationRequested(); check?.Invoke(); }
        Check();
        long length = source.Length; ValidateLength(length); CheckComplete(source);
        var before = source.GetRevision();
        Check();
        if (before is { } revision && revision.Length != length || expected is { } pinned && before != pinned)
            throw new IOException("The file changed before its edit could be copied.");
        using var output = new FileStream(working, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        uint crc = 0; long offset = 0;
        while (true)
        {
            Check(); CheckComplete(source);
            // A one-byte read at the declared end checks actual EOF (and archive checksum validation) without
            // accepting or writing bytes beyond either the declared size or the edit-session size limit.
            int want = (int)Math.Min(buffer.Length, length - offset + 1);
            int n = source.Read(offset, buffer.AsSpan(0, want));
            Check(); CheckComplete(source);
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
        return new WorkingCopy(Convert.ToHexString(hash.GetHashAndReset()), crc, offset);
    }

    /// <summary>
    /// Extracts <paramref name="member"/> into a new private working copy. The copy carries the archive's download mark,
    /// so programs that open it apply the same checks as for the archive.
    /// </summary>
    public EditSessionRecord Create(ResourceProvider zip, ItemRef member, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (member.Parent.Scheme != Schemes.Zip || member.Parent.Container is not { IsFileSystem: true } archiveFile)
            throw new NotSupportedException("Edit sessions work on members of archives in folders.");
        string archive = Path.GetFullPath(archiveFile.Path);
        string memberPath = member.Parent.Path.Length == 0 ? member.Name : member.Parent.Path + "/" + member.Name;
        var baseline = ArchiveBaseline.Of(archive);
        uint expectedCrc; long expectedLength;
        using (var catalog = System.IO.Compression.ZipFile.OpenRead(archive))
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
        if (!baseline.Matches(archive)) throw new IOException("The archive changed while its edit was being prepared.");
        return CreateSession(member.Name, working =>
        {
            ct.ThrowIfCancellationRequested();
            using var source = Content.ProgressiveContent.Sequential(zip.OpenContent(member)) ?? throw new NotSupportedException("This member is encrypted and cannot be edited here.");
            var copy = WriteWorkingCopy(source, working, ct);
            if (copy.Length != expectedLength || copy.Crc != expectedCrc || !baseline.Matches(archive))
                throw new IOException("The archive member changed or is damaged; no edit session was created.");
            return copy;
        }, (working, copy) =>
        {
            if (fs.ReadOriginMark(archive) is { } mark) fs.WriteOriginMark(working, mark);
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
    }

    public void Save(EditSessionRecord record)
    {
        string dir = Path.Combine(Root, record.Id);
        string temp = Path.Combine(dir, RecordFile + ".tmp");
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(record, Json));
        File.Move(temp, Path.Combine(dir, RecordFile), overwrite: true);
    }

    public EditState StateOf(EditSessionRecord record) => StateOf(record, new LocalFileSystemProvider());

    /// <summary>Only a complete bounded read establishes whether the working copy matches its base.</summary>
    public EditState StateOf(EditSessionRecord record, Resources.ResourceProvider provider, Action? check = null) => ReviewWorking(record, provider, check).State;

    public EditWorkingReview ReviewWorking(EditSessionRecord record, Resources.ResourceProvider provider, Action? check = null)
    {
        void Check() => check?.Invoke();
        try
        {
            Check();
            using var source = Content.ProgressiveContent.Sequential(provider.OpenContent(ItemRef.ForFileSystemPath(record.WorkingPath, EntryKind.File)));
            Check();
            if (source is null) return new(EditState.Unavailable, null);
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
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { Check(); return new(EditState.Missing, null); }
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
            var baseline = ArchiveBaseline.Of(record.ArchivePath);
            if (baseline == record.Baseline) return new(CommitCheck.Ready, baseline);
            using var archive = System.IO.Compression.ZipFile.OpenRead(record.ArchivePath);
            if (archive.Entries.Count > ZipProvider.MaxEntries) throw new InvalidDataException("Too many archive members to review an edit commit.");
            System.IO.Compression.ZipArchiveEntry? found = null; bool duplicate = false;
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (ArchivePaths.Normalize(entry.FullName) != record.MemberPath) continue;
                duplicate |= found is not null; found = entry;
            }
            if (!baseline.Matches(record.ArchivePath)) throw new IOException("The archive changed while its edit commit was reviewed.");
            var check = !duplicate && found is not null && !found.FullName.EndsWith('/') && found.Crc32 == record.MemberCrc32 && found.Length == record.MemberLength
                ? CommitCheck.ArchiveChanged : CommitCheck.MemberChanged;
            return new(check, baseline);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return new(CommitCheck.ArchiveMissing, null); }
    }

    /// <summary>Capture one complete bounded working file before queueing, with hash/CRC from those same bytes.</summary>
    public EditCommitCopy PrepareCommit(EditSessionRecord record, string tempRoot, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        string directory = Path.Combine(tempRoot, "edit-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Path.GetFileName(record.WorkingPath));
        try
        {
            // Editors that already hold a write handle cause a conservative sharing refusal on Windows.
            // Unix sharing is advisory: length/time checks still apply; this is not a hostile-writer snapshot guarantee.
            using var source = new CommitContent(record.WorkingPath);
            var copy = WriteWorkingCopy(source, path, ct);
            File.SetLastWriteTimeUtc(path, source.ModifiedUtc);
            if (fs.ReadOriginMark(record.WorkingPath) is { } mark) fs.WriteOriginMark(path, mark);
            ct.ThrowIfCancellationRequested();
            return new EditCommitCopy(path, copy.Sha256, copy.Crc, copy.Length);
        }
        catch
        {
            try { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Capture a save-copy source completely on its caller's admitted worker, before any destination replacement.</summary>
    public EditCommitCopy PrepareSaveCopy(EditSessionRecord record, string tempRoot, ResourceProvider provider, Action check)
    {
        check();
        string directory = Path.Combine(tempRoot, "edit-commit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, Path.GetFileName(record.WorkingPath));
        try
        {
            using var source = Content.ProgressiveContent.Sequential(provider.OpenContent(ItemRef.ForFileSystemPath(record.WorkingPath, EntryKind.File)))
                ?? throw new IOException("The working copy cannot be read completely.");
            check();
            var bytes = WriteWorkingCopy(source, path, default, check: check);
            check();
            if (fs.ReadOriginMark(record.WorkingPath) is { } mark) fs.WriteOriginMark(path, mark);
            check(); return new EditCommitCopy(path, bytes.Sha256, bytes.Crc, bytes.Length);
        }
        catch
        {
            try { if (File.Exists(path)) File.Delete(path); Directory.Delete(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    /// <summary>Copy owned bytes into a unique sibling first. Publish only after the native copy returns and demand remains active.</summary>
    public static void PublishSaveCopy(EditCommitCopy copy, string destination, IFileSystemOperations files, Action check)
    {
        check();
        string staged = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(destination))!, ".filecat-edit-copy-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            files.CopyFile(copy.Path, staged, new FileCopyOptions { FlushDestination = true }, null, default);
            check();
            VerifySaveCopy(copy, staged, check);
            check();
            files.Move(staged, destination, replaceExisting: true, writeThrough: true);
            check();
        }
        finally
        {
            // Never remove the destination. Even a failed copy may have created only part of this owned stage.
            if (File.Exists(staged)) files.DeleteFile(staged);
        }
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
        public CommitContent(string path) { _path = path; _stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.RandomAccess); }
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
        new(record.ArchivePath, rebase ? ArchiveBaseline.Of(record.ArchivePath) : record.Baseline,
            [new ArchiveChange(ArchiveChangeKind.Replace, record.MemberPath, record.WorkingPath)]);

    public ArchivePlan CommitPlan(EditSessionRecord record, EditCommitReview review, EditCommitCopy copy) =>
        new(record.ArchivePath, review.Baseline ?? throw new IOException("The reviewed archive is unavailable."),
            [new ArchiveChange(ArchiveChangeKind.Replace, record.MemberPath, copy.Path)]);

    /// <summary>After a successful commit: the archive's new version and the committed content become the base.</summary>
    public EditSessionRecord Committed(EditSessionRecord record, string committedSha256)
    {
        return CommittedArchive(record, committedSha256, null);
    }

    public EditSessionRecord Committed(EditSessionRecord record, EditCommitCopy copy) => CommittedArchive(record, copy.Sha256, copy);

    private EditSessionRecord CommittedArchive(EditSessionRecord record, string committedSha256, EditCommitCopy? copy)
    {
        var baseline = ArchiveBaseline.Of(record.ArchivePath);
        using var archive = System.IO.Compression.ZipFile.OpenRead(record.ArchivePath);
        if (archive.Entries.Count > ZipProvider.MaxEntries) throw new InvalidDataException("Too many archive members to confirm an edit commit.");
        var matches = archive.Entries.Where(e => ArchivePaths.Normalize(e.FullName) == record.MemberPath).Take(2).ToArray();
        if (matches.Length != 1 || matches[0].FullName.EndsWith('/')) throw new IOException("The committed archive member is missing or ambiguous; the previous edit baseline is kept.");
        var member = matches[0];
        if (copy is not null && (member.Crc32 != copy.Crc32 || member.Length != copy.Length) || !baseline.Matches(record.ArchivePath))
            throw new IOException("The archive changed after the commit; the previous edit baseline is kept.");
        var updated = record with
        {
            Baseline = baseline,
            BaseSha256 = committedSha256,
            MemberCrc32 = member.Crc32,
            MemberLength = member.Length,
            LastCommitUtc = DateTime.UtcNow,
        };
        Save(updated);
        return updated;
    }

    public void Discard(EditSessionRecord record) => Directory.Delete(Path.Combine(Root, record.Id), recursive: true);

    public static string Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

}
