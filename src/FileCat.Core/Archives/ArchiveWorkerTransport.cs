using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace FileCat.Core.Archives;

/// <summary>
/// Bounded, cancellable protocol IO inside a disposable worker. Parser waits are interruptible even if an OS pipe
/// read/write is blocked. Background IO threads do not keep the worker alive. A successful write/flush waits for
/// the actual OS operation, so normal completion cannot outrun a receipt still buffered here.
/// </summary>
internal sealed class ArchiveWorkerTransport : IDisposable
{
    internal const int MaximumChunkBytes = 65_524;
    private readonly BlockingCollection<byte[]> incoming = new(2);
    private readonly BlockingCollection<OutputRequest> outgoing = new(1);
    private readonly CancellationTokenSource stop;
    private readonly CancellationToken token;
    private ExceptionDispatchInfo? inputFailure, outputFailure;
    private readonly Stream rawInput, rawOutput;
    internal Stream Input { get; }
    internal Stream Output { get; }
    private sealed record OutputRequest(byte[] Bytes, bool Flush, TaskCompletionSource Done);

    internal ArchiveWorkerTransport(Stream input, Stream output, CancellationToken cancellation)
    {
        rawInput = input; rawOutput = output;
        stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation); token = stop.Token;
        Input = new Reader(this); Output = new Writer(this);
        new Thread(Read) { IsBackground = true, Name = "FileCat archive protocol input" }.Start();
        new Thread(Write) { IsBackground = true, Name = "FileCat archive protocol output" }.Start();
    }

    private void Read()
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var bytes = new byte[MaximumChunkBytes]; int count = rawInput.Read(bytes);
                if (count == 0) return;
                if (count != bytes.Length) Array.Resize(ref bytes, count);
                incoming.Add(bytes, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        { Volatile.Write(ref inputFailure, ExceptionDispatchInfo.Capture(ex)); }
        finally { incoming.CompleteAdding(); }
    }

    private void Write()
    {
        try
        {
            foreach (var request in outgoing.GetConsumingEnumerable(token))
            {
                try
                {
                    if (request.Flush) rawOutput.Flush(); else rawOutput.Write(request.Bytes);
                    request.Done.TrySetResult();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    Volatile.Write(ref outputFailure, ExceptionDispatchInfo.Capture(ex));
                    request.Done.TrySetException(ex); return;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { outgoing.CompleteAdding(); }
    }

    private void Send(byte[] bytes, bool flush)
    {
        token.ThrowIfCancellationRequested(); Volatile.Read(ref outputFailure)?.Throw();
        var request = new OutputRequest(bytes, flush, new(TaskCreationOptions.RunContinuationsAsynchronously));
        try { outgoing.Add(request, token); }
        catch (InvalidOperationException) { Volatile.Read(ref outputFailure)?.Throw(); throw new IOException("Archive output stopped."); }
        request.Done.Task.WaitAsync(token).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        stop.Cancel();
        // The entry point owns the raw streams. Do not wait for blocked OS IO or dispose collections/sources
        // still used by their background threads; worker exit releases them after the parser returns.
    }

    private abstract class PipeStream : Stream
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class Reader(ArchiveWorkerTransport owner) : PipeStream
    {
        private byte[]? current; private int offset;
        public override bool CanRead => true; public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            owner.token.ThrowIfCancellationRequested(); if (buffer.IsEmpty) return 0;
            if (current is null || offset == current.Length)
            {
                if (!owner.incoming.TryTake(out current, Timeout.Infinite, owner.token))
                { Volatile.Read(ref owner.inputFailure)?.Throw(); return 0; }
                offset = 0;
            }
            int count = Math.Min(buffer.Length, current.Length - offset);
            current.AsSpan(offset, count).CopyTo(buffer); offset += count; return count;
        }
        public override void Flush() { }
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class Writer(ArchiveWorkerTransport owner) : PipeStream
    {
        public override bool CanRead => false; public override bool CanWrite => true;
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            owner.token.ThrowIfCancellationRequested();
            while (!buffer.IsEmpty)
            {
                int count = Math.Min(buffer.Length, MaximumChunkBytes);
                owner.Send(buffer[..count].ToArray(), flush: false); buffer = buffer[count..];
            }
        }
        public override void Flush() => owner.Send([], flush: true);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
