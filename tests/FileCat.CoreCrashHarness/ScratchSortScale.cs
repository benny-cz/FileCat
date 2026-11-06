using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

internal static class ScratchSortScale
{
    internal static async Task<int> RunAsync(int count, string parent)
    {
        if (count is not (1_000_000 or 10_000_000)) throw new ArgumentOutOfRangeException(nameof(count));
        using var io = new DeviceIoScheduler(); string device = "native-sort-scale";
        var workspace = await AnalysisIo.Run(io, device, _ => new AnalysisScratchWorkspace(ScratchScale.Directory(parent)));
        using var stop = new CancellationTokenSource();
        using var process = Process.GetCurrentProcess(); GC.Collect(); process.Refresh();
        long baselinePrivate = process.PrivateMemorySize64, baselineWorking = process.WorkingSet64;
        long peakPrivate = baselinePrivate, peakWorking = baselineWorking, peakScratch = 0;
        var sampling = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                process.Refresh(); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64); peakWorking = Math.Max(peakWorking, process.WorkingSet64);
                peakScratch = Math.Max(peakScratch, workspace.ReservedBytes);
                try { await Task.Delay(50, stop.Token); } catch (OperationCanceledException) { break; }
            }
        });
        byte[] Key(ulong id)
        {
            byte[] key = new byte[id % 1024 == 0 ? AnalysisLimits.RawNameBytes : 24];
            key.AsSpan().Fill((byte)(id % 251)); BinaryPrimitives.WriteUInt64BigEndian(key, id); return key;
        }
        IEnumerable<AnalysisSortEntry> Source()
        { for (ulong id = (ulong)count; id != 0; id--) yield return new(Key(id), id); }
        AnalysisSortedIndex? index = null; AnalysisSortedIndexReader? reader = null;
        try
        {
            using var source = Source().GetEnumerator(); var timer = Stopwatch.StartNew();
            index = await AnalysisExternalSort.BuildAsync(workspace, io, device, source);
            double writeSeconds = timer.Elapsed.TotalSeconds; long scratchBytes = workspace.ReservedBytes;
            reader = await AnalysisIo.Run(io, device, _ => index.Acquire());
            await AnalysisIo.ReleaseScratchAsync(io, device, index); index = null;
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
            timer.Restart(); ulong checksum = 0, next = 1;
            while (next <= (ulong)count)
            {
                await AnalysisIo.Run(io, device, token =>
                {
                    int batch = 0, bytes = 0;
                    while (next <= (ulong)count && batch < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    {
                        token.ThrowIfCancellationRequested(); var entry = reader.Read(next, token); byte[] expected = Key(next);
                        if (entry.Value != next || !entry.Key.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("The external index lost binary key/value order.");
                        checksum += next++; bytes += entry.Key.Length; batch++;
                    }
                    return true;
                });
            }
            await AnalysisIo.Run(io, device, token =>
            {
                foreach (ulong id in new[] { 1UL, (ulong)count / 2, (ulong)count })
                    if (reader.FindRange(Key(id), token) != new AnalysisIndexRange(id, 1)) throw new InvalidDataException("The exact index range changed.");
                if (reader.PrefixRange([], token) != new AnalysisIndexRange(1, (ulong)count)) throw new InvalidDataException("The whole index prefix changed.");
                return true;
            });
            double readSeconds = timer.Elapsed.TotalSeconds;
            if (checksum != (ulong)count * ((ulong)count + 1) / 2) throw new InvalidDataException("The external index checksum changed.");
            await AnalysisIo.ReleaseScratchAsync(io, device, reader); reader = null;
            stop.Cancel(); await sampling;
            bool cleanup = System.IO.Directory.GetFileSystemEntries(Path.Combine(parent, "scratch")).Length == 0 && workspace.ReservedBytes == 0;
            bool privateAvailable = baselinePrivate > 0, memory = peakPrivate - baselinePrivate <= 512L * 1024 * 1024;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                purpose = "Native exact external index storage only; not source traversal or full analysis/aggregation/query/chart/export qualification",
                records = count, inputOrder = "reverse binary key order", ordinaryKeyBytes = 24, longKeyBytes = AnalysisLimits.RawNameBytes, longKeyEvery = 1024,
                writeSeconds, readSeconds, scratchBytes, sampledPeakSessionScratchBytes = peakScratch, sampledIntervalMilliseconds = 50,
                privateMemoryAvailable = privateAvailable, baselinePrivateBytes = privateAvailable ? (long?)baselinePrivate : null,
                sampledPeakPrivateIncrement = privateAvailable ? (long?)(peakPrivate - baselinePrivate) : null,
                privateMemoryBudgetVerified = privateAvailable && memory,
                privateMemoryBasis = privateAvailable ? ".NET process private bytes; sampled, not an absolute peak" : "Unavailable from this runtime; working-set samples are not private bytes",
                sampledPeakWorkingSetIncrement = peakWorking - baselineWorking,
                verifiedEveryRecord = true, exactRangesVerified = true, retainedReaderSurvivedOwnerClosure = true, checksum,
                ownedArtifactsRemoved = cleanup, remainingQuotaBytes = workspace.ReservedBytes,
                passed = cleanup && (!privateAvailable || memory)
            }));
            return cleanup && (!privateAvailable || memory) ? 0 : 21;
        }
        finally
        {
            stop.Cancel(); await sampling;
            if (reader is not null) await AnalysisIo.ReleaseScratchAsync(io, device, reader);
            if (index is not null) await AnalysisIo.ReleaseScratchAsync(io, device, index);
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
        }
    }
}
