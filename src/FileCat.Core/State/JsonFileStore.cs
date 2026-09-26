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
    public static T Load<T>(string path, JsonTypeInfo<T> typeInfo, int currentSchema, Func<T> defaults, out StateLoadStatus status)
        where T : class, IVersionedState
    {
        if (!File.Exists(path) && !File.Exists(path + ".bak"))
        {
            status = StateLoadStatus.Missing;
            return defaults();
        }
        if (TryRead(path, typeInfo, out var value))
        {
            status = value!.SchemaVersion > currentSchema ? StateLoadStatus.NewerSchemaReadOnly : StateLoadStatus.Loaded;
            return value;
        }
        if (TryRead(path + ".bak", typeInfo, out var backup))
        {
            PreserveCorrupt(path);
            status = backup!.SchemaVersion > currentSchema ? StateLoadStatus.NewerSchemaReadOnly : StateLoadStatus.RecoveredFromBackup;
            return backup;
        }
        PreserveCorrupt(path);
        status = StateLoadStatus.CorruptUsingDefaults;
        return defaults();
    }

    private static bool TryRead<T>(string path, JsonTypeInfo<T> typeInfo, out T? value) where T : class
    {
        value = null;
        try
        {
            if (!File.Exists(path)) return false;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            value = JsonSerializer.Deserialize(fs, typeInfo);
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
            if (File.Exists(path)) File.Copy(path, $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}", overwrite: false);
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
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(fs, value, typeInfo);
            fs.Flush(flushToDisk: true);
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
