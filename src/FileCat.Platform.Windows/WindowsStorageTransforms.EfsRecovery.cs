using System.Collections.Immutable;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    internal sealed class EfsRecoveryProviderAccess(string invokerSid)
    {
        private readonly HashSet<string> _accessed = new(StringComparer.Ordinal);

        internal bool Record(string selectedCertificate, StorageEfsKeyEvidence compared)
        {
            if (compared is not { State: StorageEfsKeyState.MatchedAvailable, CertificatePresent: true,
                    ProviderAssociationPresent: true } || compared.InvokerSid != invokerSid ||
                compared.CertificateSha1 != selectedCertificate)
                return false;
            _accessed.Add(selectedCertificate);
            return true;
        }

        internal Action<Action>? OwnerForBackup(string certificateSha1, Action<Action>? invokeProviderOwner) =>
            _accessed.Contains(certificateSha1) ? invokeProviderOwner : null;
    }

    private StorageEfsRecoveryApproval? ReviewEfsRecovery(Job job, string invokerSid, StorageEfsConsent consent, CancellationToken ct,
        WindowsEfsKeys.InvokeBackupWizard? invokeWizard, Action<Action>? invokeProviderOwner)
    {
        string account;
        using (StorageNativeAllowance.RequireCurrent().ClaimScratch())
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (identity.User?.Value != invokerSid) throw Changed("efs-invoker", "The invoking EFS user changed before setup.");
            account = identity.Name;
        }

        var setupDecision = job.Ask(new ConfirmRequest("Set up EFS", $"Windows account {account} ({invokerSid})\n\n" +
            "Windows may create or enroll an encryption key. That key remains if you cancel later. Setup uses a private empty file and leaves your selected items alone.",
            "", "efs-setup", [DecisionAction.Proceed, DecisionAction.CancelJob])
        { ProceedLabel = "Set up EFS…", InitialAction = DecisionAction.CancelJob, OffersApplyToAll = false });
        if (setupDecision.Action != DecisionAction.Proceed || setupDecision.ApplyToAll) return null;

        var setup = InspectEfsSetup(invokerSid, consent, explicitSetupAccepted: true, ct, allowProviderInteraction: invokeProviderOwner is not null);
        RecordSetupCleanupIssue(job, setup);
        ct.ThrowIfCancellationRequested();
        if (setup.Canceled) return null;
        if (setup.Problem is { } failed) throw new StorageInspectionException(failed);
        var association = setup.Association ?? throw Refuse("efs-setup-association", "Windows did not establish the actual setup key association.");
        string? previous = null;
        string? selectedCertificate = null;
        var providerAccess = new EfsRecoveryProviderAccess(invokerSid);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var keys = association.CurrentUserKeys.Where(key => key.InvokerSid == invokerSid &&
                key.CertificatePresent == true && key.ProviderAssociationPresent == true &&
                association.Users.Any(user => user.Sid == invokerSid && user.CertificateSha1 == key.CertificateSha1) &&
                (key.State == StorageEfsKeyState.MatchedAvailable ||
                 (invokeProviderOwner is not null && WindowsEfsKeys.CanRequestKeyAccess(key))))
                .Select(key => new StorageEfsRecoveryKeyOption(key.CertificateSha1, key.Subject,
                    RequiresProviderAccess: key.State == StorageEfsKeyState.InteractionRequired, BackupProblem: key.BackupProblem)).ToImmutableArray();
            if (keys.IsEmpty) throw Refuse("efs-setup-key-unavailable", "The actual setup key could not be matched to an accessible private key for this account.");
            var decision = job.Ask(new StorageEfsRecoveryDecision(account, invokerSid, keys, previous, setup.CleanupProblem,
                setup.RetainedSetupPath, selectedCertificate));
            if (decision.Action == DecisionAction.CancelJob) return null;
            if (decision.ApplyToAll || decision.EfsRecovery is not { } selected ||
                keys.FirstOrDefault(key => key.CertificateSha1 == selected.CertificateSha1) is not { } option)
                throw Refuse("efs-recovery-selection", "Choose a certificate from this job's actual EFS setup association.");
            ct.ThrowIfCancellationRequested();
            selectedCertificate = option.CertificateSha1;
            if (option.RequiresProviderAccess)
            {
                if (decision.Action != DecisionAction.AccessEfsKey)
                    throw Refuse("efs-recovery-access", "Verify access to this EFS key before choosing a recovery arrangement.");
                var provider = association.CurrentUserKeys.First(key =>
                    key.InvokerSid == invokerSid && key.CertificateSha1 == selected.CertificateSha1).Provider;
                var compared = job.RunEfsRecoveryInteraction(() => provider == StorageEfsKeyProvider.Capi
                    ? _efsKeys.AccessReviewedCapiKey(association, selected.CertificateSha1, selected.OwnerWindow, explicitlyRequested: true, ct)
                    : _efsKeys.AccessReviewedCngKey(association, selected.CertificateSha1, selected.OwnerWindow, explicitlyRequested: true, ct, invokeProviderOwner));
                ct.ThrowIfCancellationRequested();
                if (providerAccess.Record(selectedCertificate, compared))
                {
                    association = association with { CurrentUserKeys = association.CurrentUserKeys.Select(key =>
                        key.InvokerSid == invokerSid && key.CertificateSha1 == selectedCertificate ? compared : key).ToImmutableArray() };
                    setup = setup with { Association = association };
                    previous = null;
                }
                else previous = compared.Problem?.Message ?? "Windows could not verify access to this EFS key.";
                continue;
            }
            if (decision.Action == DecisionAction.DeclareEfsRecovery)
                return StorageEfsRecoveryApproval.DeclareArrangement(consent, setup, selected.CertificateSha1, acknowledged: true);
            if (decision.Action != DecisionAction.BackUpEfsKey)
                throw Refuse("efs-recovery-action", "Choose a recovery declaration or request the Windows backup wizard.");
            if (option.BackupProblem is { } blocked)
            {
                previous = blocked.Message;
                continue;
            }

            var backup = job.RunEfsRecoveryInteraction(() => _efsKeys.BackUpAssociatedCertificate(association,
                selected.CertificateSha1, selected.OwnerWindow, explicitlyRequested: true, ct, invokeWizard,
                providerAccess.OwnerForBackup(selected.CertificateSha1, invokeProviderOwner)));
            ct.ThrowIfCancellationRequested();
            if (backup.Completion is { } completed) return StorageEfsRecoveryApproval.RecordWindowsExport(consent, setup, completed);
            previous = backup.Problem?.Message ?? "Windows canceled the backup. You can retry or declare an existing recovery arrangement.";
        }
    }

    internal static void RecordSetupCleanupIssue(Job job, StorageEfsSetupResult setup)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.CleanupProblem is not { } cleanup) return;
        // Setup may also have failed or been canceled. A separate issue keeps the exact retained
        // artifact visible even when no recovery-choice dialog can be shown afterward.
        job.AddIssue(new JobIssue(IssueSeverity.Error,
            setup.RetainedSetupPath ?? setup.AttemptedSetupPath ?? "EFS setup artifact",
            cleanup.Message, StepOutcome.PartiallyApplied)
        {
            Cause = cleanup.Code,
            RequestedAction = "EFS key setup",
            CommittedEffect = "Owned setup artifact retained for review",
            SourceDisposition = "No selected item changed by this setup attempt",
            Certainty = setup.RetainedSetupIdentity is { } identity
                ? "Captured setup identity: " + identity + ". Verify it before cleanup."
                : "The setup identity could not be retained. Verify the artifact before cleanup.",
        });
    }
}
