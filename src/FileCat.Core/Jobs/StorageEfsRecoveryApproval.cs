using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

internal sealed record StorageEfsBackupCompletion(StorageObjectIdentity ObservedIdentity, string InvokerSid, string CertificateSha1);

/// <summary>An invoking user's recovery choice for an observed setup key and a single reviewed scope.</summary>
internal sealed class StorageEfsRecoveryApproval
{
    private readonly StorageEfsConsent _consent;

    private StorageEfsRecoveryApproval(StorageEfsConsent consent, StorageEfsAssociationSnapshot association, string certificateSha1, StorageEfsRecoveryChoice choice)
    {
        _consent = consent;
        Evidence = new(association.Identity, association.InvokerSid, certificateSha1, choice);
    }

    internal StorageEfsReviewedKey Evidence { get; }
    internal string InvokerSid => Evidence.InvokerSid;
    internal string CertificateSha1 => Evidence.CertificateSha1;

    internal static StorageEfsRecoveryApproval DeclareArrangement(StorageEfsConsent consent, StorageEfsSetupResult setup,
        string certificateSha1, bool acknowledged)
    {
        if (!acknowledged) throw new ArgumentException("Acknowledge the recovery arrangement for the reviewed EFS certificate explicitly.", nameof(acknowledged));
        var association = RequireSetup(consent, setup, certificateSha1);
        return new(consent, association, certificateSha1, StorageEfsRecoveryChoice.DeclaredArrangement);
    }

    internal static StorageEfsRecoveryApproval RecordWindowsExport(StorageEfsConsent consent, StorageEfsSetupResult setup,
        StorageEfsBackupCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        var association = RequireSetup(consent, setup, completion.CertificateSha1);
        if (completion.InvokerSid != association.InvokerSid || completion.ObservedIdentity != association.Identity)
            throw new ArgumentException("The reported export belongs to a different EFS observation or invoking user.", nameof(completion));
        return new(consent, association, completion.CertificateSha1, StorageEfsRecoveryChoice.WindowsReportedExport);
    }

    private static StorageEfsAssociationSnapshot RequireSetup(StorageEfsConsent consent, StorageEfsSetupResult setup, string certificateSha1)
    {
        StorageEfsConsent.Require(consent, consent.ReviewRevision, consent.Action, consent.Scope, consent.RootIdentities);
        if (consent.Action != StorageAction.EncryptEfs ||
            setup is not { ProvisioningAttempted: true, Canceled: false, Problem: null, Association: { } association } ||
            !association.HasMatchedCurrentUserKey ||
            !association.Users.Any(user => user.Sid == association.InvokerSid && user.CertificateSha1 == certificateSha1) ||
            !association.CurrentUserKeys.Any(key => Matches(key, association.InvokerSid, certificateSha1)))
            throw new ArgumentException("The recovery choice requires a freshly matched certificate actually associated with this EFS setup.");
        return association;
    }

    internal bool Matches(StorageEfsConsent consent, string invokerSid) => consent.Action == StorageAction.EncryptEfs &&
        consent.ReviewRevision == _consent.ReviewRevision && consent.ConsentId == _consent.ConsentId && consent.Scope == _consent.Scope &&
        consent.RootIdentities.SequenceEqual(_consent.RootIdentities) && invokerSid == InvokerSid;

    internal bool Matches(StorageEfsKeyEvidence key) => Matches(key, InvokerSid, CertificateSha1);

    private static bool Matches(StorageEfsKeyEvidence key, string sid, string hash) =>
        key.InvokerSid == sid && key.CertificateSha1 == hash && key.State == StorageEfsKeyState.MatchedAvailable &&
        key.CertificatePresent == true && key.ProviderAssociationPresent == true;
}
