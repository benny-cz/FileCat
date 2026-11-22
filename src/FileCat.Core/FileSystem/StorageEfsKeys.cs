namespace FileCat.Core.FileSystem;

public enum StorageEfsKeyState { MatchedAvailable, InteractionRequired, Unavailable, Unknown, Unsupported }
public enum StorageEfsKeyProvider { Unknown, Capi, Cng }

/// <summary>One account/certificate observation. It proves neither file decryptability nor backup recovery.</summary>
public sealed record StorageEfsKeyEvidence(string InvokerSid, string CertificateSha1, bool? CertificatePresent,
    bool? ProviderAssociationPresent, StorageEfsKeyState State, StorageEfsKeyProvider Provider, DateTime ObservedUtc,
    int? NativeError, StorageProblem? Problem)
{
    public string? Subject { get; init; }
    public StorageProblem? BackupProblem { get; init; }

    /// <summary>The key provider named by the certificate's key association, when it could be read. A key behind a
    /// smart-card provider can make Windows wait for the card even when a silent check reports the key unavailable.</summary>
    public string? ProviderName { get; init; }
}

/// <summary>E-only scheduled key check. The certificate hash must come from a reviewed actual file association.</summary>
public interface IStorageEfsKeys
{
    StorageEfsKeyEvidence ProbeAssociatedCertificate(string invokerSid, string certificateSha1, CancellationToken ct);
}
