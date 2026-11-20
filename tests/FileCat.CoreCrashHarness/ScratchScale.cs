using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using FileCat.Core.Analysis;

internal static class ScratchScale
{
    internal static AnalysisPrivateDirectory Directory(string parent)
    {
        if (!OperatingSystem.IsWindows()) return new UnixAnalysisPrivateDirectory(parent, "scratch");
        var assembly = System.Reflection.Assembly.LoadFrom(Environment.GetEnvironmentVariable("FILECAT_TEST_SCRATCH_WINDOWS_ADAPTER") ??
            Path.Combine(AppContext.BaseDirectory, "FileCat.Platform.Windows.dll"));
        var type = assembly.GetType("FileCat.Platform.Windows.WindowsAnalysisPrivateDirectory", throwOnError: true)!;
        return (AnalysisPrivateDirectory)Activator.CreateInstance(type, parent, "scratch")!;
    }

    internal static int Run(int count, string parent)
    {
        if (count is not (1_000_000 or 10_000_000)) throw new ArgumentOutOfRangeException(nameof(count));
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        using var process = Process.GetCurrentProcess(); GC.Collect(); process.Refresh();
        long baselinePrivate = process.PrivateMemorySize64, baselineWorking = process.WorkingSet64;
        long peakPrivate = baselinePrivate, peakWorking = baselineWorking;
        void Sample()
        {
            process.Refresh(); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            peakWorking = Math.Max(peakWorking, process.WorkingSet64);
        }
        using var arena = new AnalysisScratchArena(Directory(parent)); using var vector = new AnalysisScratchVector(arena);
        byte[] ordinary = new byte[64], longName = new byte[65536];
        AnalysisScratchVectorCheckpoint? earlier = null;
        try
        {
            var timer = Stopwatch.StartNew();
            for (ulong id = 1; id <= (ulong)count; id++)
            {
                byte[] record = id % 1024 == 0 ? longName : ordinary;
                record.AsSpan(24).Fill((byte)(id % 251));
                BinaryPrimitives.WriteUInt64LittleEndian(record, id); BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(8), id / 2);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(16), id % 16 == 0 ? 1 : 0);
                BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(20), record.Length - 24);
                if (vector.Append(record) != id) throw new InvalidDataException("The scratch vector lost dense identity.");
                if ((id & 16383) == 0) Sample();
                if (id == (ulong)count / 2) { vector.Flush(); earlier = vector.CaptureCommitted(); }
            }
            vector.Flush(); using var current = vector.CaptureCommitted(); Sample();
            double writeSeconds = timer.Elapsed.TotalSeconds; long scratchBytes = arena.ReservedBytes;
            vector.Dispose();
            if (current.Count != (ulong)count || earlier!.Count != (ulong)count / 2) throw new InvalidDataException("The checkpoint count changed.");
            try { earlier.Read(earlier.Count + 1); throw new InvalidDataException("An old checkpoint admitted a later record."); }
            catch (ArgumentOutOfRangeException) { }
            timer.Restart(); ulong checksum = 0;
            for (ulong id = 1; id <= current.Count; id++)
            {
                byte[] record = current.Read(id);
                int length = id % 1024 == 0 ? longName.Length : ordinary.Length;
                if (record.Length != length || BinaryPrimitives.ReadUInt64LittleEndian(record) != id ||
                    BinaryPrimitives.ReadUInt64LittleEndian(record.AsSpan(8)) != id / 2 ||
                    BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(16)) != (id % 16 == 0 ? 1 : 0) ||
                    BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(20)) != record.Length - 24 ||
                    record.AsSpan(24).IndexOfAnyExcept((byte)(id % 251)) >= 0)
                    throw new InvalidDataException("A scratch vector observation changed.");
                checksum += id; if ((id & 16383) == 0) Sample();
            }
            Sample(); double readSeconds = timer.Elapsed.TotalSeconds;
            if (checksum != (ulong)count * ((ulong)count + 1) / 2) throw new InvalidDataException("The dense record checksum changed.");
            // Neither a retained checkpoint nor cleanup can take a user-created neighbor with it.
            string path = Path.Combine(parent, "scratch"), foreign = Path.Combine(path, "user-file.txt"); File.WriteAllText(foreign, "preserve me");
            earlier.Dispose(); current.Dispose(); arena.Dispose();
            bool cleanup = System.IO.Directory.GetFileSystemEntries(path).SequenceEqual([foreign]) && File.ReadAllText(foreign) == "preserve me";
            long remainingQuota = AnalysisScratchBudget.Shared.ReservedBytes;
            bool privateMemoryAvailable = baselinePrivate > 0;
            bool memory = peakPrivate - baselinePrivate <= 512L * 1024 * 1024;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                purpose = "Native indexed scratch storage only; not full analysis scale or capability qualification",
                records = count, ordinaryRecordBytes = ordinary.Length, longRecordBytes = longName.Length, longRecordEvery = 1024,
                parentDistribution = "binary hierarchy identifiers; no full analysis graph/query/chart/export",
                writeSeconds, readSeconds, scratchBytes, sampledEveryRecords = 16384,
                privateMemoryAvailable, baselinePrivateBytes = privateMemoryAvailable ? (long?)baselinePrivate : null,
                sampledPeakPrivateIncrement = privateMemoryAvailable ? (long?)(peakPrivate - baselinePrivate) : null,
                privateMemoryBudgetVerified = privateMemoryAvailable && memory,
                privateMemoryBasis = privateMemoryAvailable ? ".NET process private bytes; sampled, not an absolute peak" : "Unavailable from this runtime; working-set samples are not private bytes",
                sampledPeakWorkingSetIncrement = peakWorking - baselineWorking,
                verifiedEveryRecord = true, retainedCheckpointKeptItsCount = true, checksum,
                ownedArtifactsRemoved = cleanup, foreignNeighborPreserved = cleanup, remainingQuotaBytes = remainingQuota,
                passed = cleanup && remainingQuota == 0 && (!privateMemoryAvailable || memory)
            }));
            return cleanup && remainingQuota == 0 && (!privateMemoryAvailable || memory) ? 0 : 19;
        }
        finally { earlier?.Dispose(); }
    }
}
