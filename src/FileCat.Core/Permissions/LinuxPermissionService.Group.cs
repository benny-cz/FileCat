using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Permissions;

internal sealed record LinuxGroupAuthority(string UserId, string GroupId, string SupplementaryGroupIds, ulong Capabilities)
{
    internal bool CanKeepSetGroupId(uint group) => Includes(group) || (Capabilities & (1UL << 4)) != 0; // CAP_FSETID

    internal bool Includes(uint group)
    {
        string id = group.ToString(CultureInfo.InvariantCulture);
        return GroupId == id || SupplementaryGroupIds.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(id, StringComparer.Ordinal);
    }

    internal Principal[] Choices(PermissionAuthority authority) => SupplementaryGroupIds.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Append(GroupId).Distinct(StringComparer.Ordinal).Select(id => new Principal(PrincipalKind.UnixGroup, id, authority.Id)).Order().ToArray();
}

internal sealed unsafe partial class LinuxPermissionService
{
    private const string CapabilitiesAttribute = "security.capability";
    private const string CapabilitiesSource = "Linux security.capability on the retained object";
    private const ushort RegularModeBits = 0x1ff, GroupExecute = 0x8, SetUserId = 0x800;

    internal static LinuxGroupAuthority ReadGroupAuthority(CancellationToken ct)
    {
        try
        {
            using var proc = OpenOwnProc(out string pid, ct);
            return ReadGroupAuthority(proc, pid);
        }
        catch (ReadFailure failure) { throw new IOException(failure.Message, failure); }
    }

    private static LinuxGroupAuthority ReadGroupAuthority(SafeFileHandle proc, string pid)
    {
        var current = ReadAccessCredentials(proc, pid);
        if (current.EffectiveUid == "0" || current.EffectiveUid != current.FileSystemUid || current.EffectiveGid != current.FileSystemGid)
            throw new UnauthorizedAccessException("This group edit requires an ordinary user with matching effective and filesystem IDs.");
        return new(current.EffectiveUid, current.EffectiveGid, current.Groups,
            ulong.Parse(current.Capabilities, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
    }

    internal static PermissionChangePlan PreviewGroupChange(PermissionSnapshot original, PermissionDraft draft, LinuxGroupAuthority authority)
    {
        RequireUnprotected(original);
        if (!IsWritableObject(original)) throw new NotSupportedException("Group changes require a retained file or directory on a qualified writable Linux filesystem.");
        if (original.Owner.Value?.Id != authority.UserId) throw new UnauthorizedAccessException("The original object is not owned by this FileCat user.");
        if (draft.Scope != PermissionScope.Objects || draft.Changes.IsDefaultOrEmpty || draft.Changes.Length > 2 ||
            draft.Changes.Count(change => change is AssignPermissionPrincipal { IsOwner: false }) != 1 ||
            draft.Changes.Any(change => change is not (AssignPermissionPrincipal { IsOwner: false } or
                ChangePermissionMode { Bits: <= PermissionBits, TouchedBits: <= PermissionBits })))
            throw new NotSupportedException("Review one group assignment and an optional mode change for this object.");
        var assignment = draft.Changes.OfType<AssignPermissionPrincipal>().Single();
        if (!TryGroupId(assignment.Principal, original.Subject.Authority.Id, out uint newGroup) || !authority.Includes(newGroup))
            throw new UnauthorizedAccessException("Choose a group in FileCat's ordinary credential.");
        if (original.Group.Value is not { } oldPrincipal || !TryGroupId(oldPrincipal, original.Subject.Authority.Id, out uint oldGroup))
            throw new NotSupportedException("The original group could not be verified.");
        ValidateKnownAcl(original);
        _ = RequireRevision(original, GroupChangeSections(original));

        var mode = draft.Changes.OfType<ChangePermissionMode>().SingleOrDefault();
        var expected = original;
        var stages = ImmutableArray.CreateBuilder<PermissionChangeStage>();
        bool setIdConsent = false;
        if (oldGroup != newGroup)
        {
            if (mode is not null)
            {
                // Remove rights first. The new owning group must never inherit the group-class rights being removed.
                ushort removed = (ushort)(original.Mode.Value!.Value & mode.TouchedBits & ~mode.Bits);
                ModeStep(new(0, removed), oldGroup);
            }
            var beforeGroup = expected;
            ushort groupMode = beforeGroup.Mode.Value!.Value;
            var fileCapabilities = beforeGroup.FileCapabilities!;
            if (original.Binding!.Identity.Kind == PermissionObjectKind.File)
            {
                groupMode &= unchecked((ushort)~SetUserId);
                if ((groupMode & GroupExecute) != 0 || !authority.CanKeepSetGroupId(oldGroup))
                    groupMode &= unchecked((ushort)~SetGroupId);
                fileCapabilities = fileCapabilities with { Status = PermissionReadStatus.Absent, Value = null, NativeError = null };
            }
            AddStage(assignment, beforeGroup with
            {
                Group = beforeGroup.Group with { Value = assignment.Principal },
                Mode = beforeGroup.Mode with { Value = groupMode },
                FileCapabilities = fileCapabilities
            }, "fchownat (held descriptor)");
            setIdConsent |= mode is not null && (mode.TouchedBits & mode.Bits & SetIdBits) != 0;
        }
        if (mode is not null)
        {
            ModeStep(new(mode.Bits, (ushort)(mode.TouchedBits & RegularModeBits)), newGroup);
            ModeStep(new(mode.Bits, (ushort)(mode.TouchedBits & ~RegularModeBits)), newGroup);
        }
        return new(expected, stages.ToImmutable(), setIdConsent);

        void ModeStep(ChangePermissionMode change, uint group)
        {
            if (change.TouchedBits == 0) return;
            var updated = ForecastMode(expected, change, acceptSetIdClear: true, out ushort bits, authority.CanKeepSetGroupId(group));
            if (updated.Mode.Value != expected.Mode.Value)
                AddStage(new ChangePermissionMode(bits, PermissionBits), updated, "fchmodat2 (held descriptor)");
        }

        void AddStage(PermissionDelta change, PermissionSnapshot updated, string nativeCall)
        {
            setIdConsent |= (expected.Mode.Value!.Value & SetIdBits & ~updated.Mode.Value!.Value) != 0;
            stages.Add(new(change, updated, nativeCall));
            expected = updated;
        }
    }

    internal static PermissionSections GroupChangeSections(PermissionSnapshot snapshot) =>
        SecuritySections(snapshot.Binding!) | PermissionSections.FileCapabilities;

    private static (ReviewedPermissionTarget Target, PermissionSnapshot Expected) PrepareGroupTarget(PermissionInspection inspection,
        ItemRef source, PermissionDraft draft, LinuxGroupAuthority authority, bool acceptSetIdClear, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) throw new UnauthorizedAccessException(refusal);
        var original = inspection.Snapshot;
        if (original.Subject.RawLocator != source.FileSystemPath)
            throw new NotSupportedException("This group change requires the original inspected object.");
        var plan = PreviewGroupChange(original, draft, authority);
        if (plan.NeedsSetIdConsent && !acceptSetIdClear)
            throw new InvalidOperationException("Accept the reviewed set-ID effects before applying the group change.");
        if (plan.Stages.IsEmpty) throw new InvalidOperationException("The requested group and mode already match.");
        var sections = GroupChangeSections(original);
        var startingRevision = RequireRevision(original, sections);
        var before = startingRevision;
        var components = ImmutableArray.CreateBuilder<ReviewedPermissionComponent>(plan.Stages.Length);
        foreach (var step in plan.Stages)
        {
            var after = RequireRevision(step.Expected, sections);
            components.Add(new(step.Change, before, after, step.NativeCall));
            before = after;
        }
        return BindPreparedTarget(inspection, source, plan.Expected, components.MoveToImmutable(), startingRevision, ct, groupAuthority: authority);
    }

    private static bool TryGroupId(Principal principal, string authority, out uint group)
    {
        group = 0;
        return principal.Kind == PrincipalKind.UnixGroup && principal.Authority == authority && PosixAclEvaluation.TryParseId(principal.Id, out group);
    }

    private static PermissionObservation<PermissionFileCapabilities> ReadFileCapabilities(SafeFileHandle proc, string pid,
        SafeFileHandle pin, Stamp stamp, CancellationToken ct)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PermissionObservation<PermissionFileCapabilities> observation = new(PermissionReadStatus.Unloaded, CapabilitiesSource, now);
        try
        {
            WithProcPin(proc, pid, pin, stamp, ct, entry =>
            {
                Span<byte> bytes = stackalloc byte[24];
                nint length;
                int error;
                fixed (byte* buffer = bytes)
                {
                    length = GetXattr(entry, CapabilitiesAttribute, buffer, (nuint)bytes.Length);
                    error = length < 0 ? Marshal.GetLastPInvokeError() : 0;
                }
                observation = error switch
                {
                    NoAttribute => new(PermissionReadStatus.Absent, CapabilitiesSource, now),
                    0 when length is 12 or 20 or 24 => new(PermissionReadStatus.Present, CapabilitiesSource, now,
                        new(ImmutableArray.Create(bytes[..(int)length]))),
                    0 or Range => new(PermissionReadStatus.Incomplete, CapabilitiesSource, now, Reason: "The file capability size is unrecognized."),
                    _ => new(ErrorStatus(error), CapabilitiesSource, now, Reason: "The kernel did not return file capabilities.", NativeError: error)
                };
            });
            return observation;
        }
        catch (ReadFailure failure) { return new(failure.Status, CapabilitiesSource, now, Reason: failure.Message, NativeError: failure.Error); }
    }

    [LibraryImport("libc", EntryPoint = "fchownat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int FchownAt(SafeFileHandle pin, string name, uint uid, uint gid, int flags);
}
