using System.Security.Cryptography;
using FileCat.Archives.Writing;

namespace FileCat.Archives;

/// <summary>
/// One read/verification job's password and bounded KDF cache. This is not a persistent view/settings credential.
/// Dispose readers/entry streams before their session. Parent UI/secret-grant plumbing is a separate integration.
/// </summary>
internal sealed class SevenZipReadPasswordSession : IDisposable
{
    private readonly byte[] _password;
    private readonly ArchiveCryptoWorkBudget _budget;
    private readonly CancellationToken _ct;
    private readonly Dictionary<(int Power, string Salt), SevenZipDerivedKey> _keys = new();
    private readonly Queue<(int Power, string Salt)> _order = new();
    private bool _disposed;

    internal SevenZipReadPasswordSession(ReadOnlySpan<char> password, ArchiveCryptoWorkBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); _password = ArchivePasswordEncoding.Encode(password, sevenZip: true);
        _budget = budget; _ct = ct;
    }

    internal ICryptoTransform CreateDecryptor(int countPower, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> iv)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _ct.ThrowIfCancellationRequested();
        if (salt.Length > 16 || iv.Length != 16 || countPower is < 0 or > 24 && countPower != 63)
            throw new InvalidDataException("The encrypted 7z coder parameters are invalid or exceed the read profile.");
        var identity = (countPower, Convert.ToHexString(salt));
        if (!_keys.TryGetValue(identity, out var key))
        {
            // Bounded FIFO: eviction clears a key; future reuse still spends this job's monotonic KDF budget.
            if (_keys.Count == 16) { var oldest = _order.Dequeue(); _keys[oldest].Dispose(); _keys.Remove(oldest); }
            try { key = SevenZipEncryptionPrimitives.Derive(_password, salt, countPower, _budget, _ct); }
            catch (FileCat.Core.Archives.ArchiveWriteException ex) { throw new InvalidDataException("The 7z password derivation exceeds this job's supported work budget.", ex); }
            _keys.Add(identity, key); _order.Enqueue(identity);
        }
        byte[] ownedKey = key.Key.ToArray(), ownedIv = iv.ToArray();
        try
        {
            using var aes = Aes.Create(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None;
            return aes.CreateDecryptor(ownedKey, ownedIv);
        }
        finally { CryptographicOperations.ZeroMemory(ownedKey); CryptographicOperations.ZeroMemory(ownedIv); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CryptographicOperations.ZeroMemory(_password);
        foreach (var key in _keys.Values) key.Dispose(); _keys.Clear(); _order.Clear();
    }
}
