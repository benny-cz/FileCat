using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>An invoking user's recovery declaration for an observed setup key and a single reviewed scope.</summary>
internal sealed class StorageEfsRecoveryApproval
{
    private readonly StorageEfsConsent _consent;

    private StorageEfsRecoveryApproval(StorageEfsConsent consent, string invokerSid, string certificateSha1)
    {
        _consent = consent;
        InvokerSid = invokerSid;
        CertificateSha1 = certificateSha1;
    }

    internal string InvokerSid { get; }
    internal string CertificateSha1 { get; }

    internal static StorageEfsRecoveryApproval DeclareArrangement(StorageEfsConsent consent, StorageEfsSetupResult setup,
        string certificateSha1, bool acknowledged)
    {
        StorageEfsConsent.Require(consent, consent.ReviewRevision, consent.Action, consent.Scope, consent.RootIdentities);
        if (!acknowledged || consent.Action != StorageAction.EncryptEfs ||
            setup is not { ProvisioningAttempted: true, Canceled: false, Problem: null, Association: { } association } ||
            !association.HasMatchedCurrentUserKey ||
            !association.Users.Any(user => user.Sid == association.InvokerSid && user.CertificateSha1 == certificateSha1) ||
            !association.CurrentUserKeys.Any(key => Matches(key, association.InvokerSid, certificateSha1)))
            throw new ArgumentException("An explicit recovery arrangement is required for a freshly matched, actual EFS setup certificate.");
        return new(consent, association.InvokerSid, certificateSha1);
    }

    internal bool Matches(StorageEfsConsent consent, string invokerSid) => consent.Action == StorageAction.EncryptEfs &&
        consent.ReviewRevision == _consent.ReviewRevision && consent.ConsentId == _consent.ConsentId && consent.Scope == _consent.Scope &&
        consent.RootIdentities.SequenceEqual(_consent.RootIdentities) && invokerSid == InvokerSid;

    internal bool Matches(StorageEfsKeyEvidence key) => Matches(key, InvokerSid, CertificateSha1);

    internal bool Matches(StorageEfsAssociationSnapshot association) => association.InvokerSid == InvokerSid &&
        association.Users.Any(user => user.Sid == InvokerSid && user.CertificateSha1 == CertificateSha1) &&
        association.Users.All(user => user.Sid is not null && (user.Sid != InvokerSid || user.CertificateSha1 == CertificateSha1)) &&
        association.CurrentUserKeys.Any(Matches);

    private static bool Matches(StorageEfsKeyEvidence key, string sid, string hash) =>
        key.InvokerSid == sid && key.CertificateSha1 == hash && key.State == StorageEfsKeyState.MatchedAvailable &&
        key.CertificatePresent == true && key.ProviderAssociationPresent == true;
}
