using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// One archive's non-persistent write keys. Data members share one fresh salt/key and receive distinct random IVs;
/// hidden headers use an independently salted key. The caller disposes its ciphers before this session.
/// No password text/bytes survive construction. Never serialize this object or reuse it for another archive.
/// </summary>
internal sealed class SevenZipEncryptionSession : IDisposable
{
    internal const int DefaultCountPower = 19;
    private readonly byte[] _dataSalt, _headerSalt;
    private readonly SevenZipDerivedKey _dataKey;
    private readonly SevenZipDerivedKey? _headerKey;
    private bool _claimed, _disposed;
    internal bool HideNames { get; }
    internal int CountPower { get; }
    internal int HeaderCountPower { get; }
    internal int HeaderSaltLength => _headerSalt.Length;

    internal SevenZipEncryptionSession(ReadOnlySpan<char> password, bool hideNames, ArchiveCryptoWorkBudget budget,
        CancellationToken ct, int countPower = DefaultCountPower, int? headerCountPower = null, int headerSaltLength = 16)
    {
        if (password.IsEmpty || countPower is < DefaultCountPower or > 24 || headerCountPower is < DefaultCountPower or > 24 ||
            headerSaltLength is < 0 or > 16 || !hideNames && (headerCountPower is not null || headerSaltLength != 16))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "New 7z encryption needs a nonempty password and a supported work factor.");
        ct.ThrowIfCancellationRequested();
        byte[] encoded = ArchivePasswordEncoding.Encode(password, sevenZip: true);
        SevenZipDerivedKey? data = null, header = null;
        try
        {
            _dataSalt = RandomNumberGenerator.GetBytes(16);
            _headerSalt = hideNames ? RandomNumberGenerator.GetBytes(headerSaltLength) : [];
            data = SevenZipEncryptionPrimitives.Derive(encoded, _dataSalt, countPower, budget, ct);
            if (hideNames) header = SevenZipEncryptionPrimitives.Derive(encoded, _headerSalt, headerCountPower ?? countPower, budget, ct);
            _dataKey = data; _headerKey = header; HideNames = hideNames; CountPower = countPower; HeaderCountPower = headerCountPower ?? countPower;
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

    internal (ArchiveAesCbcWriteStream Cipher, byte[] Properties) CreateCipher(Stream output, bool header,
        long maximumPlaintext, CancellationToken ct)
    {
        Live();
        if (!_claimed || header && !HideNames)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The 7z cipher does not belong to this output/header mode.");
        ct.ThrowIfCancellationRequested();
        var salt = header ? _headerSalt : _dataSalt;
        byte[] properties = new byte[2 + salt.Length + 16];
        properties[0] = (byte)(0x40 | (salt.Length == 0 ? 0 : 0x80) | (header ? HeaderCountPower : CountPower));
        properties[1] = (byte)((salt.Length == 0 ? 0 : (salt.Length - 1) << 4) | 15);
        salt.CopyTo(properties, 2); RandomNumberGenerator.Fill(properties.AsSpan(2 + salt.Length, 16));
        return (new(output, (header ? _headerKey! : _dataKey).Key, properties.AsSpan(2 + salt.Length), maximumPlaintext, ct), properties);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _dataKey.Dispose(); _headerKey?.Dispose();
    }
    private void Live() => ObjectDisposedException.ThrowIf(_disposed, this);
}
