using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Qualification-stage C adapter. WindowsPlatform keeps the unsupported adapter until execution gates close.</summary>
internal sealed unsafe partial class WindowsStorageTransforms : IStorageTransforms, IStorageEfsSetup
{
    // The qualification target is the Windows 11 client, not a Server product with the same build number.
    internal static bool IsQualifiedHost => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) &&
        RuntimeInformation.ProcessArchitecture == Architecture.X64 && !IsOS(29 /* OS_ANYSERVER */);
    [LibraryImport("shlwapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsOS(uint feature);
    private readonly Func<string> _windowsDirectory;
    private readonly Func<string> _efsSetupBaseDirectory;
    internal WindowsStorageTransforms(Func<string>? windowsDirectory = null, Func<string>? efsSetupBaseDirectory = null)
    {
        _windowsDirectory = windowsDirectory ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        _efsSetupBaseDirectory = efsSetupBaseDirectory ?? (() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    }

    public StorageProblem? Unavailable(StorageAction action)
    {
        if (!Enum.IsDefined(action)) return Problem(StorageReasonKind.FileCatBoundary, "action", "Choose an explicit storage action.");
        if (action is StorageAction.EncryptEfs or StorageAction.DecryptEfs)
            return Problem(StorageReasonKind.FileCatBoundary, "efs-unqualified", "EFS requires its qualified identity, key and output protocol.");
        return !IsQualifiedHost
            ? Problem(StorageReasonKind.WindowsLimitation, "target-unqualified", "This storage adapter requires a qualified Windows 11 x64 target.") : null;
    }

    public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
    {
        if (Unavailable(action ?? StorageAction.Compress) is { } unavailable) throw new StorageInspectionException(unavailable);
        var captured = StoragePaths.Capture([target.Source]);
        if (captured[0] != target) throw Refuse("target-spelling", "The storage target does not match its captured original path.");
        StorageReviewResources.RequireRootInspection(target);
        ct.ThrowIfCancellationRequested();
        using var path = Walk(target.Path, reviewLeaf: true, ct);
        using var windows = ProtectedWindowsPath(ct);
        var inspection = Inspect(path, windows.LeafFacts.Identity, ct);
        var observation = inspection.For(action);
        if (observation.IsDirectory != (target.Source.Kind == Core.Resources.EntryKind.Directory))
            throw Changed("kind-changed", "The selected item's kind changed; inspect the selection again.");
        return new Pin(this, target, inspection, action, path.TakeLeaf());
    }

    public StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action)
    {
        if (Unavailable(action ?? StorageAction.Compress) is { } unavailable) throw new StorageInspectionException(unavailable);
        var pin = RequirePin(root); return pin.Reframe(action);
    }

    public StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct)
    {
        if (Unavailable(action ?? StorageAction.Compress) is { } unavailable) throw new StorageInspectionException(unavailable);
        var pin = RequirePin(root); StorageReviewResources.RequireRootInspection(pin.Target);
        using var path = Walk(pin.Target.Path, reviewLeaf: false, ct);
        if (path.LeafFacts.Identity != pin.Observation.Identity || Facts(pin.Handle.File, path.Volume.Binding, ct).Identity != pin.Observation.Identity)
            throw Changed("review-stale", "The captured path no longer names the live reviewed object. Close this review and select it again.");
        using var windows = ProtectedWindowsPath(ct);
        var inspection = Inspect(path, windows.LeafFacts.Identity, ct);
        ct.ThrowIfCancellationRequested(); return pin.Update(inspection, action);
    }

    private Pin RequirePin(IStorageReviewLease root) => root is Pin pin && ReferenceEquals(pin.Adapter, this) && pin.IsAlive
        ? pin : throw Changed("review-pin", "This review no longer owns its original live pin. Close it and select the item again.");

    public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct)
    {
        if (Unavailable(action) is { } unavailable) throw new StorageInspectionException(unavailable);
        var allowance = StorageNativeAllowance.RequireCurrent();
        if (root is not Pin pin || !ReferenceEquals(pin.Adapter, this) || !pin.IsAlive || pin.Action != action || pin.Observation.Ineligible is not null)
            throw Changed("review-pin", "This storage review no longer owns a live pin from this adapter.");
        if (StoragePaths.Capture([pin.Target.Source])[0] != pin.Target) throw Refuse("target-spelling", "The reviewed target spelling is invalid.");
        NativePath? path = null, windows = null;
        try
        {
            path = Walk(pin.Target.Path, reviewLeaf: false, ct);
            if (path.LeafFacts.Identity != pin.Observation.Identity || Facts(pin.Handle.File, path.Volume.Binding, ct).Identity != pin.Observation.Identity)
                throw Changed("review-stale", "The captured path no longer names the live reviewed object; inspect it again.");
            windows = ProtectedWindowsPath(ct);
            var traversal = new Traversal(this, allowance, pin, pin.Target.Path, path, windows, action);
            path = null; windows = null;
            return traversal;
        }
        finally { windows?.Dispose(); path?.Dispose(); }
    }

    private NativePath ProtectedWindowsPath(CancellationToken ct)
    {
        try
        {
            var path = Walk(_windowsDirectory(), reviewLeaf: false, ct);
            if (path.LeafFacts.IsDirectory && !path.LeafFacts.DeletePending &&
                (path.LeafFacts.Attributes & (uint)FileAttributes.ReparsePoint) == 0 && !ManagedAttributes(path.LeafFacts.Attributes)) return path;
            path.Dispose();
            throw Refuse("protected-anchor", "The active Windows directory is not an ordinary bound directory.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        { throw new StorageInspectionException(Problem(StorageReasonKind.Unknown, "protected-tree-unknown", "The active Windows directory could not be bound; storage changes are unavailable for this inspection."), error); }
    }

    private static StorageObservation Observe(NativePath path, StorageObjectIdentity protectedWindows, StorageAction? action, CancellationToken ct)
        => Inspect(path, protectedWindows, ct).For(action);

    private static Inspection Inspect(NativePath path, StorageObjectIdentity protectedWindows, CancellationToken ct, bool compressionPolicy = true)
        => Inspect(path.Leaf.File, path.LeafFacts.Identity, path.Nodes.Select(n => n.Facts.Identity), path.Nodes[0].Facts.Identity,
            path.Volume, protectedWindows, ct, compressionPolicy);

    private static StorageObservation Observe(SafeFileHandle file, StorageObjectIdentity expected, IEnumerable<StorageObjectIdentity> ancestry,
        StorageObjectIdentity volumeRoot, VolumeFacts volume, StorageObjectIdentity protectedWindows, StorageAction? action, CancellationToken ct)
        => Inspect(file, expected, ancestry, volumeRoot, volume, protectedWindows, ct).For(action);

    private static Inspection Inspect(SafeFileHandle file, StorageObjectIdentity expected, IEnumerable<StorageObjectIdentity> ancestry,
        StorageObjectIdentity volumeRoot, VolumeFacts volume, StorageObjectIdentity protectedWindows, CancellationToken ct, bool compressionPolicy = true)
    {
        var facts = Facts(file, volume.Binding, ct);
        StorageProblem? problem = PhysicalProblem(expected, ancestry, volumeRoot, volume, facts, protectedWindows, compressionPolicy);
        var compression = StorageObservationState.Unknown;
        var efs = (facts.Attributes & (uint)FileAttributes.Encrypted) != 0 ? StorageObservationState.Enabled : StorageObservationState.Disabled;
        if (problem is null)
        {
            ct.ThrowIfCancellationRequested();
            var backing = WindowsStorageBacking.Query(file, ct);
            if (backing.External != false) problem = Problem(backing.External == true ? StorageReasonKind.FileCatBoundary : StorageReasonKind.Unknown, "external-backing", backing.External == true
                ? "This file uses external storage backing, which this workflow does not change."
                : "External storage backing could not be ruled out; storage changes are unavailable.");
        }
        if (problem is null) problem = SyncRootProblem(file, ct);
        if (problem is null)
        {
            ct.ThrowIfCancellationRequested();
            var format = WindowsCompression.Query(file);
            ct.ThrowIfCancellationRequested();
            compression = format.State;
            if (compression == StorageObservationState.Unknown)
                problem = Problem(StorageReasonKind.Unknown, "compression-unknown", "The main-data compression format could not be established.");
            else if (compression == StorageObservationState.Enabled && efs == StorageObservationState.Enabled)
                problem = Problem(StorageReasonKind.Unknown, "inconsistent-storage", "Both compression and EFS were observed; automatic conversion is refused.");
        }
        return new(new(compression, efs, facts.Identity, facts.IsDirectory, problem),
            (facts.Attributes & (uint)FileAttributes.ReadOnly) != 0, facts.Size);
    }

    private sealed record Inspection(StorageObservation Basic, bool ReadOnly, long Size)
    {
        internal StorageObservation For(StorageAction? action)
        {
            if (Basic.Ineligible is not null || action is null) return Basic;
            StorageProblem? problem = null;
            if (action == StorageAction.Compress && Basic.Efs == StorageObservationState.Enabled)
                problem = Problem(StorageReasonKind.FileCatBoundary, "efs-conflict", "This item is encrypted with EFS. Compression cannot remove that protection.");
            else if (NeedsChange(action is StorageAction.EncryptEfs or StorageAction.DecryptEfs ? Basic.Efs : Basic.Compression, action.Value) && !Basic.IsDirectory)
            {
                if (ReadOnly) problem = Problem(StorageReasonKind.PermissionOrPolicy, "read-only-file", "The file is read-only; its attributes will not be cleared.");
                else if (action == StorageAction.Compress && Size > 30_000_000_000L)
                    problem = Problem(StorageReasonKind.FileCatBoundary, "enable-size-limit", "This file exceeds FileCat's qualified 30,000,000,000-byte compression-enable limit.");
            }
            return Basic with { Ineligible = problem };
        }
    }

    private static bool NeedsChange(StorageObservationState current, StorageAction action) =>
        action is StorageAction.Compress or StorageAction.EncryptEfs ? current != StorageObservationState.Enabled : current != StorageObservationState.Disabled;

    private static StorageProblem? PhysicalProblem(StorageObjectIdentity expected, IEnumerable<StorageObjectIdentity> ancestry,
        StorageObjectIdentity volumeRoot, VolumeFacts volume, NodeFacts facts, StorageObjectIdentity protectedWindows, bool compressionPolicy = true)
    {
        if (facts.Identity != expected || facts.DeletePending) return Problem(StorageReasonKind.IdentityChanged, "entry-changed", "The held entry changed or is being deleted.");
        if (facts.Identity == volumeRoot) return Problem(StorageReasonKind.FileCatBoundary, "volume-root", "The volume root cannot be a storage-change target.");
        if (ancestry.Contains(protectedWindows)) return Problem(StorageReasonKind.FileCatBoundary, "windows-tree", "Storage changes are unavailable within the active Windows directory tree.");
        if ((facts.Attributes & (uint)FileAttributes.System) != 0) return Problem(StorageReasonKind.FileCatBoundary, "system-entry", "Storage changes are unavailable for an entry with the System attribute.");
        if ((facts.Attributes & (uint)FileAttributes.ReparsePoint) != 0) return Problem(StorageReasonKind.FileCatBoundary, "reparse-entry", "This reparse storage format is not supported by this change.");
        if (ManagedAttributes(facts.Attributes)) return Problem(StorageReasonKind.FileCatBoundary, "cloud-entry", "This item is cloud-managed or may need downloading; its storage protection is not changed here.");
        if (!facts.IsDirectory && facts.Links != 1) return Problem(StorageReasonKind.FileCatBoundary, "multiple-names", "This file has other names that would also be affected, or its link count is unknown.");
        if ((facts.Attributes & (uint)FileAttributes.SparseFile) != 0) return Problem(StorageReasonKind.FileCatBoundary, "sparse-entry", "Compression changes for sparse entries are outside the qualified workflow.");
        if ((volume.Flags & 0x80000) != 0) return Problem(StorageReasonKind.PermissionOrPolicy, "read-only-volume", "This drive is read-only.");
        if (volume.AllocationUnit == 0) return Problem(StorageReasonKind.Unknown, "allocation-unit-unknown", "The volume's allocation unit could not be established.");
        if (compressionPolicy && volume.AllocationUnit > 4096) return Problem(StorageReasonKind.FileCatBoundary, "allocation-unit", "Compression changes require an allocation unit no larger than 4096 bytes.");
        if (compressionPolicy && (volume.Flags & 0x10) == 0) return Problem(StorageReasonKind.WindowsLimitation, "volume-compression", "This volume does not advertise per-file compression support.");
        if (!compressionPolicy && (volume.Flags & 0x20000) == 0) return Problem(StorageReasonKind.WindowsLimitation, "volume-efs", "This volume does not advertise EFS support.");
        return null;
    }

    // 0x40000 from a queried FILE_BASIC_INFO is the EA indication, not enumeration's RECALL_ON_OPEN bit.
    private static bool ManagedAttributes(uint attributes) => (attributes & (0x1000u | 0x580000u)) != 0;
    private static StorageProblem Problem(StorageReasonKind kind, string code, string text) =>
        new(kind, code, text.Length <= 2000 ? text : text[..2000], kind is not (StorageReasonKind.FileCatBoundary or StorageReasonKind.WindowsLimitation));
    private static StorageInspectionException Refuse(string code, string text) => new(Problem(StorageReasonKind.FileCatBoundary, code, text));
    private static StorageInspectionException Changed(string code, string text) => new(Problem(StorageReasonKind.IdentityChanged, code, text));
    private static StorageInspectionException NativeFailure(int error, string operation) => new(Problem(
        error is 5 or 1314 ? StorageReasonKind.PermissionOrPolicy : error is 32 or 33 ? StorageReasonKind.TransientFailure : StorageReasonKind.Unknown,
        "native-" + error, operation + ": " + new Win32Exception(error).Message));

    private sealed class Pin(WindowsStorageTransforms adapter, StorageTarget target, Inspection inspection, StorageAction? action, Held handle, string? invokerSid = null) : IStorageReviewLease
    {
        private sealed record State(Inspection Inspection, StorageAction? Action);
        private State _state = new(inspection, action);
        internal WindowsStorageTransforms Adapter => adapter;
        internal Held Handle => handle;
        internal string? InvokerSid => invokerSid;
        internal StorageAction? Action => Volatile.Read(ref _state).Action;
        public StorageTarget Target => target;
        public StorageObservation Observation { get { var state = Volatile.Read(ref _state); return state.Inspection.For(state.Action); } }
        internal StorageObservation Reframe(StorageAction? requested) => Update(Volatile.Read(ref _state).Inspection, requested);
        internal StorageObservation Update(Inspection facts, StorageAction? requested)
        { Interlocked.Exchange(ref _state, new(facts, requested)); return facts.For(requested); }
        public bool IsAlive => !handle.File.IsClosed && !handle.File.IsInvalid;
        public void Dispose() => handle.Dispose();
    }

    private sealed class Held(SafeFileHandle file, IDisposable? charge) : IDisposable
    {
        private IDisposable? _charge = charge;
        internal SafeFileHandle File => file;
        internal void AdoptTraversalCharge()
        {
            var replacement = StorageNativeAllowance.RequireCurrent().ClaimTraversal();
            Interlocked.Exchange(ref _charge, replacement)?.Dispose();
        }
        public void Dispose() { try { file.Dispose(); } finally { Interlocked.Exchange(ref _charge, null)?.Dispose(); } }
    }
    private sealed record NodeFacts(StorageObjectIdentity Identity, uint Attributes, uint Links, long Size, bool IsDirectory, bool DeletePending, uint CaseFlags);
    private sealed record VolumeFacts(string Binding, uint Flags, ulong AllocationUnit);
    private sealed record Node(Held Handle, NodeFacts Facts);
    private sealed class NativePath(List<Node> nodes, VolumeFacts volume) : IDisposable
    {
        internal List<Node> Nodes => nodes;
        internal VolumeFacts Volume => volume;
        internal Held Leaf => nodes[^1].Handle;
        internal NodeFacts LeafFacts => nodes[^1].Facts;
        internal Held TakeLeaf() { var handle = Leaf; nodes.RemoveAt(nodes.Count - 1); return handle; }
        public void Dispose() { for (int i = nodes.Count - 1; i >= 0; i--) nodes[i].Handle.Dispose(); nodes.Clear(); }
    }
}
