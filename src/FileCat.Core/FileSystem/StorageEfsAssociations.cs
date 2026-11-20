using System.Collections.Immutable;

namespace FileCat.Core.FileSystem;

public sealed record StorageEfsCertificateAssociation(string? Sid, string CertificateSha1, string? Display);

/// <summary>Actual file-associated metadata and fresh current-user key observations, not recovery authority.</summary>
public sealed record StorageEfsAssociationSnapshot(StorageObjectIdentity Identity, string InvokerSid,
    ImmutableArray<StorageEfsCertificateAssociation> Users, ImmutableArray<StorageEfsKeyEvidence> CurrentUserKeys)
{
    public bool HasMatchedCurrentUserKey => !Users.IsDefault && !CurrentUserKeys.IsDefault && CurrentUserKeys.Any(key =>
        key.InvokerSid == InvokerSid && key.State == StorageEfsKeyState.MatchedAvailable && key.CertificatePresent == true && key.ProviderAssociationPresent == true &&
        Users.Any(user => user.Sid == InvokerSid && user.CertificateSha1 == key.CertificateSha1));
}

public sealed record StorageEfsSetupResult(StorageEfsAssociationSnapshot? Association, bool ProvisioningAttempted,
    bool Canceled, StorageProblem? Problem, StorageProblem? CleanupProblem, string? RetainedSetupPath, StorageObjectIdentity? RetainedSetupIdentity)
{
    public string? AttemptedSetupPath { get; init; }
}
