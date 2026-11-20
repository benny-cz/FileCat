using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FileCat.Core.Analysis;

internal static class ScratchPathScale
{
    internal static int Run(int count, string parent)
    {
        if (count is not (1_000_000 or 10_000_000)) throw new ArgumentOutOfRangeException(nameof(count));
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        using var process = Process.GetCurrentProcess(); GC.Collect(); process.Refresh();
        long baselinePrivate = process.PrivateMemorySize64, baselineWorking = process.WorkingSet64;
        long peakPrivate = baselinePrivate, peakWorking = baselineWorking;
        void Sample()
        { process.Refresh(); peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64); peakWorking = Math.Max(peakWorking, process.WorkingSet64); }
        bool windows = OperatingSystem.IsWindows(); string encoding = windows ? "utf16le" : "utf8";
        var start = DateTimeOffset.UtcNow;
        var identity = new SessionIdentity(Guid.NewGuid(), 1, 0, "inert-path-fixture", start, start) { TimeZoneId = "UTC" };
        string inert = Path.Combine(parent, "inert-observed-source");
        ImmutableArray<RootBinding> roots = [new("r", "file", inert, inert, null, IdentityAssurance.Unknown, "source")
            { NameRules = windows ? AnalysisNameRules.Windows : AnalysisNameRules.Unix }];
        using var arena = new AnalysisScratchArena(ScratchScale.Directory(parent));
        using var paths = new AnalysisIndexedPaths(arena, identity, roots, AnalysisOrigin.ImportedInventory);
        AnalysisNode Node(ulong id)
        {
            string suffix = id.ToString(CultureInfo.InvariantCulture);
            int length = id % 1024 == 0 ? AnalysisLimits.RawNameBytes / (windows ? 2 : 1) : 24;
            string name = id == 1 ? "source" : new string('x', length - suffix.Length - 1) + "-" + suffix;
            bool directory = id <= (ulong)count / 2;
            bool? flag = (id % 3) switch { 0 => null, 1 => false, _ => true };
            return new(id, "r", id / 2, name, directory ? AnalysisKind.Directory : AnalysisKind.File, directory ? null : suffix,
                flag, flag.HasValue ? !flag.Value : null, flag)
            {
                NameEncoding = encoding, ObservedIdentity = "observed-" + suffix,
                Times = new(start.AddTicks((long)id), id % 2 == 0 ? start.AddDays(-2) : null, 100,
                    id % 5 == 0 ? start.AddYears(-1) : null, id % 5 == 0 ? 10UL : null,
                    id % 7 == 0 ? start.AddDays(-1) : null, id % 7 == 0 ? 1000UL : null)
            };
        }
        AnalysisIndexedPathRevision? earlier = null;
        try
        {
            var timer = Stopwatch.StartNew();
            for (ulong id = 1; id <= (ulong)count; id++)
            {
                paths.Append(Node(id)); if ((id & 16383) == 0) Sample();
                if (id == (ulong)count / 2)
                {
                    paths.Flush(); earlier = paths.CaptureCommitted(identity with { Revision = 1, ObservedUtc = start.AddTicks((long)id) });
                }
            }
            paths.Flush(); using var current = paths.CaptureCommitted(identity with { Revision = 2, ObservedUtc = start.AddTicks(count) }); Sample();
            double writeSeconds = timer.Elapsed.TotalSeconds; long scratchBytes = arena.ReservedBytes; paths.Dispose();
            if (current.Count != (ulong)count || earlier!.Count != (ulong)count / 2 || current.Origin != AnalysisOrigin.ImportedInventory ||
                !current.Taint.HasFlag(AnalysisTaint.ImportedUnverified)) throw new InvalidDataException("The typed path revision lost its count or inert classification.");
            try { earlier.Read(earlier.Count + 1); throw new InvalidDataException("An old path revision admitted a later record."); }
            catch (ArgumentOutOfRangeException) { }
            timer.Restart(); ulong checksum = 0;
            for (ulong id = 1; id <= current.Count; id++)
            {
                var actual = current.Read(id); var expected = Node(id);
                if (actual != expected) throw new InvalidDataException("A typed path observation changed.");
                checksum += id; if ((id & 16383) == 0) Sample();
            }
            var expectedLineage = new List<ulong>();
            for (ulong id = (ulong)count; id != 0; id /= 2) expectedLineage.Add(id);
            expectedLineage.Reverse();
            if (!current.ReadLineage((ulong)count).Select(n => n.Id).SequenceEqual(expectedLineage))
                throw new InvalidDataException("The typed path lineage changed.");
            Sample(); double readSeconds = timer.Elapsed.TotalSeconds;
            if (checksum != (ulong)count * ((ulong)count + 1) / 2) throw new InvalidDataException("The typed path checksum changed.");
            string scratch = Path.Combine(parent, "scratch"), foreign = Path.Combine(scratch, "user-file.txt"); File.WriteAllText(foreign, "preserve me");
            earlier.Dispose(); current.Dispose(); arena.Dispose();
            bool cleanup = System.IO.Directory.GetFileSystemEntries(scratch).SequenceEqual([foreign]) && File.ReadAllText(foreign) == "preserve me";
            long remainingQuota = AnalysisScratchBudget.Shared.ReservedBytes;
            bool privateAvailable = baselinePrivate > 0, memory = peakPrivate - baselinePrivate <= 512L * 1024 * 1024;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                purpose = "Native typed path/lineage storage only; not source traversal, object/fact/issue indexes, aggregation/query/chart/export or full analysis qualification",
                records = count, nameEncoding = encoding, ordinaryNameCharacters = 24, longNameRawBytes = AnalysisLimits.RawNameBytes, longNameEvery = 1024,
                parentDistribution = "binary hierarchy; first half directories, second half files; synthetic observed identities/times and nullable metadata flags",
                writeSeconds, readSeconds, scratchBytes, sampledEveryRecords = 16384,
                privateMemoryAvailable = privateAvailable, baselinePrivateBytes = privateAvailable ? (long?)baselinePrivate : null,
                sampledPeakPrivateIncrement = privateAvailable ? (long?)(peakPrivate - baselinePrivate) : null,
                privateMemoryBudgetVerified = privateAvailable && memory,
                privateMemoryBasis = privateAvailable ? ".NET process private bytes; sampled, not an absolute peak" : "Unavailable from this runtime; working-set samples are not private bytes",
                sampledPeakWorkingSetIncrement = peakWorking - baselineWorking,
                verifiedEveryRecord = true, retainedCheckpointKeptItsCount = true, inertClassificationPreserved = true, lineageVerified = true, checksum,
                ownedArtifactsRemoved = cleanup, foreignNeighborPreserved = cleanup, remainingQuotaBytes = remainingQuota,
                passed = cleanup && remainingQuota == 0 && (!privateAvailable || memory)
            }));
            return cleanup && remainingQuota == 0 && (!privateAvailable || memory) ? 0 : 20;
        }
        finally { earlier?.Dispose(); }
    }
}
