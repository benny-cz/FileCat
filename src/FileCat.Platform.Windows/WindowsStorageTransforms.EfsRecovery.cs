using System.Collections.Immutable;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    private StorageEfsRecoveryApproval? ReviewEfsRecovery(Job job, string invokerSid, StorageEfsConsent consent, CancellationToken ct,
        WindowsEfsKeys.InvokeBackupWizard? invokeWizard)
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

        var setup = InspectEfsSetup(invokerSid, consent, explicitSetupAccepted: true, ct);
        ct.ThrowIfCancellationRequested();
        if (setup.Canceled) return null;
        if (setup.Problem is { } failed) throw new StorageInspectionException(failed);
        var association = setup.Association ?? throw Refuse("efs-setup-association", "Windows did not establish the actual setup key association.");
        var keys = association.CurrentUserKeys.Where(key => key.InvokerSid == invokerSid && key.State == StorageEfsKeyState.MatchedAvailable &&
            key.CertificatePresent == true && key.ProviderAssociationPresent == true &&
            association.Users.Any(user => user.Sid == invokerSid && user.CertificateSha1 == key.CertificateSha1))
            .Select(key => new StorageEfsRecoveryKeyOption(key.CertificateSha1, key.Subject)).ToImmutableArray();
        if (keys.IsEmpty) throw Refuse("efs-setup-key-unavailable", "The actual setup key could not be matched to an accessible private key for this account.");

        string? previous = null;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var decision = job.Ask(new StorageEfsRecoveryDecision(account, invokerSid, keys, previous, setup.CleanupProblem, setup.RetainedSetupPath));
            if (decision.Action == DecisionAction.CancelJob) return null;
            if (decision.ApplyToAll || decision.EfsRecovery is not { } selected || !keys.Any(key => key.CertificateSha1 == selected.CertificateSha1))
                throw Refuse("efs-recovery-selection", "Choose a certificate from this job's actual EFS setup association.");
            if (decision.Action == DecisionAction.DeclareEfsRecovery)
                return StorageEfsRecoveryApproval.DeclareArrangement(consent, setup, selected.CertificateSha1, acknowledged: true);
            if (decision.Action != DecisionAction.BackUpEfsKey)
                throw Refuse("efs-recovery-action", "Choose a recovery declaration or request the Windows backup wizard.");

            var backup = job.RunEfsRecoveryInteraction(() => new WindowsEfsKeys().BackUpAssociatedCertificate(association,
                selected.CertificateSha1, selected.OwnerWindow, explicitlyRequested: true, ct, invokeWizard));
            ct.ThrowIfCancellationRequested();
            if (backup.Completion is { } completed) return StorageEfsRecoveryApproval.RecordWindowsExport(consent, setup, completed);
            previous = backup.Problem?.Message ?? "Windows canceled the backup. You can retry or declare an existing recovery arrangement.";
        }
    }
}
