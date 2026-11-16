using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisComparisonFormat
{
    private static readonly SemaphoreSlim ExternalLoads = new(2, 2);

    /// <summary>Strict bounded-memory offline import. Native reads and final release stay on admitted I/O workers.</summary>
    public static async Task<AnalysisSavedComparison> ReadExternalAsync(DeviceIoScheduler io, string device,
        Func<AnalysisInventoryInput> openInput, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(openInput);
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        await ExternalLoads.WaitAsync(cancellation).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var pipe = Channel.CreateBounded<ReadOnlyMemory<byte>>(new BoundedChannelOptions(2)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        long reservation = 0, bytes = 0;
        AnalysisInventoryInput? input = null; AnalysisSavedComparison? result = null;
        Task<AnalysisSavedComparison>? decoder = null; Exception? failure = null;
        try
        {
            // Two queued chunks, one current reader chunk and one native read, plus
            // bounded outer-header parsing, stream buffering and channel overhead.
            AnalysisInventoryFormat.Reserve(2 * 1024 * 1024, ref reservation);
            await AnalysisIo.Run(io, device, token =>
            {
                token.ThrowIfCancellationRequested(); input = openInput(); input.Verify();
                if (input.Stream.Length is < 1 or > AnalysisLimits.InventoryBytes) throw Invalid("The comparison exceeds the 8 GiB input limit.");
                return true;
            }, stop.Token).ConfigureAwait(false);
            decoder = Task.Run(() =>
            {
                try { using var stream = new ExternalPipeReader(pipe.Reader, stop.Token); return Read(stream, stop.Token); }
                catch { stop.Cancel(); throw; }
            }, CancellationToken.None);
            while (true)
            {
                var chunk = await AnalysisIo.Run(io, device, token =>
                {
                    token.ThrowIfCancellationRequested(); input!.Verify(); var buffer = new byte[64 * 1024];
                    int read = input.Stream.Read(buffer); input.Verify();
                    if (read > AnalysisLimits.InventoryBytes - bytes) throw Invalid("The comparison exceeds the 8 GiB input limit.");
                    bytes += read; return buffer.AsMemory(0, read);
                }, stop.Token).ConfigureAwait(false);
                if (chunk.IsEmpty) break;
                await pipe.Writer.WriteAsync(chunk, stop.Token).ConfigureAwait(false);
            }
            pipe.Writer.TryComplete(); result = await decoder.ConfigureAwait(false);
            await AnalysisIo.Run(io, device, token => { token.ThrowIfCancellationRequested(); input!.Verify(); return true; }, stop.Token).ConfigureAwait(false);
        }
        catch (Exception error) { failure = error; }
        finally
        {
            stop.Cancel(); pipe.Writer.TryComplete(failure);
            if (decoder is not null)
            {
                try { result ??= await decoder.ConfigureAwait(false); }
                catch (Exception error)
                {
                    // A parser refusal can cancel the producer while retaining the useful original error.
                    if (failure is null or OperationCanceledException) failure = error;
                    else if (error is not OperationCanceledException && !ReferenceEquals(error, failure)) failure = new AggregateException(failure, error);
                }
            }
            try { if (input is not null) await AnalysisIo.ReleaseScratchAsync(io, device, input).ConfigureAwait(false); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            finally { AnalysisInventoryFormat.Release(reservation); ExternalLoads.Release(); }
        }
        if (failure is not null)
        {
            result?.Dispose(); ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return result ?? throw Invalid("The comparison did not finish validation.");
    }

    private sealed class ExternalPipeReader(ChannelReader<ReadOnlyMemory<byte>> reader, CancellationToken cancellation) : Stream
    {
        private ReadOnlyMemory<byte> _current;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            cancellation.ThrowIfCancellationRequested(); if (buffer.IsEmpty) return 0;
            while (_current.IsEmpty)
            {
                if (!reader.WaitToReadAsync(cancellation).AsTask().GetAwaiter().GetResult()) return 0;
                if (reader.TryRead(out var chunk)) _current = chunk;
            }
            int take = Math.Min(buffer.Length, _current.Length); _current.Span[..take].CopyTo(buffer); _current = _current[take..]; return take;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
