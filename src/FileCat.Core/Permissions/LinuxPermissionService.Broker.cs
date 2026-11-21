using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal readonly record struct LinuxBrokerIdentity(ulong Mount, ulong Inode, uint DeviceMajor, uint DeviceMinor, ushort Kind);
internal readonly record struct LinuxBrokerStamp(LinuxBrokerIdentity Identity, uint Mode, uint Uid, uint Gid, uint LinkCount,
    ulong Size, ulong Attributes, ulong AttributeMask, long ChangeSeconds, uint ChangeNanoseconds,
    long ModificationSeconds, uint ModificationNanoseconds);

internal sealed record LinuxBrokerTarget(string Path, LinuxBrokerIdentity Identity, SafeFileHandle Pin) : IDisposable
{
    public void Dispose() => Pin.Dispose();
}

internal sealed unsafe partial class LinuxPermissionService
{
    private const uint UniqueMountId = 0x4000; // STATX_MNT_ID_UNIQUE

    /// <summary>Consume the inspection lease and transfer an independent pin for the same exact ext4 object.</summary>
    internal static LinuxBrokerTarget TakeBrokerTarget(PermissionInspection inspection, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64 ||
            GetEffectiveUid() == 0 || inspection.Snapshot.Binding?.FileSystem != "ext4")
            throw new NotSupportedException("The permissions broker requires an unelevated Linux x64 FileCat and a bound ext4 item.");
        using var bound = inspection.TransferLease<LinuxBoundPermission>() ??
            throw new PermissionBindingConflictException("The original item is no longer bound. Review it again.");
        try { return bound.DuplicateBrokerTarget(cancellation); }
        catch (ReadFailure failure) when (failure.Status is PermissionReadStatus.Stale or PermissionReadStatus.Gone)
        {
            throw new PermissionBindingConflictException(failure.Message);
        }
        catch (ReadFailure failure) { throw new IOException(failure.Message, failure); }
    }

    internal static LinuxBrokerStamp ReadBrokerStamp(LinuxBrokerTarget target)
    {
        const uint fields = Type | Mode | Links | Uid | Gid | ChangeTime | ModifyTime | Size | Inode | UniqueMountId;
        byte* facts = stackalloc byte[256];
        new Span<byte>(facts, 256).Clear();
        if (Statx(target.Pin, "", AtEmptyPath, fields, facts) != 0 || (*(uint*)facts & fields) != fields)
            throw new PermissionBindingConflictException("The original object's native revision could not be corroborated.");
        var identity = new LinuxBrokerIdentity(*(ulong*)(facts + 144), *(ulong*)(facts + 32), *(uint*)(facts + 136),
            *(uint*)(facts + 140), (ushort)(*(ushort*)(facts + 28) & 0xf000));
        if (identity != target.Identity) throw new PermissionBindingConflictException("The original broker subject changed.");
        return new(identity, *(ushort*)(facts + 28), *(uint*)(facts + 20), *(uint*)(facts + 24), *(uint*)(facts + 16),
            *(ulong*)(facts + 40), *(ulong*)(facts + 8), *(ulong*)(facts + 56), *(long*)(facts + 96),
            *(uint*)(facts + 104), *(long*)(facts + 112), *(uint*)(facts + 120));
    }

    internal static PermissionSnapshot PreviewBrokerChange(PermissionSnapshot snapshot, PermissionDelta change, bool acceptSetIdClear)
    {
        if (snapshot.Binding is not { ReadOnly: false, FileSystem: "ext4", LinkCount: > 0 })
            throw new NotSupportedException("The authorization route requires the original bound writable ext4 item.");
        if (snapshot.Flags.Value is not { } flags || (flags.KnownBits & InodeProtection) != InodeProtection)
            throw new NotSupportedException("The current inode protection could not be established.");
        if (change is ChangePermissionFlags protection)
        {
            if (flags.KnownBits != uint.MaxValue || protection.TouchedBits == 0 || (protection.TouchedBits & ~(ulong)InodeProtection) != 0 ||
                (protection.Bits & ~protection.TouchedBits) != 0)
                throw new NotSupportedException("Review one immutable or append-only change with the complete inode flags.");
            return snapshot with { Flags = snapshot.Flags with { Value = flags with
                { Bits = (flags.Bits & ~protection.TouchedBits) | protection.Bits } } };
        }
        RequireUnprotected(snapshot);
        return change switch
        {
            ChangePermissionMode { Bits: <= PermissionBits, TouchedBits: <= PermissionBits } mode =>
                ForecastMode(snapshot, mode, acceptSetIdClear, out _, mayRetainSetGroupId: true),
            ChangePosixAcl { IsDefault: false } acl => ForecastAccess(snapshot, acl, acceptSetIdClear, mayRetainSetGroupId: true),
            ChangePosixAcl defaults => ForecastDefaults(snapshot, defaults),
            AssignPermissionPrincipal principal => PreviewBrokerOwnership(snapshot, principal, acceptSetIdClear),
            _ => throw new NotSupportedException("This operation has no authorized Linux editor.")
        };
    }

    private static PermissionSnapshot PreviewBrokerOwnership(PermissionSnapshot snapshot, AssignPermissionPrincipal change, bool acceptSetIdClear)
    {
        ValidateKnownAcl(snapshot);
        var principal = change.Principal;
        var current = change.IsOwner ? snapshot.Owner : snapshot.Group;
        if (principal.Kind != (change.IsOwner ? PrincipalKind.UnixUser : PrincipalKind.UnixGroup) ||
            principal.Authority != snapshot.Subject.Authority.Id ||
            !uint.TryParse(principal.Id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) ||
            id == uint.MaxValue)
            throw new ArgumentException("Enter a local numerical account ID from 0 to 4294967294.");
        if (current.Value == principal) return snapshot;
        var expected = change.IsOwner ? snapshot with { Owner = current with { Value = principal } } :
            snapshot with { Group = current with { Value = principal } };
        if (snapshot.Binding!.Identity.Kind != PermissionObjectKind.File) return expected;
        if (snapshot.FileCapabilities?.Status is not (PermissionReadStatus.Present or PermissionReadStatus.Absent))
            throw new NotSupportedException("Ownership changes require readable file capability evidence.");
        ushort mode = snapshot.Mode.Value!.Value;
        // The installed root mechanism retains a non-executable SGID bit. An executable one is cleared on chown.
        ushort retained = (ushort)(mode & ~(SetUserId | ((mode & GroupExecute) != 0 ? SetGroupId : 0)));
        if (!acceptSetIdClear && (mode & SetIdBits & ~retained) != 0)
            throw new InvalidOperationException("Review and accept clearing the existing set-ID bits before applying.");
        return expected with { Mode = snapshot.Mode with { Value = retained },
            FileCapabilities = snapshot.FileCapabilities with { Status = PermissionReadStatus.Absent, Value = null } };
    }

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int DuplicateBrokerPin(SafeFileHandle pin, int command, int minimum);
}
