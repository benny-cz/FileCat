using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// Stream-only AES-256-CBC primitive for the existing RAR5/7z formats, with explicit zero-tail completion.
/// It does not authenticate ciphertext, invent a key/salt/IV policy, own the output grant or finalize on Dispose.
/// A caller must Finish only after healthy source/codec completion and separately record the unpadded length.
/// </summary>
internal sealed class ArchiveAesCbcWriteStream : Stream
{
    private readonly Stream _output;
    private readonly CancellationToken _ct;
    private readonly long _maximumPlaintext;
    private readonly Aes _aes;
    private readonly ICryptoTransform _encryptor;
    private readonly byte[] _plain;
    private readonly byte[] _encrypted;
    private int _pending;
    private bool _completed, _failed, _disposed, _cryptoDisposed;
    internal long PlaintextBytes { get; private set; }
    internal long CiphertextBytes { get; private set; }

    internal ArchiveAesCbcWriteStream(Stream output, ReadOnlySpan<byte> key, ReadOnlySpan<byte> iv,
        long maximumPlaintext, CancellationToken ct)
    {
        if (!output.CanWrite || key.Length != 32 || iv.Length != 16 ||
            maximumPlaintext is < 0 or > ArchiveWritePolicy.MaxPhysicalBytes)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive cipher parameters or output grant are invalid.");
        ct.ThrowIfCancellationRequested(); _output = output; _ct = ct; _maximumPlaintext = maximumPlaintext;
        int blockBytes = ArchiveIoAuthority.MaximumReadBytes & ~15; // proxy frames reserve control bytes; the payload cap is not block-aligned
        _plain = new byte[blockBytes]; _encrypted = new byte[blockBytes];
        byte[] ownedKey = key.ToArray(), ownedIv = iv.ToArray();
        Aes? aes = null;
        try
        {
            aes = Aes.Create(); aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None;
            _encryptor = aes.CreateEncryptor(ownedKey, ownedIv); _aes = aes;
        }
        catch { aes?.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(ownedKey); CryptographicOperations.ZeroMemory(ownedIv); }
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void WriteByte(byte value) { Span<byte> one = stackalloc byte[1]; one[0] = value; Write(one); }
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Writable();
        try
        {
            _ct.ThrowIfCancellationRequested();
            if (buffer.Length > _maximumPlaintext - PlaintextBytes)
                throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The archive cipher input exceeds its granted extent.");
            while (!buffer.IsEmpty)
            {
                _ct.ThrowIfCancellationRequested();
                int count = Math.Min(_plain.Length - _pending, buffer.Length);
                buffer[..count].CopyTo(_plain.AsSpan(_pending)); _pending += count;
                PlaintextBytes += count; buffer = buffer[count..];
                if (_pending == _plain.Length) EncryptPrefix(_pending);
            }
        }
        catch { Stop(); throw; }
    }

    /// <summary>Padding is zero bytes to a block boundary; aligned/empty input receives no additional block.</summary>
    internal void Finish()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new IOException("The archive cipher stream is no longer writable.");
        if (_completed) return;
        Writable();
        try
        {
            _ct.ThrowIfCancellationRequested();
            if (_pending > 0)
            {
                int padded = (_pending + 15) & ~15;
                _plain.AsSpan(_pending, padded - _pending).Clear(); _pending = padded;
                EncryptPrefix(padded);
            }
            byte[] final = _encryptor.TransformFinalBlock([], 0, 0);
            try
            {
                if (final.Length != 0) throw new CryptographicException("Archive CBC finalization produced an unexpected block.");
            }
            finally { CryptographicOperations.ZeroMemory(final); }
            _ct.ThrowIfCancellationRequested(); _completed = true; ClearCrypto();
        }
        catch { Stop(); throw; }
    }

    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed) throw new IOException("The archive cipher stream is no longer writable.");
        try
        {
            _ct.ThrowIfCancellationRequested();
            int count = _completed ? 0 : _pending & ~15;
            if (count > 0) EncryptPrefix(count); // incomplete data remains pending; Flush is not EOF
            _output.Flush();
        }
        catch { Stop(); throw; }
    }

    private void EncryptPrefix(int count)
    {
        _ct.ThrowIfCancellationRequested();
        int written = _encryptor.TransformBlock(_plain, 0, count, _encrypted, 0);
        if (written != count) throw new CryptographicException("Archive CBC transform produced an unexpected extent.");
        int remaining = _pending - count;
        if (remaining > 0) _plain.AsSpan(count, remaining).CopyTo(_plain);
        CryptographicOperations.ZeroMemory(_plain.AsSpan(remaining)); _pending = remaining;
        try
        {
            _ct.ThrowIfCancellationRequested(); _output.Write(_encrypted.AsSpan(0, written)); CiphertextBytes += written;
        }
        finally { CryptographicOperations.ZeroMemory(_encrypted); }
    }

    private void Writable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failed || _completed) throw new IOException("The archive cipher stream is no longer writable.");
    }
    private void Stop() { _failed = true; ClearCrypto(); }
    private void ClearCrypto()
    {
        CryptographicOperations.ZeroMemory(_plain); CryptographicOperations.ZeroMemory(_encrypted); _pending = 0;
        if (_cryptoDisposed) return;
        _cryptoDisposed = true;
        try { _encryptor.Dispose(); } finally { _aes.Dispose(); }
    }
    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing) { _disposed = true; ClearCrypto(); }
        base.Dispose(disposing);
    }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !_disposed && !_failed && !_completed;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
