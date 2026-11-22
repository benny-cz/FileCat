using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class LinuxPermissionService(bool qualifyDefaultWrites = false, bool qualifyAccessChecks = false,
    bool qualifyGroupWrites = false, bool qualifyModeTrees = false, bool qualifyAclResets = false,
    bool qualifyAdditionalFileSystems = false, bool qualifyProtectionWrites = false, bool qualifyMountProjections = false) : IPermissionService
{
    private const string Source = "Linux statx", AclSource = "Linux POSIX ACL xattr";
    private const int AtEmptyPath = 0x1000, AtNoAutomount = 0x800, AtStatxDontSync = 0x4000, OPath = 0x200000;
    private const uint Type = 1, Mode = 2, Links = 4, Uid = 8, Gid = 16, ModifyTime = 0x40, ChangeTime = 0x80,
        Inode = 0x100, Size = 0x200, MountId = 0x1000;
    private const uint IdentityFields = Type | Inode | MountId;
    private const uint Fields = IdentityFields | Mode | Links | Uid | Gid | ChangeTime | ModifyTime | Size;
    private const ushort DirectoryType = 0x4000, FileType = 0x8000;
    private const int NoAttribute = 61, Unsupported = 95, Range = 34;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static int PinFlags => OPath | LinuxOpenFlags.CloseOnExec | LinuxOpenFlags.Current.NoFollow;
    private static int DirectoryPinFlags => PinFlags | LinuxOpenFlags.Current.Directory;

    internal bool CanEditAccessAcl(PermissionSnapshot snapshot) => snapshot.Model == PermissionModel.PosixAcl && ModeReviewRefusal(snapshot) is null;

    internal bool CanEditDefaults(PermissionSnapshot snapshot) => qualifyDefaultWrites &&
        snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory && CanEditAccessAcl(snapshot);

    internal bool CanCheckAccess(PermissionSnapshot snapshot) => qualifyAccessChecks &&
        snapshot.Model is PermissionModel.PosixAcl or PermissionModel.UnixMode &&
        snapshot.Binding?.Identity.Kind is PermissionObjectKind.File or PermissionObjectKind.Directory or PermissionObjectKind.VolumeRoot;

    internal bool CanEditGroup(PermissionSnapshot snapshot) => qualifyGroupWrites &&
        snapshot.FileCapabilities?.Status is PermissionReadStatus.Present or PermissionReadStatus.Absent &&
        ModeReviewRefusal(snapshot) is null;

    internal bool CanEditModeTree(PermissionSnapshot snapshot) => qualifyModeTrees &&
        snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory && ModeReviewRefusal(snapshot) is null;

    internal bool CanResetAclTree(PermissionSnapshot snapshot) => qualifyAclResets &&
        snapshot.Binding?.Identity.Kind == PermissionObjectKind.Directory && ModeReviewRefusal(snapshot) is null &&
        snapshot.Defaults is { Status: PermissionReadStatus.Present, Value.Acl: PosixAcl };

    public PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken) =>
        InspectCore(subject, sections, cancellationToken);

    private PermissionInspection InspectCore(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken,
        Stamp[]? treeParent = null, Stamp? treeEntry = null)
    {
        if (qualifyGroupWrites && (sections & PermissionSections.Group) != 0) sections |= PermissionSections.FileCapabilities;
        cancellationToken.ThrowIfCancellationRequested();
        bool allowProjection = qualifyMountProjections && treeParent is null;
        bool fromMountedServer = subject.Authority.Kind == PermissionAuthorityKind.MountedServer;
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            !(PermissionSnapshots.IsLocal(subject) || allowProjection && fromMountedServer && PermissionSnapshots.IsFileSystemSubject(subject)))
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "This reader requires a supported Linux x64 filesystem.");
        string path = subject.RawLocator;
        if (!IsExactPath(path))
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "The original path cannot be bound without normalizing its name.");
        string[] names = path == "/" ? [] : path[1..].TrimEnd('/').Split('/');
        if (names.Length > PermissionSnapshots.MaxPathDepth || names.Any(n => n is "" or "." or ".."))
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Incomplete, "The original path exceeds the binding limits or contains ambiguous components.");

        SafeFileHandle? file = null;
        Mount? mount = null;
        try
        {
            using var proc = OpenOwnProc(out string pid, cancellationToken);
            var mounts = ReadMounts(proc, pid, cancellationToken);
            file = Walk(names, mounts, qualifyAdditionalFileSystems, cancellationToken, out var ancestry, out mount, treeParent, allowProjection);
            Stamp stamp = ancestry[^1];
            if (treeEntry is { } reviewed && reviewed != stamp)
                throw new ReadFailure(PermissionReadStatus.Stale, "The captured directory entry changed during review.");
            PermissionObjectKind kind = stamp.FileKind == FileType ? PermissionObjectKind.File
                : mount.Path == path.TrimEnd('/') || path == "/" ? PermissionObjectKind.VolumeRoot : PermissionObjectKind.Directory;
            var projection = mount.Projection is { } observed ? ProjectionForPath(observed, mount.FileSystem, mount.Path, path) : null;
            ulong mountInstance = projection is not null ? ReadProjectedMountInstance(file, stamp) : stamp.Mount;
            var projectionAuthority = projection?.IsServerProjection == true
                ? new PermissionAuthority(PermissionAuthorityKind.MountedServer,
                    projection.ConnectionKey ?? projection.Source ?? $"{mount.FileSystem} at {mount.Path}", checked((long)mountInstance)) : null;
            if (fromMountedServer && projectionAuthority != subject.Authority)
                throw new ReadFailure(PermissionReadStatus.Stale, "The original server share or mount instance changed.");
            var identity = new PermissionIdentity(new FileIdentity(stamp.Device, $"{stamp.Inode:X16}0000000000000000"),
                mountInstance.ToString(CultureInfo.InvariantCulture), kind, subject.Authority);
            if (!PermissionSnapshots.Matches(subject, identity))
                throw new ReadFailure(PermissionReadStatus.Stale, "The original type, mount or object identity changed.");
            if (!PermissionSnapshots.MatchesListing(subject, stamp.Size, stamp.ModifiedUtcTicks))
                throw new ReadFailure(PermissionReadStatus.Stale, "The file differs from the original listing observation. Refresh the listing and select it again.");

            if (projectionAuthority is not null)
            {
                subject = subject with { Authority = projectionAuthority };
                identity = identity with { Authority = projectionAuthority };
            }
            var binding = new PermissionBinding(identity, mount.FileSystem, mount.Path,
                (stamp.Valid & Links) != 0 ? stamp.LinkCount : null, mount.ReadOnly) { PosixAclDisabled = mount.PosixAclDisabled };
            var snapshot = projection is not null
                ? ReadProjectedMount(subject, binding, stamp, projection, sections)
                : ReadSnapshot(subject, binding, file, stamp, proc, pid, sections, cancellationToken, protectionWrites: qualifyProtectionWrites);
            var currentMounts = ReadMounts(proc, pid, cancellationToken);
            using var current = Walk(names, currentMounts, qualifyAdditionalFileSystems, cancellationToken, out var refreshed, out var currentMount,
                treeParent, allowProjection);
            if (currentMount != mount || !ancestry.Select(s => s.Identity).SequenceEqual(refreshed.Select(s => s.Identity)) || refreshed[^1] != stamp ||
                projection is not null && ReadProjectedMountInstance(current, refreshed[^1]) != mountInstance)
                throw new ReadFailure(PermissionReadStatus.Stale, "The original namespace or security metadata changed during inspection.");
            cancellationToken.ThrowIfCancellationRequested();
            snapshot = PermissionSnapshots.WithCapabilities(snapshot, sections);
            if (projection is not null) return new PermissionInspection(snapshot);
            var result = new PermissionInspection(snapshot,
                new LinuxBoundPermission(subject, binding, file, names, ancestry, qualifyDefaultWrites, qualifyGroupWrites,
                    qualifyAdditionalFileSystems, qualifyProtectionWrites, treeParent));
            file = null;
            return result;
        }
        catch (ReadFailure ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((ex.MountedClient || fromMountedServer || mount?.Projection?.IsServerProjection == true) &&
                ex.Error is 5 or 100 or 101 or 104 or 107 or 110 or 111 or 112 or 113)
                return PermissionSnapshots.Unavailable(subject, sections, ex.Status,
                    $"The mounted client could not retrieve permission metadata. {ex.Message}", Source, ex.Error);
            return PermissionSnapshots.Unavailable(subject, sections, ex.Status, ex.Message, Source, ex.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EntryPointNotFoundException or DllNotFoundException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Failed, ex.Message, Source);
        }
        finally { file?.Dispose(); }
    }

    private static PermissionSnapshot ReadSnapshot(PermissionSubject subject, PermissionBinding binding, SafeFileHandle file,
        Stamp stamp, SafeFileHandle proc, string pid, PermissionSections sections, CancellationToken ct,
        SafeFileHandle? flagsHandle = null, bool protectionWrites = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var snapshot = PermissionSnapshots.Create(subject, binding.PosixAclDisabled ? PermissionModel.UnixMode : PermissionModel.PosixAcl,
            binding with { LinkCount = (stamp.Valid & Links) != 0 ? stamp.LinkCount : null }, sections, now, Source);
        if ((sections & PermissionSections.Owner) != 0)
            snapshot = snapshot with { Owner = Principal(stamp, Uid, stamp.User, PrincipalKind.UnixUser, subject.Authority.Id, now) };
        if ((sections & PermissionSections.Group) != 0)
            snapshot = snapshot with { Group = Principal(stamp, Gid, stamp.Group, PrincipalKind.UnixGroup, subject.Authority.Id, now) };
        if ((sections & PermissionSections.Mode) != 0)
            snapshot = snapshot with { Mode = (stamp.Valid & Mode) != 0 ? new(PermissionReadStatus.Present, Source, now, (ushort)(stamp.Mode & 0xfff))
                : new(PermissionReadStatus.Incomplete, Source, now, Reason: "statx did not return permission bits.") };
        if ((sections & PermissionSections.Times) != 0 && stamp.ModifiedUtcTicks is { } modified)
            snapshot = snapshot with { Times = new(PermissionReadStatus.Present, Source, now, new(new DateTimeOffset(modified, TimeSpan.Zero), null)) };

        bool readAccess = (sections & PermissionSections.Access) != 0;
        bool readDefaults = (sections & PermissionSections.Defaults) != 0 && stamp.FileKind == DirectoryType;
        if (binding.PosixAclDisabled)
        {
            var disabled = new PermissionObservation<PermissionAclValue>(PermissionReadStatus.Unsupported, "Linux mountinfo", now,
                Reason: "This Btrfs filesystem has POSIX ACLs disabled (noacl). Mode bits govern discretionary access.");
            if (readAccess) snapshot = snapshot with { Access = disabled };
            if (readDefaults) snapshot = snapshot with { Defaults = disabled };
        }
        else if (readAccess || readDefaults)
        {
            var acl = ReadAclSections(proc, pid, file, stamp, subject.Authority.Id, readAccess, readDefaults, ct);
            if (readAccess) snapshot = snapshot with { Access = acl.Access };
            if (readDefaults) snapshot = snapshot with { Defaults = acl.Defaults };
        }
        if ((sections & PermissionSections.FileCapabilities) != 0)
            snapshot = snapshot with { FileCapabilities = ReadFileCapabilities(proc, pid, file, stamp, ct) };
        if ((sections & PermissionSections.Flags) != 0)
            snapshot = snapshot with { Flags = ObserveProtection(binding, proc, pid, file, stamp, flagsHandle, protectionWrites, now, ct) };
        return snapshot;
    }

    private static bool IsExactPath(string path)
    {
        if (path.Length is 0 or > 4095 || path[0] != '/' || PathUtil.HasAmbiguousUnixName(path) || path.Contains('\0')) return false;
        try { return StrictUtf8.GetByteCount(path) < 4096; }
        catch (EncoderFallbackException) { return false; }
    }

    private static PermissionObservation<Principal> Principal(Stamp stamp, uint mask, uint id, PrincipalKind kind, string authority,
        DateTimeOffset now, string source = Source) =>
        (stamp.Valid & mask) != 0 && id != uint.MaxValue
            ? new(PermissionReadStatus.Present, source, now, new Principal(kind, id.ToString(CultureInfo.InvariantCulture), authority))
            : new(PermissionReadStatus.Incomplete, source, now, Reason: "statx did not return a representable principal.");

    private static SafeFileHandle Walk(string[] names, Dictionary<ulong, Mount> mounts, bool additionalFileSystems, CancellationToken ct, out Stamp[] ancestry,
        out Mount mount, Stamp[]? treeParent = null, bool allowProjection = false)
    {
        SafeFileHandle? current = OpenHandle(null, "/", DirectoryPinFlags);
        ancestry = new Stamp[names.Length + 1];
        try
        {
            string path = "/";
            ancestry[0] = ReadStamp(current);
            if (treeParent is not null && (treeParent.Length != names.Length || treeParent[0].Identity != ancestry[0].Identity))
                throw new ReadFailure(PermissionReadStatus.Stale, "The captured tree ancestry changed.");
            mount = AdmitMount(current, ancestry[0], mounts, path, additionalFileSystems, allowProjection, ancestor: names.Length != 0);
            bool crossedProjection = !SupportsMount(mount.FileSystem, additionalFileSystems);
            for (int i = 0; i < names.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (i != 0) path += '/';
                path += names[i];
                bool ancestor = i < names.Length - 1;
                if (mounts.Values.Any(candidate => candidate.Path == path && !CanEnterMount(candidate, additionalFileSystems, allowProjection, ancestor)))
                    throw new ReadFailure(PermissionReadStatus.Unsupported, "This path crosses a filesystem outside the qualified Linux permissions route.");
                var parent = current;
                current = treeParent is not null && i == names.Length - 1
                    ? OpenSameMountChild(parent, names[i], PinFlags, "Open the captured tree entry")
                    : OpenHandle(parent, names[i], PinFlags);
                parent.Dispose();
                bool cached = mount.Projection is not null ||
                    allowProjection && mounts.Values.Any(candidate => candidate.Path == path && candidate.Projection is not null);
                Stamp facts = ReadStamp(current, cached: cached);
                if (treeParent is not null && i + 1 < treeParent.Length && treeParent[i + 1].Identity != facts.Identity)
                    throw new ReadFailure(PermissionReadStatus.Stale, "The captured tree ancestry changed.");
                if (facts.FileKind != DirectoryType && (i != names.Length - 1 || facts.FileKind != FileType))
                    throw new ReadFailure(PermissionReadStatus.Unsupported, "A path component is a link or a special file.");
                if (facts.Mount != mount.Id)
                {
                    mount = AdmitMount(current, facts, mounts, path, additionalFileSystems, allowProjection, ancestor);
                    crossedProjection |= !SupportsMount(mount.FileSystem, additionalFileSystems);
                }
                else if (facts.Device != mount.Device)
                    throw new ReadFailure(PermissionReadStatus.Unsupported, "The held object crosses a filesystem or Btrfs subvolume boundary.");
                ancestry[i + 1] = facts;
            }
            if (crossedProjection && mount.Projection is null)
                throw new ReadFailure(PermissionReadStatus.Unsupported, "This path crosses a mount that has no qualified binding for permission changes.");
            SafeFileHandle result = current;
            current = null;
            return result;
        }
        finally { current?.Dispose(); }
    }

    private static Mount AdmitMount(SafeFileHandle handle, Stamp stamp, Dictionary<ulong, Mount> mounts, string path, bool additionalFileSystems,
        bool allowProjection = false, bool ancestor = false)
    {
        if (!mounts.TryGetValue(stamp.Mount, out var mount) || mount.Path != path || mount.FileSystem != "btrfs" && stamp.Device != mount.Device)
            throw new ReadFailure(PermissionReadStatus.Stale, "The mounted filesystem did not match the held path and device.");
        if (!CanEnterMount(mount, additionalFileSystems, allowProjection, ancestor))
            throw new ReadFailure(PermissionReadStatus.Unsupported, "The mounted filesystem is outside the qualified Linux permissions route.");
        ulong flags = VerifiedMountFlags(handle, mount.FileSystem, mountedClient: mount.Projection?.IsServerProjection == true);
        // For Btrfs, mountinfo names the superblock device. statx on the mount root gives its subvolume device.
        return mount with { Device = stamp.Device, ReadOnly = mount.ReadOnly || (flags & 1) != 0 };
    }

    private static bool SupportsMount(string fileSystem, bool additionalFileSystems) =>
        fileSystem == "ext4" || additionalFileSystems && fileSystem is "xfs" or "btrfs";

    private static bool CanEnterMount(Mount mount, bool additionalFileSystems, bool allowProjection, bool ancestor) =>
        SupportsMount(mount.FileSystem, additionalFileSystems) || allowProjection &&
        (mount.Projection is not null || ancestor && mount.FileSystem == "tmpfs");

    private static ulong VerifiedMountFlags(SafeFileHandle handle, string fileSystem, bool mountedClient = false)
    {
        long magic = fileSystem switch
        {
            "ext4" => 0xef53, "xfs" => 0x58465342, "btrfs" => 0x9123683e,
            "vfat" or "msdos" => 0x4d44, "exfat" => 0x2011bab0,
            "nfs" or "nfs4" => 0x6969, "cifs" or "smb3" => 0xff534d42,
            "zfs" => 0x2fc12fc1, "tmpfs" => 0x01021994,
            _ when IsFuseMount(fileSystem) => 0x65735546,
            _ => 0
        };
        byte* filesystem = stackalloc byte[256];
        if (magic == 0)
            throw new ReadFailure(PermissionReadStatus.Unsupported, "This mount has no verified Linux filesystem magic.");
        if (FStatFs(handle, filesystem) != 0)
            throw Failure("Read held filesystem identity", Marshal.GetLastPInvokeError(), mountedClient);
        long actual = *(long*)filesystem;
        if (actual != magic && !(fileSystem is "cifs" or "smb3" && actual == 0xfe534d42))
            throw new ReadFailure(PermissionReadStatus.Stale, "The held filesystem did not corroborate its mount entry.");
        return *(ulong*)(filesystem + 80);
    }

    private static SafeFileHandle OpenOwnProc(out string pid, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var root = OpenHandle(null, "/", DirectoryPinFlags);
        var proc = OpenHandle(root, "proc", DirectoryPinFlags);
        try
        {
            byte* bytes = stackalloc byte[256];
            if (FStatFs(proc, bytes) != 0 || *(long*)bytes != 0x9fa0)
                throw new ReadFailure(PermissionReadStatus.Unsupported, "A verified procfs mount is required for this reader.");
            pid = GetPid().ToString(CultureInfo.InvariantCulture);
            Span<byte> name = stackalloc byte[32];
            nint length;
            fixed (byte* output = name) length = ReadLinkAt(proc, "self", output, (nuint)name.Length);
            if (length != pid.Length || !name[..(int)length].SequenceEqual(Encoding.ASCII.GetBytes(pid)))
                throw new ReadFailure(PermissionReadStatus.Unsupported, "Procfs does not represent this process's PID namespace.");
            return proc;
        }
        catch { proc.Dispose(); throw; }
    }

    private static Dictionary<ulong, Mount> ReadMounts(SafeFileHandle proc, string pid, CancellationToken ct)
    {
        using var process = OpenProcChild(proc, pid, DirectoryPinFlags);
        using var file = OpenProcChild(process, "mountinfo", LinuxOpenFlags.CloseOnExec | LinuxOpenFlags.Current.NoFollow);
        byte[] contents = new byte[64 << 10];
        int count = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            int read = RandomAccess.Read(file, contents.AsSpan(count), count);
            if (read == 0) break;
            count += read;
            if (count > PermissionSnapshots.MaxSectionBytes)
                throw new ReadFailure(PermissionReadStatus.Incomplete, "The mount table exceeds the inspection byte limit.");
            if (count == contents.Length) Array.Resize(ref contents, Math.Min(contents.Length * 2, PermissionSnapshots.MaxSectionBytes + 1));
        }
        var mounts = new Dictionary<ulong, Mount>();
        foreach (var line in Encoding.UTF8.GetString(contents, 0, count).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            ct.ThrowIfCancellationRequested();
            string[] fields = line.Split(' ');
            int separator = Array.IndexOf(fields, "-");
            if (separator < 6 || separator + 3 >= fields.Length || !ulong.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
                throw new ReadFailure(PermissionReadStatus.Failed, "Procfs returned an invalid mount table entry.");
            string[] device = fields[2].Split(':');
            if (device.Length != 2 || !uint.TryParse(device[0], out uint major) || !uint.TryParse(device[1], out uint minor))
                throw new ReadFailure(PermissionReadStatus.Failed, "Procfs returned an invalid mount device number.");
            string path = DecodeMountField(fields[4]);
            string fileSystem = fields[separator + 1];
            string options = fields[separator + 3];
            bool aclDisabled = fileSystem == "btrfs" && options.Split(',').Contains("noacl");
            var projection = ObserveMountProjection(fileSystem, DecodeMountField(fields[separator + 2]), DecodeMountField(fields[3]), options);
            bool readOnly = fields[5].Split(',').Contains("ro") || options.Split(',').Contains("ro");
            if (!mounts.TryAdd(id, new(id, ((ulong)major << 32) | minor, path, fileSystem, readOnly, aclDisabled, projection)))
                throw new ReadFailure(PermissionReadStatus.Failed, "Procfs returned a duplicate mount identifier.");
        }
        return mounts;

        static string DecodeMountField(string value) => value.Replace("\\040", " ").Replace("\\011", "\t")
            .Replace("\\012", "\n").Replace("\\134", "\\");
    }

    private static (PermissionObservation<PermissionAclValue> Access, PermissionObservation<PermissionAclValue> Defaults) ReadAclSections(
        SafeFileHandle proc, string pid, SafeFileHandle pin, Stamp expected, string authority, bool access, bool defaults, CancellationToken ct)
    {
        PermissionObservation<PermissionAclValue> accessResult = new(PermissionReadStatus.Unloaded, AclSource, DateTimeOffset.UtcNow), defaultResult = accessResult;
        try
        {
            WithProcPin(proc, pid, pin, expected, ct, entry =>
            {
                if (access) accessResult = ReadAcl(entry, AccessAttribute, authority, ct);
                if (defaults) defaultResult = ReadAcl(entry, DefaultAttribute, authority, ct);
            });
        }
        catch (ReadFailure ex)
        {
            var failure = new PermissionObservation<PermissionAclValue>(ex.Status, AclSource, DateTimeOffset.UtcNow, Reason: ex.Message, NativeError: ex.Error);
            if (access) accessResult = failure;
            if (defaults) defaultResult = failure;
        }
        return (accessResult, defaultResult);
    }

    private static void WithProcPin(SafeFileHandle proc, string pid, SafeFileHandle pin, Stamp expected, CancellationToken ct, Action<string> use)
    {
        ExceptionDispatchInfo? unexpected = null;
        var worker = new Thread(() =>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                using var process = OpenProcChild(proc, pid, DirectoryPinFlags);
                using var directory = OpenProcChild(process, "fd", DirectoryPinFlags);
                if (Unshare(0x200 /* CLONE_FS */) != 0 || Fchdir(directory) != 0)
                    throw new ReadFailure(PermissionReadStatus.Unsupported, "Could not isolate the metadata worker's descriptor directory.", Marshal.GetLastPInvokeError());
                string entry = pin.DangerousGetHandle().ToInt64().ToString(CultureInfo.InvariantCulture);
                if (ReadStamp(directory, entry).Identity != expected.Identity)
                    throw new ReadFailure(PermissionReadStatus.Stale, "The procfs entry did not resolve to the held object.");
                use(entry);
            }
            catch (Exception ex) { unexpected = ExceptionDispatchInfo.Capture(ex); }
        }) { IsBackground = true, Name = "FileCat Linux metadata worker" };
        worker.Start();
        worker.Join(); // The pin stays live until the worker has returned from the native call.
        unexpected?.Throw();
        ct.ThrowIfCancellationRequested();
    }

    private static PermissionObservation<PermissionAclValue> ReadAcl(string entry, string name, string authority, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var bytes = new byte[4 + 8 * PermissionSnapshots.MaxAclEntries];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        nint length;
        int error;
        fixed (byte* buffer = bytes)
        {
            length = GetXattr(entry, name, buffer, (nuint)bytes.Length);
            error = length < 0 ? Marshal.GetLastPInvokeError() : 0;
        }
        ct.ThrowIfCancellationRequested();
        if (error != 0)
            return new(error == NoAttribute ? PermissionReadStatus.Absent : error == Range ? PermissionReadStatus.Incomplete : ErrorStatus(error),
                AclSource, now, Reason: error == NoAttribute ? null : "The kernel did not return the ACL attribute.", NativeError: error);
        try
        {
            var acl = LinuxPermissionAcl.Decode(bytes.AsSpan(0, (int)length), authority);
            return new(PermissionReadStatus.Present, AclSource, now, new PermissionAclValue(acl),
                Reason: acl is OpaquePermissionAcl ? "This ACL format is opaque." : null);
        }
        catch (InvalidDataException ex) { return new(PermissionReadStatus.Failed, AclSource, now, Reason: ex.Message); }
        catch (OverflowException ex) { return new(PermissionReadStatus.Incomplete, AclSource, now, Reason: ex.Message); }
    }

    private static SafeFileHandle OpenHandle(SafeFileHandle? parent, string name, int flags)
    {
        int fd = parent is null ? Open(name, flags) : OpenAt(parent, name, flags);
        if (fd < 0) throw Failure("Open the original entry", Marshal.GetLastPInvokeError());
        return new(fd, ownsHandle: true);
    }

    private static SafeFileHandle OpenProcChild(SafeFileHandle directory, string name, int flags) =>
        OpenSameMountChild(directory, name, flags, "Open an entry on the verified procfs mount");

    private static SafeFileHandle OpenSameMountChild(SafeFileHandle directory, string name, int flags, string action)
    {
        // Refuse mounts and links before traversing into the child.
        var how = new OpenHow { Flags = (uint)flags, Resolve = 1 | 4 }; // RESOLVE_NO_XDEV | RESOLVE_NO_SYMLINKS
        long fd = OpenAt2(437, directory, name, &how, (nuint)sizeof(OpenHow));
        if (fd < 0) throw Failure(action, Marshal.GetLastPInvokeError());
        return new((nint)fd, ownsHandle: true);
    }

    private static Stamp ReadStamp(SafeFileHandle handle, string name = "", bool cached = false)
    {
        byte* bytes = stackalloc byte[256];
        new Span<byte>(bytes, 256).Clear();
        int flags = (name.Length == 0 ? AtEmptyPath : AtNoAutomount) | (cached ? AtStatxDontSync : 0);
        if (Statx(handle, name, flags, Fields, bytes) != 0)
            throw Failure("Read held file identity", Marshal.GetLastPInvokeError());
        uint mask = *(uint*)bytes & Fields;
        if ((mask & IdentityFields) != IdentityFields || *(ulong*)(bytes + 144) == 0)
            throw new ReadFailure(PermissionReadStatus.Incomplete, "statx did not establish the type, inode and mount identity.");
        if ((*(ulong*)(bytes + 8) & 0x1000 /* STATX_ATTR_AUTOMOUNT */) != 0)
            throw new ReadFailure(PermissionReadStatus.Unsupported, "An automount trigger cannot be inspected in this route.");
        ulong device = ((ulong)*(uint*)(bytes + 136) << 32) | *(uint*)(bytes + 140);
        ulong length = *(ulong*)(bytes + 40);
        long? modified = null;
        long seconds = *(long*)(bytes + 112);
        uint nanos = *(uint*)(bytes + 120);
        if ((mask & ModifyTime) != 0 && seconds is >= -62135596800 and <= 253402300799 && nanos < 1000000000)
            modified = DateTime.UnixEpoch.Ticks + seconds * TimeSpan.TicksPerSecond + nanos / 100;
        uint knownProtection = (uint)(*(ulong*)(bytes + 56) & InodeProtection);
        return new(mask, *(ushort*)(bytes + 28), *(uint*)(bytes + 16), *(uint*)(bytes + 20), *(uint*)(bytes + 24),
            device, *(ulong*)(bytes + 32), *(ulong*)(bytes + 144), *(long*)(bytes + 96), *(uint*)(bytes + 104),
            (mask & Size) != 0 && length <= long.MaxValue ? (long)length : null, modified,
            (uint)(*(ulong*)(bytes + 8) & knownProtection), knownProtection);
    }

    private static PermissionReadStatus ErrorStatus(int error) => error switch
    {
        1 or 13 => PermissionReadStatus.Denied,
        2 or 20 or 116 => PermissionReadStatus.Gone,
        38 or 40 or Unsupported => PermissionReadStatus.Unsupported,
        _ => PermissionReadStatus.Failed
    };

    private static ReadFailure Failure(string action, int error, bool mountedClient = false) =>
        new(ErrorStatus(error), $"{action} failed (errno {error}).", error) { MountedClient = mountedClient };
    private sealed class ReadFailure(PermissionReadStatus status, string message, int? error = null) : Exception(message)
    {
        internal PermissionReadStatus Status { get; } = status;
        internal int? Error { get; } = error;
        internal bool MountedClient { get; init; }
    }

    private sealed record Mount(ulong Id, ulong Device, string Path, string FileSystem, bool ReadOnly, bool PosixAclDisabled,
        PermissionMountObservation? Projection);
    [StructLayout(LayoutKind.Sequential)] private struct OpenHow { public ulong Flags, Mode, Resolve; }
    private readonly record struct Stamp(uint Valid, ushort Mode, uint LinkCount, uint User, uint Group, ulong Device, ulong Inode, ulong Mount,
        long ChangeSeconds, uint ChangeNanos, long? Size, long? ModifiedUtcTicks, uint Protection, uint KnownProtection)
    {
        internal bool? ResetUnprotected => KnownProtection == InodeProtection ? Protection == 0 : null;
        internal ushort FileKind => (ushort)(Mode & 0xf000);
        internal (ulong Device, ulong Inode, ulong Mount, ushort Kind) Identity => (Device, Inode, Mount, FileKind);
    }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string name, int flags);
    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenAt(SafeFileHandle parent, string name, int flags);
    [LibraryImport("libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial long OpenAt2(long syscall, SafeFileHandle directory, string name, OpenHow* how, nuint length);
    [LibraryImport("libc", EntryPoint = "statx", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Statx(SafeFileHandle directory, string name, int flags, uint mask, byte* bytes);
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int FStatFs(SafeFileHandle handle, byte* buffer);
    [LibraryImport("libc", EntryPoint = "syncfs", SetLastError = true)]
    private static partial int SyncFs(SafeFileHandle handle);
    [LibraryImport("libc", EntryPoint = "readlinkat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint ReadLinkAt(SafeFileHandle handle, string name, byte* buffer, nuint length);
    [LibraryImport("libc", EntryPoint = "getpid")]
    private static partial int GetPid();
    [LibraryImport("libc", EntryPoint = "unshare", SetLastError = true)]
    private static partial int Unshare(int flags);
    [LibraryImport("libc", EntryPoint = "fchdir", SetLastError = true)]
    private static partial int Fchdir(SafeFileHandle directory);
    [LibraryImport("libc", EntryPoint = "getxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint GetXattr(string name, string attribute, byte* buffer, nuint length);
}
