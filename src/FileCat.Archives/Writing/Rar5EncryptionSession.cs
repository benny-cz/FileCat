using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Archive-local write keys, independently salted for payloads and hidden headers. Never persisted.</summary>
internal sealed class Rar5EncryptionSession : IDisposable
{
    internal const int DefaultCountPower = 19;
    private readonly byte[] _dataSalt, _headerSalt;
    private readonly Rar5DerivedKeys _dataKeys;
    private readonly Rar5DerivedKeys? _headerKeys;
    private bool _claimed, _disposed;
    internal bool HideNames { get; }
    internal int CountPower { get; }
    internal ReadOnlySpan<byte> DataSalt { get { Live(); return _dataSalt; } }
    internal ReadOnlySpan<byte> HeaderSalt { get { Live(); return _headerSalt; } }
    internal ReadOnlySpan<byte> DataCheck { get { Live(); return _dataKeys.PasswordCheck; } }
    internal ReadOnlySpan<byte> HeaderCheck { get { Live(); return _headerKeys is { } keys ? keys.PasswordCheck : ReadOnlySpan<byte>.Empty; } }

    internal Rar5EncryptionSession(ReadOnlySpan<char> password, bool hideNames, ArchiveCryptoWorkBudget budget,
        CancellationToken ct, int countPower = DefaultCountPower)
    {
        if (password.IsEmpty || countPower is < DefaultCountPower or > 24)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "New RAR5 encryption needs a nonempty password and a supported work factor.");
        ct.ThrowIfCancellationRequested(); byte[] encoded = ArchivePasswordEncoding.Encode(password, sevenZip: false);
        Rar5DerivedKeys? data = null, header = null;
        try
        {
            _dataSalt = RandomNumberGenerator.GetBytes(16); _headerSalt = hideNames ? RandomNumberGenerator.GetBytes(16) : [];
            data = Rar5EncryptionPrimitives.Derive(encoded, _dataSalt, countPower, budget, ct);
            if (hideNames) header = Rar5EncryptionPrimitives.Derive(encoded, _headerSalt, countPower, budget, ct);
            _dataKeys = data; _headerKeys = header; HideNames = hideNames; CountPower = countPower;
        }
        catch { data?.Dispose(); header?.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(encoded); }
    }

    internal void ClaimArchive()
    {
        Live();
        if (_claimed) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "An archive encryption session cannot be reused for another output.");
        _claimed = true;
    }

    internal (ArchiveAesCbcWriteStream Cipher, byte[] Iv) CreateCipher(Stream output, bool header,
        long maximumPlaintext, CancellationToken ct)
    {
        Live();
        if (!_claimed || header && !HideNames)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The RAR5 cipher does not belong to this output/header mode.");
        ct.ThrowIfCancellationRequested(); byte[] iv = RandomNumberGenerator.GetBytes(16);
        return (new(output, (header ? _headerKeys! : _dataKeys).EncryptionKey, iv, maximumPlaintext, ct), iv);
    }

    internal uint ProtectCrc32(uint crc) { Live(); return _dataKeys.ProtectCrc32(crc); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _dataKeys.Dispose(); _headerKeys?.Dispose();
    }
    private void Live() => ObjectDisposedException.ThrowIf(_disposed, this);
}
