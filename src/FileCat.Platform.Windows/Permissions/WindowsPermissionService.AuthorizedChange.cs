using System.Collections.Immutable;
using System.ComponentModel;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Permissions;

internal sealed unsafe partial class WindowsPermissionService
{
    internal static bool SupportsAuthorizedChange(ElevatedStep step)
    {
        if (step is not
            {
                Verb: ElevatedVerb.ChangeFileSecurity,
                Path: { } path,
                FileSecurity: { Scope: PermissionScope.Objects } scope
            }) return false;
        if (scope.Kind == PermissionObjectKind.VolumeRoot)
            return NtfsMetadataVolume.CanonicalGuid(path) == path && scope.FileIds is { Count: 1 } &&
                scope.Sections == PermissionSections.Access && IsRuleEdit(scope.Change);
        return scope.Kind == PermissionObjectKind.File &&
            (scope is { Sections: PermissionSections.Owner, Change.Operation: ElevatedFileSecurityOperation.AssignOwner } ||
                scope.Sections is PermissionSections.Access or PermissionSections.Audit && IsRuleEdit(scope.Change));
    }

    /// <summary>
    /// The helper's reply when the acting administrator's token lacks a privilege the reviewed route needs: every
    /// requested section denied, or a change refused before consent, so nobody approves an operation that cannot run.
    /// Null lets the request proceed. An owner assignment needs restore privilege only after a denied WRITE_OWNER open.
    /// </summary>
    internal static FileSecurityReply? RefuseWithoutPrivileges(ElevatedStep step, Func<string[], IReadOnlyList<string>> missing)
    {
        var scope = step.FileSecurity!;
        if (step.Verb == ElevatedVerb.InspectFileSecurity)
        {
            bool discretionary = (scope.Sections & DiscretionarySections) != 0;
            bool audit = (scope.Sections & PermissionSections.Audit) != 0;
            bool noBackup = missing(["SeBackupPrivilege"]).Count != 0;
            bool noAudit = audit && (noBackup || missing(["SeSecurityPrivilege"]).Count != 0);
            if (discretionary && !noBackup || audit && !noAudit) return null;
            return new(FileSecurityOutcome.Inspected, new(Denied(discretionary), Denied(audit)));
        }
        string[] required = scope.Sections switch
        {
            PermissionSections.Owner => ["SeBackupPrivilege"],
            PermissionSections.Audit => ["SeBackupPrivilege", "SeRestorePrivilege", "SeSecurityPrivilege"],
            _ => ["SeBackupPrivilege", "SeRestorePrivilege"],
        };
        return missing(required).Count == 0 ? null : new(FileSecurityOutcome.ChangeReported,
            Apply: new(FileSecurityChangeStatus.FailedBeforeChange, WindowsFileSecurityPrivilege.PrivilegeNotHeld));

        static FileSecurityDescriptorRead Denied(bool requested) => requested
            ? new(PermissionReadStatus.Denied, NativeError: WindowsFileSecurityPrivilege.PrivilegeNotHeld)
            : new(PermissionReadStatus.Unloaded);
    }

    private static bool IsRuleEdit(ElevatedFileSecurityChange? change) => change is
        {
            Operation: ElevatedFileSecurityOperation.AddRule or ElevatedFileSecurityOperation.ReplaceRule or
                ElevatedFileSecurityOperation.RemoveRule
        };

    internal static PermissionSections AuthorizedRevisionSections(PermissionSections changeSection) =>
        DiscretionarySections | (changeSection == PermissionSections.Audit ? PermissionSections.Audit : PermissionSections.None);

    internal static PermissionSnapshot PreviewAuthorizedChange(PermissionSnapshot original, PermissionDelta delta) =>
        ProjectAuthorizedChange(original, delta).Expected;

    // A volume root, and a file DACL stored without SE_DACL_AUTO_INHERITED (see HasLegacyDacl), are written as a descriptor.
    private static bool WritesAuthorizedDescriptor(PermissionSnapshot original, PermissionDelta delta) =>
        original.Binding?.Identity.Kind == PermissionObjectKind.VolumeRoot ||
        delta is AddWindowsAce or ChangeWindowsAce && HasLegacyDacl(original);

    private static (PermissionSnapshot Expected, uint Section, ImmutableArray<byte> Bytes) ProjectAuthorizedChange(
        PermissionSnapshot original, PermissionDelta delta)
    {
        if (original.Binding?.Identity.Kind == PermissionObjectKind.VolumeRoot) return ProjectFolderOnly(original, delta);
        if (WritesAuthorizedDescriptor(original, delta))
        {
            var (expected, section, acl) = Project(original, delta);
            return (expected, section, ObjectDaclDescriptor(original.Inheritance.Value!.Value, acl, original.Subject.Authority.Id));
        }
        if (delta is not ChangeWindowsAuditAce audit) return Project(original, delta);
        if (original.Audit.Value?.Acl is not WindowsSacl baseline)
            throw new NotSupportedException("The complete system ACL is required to edit an audit entry.");
        var projected = WindowsPermissionAuditPatch.ProjectFile(baseline, audit, original.Subject.Authority.Id);
        return (original with
        {
            Audit = original.Audit with { Status = PermissionReadStatus.Present, Value = new PermissionAclValue(projected) }
        }, SaclInformation, WindowsPermissionDescriptor.SaclWriteDescriptor(projected));
    }

    private static PermissionRevision RequireAuthorizedRevision(PermissionSnapshot snapshot, PermissionSections changeSection) =>
        PermissionRevision.Read(snapshot, AuthorizedRevisionSections(changeSection)) ??
        throw new IOException("The security sections needed to verify the Windows change are unavailable.");

    internal readonly record struct OwnerAssignment(Principal Principal, WindowsPermissionToken.Stamp RequesterToken,
        WindowsPermissionToken.Stamp ActingToken, bool CanUseWriteOwner)
    {
        internal static OwnerAssignment Review(Principal requested, WindowsPermissionToken.OwnerSet requester, WindowsPermissionToken.OwnerSet actor)
        {
            if (!requester.Choices.Contains(requested))
                throw new UnauthorizedAccessException("The selected owner is not assignable by the authenticated requesting token.");
            return new(requested, requester.Stamp, actor.Stamp, actor.Choices.Contains(requested));
        }

        internal void Verify(WindowsPermissionToken.OwnerSet requester, WindowsPermissionToken.OwnerSet actor)
        {
            if (RequesterToken != requester.Stamp || ActingToken != actor.Stamp ||
                !requester.Choices.Contains(Principal) || CanUseWriteOwner != actor.Choices.Contains(Principal))
                throw new PermissionBindingConflictException("A Windows token or its assignable owner choices changed during authorization.");
        }
    }

    internal static (string Description, OwnerAssignment? Owner) DescribeAuthorizedChange(ElevatedStep step,
        NtfsMetadataPeer requester, NtfsMetadataPeer actor, CancellationToken cancellation)
    {
        var (root, names, subject) = AuthorizedChangeSubject(step);
        var reviewed = step.FileSecurity!;
        bool audit = reviewed.Sections == PermissionSections.Audit;
        OwnerAssignment? owner = reviewed.Change is { Operation: ElevatedFileSecurityOperation.AssignOwner, OwnerSid: { } ownerSid }
            ? OwnerAssignment.Review(new(PrincipalKind.WindowsSid, ownerSid, subject.Authority.Id),
                requester.ReadOwnerChoices(subject.Authority.Id), actor.ReadOwnerChoices(subject.Authority.Id))
            : null;
        string description = WindowsFileSecurityPrivilege.Read(audit, () =>
        {
            using var file = Walk(root, names, subject.Authority, cancellation, out var parent, out var chain, out var binding,
                leafReadAccess: ReadControl | (audit ? AccessSystemSecurity : 0), backupIntent: true);
            using (parent)
            {
                var (snapshot, _, _, _) = CheckAuthorizedChange(reviewed, requester.UserSid.Value, owner?.Principal,
                    subject, file, binding, chain, cancellation);
                var change = reviewed.Change!;
                return change.Operation == ElevatedFileSecurityOperation.AssignOwner
                    ? $"Owner {snapshot.Owner.Value!.Value.Id} → {change.OwnerSid}."
                    : $"{(audit ? "Audit" : "Discretionary ACL")} entry{(change.Index is int index ? $" {index + 1}" : "")}\n" +
                      $"Before {DescribeRule(change.OldRule)}\nAfter {DescribeRule(change.NewRule)}";
            }
        });
        return (description, owner);
    }

    internal static FileSecurityApply ApplyAuthorizedChange(ElevatedStep step, NtfsMetadataPeer requester, NtfsMetadataPeer actor,
        OwnerAssignment? ownerAssignment, Func<bool> isCurrent, CancellationToken cancellation)
    {
        bool dispatched = false;
        uint errorCode = 0;
        try
        {
            var (root, names, subject) = AuthorizedChangeSubject(step);
            var reviewed = step.FileSecurity!;
            bool owner = reviewed.Sections == PermissionSections.Owner;
            bool audit = reviewed.Sections == PermissionSections.Audit;
            bool volumeRoot = reviewed.Kind == PermissionObjectKind.VolumeRoot;
            if (owner != ownerAssignment.HasValue)
                throw new UnauthorizedAccessException("The owner change requires verified requester and administrator tokens.");
            if (ownerAssignment is { CanUseWriteOwner: true } &&
                WindowsFileSecurityPrivilege.WithOwnerAccess(() => Attempt(useRestore: false)) is { } applied)
                return applied;
            return WindowsFileSecurityPrivilege.Write(audit, () => Attempt(useRestore: true))!;

            FileSecurityApply? Attempt(bool useRestore)
            {
                CheckRequest();
                SafeFileHandle file;
                SafeFileHandle? parent;
                FileStamp[] chain;
                PermissionBinding binding;
                try
                {
                    uint access = ReadControl | (useRestore ? WriteDac : 0) | (owner ? WriteOwner : 0) | (audit ? AccessSystemSecurity : 0);
                    file = Walk(root, names, subject.Authority, cancellation, out parent, out chain, out binding,
                        leafReadAccess: access, backupIntent: true);
                }
                catch (ReadFailure failure) when (!useRestore && failure.Status == PermissionReadStatus.Denied)
                {
                    // Only a denied handle acquisition can fall back to the approved restore route.
                    return null;
                }
                using (file)
                using (parent)
                {
                    var (_, section, bytes, descriptorWrite) = CheckAuthorizedChange(reviewed, requester.UserSid.Value,
                        ownerAssignment?.Principal, subject, file, binding, chain, cancellation);
                    using var latest = Walk(root, names, subject.Authority, cancellation, out var latestParent, out var current, out var currentBinding,
                        backupIntent: true);
                    using (latestParent)
                    {
                        RequireReviewedTarget(reviewed, current, currentBinding);
                        if (ownerAssignment is { } assignment)
                            assignment.Verify(requester.ReadOwnerChoices(subject.Authority.Id), actor.ReadOwnerChoices(subject.Authority.Id));
                        if (binding != currentBinding || chain[^1] != current[^1] ||
                            Screen(file, binding.Identity.MountInstance, volumeRoot, subject.Authority, cancellation) != chain[^1])
                            return new FileSecurityApply(FileSecurityChangeStatus.Conflict);

                        CheckRequest();
                        fixed (byte* payload = bytes.AsSpan())
                        {
                            dispatched = true;
                            if (descriptorWrite)
                            {
                                int status = NtSetSecurityObject(file, section, payload);
                                errorCode = status < 0 ? (uint)StatusToDosError(status) : 0;
                            }
                            else errorCode = SetSecurityInfo(file, 1 /* SE_FILE_OBJECT */, section,
                                owner ? payload : null, null, section == DaclInformation ? payload : null, audit ? payload : null);
                        }

                        // Observe the same handle after dispatch, including when cancellation or a Win32 error races the write.
                        var after = Screen(file, binding.Identity.MountInstance, volumeRoot, subject.Authority, CancellationToken.None);
                        if (after.Identity != chain[^1].Identity || after.Links != reviewed.LinkCount)
                            return new FileSecurityApply(FileSecurityChangeStatus.Uncertain, errorCode);
                        var observed = ReadAuthorizedChangeSnapshot(subject, binding, file, after, audit, CancellationToken.None);
                        string revision = RequireAuthorizedRevision(observed, reviewed.Sections).Digest;
                        string before = reviewed.Change!.Before;
                        string expected = reviewed.Change.Expected;
                        if (errorCode == 0 && revision == expected)
                            return new FileSecurityApply(FileSecurityChangeStatus.Verified, ObservedRevision: revision);
                        return revision == before && errorCode is 5 or 87 or 1307 or 1314 or 1336
                            ? new FileSecurityApply(FileSecurityChangeStatus.FailedBeforeChange, errorCode, revision)
                            : new FileSecurityApply(FileSecurityChangeStatus.Uncertain, errorCode, revision);
                    }
                }
            }

            void CheckRequest()
            {
                cancellation.ThrowIfCancellationRequested();
                if (!isCurrent()) throw new OperationCanceledException();
            }
        }
        catch (Exception ex) when (IsAuthorizedChangeFailure(ex))
        {
            if (dispatched) return new(FileSecurityChangeStatus.Uncertain, errorCode);
            if (IsAuthorizedBindingConflict(ex))
                return new(FileSecurityChangeStatus.Conflict);
            uint native = ex switch
            {
                ReadFailure { Error: { } readError } => unchecked((uint)readError),
                Win32Exception win32 => unchecked((uint)win32.NativeErrorCode),
                _ => 0,
            };
            return new(FileSecurityChangeStatus.FailedBeforeChange, native);
        }
    }

    internal static bool IsAuthorizedChangeFailure(Exception ex) =>
        ex is ReadFailure or IOException or Win32Exception or UnauthorizedAccessException or NotSupportedException or
            InvalidDataException or OverflowException;

    internal static bool IsAuthorizedBindingConflict(Exception ex) =>
        ex is ReadFailure { Status: PermissionReadStatus.Stale or PermissionReadStatus.Gone } or PermissionBindingConflictException;

    private static (string Root, string[] Names, PermissionSubject Subject) AuthorizedChangeSubject(ElevatedStep step)
    {
        if (!ElevationBroker.IsNativeFileSecurityHost || !SupportsAuthorizedChange(step))
            throw new NotSupportedException("The installed helper requires a reviewed security change on a local NTFS file or volume root.");
        string path = step.Path!;
        var (root, names) = SplitLocalPath(path);
        var authority = new PermissionAuthority(PermissionAuthorityKind.Local, "local", 0);
        return (root, names, new(Location.FileSystem(path), path, path, authority, step.FileSecurity!.Kind, PermissionLinkPolicy.NoFollow));
    }

    private static (PermissionSnapshot Before, uint Section, ImmutableArray<byte> Bytes, bool DescriptorWrite) CheckAuthorizedChange(
        ElevatedFileSecurityScope reviewed, string requesterSid, Principal? assignedOwner, PermissionSubject subject, SafeFileHandle file,
        PermissionBinding binding, FileStamp[] chain, CancellationToken cancellation)
    {
        RequireReviewedTarget(reviewed, chain, binding);
        if (binding.ReadOnly != false) throw new NotSupportedException("The reviewed Windows volume is read-only.");
        bool audit = reviewed.Sections == PermissionSections.Audit;
        var original = ReadAuthorizedChangeSnapshot(subject, binding, file, chain[^1], audit, cancellation);
        var change = reviewed.Change!;
        if (RequireAuthorizedRevision(original, reviewed.Sections).Digest != change.Before)
            throw new PermissionBindingConflictException("The reviewed security descriptor changed.");

        string ownerSid = original.Owner.Value?.Id ?? throw new IOException("The reviewed owning SID could not be read.");
        PermissionDelta delta;
        if (change.Operation == ElevatedFileSecurityOperation.AssignOwner)
        {
            if (assignedOwner is not { } principal || principal.Id != change.OwnerSid)
                throw new UnauthorizedAccessException("This operation requires the independently reviewed owner from the requesting token.");
            delta = new AssignPermissionPrincipal(true, principal);
        }
        else if (audit)
        {
            if (original.Audit.Value?.Acl is not WindowsSacl sacl)
                throw new NotSupportedException("The complete system ACL is required to edit an audit entry.");
            var replacement = change.NewRule is { } newRule ? WindowsPermissionAuditPatch.FileEntry(
                new(PrincipalKind.WindowsSid, newRule.Sid, subject.Authority.Id), newRule.Rights, newRule.Flags) : null;
            WindowsAuditAce? existing = null;
            if (change.Index is int index)
            {
                var old = change.OldRule!;
                if ((uint)index >= sacl.Entries.Length)
                    throw new PermissionBindingConflictException("The reviewed audit entry occurrence is no longer present.");
                existing = sacl.Entries[index];
                if (!WindowsPermissionAuditPatch.CanEditFileEntry(existing) ||
                    existing.Flags != old.Flags || existing.Mask != old.Rights || existing.Principal?.Id != old.Sid)
                    throw new PermissionBindingConflictException("The reviewed audit entry occurrence changed.");
            }
            delta = new ChangeWindowsAuditAce(change.Index, existing, replacement);
        }
        else
        {
            if (original.Access.Value?.Acl is not WindowsDacl { Form: WindowsDaclForm.Entries } acl)
                throw new NotSupportedException("A readable discretionary ACL with known entry structure is required.");
            var replacement = change.NewRule is { } added ? WindowsPermissionAclPatch.SimpleEntry(
                added.Type == ElevatedFileSecurityRuleType.Allow ? WindowsAceKind.Allow : WindowsAceKind.Deny,
                new(PrincipalKind.WindowsSid, added.Sid, subject.Authority.Id), added.Rights, added.Flags) : null;
            if (change.Index is int index)
            {
                var old = change.OldRule!;
                if ((uint)index >= acl.Entries.Length)
                    throw new PermissionBindingConflictException("The reviewed access rule occurrence is no longer present.");
                var existing = acl.Entries[index];
                byte type = old.Type == ElevatedFileSecurityRuleType.Allow ? (byte)0 : (byte)1;
                if (existing.Type != type || existing.Flags != old.Flags || existing.Mask != old.Rights || existing.Principal?.Id != old.Sid)
                    throw new PermissionBindingConflictException("The reviewed access rule occurrence changed.");
                delta = new ChangeWindowsAce(index, existing, replacement);
            }
            else
            {
                delta = new AddWindowsAce(replacement!);
            }
        }
        if (reviewed.Kind == PermissionObjectKind.File && reviewed.LinkCount > 1 && ownerSid != requesterSid &&
            !StrictlyRestrictsFileAccess(original.Access.Value?.Acl as WindowsDacl, delta))
            throw new UnauthorizedAccessException("A file owned by another account has additional hard-link names. Only a strictly restrictive change can be approved.");

        var projection = ProjectAuthorizedChange(original, delta);
        if (RequireAuthorizedRevision(projection.Expected, reviewed.Sections).Digest != change.Expected)
            throw new PermissionBindingConflictException("The projected descriptor differs from the reviewed result.");
        // Audit changes are always written as a SACL-only descriptor (see WindowsPermissionDescriptor.SaclWriteDescriptor).
        return (original, projection.Section, projection.Bytes, WritesAuthorizedDescriptor(original, delta) || delta is ChangeWindowsAuditAce);
    }

    private static PermissionSnapshot ReadAuthorizedChangeSnapshot(PermissionSubject subject, PermissionBinding binding,
        SafeFileHandle file, FileStamp stamp, bool audit, CancellationToken cancellation)
    {
        var now = DateTimeOffset.UtcNow;
        var candidate = PermissionSnapshots.Create(subject, PermissionModel.WindowsDescriptor, binding,
            audit ? DiscretionarySections | PermissionSections.Audit : DiscretionarySections, now, Source);
        var snapshot = audit
            ? ReadCompleteSecurityDescriptor(file, cancellation, descriptor =>
            {
                var ordinary = Decode(candidate, DiscretionarySections, descriptor, now);
                var sacl = WindowsPermissionDescriptor.Sacl(descriptor, subject.Authority.Id);
                return ordinary with { Audit = new(sacl.Present ? PermissionReadStatus.Present : PermissionReadStatus.Absent,
                    Source, now, new PermissionAclValue(sacl)) };
            })
            : ReadHeldDescriptor(candidate, DiscretionarySections, file, cancellation);
        if (Screen(file, binding.Identity.MountInstance, binding.Identity.Kind == PermissionObjectKind.VolumeRoot, subject.Authority, cancellation) != stamp)
            throw new PermissionBindingConflictException("The held Windows item changed while reading security.");
        return snapshot;
    }

    internal static bool StrictlyRestrictsFileAccess(WindowsDacl? acl, PermissionDelta delta)
    {
        if (acl is not { Form: WindowsDaclForm.Entries } ||
            acl.Entries.Any(rule => rule.Kind == WindowsAceKind.Opaque || rule.Principal?.Id == "S-1-3-4" /* OWNER RIGHTS */))
            return false;
        return delta switch
        {
            AddWindowsAce { Entry: { Kind: WindowsAceKind.Deny, Flags: 0, Mask: > 0, Principal: { Id: not "S-1-3-4" } } } => true,
            ChangeWindowsAce { Expected: { Kind: WindowsAceKind.Allow, Flags: 0 }, Replacement: null } => true,
            ChangeWindowsAce { Expected: { Flags: 0, Mask: uint oldMask } before, Replacement: { Flags: 0, Mask: uint newMask } after }
                when before.Kind == after.Kind && before.Principal == after.Principal =>
                StrictMaskChange(before.Kind, WindowsFileAccessRights.MapGeneric(oldMask), WindowsFileAccessRights.MapGeneric(newMask)),
            _ => false,
        };
    }

    private static bool StrictMaskChange(WindowsAceKind kind, uint before, uint after) => before != after &&
        (kind == WindowsAceKind.Allow && (after & ~before) == 0 || kind == WindowsAceKind.Deny && (before & ~after) == 0);

    private static string DescribeRule(ElevatedFileSecurityRule? rule)
    {
        if (rule is null) return "no entry";
        string kind = rule.Type != ElevatedFileSecurityRuleType.Audit ? rule.Type.ToString() :
            (rule.Flags & 0xc0) switch { 0x40 => "Audit success", 0x80 => "Audit failure", _ => "Audit success and failure" };
        return $"{kind} {rule.Sid}, mask 0x{rule.Rights:X8}, flags 0x{rule.Flags:X2}";
    }
}
