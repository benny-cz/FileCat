using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Owned AES key. No record-generated diagnostic representation or persistence; dispose after the job.</summary>
internal sealed class SevenZipDerivedKey(byte[] key) : IDisposable
{
    private bool _disposed;
    internal ReadOnlySpan<byte> Key { get { ObjectDisposedException.ThrowIf(_disposed, this); return key; } }
    public void Dispose() { if (!_disposed) { CryptographicOperations.ZeroMemory(key); _disposed = true; } }
}

/// <summary>
/// Bounded 7z SHA-256 KDF over raw UTF-16LE password bytes, salt and an eight-byte little-endian counter.
/// Format facts checked against the existing permitted AES decoder. New output must use a qualified normal KDF;
/// the legacy direct-key variant is a distinct reader operation, never an implicit fast-writing preset.
/// </summary>
internal static class SevenZipEncryptionPrimitives
{
    internal static SevenZipDerivedKey Derive(ReadOnlySpan<byte> utf16Password, ReadOnlySpan<byte> salt, int countPower,
        ArchiveCryptoWorkBudget budget, CancellationToken ct)
    {
        if (utf16Password.Length > ArchivePasswordEncoding.MaximumBytes || (utf16Password.Length & 1) != 0 ||
            salt.Length > 16 || countPower is < 0 or > 24 && countPower != 63)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The 7z password derivation parameters are invalid.");
        ct.ThrowIfCancellationRequested();
        byte[] key = new byte[32];
        if (countPower == 63)
        {
            budget.Reserve(0, salt.Length + utf16Password.Length);
            salt.CopyTo(key); utf16Password[..Math.Min(utf16Password.Length, key.Length - salt.Length)].CopyTo(key.AsSpan(salt.Length));
            return new(key);
        }
        long rounds = 1L << countPower;
        int frameBytes = salt.Length + utf16Password.Length + 8;
        budget.Reserve(rounds, checked(rounds * frameBytes + 128));
        byte[] frame = new byte[frameBytes]; salt.CopyTo(frame); utf16Password.CopyTo(frame.AsSpan(salt.Length));
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (long counter = 0; counter < rounds; counter++)
            {
                if ((counter & 255) == 0) ct.ThrowIfCancellationRequested();
                BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(frame.Length - 8), (ulong)counter);
                hash.AppendData(frame);
            }
            if (!hash.TryGetHashAndReset(key, out int written) || written != 32) throw new CryptographicException("Archive KDF primitive failed.");
            ct.ThrowIfCancellationRequested(); return new(key);
        }
        catch { CryptographicOperations.ZeroMemory(key); throw; }
        finally { CryptographicOperations.ZeroMemory(frame); }
    }
}
