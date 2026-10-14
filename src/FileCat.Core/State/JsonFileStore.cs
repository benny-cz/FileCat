using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

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
        if (TryRead(path, typeInfo, out var value, out bool primaryOversized))
        {
            status = value!.SchemaVersion > currentSchema ? StateLoadStatus.NewerSchemaReadOnly : StateLoadStatus.Loaded;
            return value;
        }
        bool backupLoaded = TryRead(path + ".bak", typeInfo, out var backup, out bool backupOversized);
        if (primaryOversized || backupOversized)
        {
            // An oversized state might be valid or from a newer release. Never move or overwrite it automatically.
            status = StateLoadStatus.OversizedReadOnly;
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

    private static bool TryRead<T>(string path, JsonTypeInfo<T> typeInfo, out T? value, out bool oversized) where T : class
    {
        value = null;
        oversized = false;
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length > MaxStateBytes) { oversized = true; return false; }
            byte[] bytes = new byte[checked((int)fs.Length)];
            fs.ReadExactly(bytes);
            if (fs.ReadByte() != -1) { oversized = true; return false; }
            using var bounded = new MemoryStream(bytes, writable: false);
            value = JsonSerializer.Deserialize(bounded, typeInfo);
            return value is not null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static void PreserveCorrupt(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
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
        var tmp = path + ".tmp";
        long writtenBytes;
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(fs, value, typeInfo);
            writtenBytes = fs.Length;
            if (writtenBytes <= MaxStateBytes) fs.Flush(flushToDisk: true);
        }
        if (writtenBytes > MaxStateBytes)
        {
            File.Delete(tmp);
            throw new IOException($"State exceeds the {MaxStateBytes}-byte read and write limit.");
        }
        if (File.Exists(path))
        {
            try
            {
                File.Replace(tmp, path, path + ".bak", ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }
            File.Copy(path, path + ".bak", overwrite: true);
        }
        File.Move(tmp, path, overwrite: true);
    }
}
