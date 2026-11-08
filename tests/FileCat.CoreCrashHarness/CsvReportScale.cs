using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

internal static class CsvReportScale
{
    internal static async Task<int> RunAsync(int count, string shape, string parent)
    {
        if (count is not (1_000 or 1_000_000 or 10_000_000) || shape is not ("wide" or "deep")) throw new ArgumentOutOfRangeException(nameof(count));
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        string absolute = Path.GetFullPath(parent); int directories = shape == "deep" ? 64 : 1;
        string inert = Path.Combine(absolute, "inert-observed-source"), output = Path.Combine(absolute, "output");
        if (Directory.Exists(inert) || Directory.Exists(output)) throw new InvalidOperationException("Use a fresh generated scale parent.");
        using var io = new DeviceIoScheduler(); AnalysisIoDevices device = "native-csv-scale";
        var workspace = await AnalysisIo.Run(io, device, _ => new AnalysisScratchWorkspace(ScratchScale.Directory(absolute)));
        using var process = Process.GetCurrentProcess(); using var stop = new CancellationTokenSource();
        GC.Collect(); process.Refresh(); long baselinePrivate = process.PrivateMemorySize64, baselineWorking = process.WorkingSet64;
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
        AnalysisIndexedGraph? graph = null; AnalysisIndexedGraphRevision? revision = null; AnalysisDataset? dataset = null; AnalysisDatasetLease? lease = null;
        long retainedSourceScratch = 0; bool destinationOpened = false;
        try
        {
            var start = DateTimeOffset.UtcNow;
            var header = new AnalysisIndexedGraphHeader(new(Guid.NewGuid(), 1, 0, "inert-csv-scale", start, start) { TimeZoneId = "UTC" },
                [new("root", "file", inert, inert, null, IdentityAssurance.Unknown, "source") { NameRules = OperatingSystem.IsWindows() ? AnalysisNameRules.Windows : AnalysisNameRules.Unix }],
                new(), AnalysisPhase.Scanning, NamespaceCoverage.Partial, Origin: AnalysisOrigin.ImportedInventory);
            graph = await AnalysisIo.Run(io, device, _ => new AnalysisIndexedGraph(workspace.CreateArena(), header));
            Console.WriteLine("PHASE graph-write"); Console.Out.Flush(); var timer = Stopwatch.StartNew(); ulong next = 1;
            while (next <= (ulong)count)
                await AnalysisIo.Run(io, device, token =>
                {
                    for (int batch = 0; batch < 128 && next <= (ulong)count; batch++, next++)
                    {
                        token.ThrowIfCancellationRequested(); bool directory = next <= (ulong)directories;
                        string digits = next.ToString("D8", CultureInfo.InvariantCulture), name = directory ? "d" + digits : (next % 1024 == 0 ? "=f" : "f") + digits;
                        string? objectId = directory ? null : "o" + digits;
                        graph.AppendPath(new(next, "root", directory ? next - 1 : (ulong)directories, name, directory ? AnalysisKind.Directory : AnalysisKind.File, objectId)
                        { NameEncoding = OperatingSystem.IsWindows() ? "utf16le" : "utf8" }, token);
                        if (!directory) graph.AppendObject(new(objectId!, null, AnalysisMeasurement.Known(8), AnalysisMeasurement.Known(1)), token);
                    }
                    return true;
                });
            double writeSeconds = timer.Elapsed.TotalSeconds; timer.Restart(); Console.WriteLine("PHASE graph-seal"); Console.Out.Flush();
            revision = await graph.SealAsync(workspace, io, device, header.Identity with { Revision = 1, ObservedUtc = DateTimeOffset.UtcNow }, AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy);
            double sealSeconds = timer.Elapsed.TotalSeconds;
            dataset = new AnalysisDataset(revision, io, device); revision = null; lease = await dataset.AcquireAsync();
            await AnalysisIo.ReleaseScratchAsync(io, device, graph); graph = null;
            retainedSourceScratch = workspace.ReservedBytes;
            await AnalysisIo.Run(io, device, _ => { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "neighbor.txt"), "preserve"); return true; });
            var results = new List<object>();
            foreach (bool redact in new[] { true, false })
            {
                Console.WriteLine("PHASE report-capture redact=" + redact); Console.Out.Flush(); timer.Restart();
                await using var report = await lease.CaptureReportAsync(AnalysisMetric.FileSize, AnalysisReportFormat.Csv, AnalysisReportScope.EntireResult, new(""));
                double captureSeconds = timer.Elapsed.TotalSeconds;
                if (report.Header.Rows != (ulong)count || report.Header.Totals.Known != (UInt128)(count - directories) * 8) throw new InvalidDataException("The captured scale summary differs from the generated observations.");
                string name = redact ? "redacted.csv" : "displayed.csv"; timer.Restart(); Console.WriteLine("PHASE report-export redact=" + redact); Console.Out.Flush();
                var receipt = await report.ExportNewAsync(name, device, () => { destinationOpened = true; return Destination(output, absolute); }, redact);
                double exportSeconds = timer.Elapsed.TotalSeconds; string path = Path.Combine(output, name);
                long bytes = 0, lines = 0; string hash;
                var stream = await AnalysisIo.Run(io, device, _ => File.OpenRead(path));
                try
                {
                    using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[64 * 1024];
                    while (true)
                    {
                        int read = await AnalysisIo.Run(io, device, _ => stream.Read(buffer)); if (read == 0) break;
                        bytes += read; checksum.AppendData(buffer, 0, read);
                        for (int i = 0; i < read; i++) if (buffer[i] == (byte)'\n') lines++;
                    }
                    hash = Convert.ToHexStringLower(checksum.GetHashAndReset());
                }
                finally { await AnalysisIo.Run(io, device, _ => { stream.Dispose(); return true; }); }
                if (bytes != receipt.Bytes || lines != count + 2L) throw new InvalidDataException("The full CSV did not contain every declared row.");
                results.Add(new { redact, captureSeconds, exportSeconds, bytes, lines, sha256 = hash, verifiedEveryDeclaredRow = true });
                await AnalysisIo.Run(io, device, _ =>
                {
                    if (Path.GetDirectoryName(Path.GetFullPath(path)) != output) throw new InvalidOperationException("Owned output escaped the generated scale folder.");
                    File.Delete(path); return true;
                });
            }
            await lease.DisposeAsync(); lease = null; await dataset.DisposeAsync(); dataset = null;
            stop.Cancel(); await sampling;
            bool clean = workspace.ReservedBytes == 0 && AnalysisScratchBudget.Shared.ReservedBytes == 0 &&
                !Directory.Exists(inert) && File.ReadAllText(Path.Combine(output, "neighbor.txt")) == "preserve" && Directory.GetFiles(output).Length == 1;
            bool privateAvailable = baselinePrivate > 0, bounded = peakPrivate - baselinePrivate <= 512L * 1024 * 1024;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                purpose = "Production canonical CSV capture, bounded row/lineage reads and guarded native publication over an inert generated indexed dataset; no source traversal qualification",
                records = count, shape, directories, observedSourceWasNeverCreated = !Directory.Exists(inert), writeSeconds, sealSeconds, reports = results,
                sampledPeakSessionScratchBytes = peakScratch, sampledIntervalMilliseconds = 50, baselinePrivateBytes = privateAvailable ? (long?)baselinePrivate : null,
                sampledPeakPrivateIncrement = privateAvailable ? (long?)(peakPrivate - baselinePrivate) : null, privateMemoryBudgetVerified = privateAvailable && bounded,
                privateMemoryBasis = privateAvailable ? ".NET process private bytes; sampled, not an absolute peak" : "Unavailable; working-set samples are not private bytes",
                sampledPeakWorkingSetIncrement = peakWorking - baselineWorking, ownedArtifactsRemoved = clean, foreignNeighborPreserved = clean,
                remainingQuotaBytes = workspace.ReservedBytes, remainingProfileQuotaBytes = AnalysisScratchBudget.Shared.ReservedBytes, passed = clean && (!privateAvailable || bounded)
            }));
            return clean && (!privateAvailable || bounded) ? 0 : 29;
        }
        catch (IOException error) when (count == 10_000_000 && lease is not null && !destinationOpened &&
            error.Message == "The private analysis scratch budget was reached. Keep the partial result or choose a narrower scope.")
        {
            // The plan makes limits independent; this is a measured safe refusal,
            // not evidence that a complete 10M-row CSV can be exported at this cap.
            var first = await lease.ReadPathAsync(1); var last = await lease.ReadPathAsync((ulong)count);
            bool retained = lease.Descriptor.Paths == (ulong)count && first.Id == 1 && last.Id == (ulong)count && workspace.ReservedBytes == retainedSourceScratch;
            await lease.DisposeAsync(); lease = null; await dataset!.DisposeAsync(); dataset = null;
            stop.Cancel(); await sampling;
            bool clean = workspace.ReservedBytes == 0 && AnalysisScratchBudget.Shared.ReservedBytes == 0 && !Directory.Exists(inert) &&
                Directory.GetFiles(output).Length == 1 && File.ReadAllText(Path.Combine(output, "neighbor.txt")) == "preserve";
            bool privateAvailable = baselinePrivate > 0, bounded = peakPrivate - baselinePrivate <= 512L * 1024 * 1024;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                purpose = "Measured 10M report-capture scratch refusal with retained source and native cleanup; full 10M CSV export is not qualified",
                records = count, shape, directories, limitRefusal = true, fullCsvExportQualified = false, retainedSourceUnchanged = retained,
                sourcePaths = count, firstSourcePath = first.Id, lastSourcePath = last.Id, retainedSourceScratchBytes = retainedSourceScratch,
                sampledPeakSessionScratchBytes = peakScratch, scratchLimitBytes = AnalysisLimits.ScratchBytes, destinationWasNeverOpened = !destinationOpened,
                observedSourceWasNeverCreated = !Directory.Exists(inert), ownedArtifactsRemoved = clean, foreignNeighborPreserved = clean,
                baselinePrivateBytes = privateAvailable ? (long?)baselinePrivate : null, sampledPeakPrivateIncrement = privateAvailable ? (long?)(peakPrivate - baselinePrivate) : null,
                privateMemoryBudgetVerified = privateAvailable && bounded, sampledPeakWorkingSetIncrement = peakWorking - baselineWorking,
                remainingQuotaBytes = workspace.ReservedBytes, remainingProfileQuotaBytes = AnalysisScratchBudget.Shared.ReservedBytes,
                passed = retained && clean && (!privateAvailable || bounded)
            }));
            return retained && clean && (!privateAvailable || bounded) ? 0 : 30;
        }
        finally
        {
            stop.Cancel(); await sampling;
            try { if (lease is not null) await lease.DisposeAsync(); } finally
            { try { if (dataset is not null) await dataset.DisposeAsync(); } finally
                { try { if (revision is not null) await AnalysisIo.ReleaseScratchAsync(io, device, revision); } finally
                    { try { if (graph is not null) await AnalysisIo.ReleaseScratchAsync(io, device, graph); } finally { await AnalysisIo.ReleaseScratchAsync(io, device, workspace); } } } }
        }
    }
    private static AnalysisExportDestination Destination(string output, string parent)
    {
        if (!OperatingSystem.IsWindows()) return UnixAnalysisPrivateDirectory.OpenExportDestination(output, () => new UnixAnalysisPrivateDirectory(parent, "export-staging"));
        var assembly = Assembly.LoadFrom(Environment.GetEnvironmentVariable("FILECAT_TEST_SCRATCH_WINDOWS_ADAPTER") ?? Path.Combine(AppContext.BaseDirectory, "FileCat.Platform.Windows.dll"));
        var type = assembly.GetType("FileCat.Platform.Windows.WindowsAnalysisPrivateDirectory", throwOnError: true)!;
        var method = type.GetMethod("OpenExportDestination", BindingFlags.Public | BindingFlags.Static)!;
        return (AnalysisExportDestination)method.Invoke(null, method.GetParameters().Select((parameter, i) => i == 0 ? (object)output : parameter.DefaultValue).ToArray())!;
    }
}
