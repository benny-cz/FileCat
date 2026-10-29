using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SharpCompress.Common;

internal sealed class ChecksumValidationStream : Stream
{
    private readonly Stream _stream;
    private readonly ChecksumDescriptor _checksum;
    private readonly string _entryName;
    private readonly uint[] _crc32Table;
    private uint _seed = 0xFFFF_FFFF;
    private ushort _crc16;
    private bool _validated;

    internal ChecksumValidationStream(Stream stream, ChecksumDescriptor checksum, string? entryName)
    {
        _stream = stream;
        _checksum = checksum;
        _entryName = string.IsNullOrEmpty(entryName) ? "Entry" : entryName!;
        _crc32Table = Crc32Table();
    }

    public override bool CanRead => _stream.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _stream.Length;

    public override long Position
    {
        get => _stream.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() => _stream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        _stream.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = _stream.Read(buffer, offset, count);
        UpdateAndValidateAtEof(buffer.AsSpan(offset, read), read);
        return read;
    }

#if !LEGACY_DOTNET
    public override int Read(Span<byte> buffer)
    {
        var read = _stream.Read(buffer);
        UpdateAndValidateAtEof(buffer[..read], read);
        return read;
    }
#endif

    public override int ReadByte() => throw new NotSupportedException();

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken
    )
    {
        var read = await _stream
            .ReadAsync(buffer, offset, count, cancellationToken)
            .ConfigureAwait(false);
        UpdateAndValidateAtEof(buffer.AsSpan(offset, read), read);
        return read;
    }

#if !LEGACY_DOTNET
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default
    )
    {
        var read = await _stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        UpdateAndValidateAtEof(buffer.Span[..read], read);
        return read;
    }
#endif

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    private void UpdateAndValidateAtEof(ReadOnlySpan<byte> buffer, int read)
    {
        if (read > 0)
        {
            UpdateChecksum(buffer);
            return;
        }

        Validate();
    }

    private void UpdateChecksum(ReadOnlySpan<byte> buffer)
    {
        switch (_checksum.Kind)
        {
            case ChecksumKind.Crc32:
            case ChecksumKind.Crc32NoFinalXor:
                _seed = Crc32Update(_crc32Table, _seed, buffer);
                break;
            case ChecksumKind.Crc16Arc:
                _crc16 = CalculateCrc16Arc(_crc16, buffer);
                break;
        }
    }

    private void Validate()
    {
        if (_validated)
        {
            return;
        }

        _validated = true;

        switch (_checksum.Kind)
        {
            case ChecksumKind.Crc32:
                ValidateCrc32(finalXor: true);
                break;
            case ChecksumKind.Crc32NoFinalXor:
                ValidateCrc32(finalXor: false);
                break;
            case ChecksumKind.Crc16Arc:
                ValidateCrc16Arc();
                break;
        }
    }

    private void ValidateCrc32(bool finalXor)
    {
        var actual = finalXor ? ~_seed : _seed;
        var expected = unchecked((uint)_checksum.ExpectedValue);
        if (actual != expected)
        {
            throw new InvalidFormatException(
                $"CRC mismatch for entry '{_entryName}'. Expected 0x{expected:X8}, actual 0x{actual:X8}."
            );
        }
    }

    private void ValidateCrc16Arc()
    {
        var expected = unchecked((ushort)_checksum.ExpectedValue);
        if (_crc16 != expected)
        {
            throw new InvalidFormatException(
                $"CRC mismatch for entry '{_entryName}'. Expected 0x{expected:X4}, actual 0x{_crc16:X4}."
            );
        }
    }

    // FileCat: the CRC-32 table and update are written here since FileCat's build drops Crypto/Crc32Stream.cs
    // (THIRD-PARTY-CHANGES.md); reflected polynomial 0xEDB88320, the same values that class gave.
    private static uint[] Crc32Table()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32Update(uint[] table, uint crc, ReadOnlySpan<byte> buffer)
    {
        foreach (var value in buffer)
        {
            crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static ushort CalculateCrc16Arc(ushort crc, ReadOnlySpan<byte> buffer)
    {
        foreach (var value in buffer)
        {
            crc ^= value;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
        }

        return crc;
    }
}
