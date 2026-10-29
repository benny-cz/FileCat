#nullable disable

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Compressors.LZMA.RangeCoder;

namespace SharpCompress.Compressors.PPMd;

/// <summary>
/// FileCat: 7z's PPMd (variant H with 7-Zip's range coder) for decompression only, through FileCat's shared PPMd variant H
/// model (<see cref="PpmdModelH7z"/>). Upstream's stream also compressed and decoded ZIP's PPMd variant I and variant H
/// with RAR's coder; FileCat's build keeps the members and the 7z decoding path and refuses the rest.
/// </summary>
public class PpmdStream : Stream, IAsyncDisposable
{
    private readonly Stream _stream;
    private PpmdModelH7z _modelH;
    private Decoder _decoder;
    private long _position;
    private bool _isDisposed;

    private PpmdStream(Stream stream, PpmdModelH7z model)
    {
        _stream = stream;
        _modelH = model;
    }

    private static PpmdModelH7z NewModel(PpmdProperties properties, bool compress)
    {
        ThrowHelper.ThrowIfNull(properties);
        if (compress)
        {
            throw new NotSupportedException("PPMd compression is not part of FileCat's build of SharpCompress.");
        }
        var model = new PpmdModelH7z();
        model.DecodeInit(properties.ModelOrder, properties.AllocatorSize);
        return model;
    }

    public static PpmdStream Create(PpmdProperties properties, Stream stream, bool compress)
    {
        ThrowHelper.ThrowIfNull(stream);
        var instance = new PpmdStream(stream, NewModel(properties, compress));
        instance._decoder = new Decoder();
        instance._decoder.Init(stream);
        return instance;
    }

    public static async ValueTask<PpmdStream> CreateAsync(
        PpmdProperties properties,
        Stream stream,
        bool compress,
        CancellationToken cancellationToken = default
    )
    {
        ThrowHelper.ThrowIfNull(stream);
        var instance = new PpmdStream(stream, NewModel(properties, compress));
        try
        {
            instance._decoder = new Decoder();
            await instance._decoder.InitAsync(stream, cancellationToken).ConfigureAwait(false);
            return instance;
        }
        catch
        {
            await instance.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override void Flush() { }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        if (disposing)
        {
            _modelH?.Dispose();
            _modelH = null;
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }
        _isDisposed = true;
        _modelH?.Dispose();
        _modelH = null;
        await base.DisposeAsync().ConfigureAwait(false);
    }

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        var size = 0;
        int c;
        while (size < buffer.Length && (c = _modelH.DecodeChar(_decoder)) >= 0)
        {
            buffer[size++] = (byte)c;
        }
        _position += size;
        return size;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Read(buffer.AsSpan(offset, count)));
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(Read(buffer.Span));
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
