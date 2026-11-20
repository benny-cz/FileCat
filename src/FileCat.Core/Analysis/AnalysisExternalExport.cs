using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    /// <summary>Streams the entire captured inventory, independently of display filters, to an explicitly reviewed create-new destination.</summary>
    public static Task<AnalysisExportReceipt> ExportSnapshotNewAsync(AnalysisSnapshot snapshot, Guid inventoryId,
        DeviceIoScheduler io, AnalysisIoDevices devices, string name, Func<AnalysisExportDestination> createDestination,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (inventoryId == Guid.Empty) throw new ArgumentException("A generated inventory identity is required.", nameof(inventoryId));
        AnalysisPrivateInventoryStore.ValidateName(name);
        return ExportNewAsync(io, devices, name, createDestination,
            (sink, token) => WriteSnapshotPipeAsync(snapshot, inventoryId, sink, io, devices, token), cancellation);
    }

    internal static Task<AnalysisExportReceipt> ExportDatasetNewAsync(AnalysisIndexedGraphReader reader, Guid inventoryId,
        DeviceIoScheduler io, AnalysisIoDevices devices, string name, Func<AnalysisExportDestination> createDestination, CancellationToken cancellation) =>
        ExportNewAsync(io, devices, name, createDestination, (sink, token) => WriteDatasetPreparedAsync(reader, inventoryId, sink, io, devices, token), cancellation);

    internal static async Task<AnalysisExportReceipt> ExportNewAsync(DeviceIoScheduler io, AnalysisIoDevices devices, string name,
        Func<AnalysisExportDestination> createDestination, Func<IAnalysisInventoryWriteSink, CancellationToken, Task> write, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(devices); ArgumentNullException.ThrowIfNull(createDestination);
        AnalysisExportDestination.ValidateName(name);
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        await DatasetSaves.WaitAsync(cancellation).ConfigureAwait(false);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        object gate = new(); bool finished = false, published = false; ExternalSink? sink = null;
        void HealthChanged(string key, DeviceHealth health)
        { if (health == DeviceHealth.NotResponding && devices.Contains(key)) lock (gate) { if (!finished) stop.Cancel(); } }
        io.HealthChanged += HealthChanged;
        try
        {
            try
            {
                sink = await AnalysisIo.Run(io, devices, token => { token.ThrowIfCancellationRequested(); return new ExternalSink(createDestination(), token); }, stop.Token).ConfigureAwait(false);
                await write(sink, stop.Token).ConfigureAwait(false);
                return await AnalysisIo.Run(io, devices, token =>
                {
                    token.ThrowIfCancellationRequested(); sink.Prepare(); token.ThrowIfCancellationRequested();
                    try { sink.Destination.PublishNew(name); published = true; }
                    catch (AnalysisExportUncertainException) { published = true; throw; }
                    return new AnalysisExportReceipt(name, sink.Bytes);
                }, stop.Token).ConfigureAwait(false);
            }
            finally { if (sink is not null) await AnalysisIo.ReleaseScratchAsync(io, devices, sink).ConfigureAwait(false); }
        }
        catch (Exception error) when (published && error is not AnalysisExportUncertainException)
        { throw new AnalysisExportUncertainException(name, error); }
        finally { io.HealthChanged -= HealthChanged; lock (gate) finished = true; DatasetSaves.Release(); }
    }

    private sealed class ExternalSink : IAnalysisInventoryWriteSink, IAnalysisScratchOwned
    {
        internal AnalysisExportDestination Destination { get; }
        private readonly ExportWriteStream _stream;
        internal long Bytes => _stream.Written;
        public Stream Stream => _stream;
        internal ExternalSink(AnalysisExportDestination destination, CancellationToken cancellation)
        {
            Destination = destination ?? throw new InvalidOperationException("Native output ownership is required.");
            try { destination.Verify(); _stream = new(destination, cancellation); }
            catch { AnalysisIo.CloseOnWorker(destination); throw; }
        }
        public void FlushBatch() { _stream.Flush(); Destination.Verify(); }
        internal void Prepare() { FlushBatch(); Destination.FlushDurable(); Destination.Verify(); }
        public void Dispose() => Destination.Dispose();
    }

    private sealed class ExportWriteStream(AnalysisExportDestination destination, CancellationToken cancellation) : Stream
    {
        internal long Written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Written;
        public override long Position { get => Written; set => throw new NotSupportedException(); }
        public override void Flush() { cancellation.ThrowIfCancellationRequested(); destination.Verify(); destination.Stream.Flush(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length > AnalysisLimits.InventoryBytes - Written) throw new IOException("The complete output exceeds the 8 GiB limit.");
            while (!buffer.IsEmpty)
            {
                cancellation.ThrowIfCancellationRequested(); destination.Verify(); int count = Math.Min(buffer.Length, 64 * 1024);
                var space = destination.Space();
                if (space.Capacity == 0 || space.Available > space.Capacity || space.Available < AnalysisPrivateInventoryStore.RequiredReserve(space.Capacity) + (uint)count)
                    throw new IOException("Not enough output space remains above the analysis reserve.");
                destination.Stream.Write(buffer[..count]); Written += count; buffer = buffer[count..]; destination.Verify();
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

public sealed partial class AnalysisDatasetLease
{
    public async Task<AnalysisExportReceipt> ExportInventoryNewAsync(string name, Guid inventoryId, AnalysisIoDevices destinationDevices,
        Func<AnalysisExportDestination> createDestination, CancellationToken cancellation = default)
    {
        AnalysisPrivateInventoryStore.ValidateName(name); ArgumentNullException.ThrowIfNull(createDestination);
        if (inventoryId == Guid.Empty) throw new ArgumentException("A generated inventory identity is required.", nameof(inventoryId));
        ArgumentNullException.ThrowIfNull(destinationDevices);
        var devices = _device; foreach (string key in destinationDevices.Keys) devices = devices.Include(key);
        bool published = false;
        try
        {
            return await _owned.UseAsync(async reader =>
            {
                try
                {
                    var receipt = await AnalysisInventoryFormat.ExportDatasetNewAsync(reader, inventoryId, _io, devices, name, createDestination, cancellation).ConfigureAwait(false);
                    published = true; return receipt;
                }
                catch (AnalysisExportUncertainException) { published = true; throw; }
            }).ConfigureAwait(false);
        }
        catch (Exception error) when (published && error is not AnalysisExportUncertainException)
        { throw new AnalysisExportUncertainException(name, error); }
    }
}
