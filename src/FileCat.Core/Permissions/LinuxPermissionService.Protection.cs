using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class LinuxPermissionService
{
    // The Linux statx and inode ioctl interfaces use the same bits for these two flags.
    internal const uint ImmutableInode = 0x10, AppendOnlyInode = 0x20, InodeProtection = ImmutableInode | AppendOnlyInode;
    private const uint ImmutableCapability = 1 << 9; // CAP_LINUX_IMMUTABLE
    private const ulong GetInodeFlags = 0x8008_6601, SetInodeFlags = 0x4008_6602; // Linux x64 ioctl requests
    private const string FlagSource = "Linux FS_IOC_GETFLAGS on the held inode";
    private const string ClearProtectionFirst = "Review explicitly clearing immutable and append-only protection before changing this object's permissions.";

    internal static bool CanEditProtection(PermissionSnapshot snapshot) =>
        snapshot.Flags.Value is { KnownBits: uint.MaxValue, WritableBits: InodeProtection } && ModeReviewRefusal(snapshot) is null;

    private static bool HasImmutableCapability()
    {
        int* header = stackalloc int[2] { 0x20080522, 0 };
        uint* capabilities = stackalloc uint[6];
        return CapGet(header, capabilities) == 0 && (capabilities[0] & ImmutableCapability) != 0;
    }

    private static SafeFileHandle OpenFlagHandle(SafeFileHandle proc, string pid, SafeFileHandle pin, Stamp stamp, CancellationToken ct)
    {
        SafeFileHandle? data = null;
        try
        {
            WithProcPin(proc, pid, pin, stamp, ct, entry =>
            {
                // The proc alias is verified against the held regular file or directory. No data is read.
                data = OpenHandle(null, entry, LinuxOpenFlags.CloseOnExec | 0x800 /* O_NONBLOCK */);
                if (ReadStamp(data) != stamp)
                    throw new ReadFailure(PermissionReadStatus.Stale, "The readable descriptor did not match the held inode.");
            });
            var held = data!;
            data = null;
            return held;
        }
        finally { data?.Dispose(); }
    }

    private static PermissionObservation<PermissionFlagsValue> ObserveProtection(PermissionBinding binding, SafeFileHandle proc,
        string pid, SafeFileHandle pin, Stamp stamp, SafeFileHandle? held, bool writesEnabled, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            using var opened = held is null ? OpenFlagHandle(proc, pid, pin, stamp, ct) : null;
            uint flags = ReadInodeFlags(held ?? opened!);
            if ((flags & stamp.KnownProtection) != stamp.Protection)
                throw new ReadFailure(PermissionReadStatus.Stale, "The inode protection changed while it was being inspected.");
            bool permitted = HasImmutableCapability();
            bool writable = permitted && writesEnabled && binding is { FileSystem: "ext4", ReadOnly: false,
                Identity.Kind: PermissionObjectKind.File or PermissionObjectKind.Directory } &&
                (stamp.Valid & Uid) != 0 && GetEffectiveUid() is var uid && uid != 0 && uid == stamp.User;
            string? reason = writable ? null : permitted ? "Protection editing is unavailable on this permissions route."
                : "Changing immutable or append-only requires CAP_LINUX_IMMUTABLE.";
            return new(PermissionReadStatus.Present, FlagSource, now, new(flags, uint.MaxValue, writable ? InodeProtection : 0), Reason: reason);
        }
        catch (ReadFailure failure)
        {
            if (stamp.KnownProtection != 0 && failure.Status is not (PermissionReadStatus.Stale or PermissionReadStatus.Gone))
                return new(PermissionReadStatus.Present, Source, now, new(stamp.Protection, stamp.KnownProtection),
                    Reason: "Full inode flags require a readable descriptor and filesystem ioctl support.", NativeError: failure.Error);
            return new(failure.Status, FlagSource, now, Reason: failure.Message, NativeError: failure.Error);
        }
    }

    private static uint ReadInodeFlags(SafeFileHandle file)
    {
        // The kernel reads or writes an int even though the UAPI request number encodes sizeof(long).
        uint flags = 0;
        if (InodeFlagIoctl(file, GetInodeFlags, &flags) != 0)
            throw Failure("Read inode flags", Marshal.GetLastPInvokeError());
        return flags;
    }

    internal static void RequireUnprotected(PermissionSnapshot snapshot)
    {
        if ((snapshot.Flags.Value.GetValueOrDefault().Bits & InodeProtection) != 0)
            throw new UnauthorizedAccessException(ClearProtectionFirst);
    }

    internal static PermissionSections ProtectionChangeSections(PermissionSnapshot snapshot, PermissionDraft draft)
    {
        var sections = SecuritySections(snapshot.Binding!) | PermissionSections.Flags;
        if (draft.Changes.Any(change => change is AssignPermissionPrincipal)) sections |= PermissionSections.FileCapabilities;
        if (draft.Changes.Any(change => change is ChangePermissionTimes)) sections |= PermissionSections.Times;
        return sections;
    }

    internal static PermissionChangePlan PreviewProtection(PermissionSnapshot original, PermissionDraft draft,
        LinuxGroupAuthority? groupAuthority, bool acceptSetIdClear = true)
    {
        if (!CanEditProtection(original))
            throw new UnauthorizedAccessException("Protection changes require a qualified ext4 binding, a readable descriptor and CAP_LINUX_IMMUTABLE.");
        if (draft.Changes.IsDefaultOrEmpty || draft.Changes.Count(change => change is ChangePermissionFlags) != 1)
            throw new NotSupportedException("Review one touched-bit inode protection change.");

        var flags = draft.Changes.OfType<ChangePermissionFlags>().Single();
        ulong current = original.Flags.Value!.Value.Bits;
        if (flags.TouchedBits == 0 || (flags.TouchedBits & ~(ulong)InodeProtection) != 0 || (flags.Bits & ~flags.TouchedBits) != 0 ||
            ((current ^ flags.Bits) & flags.TouchedBits) != flags.TouchedBits)
            throw new InvalidDataException("Only changed immutable and append-only bits can be reviewed.");
        var edits = draft.Changes.Where(change => change is not ChangePermissionFlags).ToImmutableArray();
        bool group = edits.Any(change => change is AssignPermissionPrincipal);
        bool defaults = draft.Scope == PermissionScope.DefaultsForNewChildren;
        if (defaults ? edits is not [ChangePosixAcl { IsDefault: true }] :
            draft.Scope != PermissionScope.Objects || !group &&
            (edits.Any(change => change is not (ChangePermissionMode or ChangePosixAcl { IsDefault: false } or ChangePermissionTimes)) ||
             edits.Count(change => change is ChangePermissionTimes) > 1 || edits.Count(change => change is not ChangePermissionTimes) > 1))
            throw new NotSupportedException("Select one access or mode change and an optional modification time, or a directory default ACL change.");
        _ = RequireRevision(original, ProtectionChangeSections(original, draft));

        var expected = original;
        var stages = ImmutableArray.CreateBuilder<PermissionChangeStage>();
        bool needsSetIdConsent = false;
        ulong clear = flags.TouchedBits & ~flags.Bits;
        ulong set = flags.Bits & flags.TouchedBits;
        FlagStep(0, clear);
        if (!edits.IsEmpty) RequireUnprotected(expected);
        if (group)
        {
            var plan = PreviewGroupChange(expected, new(draft.Scope, edits), groupAuthority ??
                throw new UnauthorizedAccessException("FileCat's ordinary group credential could not be verified."));
            if (plan.NeedsSetIdConsent && !acceptSetIdClear)
                throw new InvalidOperationException("Accept the reviewed set-ID effects before applying the group change.");
            stages.AddRange(plan.Stages);
            expected = plan.Expected;
            needsSetIdConsent = plan.NeedsSetIdConsent;
        }
        else
        {
            foreach (var change in edits.OrderBy(change => change is ChangePermissionTimes))
            {
                ushort beforeMode = expected.Mode.Value!.Value;
                stages.Add(ProjectObjectChange(expected, change, acceptSetIdClear));
                expected = stages[^1].Expected;
                needsSetIdConsent |= (beforeMode & SetIdBits & ~expected.Mode.Value!.Value) != 0;
            }
        }
        FlagStep(set, set);
        return new(expected, stages.ToImmutable(), needsSetIdConsent);

        void FlagStep(ulong bits, ulong touched)
        {
            if (touched == 0) return;
            var value = expected.Flags.Value!.Value;
            expected = expected with { Flags = expected.Flags with { Value = value with { Bits = (value.Bits & ~touched) | bits } } };
            stages.Add(new(new ChangePermissionFlags(bits, touched), expected, "FS_IOC_SETFLAGS (held descriptor)"));
        }
    }

    private static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) PrepareProtectionTarget(PermissionInspection inspection,
        ItemRef source, PermissionDraft draft, LinuxGroupAuthority? groupAuthority, bool acceptSetIdClear, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var original = inspection.Snapshot;
        if (original.Subject.RawLocator != source.FileSystemPath)
            throw new NotSupportedException("This protection change requires the original inspected object.");
        var plan = PreviewProtection(original, draft, groupAuthority, acceptSetIdClear);
        var sections = ProtectionChangeSections(original, draft);
        var startingRevision = RequireRevision(original, sections);
        var before = startingRevision;
        var components = ImmutableArray.CreateBuilder<ReviewedPermissionComponent>(plan.Stages.Length);
        foreach (var stage in plan.Stages)
        {
            var after = RequireRevision(stage.Expected, sections);
            components.Add(new(stage.Change, before, after, stage.NativeCall));
            before = after;
        }
        return BindPreparedTarget(inspection, source, plan.Expected, components.MoveToImmutable(), startingRevision, ct,
            editDefaults: draft.Scope == PermissionScope.DefaultsForNewChildren, groupAuthority: groupAuthority, reviewProtection: true);
    }

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int InodeFlagIoctl(SafeFileHandle file, ulong request, uint* flags);
}
