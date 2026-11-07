// Encryption layouts/KDF/checksum semantics follow rardecode v2.4.5 archive50.go and reader.go, BSD-2-Clause.
// Copyright (c) 2015 Nicholas Waples; see licenses/rardecode/LICENSE and the pinned password source ledger.
using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Jobs;

namespace FileCat.Archives.Rar;

internal sealed record Rar5CryptoParameters(int CountPower, byte[] Salt, byte[] Iv, byte[] Check, bool UseMac)
{
    internal static Rar5CryptoParameters Parse(ReadOnlySpan<byte> data, bool header)
    {
        var r = new RarHeaderReader(data);
        if (r.VarInt() != 0) throw RarData.Unsupported("this RAR encryption version");
        ulong flags = r.VarInt();
        if ((flags & ~(header ? 1UL : 3UL)) != 0) throw RarData.Unsupported("unknown RAR encryption properties");
        int power = r.Bytes(1)[0];
        if (power > 24) throw new InvalidDataException("The RAR password derivation exceeds the supported work factor.");
        byte[] salt = r.Bytes(16).ToArray(), iv = header ? [] : r.Bytes(16).ToArray();
        byte[] check = (flags & 1) != 0 ? r.Bytes(12).ToArray() : [];
        if (r.Remaining != 0) throw RarData.Unsupported("trailing RAR encryption properties");
        return new(power, salt, iv, check, !header && (flags & 2) != 0);
    }
}

/// <summary>One RAR5/7 read or verification job. No settings/view persistence; dispose readers before this session.</summary>
internal sealed class Rar5ReadPasswordSession : IDisposable
{
    private readonly byte[] _password;
    private readonly ArchiveCryptoWorkBudget _budget;
    private readonly CancellationToken _ct;
    private readonly Dictionary<(int Power, string Salt), Rar5DerivedKeys> _keys = new();
    private readonly Queue<(int Power, string Salt)> _order = new();
    private bool _disposed;

    internal Rar5ReadPasswordSession(ReadOnlySpan<char> password, ArchiveCryptoWorkBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); _password = ArchivePasswordEncoding.Encode(password, sevenZip: false);
        _budget = budget; _ct = ct;
    }

    internal Rar5DerivedKeys Keys(Rar5CryptoParameters parameters)
    {
        ObjectDisposedException.ThrowIf(_disposed, this); _ct.ThrowIfCancellationRequested();
        var identity = (parameters.CountPower, Convert.ToHexString(parameters.Salt));
        if (!_keys.TryGetValue(identity, out var keys))
        {
            if (_keys.Count == 16) { var oldest = _order.Dequeue(); _keys[oldest].Dispose(); _keys.Remove(oldest); }
            try { keys = Rar5EncryptionPrimitives.Derive(_password, parameters.Salt, parameters.CountPower, _budget, _ct); }
            catch (FileCat.Core.Archives.ArchiveWriteException ex) { throw new InvalidDataException("The RAR password derivation exceeds this job's supported work budget.", ex); }
            _keys.Add(identity, keys); _order.Enqueue(identity);
        }
        if (parameters.Check.Length != 0)
        {
            var check = keys.Check(parameters.Check);
            if (check == Rar5PasswordCheck.CorruptCheckData) throw new InvalidDataException("The archive password check is damaged.");
            if (check == Rar5PasswordCheck.IncorrectPassword) throw new InvalidDataException("The supplied archive password is incorrect.");
        }
        return keys;
    }

    internal ICryptoTransform CreateDecryptor(Rar5CryptoParameters parameters, ReadOnlySpan<byte> iv)
    {
        if (iv.Length != 16) throw RarData.Corrupt("RAR encryption IV length");
        byte[] key = Keys(parameters).EncryptionKey.ToArray(), ownedIv = iv.ToArray();
        try
        {
            using var aes = Aes.Create(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None;
            return aes.CreateDecryptor(key, ownedIv);
        }
        finally { CryptographicOperations.ZeroMemory(key); CryptographicOperations.ZeroMemory(ownedIv); }
    }

    internal byte[] ReadHeader(Stream input, long position, Rar5CryptoParameters parameters, out int sizeBytes, out int size,
        out long dataStart, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (position < 0 || position > input.Length - 32) throw RarData.Corrupt("encrypted header past the end of the volume");
        byte[] iv = new byte[16], first = new byte[16], firstPlain = new byte[16]; byte[]? plain = null;
        input.Position = position; input.ReadExactly(iv); input.ReadExactly(first);
        try
        {
            using var decoder = CreateDecryptor(parameters, iv);
            if (decoder.TransformBlock(first, 0, 16, firstPlain, 0) != 16) throw RarData.Corrupt("encrypted header extent");
            size = 0; sizeBytes = 0;
            for (; sizeBytes < 3; sizeBytes++)
            {
                byte b = firstPlain[4 + sizeBytes]; size |= (b & 127) << (7 * sizeBytes);
                if ((b & 128) == 0) break;
            }
            if (sizeBytes == 3 || size < 2 || size > RarLimits.MaxHeaderBytes) throw RarData.Corrupt("encrypted RAR 5 header size");
            sizeBytes++;
            int rawLength = checked(4 + sizeBytes + size), padded = (rawLength + 15) & ~15;
            dataStart = checked(position + 16 + padded);
            if (dataStart > input.Length) throw RarData.Corrupt("encrypted header past the end of the volume");
            plain = new byte[padded]; firstPlain.CopyTo(plain, 0);
            if (padded > 16)
            {
                byte[] remaining = new byte[padded - 16]; input.ReadExactly(remaining); ct.ThrowIfCancellationRequested();
                if (decoder.TransformBlock(remaining, 0, remaining.Length, plain, 16) != remaining.Length) throw RarData.Corrupt("encrypted header extent");
            }
            byte[] final = decoder.TransformFinalBlock([], 0, 0);
            try { if (final.Length != 0) throw RarData.Corrupt("encrypted header tail"); }
            finally { CryptographicOperations.ZeroMemory(final); }
            uint expected = BinaryPrimitives.ReadUInt32LittleEndian(plain);
            if (Crc32.HashToUInt32(plain.AsSpan(4, sizeBytes + size)) != expected) throw RarData.Corrupt("header checksum");
            ct.ThrowIfCancellationRequested(); byte[] result = plain; plain = null; return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstPlain);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CryptographicOperations.ZeroMemory(_password);
        foreach (var keys in _keys.Values) keys.Dispose(); _keys.Clear(); _order.Clear();
    }
}
