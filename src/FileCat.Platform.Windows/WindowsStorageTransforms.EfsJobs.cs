using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    /// <summary>Internal qualification composition. The product platform continues to control mutation availability.</summary>
    internal IStorageTransforms CreateEfsJobCandidate(
        Func<Job, string, StorageEfsConsent, CancellationToken, StorageEfsRecoveryApproval?> recoveryDecision) =>
        new SelectedEfsJobs(this, recoveryDecision ?? throw new ArgumentNullException(nameof(recoveryDecision)));

    private sealed class SelectedEfsJobs(WindowsStorageTransforms adapter,
        Func<Job, string, StorageEfsConsent, CancellationToken, StorageEfsRecoveryApproval?> recoveryDecision)
        : IStorageTransforms, IStorageEfsJobAdapter
    {
        public StorageProblem? Unavailable(StorageAction action)
        {
            if (!StorageJournalPolicy.IsEfs(action)) return adapter.Unavailable(action);
            return IsQualifiedHost && EfsEdition.Value ? null : Problem(StorageReasonKind.WindowsLimitation, "efs-build",
                "This EFS candidate requires Windows 11 x64 Pro, Enterprise or Education.");
        }

        public StorageProblem? ScopeUnavailable(StorageScope scope) => scope == StorageScope.SelectedItems ? null
            : Problem(StorageReasonKind.FileCatBoundary, "efs-recursion-unqualified", "This EFS candidate can change selected items. Recursive EFS changes require further qualification.");

        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct) =>
            action is StorageAction.EncryptEfs or StorageAction.DecryptEfs
                ? adapter.InspectEfsRoot(target, action.Value, ct) : adapter.InspectRoot(target, action, ct);

        public StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action) =>
            action is StorageAction.EncryptEfs or StorageAction.DecryptEfs || adapter.RequirePin(root).InvokerSid is not null
                ? null : adapter.EvaluatePinnedRoot(root, action);

        public StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct)
        {
            var pin = adapter.RequirePin(root);
            bool efs = action is StorageAction.EncryptEfs or StorageAction.DecryptEfs;
            if (!efs && pin.InvokerSid is null) return adapter.InspectPinnedRoot(root, action, ct);
            if (!efs || pin.InvokerSid is null) throw Refuse("efs-review-action", "Open a new review to switch between EFS and compression.");
            RequireEfsHost(action!.Value);
            StorageReviewResources.RequireRootInspection(pin.Target);
            if (EfsInvoker() != pin.InvokerSid) throw Changed("efs-invoker", "The invoking EFS user changed; create a new review.");
            using var path = Walk(pin.Target.Path, reviewLeaf: false, ct);
            if (path.LeafFacts.Identity != pin.Observation.Identity || Facts(pin.Handle.File, path.Volume.Binding, ct).Identity != pin.Observation.Identity)
                throw Changed("review-stale", "The captured EFS path no longer names its original reviewed object.");
            using var windows = adapter.ProtectedWindowsPath(ct);
            return pin.Update(Inspect(path, windows.LeafFacts.Identity, ct, compressionPolicy: false), action);
        }

        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) =>
            adapter.OpenReviewedRoot(root, action, ct);

        public IStorageTraversal OpenReviewedEfsRoot(IStorageReviewLease root, Job job, int sourceIndex,
            StorageEfsRecoverySession recovery, CancellationToken ct)
        {
            if (ScopeUnavailable(recovery.Consent.Scope) is { } unavailable) throw new StorageInspectionException(unavailable);
            var pin = adapter.RequirePin(root);
            var binding = adapter.OpenEfsBinding(pin, recovery.Consent, null, true, ct, InvokeEfs, sourceIndex,
                token => recovery.Resolve(job, pin.InvokerSid!, recoveryDecision, token));
            return new SelectedTraversal(binding);
        }
    }

    private sealed class SelectedTraversal(IStorageObjectLease root) : IStorageTraversal
    {
        public IStorageObjectLease Root { get; } = root;
        public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct) =>
            throw new NotSupportedException("Selected-items EFS does not inspect recursive overlap.");
        public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct) =>
            throw new NotSupportedException("Recursive EFS is unavailable in this qualification lane.");
        public void Dispose() => Root.Dispose();
    }
}
