using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.Platform.Windows.Permissions;

internal sealed partial class WindowsPermissionService
{
    internal static ReviewedPermissionOperation PrepareAuthorizedChange(PermissionInspection inspection, ItemRef source,
        PermissionDraft draft, PermissionRevision accepted, bool portable, string exchangeRoot, nint owner, CancellationToken cancellation,
        bool approveVolumeRoot = false)
    {
        cancellation.ThrowIfCancellationRequested();
        var snapshot = inspection.Snapshot;
        if (portable || !ElevationBroker.IsNativeFileSecurityHost || !CanBindAuthorizedInspection(snapshot) ||
            snapshot.Binding is not { Identity.Kind: PermissionObjectKind.File or PermissionObjectKind.VolumeRoot, ReadOnly: false } binding ||
            source.FileSystemPath != snapshot.Subject.RawLocator || draft.Scope != PermissionScope.Objects || draft.Changes.Length != 1)
            throw new NotSupportedException("Review one security change on the original local NTFS file or volume root.");

        var delta = draft.Changes[0];
        bool volumeRoot = binding.Identity.Kind == PermissionObjectKind.VolumeRoot;
        if (volumeRoot && (!approveVolumeRoot || source.Kind != EntryKind.Directory || delta is not (AddWindowsAce or ChangeWindowsAce)))
            throw new NotSupportedException("Approve one discretionary access rule change for the reviewed volume root.");
        var before = RequireAuthorizedRevision(snapshot, delta.Section);
        var expected = RequireAuthorizedRevision(PreviewAuthorizedChange(snapshot, delta), delta.Section);
        if (accepted != expected || expected == before)
            throw new PermissionBindingConflictException("The projected Windows descriptor differs from the approved change.");
        using var requester = NtfsMetadataPeer.HoldSelf(elevated: false);
        if (!volumeRoot && binding.LinkCount > 1 && snapshot.Owner.Value?.Id != requester.UserSid.Value &&
            !StrictlyRestrictsFileAccess(snapshot.Access.Value?.Acl as WindowsDacl, delta))
            throw new UnauthorizedAccessException("A file owned by another account has additional hard-link names. Only a strictly restrictive change can be approved.");
        WindowsPermissionToken.Stamp? ownerToken = null;
        if (delta is AssignPermissionPrincipal { IsOwner: true } assign)
        {
            var owners = requester.ReadOwnerChoices(snapshot.Subject.Authority.Id);
            if (!owners.Choices.Contains(assign.Principal))
                throw new UnauthorizedAccessException("Select an owner SID assignable by the requesting Windows token.");
            ownerToken = owners.Stamp;
        }
        var (sections, change) = AuthorizedDelta(delta, before, expected);

        var pin = inspection.TransferLease<WindowsInspectionPin>() ??
            throw new IOException("The authorized inspection has ended. Inspect the item again.");
        try
        {
            var mutation = new AuthorizedFileMutation(snapshot, pin, delta, sections, change, ownerToken, portable, exchangeRoot, owner);
            mutation.CheckCurrent(before.Sections, cancellation);
            string sectionName = sections switch
            {
                PermissionSections.Owner => "owner",
                PermissionSections.Audit => "SACL",
                _ => "DACL"
            };
            var component = new ReviewedPermissionComponent(delta, before, expected,
                volumeRoot ? "Windows administrator NtSetSecurityObject volume-root DACL" : $"Windows administrator SetSecurityInfo {sectionName}");
            return new(PermissionScope.Objects, [new(source, snapshot.Subject, binding, [component], expected, mutation)]);
        }
        catch
        {
            pin.Dispose();
            throw;
        }
    }

    private static (PermissionSections Sections, ElevatedFileSecurityChange Change) AuthorizedDelta(PermissionDelta delta,
        PermissionRevision before, PermissionRevision expected)
    {
        return delta switch
        {
            AssignPermissionPrincipal { IsOwner: true } assign =>
                (PermissionSections.Owner, new(ElevatedFileSecurityOperation.AssignOwner, before.Digest, expected.Digest, OwnerSid: assign.Principal.Id)),
            AddWindowsAce add => (PermissionSections.Access,
                new(ElevatedFileSecurityOperation.AddRule, before.Digest, expected.Digest, NewRule: Rule(add.Entry))),
            ChangeWindowsAce edit => (PermissionSections.Access,
                new(edit.Replacement is null ? ElevatedFileSecurityOperation.RemoveRule : ElevatedFileSecurityOperation.ReplaceRule,
                    before.Digest, expected.Digest, Index: edit.Occurrence, OldRule: Rule(edit.Expected),
                    NewRule: edit.Replacement is { } replacement ? Rule(replacement) : null)),
            ChangeWindowsAuditAce audit => (PermissionSections.Audit,
                new(audit.Occurrence is null ? ElevatedFileSecurityOperation.AddRule : audit.Replacement is null
                    ? ElevatedFileSecurityOperation.RemoveRule : ElevatedFileSecurityOperation.ReplaceRule,
                    before.Digest, expected.Digest, Index: audit.Occurrence,
                    OldRule: AuditRule(audit.Expected), NewRule: AuditRule(audit.Replacement))),
            _ => throw new NotSupportedException("Choose an owner from the requesting token or one known access or audit entry."),
        };

        static ElevatedFileSecurityRule Rule(WindowsAce ace)
        {
            if (ace.Kind is not (WindowsAceKind.Allow or WindowsAceKind.Deny) || ace.Principal is not { } principal || ace.Mask is not { } mask)
                throw new NotSupportedException("The change requires a known allow or deny rule with a SID.");
            return new(ace.Kind == WindowsAceKind.Allow ? ElevatedFileSecurityRuleType.Allow : ElevatedFileSecurityRuleType.Deny,
                principal.Id, mask, ace.Flags);
        }

        static ElevatedFileSecurityRule? AuditRule(WindowsAuditAce? entry)
        {
            if (entry is null) return null;
            if (!WindowsPermissionAuditPatch.CanEditFileEntry(entry))
                throw new NotSupportedException("Choose a known explicit audit entry with a SID.");
            return new(ElevatedFileSecurityRuleType.Audit, entry.Principal!.Value.Id, entry.Mask!.Value, entry.Flags);
        }
    }

    private sealed class AuthorizedFileMutation(PermissionSnapshot original, WindowsInspectionPin pin, PermissionDelta delta,
        PermissionSections sections, ElevatedFileSecurityChange change, WindowsPermissionToken.Stamp? ownerToken, bool portable,
        string exchangeRoot, nint owner) : IBoundPermissionMutation
    {
        private bool _ready, _used;
        private FileSecurityApply? _result;
        private string? _detail;

        public PermissionSnapshot CheckCurrent(PermissionSections requested, CancellationToken cancellation)
        {
            _ready = false;
            if (_used || requested != AuthorizedRevisionSections(sections))
                throw new InvalidOperationException("The reviewed authorization already ran or has different security sections.");
            if (ownerToken is { } stamp && WindowsPermissionToken.CurrentStamp() != stamp)
                throw new PermissionBindingConflictException("The requesting Windows token changed after the owner was reviewed.");
            // The ordinary process may be unable to read the DACL. The helper independently reads and checks the revision
            // before consent and again on the write handle. This check holds the reviewed namespace unchanged until dispatch.
            CheckPinnedSecurityTarget(original, pin, requireOriginalStamp: true, cancellation);
            _ready = true;
            return original;
        }

        public PermissionMutationReturn Apply(PermissionDelta component) => Apply(component, CancellationToken.None);

        public PermissionMutationReturn Apply(PermissionDelta component, CancellationToken cancellation)
        {
            if (!_ready || _used || !ReferenceEquals(delta, component))
                throw new InvalidOperationException("The helper requires the rechecked reviewed change.");
            _used = true;
            bool sending = false;
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var plan = CreateFileSecurityPlan(original, pin, sections, change);
                var reply = ElevationBroker.RequestFileSecurityAsync(plan, portable, exchangeRoot, owner, cancellation, () => sending = true)
                    .GetAwaiter().GetResult();
                if (reply.Outcome == FileSecurityOutcome.Declined)
                    return new(CanceledBeforeChange: true, Detail: "Administrator approval was declined.");
                if (reply.Apply is not { } result)
                    return new(FailedWithoutDispatch: true, Detail: "The installed helper could not admit the reviewed change.");
                _result = result;
                int? nativeError = result.NativeError == 0 ? null : unchecked((int)result.NativeError);
                return result.Status switch
                {
                    FileSecurityChangeStatus.Conflict => new(BindingConflict: true),
                    FileSecurityChangeStatus.FailedBeforeChange when result.ObservedRevision is null =>
                        new(nativeError, FailedWithoutDispatch: true, Detail: "The helper refused the change before dispatch."),
                    FileSecurityChangeStatus.FailedBeforeChange =>
                        new(nativeError, RejectedBeforeChange: true, Detail: "Windows refused the write and the helper verified the original security."),
                    FileSecurityChangeStatus.Verified => new(),
                    _ => new(nativeError, Detail: "The helper could not verify the approved security after dispatch."),
                };
            }
            catch (OperationCanceledException) when (!sending && cancellation.IsCancellationRequested)
            {
                return new(CanceledBeforeChange: true);
            }
            catch (Exception error) when (IsAuthorizedChangeFailure(error) || error is TimeoutException or OperationCanceledException)
            {
                _detail = sending ? "The authorized request ended without a verified result. Inspect the original object again." : error.Message;
                return new(FailedWithoutDispatch: !sending, Detail: _detail);
            }
        }

        public PermissionReadBack Observe(PermissionSections requested)
        {
            if (requested != AuthorizedRevisionSections(sections) || _result is not { ObservedRevision: { } observed } result ||
                result.Status == FileSecurityChangeStatus.Uncertain && observed == change.Expected)
                return new(original, PermissionVerification.Unavailable, Detail: _detail);

            bool moved = false;
            string? detail = null;
            try { CheckPinnedSecurityTarget(original, pin, requireOriginalStamp: false, CancellationToken.None); }
            catch (PermissionBindingConflictException) when (result.Status == FileSecurityChangeStatus.Verified)
            {
                moved = true;
                detail = "The helper read back security on the original file. Its original namespace changed.";
            }
            catch (Exception error) when (IsAuthorizedChangeFailure(error))
            {
                detail = "The helper read back security on the original file. The original namespace could not be confirmed.";
            }
            return new(original, PermissionVerification.SameObject, moved, Detail: detail, AttestedRevision: new(requested, observed));
        }

        public void Dispose() => pin.Dispose();
    }
}
