using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Resources;
using Microsoft.Win32;

namespace FileCat.Platform.Windows;

public sealed record RegistryTreeEntry(string RelativePath, bool IsLink, string ValueHash);

public sealed record RegistryTreeSnapshot(IReadOnlyList<RegistryTreeEntry> Keys, int ValueCount, long DataBytes, int LinkCount)
{
    public int KeyCount => Keys.Count;
}

/// <summary>Bounded, link-refusing subtree preflight. Fingerprints detect changed values before destructive steps.</summary>
public static class RegistryTree
{
    public const int MaxKeys = 100_000;
    public const int MaxValues = 250_000;
    public const long MaxDataBytes = 256L * 1024 * 1024;

    public static string Digest(RegistryTreeSnapshot snapshot)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in snapshot.Keys)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry.RelativePath));
            hash.AppendData([entry.IsLink ? (byte)1 : (byte)0]);
            hash.AppendData(Encoding.ASCII.GetBytes(entry.ValueHash));
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public static RegistryTreeSnapshot Scan(Location root, CancellationToken ct = default)
    {
        if (root.Scheme != Schemes.Registry || root.Path.Length == 0) throw new ArgumentException("A Registry key is required.");
        var entries = new List<RegistryTreeEntry>();
        var pending = new Stack<string>();
        pending.Push("");
        int values = 0, links = 0;
        long bytes = 0;
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            if (entries.Count >= MaxKeys) throw new IOException($"Registry subtree exceeds {MaxKeys:N0} keys.");
            var relative = pending.Pop();
            var location = relative.Length == 0 ? root : root.WithPath(root.Path + "\\" + relative);
            var parent = location.Path[..location.Path.LastIndexOf('\\')];
            var name = location.Path[(location.Path.LastIndexOf('\\') + 1)..];
            using var parentKey = WindowsRegistryProvider.Open(location.WithPath(parent), false);
            var link = RegistryRaw.LinkTarget(parentKey, name);
            if (link is not null)
            {
                links++;
                entries.Add(new RegistryTreeEntry(relative, true, ""));
                continue;
            }
            using var key = WindowsRegistryProvider.Open(location, false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var valueName in RegistryRaw.ValueNames(key).Take(MaxValues - values + 1).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (values >= MaxValues) throw new IOException($"Registry subtree exceeds {MaxValues:N0} values.");
                ct.ThrowIfCancellationRequested();
                var value = RegistryRaw.Read(key, valueName);
                if (value.Data.Length != value.Length) throw new IOException("A Registry value is too large for guarded subtree work.");
                bytes = checked(bytes + value.Length);
                if (bytes > MaxDataBytes) throw new IOException($"Registry subtree exceeds {MaxDataBytes / (1024 * 1024)} MiB of data.");
                values++;
                hash.AppendData(Encoding.UTF8.GetBytes(valueName));
                hash.AppendData([0]);
                hash.AppendData(BitConverter.GetBytes(value.Type));
                hash.AppendData(BitConverter.GetBytes(value.Length));
                hash.AppendData(value.Data);
            }
            entries.Add(new RegistryTreeEntry(relative, false, Convert.ToHexString(hash.GetHashAndReset())));
            foreach (var child in RegistryRaw.SubKeyNames(key).Take(MaxKeys - entries.Count + 1).OrderDescending(StringComparer.OrdinalIgnoreCase))
                pending.Push(relative.Length == 0 ? child : relative + "\\" + child);
            if (pending.Count + entries.Count > MaxKeys)
                throw new IOException($"Registry subtree exceeds {MaxKeys:N0} keys.");
        }
        return new RegistryTreeSnapshot(entries, values, bytes, links);
    }

    public static string ValueHash(Location location)
    {
        using var key = WindowsRegistryProvider.Open(location, false);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int count = 0;
        foreach (var valueName in RegistryRaw.ValueNames(key).Take(MaxValues + 1).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (++count > MaxValues) throw new IOException($"Registry key exceeds {MaxValues:N0} values.");
            var value = RegistryRaw.Read(key, valueName);
            if (value.Data.Length != value.Length) throw new IOException("A Registry value is too large to compare.");
            hash.AppendData(Encoding.UTF8.GetBytes(valueName));
            hash.AppendData([0]);
            hash.AppendData(BitConverter.GetBytes(value.Type));
            hash.AppendData(BitConverter.GetBytes(value.Length));
            hash.AppendData(value.Data);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
