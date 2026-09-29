namespace FileCat.Core.HiddenData;

/// <summary>What kind of data a file carries beside its contents (D-55).</summary>
public enum HiddenKind
{
    /// <summary>An NTFS or ReFS alternate data stream.</summary>
    Stream,
    /// <summary>An extended attribute (Linux namespaces, macOS attributes).</summary>
    Attribute,
    /// <summary>An NTFS extended attribute (EA), kept in the file's $EA attribute.</summary>
    NtfsAttribute,
    /// <summary>A macOS resource fork.</summary>
    ResourceFork,
}

/// <summary>One stream, attribute, or fork of a file.</summary>
/// <param name="Name">Its name as the system names it (a stream without the ":$DATA" suffix).</param>
/// <param name="Size">Its size in bytes, or -1 when unknown.</param>
public sealed record HiddenItem(string Name, HiddenKind Kind, long Size)
{
    /// <summary>The Linux namespace ("user", "security", "trusted", "system"), or null.</summary>
    public string? Namespace => Kind == HiddenKind.Attribute && Name.IndexOf('.') is > 0 and var dot && !OperatingSystem.IsMacOS() ? Name[..dot] : null;

    public string KindText => Kind switch
    {
        HiddenKind.Stream => "Stream",
        HiddenKind.NtfsAttribute => "NTFS attribute",
        HiddenKind.ResourceFork => "Resource fork",
        _ => "Attribute",
    };
}

/// <summary>
/// The data a file carries beside its contents, as this system keeps it (D-55): NTFS alternate data streams and
/// extended attributes on Windows, extended attributes on Linux and macOS (with macOS resource forks). Links are
/// never followed: a link's own attributes are its own.
/// </summary>
public interface IHiddenData
{
    /// <summary>Whether this system keeps such data at all.</summary>
    bool IsSupported { get; }

    /// <summary>Everything the file carries beside its contents (the unnamed main stream not included).</summary>
    IReadOnlyList<HiddenItem> List(string path);

    /// <summary>At most <paramref name="max"/> bytes of an item, from its start.</summary>
    byte[] Read(string path, HiddenItem item, int max);

    /// <summary>The whole item for reading (a stream may be gigabytes).</summary>
    Stream Open(string path, HiddenItem item);

    void Delete(string path, HiddenItem item);
}

/// <summary>What the systems and browsers write on every download: common, so not what a search for hidden data wants.</summary>
public static class DownloadMarks
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Zone.Identifier", "SmartScreen", "com.apple.quarantine", "com.apple.metadata:kMDItemWhereFroms",
        "com.apple.metadata:kMDItemDownloadedDate", "user.xdg.origin.url", "user.xdg.referrer.url",
    };

    public static bool IsDownloadMark(string name) => Names.Contains(name);
}

/// <summary>A system without such data.</summary>
public sealed class NoHiddenData : IHiddenData
{
    public bool IsSupported => false;
    public IReadOnlyList<HiddenItem> List(string path) => [];
    public byte[] Read(string path, HiddenItem item, int max) => throw new FileNotFoundException(item.Name);
    public Stream Open(string path, HiddenItem item) => throw new FileNotFoundException(item.Name);
    public void Delete(string path, HiddenItem item) => throw new FileNotFoundException(item.Name);
}
