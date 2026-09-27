namespace FileCat.Core.Resources;

public enum EntryKind : byte
{
    File = 0,
    Directory = 1,
    /// <summary>The ".." row that navigates to the parent location.</summary>
    Parent = 2,
    Drive = 3,
    Server = 4,
    Share = 5,
    RegistryKey = 6,
    RegistryValue = 7,
}

/// <summary>Provider-owned, safe single-line text for the Kind and Details columns.</summary>
public interface IDisplayDetails
{
    string KindText { get; }
    string DetailsText { get; }
}

[Flags]
public enum EntryFlags : ushort
{
    None = 0,
    Hidden = 1 << 0,
    System = 1 << 1,
    ReadOnly = 1 << 2,
    /// <summary>Reparse point, symbolic link, junction, or Registry link. Never followed implicitly.</summary>
    Link = 1 << 3,
    /// <summary>Cloud placeholder or offline file: reading content may trigger a recall.</summary>
    Offline = 1 << 4,
    Encrypted = 1 << 5,
    Compressed = 1 << 6,
    Sparse = 1 << 7,
    Temporary = 1 << 8,
    /// <summary>Not currently accessible (drive not ready, member not extractable, missing bookmark).</summary>
    Unavailable = 1 << 9,
    /// <summary>A file that FileCat can enter as a container (supported archive).</summary>
    Container = 1 << 10,
    /// <summary>Content protected by encryption the provider cannot open (e.g. encrypted ZIP entry).</summary>
    Protected = 1 << 11,
    /// <summary>Directory size in <see cref="EntryData.Size"/> was computed explicitly.</summary>
    SizeComputed = 1 << 12,
}

/// <summary>
/// Compact node descriptor stored by listings. <see cref="Name"/> is the exact raw name and the only
/// identity-bearing text besides the listing location. Kind also distinguishes typed namespaces
/// (a Registry key and value can have the same name); everything else is presentation metadata.
/// </summary>
public struct EntryData
{
    public string Name;
    public EntryKind Kind;
    public EntryFlags Flags;
    /// <summary>Size in bytes, or -1 when unknown.</summary>
    public long Size;
    /// <summary>Last modification, UTC ticks; 0 when unknown.</summary>
    public long Modified;
    /// <summary>Creation, UTC ticks; 0 when unknown.</summary>
    public long Created;
    /// <summary>Provider-native attribute bits (Windows FILE_ATTRIBUTE_*, POSIX mode...).</summary>
    public uint Attributes;
    /// <summary>Provider-specific typed payload (result provenance, Registry value info...).</summary>
    public object? Tag;

    public EntryData(string name, EntryKind kind, long size = -1, long modified = 0)
    {
        Name = name;
        Kind = kind;
        Size = size;
        Modified = modified;
        Flags = EntryFlags.None;
        Created = 0;
        Attributes = 0;
        Tag = null;
    }

    public readonly bool IsParent => Kind == EntryKind.Parent;

    /// <summary>Entries that navigate rather than open (directories, drives, keys...).</summary>
    public readonly bool IsContainer => Kind is EntryKind.Directory or EntryKind.Parent or EntryKind.Drive
        or EntryKind.Server or EntryKind.Share or EntryKind.RegistryKey;

    public readonly bool Has(EntryFlags flag) => (Flags & flag) != 0;

    public readonly DateTime? ModifiedUtc => Modified > 0 ? new DateTime(Modified, DateTimeKind.Utc) : null;

    public readonly DateTime? CreatedUtc => Created > 0 ? new DateTime(Created, DateTimeKind.Utc) : null;

    /// <summary>Extension without the dot, or empty for containers and names without one.</summary>
    public readonly string Extension => IsContainer ? string.Empty : NameParts.GetExtension(Name);
}

/// <summary>
/// Identity of one item: its container, kind, and exact raw name (PI-10). Size/time are revision
/// evidence captured at selection time and are deliberately excluded from equality.
/// </summary>
public sealed class ItemRef : IEquatable<ItemRef>
{
    public ItemRef(Location parent, string name, EntryKind kind, long size = -1, long modified = 0)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(name);
        Parent = parent;
        Name = name;
        Kind = kind;
        Size = size;
        Modified = modified;
    }

    public Location Parent { get; }
    public string Name { get; }
    public EntryKind Kind { get; }
    public long Size { get; }
    public long Modified { get; }

    /// <summary>Captured presentation/safety flags; not part of identity.</summary>
    public EntryFlags Flags { get; init; }

    /// <summary>Distinguishes items that share a name in one container (duplicate archive entries).</summary>
    public int Ordinal { get; init; }

    /// <summary>Folder relative to a result set's search root (keep-relative-paths copies). Not identity.</summary>
    public string? RelativeFolder { get; init; }

    public bool IsContainer => Kind is EntryKind.Directory or EntryKind.Drive or EntryKind.Share or EntryKind.RegistryKey;

    /// <summary>Full file-system path when the parent is a file-system location, otherwise null.</summary>
    public string? FileSystemPath => Parent.IsFileSystem ? System.IO.Path.Join(Parent.Path, Name) : null;

    public static ItemRef FromEntry(Location parent, in EntryData e) => new(parent, e.Name, e.Kind, e.Size, e.Modified) { Flags = e.Flags };

    public static ItemRef ForFileSystemPath(string fullPath, EntryKind kind)
    {
        var trimmed = System.IO.Path.TrimEndingDirectorySeparator(fullPath);
        var parent = System.IO.Path.GetDirectoryName(trimmed) ?? trimmed;
        return new ItemRef(Location.FileSystem(parent), System.IO.Path.GetFileName(trimmed), kind);
    }

    public bool Equals(ItemRef? other) => other is not null && Kind == other.Kind && Ordinal == other.Ordinal &&
        string.Equals(Name, other.Name, StringComparison.Ordinal) && Parent.Equals(other.Parent);

    public override bool Equals(object? obj) => Equals(obj as ItemRef);

    public override int GetHashCode() => HashCode.Combine(Parent, Name, Kind, Ordinal);

    public override string ToString() => $"{Parent} :: {Name}";
}

/// <summary>Name/extension helpers that never alter the raw name.</summary>
public static class NameParts
{
    /// <summary>Extension without the dot. A leading dot (".gitignore") is not an extension.</summary>
    public static string GetExtension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? string.Empty : name[(dot + 1)..];
    }

    /// <summary>Span form of <see cref="GetExtension(string)"/> (no allocation).</summary>
    public static ReadOnlySpan<char> GetExtension(ReadOnlySpan<char> name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 || dot == name.Length - 1 ? ReadOnlySpan<char>.Empty : name[(dot + 1)..];
    }

    public static string GetStem(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    /// <summary>Span form of <see cref="GetStem(string)"/> (no allocation).</summary>
    public static ReadOnlySpan<char> GetStem(ReadOnlySpan<char> name)
    {
        int dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }
}
