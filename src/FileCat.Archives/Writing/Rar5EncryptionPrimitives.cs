// RAR5 KDF/password-check/checksum processing adapted from rardecode v2.4.5, archive50.go and reader.go,
// commit ad22ba91a2ab0d3ff6bca20cd978b33ee78faab1. Copyright (c) 2015 Nicholas Waples, BSD-2-Clause;
// see licenses/rardecode/LICENSE. FileCat adds bounded work, cancellation, framework primitives and owned-buffer
// disposal. Retain the upstream licence/attribution; this is not an UnRAR port or a clean-room claim.
using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal enum Rar5PasswordCheck { Match, IncorrectPassword, CorruptCheckData }

internal sealed class Rar5DerivedKeys : IDisposable
{
    private readonly byte[] _aes, _checksum, _check;
    private bool _disposed;
    internal Rar5DerivedKeys(byte[] aes, byte[] checksum, byte[] check) { _aes = aes; _checksum = checksum; _check = check; }
    internal ReadOnlySpan<byte> EncryptionKey { get { Live(); return _aes; } }
    internal ReadOnlySpan<byte> ChecksumKey { get { Live(); return _checksum; } }
    internal ReadOnlySpan<byte> PasswordCheck { get { Live(); return _check; } }

    internal Rar5PasswordCheck Check(ReadOnlySpan<byte> received)
    {
        Live(); if (received.Length != 12) return Rar5PasswordCheck.CorruptCheckData;
        Span<byte> hash = stackalloc byte[32]; SHA256.HashData(received[..8], hash);
        if (!CryptographicOperations.FixedTimeEquals(hash[..4], received[8..])) return Rar5PasswordCheck.CorruptCheckData;
        return CryptographicOperations.FixedTimeEquals(_check, received) ? Rar5PasswordCheck.Match : Rar5PasswordCheck.IncorrectPassword;
    }

    internal uint ProtectCrc32(uint crc32)
    {
        Live(); Span<byte> input = stackalloc byte[4]; Span<byte> mac = stackalloc byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(input, crc32);
        HMACSHA256.HashData(_checksum, input, mac);
        for (int index = 4; index < mac.Length; index++) mac[index & 3] ^= mac[index];
        uint result = BinaryPrimitives.ReadUInt32LittleEndian(mac); CryptographicOperations.ZeroMemory(mac); return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_aes); CryptographicOperations.ZeroMemory(_checksum); CryptographicOperations.ZeroMemory(_check);
        _disposed = true;
    }
    private void Live() => ObjectDisposedException.ThrowIf(_disposed, this);
}

internal static class Rar5EncryptionPrimitives
{
    internal static Rar5DerivedKeys Derive(ReadOnlySpan<byte> utf8Password, ReadOnlySpan<byte> salt, int countPower,
        ArchiveCryptoWorkBudget budget, CancellationToken ct)
    {
        if (utf8Password.Length > ArchivePasswordEncoding.MaximumBytes || salt.Length != 16 || countPower is < 0 or > 24)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The RAR5 password derivation parameters are invalid.");
        ct.ThrowIfCancellationRequested();
        int rounds = 1 << countPower;
        budget.Reserve(rounds + 32L, checked((rounds + 32L) * 256 + utf8Password.Length + 128));
        byte[] password = utf8Password.ToArray(), current = new byte[32], next = new byte[32], total = new byte[32];
        byte[]? aes = null, checksum = null, check = null;
        try
        {
            using var hmac = new HMACSHA256(password);
            Span<byte> initial = stackalloc byte[20]; salt.CopyTo(initial); initial[16..].Clear(); initial[19] = 1;
            Span<byte> digest = stackalloc byte[32];
            Compute(initial, current); current.CopyTo(total, 0);
            for (int phase = 0; phase < 3; phase++)
            {
                int count = phase == 0 ? rounds - 1 : 16;
                for (int iteration = 0; iteration < count; iteration++)
                {
                    if ((iteration & 255) == 0) ct.ThrowIfCancellationRequested();
                    Compute(current, next);
                    for (int index = 0; index < 32; index++) total[index] ^= next[index];
                    (current, next) = (next, current);
                }
                if (phase == 0) aes = (byte[])total.Clone();
                else if (phase == 1) checksum = (byte[])total.Clone();
                else
                {
                    check = new byte[12]; total.AsSpan(0, 8).CopyTo(check);
                    for (int index = 8; index < 32; index++) check[index & 7] ^= total[index];
                    SHA256.HashData(check.AsSpan(0, 8), digest); digest[..4].CopyTo(check.AsSpan(8));
                }
            }
            ct.ThrowIfCancellationRequested();
            return new(aes!, checksum!, check!);

            void Compute(ReadOnlySpan<byte> input, Span<byte> output)
            {
                if (!hmac.TryComputeHash(input, output, out int written) || written != 32) throw new CryptographicException("Archive KDF primitive failed.");
            }
        }
        catch
        {
            if (aes is not null) CryptographicOperations.ZeroMemory(aes);
            if (checksum is not null) CryptographicOperations.ZeroMemory(checksum);
            if (check is not null) CryptographicOperations.ZeroMemory(check);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password); CryptographicOperations.ZeroMemory(current);
            CryptographicOperations.ZeroMemory(next); CryptographicOperations.ZeroMemory(total);
        }
    }
}
