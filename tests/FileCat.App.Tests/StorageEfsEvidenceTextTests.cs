using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class StorageEfsEvidenceTextTests
{
    [Theory]
    [InlineData(StorageEfsRecoveryChoice.DeclaredArrangement)]
    [InlineData(StorageEfsRecoveryChoice.WindowsReportedExport)]
    public void A_call_report_displays_the_recorded_recovery_choice_and_the_observed_key(StorageEfsRecoveryChoice choice)
    {
        var identity = new StorageObjectIdentity(1, new string('A', 32)) { VolumeBinding = Guid.NewGuid().ToString() };
        var before = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, identity, false, null);
        string reviewed = new('B', 40), observed = new('C', 40);
        var result = new StorageMutationResult(before, before with { Efs = StorageObservationState.Enabled }, StorageEffect.Uncertain,
            new(StorageReasonKind.PermissionOrPolicy, "efs-recovery-review-required", "Windows associated an unexpected key.", true))
        {
            NativeCallAttempted = true,
            EfsKeys = new(true, [new(observed, StorageEfsKeyState.MatchedAvailable)]),
        };
        var call = new StorageHistoryCall(1, 0, @"C:\scope\file", identity, before, StorageObservationState.Enabled,
            StorageCallDisposition.Observed, null, result)
        { EfsReviewedKey = new(identity, "S-1-5-21-1-2-3-1000", reviewed, choice) };
        var page = new StorageCallEvidencePage(new(Guid.NewGuid(), 3, 2000, new string('A', 64)), [call], false, 0, 1, 2000)
        { Action = StorageAction.EncryptEfs, Scope = StorageScope.SelectedItems, ReviewRevision = Guid.NewGuid() };
        string text = StorageCallEvidenceText.Format(page, false);
        Assert.Contains("Reviewed EFS certificate \"" + reviewed + "\"", text);
        Assert.Contains("Observed EFS certificate \"" + observed + "\" [MatchedAvailable]", text);
        Assert.Contains(choice == StorageEfsRecoveryChoice.WindowsReportedExport
            ? "Windows reported private-key export completed." : "Recovery arrangement acknowledged by the user.", text);
        Assert.Contains("observed effect: Uncertain", text);
    }

    [Theory]
    [InlineData(StorageAction.EncryptEfs)]
    [InlineData(StorageAction.DecryptEfs)]
    public void Recorded_Efs_calls_name_the_effect_and_both_independent_acknowledgment_meanings(StorageAction action)
    {
        var identity = new StorageObjectIdentity(1, new string('A', 32)) { VolumeBinding = Guid.NewGuid().ToString() };
        var consent = new StorageEfsConsent(Guid.NewGuid(), Guid.NewGuid(), action, StorageScope.ExistingDescendants,
            [identity], new(true, action == StorageAction.EncryptEfs));
        var before = new StorageObservation(StorageObservationState.Disabled,
            action == StorageAction.EncryptEfs ? StorageObservationState.Disabled : StorageObservationState.Enabled, identity, false, null);
        var desired = action == StorageAction.EncryptEfs ? StorageObservationState.Enabled : StorageObservationState.Disabled;
        var call = new StorageHistoryCall(1, 0, @"C:\scope\file", identity, before, desired, null, null, null) { EfsConsentId = consent.ConsentId };
        var page = new StorageCallEvidencePage(new(Guid.NewGuid(), 2, 2000, new string('A', 64)), [call], false, 0, 1, 2000)
            { Action = action, Scope = consent.Scope, ReviewRevision = consent.ReviewRevision, EfsConsent = consent };
        string text = StorageCallEvidenceText.Format(page, true);
        Assert.Contains("Desired EFS state: " + desired, text); Assert.DoesNotContain("Desired compression state", text);
        Assert.Contains("Concurrent in-place link-change limitation accepted: True", text);
        Assert.Contains(consent.ConsentId.ToString("D"), text);
        Assert.Contains("do not establish key recovery, output approval or prevention of concurrent link redirection", text);
        Assert.Contains(action == StorageAction.EncryptEfs ? "including streams added before completion, accepted: True"
            : "All-stream decompression consent: not applicable to EFS decryption.", text);
        var history = new StorageHistorySnapshot(page.Prefix, [new(0, 0, call.Path, before)], action, consent.Scope,
            consent.ReviewRevision, true, true, false, false, false, null, call) { EfsConsent = consent };
        var reviewed = new StorageReviewedJournal(page.Prefix.JobId, "directory", "history.fcj", new("file", 2000, new string('A', 64)), history);
        string reviewText = StorageHistoryReport.Format(new(reviewed, null, true, []));
        Assert.Contains("Recorded EFS scope consent: " + consent.ConsentId.ToString("D"), reviewText);
        Assert.Contains("Concurrent in-place link-change limitation accepted: True", reviewText);
        Assert.Contains("do not establish key recovery, output approval or prevention of concurrent link redirection", reviewText);
    }
}
