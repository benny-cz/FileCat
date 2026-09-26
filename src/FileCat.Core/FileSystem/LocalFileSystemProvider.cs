using System.IO.Enumeration;
using FileCat.Core.Resources;

namespace FileCat.Core.FileSystem;

/// <summary>Detects files FileCat can enter as containers (ZIP in v1).</summary>
public interface IContainerDetector
{
    bool IsContainer(string fileName);
    Location? GetContainerLocation(string filePath);
}

/// <summary>
/// Portable local file-system provider built on the .NET enumeration APIs (which use FindFirstFileEx
/// on Windows). Enumeration never follows links and never reads file content.
/// </summary>
public class LocalFileSystemProvider : ResourceProvider
{
    private const int BatchSize = 512;

    private static readonly EnumerationOptions ListOptions = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
        MatchType = MatchType.Simple,
        BufferSize = 64 * 1024,
    };

    public override string Scheme => Schemes.FileSystem;

    public IContainerDetector? ContainerDetector { get; set; }

    public override string GetDisplayPath(Location location) => location.Path;

    public override string GetDisplayName(Location location)
    {
        var t = Path.TrimEndingDirectorySeparator(location.Path);
        var name = Path.GetFileName(t);
        return string.IsNullOrEmpty(name) ? location.Path : name;
    }

    public override Location? GetParent(Location location)
    {
        var path = location.Path;
        if (PathUtil.IsUncPath(path))
        {
            if (PathUtil.IsUncServerRoot(path)) return null;
            if (PathUtil.IsUncShareRoot(path)) return new Location(Schemes.Network, PathUtil.GetUncServer(path)!);
        }
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var parent = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(parent)) return new Location(Schemes.Computer, string.Empty);
        return Location.FileSystem(parent);
    }

    public override string? GetNameInParent(Location location)
    {
        var path = location.Path;
        if (PathUtil.IsUncShareRoot(path))
        {
            var t = path.TrimEnd('\\', '/');
            return t[(t.LastIndexOfAny(['\\', '/']) + 1)..];
        }
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrEmpty(name)) return name;
        // A drive root is listed as "C:" in the computer location.
        return PathUtil.IsWindows ? trimmed.TrimEnd('\\', '/') : trimmed;
    }

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(location.Path);

    public override LocationCapabilities GetCapabilities(Location location) =>
        LocationCapabilities.Enumerate | LocationCapabilities.CreateDirectory | LocationCapabilities.CreateFile |
        LocationCapabilities.Delete | LocationCapabilities.Rename | LocationCapabilities.TransferTarget |
        LocationCapabilities.ReadContent | LocationCapabilities.Watch | LocationCapabilities.ExternalEdit |
        LocationCapabilities.MoveSource | (CanRecycle(location) ? LocationCapabilities.Recycle : 0);

    /// <summary>Whether the platform offers a trash/recycle service for this location.</summary>
    protected virtual bool CanRecycle(Location location) => false;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) =>
        capability == LocationCapabilities.Recycle
            ? "The Recycle Bin is not available here (network shares and most removable drives have none). Items can only be deleted permanently."
            : base.ExplainUnavailable(location, capability);

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        // Runs synchronously on the calling (per-device I/O) thread.
        var detector = ContainerDetector;
        var batch = new EntryData[BatchSize];
        int n = 0;
        var enumerable = new FileSystemEnumerable<EntryData>(location.Path, ToEntry, ListOptions);
        using var e = enumerable.GetEnumerator();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            bool moved;
            try
            {
                moved = e.MoveNext();
            }
            catch (IOException) when (n > 0)
            {
                sink.AddBatch(batch.AsSpan(0, n));
                throw;
            }
            if (!moved) break;
            var entry = e.Current;
            if (detector is not null && entry.Kind == EntryKind.File && detector.IsContainer(entry.Name))
                entry.Flags |= EntryFlags.Container;
            batch[n++] = entry;
            if (n == BatchSize)
            {
                sink.AddBatch(batch);
                n = 0;
            }
        }
        if (n > 0) sink.AddBatch(batch.AsSpan(0, n));
        return Task.CompletedTask;
    }

    internal static EntryData ToEntry(ref FileSystemEntry fe)
    {
        var attrs = fe.Attributes;
        var e = new EntryData
        {
            Name = fe.FileName.ToString(),
            Kind = fe.IsDirectory ? EntryKind.Directory : EntryKind.File,
            Size = fe.IsDirectory ? -1 : fe.Length,
            Modified = fe.LastWriteTimeUtc.UtcTicks,
            Created = fe.CreationTimeUtc.UtcTicks,
            Attributes = (uint)attrs,
            Flags = MapFlags(attrs),
        };
        return e;
    }

    public static EntryFlags MapFlags(FileAttributes attrs)
    {
        var f = EntryFlags.None;
        if ((attrs & FileAttributes.Hidden) != 0) f |= EntryFlags.Hidden;
        if ((attrs & FileAttributes.System) != 0) f |= EntryFlags.System;
        if ((attrs & FileAttributes.ReadOnly) != 0) f |= EntryFlags.ReadOnly;
        if ((attrs & FileAttributes.ReparsePoint) != 0) f |= EntryFlags.Link;
        // FILE_ATTRIBUTE_RECALL_ON_OPEN (0x40000) and RECALL_ON_DATA_ACCESS (0x400000) mark cloud placeholders.
        if ((attrs & FileAttributes.Offline) != 0 || ((uint)attrs & 0x440000u) != 0) f |= EntryFlags.Offline;
        if ((attrs & FileAttributes.Encrypted) != 0) f |= EntryFlags.Encrypted;
        if ((attrs & FileAttributes.Compressed) != 0) f |= EntryFlags.Compressed;
        if ((attrs & FileAttributes.SparseFile) != 0) f |= EntryFlags.Sparse;
        if ((attrs & FileAttributes.Temporary) != 0) f |= EntryFlags.Temporary;
        return f;
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        switch (entry.Kind)
        {
            case EntryKind.Parent:
                return GetParent(parent);
            case EntryKind.Directory:
                return Location.FileSystem(Path.Join(parent.Path, entry.Name));
            case EntryKind.File when entry.Has(EntryFlags.Container) && ContainerDetector is not null:
                return ContainerDetector.GetContainerLocation(Path.Join(parent.Path, entry.Name));
            default:
                return null;
        }
    }

    public override IContentSource? OpenContent(ItemRef item) =>
        item.FileSystemPath is { } p && !item.IsContainer ? new FileContentSource(p) : null;

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        var t = PathUtil.ExpandUserInput(text);
        if (t.Length == 0) return false;
        if (PathUtil.IsWindows && PathUtil.IsUncServerRoot(t))
        {
            location = new Location(Schemes.Network, t.TrimEnd('\\', '/'));
            return true;
        }
        // "C:" alone means the drive root, not the drive's current directory.
        if (PathUtil.IsWindows && t.Length == 2 && t[1] == ':' && char.IsAsciiLetter(t[0])) t += "\\";
        try
        {
            if (Path.IsPathFullyQualified(t))
            {
                location = Location.FileSystem(NormalizeUserPath(Path.GetFullPath(t)));
                return true;
            }
            if (current is { IsFileSystem: true } && !Path.IsPathRooted(t))
            {
                location = Location.FileSystem(NormalizeUserPath(Path.GetFullPath(Path.Combine(current.Path, t))));
                return true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
        return false;
    }

    /// <summary>Keeps roots with their separator ("C:\", "/") and trims it elsewhere.</summary>
    public static string NormalizeUserPath(string full)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(full);
        return string.IsNullOrEmpty(Path.GetFileName(trimmed)) && !PathUtil.IsUncShareRoot(trimmed) ? full : trimmed;
    }

    public override bool IsSameLocation(Location a, Location b) =>
        a.Scheme == b.Scheme && a.Scheme == Schemes.FileSystem
            ? PathUtil.NormalizeForCompare(a.Path).Equals(PathUtil.NormalizeForCompare(b.Path), PathUtil.SafetyComparison)
            : a.Equals(b);
}
