using System.Collections.Immutable;

namespace FileCat.Core.Permissions;

internal sealed unsafe partial class DarwinPermissionService
{
    private enum AclDirection { Restricts, Grants, Mixed }

    internal static PermissionChangePlan PreviewAccessChanges(PermissionSnapshot original, PermissionDraft draft,
        DarwinGroupAuthority? authority = null)
    {
        if (ModeRefusal(original) is { } refusal) throw new NotSupportedException(refusal);
        if (draft.Scope != PermissionScope.Objects || draft.Changes.IsDefaultOrEmpty || draft.Changes.Length > 4 ||
            draft.Changes.Select(change => change.Section).Distinct().Count() != draft.Changes.Length ||
            draft.Changes.Any(change => change is not (AssignPermissionPrincipal { IsOwner: false } or DarwinAclDelta or
                ChangePermissionMode { Bits: <= PermissionBits, TouchedBits: <= PermissionBits } or ChangePermissionFlags)))
            throw new NotSupportedException("Review the original Darwin ACL, group, mode and file protection.");

        var assignment = draft.Changes.OfType<AssignPermissionPrincipal>().SingleOrDefault();
        var acl = draft.Changes.OfType<DarwinAclDelta>().SingleOrDefault();
        var mode = draft.Changes.OfType<ChangePermissionMode>().SingleOrDefault();
        var (oldGroup, newGroup) = assignment is null ? (0u, 0u) : ReviewGroup(original, assignment, authority);
        bool transfersGroup = oldGroup != newGroup;
        uint activeGroup = oldGroup;
        ushort beforeMode = original.Mode.Value!.Value;
        ushort wanted = mode is null ? beforeMode : (ushort)((beforeMode & ~mode.TouchedBits) | (mode.Bits & mode.TouchedBits));
        if (transfersGroup)
            wanted = (ushort)((wanted & ~SetId) | ((mode?.Bits ?? 0) & (mode?.TouchedBits ?? 0) & SetId));

        var expected = original;
        var stages = ImmutableArray.CreateBuilder<PermissionChangeStage>();
        bool needsConsent = transfersGroup && (wanted & SetId) != 0;
        bool descriptor = original.Binding!.DarwinRoute == DarwinBindingRoute.Descriptor;
        bool checkedDirectory = !descriptor && original.Binding.Identity.Kind == PermissionObjectKind.Directory;
        var order = OrderDarwinChanges(original, draft.Changes);
        foreach (var change in order.OfType<ChangePermissionFlags>().Where(flags => flags.Bits == 0 && flags.TouchedBits != FinderHidden))
            FlagStep(change);

        AclDirection? direction = null;
        if (acl is not null)
        {
            var endpoint = ProjectAcl(expected, acl, verifyExchange: false).Snapshot;
            if (RequireRevision(expected, WriteSecuritySections) != RequireRevision(endpoint, WriteSecuritySections))
                direction = Direction(original.Access.Value?.Acl as DarwinAcl, acl);
        }

        // BSD permissions removed by the draft cannot be used while an ACL denial is being removed.
        // The sticky bit restricts removal on a directory, so setting it belongs with restrictions.
        int remove = beforeMode & ~wanted & (AccessBits | (transfersGroup ? 0 : SetId));
        ushort floor = (ushort)((beforeMode & ~remove) | (wanted & StickyBit));
        if (transfersGroup && direction == AclDirection.Mixed)
        {
            // For an ACL change with both effects, use the same group/other permissions while the
            // group class switches. Each affected class then has at most its old or final BSD bits
            // at every step, even for users whose class changes. ACL UUID matching is credential based.
            int shared = ((floor >> 3) & floor) & 7;
            floor = (ushort)((floor & ~0x3f) | (shared << 3) | shared);
        }
        if (transfersGroup && floor != beforeMode) floor = (ushort)(floor & ~SetId);
        // Keep the established single mode write when the ACL and group are unchanged.
        if (transfersGroup || direction is not null) ModeStep(floor);

        if (direction == AclDirection.Restricts) AclStep(acl!);
        if (transfersGroup)
        {
            AddStage(assignment!, expected with
            {
                Group = expected.Group with { Value = assignment!.Principal },
                Mode = expected.Mode with { Value = (ushort)(expected.Mode.Value!.Value & ~SetId) }
            }, descriptor ? "fchown group (retained descriptor)" : "fchownat group (checked parent and name)");
            activeGroup = newGroup;
        }

        // A pure ACL grant may safely be last when it opens a checked directory to other accounts.
        // Once that boundary is opened, the caller cannot issue another checked-parent write.
        bool aclLast = checkedDirectory && direction == AclDirection.Grants &&
            OpensDirectory(ProjectAcl(expected, acl!, verifyExchange: false).Snapshot);
        if (!aclLast && direction is AclDirection.Grants or AclDirection.Mixed) AclStep(acl!);
        if (transfersGroup || direction is not null)
        {
            ushort keepSpecial = (ushort)(expected.Mode.Value!.Value & SetId);
            ModeStep((ushort)((wanted & ~SetId) | keepSpecial));
            needsConsent |= direction is not null && (wanted & SetId & ~beforeMode) != 0;
        }
        ModeStep(wanted);
        if (aclLast) AclStep(acl!);
        foreach (var change in order.OfType<ChangePermissionFlags>().Where(flags => flags.Bits != 0 || flags.TouchedBits == FinderHidden))
            FlagStep(change);

        if (checkedDirectory && stages.Take(stages.Count - 1).Any(stage => OpensDirectory(stage.Expected)))
            throw new NotSupportedException("This checked-parent review must open directory access in its last write. Review the directory grants separately.");
        return new(expected, stages.ToImmutable(), needsConsent);

        void ModeStep(ushort bits)
        {
            if (bits == expected.Mode.Value) return;
            var change = new ChangePermissionMode(bits, PermissionBits);
            var projected = ProjectMode(expected, change, acceptSetIdClear: true,
                groupMember: assignment is null ? null : authority!.Includes(activeGroup));
            AddStage(change, projected, descriptor ? "fchmod (retained descriptor)" : "fchmodat (checked parent and name)");
        }

        void AclStep(DarwinAclDelta change) => AddStage(change, ProjectAcl(expected, change).Snapshot, DescribeAclCall(change, descriptor));

        void FlagStep(ChangePermissionFlags change)
        {
            var projected = PreviewFlags(expected, change);
            if (projected.Flags.Value != expected.Flags.Value) AddStage(change, projected, DescribeFlagCall(change, descriptor));
        }

        void AddStage(PermissionDelta change, PermissionSnapshot projected, string call)
        {
            needsConsent |= (expected.Mode.Value!.Value & SetId & ~projected.Mode.Value!.Value) != 0;
            stages.Add(new(change, projected, call));
            expected = projected;
        }
    }

    private static AclDirection Direction(DarwinAcl? baseline, DarwinAclDelta delta)
    {
        return delta switch
        {
            AddDarwinAce add => add.Entry.Kind == DarwinAceKind.Deny ? AclDirection.Restricts : AclDirection.Grants,
            ChangeDarwinAce { Replacement: null } remove => remove.Expected.Kind == DarwinAceKind.Allow ? AclDirection.Restricts : AclDirection.Grants,
            ChangeDarwinAce { Replacement: { } after } edit => Direction(edit.Expected, after),
            RemoveDarwinAcl when baseline!.Entries.All(entry => entry.Kind == DarwinAceKind.Allow) => AclDirection.Restricts,
            RemoveDarwinAcl when baseline!.Entries.All(entry => entry.Kind == DarwinAceKind.Deny) => AclDirection.Grants,
            _ => AclDirection.Mixed
        };
    }

    private static AclDirection Direction(DarwinAce before, DarwinAce after)
    {
        if (before.Kind != after.Kind) return after.Kind == DarwinAceKind.Deny ? AclDirection.Restricts : AclDirection.Grants;
        if (before.Principal != after.Principal || ((before.Flags ^ after.Flags) & ~0xfu) != 0) return AclDirection.Mixed;
        uint removed = before.Rights & ~after.Rights, added = after.Rights & ~before.Rights;
        if (removed != 0 && added != 0) return AclDirection.Mixed;
        return (added != 0) == (after.Kind == DarwinAceKind.Allow) ? AclDirection.Grants : AclDirection.Restricts;
    }

    private static bool OpensDirectory(PermissionSnapshot snapshot)
    {
        if ((snapshot.Mode.Value!.Value & GroupOrOtherWrite) != 0) return true;
        try { AdmitDirectoryAcl(snapshot.Access, parent: false, EffectiveUser(), snapshot.Subject.Authority.Id); }
        catch (NotSupportedException) { return true; }
        return false;
    }
}
