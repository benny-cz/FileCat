using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FileCat.Core.State;

/// <summary>Coordinates ordinary Windows settings writers and owns the checked appearance replacement.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSettingsFile(string path)
{
    private const string BackupPattern = ".appearance-*.bak";
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };
    private readonly string _mutexName = @"Global\FileCat-Settings-" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
    private string? _revision;
    private StateLoadStatus _loadStatus;

    public AppSettings Load(out StateLoadStatus status)
    {
        var settings = JsonFileStore.Load(path, StateJsonContext.Default.AppSettings, AppSettings.CurrentSchema,
            () => new AppSettings(), out status, out var primary);
        _revision = Revision(primary);
        _loadStatus = status;
        return settings;
    }

    public AppSettings ReadSaved()
    {
        RequireNormal();
        return ReadSnapshot(path).Settings;
    }

    public void Save(AppSettings settings)
    {
        RequireNormal();
        if (_loadStatus is StateLoadStatus.NewerSchemaReadOnly or StateLoadStatus.OversizedReadOnly or StateLoadStatus.UnavailableReadOnly)
            throw new IOException("The normal settings are read-only.");
        WithWriterLock(() =>
        {
            var current = ReadPrimary();
            CheckRevision(current, _revision);
            var next = SerializeSettings(settings);
            if (_loadStatus == StateLoadStatus.Loaded && current is not null)
            {
                var saved = ParseSnapshot(current);
                var modeled = SerializeSettings(saved.Settings);
                // Apply only changed modeled fields, preserving unrelated data and skipping timer writes
                // when the settings are identical. In particular, never rotate a backup on a no-op.
                if (JsonNode.DeepEquals(modeled, next)) return;
                foreach (var field in modeled)
                    if (!next.ContainsKey(field.Key)) saved.Json.Remove(field.Key);
                foreach (var field in next)
                    if (!modeled.TryGetPropertyValue(field.Key, out var old) || !JsonNode.DeepEquals(old, field.Value))
                        saved.Json[field.Key] = field.Value?.DeepClone();
                next = saved.Json;
            }
            byte[] bytes = Serialize(next);
            JsonFileStore.SaveSettingsBytes(path, bytes, () => Revision(ReadPrimary()) == _revision);
            _revision = Revision(bytes);
            _loadStatus = StateLoadStatus.Loaded;
        });
    }

    public sealed class Review
    {
        internal Review(WindowsSettingsFile owner, string revision, byte[] candidate, AppSettings before, AppSettings after)
        {
            Owner = owner;
            Revision = revision;
            Candidate = candidate;
            Before = before;
            After = after;
            Changes = AppearanceTransferV1.DescribeChanges(before, after);
        }

        internal WindowsSettingsFile Owner { get; }
        internal string Revision { get; }
        internal byte[] Candidate { get; }
        internal AppSettings Before { get; }
        internal AppSettings After { get; }
        public IReadOnlyList<string> Changes { get; }
    }

    public Review ReviewReplacement(AppearanceTransferV1 transfer) => MakeReview(transfer.Patch);

    public Review ReviewUndo()
    {
        RequireNormal();
        string? backup = Directory.EnumerateFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + BackupPattern)
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal).FirstOrDefault();
        if (backup is null) throw new IOException("This profile has no appearance replacement backup.");
        var previous = ReadSnapshot(backup);
        return MakeReview(json => AppearanceTransferV1.RestoreFields(json, previous.Json));
    }

    private Review MakeReview(Action<JsonObject> patch)
    {
        RequireNormal();
        var current = ReadSnapshot(path);
        if (current.Revision != _revision)
            throw new IOException("Saved settings changed. Reopen FileCat to review the current profile.");
        patch(current.Json);
        byte[] candidate = Serialize(current.Json);
        return new Review(this, current.Revision, candidate, current.Settings, ParseSnapshot(candidate).Settings);
    }

    public string ReplaceAppearance(Review review, AppSettings live)
    {
        RequireNormal();
        if (review.Owner != this) throw new IOException("This review is for a different profile window.");
        if (review.Changes.Count == 0) throw new IOException("There are no appearance changes to save.");
        string? retainedBackup = null;
        WithWriterLock(() =>
        {
            byte[]? original = ReadPrimary();
            CheckRevision(original, review.Revision);
            CheckRevision(original, _revision);
            if (original is null) throw new IOException("The saved settings file is missing.");
            var target = new FileInfo(path);
            if ((target.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Encrypted | FileAttributes.ReadOnly)) != 0)
                throw new IOException("This settings file cannot be safely backed up and replaced.");
            var access = target.GetAccessControl(AccessControlSections.Access);
            access.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);

            string backup = path + FormattableString.Invariant($".appearance-{DateTime.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}.bak");
            string stage = path + $".appearance-{Guid.NewGuid():N}.tmp";
            string recovery = backup + ".recovery";
            string backupStage = recovery + ".tmp";
            bool ownsStage = false;
            bool ownsBackupStage = false;
            try
            {
                WriteNew(backupStage, original, access);
                ownsBackupStage = true;
                CheckRevision(ReadBytes(backupStage), review.Revision);
                File.Move(backupStage, recovery);
                ownsBackupStage = false;
                retainedBackup = recovery;
                WriteNew(stage, review.Candidate, access);
                ownsStage = true;
                CheckRevision(ReadPrimary(), review.Revision);
                // Keep the verified backup even if Windows reports a partial replacement failure.
                // An ordinary FileCat save cannot enter this section concurrently.
                File.Replace(stage, path, backup);
                ownsStage = false;
                retainedBackup = backup;
                TryDelete(recovery);
            }
            catch (Exception ex) when (retainedBackup is not null && ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException($"Appearance replacement did not complete. The original settings are in {retainedBackup}. {ex.Message}", ex);
            }
            finally
            {
                if (ownsStage) TryDelete(stage);
                if (ownsBackupStage) TryDelete(backupStage);
            }

            _revision = Revision(review.Candidate);
            _loadStatus = StateLoadStatus.Loaded;
            AppearanceTransferV1.ApplyChanges(live, review.Before, review.After);
        });
        return retainedBackup!;
    }

    private static void WriteNew(string file, byte[] bytes, FileSecurity access)
    {
        using var output = new FileInfo(file).Create(FileMode.CreateNew, FileSystemRights.Write,
            FileShare.None, 4096, FileOptions.WriteThrough, access);
        try
        {
            output.Write(bytes);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            output.Dispose();
            TryDelete(file);
            throw;
        }
    }

    private static void TryDelete(string file)
    {
        try { File.Delete(file); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void RequireNormal()
    {
        if (WindowsTokenAuthority.IsHighIntegrity)
            throw new IOException("Open FileCat normally to access saved appearance settings.");
    }

    private void WithWriterLock(Action action)
    {
        using var mutex = new Mutex(false, _mutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(2))) throw new IOException("Another window is saving this profile. Try again.");
        }
        catch (AbandonedMutexException) { }
        try { action(); }
        finally { mutex.ReleaseMutex(); }
    }

    private byte[]? ReadPrimary()
    {
        try { return ReadBytes(path); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    private static byte[] ReadBytes(string file)
    {
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > JsonFileStore.MaxStateBytes) throw new IOException("The saved settings exceed the safe size limit.");
        var bytes = new byte[(int)input.Length];
        input.ReadExactly(bytes);
        if (input.ReadByte() != -1) throw new IOException("The saved settings changed while being read.");
        return bytes;
    }

    private static (JsonObject Json, AppSettings Settings, string Revision) ReadSnapshot(string file) =>
        ParseSnapshot(ReadBytes(file));

    private static (JsonObject Json, AppSettings Settings, string Revision) ParseSnapshot(byte[] bytes)
    {
        try
        {
            ReadOnlySpan<byte> jsonBytes = bytes;
            if (jsonBytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) jsonBytes = jsonBytes[3..];
            var json = JsonNode.Parse(jsonBytes, documentOptions: ReadOptions) as JsonObject
                ?? throw new IOException("The saved settings are not a JSON object.");
            var settings = json.Deserialize(StateJsonContext.Default.AppSettings)
                ?? throw new IOException("The saved settings are empty.");
            if (settings.SchemaVersion > AppSettings.CurrentSchema)
                throw new IOException("The saved settings were written by a newer FileCat.");
            return (json, settings, Revision(bytes)!);
        }
        catch (JsonException ex) { throw new IOException("The saved settings could not be read.", ex); }
    }

    private static JsonObject SerializeSettings(AppSettings settings) =>
        JsonSerializer.SerializeToNode(settings, StateJsonContext.Default.AppSettings)!.AsObject();

    private static byte[] Serialize(JsonObject json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (bytes.Length > JsonFileStore.MaxStateBytes) throw new IOException("The settings exceed the safe size limit.");
        return bytes;
    }

    private static string? Revision(byte[]? bytes) => bytes is null ? null : Convert.ToHexString(SHA256.HashData(bytes));

    private static void CheckRevision(byte[]? current, string? expected)
    {
        if (Revision(current) != expected)
            throw new IOException("Saved settings changed. Reopen FileCat before saving this profile.");
    }
}
