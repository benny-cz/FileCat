using FileCat.Archives.Rar;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>One role/request-bound grant and one aggregate KDF budget. Dispose sessions before leaving the worker.</summary>
internal sealed class Rar5WorkerPasswords : IDisposable
{
    internal Rar5ReadPasswordSession? Read { get; private set; }
    internal Rar5EncryptionSession? Write { get; private set; }
    private Rar5WorkerPasswords() { }
    internal static Rar5WorkerPasswords Receive(ArchiveWorkerProtocol protocol, Stream input, ArchiveWriteProfile profile,
        ArchiveWorkerRole role, string binding, bool read, bool write, CancellationToken ct)
    {
        var result = new Rar5WorkerPasswords();
        if (profile.Encryption.Mode == ArchiveEncryptionMode.None) return result;
        if (protocol.ProtocolVersion != ArchiveWorkerProtocol.SecretVersion)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "An encrypted RAR job requires the fixed protocol-2 endpoint.");
        using var grant = ArchiveWorkerSecretWire.ReadGrant(protocol, input, role, binding, ct);
        try
        {
            return grant.UseOnce(password =>
            {
                var budget = new ArchiveCryptoWorkBudget(); ct.ThrowIfCancellationRequested();
                if (read) result.Read = new(password, budget, ct);
                if (write) result.Write = new(password, profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames, budget, ct, profile.Encryption.CountPower);
                ct.ThrowIfCancellationRequested(); return result;
            });
        }
        catch { result.Dispose(); throw; }
    }
    public void Dispose() { try { Write?.Dispose(); } finally { Read?.Dispose(); Write = null; Read = null; } }
}
