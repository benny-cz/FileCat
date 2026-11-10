using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace FileCat.Core.Archives;

/// <summary>
/// Bounded, cancellable protocol IO inside a disposable worker. Parser waits are interruptible even if an OS pipe
/// read/write is blocked. Background IO threads do not keep the worker alive. A successful flush waits for the actual OS
/// operation covering every byte written before it, so normal completion cannot outrun a receipt still buffered here.
/// Writes are coalesced until a flush or a full chunk (each protocol frame is header, payload and flush: one handoff to
/// the output thread instead of three), and input chunks reuse zeroed buffers instead of a fresh 64 KiB array per read
/// (AT18: many tiny members made these per-frame costs dominate a transform's CPU).
/// </summary>
internal sealed class ArchiveWorkerTransport : IDisposable
{
    internal const int MaximumChunkBytes = 65_524;
    private sealed record InputChunk(byte[] Bytes, int Count);
    private readonly BlockingCollection<InputChunk> incoming = new(2);
    // Every buffer here is entirely zero: a consumer clears the bytes it was given before returning a buffer.
    private readonly ConcurrentQueue<byte[]> free = new();
    private readonly BlockingCollection<OutputRequest> outgoing = new(1);
    private readonly CancellationTokenSource stop;
    private readonly CancellationToken token;
    private ExceptionDispatchInfo? inputFailure, outputFailure;
    private readonly Stream rawInput, rawOutput;
    private int disposed;
    internal Stream Input { get; }
    internal Stream Output { get; }
    private sealed record OutputRequest(byte[] Bytes, int Count, bool Flush, TaskCompletionSource Done);

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
                if (!free.TryDequeue(out var bytes)) bytes = new byte[MaximumChunkBytes];
                bool queued = false;
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
                    if (request.Count != 0) rawOutput.Write(request.Bytes, 0, request.Count);
                    if (request.Flush) rawOutput.Flush();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    Volatile.Write(ref outputFailure, ExceptionDispatchInfo.Capture(ex));
                    failure = ex;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                { cancelled = true; }
                // Only the sent bytes were ever written into this buffer: clearing them leaves it entirely zero.
                finally { CryptographicOperations.ZeroMemory(request.Bytes.AsSpan(0, request.Count)); }
                // Completion means the OS operation returned and our transport-owned copy was cleared.
                if (failure is not null) { request.Done.TrySetException(failure); return; }
                if (cancelled) { request.Done.TrySetCanceled(token); return; }
                request.Done.TrySetResult();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { outgoing.CompleteAdding(); ClearOutputQueue(); }
    }

    private void Send(byte[] bytes, int count, bool flush)
    {
        // No continuation runs on the output thread except this wait's own wake-up, so completion need not hop through the
        // thread pool; a direct wait avoids a pool handoff per frame.
        var request = new OutputRequest(bytes, count, flush, new(TaskCreationOptions.None));
        bool queued = false;
        try
        {
            token.ThrowIfCancellationRequested(); Volatile.Read(ref outputFailure)?.Throw();
            try { outgoing.Add(request, token); queued = true; }
            catch (InvalidOperationException) { Volatile.Read(ref outputFailure)?.Throw(); throw new IOException("Archive output stopped."); }
            try { request.Done.Task.Wait(token); }
            catch (AggregateException) { } // Rethrown below as the original IO failure or cancellation.
            request.Done.Task.GetAwaiter().GetResult();
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
        ((Reader)Input).Clear(); ((Writer)Output).Clear(); ClearInputQueue(); ClearOutputQueue();
        // The entry point owns the raw streams. Do not wait for blocked OS IO or dispose collections/sources
        // still used by their background threads; worker exit releases them after the parser returns.
    }

    private void ClearInputQueue()
    {
        while (incoming.TryTake(out var chunk)) CryptographicOperations.ZeroMemory(chunk.Bytes);
        while (free.TryDequeue(out _)) { } // Already zero; only drop them.
    }
    /// <summary>A consumed chunk's bytes are cleared and its buffer reused; at most a few are kept.</summary>
    private void Recycle(InputChunk chunk)
    {
        CryptographicOperations.ZeroMemory(chunk.Bytes.AsSpan(0, chunk.Count));
        if (Volatile.Read(ref disposed) == 0 && free.Count < 4) free.Enqueue(chunk.Bytes);
    }
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
                if (offset == current.Count) { owner.Recycle(current); current = null; offset = 0; }
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
        private readonly object gate = new();
        // Reused across frames and entirely zero between them; after a failed send the output thread may still own it.
        private byte[]? pending; private int length;
        public override bool CanRead => false; public override bool CanWrite => true;
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            lock (gate)
            {
                owner.token.ThrowIfCancellationRequested();
                while (!buffer.IsEmpty)
                {
                    pending ??= new byte[MaximumChunkBytes];
                    int count = Math.Min(buffer.Length, MaximumChunkBytes - length);
                    buffer[..count].CopyTo(pending.AsSpan(length)); length += count; buffer = buffer[count..];
                    if (length == MaximumChunkBytes) Send(flush: false);
                }
            }
        }
        public override void Flush() { lock (gate) Send(flush: true); }
        // The output thread owns and clears a sent buffer; a buffer that was never sent is cleared here.
        private void Send(bool flush)
        {
            byte[] bytes = pending ?? []; int count = length; bool completed = false;
            length = 0;
            try { owner.Send(bytes, count, flush); completed = true; }
            finally { if (!completed) pending = null; }
        }
        internal void Clear()
        { lock (gate) { if (pending is { } bytes) CryptographicOperations.ZeroMemory(bytes); pending = null; length = 0; } }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
