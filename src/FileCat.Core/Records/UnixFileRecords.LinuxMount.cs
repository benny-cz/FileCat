using System.Text;
using FileCat.Core.Inspect;

namespace FileCat.Core.Records;

public sealed partial class UnixFileRecords
{
    private const ulong StatxAttributeMountRoot = 0x2000; // STATX_ATTR_MOUNT_ROOT (Linux 5.8)

    /// <summary>
    /// A mount as a /proc/self/mountinfo line describes it, its fields decoded: its device (major:minor), its root within the
    /// source file system ("/" for the whole of it, a folder for a bind mount or a Btrfs subvolume, a name such as
    /// "mnt:[4026532817]" where the file system has no folders), where it is mounted, its type and source, and its options
    /// (the mount's, then the file system's, as mountinfo writes them).
    /// </summary>
    internal sealed record LinuxMount(string DeviceNumber, string Root, string MountPoint, string Type, string Source, string Options);

    /// <summary>
    /// A mountinfo line split at its spaces (ID, parent ID, major:minor, root, mount point, mount options, optional fields
    /// such as "shared:1", "-", type, source, super options), or null when it is not one.
    /// </summary>
    internal static LinuxMount? ParseLinuxMount(string[] fields)
    {
        int separator = Array.IndexOf(fields, "-");
        if (separator < 6 || separator + 4 != fields.Length || fields[separator + 1].Length == 0) return null;
        return new LinuxMount(fields[2], UnescapeMountField(fields[3]), UnescapeMountField(fields[4]),
            UnescapeMountField(fields[separator + 1]), UnescapeMountField(fields[separator + 2]), fields[5] + "; " + fields[separator + 3]);
    }

    /// <summary>
    /// mountinfo writes a space, tab, newline or backslash in a path as a backslash and three octal digits ("\040", and
    /// "\134" for a backslash itself); this turns such escapes back into their characters and keeps everything else.
    /// </summary>
    internal static string UnescapeMountField(string field)
    {
        if (!field.Contains('\\')) return field;
        static bool Octal(char c) => c is >= '0' and <= '7';
        var text = new StringBuilder(field.Length);
        for (int i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 3 < field.Length && Octal(field[i + 1]) && Octal(field[i + 2]) && Octal(field[i + 3]))
            {
                // The kernel escapes only ASCII; a byte above it would be half of a UTF-8 sequence, not a character.
                int code = (field[i + 1] - '0') * 64 + (field[i + 2] - '0') * 8 + (field[i + 3] - '0');
                if (code < 0x80)
                {
                    text.Append((char)code);
                    i += 3;
                    continue;
                }
            }
            text.Append(field[i]);
        }
        return text.ToString();
    }

    /// <summary>
    /// Whether the item is the root of its mount: statx says so where the kernel reports it (Linux 5.8 and later), so an
    /// item reached through a symbolic link counts too; before that, whether its path is the mount point.
    /// </summary>
    internal static bool IsLinuxMountRoot(ulong attributes, ulong supported, string path, LinuxMount mount) =>
        (supported & StatxAttributeMountRoot) != 0 ? (attributes & StatxAttributeMountRoot) != 0 : path == mount.MountPoint;

    /// <summary>
    /// I612: the folder of its source file system that a mount shows, said of the mount's own root when that is not the
    /// whole source (a bind mount, a Btrfs subvolume). Null otherwise. The words assume nothing about who made the mount.
    /// </summary>
    internal static string? LinuxSourceRoot(LinuxMount mount, bool mountRoot) => mountRoot && mount.Root != "/" ? mount.Root : null;

    /// <summary>The Volume section's fields, for any item on the mount: its root within the source is "/" for the whole of it.</summary>
    internal static List<(string, string)> LinuxVolumeFields(LinuxMount mount) =>
    [
        ("Mounted at", RecordText.Visible(mount.MountPoint)),
        ("File system", RecordText.Visible(mount.Type)),
        ("Source", RecordText.Visible(mount.Source)),
        ("Root within source", RecordText.Visible(mount.Root)),
        ("Options", RecordText.Visible(mount.Options)),
    ];

    /// <summary>
    /// The held item's mount in its details (I612, I662): the source root in the Item section of both tiers when the item is
    /// a mount's root, and the Volume section in the full record.
    /// </summary>
    internal static FileDetailsReport AddLinuxMount(FileDetailsReport report, LinuxMount mount, bool mountRoot, bool fullReport, string source)
    {
        if (LinuxSourceRoot(mount, mountRoot) is { } root)
            report = report with { Summary = report.Summary with { SourceRoot = ObservedFact<string>.Known(root, FactScope.Volume, source) } };
        if (!fullReport) return report;
        var section = new InspectionSection("Volume", [.. LinuxVolumeFields(mount), ("Observation", source)]);
        return report with { Technical = report.Technical with { Sections = [.. report.Technical.Sections, section] } };
    }
}
