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

    private static LinuxAccessReport CheckBoundAccess(SafeFileHandle pin, PermissionBinding binding, Stamp original, bool additionalFileSystems,
        Func<Dictionary<ulong, Mount>, CancellationToken, Stamp?, bool> namespaceMatches, CancellationToken cancellation)
    {
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
            var checks = ImmutableArray.CreateBuilder<PermissionAccessCheck>(directory ? 4 : 3);
            checks.Add(CheckRight(directory ? "List names" : "Read content", 4));
            checks.Add(CheckRight(directory ? "Write permission on this directory" : "Write content", 2));
            checks.Add(CheckRight(directory ? "Search known names" : "Execute permission", 1));
            if (directory) checks.Add(CheckRight("Write and search together", 3));

            var refreshedMounts = ReadMounts(proc, pid, cancellation);
            if (ReadAccessCredentials(proc, pid) != credentials || AdmitMount(pin, stamp, refreshedMounts, binding.MountRoot, additionalFileSystems) != mount ||
                (VerifiedMountFlags(pin, mount.FileSystem) & AccessMountFlags) != flags || ReadStamp(pin) != stamp || !namespaceMatches(refreshedMounts, cancellation, stamp))
                throw new PermissionBindingConflictException("The object, credentials or mount changed during the access check.");
            return new(binding.Identity, checkedAt, credentials.EffectiveUid, credentials.EffectiveGid,
                credentials.FileSystemUid, credentials.FileSystemGid, credentials.Groups.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToImmutableArray(),
                credentials.Capabilities, mount.ReadOnly || (flags & MountReadOnly) != 0, (flags & MountNoExecute) != 0,
                (flags & MountNoSetId) != 0, checks.MoveToImmutable());
        }
        catch (ReadFailure failure) { throw new IOException(failure.Message, failure); }

        PermissionAccessCheck CheckRight(string right, int mode)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Faccessat2(Faccessat2Number, pin, "", mode, AtEffectiveAccess | AtEmptyPath) == 0)
                return new(right, PermissionReadStatus.Present, null);
            int error = Marshal.GetLastPInvokeError();
            return new(right, error switch
            {
                1 or 13 or 30 => PermissionReadStatus.Denied,
                22 or 38 or 95 => PermissionReadStatus.Unsupported,
                _ => PermissionReadStatus.Failed
            }, error);
        }
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
