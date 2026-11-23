using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

/// <summary>Observes Darwin security through a retained descriptor or a no-follow name relative to its parent.</summary>
internal sealed unsafe partial class DarwinPermissionService(bool qualifyParentWrites = false, bool qualifyDescriptorWrites = false,
    bool qualifyProtectionWrites = false, bool qualifyAclWrites = false, bool qualifyGroupWrites = false,
    bool qualifyAccessChecks = false, bool qualifyModeTrees = false, bool qualifyAclTrees = false) : IPermissionService
{
    private const string Source = "Darwin getattrlistat";
    private const int SearchNoFollow = 0x61100000; // O_SEARCH | O_CLOEXEC | O_NOFOLLOW_ANY
    private const nuint NoFollow = 1;
    private const uint Returned = 0x80000000, ExtendedSecurity = 0x00400000;
    private const uint Metadata = 0x02078e0e; // dev, fsid, type, birth/modified/change time, uid, gid, mode, flags, file ID
    private const uint AclIdentity = 0x0200080c; // fsid, type, change time and file ID
    private const uint LinkCount = 1, DataLength = 0x200;
    private const int HeaderSize = 24; // length plus attribute_set_t
    private const uint MountLocal = 0x1000, MountReadOnly = 1;
    private const uint VolumeCapabilities = 0x20000, VolumeInfo = 0x80000000, ExtendedSecurityCapability = 0x400;

    internal bool WritesEnabled => qualifyParentWrites || qualifyDescriptorWrites;

    public PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsMacOS() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64 || !PermissionSnapshots.IsLocal(subject))
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "This reader requires a local macOS arm64 subject.");
        InspectionPin? retained = null;
        try
        {
            var snapshot = MacMaterializationPolicy.Run(cancellationToken, () => InspectWithPolicy(subject, sections, cancellationToken, out retained),
                (_, reason) => Unavailable(subject, sections, PermissionReadStatus.Unsupported, reason));
            var result = new PermissionInspection(snapshot, snapshot.Binding is null ? null : retained);
            if (snapshot.Binding is not null) retained = null;
            return result;
        }
        finally { retained?.Dispose(); }
    }

    private PermissionSnapshot InspectWithPolicy(PermissionSubject subject, PermissionSections sections, CancellationToken ct, out InspectionPin? pin)
    {
        pin = null;
        string path = Path.TrimEndingDirectorySeparator(subject.RawLocator);
        if (PathUtil.HasAmbiguousUnixName(path) || !Path.IsPathFullyQualified(path) || Path.GetFullPath(path) != path ||
            path.Count(character => character == '/') > PermissionSnapshots.MaxPathDepth)
            return Unavailable(subject, sections, PermissionReadStatus.Unsupported, "This reader requires an exact filesystem path.");
        string leaf = path == "/" ? "." : Path.GetFileName(path);
        string directory = Path.GetDirectoryName(path) ?? "/";
        SafeFileHandle? parent = null;
        SafeFileHandle? descriptor = null;
        try
        {
            parent = OpenDirectory(directory);
            var parentIdentity = UnixFiles.Stat(parent)?.Identity ?? throw new ReadFailure(PermissionReadStatus.Incomplete, "The parent identity is unavailable.");
            var parentMount = ReadMount(parent);
            var mount = parentMount;
            var before = ReadEntry(parent, leaf);
            if (before.FileSystem != mount.Id)
            {
                if (before.Kind != PermissionObjectKind.Directory)
                    throw new ReadFailure(PermissionReadStatus.Unsupported, "The original crossed a mount boundary without a directory binding.");
                descriptor = OpenMetadataDescriptor(parent, leaf, before.Kind);
                mount = ReadMount(descriptor);
                if (mount.Root != path || before.FileSystem != mount.Id || ReadEntry(descriptor, null) != before)
                    throw new ReadFailure(PermissionReadStatus.Stale, "The original mount root could not be corroborated.");
            }
            var identity = new PermissionIdentity(before.Object, $"darwin:{mount.Id:X16}:{mount.Root}",
                path == mount.Root ? PermissionObjectKind.VolumeRoot : before.Kind, subject.Authority);
            if (!PermissionSnapshots.Matches(subject, identity) ||
                !PermissionSnapshots.MatchesListing(subject, before.Size, before.Modified?.UtcTicks))
                throw new ReadFailure(PermissionReadStatus.Stale, "The original object or listing metadata changed.");

            var binding = new PermissionBinding(identity, mount.Type, mount.Root, before.Links, (mount.Flags & MountReadOnly) != 0);
            var snapshot = descriptor is null ? ReadSnapshot(subject, binding, sections, parent, leaf, before, mount)
                : ReadSnapshot(subject, binding, sections, descriptor, null, before, mount);
            if (sections.HasFlag(PermissionSections.Mode))
                snapshot = PrepareWriteAvailability(snapshot, sections, parent, parentIdentity, leaf, mount, before, ref descriptor);
            else snapshot = PermissionSnapshots.WithCapabilities(snapshot, sections);

            ct.ThrowIfCancellationRequested();
            using var currentParent = OpenDirectory(directory);
            if (UnixFiles.Stat(currentParent)?.Identity != parentIdentity || ReadMount(currentParent) != parentMount ||
                ReadEntry(currentParent, leaf) != before || descriptor is not null && (ReadMount(descriptor) != mount || ReadEntry(descriptor, null) != before))
                throw new ReadFailure(PermissionReadStatus.Stale, "The original entry or namespace changed during inspection.");
            ct.ThrowIfCancellationRequested();
            pin = new(parent, directory, leaf, parentIdentity, parentMount, mount, before, descriptor);
            parent = null;
            descriptor = null;
            return snapshot;
        }
        catch (ReadFailure error) { return Unavailable(subject, sections, error.Status, error.Message, error.NativeError); }
        catch (Exception error) when (error is EntryPointNotFoundException or DllNotFoundException)
        { return Unavailable(subject, sections, PermissionReadStatus.Unsupported, "Required Darwin metadata calls are unavailable."); }
        finally { descriptor?.Dispose(); parent?.Dispose(); }
    }

    private static PermissionSnapshot Unavailable(PermissionSubject subject, PermissionSections sections, PermissionReadStatus status,
        string reason, int? error = null)
    {
        using var observation = PermissionSnapshots.Unavailable(subject, sections, status, reason, Source, error);
        return observation.Snapshot;
    }

    private static PermissionSnapshot ReadSnapshot(PermissionSubject subject, PermissionBinding binding, PermissionSections sections,
        SafeFileHandle handle, string? leaf, Entry entry, Mount mount)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string authority = subject.Authority.Id;
        string source = leaf is null ? "Darwin fgetattrlist" : Source;
        var snapshot = PermissionSnapshots.Create(subject, PermissionModel.DarwinAcl, binding, sections, now, source);
        if (sections.HasFlag(PermissionSections.Owner)) snapshot = snapshot with { Owner = Present(Id(PrincipalKind.UnixUser, entry.Owner)) };
        if (sections.HasFlag(PermissionSections.Group)) snapshot = snapshot with { Group = Present(Id(PrincipalKind.UnixGroup, entry.Group)) };
        if (sections.HasFlag(PermissionSections.Mode)) snapshot = snapshot with { Mode = Present(entry.Mode) };
        if (sections.HasFlag(PermissionSections.Flags)) snapshot = snapshot with { Flags = Present(new PermissionFlagsValue(entry.Flags, uint.MaxValue)) };
        if (sections.HasFlag(PermissionSections.Times)) snapshot = snapshot with { Times = Present(new PermissionTimesValue(entry.Modified, entry.Created)) };
        if (sections.HasFlag(PermissionSections.Access)) snapshot = snapshot with { Access = ReadAcl(handle, leaf, entry, authority, now) };
        if (sections.HasFlag(PermissionSections.VolumePolicy))
            snapshot = snapshot with { VolumePolicy = Present(new PermissionVolumePolicy((mount.Flags & MountIgnoreOwnership) == 0)) with { Source = "Darwin fstatfs" } };
        return snapshot;

        Principal Id(PrincipalKind kind, uint id) => new(kind, id.ToString(CultureInfo.InvariantCulture), authority);
        PermissionObservation<T> Present<T>(T value) where T : struct => new(PermissionReadStatus.Present, source, now, value);
    }

    private static SafeFileHandle OpenDirectory(string path)
    {
        int descriptor = Open(path, SearchNoFollow);
        if (descriptor < 0) throw NativeFailure("Open the original parent for search", Marshal.GetLastPInvokeError());
        return new(descriptor, ownsHandle: true);
    }

    private static Entry ReadEntry(SafeFileHandle parent, string? leaf)
    {
        var request = new AttrList { Count = 5, Common = Returned | Metadata, Directory = LinkCount, File = LinkCount | DataLength };
        Span<byte> data = stackalloc byte[124];
        ReadAt(parent, leaf, request, data);
        if ((UInt(data, 4) & Metadata) != Metadata || UInt(data, 0) is < 116 or > 124)
            throw new ReadFailure(PermissionReadStatus.Incomplete, "The original did not return all required identity and security metadata.");
        PermissionObjectKind kind = UInt(data, 36) switch
        {
            1 => PermissionObjectKind.File,
            2 => PermissionObjectKind.Directory,
            _ => throw new ReadFailure(PermissionReadStatus.Unsupported, "Darwin permission inspection here requires a regular file or directory.")
        };
        uint available = UInt(data, kind == PermissionObjectKind.Directory ? 12 : 16);
        if ((available & LinkCount) == 0 || leaf is not null && UInt(data, 112) == 0)
            throw new ReadFailure(PermissionReadStatus.Incomplete, "The original link count is unavailable.");
        ulong inode = ULong(data, 104);
        var identity = new FileIdentity(UInt(data, 24), $"{inode:X16}0000000000000000");
        long? size = kind == PermissionObjectKind.File && (available & DataLength) != 0 && UInt(data, 0) == 124
            ? Long(data, 116) : null;
        return new(identity, ULong(data, 28), inode, kind, UInt(data, 112), UInt(data, 88), UInt(data, 92),
            (ushort)(UInt(data, 96) & 0xfff), UInt(data, 100), Time(data, 40), Time(data, 56), Long(data, 72), Long(data, 80), size);
    }

    private static PermissionObservation<PermissionAclValue> ReadAcl(SafeFileHandle parent, string? leaf, Entry expected,
        string authority, DateTimeOffset now)
    {
        const int reference = 52; // length, mask, fsid, type, change time
        const int referenceSize = 8, fileIdSize = 8;
        var request = new AttrList { Count = 5, Common = Returned | AclIdentity | ExtendedSecurity };
        byte[] data = new byte[reference + referenceSize + fileIdSize + 44 + 24 * PermissionSnapshots.MaxAclEntries];
        string source = leaf is null ? "Darwin fgetattrlist" : Source;
        try
        {
            ReadAt(parent, leaf, request, data);
            uint used = UInt(data, 0);
            uint returned = UInt(data, 4);
            bool hasAcl = (returned & ExtendedSecurity) != 0;
            int fileId = reference + (hasAcl ? referenceSize : 0);
            if (used < fileId + fileIdSize || used > data.Length || (returned & AclIdentity) != AclIdentity)
                throw new ReadFailure(PermissionReadStatus.Incomplete, "The ACL observation lacks the required identity evidence.");
            uint type = expected.Kind == PermissionObjectKind.File ? 1u : 2u;
            if (ULong(data, 24) != expected.FileSystem || UInt(data, 32) != type || ULong(data, fileId) != expected.Inode ||
                Long(data, 36) != expected.Changed || Long(data, 44) != expected.ChangedNanos)
                throw new ReadFailure(PermissionReadStatus.Stale, "The security observation did not identify the original Darwin entry.");
            if (!hasAcl)
                return SupportsAcl(parent) ? new(PermissionReadStatus.Absent, source, now) :
                    new(PermissionReadStatus.Unsupported, source, now, Reason: "Extended security support could not be verified for this volume.");
            uint displacement = UInt(data, reference), length = UInt(data, reference + 4);
            if (displacement < referenceSize + fileIdSize || displacement > used - reference || length > used - reference - displacement)
                throw new InvalidDataException("The Darwin ACL reference is outside the returned metadata.");
            var acl = DarwinPermissionAcl.Decode(data.AsSpan(reference + (int)displacement, (int)length), authority);
            return acl is null ? new(PermissionReadStatus.Absent, source, now) : new(PermissionReadStatus.Present, source, now, new PermissionAclValue(acl));
        }
        catch (ReadFailure error) when (error.Status is PermissionReadStatus.Denied or PermissionReadStatus.Unsupported)
        { return new(error.Status, source, now, Reason: error.Message, NativeError: error.NativeError); }
        catch (OverflowException error) { return new(PermissionReadStatus.Incomplete, source, now, Reason: error.Message); }
        catch (InvalidDataException error) { return new(PermissionReadStatus.Failed, source, now, Reason: error.Message); }
    }

    private static bool SupportsAcl(SafeFileHandle parent)
    {
        var request = new AttrList { Count = 5, Common = Returned, Volume = VolumeInfo | VolumeCapabilities };
        Span<byte> data = stackalloc byte[HeaderSize + 32];
        fixed (byte* result = data)
        {
            if (GetHeldAttributes(parent, &request, result, (nuint)data.Length, 0) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == MacMaterializationPolicy.Required) throw NativeFailure("Read ACL volume capabilities", error);
                return false;
            }
        }
        return UInt(data, 0) == data.Length && (UInt(data, 8) & VolumeCapabilities) != 0 &&
            (UInt(data, HeaderSize + 4) & UInt(data, HeaderSize + 20) & ExtendedSecurityCapability) != 0;
    }

    private static void ReadAt(SafeFileHandle handle, string? leaf, AttrList request, Span<byte> data)
    {
        fixed (byte* result = data)
        {
            int returned = leaf is null ? GetHeldAttributes(handle, &request, result, (nuint)data.Length, 0)
                : GetAttributesAt(handle, leaf, &request, result, (nuint)data.Length, NoFollow);
            if (returned != 0)
                throw NativeFailure("Read the exact Darwin entry", Marshal.GetLastPInvokeError());
        }
    }

    private static Mount ReadMount(SafeFileHandle parent)
    {
        // Darwin arm64 struct statfs holds fsid at 48, mount flags at 64 and the two fixed strings at 72 and 88.
        Span<byte> data = stackalloc byte[2168];
        fixed (byte* result = data)
            if (StatFileSystem(parent, result) != 0) throw NativeFailure("Read the held Darwin volume", Marshal.GetLastPInvokeError());
        string type = Text(data.Slice(72, 16));
        string root = Text(data.Slice(88, 1024));
        uint flags = UInt(data, 64);
        if ((flags & MountLocal) == 0 || type is not ("apfs" or "hfs") || !root.StartsWith('/'))
            throw new ReadFailure(PermissionReadStatus.Unsupported, "This reader requires a local APFS or HFS+ filesystem.");
        return new(ULong(data, 48), type, root, flags, UInt(data, 56));
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return end < 0 ? string.Empty : Encoding.UTF8.GetString(bytes[..end]);
    }

    private static DateTimeOffset? Time(ReadOnlySpan<byte> data, int offset)
    {
        long seconds = Long(data, offset), nanos = Long(data, offset + 8);
        return seconds is >= -62135596800 and <= 253402300799 && nanos is >= 0 and < 1000000000
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanos / 100) : null;
    }

    private static uint UInt(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    private static ulong ULong(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(data[offset..]);
    private static long Long(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt64LittleEndian(data[offset..]);
    private static Exception NativeFailure(string action, int error) => error == MacMaterializationPolicy.Required
        ? new IOException(action, error) : new ReadFailure(error switch
        {
            2 or 20 => PermissionReadStatus.Gone,
            1 or 13 => PermissionReadStatus.Denied,
            45 or 62 or 102 => PermissionReadStatus.Unsupported,
            _ => PermissionReadStatus.Failed
        }, $"{action} failed (errno {error}).", error);

    private sealed class ReadFailure(PermissionReadStatus status, string message, int? error = null) : Exception(message)
    {
        internal PermissionReadStatus Status { get; } = status;
        internal int? NativeError { get; } = error;
    }

    private readonly record struct Mount(ulong Id, string Type, string Root, uint Flags, uint Owner);
    private readonly record struct Entry(FileIdentity Object, ulong FileSystem, ulong Inode, PermissionObjectKind Kind, uint Links, uint Owner, uint Group,
        ushort Mode, uint Flags, DateTimeOffset? Created, DateTimeOffset? Modified, long Changed, long ChangedNanos, long? Size);
    private sealed record InspectionPin(SafeFileHandle Handle, string Directory, string Leaf, FileIdentity Identity, Mount ParentMount,
        Mount Mount, Entry Entry, SafeFileHandle? Descriptor) : IDisposable
    {
        public void Dispose() { Descriptor?.Dispose(); Handle.Dispose(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct AttrList { public ushort Count, Reserved; public uint Common, Volume, Directory, File, Fork; }

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string name, int flags);
    [LibraryImport("libc", EntryPoint = "getattrlistat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int GetAttributesAt(SafeFileHandle parent, string name, AttrList* attributes, byte* data, nuint length, nuint options);
    [LibraryImport("libc", EntryPoint = "fgetattrlist", SetLastError = true)]
    private static partial int GetHeldAttributes(SafeFileHandle handle, AttrList* attributes, byte* data, nuint length, nuint options);
    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int StatFileSystem(SafeFileHandle handle, byte* data);
}
