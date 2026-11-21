using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    private const string SmbReadOnly = "Windows SMB permissions are inspection-only until FileCat can verify the server session and share for changes.";
    private const uint LanManagerProvider = 0x00020000, OfflineProtocol = 2, DfsShareCapability = 8, DfsShareFlags = 3;
    private const string UncPrefix = @"\\?\UNC\";

    private static bool TrySmbTarget(string path, out SmbTarget target)
    {
        target = default;
        if (path.Length > 32767 || path.AsSpan().IndexOfAny('\0', '/', '*') >= 0)
            return false;

        if (path.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase))
            path = @"\\" + path[UncPrefix.Length..];
        if (PathUtil.IsUncPath(path))
        {
            var parts = path[2..].TrimEnd('\\').Split('\\');
            if (parts.Length < 2 || parts.Any(component => !StoragePaths.IsChildName(component)))
                return false;
            string share = @"\\" + parts[0] + @"\" + parts[1];
            target = new(@"\??\UNC\" + share[2..] + @"\", share, parts[2..]);
            return true;
        }

        if (!StoragePaths.IsOrdinaryLocalPath(path))
            return false;
        var (root, names) = SplitLocalPath(path);
        if (root.Length != 3 || GetDriveType(root) != 4 /* DRIVE_REMOTE */)
            return false;
        target = new(@"\??\" + root, null, names);
        return true;
    }

    private static PermissionInspection InspectSmb(PermissionSubject subject, PermissionSections sections, SmbTarget target, CancellationToken ct)
    {
        if (subject.LinkPolicy != PermissionLinkPolicy.NoFollow || target.Names.Length > PermissionSnapshots.MaxPathDepth)
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported,
                "Windows SMB inspection requires an exact original within the supported path depth.");

        try
        {
            var requested = sections & DiscretionarySections;
            using var handle = WalkSmb(target, requested != PermissionSections.None, ct, out var first);
            var final = first[^1];
            var authority = new PermissionAuthority(PermissionAuthorityKind.MountedServer, first[0].Path, 0);
            if (subject.Authority.Kind == PermissionAuthorityKind.MountedServer &&
                !string.Equals(subject.Authority.Id, authority.Id, StringComparison.OrdinalIgnoreCase))
                throw new ReadFailure(PermissionReadStatus.Stale, "The Windows SMB share mapping changed.");
            var identity = final.Stamp.Identity with { Authority = authority };
            var current = subject with { Authority = authority };
            if (!PermissionSnapshots.Matches(current, identity) ||
                !PermissionSnapshots.MatchesListing(subject, final.Stamp.Size, final.Stamp.ModifiedUtcTicks))
                throw new ReadFailure(PermissionReadStatus.Stale, "The Windows SMB entry differs from the original selection.");

            DateTimeOffset now = DateTimeOffset.UtcNow;
            var snapshot = PermissionSnapshots.Create(current with { ExpectedIdentity = identity }, PermissionModel.WindowsDescriptor,
                null, sections, now, Source) with
            {
                Mount = new(PermissionMountPresentation.Translated, authority.Id, "/", $"Windows SMB {final.Dialect}",
                    "The descriptor is presented by the SMB client. Share-level authorization is separate.",
                    true, SmbReadOnly)
            };
            if (requested != PermissionSections.None)
                snapshot = ReadHeldDescriptor(snapshot, requested, handle, ct);
            if (sections.HasFlag(PermissionSections.Audit))
                snapshot = snapshot with { Audit = new(PermissionReadStatus.Unsupported, Source, now,
                    Reason: "This Windows SMB route does not request the server audit ACL.") };

            using var checkedHandle = WalkSmb(target, requested != PermissionSections.None, ct, out var second);
            if (!first.SequenceEqual(second))
                throw new ReadFailure(PermissionReadStatus.Stale, "The Windows SMB mapping or entry changed during inspection.");
            ct.ThrowIfCancellationRequested();
            return new(PermissionSnapshots.WithCapabilities(snapshot, sections));
        }
        catch (ReadFailure ex)
        {
            ct.ThrowIfCancellationRequested();
            var status = ex.Error is 53 or 64 or 67 or 2250 ? PermissionReadStatus.Gone : ex.Status;
            return PermissionSnapshots.Unavailable(subject, sections, status, ex.Message, Source, ex.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ct.ThrowIfCancellationRequested();
            int error = ex is System.ComponentModel.Win32Exception native ? native.NativeErrorCode : ex.HResult & 0xffff;
            return PermissionSnapshots.Unavailable(subject, sections, ErrorStatus(error), ex.Message, Source, error);
        }
    }

    private static SafeFileHandle WalkSmb(SmbTarget target, bool security, CancellationToken ct, out SmbStamp[] observed)
    {
        observed = new SmbStamp[target.Names.Length + 1];
        SafeFileHandle? current = Open(null, target.Root, security && target.Names.Length == 0, ct);
        try
        {
            string share = OpenedSmbPath(current, ct);
            if (share[2..].Count(c => c == '\\') != 1 ||
                target.ExpectedShare is { } declared && !string.Equals(share, declared, StringComparison.OrdinalIgnoreCase))
                throw new ReadFailure(PermissionReadStatus.Unsupported, "The Windows SMB share root differs from the captured path.");
            var authority = new PermissionAuthority(PermissionAuthorityKind.MountedServer, share, 0);
            observed[0] = DescribeSmb(current, share, root: true, authority, ct);
            string expected = share;
            for (int i = 0; i < target.Names.Length; i++)
            {
                var child = Open(current, target.Names[i], security && i == target.Names.Length - 1, ct);
                current.Dispose();
                current = child;
                expected += @"\" + target.Names[i];
                if (!string.Equals(OpenedSmbPath(current, ct), expected, StringComparison.OrdinalIgnoreCase))
                    throw new ReadFailure(PermissionReadStatus.Stale, "A Windows SMB path component resolved outside its captured share.");
                observed[i + 1] = DescribeSmb(current, expected, root: false, authority, ct);
                if (i < target.Names.Length - 1 && observed[i + 1].Stamp.Identity.Kind != PermissionObjectKind.Directory)
                    throw new ReadFailure(PermissionReadStatus.Stale, "A Windows SMB ancestor is no longer a directory.");
            }
            var result = current;
            current = null;
            return result;
        }
        finally { current?.Dispose(); }
    }

    private static SmbStamp DescribeSmb(SafeFileHandle handle, string path, bool root, PermissionAuthority authority, CancellationToken ct)
    {
        var basic = NtFile.GetBasic(handle);
        if ((basic.FileAttributes & UnsafeAttributes) != 0)
            throw new ReadFailure(PermissionReadStatus.Unsupported, "A Windows SMB entry is a reparse point, offline, or requires recall.");

        RemoteProtocolInfo protocol;
        if (!GetInformation(handle, 13 /* FileRemoteProtocolInfo */, &protocol, (uint)sizeof(RemoteProtocolInfo)) ||
            protocol.Version < 3 || protocol.Size < 64 || protocol.Provider != LanManagerProvider || protocol.Major is not (2 or 3))
            throw new ReadFailure(PermissionReadStatus.Unsupported, "The inspected handle did not report a supported Windows SMB dialect.");
        if ((protocol.Flags & OfflineProtocol) != 0)
            throw new ReadFailure(PermissionReadStatus.Gone, "The Windows SMB client reports this entry offline.");
        if ((protocol.ShareCapabilities & DfsShareCapability) != 0 || (protocol.ShareFlags & DfsShareFlags) != 0)
            throw new ReadFailure(PermissionReadStatus.Unsupported, "Windows SMB DFS paths lack a corroborated permissions route.");

        string dialect = FormattableString.Invariant($"{protocol.Major}.{protocol.Minor}.{protocol.Revision}");
        var stamp = ReadFileStamp(handle, basic, authority.Id, root, authority, ct);
        return new(path, stamp, dialect, protocol.Flags, protocol.ShareCapabilities, protocol.ShareFlags);
    }

    private static string OpenedSmbPath(SafeFileHandle handle, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Span<char> buffer = stackalloc char[512];
        uint count;
        fixed (char* path = buffer)
            count = GetOpenedFileName(handle, path, (uint)buffer.Length, 8 /* FILE_NAME_OPENED | VOLUME_NAME_DOS */);
        if (count >= buffer.Length && count < 32768)
        {
            buffer = new char[count];
            fixed (char* path = buffer)
                count = GetOpenedFileName(handle, path, (uint)buffer.Length, 8);
        }
        ct.ThrowIfCancellationRequested();
        if (count == 0 || count >= buffer.Length ||
            !buffer[..(int)count].StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ReadFailure(PermissionReadStatus.Unsupported, "Windows did not corroborate the SMB share path by handle.");
        return @"\\" + buffer[UncPrefix.Length..(int)count].ToString().TrimEnd('\\');
    }

    private readonly record struct SmbTarget(string Root, string? ExpectedShare, string[] Names);
    private sealed record SmbStamp(string Path, FileStamp Stamp, string Dialect, uint Flags, uint ShareCapabilities, uint ShareFlags);

    [StructLayout(LayoutKind.Explicit, Size = 116)]
    private struct RemoteProtocolInfo
    {
        [FieldOffset(0)] internal ushort Version;
        [FieldOffset(2)] internal ushort Size;
        [FieldOffset(4)] internal uint Provider;
        [FieldOffset(8)] internal ushort Major;
        [FieldOffset(10)] internal ushort Minor;
        [FieldOffset(12)] internal ushort Revision;
        [FieldOffset(16)] internal uint Flags;
        [FieldOffset(56)] internal uint ShareCapabilities;
        [FieldOffset(60)] internal uint ShareFlags;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetOpenedFileName(SafeFileHandle file, char* name, uint length, uint flags);
}
