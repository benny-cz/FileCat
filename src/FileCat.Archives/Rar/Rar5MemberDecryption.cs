using System.Buffers.Binary;
using System.Security.Cryptography;

namespace FileCat.Archives.Rar;

/// <summary>One packed member's CBC reader and optional copied checksum key; closed on completion or reader cleanup.</summary>
internal sealed class Rar5MemberDecryption : Stream
{
    private readonly CryptoStream _stream;
    private readonly byte[] _checksumKey;
    private readonly Action<Rar5MemberDecryption> _closed;
    private bool _disposed;

    internal Rar5MemberDecryption(Stream packed, long packedLength, Rar5CryptoParameters parameters, Rar5ReadPasswordSession password,
        Action<Rar5MemberDecryption> closed)
    {
        // Packed streams can be forward-only; the checked header/segment descriptor owns the physical extent.
        if (packedLength < 0 || (packedLength & 15) != 0) throw RarData.Corrupt("encrypted member block alignment");
        _checksumKey = parameters.UseMac ? password.Keys(parameters).ChecksumKey.ToArray() : [];
        ICryptoTransform? decoder = null;
        try
        {
            decoder = password.CreateDecryptor(parameters, parameters.Iv);
            _stream = new CryptoStream(packed, decoder, CryptoStreamMode.Read); _closed = closed;
        }
        catch { decoder?.Dispose(); CryptographicOperations.ZeroMemory(_checksumKey); throw; }
    }

    internal uint ProtectCrc(uint crc)
    {
        if (_checksumKey.Length == 0) return crc;
        ObjectDisposedException.ThrowIf(_disposed, this);
        Span<byte> source = stackalloc byte[4]; Span<byte> mac = stackalloc byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(source, crc); HMACSHA256.HashData(_checksumKey, source, mac);
        for (int i = 4; i < mac.Length; i++) mac[i & 3] ^= mac[i];
        uint result = BinaryPrimitives.ReadUInt32LittleEndian(mac); CryptographicOperations.ZeroMemory(mac); return result;
    }

    internal byte[] ProtectHash(ReadOnlySpan<byte> hash)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _checksumKey.Length == 0 ? hash.ToArray() : HMACSHA256.HashData(_checksumKey, hash);
    }

    public override int Read(byte[] buffer, int offset, int count) { ObjectDisposedException.ThrowIf(_disposed, this); return _stream.Read(buffer, offset, count); }
    public override int Read(Span<byte> buffer) { ObjectDisposedException.ThrowIf(_disposed, this); return _stream.Read(buffer); }
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            try { _stream.Dispose(); }
            finally { CryptographicOperations.ZeroMemory(_checksumKey); _closed(this); }
        }
        base.Dispose(disposing);
    }
    public override bool CanRead => !_disposed;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
