using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

/// <summary>
/// Bounded, cancellable protocol IO inside a disposable worker. Parser waits are interruptible even if an OS pipe
/// read/write is blocked. Background IO threads do not keep the worker alive. A successful write/flush waits for
/// the actual OS operation, so normal completion cannot outrun a receipt still buffered here.
/// </summary>
internal sealed class ArchiveWorkerTransport : IDisposable
{
    internal const int MaximumChunkBytes = 65_524;
    private sealed record InputChunk(byte[] Bytes, int Count);
    private readonly BlockingCollection<InputChunk> incoming = new(2);
    private readonly BlockingCollection<OutputRequest> outgoing = new(1);
    private readonly CancellationTokenSource stop;
    private readonly CancellationToken token;
    private ExceptionDispatchInfo? inputFailure, outputFailure;
    private readonly Stream rawInput, rawOutput;
    private int disposed;
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
                var bytes = new byte[MaximumChunkBytes]; bool queued = false;
                try
                {
                    int count = rawInput.Read(bytes, 0, bytes.Length);
                    if ((uint)count > bytes.Length) throw new IOException("Archive input returned an invalid byte count.");
                    if (count == 0) return;
                    // Never resize a short read: that would abandon an uncleared plaintext copy.
                    incoming.Add(new(bytes, count), token); queued = true;
                }
                finally { if (!queued) CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        { Volatile.Write(ref inputFailure, ExceptionDispatchInfo.Capture(ex)); }
        finally { incoming.CompleteAdding(); if (token.IsCancellationRequested) ClearInputQueue(); }
    }

    private void Write()
    {
        try
        {
            foreach (var request in outgoing.GetConsumingEnumerable(token))
            {
                Exception? failure = null; bool cancelled = false;
                try
                {
                    token.ThrowIfCancellationRequested();
                    if (request.Flush) rawOutput.Flush(); else rawOutput.Write(request.Bytes, 0, request.Bytes.Length);
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    Volatile.Write(ref outputFailure, ExceptionDispatchInfo.Capture(ex));
                    failure = ex;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                { cancelled = true; }
                finally { CryptographicOperations.ZeroMemory(request.Bytes); }
                // Completion means the OS operation returned and our transport-owned copy was cleared.
                if (failure is not null) { request.Done.TrySetException(failure); return; }
                if (cancelled) { request.Done.TrySetCanceled(token); return; }
                request.Done.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { outgoing.CompleteAdding(); ClearOutputQueue(); }
    }

    private void Send(byte[] bytes, bool flush)
    {
        var request = new OutputRequest(bytes, flush, new(TaskCreationOptions.RunContinuationsAsynchronously));
        bool queued = false;
        try
        {
            token.ThrowIfCancellationRequested(); Volatile.Read(ref outputFailure)?.Throw();
            try { outgoing.Add(request, token); queued = true; }
            catch (InvalidOperationException) { Volatile.Read(ref outputFailure)?.Throw(); throw new IOException("Archive output stopped."); }
            request.Done.Task.WaitAsync(token).GetAwaiter().GetResult();
        }
        finally
        {
            // Once queued, only the writer/queue owner clears it. The OS may still be using an in-flight buffer
            // after the parser's cancellation wait returns; wiping it concurrently would corrupt pending output.
            if (!queued) CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        ((Reader)Input).Clear(); ClearInputQueue(); ClearOutputQueue();
        // The entry point owns the raw streams. Do not wait for blocked OS IO or dispose collections/sources
        // still used by their background threads; worker exit releases them after the parser returns.
    }

    private void ClearInputQueue()
    { while (incoming.TryTake(out var chunk)) CryptographicOperations.ZeroMemory(chunk.Bytes); }
    private void ClearOutputQueue()
    {
        while (outgoing.TryTake(out var request))
        {
            CryptographicOperations.ZeroMemory(request.Bytes);
            if (token.IsCancellationRequested) request.Done.TrySetCanceled(token);
            else request.Done.TrySetException(new IOException("Archive output stopped."));
        }
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
        private InputChunk? current; private int offset;
        private readonly object gate = new();
        public override bool CanRead => true; public override bool CanWrite => false;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            lock (gate)
            {
                owner.token.ThrowIfCancellationRequested(); if (buffer.IsEmpty) return 0;
                if (current is null)
                {
                    if (!owner.incoming.TryTake(out current, Timeout.Infinite, owner.token))
                    { Volatile.Read(ref owner.inputFailure)?.Throw(); return 0; }
                    offset = 0;
                }
                int count = Math.Min(buffer.Length, current.Count - offset);
                var consumed = current.Bytes.AsSpan(offset, count); consumed.CopyTo(buffer);
                CryptographicOperations.ZeroMemory(consumed); offset += count;
                if (offset == current.Count) { CryptographicOperations.ZeroMemory(current.Bytes); current = null; offset = 0; }
                return count;
            }
        }
        internal void Clear()
        { lock (gate) { if (current is { } chunk) CryptographicOperations.ZeroMemory(chunk.Bytes); current = null; offset = 0; } }
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
