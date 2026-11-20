using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.FileSystem;

/// <summary>Observed type only. This taxonomy grants no traversal or mutation permission.</summary>
public enum ReparseKind { None, Unavailable, Unknown, SymbolicLink, MountPoint, WslLink, AppAlias, Cloud, Wof, Special }

public sealed record ReparseDescription(uint Tag, ReparseKind Kind, string Label)
{
    public bool IsNameSurrogate => (Tag & 0x20000000) != 0;
    public bool HasLinkPresentation => Kind is ReparseKind.SymbolicLink or ReparseKind.MountPoint or ReparseKind.WslLink;
}

/// <summary>Stored names are never executable commands. Target existence has not been checked.</summary>
public sealed record StoredLinkTarget(string SubstituteName, string PrintName, bool Relative);

public static class ReparsePresentation
{
    public static bool IsCloudTag(uint tag) => (tag & 0xFFFF0FFF) == 0x9000001A;

    public static ReparseDescription Describe(uint tag) => tag switch
    {
        0 => new(tag, ReparseKind.Unavailable, "Reparse point · type unavailable"),
        ReparseLinks.SymbolicLinkTag => new(tag, ReparseKind.SymbolicLink, "Symbolic link"),
        ReparseLinks.MountPointTag => new(tag, ReparseKind.MountPoint, "Junction or volume mount"),
        ReparseLinks.WslSymbolicLinkTag => new(tag, ReparseKind.WslLink, "WSL symbolic link"),
        ReparseLinks.AppExecutionAliasTag => new(tag, ReparseKind.AppAlias, "App execution alias"),
        0x80000017 => new(tag, ReparseKind.Wof, "WOF-backed file"),
        _ when IsCloudTag(tag) => new(tag, ReparseKind.Cloud, "Cloud-managed item"),
        0x80000023 => new(tag, ReparseKind.Special, "Unix-domain socket"),
        0x80000024 => new(tag, ReparseKind.Special, "WSL FIFO"),
        0x80000025 => new(tag, ReparseKind.Special, "WSL character device"),
        0x80000026 => new(tag, ReparseKind.Special, "WSL block device"),
        0xC0000004 or 0x80000006 => new(tag, ReparseKind.Special, "Hierarchical storage reparse point"),
        0x80000007 => new(tag, ReparseKind.Special, "Single-instance storage"),
        0x80000008 => new(tag, ReparseKind.Special, "WIM image mount"),
        0x80000009 => new(tag, ReparseKind.Special, "Cluster shared volume"),
        0x8000000A or 0x80000012 => new(tag, ReparseKind.Special, "DFS reparse point"),
        0x80000013 => new(tag, ReparseKind.Special, "Data Deduplication"),
        0x80000014 => new(tag, ReparseKind.Special, "NFS reparse point"),
        0x80000015 or 0x80000021 => new(tag, ReparseKind.Special, "Provider placeholder"),
        0x80000018 or 0x90001018 or 0xA000001F or 0xA0000027 or 0xA0001027 => new(tag, ReparseKind.Special, "Windows Container Isolation"),
        0xA0000019 => new(tag, ReparseKind.Special, "Global reparse point"),
        0x9000001C or 0xA0000022 => new(tag, ReparseKind.Special, "Projected File System reparse point"),
        0x8000001E or 0x90000027 => new(tag, ReparseKind.Special, "Azure File Sync"),
        0x80000020 => new(tag, ReparseKind.Special, "Unhandled reparse point"),
        0xA0000028 => new(tag, ReparseKind.Special, "Dataless CIM"),
        _ => new(tag, ReparseKind.Unknown, "Reparse point · unknown type"),
    };

    /// <summary>Parses the data portion, excluding the 8-byte reparse header; malformed UTF-16 is refused.</summary>
    public static StoredLinkTarget? ParseLink(uint tag, ReadOnlySpan<byte> data)
    {
        int header = tag switch { ReparseLinks.SymbolicLinkTag => 12, ReparseLinks.MountPointTag => 8, _ => 0 };
        if (header == 0 || data.Length < header || data.Length > 16 * 1024 - 8) return null;
        uint flags = header == 12 ? BinaryPrimitives.ReadUInt32LittleEndian(data[8..]) : 0;
        if ((flags & ~1u) != 0) return null;
        try
        {
            string? sub = Name(data, header, 0), print = Name(data, header, 4);
            return sub is null || print is null || sub.Length == 0 ? null : new(sub, print, (flags & 1) != 0);
        }
        catch (DecoderFallbackException) { return null; }
    }

    private static string? Name(ReadOnlySpan<byte> data, int header, int field)
    {
        int offset = BinaryPrimitives.ReadUInt16LittleEndian(data[field..]);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(data[(field + 2)..]);
        if (((offset | length) & 1) != 0 || offset > data.Length - header || length > data.Length - header - offset) return null;
        string name = new UnicodeEncoding(false, false, true).GetString(data.Slice(header + offset, length));
        return name.Contains('\0') ? null : name;
    }
}

/// <summary>Shared, cached-only row presentation. Provider descriptions retain ownership.</summary>
public static class EntryPresentation
{
    public static bool IsWindowsFileSystem(in EntryData entry, Location? location = null)
    {
        var origin = (entry.Tag as ResultTag)?.Parent ?? location;
        return OperatingSystem.IsWindows() && (origin is null || origin.IsFileSystem);
    }

    public static bool IsLink(in EntryData entry, Location? location = null)
    {
        var origin = (entry.Tag as ResultTag)?.Parent ?? location;
        if ((origin is null || origin.IsFileSystem) && (entry.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            return OperatingSystem.IsWindows() ? ReparsePresentation.Describe(entry.NativeReparseTag).HasLinkPresentation : true;
        return entry.Has(EntryFlags.Link);
    }

    public static string Kind(in EntryData entry, Location? location = null) => entry.Tag is IDisplayDetails details
        ? details.KindText
        : IsWindowsFileSystem(entry, location) && (entry.Attributes & (uint)FileAttributes.ReparsePoint) != 0
            ? ReparsePresentation.Describe(entry.NativeReparseTag).Label
            : IsLink(entry, location) ? "Symbolic link" : string.Empty;
}
