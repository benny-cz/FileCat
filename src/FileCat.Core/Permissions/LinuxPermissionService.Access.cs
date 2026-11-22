using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed record LinuxAccessReport(PermissionIdentity Identity, DateTimeOffset CheckedAt, string EffectiveUid, string EffectiveGid,
    string FileSystemUid, string FileSystemGid, ImmutableArray<string> SupplementaryGroups, string EffectiveCapabilities,
    bool ReadOnly, bool NoExecute, bool NoSetId, ImmutableArray<PermissionAccessCheck> Checks);

internal sealed unsafe partial class LinuxPermissionService
{
    private const int AtEffectiveAccess = 0x200, Faccessat2Number = 439;
    private const ulong MountReadOnly = 1, MountNoSetId = 2, MountNoExecute = 8;
    private const ulong AccessMountFlags = MountReadOnly | MountNoSetId | MountNoExecute;

    internal LinuxAccessReport CheckEffectiveAccess(PermissionSubject subject, CancellationToken cancellation)
    {
        using var inspection = Inspect(subject, PermissionSections.None, cancellation);
        if (!CanCheckAccess(inspection.Snapshot))
            throw new NotSupportedException(inspection.Snapshot.BindingProblem ?? "The current-process access check is unavailable for this object.");
        using var lease = inspection.TransferLease<LinuxBoundPermission>() ??
            throw new NotSupportedException(inspection.Snapshot.BindingProblem ?? "FileCat could not bind the object for the access check.");
        return lease.CheckEffectiveAccess(cancellation);
    }

    private static LinuxAccessReport CheckBoundAccess(SafeFileHandle pin, PermissionBinding binding, string[] names, Stamp[] ancestry,
        bool additionalFileSystems, Func<Dictionary<ulong, Mount>, CancellationToken, Stamp?, bool> namespaceMatches, CancellationToken cancellation)
    {
        var original = ancestry[^1];
        SafeFileHandle? parent = null;
        try
        {
            using var proc = OpenOwnProc(out string pid, cancellation);
            var mounts = ReadMounts(proc, pid, cancellation);
            var stamp = ReadStamp(pin);
            if (stamp.Identity != original.Identity || (stamp.Valid & ChangeTime) == 0 ||
                !namespaceMatches(mounts, cancellation, stamp))
                throw new PermissionBindingConflictException("The original object or its access path changed before the check.");
            var mount = AdmitMount(pin, stamp, mounts, binding.MountRoot, additionalFileSystems);
            if (mount.FileSystem != binding.FileSystem || mount.PosixAclDisabled != binding.PosixAclDisabled)
                throw new PermissionBindingConflictException("The mounted filesystem or its ACL policy changed before the check.");
            ulong flags = VerifiedMountFlags(pin, mount.FileSystem) & AccessMountFlags;
            var credentials = ReadAccessCredentials(proc, pid);
            bool directory = stamp.FileKind == DirectoryType;
            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            var checks = ImmutableArray.CreateBuilder<PermissionAccessCheck>(directory ? 5 : 4);
            checks.Add(CheckRight(directory ? "List names" : "Read content", 4));
            checks.Add(CheckRight(directory ? "Write permission on this directory" : "Write content", 2));
            checks.Add(CheckRight(directory ? "Search known names" : "Execute permission", 1));
            if (directory) checks.Add(CheckRight("Write and search together", 3));
            Stamp? parentStamp = null;
            checks.Add(CheckDelete(mounts, stamp, credentials, ref parentStamp));

            var refreshedMounts = ReadMounts(proc, pid, cancellation);
            if (ReadAccessCredentials(proc, pid) != credentials || AdmitMount(pin, stamp, refreshedMounts, binding.MountRoot, additionalFileSystems) != mount ||
                (VerifiedMountFlags(pin, mount.FileSystem) & AccessMountFlags) != flags || ReadStamp(pin) != stamp || !namespaceMatches(refreshedMounts, cancellation, stamp) ||
                parent is not null && ReadStamp(parent) != parentStamp)
                throw new PermissionBindingConflictException("The object, credentials or mount changed during the access check.");
            return new(binding.Identity, checkedAt, credentials.EffectiveUid, credentials.EffectiveGid,
                credentials.FileSystemUid, credentials.FileSystemGid, credentials.Groups.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray(),
                credentials.Capabilities, mount.ReadOnly || (flags & MountReadOnly) != 0, (flags & MountNoExecute) != 0,
                (flags & MountNoSetId) != 0, checks.MoveToImmutable());
        }
        catch (ReadFailure failure) { throw new IOException(failure.Message, failure); }
        finally { parent?.Dispose(); }

        PermissionAccessCheck CheckRight(string right, int mode) => Check(pin, right, mode);

        PermissionAccessCheck Check(SafeFileHandle handle, string right, int mode)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Faccessat2(Faccessat2Number, handle, "", mode, AtEffectiveAccess | AtEmptyPath) == 0)
                return new(right, PermissionReadStatus.Present, null);
            int error = Marshal.GetLastPInvokeError();
            return new(right, error switch
            {
                1 or 13 or 30 => PermissionReadStatus.Denied,
                22 or 38 or 95 => PermissionReadStatus.Unsupported,
                _ => PermissionReadStatus.Failed
            }, error);
        }

        // Linux has no access mode for unlinking, so this composes the kernel's own checks in the order may_delete applies
        // them: write and search on the parent under the effective credentials, then the append-only parent, the
        // immutable or append-only entry and the sticky-directory rule. A mount root cannot be removed (EBUSY). Security
        // modules and an active swap file can still refuse the removal.
        PermissionAccessCheck CheckDelete(Dictionary<ulong, Mount> mounts, Stamp entry, AccessCredentials credentials, ref Stamp? parentStamp)
        {
            const string right = "Delete this entry from its parent";
            if (names.Length == 0) return new(right, PermissionReadStatus.Unsupported, null);
            if (ancestry[^2].Mount != entry.Mount || ancestry[^2].Device != entry.Device) return new(right, PermissionReadStatus.Denied, 16);
            parent = Walk(names[..^1], mounts, additionalFileSystems, cancellation, out var parents, out _);
            var folder = parents[^1];
            if (folder.Identity != ancestry[^2].Identity)
                throw new PermissionBindingConflictException("The original parent changed before the access check.");
            parentStamp = folder;
            var search = Check(parent, right, 3);
            if (search.Status != PermissionReadStatus.Present) return search;
            if ((folder.KnownProtection & AppendOnlyInode) == 0 || entry.KnownProtection != InodeProtection)
                return new(right, PermissionReadStatus.Incomplete, null);
            if ((folder.Protection & AppendOnlyInode) != 0 || entry.Protection != 0) return new(right, PermissionReadStatus.Denied, 1);
            if ((folder.Mode & 0x200) != 0 && !OwnsOrMayOverride(credentials, entry.User, folder.User))
                return new(right, PermissionReadStatus.Denied, 1);
            return new(right, PermissionReadStatus.Present, null);
        }

        // check_sticky: the entry's owner, the directory's owner or CAP_FOWNER (bit 3) may remove the entry.
        static bool OwnsOrMayOverride(AccessCredentials credentials, uint entryOwner, uint folderOwner) =>
            uint.TryParse(credentials.FileSystemUid, NumberStyles.None, CultureInfo.InvariantCulture, out uint uid) &&
            (uid == entryOwner || uid == folderOwner ||
             ulong.TryParse(credentials.Capabilities, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong capabilities) &&
             (capabilities & (1UL << 3)) != 0);
    }

    private sealed record AccessCredentials(string EffectiveUid, string EffectiveGid, string FileSystemUid, string FileSystemGid,
        string Groups, string Capabilities);

    private static AccessCredentials ReadAccessCredentials(SafeFileHandle proc, string pid)
    {
        using var process = OpenProcChild(proc, pid, DirectoryPinFlags);
        using var tasks = OpenProcChild(process, "task", DirectoryPinFlags);
        using var thread = OpenProcChild(tasks, GetTid().ToString(CultureInfo.InvariantCulture), DirectoryPinFlags);
        using var status = OpenProcChild(thread, "status", LinuxOpenFlags.CloseOnExec | LinuxOpenFlags.Current.NoFollow);
        using var reader = new StreamReader(new FileStream(status, FileAccess.Read));
        string[]? uids = null, gids = null;
        string? groups = null, capabilities = null;
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("Uid:\t", StringComparison.Ordinal)) uids = ParseIds(line);
            if (line.StartsWith("Gid:\t", StringComparison.Ordinal)) gids = ParseIds(line);
            if (line.StartsWith("Groups:\t", StringComparison.Ordinal)) groups = line[8..].Trim();
            if (line.StartsWith("CapEff:\t", StringComparison.Ordinal)) capabilities = line[8..].Trim();
            if (uids is { Length: 4 } && gids is { Length: 4 } && groups is not null && capabilities is { Length: > 0 } &&
                ulong.TryParse(capabilities, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _))
                return new(uids[1], gids[1], uids[3], gids[3], groups, capabilities);
        }
        throw new IOException("FileCat could not observe the thread's access credentials in procfs.");

        static string[] ParseIds(string line) => line[5..].Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [LibraryImport("libc", EntryPoint = "gettid")]
    private static partial int GetTid();
    [LibraryImport("libc", EntryPoint = "syscall", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial long Faccessat2(long syscall, SafeFileHandle pin, string path, int mode, int flags);
}
