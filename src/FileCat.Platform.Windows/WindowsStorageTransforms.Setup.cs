using System.ComponentModel;
using System.Security.AccessControl;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    private const string EfsSetupDirectory = "FileCat.EfsSetup";
    private const uint ReadControl = 0x20000;

    StorageEfsSetupResult IStorageEfsSetup.InspectSetup(string invokerSid, StorageEfsConsent scopeConsent, bool explicitSetupAccepted, CancellationToken ct) =>
        InspectEfsSetup(invokerSid, scopeConsent, explicitSetupAccepted, ct);

    internal StorageEfsSetupResult InspectEfsSetup(string invokerSid, StorageEfsConsent scopeConsent, bool explicitSetupAccepted,
        CancellationToken ct, Action<string>? createdForQualification = null, bool allowProviderInteraction = false)
    {
        ArgumentNullException.ThrowIfNull(scopeConsent);
        if (!explicitSetupAccepted) throw Refuse("efs-setup-consent", "EFS setup may create or enroll a key only after an explicit setup choice.");
        RequireEfsHost(scopeConsent.Action);
        if (scopeConsent.Action != StorageAction.EncryptEfs) throw Refuse("efs-setup-action", "Decryption and already-satisfied work do not provision an EFS key.");
        StorageEfsConsent.Require(scopeConsent, scopeConsent.ReviewRevision, scopeConsent.Action, scopeConsent.Scope, scopeConsent.RootIdentities);
        ct.ThrowIfCancellationRequested();
        if (EfsInvoker() != invokerSid) throw Changed("efs-invoker", "The invoking EFS identity changed before setup.");
        var allowance = StorageNativeAllowance.RequireCurrent();
        NativePath? path = null, windows = null; Held? directory = null, canary = null;
        NodeFacts? directoryFacts = null;
        StorageObjectIdentity? canaryIdentity = null;
        StorageEfsAssociationSnapshot? association = null;
        StorageProblem? problem = null, cleanup = null;
        string? capturedFile = null, apiFile = null, applicationBase = null, leaf = null;
        bool attempted = false, canceled = false, deleted = false;
        try
        {
            applicationBase = _efsSetupBaseDirectory();
            path = Walk(applicationBase, reviewLeaf: false, ct);
            if (!path.LeafFacts.IsDirectory || path.Nodes.Count is < 2 or > 62) throw Refuse("efs-setup-directory", "Setup requires a bounded ordinary application directory below the volume root.");
            windows = ProtectedWindowsPath(ct);
            var eligibility = Inspect(path, windows.LeafFacts.Identity, ct, compressionPolicy: false).For(StorageAction.EncryptEfs);
            if (eligibility.Ineligible is { } excluded) throw new StorageInspectionException(excluded);
            PromoteEfsGuards(path, applicationBase, ct);
            leaf = "setup-" + Guid.NewGuid().ToString("N") + ".tmp";
            capturedFile = applicationBase.TrimEnd('\\') + "\\" + EfsSetupDirectory + "\\" + leaf;
            apiFile = EfsApiPath(path, applicationBase) + "\\" + EfsSetupDirectory + "\\" + leaf;
            if (capturedFile.Length > 32767 || apiFile.Length > 32767) throw Refuse("efs-setup-path-limit", "The complete setup pathname exceeds the bounded native query path.");
            using (var parent = OpenSetupParent(path, applicationBase, ct))
            {
                RequireSetupNamespace(parent.File, invokerSid, strict: false, ct);
                directory = OpenSetupDirectory(parent.File, invokerSid, ct);
            }
            directoryFacts = Facts(directory.File, path.Volume.Binding, ct);
            ValidateSetupPath(path, applicationBase, directory, directoryFacts, invokerSid, ct);
            IDisposable? charge = allowance.ClaimScratch(); SafeFileHandle? file = null;
            try
            {
                ct.ThrowIfCancellationRequested(); attempted = true;
                int status = NtFile.TryOpen(directory.File, leaf, NtFile.ReadData | NtFile.WriteData | NtFile.ReadAttributes | ReadControl | NtFile.Delete | NtFile.Synchronize,
                    NtFile.ShareRead, NtFile.Create, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | 0x400000 /* NO_RECALL */, out file,
                    (uint)FileAttributes.Encrypted, SetupSecurity(invokerSid, false), caseSensitive: true);
                if (status < 0) throw NtFile.Error(status, "Create the explicit zero-byte encrypted setup file");
                canary = new Held(file, charge); file = null; charge = null;
            }
            finally { file?.Dispose(); charge?.Dispose(); }
            canaryIdentity = Facts(canary.File, path.Volume.Binding, CancellationToken.None).Identity;
            ValidateCanary(ct);
            createdForQualification?.Invoke(capturedFile);
            ValidateCanary(ct);
            association = WindowsEfsAssociations.Read(apiFile, canaryIdentity, invokerSid, _efsKeys, ValidateCanary, ct);
            if (!association.HasMatchedCurrentUserKey && !(allowProviderInteraction &&
                association.CurrentUserKeys.Any(WindowsEfsKeys.NeedsParentedCngAccess)))
                problem = Problem(StorageReasonKind.PermissionOrPolicy, "efs-setup-key-unavailable", "The actual setup file has no freshly matched accessible key for the invoking user. A recovery declaration cannot replace that check.");
        }
        catch (OperationCanceledException)
        { canceled = true; problem = Problem(StorageReasonKind.TransientFailure, "efs-setup-canceled", "Setup stopped. Windows-created keys are retained; no selected user item was encrypted by this setup phase."); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception)
        { problem = error is StorageInspectionException inspection ? inspection.Problem : Problem(StorageReasonKind.Unknown, "efs-setup-failed", error.Message); }
        finally
        {
            if (canary is not null)
            {
                try
                {
                    // Cleanup uses the exact created object, never a pathname reopen or recursive delete.
                    var facts = Facts(canary.File, path!.Volume.Binding, CancellationToken.None);
                    if (canaryIdentity is null || facts.Identity != canaryIdentity || facts.IsDirectory || facts.Size != 0 || facts.Links != 1)
                        throw new IOException("The created setup object is no longer a verified zero-byte single-link file.");
                    var streams = WindowsEfsStreams.Read(canary.File, canaryIdentity, false, CancellationToken.None);
                    if (streams.Length != 1 || streams[0].NativeName != "::$DATA" || streams[0].Length != 0)
                        throw new IOException("The created setup file gained another stream or data; it was retained for review.");
                    NtFile.DeleteOpen(canary.File); deleted = true;
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception)
                { cleanup = Problem(StorageReasonKind.Unknown, "efs-setup-cleanup", "The owned setup artifact was retained; its last captured spelling and identity need review: " + error.Message); }
            }
            try { canary?.Dispose(); }
            finally { try { directory?.Dispose(); } finally { try { windows?.Dispose(); } finally { path?.Dispose(); } } }
        }
        return new(association, attempted, canceled, problem, cleanup, canary is not null && !deleted ? capturedFile : null,
            canary is not null && !deleted ? canaryIdentity : null) { AttemptedSetupPath = attempted ? capturedFile : null };

        void ValidateCanary(CancellationToken token)
        {
            if (EfsInvoker() != invokerSid) throw Changed("efs-invoker", "The invoking identity changed during setup.");
            ValidateSetupPath(path!, applicationBase!, directory!, directoryFacts!, invokerSid, token);
            var facts = Facts(canary!.File, path!.Volume.Binding, token);
            if (facts.Identity != canaryIdentity || facts.IsDirectory || facts.DeletePending || facts.Size != 0 || facts.Links != 1 ||
                (facts.Attributes & (uint)FileAttributes.Encrypted) == 0 || (facts.Attributes & (uint)FileAttributes.ReparsePoint) != 0 || ManagedAttributes(facts.Attributes))
                throw Changed("efs-setup-changed", "The setup file is no longer the ordinary encrypted zero-byte object that was created.");
            WindowsArchiveNative.RequirePrivateSecurity(canary.File, invokerSid);
            using (StorageNativeAllowance.RequireCurrent().ClaimScratch())
            {
                int status = NtFile.TryOpen(directory!.File, leaf!, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, NtFile.Open,
                    NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, out var current, caseSensitive: true);
                using (current)
                    if (status < 0 || Facts(current, path.Volume.Binding, token).Identity != canaryIdentity)
                        throw Changed("efs-setup-spelling", "The captured setup spelling no longer names the created held file.");
            }
            var streams = WindowsEfsStreams.Read(canary.File, canaryIdentity!, false, token);
            if (streams.Length != 1 || streams[0].NativeName != "::$DATA" || streams[0].Length != 0)
                throw Changed("efs-setup-streams", "The setup file gained another stream or data; no association is accepted.");
        }
    }

    private static Held OpenSetupParent(NativePath path, string captured, CancellationToken ct)
    {
        var charge = StorageNativeAllowance.RequireCurrent().ClaimScratch(); SafeFileHandle? file = null;
        try
        {
            var parent = path.Nodes[^2];
            string name = captured.TrimEnd('\\'); string opened = name[(name.LastIndexOf('\\') + 1)..];
            int status = NtFile.TryOpen(parent.Handle.File, opened, NtFile.ListDirectory | NtFile.AddSubdirectory | NtFile.ReadAttributes | ReadControl | NtFile.Synchronize,
                NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open, NtFile.DirectoryFile | NtFile.OpenReparsePoint, out file, caseSensitive: true);
            if (status < 0) throw NtFile.Error(status, "Open the verified setup application namespace");
            if (Facts(file, path.Volume.Binding, ct).Identity != path.LeafFacts.Identity) throw Changed("efs-setup-parent", "The setup application directory changed.");
            var held = new Held(file, charge); file = null; return held;
        }
        catch { charge.Dispose(); throw; }
        finally { file?.Dispose(); }
    }
    private static Held OpenSetupDirectory(SafeFileHandle parent, string owner, CancellationToken ct)
    {
        IDisposable? charge = StorageNativeAllowance.RequireCurrent().ClaimScratch(); SafeFileHandle? file = null;
        try
        {
            uint access = NtFile.ListDirectory | NtFile.WriteData | NtFile.ReadAttributes | ReadControl | NtFile.Synchronize;
            ct.ThrowIfCancellationRequested();
            int status = NtFile.TryOpen(parent, EfsSetupDirectory, access, NtFile.ShareRead | NtFile.ShareWrite, NtFile.Open,
                NtFile.DirectoryFile | NtFile.OpenReparsePoint, out file, caseSensitive: true);
            if (status == NtFile.NameNotFound)
            {
                file.Dispose(); file = null; ct.ThrowIfCancellationRequested();
                status = NtFile.TryOpen(parent, EfsSetupDirectory, access, NtFile.ShareRead | NtFile.ShareWrite, NtFile.Create,
                    NtFile.DirectoryFile | NtFile.OpenReparsePoint, out file, (uint)FileAttributes.Directory, SetupSecurity(owner, true), caseSensitive: true);
            }
            if (status < 0) throw NtFile.Error(status, "Open the private EFS setup directory");
            RequireSetupNamespace(file, owner, strict: true, ct);
            var held = new Held(file, charge); file = null; charge = null; return held;
        }
        finally { file?.Dispose(); charge?.Dispose(); }
    }
    private static void ValidateSetupPath(NativePath path, string captured, Held directory, NodeFacts originalDirectory, string owner, CancellationToken ct)
    {
        ValidateCapturedEfsPath(path, captured, ct);
        var facts = Facts(directory.File, path.Volume.Binding, ct);
        EfsPathBinding.ValidateNode(facts, originalDirectory);
        if (!facts.IsDirectory) throw Changed("efs-setup-namespace", "The private setup namespace is not a directory.");
        using (StorageNativeAllowance.RequireCurrent().ClaimScratch())
        {
            int status = NtFile.TryOpen(path.Leaf.File, EfsSetupDirectory, NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, NtFile.Open,
                NtFile.DirectoryFile | NtFile.OpenReparsePoint, out var current, caseSensitive: true);
            using (current)
            {
                if (status < 0) throw Changed("efs-setup-namespace", "The setup directory spelling could not be reopened.");
                EfsPathBinding.ValidateNode(Facts(current, path.Volume.Binding, ct), originalDirectory);
            }
        }
        RequireSetupNamespace(directory.File, owner, strict: true, ct);
    }
    private static void RequireSetupNamespace(SafeFileHandle file, string owner, bool strict, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); WindowsArchiveNative.SourceDirectory(file);
        uint attributes = NtFile.GetBasic(file).FileAttributes;
        if (ManagedAttributes(attributes) || SyncRootProblem(file, ct) is not null) throw Refuse("efs-setup-managed", "The setup namespace is cloud-managed or unavailable.");
        if (strict) { WindowsArchiveNative.RequirePrivateEditSecurity(file, owner, directory: true); return; }
        var security = new RawSecurityDescriptor(WindowsArchiveNative.ReadOwnerDaclSecurity(file), 0);
        if (security.Owner?.Value != owner || security.DiscretionaryAcl is not { Count: > 0 } acl)
            throw new UnauthorizedAccessException("The setup application namespace is not owned by the invoking user.");
        foreach (var entry in acl)
        {
            if (entry is not QualifiedAce ace) throw new UnauthorizedAccessException("The setup namespace permissions are unqualified.");
            if (ace.AceQualifier == AceQualifier.AccessAllowed && (ace.AceFlags & AceFlags.InheritOnly) == 0 &&
                ace.SecurityIdentifier.Value != owner && ace.SecurityIdentifier.Value is not ("S-1-5-18" or "S-1-5-32-544") &&
                (unchecked((uint)ace.AccessMask) & 0x500D0156u) != 0)
                throw new UnauthorizedAccessException("Another identity can change the setup application namespace.");
        }
    }
    private static byte[] SetupSecurity(string owner, bool directory)
    {
        string inheritance = directory ? "OICI" : "";
        var descriptor = new RawSecurityDescriptor($"O:{owner}D:P(A;{inheritance};FA;;;{owner})(A;{inheritance};FA;;;SY)");
        byte[] bytes = new byte[descriptor.BinaryLength]; descriptor.GetBinaryForm(bytes, 0); return bytes;
    }
}
