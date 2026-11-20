using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

internal static class ScratchGraphScale
{
    internal static async Task<int> RunAsync(int count, string parent)
    {
        if (count is not (1_000_000 or 10_000_000)) throw new ArgumentOutOfRangeException(nameof(count));
        using var io = new DeviceIoScheduler(); const string device = "native-graph-scale";
        var workspace = await AnalysisIo.Run(io, device, _ => new AnalysisScratchWorkspace(ScratchScale.Directory(parent)));
        using var stop = new CancellationTokenSource(); using var process = Process.GetCurrentProcess(); GC.Collect(); process.Refresh();
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
        bool windows = OperatingSystem.IsWindows(); var start = DateTimeOffset.UtcNow; ulong half = (ulong)count / 2;
        string inert = Path.Combine(parent, "inert-observed-source");
        var header = new AnalysisIndexedGraphHeader(new(Guid.NewGuid(), 1, 0, "inert-graph-fixture", start, start) { TimeZoneId = "UTC" },
            [new("root", "file", inert, inert, null, IdentityAssurance.Unknown, "source") { NameRules = windows ? AnalysisNameRules.Windows : AnalysisNameRules.Unix }],
            new(), AnalysisPhase.Scanning, NamespaceCoverage.Partial, Origin: AnalysisOrigin.ImportedInventory);
        string Digits(ulong id) => id.ToString("D8", CultureInfo.InvariantCulture);
        AnalysisNode Node(ulong id)
        {
            string digits = Digits(id); bool directory = id <= half;
            int length = id % 16384 == 0 ? AnalysisLimits.RawNameBytes / (windows ? 2 : 1) : 24;
            string name = id == 1 ? "source" : new string('x', length - digits.Length - 1) + "-" + digits;
            bool? flag = (id % 3) switch { 0 => null, 1 => false, _ => true };
            return new(id, "root", id / 2, name, directory ? AnalysisKind.Directory : AnalysisKind.File,
                directory ? null : "o" + digits, flag, flag.HasValue ? !flag.Value : null, flag)
            { NameEncoding = windows ? "utf16le" : "utf8", ObservedIdentity = directory ? null : "i" + digits, Times = new(start.AddTicks((long)id), null, 1) };
        }
        AnalysisObject Object(ulong row)
        { string digits = Digits(half + row); return new("o" + digits, "i" + digits, AnalysisMeasurement.Known(row), AnalysisMeasurement.Known(1), VersionEvidence: "v"); }
        StorageFact Fact(ulong row) => new("f" + Digits(half + row), "o" + Digits(half + row), null, "data", "fixture", AnalysisMeasurement.Known(row * 4096), "native", start, []);
        AnalysisIssue Issue(ulong row) => new("synthetic-storage-probe", row * 4096, MeasurementState.Unsupported) { RootId = "root" };
        AnalysisIndexedGraph? graph = null; AnalysisIndexedGraphRevision? revision = null; AnalysisIndexedGraphReader? reader = null;
        string foreign = Path.Combine(parent, "scratch", "foreign.txt");
        try
        {
            graph = await AnalysisIo.Run(io, device, _ => new AnalysisIndexedGraph(workspace.CreateArena(), header));
            var timer = Stopwatch.StartNew(); ulong next = 1;
            while (next <= (ulong)count)
                await AnalysisIo.Run(io, device, token =>
                {
                    for (int batch = 0; batch < 128 && next <= (ulong)count; batch++, next++)
                    {
                        token.ThrowIfCancellationRequested(); graph.AppendPath(Node(next), token);
                        if (next > half) { graph.AppendObject(Object(next - half), token); graph.AppendFact(Fact(next - half), token); }
                        if (next % 4096 == 0) graph.AppendIssue(Issue(next / 4096), token);
                    }
                    return true;
                });
            double writeSeconds = timer.Elapsed.TotalSeconds; timer.Restart();
            revision = await graph.SealAsync(workspace, io, device, header.Identity with { Revision = 1, ObservedUtc = start.AddTicks(count) },
                AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);
            double indexAndValidationSeconds = timer.Elapsed.TotalSeconds; long scratchBytes = workspace.ReservedBytes;
            reader = await AnalysisIo.Run(io, device, _ => revision.Acquire());
            await AnalysisIo.ReleaseScratchAsync(io, device, graph); graph = null;
            await AnalysisIo.ReleaseScratchAsync(io, device, revision); revision = null;
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace); File.WriteAllText(foreign, "preserve me");
            timer.Restart(); ulong checksum = 0;
            for (int table = 0; table < 4; table++)
            {
                int kind = table; next = 1; ulong rows = reader.Count(table);
                ulong expected = table switch { AnalysisIndexedGraph.Paths => (ulong)count, AnalysisIndexedGraph.Issues => (ulong)count / 4096, _ => half };
                if (rows != expected) throw new InvalidDataException("The indexed graph changed a typed table count.");
                while (next <= rows)
                    await AnalysisIo.Run(io, device, token =>
                    {
                        for (int batch = 0; batch < 128 && next <= rows; batch++, next++)
                        {
                            token.ThrowIfCancellationRequested(); bool equal = kind switch
                            {
                                AnalysisIndexedGraph.Paths => reader.ReadPath(next, token) == Node(next),
                                AnalysisIndexedGraph.Objects => reader.ReadObject(next, token) == Object(next),
                                AnalysisIndexedGraph.Facts => reader.ReadFact(next, token) == Fact(next),
                                _ => reader.ReadIssue(next, token) == Issue(next)
                            };
                            if (!equal) throw new InvalidDataException("An indexed graph typed observation changed.");
                            if (kind == AnalysisIndexedGraph.Paths) checksum += next;
                        }
                        return true;
                    });
            }
            for (int index = 0; index < AnalysisIndexedGraphRevision.IndexCount; index++)
            {
                int kind = index; next = 1; byte[]? previous = null; ulong rows = reader.IndexCount(kind);
                ulong expected = kind switch { AnalysisIndexedGraphRevision.Children => (ulong)count, AnalysisIndexedGraphRevision.IssueOwners => (ulong)count / 4096, _ => half };
                if (rows != expected) throw new InvalidDataException("The indexed graph changed an index count.");
                while (next <= rows)
                    await AnalysisIo.Run(io, device, token =>
                    {
                        for (int batch = 0; batch < 128 && next <= rows; batch++, next++)
                        {
                            token.ThrowIfCancellationRequested(); var entry = reader.ReadIndex(kind, next, token); ulong value = entry.Value;
                            if (value == 0 || value > (kind is AnalysisIndexedGraphRevision.Children or AnalysisIndexedGraphRevision.Aliases ? (ulong)count : expected) ||
                                kind == AnalysisIndexedGraphRevision.Aliases && value <= half) throw new InvalidDataException("An indexed graph value is outside its source table.");
                            byte[] key = kind switch
                            {
                                AnalysisIndexedGraphRevision.Children => AnalysisGraphKeys.Child(new(Node(value), 0, 0)),
                                AnalysisIndexedGraphRevision.ObjectIds => AnalysisGraphKeys.Text(Object(value).Id),
                                AnalysisIndexedGraphRevision.Aliases => AnalysisGraphKeys.Text(Node(value).ObjectId!),
                                AnalysisIndexedGraphRevision.FactOwners => AnalysisGraphKeys.FactOwner(Fact(value).ObjectId, null),
                                AnalysisIndexedGraphRevision.FactIds => AnalysisGraphKeys.Text(Fact(value).Id),
                                _ => AnalysisGraphKeys.IssueOwner(Issue(value).NodeId, "root", header.Roots)
                            };
                            if (!entry.Key.AsSpan().SequenceEqual(key) || previous is not null && previous.AsSpan().SequenceCompareTo(entry.Key) >= 0)
                                throw new InvalidDataException("An indexed graph key/value or exact order changed.");
                            previous = entry.Key;
                        }
                        return true;
                    });
            }
            await AnalysisIo.Run(io, device, token =>
            {
                if (reader.Header.Origin != AnalysisOrigin.ImportedInventory || !reader.Header.Taint.HasFlag(AnalysisTaint.ImportedUnverified)) throw new InvalidDataException("The graph lost its inert classification.");
                foreach (ulong id in new[] { half + 1, half + half / 2, (ulong)count })
                {
                    if (reader.Aliases("o" + Digits(id), token).Count != 1 || reader.Children(0, id / 2, token).Count == 0)
                        throw new InvalidDataException("An exact related-graph range changed.");
                }
                return true;
            });
            double readSeconds = timer.Elapsed.TotalSeconds;
            if (checksum != (ulong)count * ((ulong)count + 1) / 2) throw new InvalidDataException("The graph checksum changed.");
            await AnalysisIo.ReleaseScratchAsync(io, device, reader); reader = null; stop.Cancel(); await sampling;
            bool cleanup = System.IO.Directory.GetFileSystemEntries(Path.Combine(parent, "scratch")).SequenceEqual([foreign]) && File.ReadAllText(foreign) == "preserve me" &&
                workspace.ReservedBytes == 0 && AnalysisScratchBudget.Shared.ReservedBytes == 0;
            bool privateAvailable = baselinePrivate > 0, memory = peakPrivate - baselinePrivate <= 512L * 1024 * 1024;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                purpose = "Native typed graph and six exact secondary indexes; synthetic storage only, no source traversal or production projection/export qualification",
                records = count, objects = half, facts = half, issues = count / 4096, ordinaryNameCharacters = 24, longNameRawBytes = AnalysisLimits.RawNameBytes,
                longNameEvery = 16384, nameEncoding = windows ? "utf16le" : "utf8", parentDistribution = "binary tree, first half directories and second half single-alias files; fixed-width synthetic IDs",
                writeSeconds, indexAndValidationSeconds, readSeconds, scratchBytes, sampledPeakSessionScratchBytes = peakScratch, sampledIntervalMilliseconds = 50,
                privateMemoryAvailable = privateAvailable, baselinePrivateBytes = privateAvailable ? (long?)baselinePrivate : null,
                sampledPeakPrivateIncrement = privateAvailable ? (long?)(peakPrivate - baselinePrivate) : null, privateMemoryBudgetVerified = privateAvailable && memory,
                privateMemoryBasis = privateAvailable ? ".NET process private bytes; sampled, not an absolute peak" : "Unavailable from this runtime; working-set samples are not private bytes",
                sampledPeakWorkingSetIncrement = peakWorking - baselineWorking, verifiedEveryRecord = true, verifiedEveryIndexEntry = true, exactRangesVerified = true,
                retainedReaderSurvivedOwnerClosure = true, inertClassificationPreserved = true, checksum,
                ownedArtifactsRemoved = cleanup, foreignNeighborPreserved = cleanup, remainingQuotaBytes = workspace.ReservedBytes,
                remainingProfileQuotaBytes = AnalysisScratchBudget.Shared.ReservedBytes, passed = cleanup && (!privateAvailable || memory)
            }));
            return cleanup && (!privateAvailable || memory) ? 0 : 22;
        }
        finally
        {
            stop.Cancel(); await sampling;
            if (reader is not null) await AnalysisIo.ReleaseScratchAsync(io, device, reader);
            if (revision is not null) await AnalysisIo.ReleaseScratchAsync(io, device, revision);
            if (graph is not null) await AnalysisIo.ReleaseScratchAsync(io, device, graph);
            await AnalysisIo.ReleaseScratchAsync(io, device, workspace);
        }
    }
}
