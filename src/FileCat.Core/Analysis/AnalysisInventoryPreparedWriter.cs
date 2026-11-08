using System.Diagnostics;
using System.Threading.Channels;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    internal static Task<AnalysisPrivateInventoryStore.DatasetWrite> PrepareSnapshotReplacementAsync(AnalysisSnapshot snapshot,
        DeviceIoScheduler io, AnalysisIoDevices devices, Func<AnalysisPrivateDirectory> createDirectory, string name, Guid id, CancellationToken cancellation) =>
        PrepareReplacementAsync(io, devices, createDirectory, name,
            (output, token) => WriteSnapshotPipeAsync(snapshot, id, output, io, devices, token), cancellation);

    internal static Task<AnalysisPrivateInventoryStore.DatasetWrite> PrepareDatasetReplacementAsync(AnalysisIndexedGraphReader reader,
        DeviceIoScheduler io, AnalysisIoDevices devices, Func<AnalysisPrivateDirectory> createDirectory, string name, Guid id, CancellationToken cancellation) =>
        PrepareReplacementAsync(io, devices, createDirectory, name, async (output, token) =>
        {
            using var writer = new DatasetWriter(reader, id);
            while (!writer.Done)
                await AnalysisIo.Run(io, devices, actual =>
                {
                    int bytes = 0, records = 0; var clock = Stopwatch.StartNew();
                    while (!writer.Done && bytes < AnalysisLimits.BatchBytes && records < AnalysisLimits.BatchRecords)
                    {
                        actual.ThrowIfCancellationRequested();
                        if (writer.HasPending) bytes += writer.Drain(output.Stream, AnalysisLimits.BatchBytes - bytes, actual);
                        else { writer.Prepare(actual); records++; }
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    output.FlushBatch(); return true;
                }, token).ConfigureAwait(false);
        }, cancellation);

    private static async Task<AnalysisPrivateInventoryStore.DatasetWrite> PrepareReplacementAsync(DeviceIoScheduler io,
        AnalysisIoDevices devices, Func<AnalysisPrivateDirectory> createDirectory, string name,
        Func<AnalysisPrivateInventoryStore.DatasetWrite, CancellationToken, Task> write, CancellationToken cancellation)
    {
        await DatasetSaves.WaitAsync(cancellation).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        AnalysisPrivateInventoryStore.DatasetWrite? output = null;
        object gate = new(); bool finished = false;
        void HealthChanged(string key, DeviceHealth health)
        {
            if (health != DeviceHealth.NotResponding || !devices.Contains(key)) return;
            lock (gate) { if (!finished) stop.Cancel(); }
        }
        io.HealthChanged += HealthChanged;
        try
        {
            output = await AnalysisIo.Run(io, devices, token =>
            { token.ThrowIfCancellationRequested(); return AnalysisPrivateInventoryStore.BeginReplacementWrite(createDirectory(), name, token); }, stop.Token).ConfigureAwait(false);
            await write(output, stop.Token).ConfigureAwait(false);
            await AnalysisIo.Run(io, devices, token => { token.ThrowIfCancellationRequested(); output.Prepare(); return true; }, stop.Token).ConfigureAwait(false);
            var prepared = output; output = null; return prepared;
        }
        finally
        {
            io.HealthChanged -= HealthChanged; lock (gate) finished = true;
            try { if (output is not null) await AnalysisIo.ReleaseScratchAsync(io, devices, output).ConfigureAwait(false); }
            finally { DatasetSaves.Release(); }
        }
    }

    private static async Task WriteSnapshotPipeAsync(AnalysisSnapshot snapshot, Guid id, AnalysisPrivateInventoryStore.DatasetWrite output,
        DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var pipe = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var producer = Task.Run(() =>
        {
            Exception? failure = null;
            try { using var stream = new PreparedPipeStream(pipe.Writer, stop.Token); Write(stream, snapshot, id, stop.Token); }
            catch (Exception error) { failure = error; throw; }
            finally { pipe.Writer.TryComplete(failure); }
        }, CancellationToken.None);
        Exception? failed = null;
        try
        {
            while (await pipe.Reader.WaitToReadAsync(stop.Token).ConfigureAwait(false))
                while (pipe.Reader.TryRead(out var bytes))
                    await AnalysisIo.Run(io, devices, token =>
                    { token.ThrowIfCancellationRequested(); output.Stream.Write(bytes); output.FlushBatch(); return true; }, stop.Token).ConfigureAwait(false);
        }
        catch (Exception error) { failed = error; }
        finally
        {
            stop.Cancel(); pipe.Writer.TryComplete();
            // The producer may be blocked on the bounded queue. Cancellation releases it;
            // staging cannot close until its actual producer and admitted write have ended.
            try { await producer.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (failed is null) failed = error;
                else if (error is not OperationCanceledException && !ReferenceEquals(error, failed)) failed = new AggregateException(failed, error);
            }
        }
        if (failed is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed).Throw();
    }

    private sealed class PreparedPipeStream(ChannelWriter<byte[]> writer, CancellationToken cancellation) : Stream
    {
        public override bool CanWrite => true;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => cancellation.ThrowIfCancellationRequested();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> bytes)
        {
            while (!bytes.IsEmpty)
            {
                cancellation.ThrowIfCancellationRequested(); int count = Math.Min(64 * 1024, bytes.Length);
                writer.WriteAsync(bytes[..count].ToArray(), cancellation).AsTask().GetAwaiter().GetResult(); bytes = bytes[count..];
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
