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
        0x80000005 => new(tag, ReparseKind.Special, "Drive Extender"),
        0x8000000B => new(tag, ReparseKind.Special, "Filter Manager test tag"),
        0xA0000010 => new(tag, ReparseKind.Special, "IIS cache"),
        0xC0000014 => new(tag, ReparseKind.Special, "Unused AppX stream tag"),
        0x80000016 => new(tag, ReparseKind.Special, "Dynamic File filter"),
        _ => new(tag, ReparseKind.Unknown, "Reparse point · unknown type"),
    };

    /// <summary>
    /// The name Microsoft gives a tag it publishes (the Windows SDK's winnt.h and ntifs.h, MS-FSCC 2.1.2.1); null for
    /// any other tag, third-party ones included.
    /// </summary>
    public static string? TagName(uint tag) => tag switch
    {
        0xA0000003 => "IO_REPARSE_TAG_MOUNT_POINT",
        0xC0000004 => "IO_REPARSE_TAG_HSM",
        0x80000005 => "IO_REPARSE_TAG_DRIVE_EXTENDER",
        0x80000006 => "IO_REPARSE_TAG_HSM2",
        0x80000007 => "IO_REPARSE_TAG_SIS",
        0x80000008 => "IO_REPARSE_TAG_WIM",
        0x80000009 => "IO_REPARSE_TAG_CSV",
        0x8000000A => "IO_REPARSE_TAG_DFS",
        0x8000000B => "IO_REPARSE_TAG_FILTER_MANAGER",
        0xA000000C => "IO_REPARSE_TAG_SYMLINK",
        0xA0000010 => "IO_REPARSE_TAG_IIS_CACHE",
        0x80000012 => "IO_REPARSE_TAG_DFSR",
        0x80000013 => "IO_REPARSE_TAG_DEDUP",
        0xC0000014 => "IO_REPARSE_TAG_APPXSTRM",
        0x80000014 => "IO_REPARSE_TAG_NFS",
        0x80000015 => "IO_REPARSE_TAG_FILE_PLACEHOLDER",
        0x80000016 => "IO_REPARSE_TAG_DFM",
        0x80000017 => "IO_REPARSE_TAG_WOF",
        0x80000018 => "IO_REPARSE_TAG_WCI",
        0x90001018 => "IO_REPARSE_TAG_WCI_1",
        0xA0000019 => "IO_REPARSE_TAG_GLOBAL_REPARSE",
        0x9000001A => "IO_REPARSE_TAG_CLOUD",
        _ when IsCloudTag(tag) => $"IO_REPARSE_TAG_CLOUD_{(tag >> 12) & 0xF:X}",
        0x8000001B => "IO_REPARSE_TAG_APPEXECLINK",
        0x9000001C => "IO_REPARSE_TAG_PROJFS",
        0xA000001D => "IO_REPARSE_TAG_LX_SYMLINK",
        0x8000001E => "IO_REPARSE_TAG_STORAGE_SYNC",
        0xA000001F => "IO_REPARSE_TAG_WCI_TOMBSTONE",
        0x80000020 => "IO_REPARSE_TAG_UNHANDLED",
        0x80000021 => "IO_REPARSE_TAG_ONEDRIVE",
        0xA0000022 => "IO_REPARSE_TAG_PROJFS_TOMBSTONE",
        0x80000023 => "IO_REPARSE_TAG_AF_UNIX",
        0x80000024 => "IO_REPARSE_TAG_LX_FIFO",
        0x80000025 => "IO_REPARSE_TAG_LX_CHR",
        0x80000026 => "IO_REPARSE_TAG_LX_BLK",
        0x90000027 => "IO_REPARSE_TAG_STORAGE_SYNC_FOLDER",
        0xA0000027 => "IO_REPARSE_TAG_WCI_LINK",
        0xA0001027 => "IO_REPARSE_TAG_WCI_LINK_1",
        0xA0000028 => "IO_REPARSE_TAG_DATALESS_CIM",
        _ => null,
    };

    /// <summary>
    /// What a tag means in plain words, a line each: the meaning, the tag's number and published name ("Tag 0x… · IO_…"),
    /// then what the number's flag bits say. What the Reparse type column explains for a value; a tag FileCat does not
    /// know is still described by its bits.
    /// </summary>
    public static string Explain(uint tag)
    {
        string code = TagName(tag) is { } name ? $"Tag 0x{tag:X8} · {name}" : $"Tag 0x{tag:X8}";
        if (tag == 0) return Meaning(tag) + "\n" + code;
        var bits = new List<string>(3) { (tag & 0x80000000) != 0 ? "A Microsoft tag" : "A third-party tag" };
        if ((tag & 0x20000000) != 0) bits.Add("name surrogate: it stands for another item");
        if ((tag & 0x10000000) != 0) bits.Add("a folder with this tag can have contents");
        return Meaning(tag) + "\n" + code + "\n" + string.Join(" · ", bits);
    }

    private static string Meaning(uint tag) => tag switch
    {
        0 => "A reparse point whose tag could not be read.",
        ReparseLinks.SymbolicLinkTag => "A symbolic link: Windows follows the path stored in it to another file or folder, which can be on another drive or computer.",
        ReparseLinks.MountPointTag => "A junction or volume mount point: opening this folder opens another folder, or another drive's root, in its place. The tag alone does not distinguish a junction from a volume mount; Link target shows the stored path.",
        ReparseLinks.WslSymbolicLinkTag => "A Linux symbolic link made in WSL: Linux programs follow it; Windows does not treat it as a Windows link.",
        ReparseLinks.AppExecutionAliasTag => "An app execution alias: running it starts an app installed from a package (Microsoft Store or MSIX).",
        0x80000017 => "A file whose data the Windows Overlay Filter (WOF) supplies: compressed with compact.exe /EXE or CompactOS, or backed by a WIM image (WIMBoot).",
        _ when IsCloudTag(tag) => "A Cloud Files item that a sync app such as OneDrive manages: its data can stay online until it is opened.",
        0x80000023 => "A Unix-domain socket (AF_UNIX): an address a program listens on, not stored file data.",
        0x80000024 => "A Linux named pipe (FIFO) made in WSL.",
        0x80000025 => "A Linux character device file made in WSL.",
        0x80000026 => "A Linux block device file made in WSL.",
        0xC0000004 or 0x80000006 => "Hierarchical storage management, an obsolete product: data moved to slower storage and brought back when read.",
        0x80000005 => "Drive Extender, from Windows Home Server.",
        0x80000007 => "Single-instance storage (SIS): identical files kept as one stored copy.",
        0x80000008 => "An item of a WIM image mounted through the WIM Mount filter (for example with DISM).",
        0x80000009 => "Cluster Shared Volumes version 1, from Windows Server 2008 R2 (obsolete).",
        0x8000000A => "A DFS namespace link: clients that open it are referred to the shared folder it names.",
        0x8000000B => "Used by the Filter Manager test harness.",
        0xA0000010 => "Used by the Internet Information Services (IIS) cache.",
        0x80000012 => "Used by DFS Replication.",
        0x80000013 => "A file optimized by Data Deduplication: its data is kept in the volume's chunk store.",
        0xC0000014 => "An AppX stream tag that Microsoft lists as unused.",
        0x80000014 => "Used by the Windows NFS component, for example for a link or special file made over NFS.",
        0x80000015 => "An obsolete placeholder file from Windows 8.1's OneDrive integration.",
        0x80000016 => "Used by the Dynamic File filter.",
        0x80000018 or 0x90001018 or 0xA000001F or 0xA0000027 or 0xA0001027 or 0x80000020 =>
            "Used by Windows Container Isolation: files, links and tombstones of a Windows container's layered file system.",
        0xA0000019 => "A named-pipe link from a Windows container (server silo) into the host.",
        0x9000001C => "A Projected File System placeholder: a provider app (such as VFS for Git) supplies its contents when it is first used.",
        0xA0000022 => "A Projected File System tombstone: marks a projected item that was deleted here.",
        0x8000001E => "Azure File Sync: a file whose data can be tiered to an Azure file share and recalled when read.",
        0x90000027 => "Azure File Sync, on a folder.",
        0x80000021 => "A OneDrive tag that Microsoft lists as unused.",
        0xA0000028 => "Microsoft lists this tag in the Windows SDK without describing its use.",
        _ => (tag & 0x80000000) != 0
            ? "A Microsoft tag that FileCat does not know."
            : "A tag that FileCat does not know, set by a third-party driver or app.",
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
