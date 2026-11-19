using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    internal static StorageMutationResult ClassifyCompressionOutcome(StorageAction action, StorageObservation before,
        StorageObservation after, WindowsCompression.CallResult call)
    {
        if (after.Compression is StorageObservationState.Unknown or StorageObservationState.Unsupported || after.Ineligible?.Code == "inconsistent-storage")
            return new(before, after, StorageEffect.Uncertain, after.Ineligible ??
                Problem(StorageReasonKind.Unknown, "postcondition-unknown", "The native call's result remains uncertain.")) { NativeCallAttempted = true };
        if (!NeedsChange(after.Compression, action))
            return new(before, after, StorageEffect.Changed, after.Ineligible ?? (call.NativeSucceeded ? null :
                Problem(StorageReasonKind.TransientFailure, "native-" + call.NativeError, "The desired state was observed after a failed native return."))) { NativeCallAttempted = true };
        if (!call.NativeSucceeded && call.NativeError == 112 /* ERROR_DISK_FULL */)
            return new(before, after, StorageEffect.Failed, Problem(StorageReasonKind.TransientFailure, "compression-disk-full",
                "The drive ran out of free space while Windows tried to change NTFS compression (error 112).")) { NativeCallAttempted = true };
        return new(before, after, StorageEffect.Failed, Problem(StorageReasonKind.TransientFailure, "compression-postcondition",
            call.NativeSucceeded ? "Windows returned success but the requested compression state was not observed." :
                "Windows could not establish the requested compression state (error " + call.NativeError + ").")) { NativeCallAttempted = true };
    }

    private sealed class Traversal : IStorageTraversal
    {
        private readonly WindowsStorageTransforms _adapter;
        private readonly StorageNativeAllowance _allowance;
        private readonly NativePath _path, _windows;
        private readonly Pin _pin;
        private readonly StorageAction _action;
        private readonly HashSet<Frame> _frames = [];
        private bool _disposed;
        private readonly Frame _root;
        internal Traversal(WindowsStorageTransforms adapter, StorageNativeAllowance allowance, Pin pin, string path, NativePath heldPath, NativePath windows, StorageAction action)
        {
            _adapter = adapter; _allowance = allowance; _pin = pin; _path = heldPath; _windows = windows; _action = action;
            var facts = heldPath.LeafFacts;
            int depth = heldPath.Nodes.Count;
            var leaf = heldPath.TakeLeaf();
            try { _root = new(this, null, path, path[(path.LastIndexOf('\\') + 1)..], leaf, facts, depth, null); }
            catch { leaf.Dispose(); throw; }
        }
        public IStorageObjectLease Root => _root;
        public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct)
        {
            _root.ValidateAncestors(ct);
            return _path.Nodes.Select(node => node.Facts.Identity).ToArray();
        }
        private void Check()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_pin.IsAlive) throw Changed("review-pin", "The reviewed lifetime pin was released during traversal.");
            if (!ReferenceEquals(StorageNativeAllowance.RequireCurrent(), _allowance)) throw new InvalidOperationException("The traversal requires its original owned native allowance.");
        }
        public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct)
        {
            foreach (var entry in Entries(directory, ct))
                yield return entry.Object ?? throw new StorageInspectionException(entry.Problem ??
                    Problem(StorageReasonKind.Unknown, "child-open", "The discovered child could not be admitted."));
        }
        public IEnumerable<StorageTraversalEntry> Entries(IStorageObjectLease directory, CancellationToken ct)
        {
            Check();
            if (directory is not Frame frame || !ReferenceEquals(frame.Owner, this)) throw new ArgumentException("The directory does not belong to this traversal.", nameof(directory));
            frame.EnsureListing(ct);
            if (frame.Depth >= 64) throw Refuse("depth-limit", "The storage traversal reached the 64-level ancestor/depth limit; additional contents were not discovered.");
            frame.Cursor ??= new(frame.Handle.File);
            if (frame.Enumerating) throw new InvalidOperationException("This held directory cursor is already being consumed.");
            frame.Enumerating = true;
            try
            {
                while (!frame.Cursor.IsComplete)
                {
                    frame.ValidateAncestors(ct); frame.RequireEligible(ct);
                    foreach (var entry in frame.Cursor.ReadBatch(ct))
                    {
                        frame.ValidateAncestors(ct); frame.RequireEligible(ct);
                        yield return OpenChildEntry(frame, entry, ct);
                    }
                }
            }
            finally { frame.Enumerating = false; }
        }
        private StorageTraversalEntry OpenChildEntry(Frame parent, WindowsStorageDirectoryCursor.Entry entry, CancellationToken ct)
        {
            parent.ValidateAncestors(ct); parent.RequireEligible(ct);
            string path = parent.Path + "\\" + entry.Name;
            if (path.Length > 32767) return new(path, entry.IsDirectory, null,
                Problem(StorageReasonKind.FileCatBoundary, "path-limit", "A discovered storage path exceeds the qualified path bound."));
            Held? child = null;
            try
            {
                child = Open(parent.Handle.File, entry.Name, directory: false, pin: false, ct);
                var facts = Facts(child.File, _path.Volume.Binding, ct);
                var cursorProblem = CompareCursorEntry(facts, entry);
                var result = new Frame(this, parent, path, entry.Name, child, facts, parent.Depth + 1, cursorProblem);
                child = null; return new(path, facts.IsDirectory, result, null);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                int code = error.HResult & 0xffff;
                var problem = error is StorageInspectionException inspection ? inspection.Problem :
                    Problem(error is UnauthorizedAccessException ? StorageReasonKind.PermissionOrPolicy : code is 2 or 3 ? StorageReasonKind.IdentityChanged : StorageReasonKind.TransientFailure,
                        code is 32 or 33 ? "sharing-violation" : "child-open", "A discovered child could not be admitted: " + error.Message);
                return new(path, entry.IsDirectory, null, problem) { Retry = token => OpenChildEntry(parent, entry, token) };
            }
            finally { child?.Dispose(); }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { foreach (var frame in _frames.OrderByDescending(f => f.Depth).ToArray()) frame.Dispose(); }
            finally { try { _path.Dispose(); } finally { _windows.Dispose(); } }
        }

        private enum Access { Metadata, Listing, Mutation }
        private sealed class Frame : IStorageObjectLease
        {
            internal readonly Traversal Owner;
            internal readonly int Depth;
            private readonly Frame? _parent;
            private readonly string _name;
            private readonly NodeFacts _original;
            private readonly StorageProblem? _cursorProblem;
            private Held _handle;
            private Access _access;
            private bool _disposed;
            private StorageAction? _prepared;
            private StorageObservation? _preparedObservation;
            private SparseLayout? _preparedSparse;
            internal WindowsStorageDirectoryCursor? Cursor;
            internal bool Enumerating;
            internal Held Handle => _handle;
            public string Path { get; }
            public StorageObjectIdentity Identity => _original.Identity;
            internal Frame(Traversal owner, Frame? parent, string path, string name, Held handle, NodeFacts original, int depth, StorageProblem? cursorProblem)
            {
                Owner = owner; _parent = parent; Path = path; _name = name; _handle = handle; _original = original;
                Depth = depth; _cursorProblem = cursorProblem; owner._frames.Add(this);
            }
            private void Check()
            {
                Owner.Check(); ObjectDisposedException.ThrowIf(_disposed, this);
            }
            private IEnumerable<Frame> Parents()
            {
                var parents = new List<Frame>();
                for (var parent = _parent; parent is not null; parent = parent._parent) parents.Add(parent);
                for (int i = parents.Count - 1; i >= 0; i--) yield return parents[i];
            }
            private IEnumerable<StorageObjectIdentity> Ancestry() => Owner._path.Nodes.Select(n => n.Facts.Identity)
                .Concat(Parents().Select(p => p._original.Identity)).Append(_original.Identity);
            internal void ValidateAncestors(CancellationToken ct)
            {
                Check();
                foreach (var node in Owner._windows.Nodes) Validate(node.Handle.File, node.Facts, ct);
                foreach (var node in Owner._path.Nodes) Validate(node.Handle.File, node.Facts, ct);
                foreach (var parent in Parents()) { parent.Check(); Validate(parent._handle.File, parent._original, ct); }
            }
            private static void Validate(SafeFileHandle handle, NodeFacts expected, CancellationToken ct)
            {
                string volume = expected.Identity.VolumeBinding ?? throw Changed("volume-binding", "The held ancestor has no captured volume binding.");
                var current = Facts(handle, volume, ct);
                if (current.Identity != expected.Identity || current.CaseFlags != expected.CaseFlags || current.DeletePending ||
                    (current.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || ManagedAttributes(current.Attributes))
                    throw Changed("ancestor-changed", "A held storage ancestor changed; its remaining subtree is not processed.");
            }
            public StorageObservation Observe(CancellationToken ct)
            {
                ValidateAncestors(ct);
                return ObserveHandle(_handle.File, ct);
            }
            private StorageObservation ObserveHandle(SafeFileHandle handle, CancellationToken ct)
            {
                var result = WindowsStorageTransforms.Observe(handle, _original.Identity, Ancestry(), Owner._path.Nodes[0].Facts.Identity,
                    Owner._path.Volume, Owner._windows.LeafFacts.Identity, Owner._action, ct);
                return _cursorProblem is null ? result : result with { Ineligible = _cursorProblem };
            }
            internal StorageObservation RequireEligible(CancellationToken ct)
            {
                var observation = Observe(ct);
                if (observation.Ineligible is { } problem) throw new StorageInspectionException(problem);
                return observation;
            }
            private void RequireAction(StorageAction action)
            {
                Check();
                if (action != Owner._action || Owner._adapter.Unavailable(action) is not null)
                    throw new InvalidOperationException("The action does not match this reviewed traversal.");
            }
            public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct)
            {
                RequireAction(action); _prepared = null; _preparedObservation = null; _preparedSparse = null;
                var observation = Observe(ct);
                if (observation.Ineligible is null && NeedsChange(observation.Compression, action))
                {
                    Promote(Access.Mutation, ct);
                    observation = Observe(ct);
                    if (observation.Ineligible is null && !observation.IsDirectory && NeedsChange(observation.Compression, action))
                        _preparedSparse = ReadSparseLayout(_handle.File, ct);
                }
                _prepared = action;
                _preparedObservation = observation;
                return observation;
            }
            public StorageMutationResult ApplyAdmitted(StorageAction action)
            {
                RequireAction(action);
                if (_prepared != action || _preparedObservation is not { } before) throw new InvalidOperationException("Prepare the same held object before durable intent and call admission.");
                var preparedSparse = _preparedSparse;
                _prepared = null; _preparedObservation = null; _preparedSparse = null;
                bool attempted = false;
                try
                {
                    before = Observe(CancellationToken.None);
                    if (before.Ineligible is { } excluded) return new(before, before, StorageEffect.Skipped, excluded);
                    if (!NeedsChange(before.Compression, action)) return new(before, before, StorageEffect.AlreadySatisfied, null);
                    if (_access != Access.Mutation) return new(before, before, StorageEffect.NotProcessed,
                        Problem(StorageReasonKind.IdentityChanged, "new-preparation-required", "The storage state changed after preparation; no native call was attempted."));
                    var beforeSparse = before.IsDirectory ? null : ReadSparseLayout(_handle.File, CancellationToken.None);
                    if (!SparseLayout.SameHoles(preparedSparse, beforeSparse) || preparedSparse?.Allocation != beforeSparse?.Allocation)
                        return new(before, before, StorageEffect.NotProcessed,
                            Problem(StorageReasonKind.IdentityChanged, "sparse-preparation-changed", "The sparse layout changed after preparation. No native call was attempted."));
                    // The caller has durably recorded intent and atomically admitted this call. No cancellation token intervenes.
                    attempted = true;
                    var call = WindowsCompression.InvokeAdmitted(_handle.File, action, Owner._adapter._compressionSetReturnedForQualification);
                    var after = Observe(CancellationToken.None);
                    var afterSparse = after.IsDirectory ? null : ReadSparseLayout(_handle.File, CancellationToken.None);
                    if (!SparseLayout.SameHoles(beforeSparse, afterSparse))
                        return new(before, after, StorageEffect.Uncertain,
                            Problem(StorageReasonKind.Unknown, "sparse-postcondition-changed",
                                "Sparse state, length or the reported hole ranges changed. Review the file before continuing.")) { NativeCallAttempted = true };
                    return ClassifyCompressionOutcome(action, before, after, call);
                }
                catch (Exception error)
                {
                    return new(before, null, attempted ? StorageEffect.Uncertain : StorageEffect.NotProcessed,
                        Problem(StorageReasonKind.Unknown, attempted ? "postcondition-unreadable" : "precall-observation-failed",
                            attempted ? "A native call may have changed this item; its held postcondition could not be established: " + error.Message
                                : "The held precall observation failed; no native call was attempted: " + error.Message)) { NativeCallAttempted = attempted };
                }
            }
            internal void EnsureListing(CancellationToken ct)
            {
                Check(); var observation = RequireEligible(ct);
                if (!observation.IsDirectory) throw new ArgumentException("Only a held directory has children.");
                Validate(_handle.File, _original, ct);
                if (_access == Access.Metadata) Promote(Access.Listing, ct);
                observation = RequireEligible(ct);
                if (NeedsChange(observation.Compression, Owner._action))
                {
                    // A no-op directory can change before listing. Keep continuity while preparing the mutation handle;
                    // the caller must persist a fresh intent and admit a call before returning here.
                    if (_access != Access.Mutation) Promote(Access.Mutation, ct);
                    throw Changed("directory-needs-preparation", "The directory default changed before descent; a new prepared call is required.");
                }
            }
            private void Promote(Access access, CancellationToken ct)
            {
                Check();
                if (_access >= access) return;
                if (Cursor is not null) throw Changed("cursor-active", "A directory changed after enumeration began; its remaining subtree is not processed.");
                ValidateAncestors(ct);
                using var chargeOnFailure = new ChargeAdoption(Owner._allowance.ClaimScratch());
                SafeFileHandle? opened = null;
                try
                {
                    uint rights = NtFile.ReadAttributes | NtFile.Synchronize | NtFile.ReadData | (access == Access.Mutation ? NtFile.WriteData : 0);
                    int status = NtFile.TryOpen(_parent?._handle.File ?? Owner._path.Nodes[^1].Handle.File, _name, rights,
                        access == Access.Mutation ? NtFile.ShareRead : NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open,
                        NtFile.OpenReparsePoint | (_original.IsDirectory ? NtFile.DirectoryFile : NtFile.NonDirectoryFile), out opened, caseSensitive: true);
                    if (status < 0) throw NtFile.Error(status, "Acquire the held storage mutation/listing handle");
                    ValidateAncestors(ct);
                    var observation = ObserveHandle(opened, ct);
                    if (observation.Ineligible is { } problem) throw new StorageInspectionException(problem);
                    var replacement = new Held(opened, chargeOnFailure.Take()); opened = null;
                    var old = _handle; _handle = replacement;
                    old.Dispose(); // the validated replacement remains held across this transition
                    replacement.AdoptTraversalCharge(); _access = access;
                }
                finally { opened?.Dispose(); }
            }
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { Cursor?.Dispose(); _handle.Dispose(); }
                finally { Owner._frames.Remove(this); }
            }
        }
        private sealed class ChargeAdoption(IDisposable charge) : IDisposable
        {
            private IDisposable? _charge = charge;
            internal IDisposable Take() => Interlocked.Exchange(ref _charge, null) ?? throw new InvalidOperationException();
            public void Dispose() => Interlocked.Exchange(ref _charge, null)?.Dispose();
        }
    }

    private static StorageProblem? CompareCursorEntry(NodeFacts facts, WindowsStorageDirectoryCursor.Entry entry)
    {
        if ((facts.Attributes & (uint)FileAttributes.ReparsePoint) != (entry.Attributes & (uint)FileAttributes.ReparsePoint))
            return Problem(StorageReasonKind.IdentityChanged, "cursor-entry-changed", "The discovered reparse state changed; it will not be changed.");
        return entry.ObjectId is null || facts.Identity.ObjectId != entry.ObjectId || facts.IsDirectory != entry.IsDirectory
            ? Problem(StorageReasonKind.IdentityChanged, "cursor-entry-changed", "The discovered name no longer has its cursor identity and kind; it will not be changed.") : null;
    }
}
