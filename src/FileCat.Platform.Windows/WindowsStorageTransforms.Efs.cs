using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    // Native qualification seam only. Product admission still rejects EFS until the key, output,
    // approval/journal and architecture gates have been implemented and qualified together.
    internal sealed record EfsNativeReturn(bool Succeeded, int Error);
    private static readonly Lazy<bool> EfsEdition = new(() => GetProductInfo(10, 0, 0, 0, out uint product) &&
        product is 0x30 or 0x31 /* Pro, Pro N */ or 0x04 or 0x1B /* Enterprise, Enterprise N */ or 0x79 or 0x7A /* Education, Education N */);

    internal IStorageReviewLease InspectEfsRoot(StorageTarget target, StorageAction action, CancellationToken ct)
    {
        RequireEfsHost(action);
        if (StoragePaths.Capture([target.Source])[0] != target) throw Refuse("target-spelling", "The EFS target does not match its captured original path.");
        StorageReviewResources.RequireRootInspection(target);
        string sid = EfsInvoker();
        using var path = Walk(target.Path, reviewLeaf: true, ct);
        using var windows = ProtectedWindowsPath(ct);
        var inspection = Inspect(path, windows.LeafFacts.Identity, ct, compressionPolicy: false);
        if (inspection.Basic.IsDirectory != (target.Source.Kind == Core.Resources.EntryKind.Directory))
            throw Changed("kind-changed", "The selected item's kind changed; inspect the selection again.");
        return new Pin(this, target, inspection, action, path.TakeLeaf(), sid);
    }

    internal IStorageObjectLease OpenEfsPath(IStorageReviewLease root, StorageEfsConsent consent, CancellationToken ct,
        Func<string, StorageAction, EfsNativeReturn>? invoke = null, int sourceIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(consent);
        RequireEfsHost(consent.Action);
        var pin = RequirePin(root);
        try { StorageEfsConsent.Require(consent, consent.ReviewRevision, consent.Action, consent.Scope, consent.RootIdentities); }
        catch (ArgumentException) { throw Refuse("efs-consent", "EFS needs fresh action/scope/revision-bound acknowledgment of concurrent link changes and, for encryption, all-stream decompression."); }
        if (pin.InvokerSid is null || pin.Action != consent.Action || (uint)sourceIndex >= (uint)consent.RootIdentities.Length ||
            consent.RootIdentities[sourceIndex] != pin.Observation.Identity)
            throw Refuse("efs-consent", "EFS needs separate identity-bound acknowledgment of the concurrent link-change boundary and, for encryption, all-stream decompression.");
        if (EfsInvoker() != pin.InvokerSid) throw Changed("efs-invoker", "The invoking EFS user changed; create a new review.");
        NativePath? path = null, windows = null;
        try
        {
            path = Walk(pin.Target.Path, reviewLeaf: false, ct);
            if (path.LeafFacts.Identity != pin.Observation.Identity || Facts(pin.Handle.File, path.Volume.Binding, ct).Identity != pin.Observation.Identity)
                throw Changed("review-stale", "The captured EFS path no longer names its live reviewed object.");
            windows = ProtectedWindowsPath(ct);
            var observation = Inspect(path, windows.LeafFacts.Identity, ct, compressionPolicy: false).For(consent.Action);
            if (observation.Ineligible is { } excluded) throw new StorageInspectionException(excluded);
            var binding = new EfsPathBinding(pin, path, windows, consent, sourceIndex, invoke ?? InvokeEfs);
            path = null; windows = null;
            return binding;
        }
        finally { windows?.Dispose(); path?.Dispose(); }
    }

    private static void RequireEfsHost(StorageAction action)
    {
        if (action is not (StorageAction.EncryptEfs or StorageAction.DecryptEfs)) throw new ArgumentException("Choose an EFS action.", nameof(action));
        if (!IsQualifiedHost || !EfsEdition.Value) throw Refuse("efs-build", "This EFS candidate requires Windows 11 x64 Pro, Enterprise or Education; unknown editions remain unavailable.");
    }
    private static string EfsInvoker()
    {
        using var charge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
        if (WindowsTokenAuthority.IsHighIntegrity) throw Refuse("efs-normal-user", "Run EFS as the normal invoking user; elevated or unestablished tokens are refused.");
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User?.Value ?? throw Refuse("efs-invoker-unknown", "The invoking EFS user's SID could not be established.");
    }

    private static void PromoteEfsGuards(NativePath path, string captured, CancellationToken ct)
    {
        // Attribute-only opens do not participate in Windows' data/delete sharing checks.
        // Establish each read/list guard after metadata eligibility; keep old and replacement
        // simultaneously until identity validation succeeds, with only one rolling scratch slot.
        string raw = captured.StartsWith(@"\\?\", StringComparison.Ordinal) ? captured[4..] : captured;
        int prefix = raw.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase) ? 45 : 3;
        string[] names = raw[prefix..].TrimEnd('\\').Split('\\');
        string deviceRoot = EfsDeviceRoot(path);
        for (int i = 0; i < path.Nodes.Count; i++)
        {
            var node = path.Nodes[i];
            var facts = Facts(node.Handle.File, path.Volume.Binding, ct);
            EfsPathBinding.ValidateNode(facts, node.Facts);
            IDisposable? charge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
            SafeFileHandle? file = null;
            try
            {
                int status = NtFile.TryOpen(i == 0 ? null : path.Nodes[i - 1].Handle.File, i == 0 ? deviceRoot : names[i - 1],
                    NtFile.ReadData | NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open,
                    NtFile.OpenReparsePoint | (facts.IsDirectory ? NtFile.DirectoryFile : NtFile.NonDirectoryFile | 0x400000 /* NO_RECALL */),
                    out file, caseSensitive: true);
                if (status < 0) throw new StorageInspectionException(Problem(StorageReasonKind.PermissionOrPolicy, "efs-guard-open",
                    "Acquire EFS rename/deletion guard at component " + i + ": " + NtFile.Error(status, "Exact held-relative open").Message));
                EfsPathBinding.ValidateNode(Facts(file, path.Volume.Binding, ct), facts);
                var replacement = new Held(file, charge); file = null; charge = null;
                path.Nodes[i] = node with { Handle = replacement };
                node.Handle.Dispose(); replacement.AdoptTraversalCharge();
            }
            finally { file?.Dispose(); charge?.Dispose(); }
        }
    }

    private sealed class EfsPathBinding : IStorageObjectLease
    {
        private readonly Pin _pin;
        private readonly NativePath _path, _windows;
        private readonly StorageNativeAllowance _allowance;
        private readonly StorageEfsConsent _consent;
        private readonly StorageObjectIdentity _identity;
        private readonly Func<string, StorageAction, EfsNativeReturn> _invoke;
        private readonly string _apiPath;
        private StorageObservation? _prepared;
        private bool _preparedNeeded;
        private bool _guards;
        private bool _disposed;
        internal ImmutableArray<WindowsEfsStream> PreparedStreams { get; private set; } = [];
        // The containing type owns construction; callers receive no unpinned ID-to-path authority.
        internal EfsPathBinding(Pin pin, NativePath path, NativePath windows, StorageEfsConsent consent, int sourceIndex,
            Func<string, StorageAction, EfsNativeReturn> invoke)
        {
            _pin = pin; _path = path; _windows = windows; _consent = consent; _invoke = invoke;
            _identity = consent.RootIdentities[sourceIndex];
            _allowance = StorageNativeAllowance.RequireCurrent();
            _apiPath = EfsApiPath(path, pin.Target.Path);
        }
        public string Path => _pin.Target.Path;
        public StorageObjectIdentity Identity => _identity;
        private void Check()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_pin.IsAlive || !ReferenceEquals(StorageNativeAllowance.RequireCurrent(), _allowance))
                throw Changed("review-pin", "The EFS path requires its original live review pin and native owner.");
            if (EfsInvoker() != _pin.InvokerSid) throw Changed("efs-invoker", "The invoking EFS user changed.");
        }
        public StorageObservation Observe(CancellationToken ct)
        {
            Check(); ValidateSpelling(ct);
            return Inspect(_path, _windows.LeafFacts.Identity, ct, compressionPolicy: false).For(_consent.Action);
        }
        public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct)
        {
            RequireAction(action); _prepared = null; PreparedStreams = [];
            var before = Observe(ct);
            if (before.Ineligible is null && NeedsChange(before.Efs, action))
            {
                if (!_guards) { PromoteEfsGuards(_path, Path, ct); _guards = true; before = Observe(ct); }
                PreparedStreams = WindowsEfsStreams.Read(_path.Leaf.File, Identity, before.IsDirectory, ct);
                RequireNativeStatus(before, ct);
                before = Observe(ct);
            }
            _preparedNeeded = before.Ineligible is null && NeedsChange(before.Efs, action);
            _prepared = before; return before;
        }
        public StorageMutationResult ApplyAdmitted(StorageAction action)
        {
            RequireAction(action);
            var before = _prepared ?? throw new InvalidOperationException("Prepare the same EFS object before durable intent and atomic call admission.");
            _prepared = null; bool attempted = false;
            try
            {
                before = Observe(CancellationToken.None);
                if (before.Ineligible is { } excluded) return new(before, before, StorageEffect.Skipped, excluded);
                if (!NeedsChange(before.Efs, action)) return new(before, before, StorageEffect.AlreadySatisfied, null);
                if (!_preparedNeeded) return new(before, before, StorageEffect.NotProcessed,
                    Problem(StorageReasonKind.IdentityChanged, "new-preparation-required", "The EFS state changed after no-op preparation; no native call was attempted."));
                // Complete metadata again after preparation. Consent covers compressed streams added meanwhile.
                PreparedStreams = WindowsEfsStreams.Read(_path.Leaf.File, Identity, before.IsDirectory, CancellationToken.None);
                RequireNativeStatus(before, CancellationToken.None);
                before = Observe(CancellationToken.None);
                if (before.Ineligible is { } changed) return new(before, before, StorageEffect.NotProcessed, changed);
                if (!NeedsChange(before.Efs, action)) return new(before, before, StorageEffect.AlreadySatisfied, null);
                // Supported pathname API: the acknowledged in-place reparse race remains possible after this check.
                attempted = true;
                var call = _invoke(_apiPath, action);
                var after = Observe(CancellationToken.None);
                if (after.Ineligible is not null || after.Efs is not (StorageObservationState.Enabled or StorageObservationState.Disabled))
                    return Result(after, StorageEffect.Uncertain, after.Ineligible ?? Problem(StorageReasonKind.Unknown, "efs-postcondition-unknown", "The held EFS effect could not be established."));
                if (!NeedsChange(after.Efs, action)) return Result(after, StorageEffect.Changed,
                    call.Succeeded ? null : Problem(StorageReasonKind.TransientFailure, "efs-native-" + call.Error, "The desired held EFS state was observed after a failed native return."));
                return Result(after, call.Succeeded ? StorageEffect.Uncertain : StorageEffect.Failed,
                    Problem(call.Succeeded ? StorageReasonKind.Unknown : StorageReasonKind.TransientFailure, "efs-postcondition",
                        call.Succeeded ? "Windows returned success without the requested held EFS state. Another item might have changed; stop and inspect the scope."
                            : "Windows did not establish the requested held EFS state (error " + call.Error + ")."));
            }
            catch (Exception error)
            {
                return new(before, null, attempted ? StorageEffect.Uncertain : StorageEffect.NotProcessed,
                    Problem(StorageReasonKind.Unknown, attempted ? "efs-postcondition-unreadable" : "efs-precall-failed",
                        attempted ? "An EFS call may have changed an item; the held object or captured spelling could not be verified: " + error.Message
                            : "No EFS call was attempted: " + error.Message)) { NativeCallAttempted = attempted };
            }
            StorageMutationResult Result(StorageObservation after, StorageEffect effect, StorageProblem? issue) =>
                new(before, after, effect, issue) { NativeCallAttempted = true };
        }
        private void RequireAction(StorageAction action)
        { if (action != _consent.Action) throw new ArgumentException("This EFS binding belongs to one acknowledged action.", nameof(action)); }
        private void RequireNativeStatus(StorageObservation before, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!EncryptionStatus(_apiPath, out uint status)) throw NativeFailure(Marshal.GetLastPInvokeError(), "Inspect EFS native status");
            ct.ThrowIfCancellationRequested();
            uint expected = before.Efs == StorageObservationState.Enabled ? 1u : 0u;
            if (status != expected) throw new StorageInspectionException(Problem(status is 2 or 3 or 4 or 6 ? StorageReasonKind.WindowsLimitation : StorageReasonKind.PermissionOrPolicy,
                "efs-status-" + status, "Windows EFS status " + status + " does not admit this change; no EFS call was attempted."));
        }
        private void ValidateSpelling(CancellationToken ct)
        {
            string raw = Path.StartsWith(@"\\?\", StringComparison.Ordinal) ? Path[4..] : Path;
            int prefix = raw.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase) ? 45 : 3;
            string[] names = raw[prefix..].TrimEnd('\\').Split('\\');
            for (int i = 0; i < _path.Nodes.Count; i++)
            {
                var node = _path.Nodes[i]; var facts = Facts(node.Handle.File, _path.Volume.Binding, ct);
                ValidateNode(facts, node.Facts);
                using var scratch = _allowance.ClaimScratch();
                int status = NtFile.TryOpen(i == 0 ? null : _path.Nodes[i - 1].Handle.File, i == 0 ? @"\??\" + raw[..prefix] : names[i - 1],
                    NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open,
                    NtFile.OpenReparsePoint | (node.Facts.IsDirectory ? NtFile.DirectoryFile : 0), out var current, caseSensitive: i != 0);
                using (current)
                {
                    if (status < 0) throw Changed("efs-path-changed", "The captured EFS spelling could not be reopened without following links.");
                    ValidateNode(Facts(current, _path.Volume.Binding, ct), node.Facts);
                }
            }
            ValidateNode(Facts(_pin.Handle.File, _path.Volume.Binding, ct), _path.LeafFacts);
            foreach (var node in _windows.Nodes) ValidateNode(Facts(node.Handle.File, _windows.Volume.Binding, ct), node.Facts);
        }
        internal static void ValidateNode(NodeFacts current, NodeFacts original)
        {
            if (current.Identity != original.Identity || current.IsDirectory != original.IsDirectory || current.CaseFlags != original.CaseFlags || current.DeletePending ||
                (current.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || ManagedAttributes(current.Attributes) || !current.IsDirectory && current.Links != 1)
                throw Changed("efs-path-changed", "A held EFS item or ancestor changed identity, case mode, links or storage backing; stop and review the scope.");
        }
        public void Dispose()
        { if (_disposed) return; _disposed = true; try { _path.Dispose(); } finally { _windows.Dispose(); } }
    }

    private static string EfsApiPath(NativePath path, string captured)
    {
        // Derive only the physical root from its handle, then append captured exact components.
        // Do not chase a renamed leaf or let a drive-letter/session DOS-device remap redirect the API.
        string root = EfsDeviceRoot(path);
        string raw = captured.StartsWith(@"\\?\", StringComparison.Ordinal) ? captured[4..] : captured;
        int prefix = raw.StartsWith("Volume{", StringComparison.OrdinalIgnoreCase) ? 45 : 3;
        string result = @"\\?\GLOBALROOT" + root + raw[prefix..].TrimEnd('\\');
        if (result.Length > 32767) throw Refuse("efs-path-limit", "The physical EFS pathname exceeds the qualified bound.");
        return result;
    }
    private static string EfsDeviceRoot(NativePath path)
    {
        Span<char> buffer = stackalloc char[256]; uint count;
        fixed (char* text = buffer) count = FinalPath(path.Nodes[0].Handle.File, text, (uint)buffer.Length, 10 /* NT | OPENED */);
        if (count == 0 || count >= buffer.Length) throw Refuse("efs-device-root", "The held physical EFS volume root could not be established.");
        string root = new(buffer[..(int)count]); const string start = @"\Device\HarddiskVolume";
        if (!root.StartsWith(start, StringComparison.Ordinal) || !root.EndsWith('\\') || root.Length <= start.Length + 1 ||
            root.AsSpan(start.Length, root.Length - start.Length - 1).IndexOfAnyExceptInRange('0', '9') >= 0)
            throw Refuse("efs-device-root", "The held EFS device root is outside the qualified physical-volume grammar.");
        return root;
    }
    private static EfsNativeReturn InvokeEfs(string path, StorageAction action)
    {
        bool result = action == StorageAction.EncryptEfs ? Encrypt(path) : Decrypt(path, 0);
        return new(result, result ? 0 : Marshal.GetLastPInvokeError());
    }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool GetProductInfo(uint major, uint minor, uint spMajor, uint spMinor, out uint product);
    [LibraryImport("advapi32.dll", EntryPoint = "FileEncryptionStatusW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool EncryptionStatus(string path, out uint status);
    [LibraryImport("advapi32.dll", EntryPoint = "EncryptFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Encrypt(string path);
    [LibraryImport("advapi32.dll", EntryPoint = "DecryptFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool Decrypt(string path, uint reserved);
}
