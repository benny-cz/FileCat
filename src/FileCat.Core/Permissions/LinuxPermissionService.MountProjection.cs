using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Network;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed partial class LinuxPermissionService
{
    internal static PermissionMountObservation? ObserveMountProjection(string fileSystem, string source, string root, string options)
    {
        string[] settings = options.Split(',');
        switch (fileSystem)
        {
            case "vfat":
            case "msdos":
            case "exfat":
                return new(PermissionMountPresentation.Synthetic, PublicMountSource(source), root, null, null, false,
                    "This format presents mount-derived IDs and mode. FileCat cannot edit persistent per-item ownership or ACLs here.");
            case "cifs":
            case "smb3":
                return new(PermissionMountPresentation.Translated,
                    source.StartsWith("//", StringComparison.Ordinal) ? PublicMountSource(source) : null, root,
                    MountProtocol(settings, "SMB"),
                    settings.Contains("noperm") ? "The CIFS client skips its Unix permission check. The server still checks access."
                    : "The CIFS client and server can each enforce access.", true,
                    "FileCat has no verified server ACL writer for this Linux SMB mount.");
            case "nfs":
            case "nfs4":
                return ObserveNfsMount(fileSystem, source, root, settings);
            case "zfs":
                return ObserveZfsMount(source, root, settings);
            case "fuse.gvfsd-fuse":
            case "fuse.sshfs":
                return new(PermissionMountPresentation.Translated, null, root, null,
                    "The userspace mount presents these fields for the mounted connection.", true,
                    "FileCat has no verified server ACL writer through this userspace mount.");
            default:
                return IsFuseMount(fileSystem) ? new(PermissionMountPresentation.Unidentified, null, root, null,
                    "The userspace filesystem determines which permission fields it exposes.", false,
                    "The mounted filesystem's permission model could not be verified.") : null;
        }
    }

    private static bool IsFuseMount(string type) => type is "fuse" or "fuseblk" ||
        type.StartsWith("fuse.", StringComparison.Ordinal) || type.StartsWith("fuseblk.", StringComparison.Ordinal);

    private static string? PublicMountSource(string source) =>
        source is { Length: > 0 and <= 4096 } && source.IndexOfAny(['@', '?', '#']) < 0 && !source.Any(char.IsControl)
            ? source : null;

    private static PermissionMountObservation ObserveZfsMount(string source, string root, string[] settings)
    {
        bool posix = settings.Contains("posixacl");
        bool disabled = settings.Contains("noacl");
        string policy = (posix, disabled) switch
        {
            (true, false) => "The kernel reports POSIX ACLs on this ZFS mount.",
            (false, true) => "The kernel reports ACLs disabled on this ZFS mount.",
            _ => "The kernel did not establish the effective ACL type on this ZFS mount."
        };
        return new(posix != disabled ? PermissionMountPresentation.Native : PermissionMountPresentation.Unidentified,
            PublicMountSource(source), root, null, policy + " Dataset mode and inheritance policies are unverified.",
            false, "FileCat has no verified permission editor for this ZFS mount.");
    }

    private static PermissionMountObservation ObserveNfsMount(string fileSystem, string source, string root, string[] settings)
    {
        string protocol = MountProtocol(settings, "NFS");
        bool versionFour = fileSystem == "nfs4" || protocol == "NFS 4" || protocol.StartsWith("NFS 4.", StringComparison.Ordinal);
        string? security = settings.FirstOrDefault(option => option.StartsWith("sec=", StringComparison.Ordinal));
        string credentials = security switch
        {
            "sec=sys" => "The client uses AUTH_SYS numerical user and group IDs.",
            "sec=krb5" => "The client uses Kerberos authentication.",
            "sec=krb5i" => "The client uses Kerberos authentication and integrity protection.",
            "sec=krb5p" => "The client uses Kerberos authentication and privacy protection.",
            "sec=none" => "The mount reports unauthenticated RPC requests.",
            _ => "The RPC security flavor is unverified."
        };
        return new(PermissionMountPresentation.Translated, source.Contains(":/", StringComparison.Ordinal) ? PublicMountSource(source) : null,
            root, protocol, credentials + " The server's export policy and identity mapping govern access.", true,
            versionFour ? "Full NFSv4 ACL inspection and editing require a verified server adapter."
                : "FileCat has no verified ACL writer for this NFS mount.");
    }

    private static string MountProtocol(string[] settings, string protocol)
    {
        string? version = settings.FirstOrDefault(option => option.StartsWith("vers=", StringComparison.Ordinal));
        return version is { Length: >= 6 and <= 10 } && version[5..].Split('.').All(
            component => component.Length > 0 && component.All(char.IsAsciiDigit)) ? $"{protocol} {version[5..]}" : protocol;
    }

    internal static PermissionMountObservation ProjectionForPath(PermissionMountObservation observation, string fileSystem,
        string mountedAt, string path)
    {
        if (fileSystem != "fuse.gvfsd-fuse") return observation;
        string prefix = mountedAt.EndsWith('/') ? mountedAt : mountedAt + "/";
        string relative = observation.Root != "/" ? observation.Root.TrimStart('/') :
            path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : "";
        string component = relative.Split('/', 2)[0];
        if (SmbTools.ParseGvfsName(component) is not { } share ||
            share.Server.Length == 0 || share.Share.Length == 0 ||
            share.Server.IndexOfAny(['/', '\\']) >= 0 || share.Share.IndexOfAny(['/', '\\']) >= 0 ||
            PublicMountSource($"//{share.Server}/{share.Share}") is not { } source)
            return observation;
        // Several account-specific shares can sit under the same GVfs FUSE mount and display the same server and share.
        return observation with
        {
            Source = source, Protocol = "SMB via GVfs",
            ConnectionKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(component)))
        };
    }

    private static unsafe ulong ReadProjectedMountInstance(SafeFileHandle file, Stamp expected)
    {
        const uint fields = Type | Inode | UniqueMountId;
        byte* data = stackalloc byte[256];
        new Span<byte>(data, 256).Clear();
        if (Statx(file, "", AtEmptyPath | AtStatxDontSync, fields, data) != 0)
            throw Failure("Read the unique projected mount identity", Marshal.GetLastPInvokeError());
        ulong mount = *(ulong*)(data + 144);
        if ((*(uint*)data & fields) != fields || mount == 0)
            throw new ReadFailure(PermissionReadStatus.Incomplete, "statx did not establish the unique mount identity.");
        ulong device = ((ulong)*(uint*)(data + 136) << 32) | *(uint*)(data + 140);
        if (device != expected.Device || *(ulong*)(data + 32) != expected.Inode || (*(ushort*)(data + 28) & 0xf000) != expected.FileKind)
            throw new ReadFailure(PermissionReadStatus.Stale, "The projected object's identity changed while reading the mount.");
        return mount;
    }

    private static PermissionSnapshot ReadProjectedMount(PermissionSubject subject, PermissionBinding binding, Stamp stamp,
        PermissionMountObservation observation, PermissionSections sections)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string authoritySource = observation.IsServerProjection ? "Linux mountinfo and held statx (client cache allowed)"
            : "Linux mountinfo and held statx";
        subject = subject with { LastMountObservation = now };
        var snapshot = PermissionSnapshots.Create(subject, PermissionModel.Projected, binding, sections, now, authoritySource,
            missingReason: "This mount does not expose a verified value for this section.") with
        {
            Mount = observation
        };
        if ((sections & PermissionSections.Owner) != 0)
            snapshot = snapshot with { Owner = Principal(stamp, Uid, stamp.User, PrincipalKind.UnixUser, subject.Authority.Id, now, authoritySource) };
        if ((sections & PermissionSections.Group) != 0)
            snapshot = snapshot with { Group = Principal(stamp, Gid, stamp.Group, PrincipalKind.UnixGroup, subject.Authority.Id, now, authoritySource) };
        if ((sections & PermissionSections.Mode) != 0)
            snapshot = snapshot with { Mode = (stamp.Valid & Mode) != 0
                ? new(PermissionReadStatus.Present, authoritySource, now, (ushort)(stamp.Mode & 0xfff))
                : new(PermissionReadStatus.Incomplete, authoritySource, now, Reason: "statx did not return permission bits.") };
        return snapshot;
    }
}
