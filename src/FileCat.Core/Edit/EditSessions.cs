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
public enum EditState { Unchanged, Modified, Missing }

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
        string? originMark)
    {
        if (revision.Length > MaxMemberBytes)
            throw new IOException($"Files over {MaxMemberBytes / (1024 * 1024 * 1024)} GiB are not edited through sessions; copy the file with F5 instead.");
        string name = remotePath[(remotePath.LastIndexOf('/') + 1)..];
        return CreateSession(name, working => WriteWorkingCopy(source, working), working =>
        {
            if (originMark is not null) fs.WriteOriginMark(working, originMark);
            return new EditSessionRecord
            {
                Kind = EditSessionRecord.RemoteKind,
                ProfileId = profileId,
                ServerDisplay = serverDisplay,
                RemotePath = remotePath,
                RemoteBaseline = revision,
                BaseSha256 = Hash(working),
                WorkingPath = working,
            };
        });
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
    private EditSessionRecord CreateSession(string fileName, Action<string> write, Func<string, EditSessionRecord> describe)
    {
        string id = Guid.NewGuid().ToString("N");
        string dir = Path.Combine(Root, id);
        Directory.CreateDirectory(dir);
        string working = Path.Combine(dir, SafeNames.Validate(fileName) is null ? fileName : "file" + Path.GetExtension(fileName));
        try
        {
            write(working);
            var record = describe(working) with { Id = id, CreatedUtc = DateTime.UtcNow };
            Save(record);
            return record;
        }
        catch
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (IOException) { }
            throw;
        }
    }

    private static void WriteWorkingCopy(IContentSource source, string working)
    {
        using var output = new FileStream(working, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 1024];
        for (long offset = 0; ;)
        {
            int n = source.Read(offset, buffer);
            if (n <= 0) break;
            output.Write(buffer, 0, n);
            offset += n;
        }
    }

    /// <summary>
    /// Extracts <paramref name="member"/> into a new private working copy. The copy carries the archive's download mark,
    /// so programs that open it apply the same checks as for the archive.
    /// </summary>
    public EditSessionRecord Create(ZipProvider zip, ItemRef member)
    {
        if (member.Parent.Scheme != Schemes.Zip || member.Parent.Container is not { IsFileSystem: true } archiveFile)
            throw new NotSupportedException("Edit sessions work on members of archives in folders.");
        string archive = Path.GetFullPath(archiveFile.Path);
        string memberPath = member.Parent.Path.Length == 0 ? member.Name : member.Parent.Path + "/" + member.Name;
        var baseline = ArchiveBaseline.Of(archive);
        return CreateSession(member.Name, working =>
        {
            using var source = Content.ProgressiveContent.Sequential(zip.OpenContent(member)) ?? throw new NotSupportedException("This member is encrypted and cannot be edited here.");
            if (source.Length > MaxMemberBytes)
                throw new IOException($"Members over {MaxMemberBytes / (1024 * 1024 * 1024)} GiB are not edited through sessions; extract it with F5 instead.");
            WriteWorkingCopy(source, working);
        }, working =>
        {
            if (fs.ReadOriginMark(archive) is { } mark) fs.WriteOriginMark(working, mark);
            var (crc, length) = Checksum(working);
            return new EditSessionRecord
            {
                ArchivePath = archive,
                MemberPath = memberPath,
                Baseline = baseline,
                MemberCrc32 = crc,
                MemberLength = length,
                BaseSha256 = Hash(working),
                WorkingPath = working,
            };
        });
    }

    public void Save(EditSessionRecord record)
    {
        string dir = Path.Combine(Root, record.Id);
        string temp = Path.Combine(dir, RecordFile + ".tmp");
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(record, Json));
        File.Move(temp, Path.Combine(dir, RecordFile), overwrite: true);
    }

    public EditState StateOf(EditSessionRecord record)
    {
        if (!File.Exists(record.WorkingPath)) return EditState.Missing;
        try { return Hash(record.WorkingPath) == record.BaseSha256 ? EditState.Unchanged : EditState.Modified; }
        catch (IOException) { return EditState.Modified; } // an editor holding the file: treat as changed, check again on commit
    }

    /// <summary>Whether a commit would replace exactly the member the edit started from.</summary>
    public CommitCheck Check(EditSessionRecord record)
    {
        if (!File.Exists(record.ArchivePath)) return CommitCheck.ArchiveMissing;
        if (record.Baseline.Matches(record.ArchivePath)) return CommitCheck.Ready;
        try
        {
            using var archive = System.IO.Compression.ZipFile.OpenRead(record.ArchivePath);
            var matches = archive.Entries.Where(e => ArchivePaths.Normalize(e.FullName) == record.MemberPath).ToList();
            if (matches.Count != 1) return CommitCheck.MemberChanged;
            return matches[0].Crc32 == record.MemberCrc32 && matches[0].Length == record.MemberLength ? CommitCheck.ArchiveChanged : CommitCheck.MemberChanged;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { return CommitCheck.ArchiveMissing; }
    }

    /// <summary>The update that writes the working copy back; <paramref name="rebase"/> accepts the archive's current version.</summary>
    public ArchivePlan CommitPlan(EditSessionRecord record, bool rebase) =>
        new(record.ArchivePath, rebase ? ArchiveBaseline.Of(record.ArchivePath) : record.Baseline,
            [new ArchiveChange(ArchiveChangeKind.Replace, record.MemberPath, record.WorkingPath)]);

    /// <summary>After a successful commit: the archive's new version and the committed content become the base.</summary>
    public EditSessionRecord Committed(EditSessionRecord record, string committedSha256)
    {
        var (crc, length) = Checksum(record.WorkingPath);
        var updated = record with
        {
            Baseline = ArchiveBaseline.Of(record.ArchivePath),
            BaseSha256 = committedSha256,
            MemberCrc32 = crc,
            MemberLength = length,
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

    private static (uint Crc, long Length) Checksum(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        uint crc = 0;
        long length = 0;
        int n;
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            crc = Crc32.Append(crc, buffer.AsSpan(0, n));
            length += n;
        }
        return (crc, length);
    }
}
