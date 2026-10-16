using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using FileCat.Core.FileSystem;

namespace FileCat.Core.State;

public enum StateLoadStatus
{
    /// <summary>Loaded the primary file.</summary>
    Loaded,
    /// <summary>No file yet; defaults are used.</summary>
    Missing,
    /// <summary>The primary file was unreadable; the last-known-good copy was used.</summary>
    RecoveredFromBackup,
    /// <summary>Both copies were unreadable; defaults are used and the corrupt file was preserved.</summary>
    CorruptUsingDefaults,
    /// <summary>Written by a newer FileCat; loaded read-only so it is never overwritten (plan §19.1).</summary>
    NewerSchemaReadOnly,
    /// <summary>Too large to read safely; defaults or a backup are used without replacing the original.</summary>
    OversizedReadOnly,
    /// <summary>Unreadable or non-regular input; defaults or a backup are used without replacing the original.</summary>
    UnavailableReadOnly,
}

public interface IVersionedState
{
    int SchemaVersion { get; set; }
}

/// <summary>
/// Versioned JSON persistence with atomic replacement and a last-known-good copy. A corrupt file is
/// kept aside instead of being overwritten, and a newer schema opens read-only.
/// </summary>
public static class JsonFileStore
{
    private const int MaxStateBytes = 8 * 1024 * 1024;

    public static T Load<T>(string path, JsonTypeInfo<T> typeInfo, int currentSchema, Func<T> defaults, out StateLoadStatus status)
        where T : class, IVersionedState
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak"))
        {
            status = StateLoadStatus.Missing;
            return defaults();
        }
        if (TryRead(path, typeInfo, out var value, out bool primaryOversized, out bool primaryUnavailable))
        {
            status = value!.SchemaVersion > currentSchema ? StateLoadStatus.NewerSchemaReadOnly : StateLoadStatus.Loaded;
            return value;
        }
        bool backupLoaded = TryRead(path + ".bak", typeInfo, out var backup, out bool backupOversized, out bool backupUnavailable);
        if (primaryUnavailable || backupUnavailable || primaryOversized || backupOversized)
        {
            // A state we cannot safely read may be valid or from a newer release. Never overwrite it automatically.
            status = primaryUnavailable || backupUnavailable ? StateLoadStatus.UnavailableReadOnly : StateLoadStatus.OversizedReadOnly;
            return backupLoaded ? backup! : defaults();
        }
        if (backupLoaded)
        {
            PreserveCorrupt(path);
            status = backup!.SchemaVersion > currentSchema ? StateLoadStatus.NewerSchemaReadOnly : StateLoadStatus.RecoveredFromBackup;
            return backup;
        }
        PreserveCorrupt(path);
        status = StateLoadStatus.CorruptUsingDefaults;
        return defaults();
    }

    private static bool TryRead<T>(string path, JsonTypeInfo<T> typeInfo, out T? value, out bool oversized,
        out bool unavailable) where T : class
    {
        value = null;
        oversized = false;
        unavailable = false;
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = OperatingSystem.IsWindows()
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                : new FileStream(UnixFiles.OpenRegularFileForRead(path), FileAccess.Read);
            if (fs.Length > MaxStateBytes) { oversized = true; return false; }
            byte[] bytes = new byte[checked((int)fs.Length)];
            fs.ReadExactly(bytes);
            if (fs.ReadByte() != -1) { oversized = true; return false; }
            using var bounded = new MemoryStream(bytes, writable: false);
            value = JsonSerializer.Deserialize(bounded, typeInfo);
            return value is not null;
        }
        catch (JsonException) { return false; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            unavailable = true;
            return false;
        }
    }

    private static void PreserveCorrupt(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var source = OperatingSystem.IsWindows()
                ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
                : new FileStream(UnixFiles.OpenRegularFileForRead(path), FileAccess.Read);
            if (source.Length > MaxStateBytes) return;
            byte[] bytes = new byte[checked((int)source.Length)];
            source.ReadExactly(bytes);
            if (source.ReadByte() != -1) return;
            using var copy = new FileStream($"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", FileMode.CreateNew, FileAccess.Write);
            copy.Write(bytes);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Writes to a temporary sibling, flushes, then replaces the target keeping the previous version as .bak.</summary>
    public static void Save<T>(string path, T value, JsonTypeInfo<T> typeInfo)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        if (!OperatingSystem.IsWindows())
        {
            // A same-user replacement must not turn the save/backup path into a blocking FIFO or device.
            foreach (string existing in new[] { path, path + ".bak" })
                if (UnixFiles.Stat(existing) is { IsRegular: false })
                    throw new IOException("The state or backup path is not a regular file.");
        }
        // Unix needs a fresh exclusive sibling so an old crash-left .tmp FIFO cannot block a save.
        // Keep the existing Windows temporary path and overwrite behavior.
        var tmp = OperatingSystem.IsWindows() ? path + ".tmp" : path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = OperatingSystem.IsWindows() ? FileMode.Create : FileMode.CreateNew,
                Access = FileAccess.Write, Share = FileShare.None,
                BufferSize = 4096, Options = FileOptions.WriteThrough,
            };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            long writtenBytes;
            using (var fs = new FileStream(tmp, options))
            {
                JsonSerializer.Serialize(fs, value, typeInfo);
                writtenBytes = fs.Length;
                if (writtenBytes <= MaxStateBytes) fs.Flush(flushToDisk: true);
            }
            if (writtenBytes > MaxStateBytes)
                throw new IOException($"State exceeds the {MaxStateBytes}-byte read and write limit.");
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
                    return;
                }
                catch (PlatformNotSupportedException) { }
                catch (IOException) { }
                if (OperatingSystem.IsWindows()) File.Copy(path, path + ".bak", overwrite: true);
                else
                {
                    // A failed Replace fallback still must not open a replaced special-file pathname.
                    using var source = new FileStream(UnixFiles.OpenRegularFileForRead(path), FileAccess.Read);
                    string backupTemp = path + ".bak.tmp-" + Guid.NewGuid().ToString("N");
                    try
                    {
                        using (var backup = new FileStream(backupTemp, options))
                        {
                            source.CopyTo(backup);
                            backup.Flush(flushToDisk: true);
                        }
                        File.Move(backupTemp, path + ".bak", overwrite: true);
                    }
                    finally { if (File.Exists(backupTemp)) File.Delete(backupTemp); }
                }
            }
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }
}
