using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Receive one role/request-bound secret before any IO; consumers keep the span inside its one-use lifetime.</summary>
internal static class SevenZipWorkerPassword
{
    internal static ArchiveWorkerSecretGrant? Receive(ArchiveWorkerProtocol protocol, Stream input, ArchiveWriteProfile profile,
        ArchiveWorkerRole role, string binding, CancellationToken ct)
    {
        if (profile.Encryption.Mode == ArchiveEncryptionMode.None) return null;
        if (protocol.ProtocolVersion != ArchiveWorkerProtocol.SecretVersion)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "An encrypted 7z job requires the fixed protocol-2 endpoint.");
        return ArchiveWorkerSecretWire.ReadGrant(protocol, input, role, binding, ct);
    }
}
