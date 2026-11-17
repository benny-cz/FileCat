namespace FileCat.Core.FileSystem;

public enum StorageEfsKeyState { MatchedAvailable, InteractionRequired, Unavailable, Unknown, Unsupported }
public enum StorageEfsKeyProvider { Unknown, Capi, Cng }

/// <summary>One account/certificate observation. It proves neither file decryptability nor backup recovery.</summary>
public sealed record StorageEfsKeyEvidence(string InvokerSid, string CertificateSha1, bool? CertificatePresent,
    bool? ProviderAssociationPresent, StorageEfsKeyState State, StorageEfsKeyProvider Provider, DateTime ObservedUtc,
    int? NativeError, StorageProblem? Problem);

/// <summary>E-only scheduled key check. The certificate hash must come from a reviewed actual file association.</summary>
public interface IStorageEfsKeys
{
    StorageEfsKeyEvidence ProbeAssociatedCertificate(string invokerSid, string certificateSha1, CancellationToken ct);
}
