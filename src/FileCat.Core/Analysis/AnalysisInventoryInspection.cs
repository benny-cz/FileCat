using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisInventoryFormat
{
    internal sealed record ContainerInspection(Guid Id, long Bytes, DateTimeOffset Started, DateTimeOffset Observed,
        NamespaceCoverage Coverage, AnalysisTaint Taint, string Sha256, ulong Records);

    // Container inspection validates schema/framing/checksum, not cross-record graph semantics.
    // No decoded path is retained or reconstructed as filesystem authority.
    internal static Task<ContainerInspection> InspectContainerAsync(AnalysisPrivateInventoryStore.DatasetRead input,
        DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation) =>
        InspectContainerAsync(input.Stream, input.Verify, io, device, cancellation);

    internal static async Task<ContainerInspection> InspectContainerAsync(Stream source, Action verify,
        DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation)
    {
        await Imports.WaitAsync(cancellation).ConfigureAwait(false);
        long reservation = 0;
        try
        {
            // Same bounded construction reservation as streaming import, without its scratch graph.
            Reserve(StageMemory, ref reservation);
            var frame = new RecordFramer(source); using var decoder = new RecordDecoder();
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Manifest? manifest = null; ulong records = 0; bool ended = false;
            while (!ended)
            {
                var batch = await AnalysisIo.Run(io, device, token =>
                {
                    verify(); int read = 0, steps = 0, bytes = 0; bool eof = false;
                    var lines = new List<byte[]>(); var clock = Stopwatch.StartNew();
                    while (steps++ < AnalysisLimits.BatchRecords && read < AnalysisLimits.BatchBytes && bytes < AnalysisLimits.BatchBytes)
                    {
                        var step = frame.Step(token); read += step.BytesRead;
                        if (step.Status == FrameStatus.End) { eof = true; break; }
                        if (step.Status == FrameStatus.Record) { lines.Add(frame.Line.ToArray()); bytes += frame.Line.Length + 1; }
                        if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    verify(); return (Lines: lines, Ended: eof);
                }, cancellation).ConfigureAwait(false);
                await Task.Run(() =>
                {
                    foreach (var line in batch.Lines)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var decoded = decoder.Read(line, cancellation); records++;
                        digest.AppendData(line); digest.AppendData(Lf);
                        if (decoded.Kind == RecordKind.Manifest) manifest = decoded.Manifest;
                    }
                }, cancellation).ConfigureAwait(false);
                ended = batch.Ended;
            }
            decoder.Complete(); cancellation.ThrowIfCancellationRequested();
            if (manifest is null) throw Invalid("The saved inventory has no supported manifest.");
            return new(manifest.Id, frame.BytesRead, manifest.Identity.StartedUtc, manifest.Identity.ObservedUtc, manifest.Coverage,
                manifest.Taint, Convert.ToHexStringLower(digest.GetHashAndReset()), records);
        }
        finally { Release(reservation); Imports.Release(); }
    }
}
