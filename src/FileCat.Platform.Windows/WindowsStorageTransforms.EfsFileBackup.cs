using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    private const int SelectedBackupKeyLimit = 256;

    private StorageEfsFileBackupReview InspectBackupFiles(IStorageEfsFileBackup adapter, ImmutableArray<StorageTarget> targets, CancellationToken ct)
    {
        RequireEfsHost(StorageAction.DecryptEfs);
        if (targets.IsDefaultOrEmpty || targets.Length > 64) throw new ArgumentException("Select between 1 and 64 files for EFS backup.", nameof(targets));
        string sid = EfsInvoker();
        var items = ImmutableArray.CreateBuilder<StorageEfsFileBackupItem>(targets.Length);
        var keys = new Dictionary<string, StorageEfsFileBackupKey>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            var (association, problem) = ReadBackupAssociation(target, sid, expected: null, ct);
            var hashes = ImmutableArray.CreateBuilder<string>();
            if (association is not null)
            {
                foreach (var key in association.CurrentUserKeys)
                {
                    if (!keys.TryGetValue(key.CertificateSha1, out var option))
                    {
                        if (keys.Count == SelectedBackupKeyLimit)
                            throw Refuse("efs-backup-key-limit", "The selection has more than 256 EFS certificates for this account. Review fewer files at a time.");
                        option = new(key.CertificateSha1, key.Subject, key.State, key.Problem);
                        keys.Add(option.CertificateSha1, option);
                    }
                    else if (option.State == StorageEfsKeyState.MatchedAvailable && key.State != StorageEfsKeyState.MatchedAvailable)
                    {
                        // Different observations of the same key need a fresh inspection before it can be chosen.
                        keys[option.CertificateSha1] = option with { State = key.State, Problem = key.Problem };
                    }
                    hashes.Add(option.CertificateSha1);
                }
                if (hashes.Count == 0)
                    problem = Problem(StorageReasonKind.PermissionOrPolicy, "efs-backup-no-user-key", "The selected file has no EFS certificate association for this Windows account.");
            }
            items.Add(new(target, association?.Identity, hashes.ToImmutable(), problem));
        }
        return new(adapter, sid, items.MoveToImmutable(), keys.Values.OrderBy(key => key.CertificateSha1, StringComparer.Ordinal).ToImmutableArray());
    }

    private StorageEfsFileBackupResult BackUpFileKey(IStorageEfsFileBackup adapter, StorageEfsFileBackupReview review,
        string certificateSha1, nint ownerWindow, CancellationToken ct, WindowsEfsKeys.InvokeBackupWizard? invokeWizard)
    {
        RequireEfsHost(StorageAction.DecryptEfs);
        if (!ReferenceEquals(review.Adapter, adapter) || review.InvokerSid != EfsInvoker() ||
            !review.Keys.Any(key => key.CertificateSha1 == certificateSha1 && key.State == StorageEfsKeyState.MatchedAvailable))
            throw Refuse("efs-backup-reviewed-key", "Choose an available certificate from the current selection review for this Windows account.");

        StorageEfsAssociationSnapshot? reviewed = null;
        foreach (var item in review.Items)
        {
            ct.ThrowIfCancellationRequested();
            if ((reviewed = RecheckBackupAssociation(item, review.InvokerSid, certificateSha1, ct)) is not null) break;
        }
        if (reviewed is null)
            return new(false, false, [], Problem(StorageReasonKind.IdentityChanged, "efs-backup-association-changed",
                "None of the originally reviewed files could still be associated with the selected key. Inspect the selection again."));

        var backup = new WindowsEfsKeys().BackUpAssociatedCertificate(reviewed, certificateSha1, ownerWindow, explicitlyRequested: true, ct, invokeWizard);
        if (backup.Completion is null) return new(false, backup.State == WindowsEfsKeys.BackupState.Canceled, [], backup.Problem);

        var covered = ImmutableArray.CreateBuilder<int>();
        try
        {
            for (int i = 0; i < review.Items.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (RecheckBackupAssociation(review.Items[i], review.InvokerSid, certificateSha1, ct) is not null) covered.Add(i);
            }
            return new(true, false, covered.ToImmutable(), null);
        }
        catch (OperationCanceledException)
        {
            return new(true, false, covered.ToImmutable(), Problem(StorageReasonKind.Unknown, "efs-backup-coverage-stopped",
                "Windows reported that the export completed. The remaining selected files could not be checked before stopping."));
        }
    }

    private StorageEfsAssociationSnapshot? RecheckBackupAssociation(StorageEfsFileBackupItem item, string sid, string hash, CancellationToken ct)
    {
        if (item.Identity is not { } identity || !item.Certificates.Contains(hash, StringComparer.Ordinal)) return null;
        var (association, _) = ReadBackupAssociation(item.Target, sid, identity, ct);
        return association?.CurrentUserKeys.Any(key => key.CertificateSha1 == hash && key.State == StorageEfsKeyState.MatchedAvailable) == true
            ? association : null;
    }

    private (StorageEfsAssociationSnapshot? Association, StorageProblem? Problem) ReadBackupAssociation(
        StorageTarget target, string sid, StorageObjectIdentity? expected, CancellationToken ct)
    {
        if (target.Source.Kind != EntryKind.File)
            return (null, Problem(StorageReasonKind.FileCatBoundary, "efs-backup-file-only", "Select individual encrypted files to review their EFS keys."));
        if (StoragePaths.Capture([target.Source])[0] != target)
            return (null, Problem(StorageReasonKind.FileCatBoundary, "target-spelling", "The EFS target does not match its captured original path."));
        try
        {
            using var path = Walk(target.Path, reviewLeaf: false, ct);
            if (expected is { } reviewed && reviewed != path.LeafFacts.Identity)
                throw Changed("efs-backup-identity-changed", "The selected file no longer names the originally reviewed object.");
            using var windows = ProtectedWindowsPath(ct);
            RequireEncryptedFile(ct);
            PromoteEfsGuards(path, target.Path, ct);
            var association = WindowsEfsAssociations.Read(EfsApiPath(path, target.Path), path.LeafFacts.Identity, sid, Validate, ct);
            return (association, null);

            void Validate(CancellationToken token)
            {
                ValidateCapturedEfsPath(path, target.Path, token);
                foreach (var node in windows.Nodes)
                    EfsPathBinding.ValidateNode(Facts(node.Handle.File, windows.Volume.Binding, token), node.Facts);
                RequireEncryptedFile(token);
            }

            void RequireEncryptedFile(CancellationToken token)
            {
                var current = Inspect(path, windows.LeafFacts.Identity, token, compressionPolicy: false).Basic;
                if (current.Ineligible is { } reason) throw new StorageInspectionException(reason);
                if (current.IsDirectory || current.Efs != StorageObservationState.Enabled)
                    throw Refuse("efs-backup-encrypted-file", "The held item is no longer known to be an EFS-encrypted file.");
            }
        }
        catch (StorageInspectionException error) { return (null, error.Problem); }
        catch (UnauthorizedAccessException)
        {
            return (null, Problem(StorageReasonKind.PermissionOrPolicy, "efs-backup-access", "The selected file or its certificate evidence could not be accessed under this Windows account."));
        }
        catch (IOException)
        {
            return (null, Problem(StorageReasonKind.Unknown, "efs-backup-unreadable", "The selected file's certificate evidence could not be checked."));
        }
    }
}
