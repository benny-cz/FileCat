using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    /// <summary>Internal qualification composition. The product platform continues to control mutation availability.</summary>
    internal IStorageTransforms CreateEfsJobCandidate(
        Func<Job, string, StorageEfsConsent, CancellationToken, StorageEfsRecoveryApproval?> recoveryDecision) =>
        new EfsJobs(this, recoveryDecision ?? throw new ArgumentNullException(nameof(recoveryDecision)));

    private sealed class EfsJobs(WindowsStorageTransforms adapter,
        Func<Job, string, StorageEfsConsent, CancellationToken, StorageEfsRecoveryApproval?> recoveryDecision)
        : IStorageTransforms, IStorageEfsJobAdapter
    {
        public StorageProblem? Unavailable(StorageAction action)
        {
            if (!StorageJournalPolicy.IsEfs(action)) return adapter.Unavailable(action);
            return IsQualifiedHost && EfsEdition.Value ? null : Problem(StorageReasonKind.WindowsLimitation, "efs-build",
                "This EFS candidate requires Windows 11 x64 Pro, Enterprise or Education.");
        }

        public StorageProblem? ScopeUnavailable(StorageScope scope) => Enum.IsDefined(scope) ? null
            : Problem(StorageReasonKind.FileCatBoundary, "efs-scope", "Choose an explicit EFS scope.");

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
            return new EfsTraversal(binding);
        }
    }

    private sealed class EfsTraversal(EfsPathBinding root) : IStorageTraversal
    {
        public IStorageObjectLease Root => root;
        public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct) => root.Ancestors(ct);
        public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct)
        {
            foreach (var entry in Entries(directory, ct))
                yield return entry.Object ?? throw new StorageInspectionException(entry.Problem ??
                    Problem(StorageReasonKind.Unknown, "child-open", "The discovered EFS child could not be admitted."));
        }
        public IEnumerable<StorageTraversalEntry> Entries(IStorageObjectLease directory, CancellationToken ct)
        {
            if (directory is not EfsPathBinding parent || !parent.BelongsTo(root))
                throw new ArgumentException("The directory does not belong to this EFS traversal.", nameof(directory));
            return parent.EnumerateChildren(ct);
        }
        public void Dispose() => root.Dispose();
    }

    private sealed partial class EfsPathBinding
    {
        private WindowsStorageDirectoryCursor? _cursor;
        private bool _enumerating;

        internal bool BelongsTo(EfsPathBinding root) => ReferenceEquals(_root, root);

        internal IReadOnlyList<StorageObjectIdentity> Ancestors(CancellationToken ct)
        {
            Check(); ValidateSpelling(ct);
            return _path.Nodes.Take(_path.Nodes.Count - 1).Select(node => node.Facts.Identity).ToArray();
        }

        internal IEnumerable<StorageTraversalEntry> EnumerateChildren(CancellationToken ct)
        {
            if (_consent.Scope != StorageScope.ExistingDescendants)
                throw Refuse("efs-scope", "The reviewed EFS scope covers selected items only.");
            if (_path.Nodes.Count >= 64)
                throw Refuse("depth-limit", "The held EFS path reached the 64-level limit. Additional contents were not discovered.");
            RequireDirectory(ct);
            EnsureGuards(ct); // Even an already-satisfied directory must guard itself while holding its cursor.
            _cursor ??= new(_path.Leaf.File);
            if (_enumerating) throw new InvalidOperationException("This held EFS directory cursor is already being consumed.");
            _enumerating = true;
            try
            {
                while (!_cursor.IsComplete)
                {
                    RequireDirectory(ct);
                    foreach (var entry in _cursor.ReadBatch(ct)) yield return OpenChildEntry(entry, ct);
                }
            }
            finally { _enumerating = false; }
        }

        private void RequireDirectory(CancellationToken ct)
        {
            var observed = Observe(ct);
            if (observed.Ineligible is { } problem) throw new StorageInspectionException(problem);
            if (!observed.IsDirectory) throw Refuse("efs-directory", "Only a held EFS directory can enumerate children.");
        }

        private StorageTraversalEntry OpenChildEntry(WindowsStorageDirectoryCursor.Entry entry, CancellationToken ct)
        {
            RequireDirectory(ct);
            string captured = Path + "\\" + entry.Name;
            if (captured.Length > 32767) return new(captured, entry.IsDirectory, null,
                Problem(StorageReasonKind.FileCatBoundary, "path-limit", "A discovered EFS path exceeds the qualified length."));

            Held? child = null;
            NativePath? borrowed = null;
            try
            {
                child = Open(_path.Leaf.File, entry.Name, directory: false, pin: false, ct);
                var facts = Facts(child.File, _path.Volume.Binding, ct);
                var cursorProblem = CompareCursorEntry(facts, entry);
                borrowed = _path.BorrowAncestorsAndAppend(new(child, facts));
                child = null;
                var binding = new EfsPathBinding(_pin, borrowed, _windows, _consent, _invoke, _recovery, _requireRecovery,
                    _resolveRecovery, this, captured, cursorProblem);
                borrowed = null;
                return new(captured, facts.IsDirectory, binding, null);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                int code = error.HResult & 0xffff;
                var problem = error is StorageInspectionException inspection ? inspection.Problem :
                    Problem(error is UnauthorizedAccessException ? StorageReasonKind.PermissionOrPolicy : code is 2 or 3 ? StorageReasonKind.IdentityChanged : StorageReasonKind.TransientFailure,
                        code is 32 or 33 ? "sharing-violation" : "child-open", "The discovered EFS child could not be admitted. " + error.Message);
                return new(captured, entry.IsDirectory, null, problem) { Retry = token => OpenChildEntry(entry, token) };
            }
            finally { borrowed?.Dispose(); child?.Dispose(); }
        }
    }
}
