using System.Diagnostics;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    /// <summary>Encode a retained graph without holding device admission while its consumer waits.</summary>
    internal static async Task<long> WriteDatasetStreamAsync(AnalysisIndexedGraphReader reader, Guid inventoryId,
        Stream destination, DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        if (inventoryId == Guid.Empty || !destination.CanWrite) throw new ArgumentException("A generated inventory identity and writable stream are required.");
        await DatasetSaves.WaitAsync(cancellation).ConfigureAwait(false);
        long reservation = 0, total = 0;
        try
        {
            Reserve(4L * AnalysisLimits.RecordBytes, ref reservation);
            using var writer = new DatasetWriter(reader, inventoryId);
            while (!writer.Done)
            {
                byte[] chunk = await AnalysisIo.Run(io, devices, token =>
                {
                    using var buffer = new MemoryStream(AnalysisLimits.BatchBytes);
                    int records = 0; var clock = Stopwatch.StartNew();
                    while (!writer.Done && buffer.Length < AnalysisLimits.BatchBytes && records < AnalysisLimits.BatchRecords)
                    {
                        token.ThrowIfCancellationRequested();
                        if (writer.HasPending) writer.Drain(buffer, AnalysisLimits.BatchBytes - checked((int)buffer.Length), token);
                        else { writer.Prepare(token); records++; }
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    return buffer.ToArray();
                }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                // The bounded export pipe may wait for its destination's I/O
                // turn. Waiting inside the preceding admitted read would deadlock
                // when source and destination share a device.
                await destination.WriteAsync(chunk, cancellation).ConfigureAwait(false);
                total = checked(total + chunk.Length);
            }
            return total;
        }
        finally { Release(reservation); DatasetSaves.Release(); }
    }
}
