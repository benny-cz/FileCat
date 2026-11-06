using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisInventoryStage
{
    private static readonly SemaphoreSlim GraphBuilds = new(2, 2);
    // Covers the temporary ID map, graph indexes, one decoded record and validation
    // state. Retained stage headers share the same global import reservation.
    private const long GraphMemory = 24L * AnalysisLimits.RecordBytes;
    internal Task<AnalysisDataset> BuildDatasetAsync(CancellationToken cancellation = default) =>
        _owned.UseAsync(data => BuildDatasetAsync(data, cancellation));

    private async Task<AnalysisDataset> BuildDatasetAsync(Contents source, CancellationToken cancellation)
    {
        await GraphBuilds.WaitAsync(cancellation).ConfigureAwait(false);
        long reservation = 0; var build = new DatasetBuild(); AnalysisInventoryPathMap? map = null; AnalysisDataset? result = null;
        try
        {
            AnalysisInventoryFormat.Reserve(GraphMemory, ref reservation);
            using (var identities = source.PathIdentities(cancellation).GetEnumerator())
                map = await AnalysisInventoryPathMap.BuildAsync(source.Workspace!, _io, _devices, identities, cancellation).ConfigureAwait(false);
            var staged = source.Header!; var historical = staged.Historical;
            var header = new AnalysisIndexedGraphHeader(historical.Identity, historical.Roots, historical.Policy,
                historical.Phase, historical.Coverage, historical.Freshness, AnalysisOrigin.ImportedInventory, staged.Taint)
            {
                IssueTotals = staged.IssueTotals, BoundaryTotals = staged.Coverage.BoundaryTotals,
                ImportedCoverage = staged.Coverage
            };
            await AnalysisIo.Run(_io, _devices, _ =>
            {
                var arena = source.Workspace!.CreateArena();
                try { build.Writer = new(arena, header); }
                catch { arena.Dispose(); throw; }
                return true;
            }, cancellation).ConfigureAwait(false);
            foreach (var kind in new[] { AnalysisInventoryFormat.RecordKind.Object, AnalysisInventoryFormat.RecordKind.Path,
                AnalysisInventoryFormat.RecordKind.Fact, AnalysisInventoryFormat.RecordKind.Issue })
                for (ulong row = 1, count = source.Metadata!.Count(Table(kind)); row <= count; row++)
                {
                    var record = await ReadRecordAsync(source, kind, row, cancellation).ConfigureAwait(false);
                    ulong parentRow = 0;
                    if (kind == AnalysisInventoryFormat.RecordKind.Path)
                    {
                        var mapping = await map.ReadAsync(row, cancellation).ConfigureAwait(false);
                        if (mapping.Id != record.Path!.Id || mapping.ParentId != record.Path.ParentId) throw Invalid();
                        parentRow = mapping.ParentRow;
                    }
                    await AnalysisIo.Run(_io, _devices, token =>
                    {
                        switch (kind)
                        {
                            case AnalysisInventoryFormat.RecordKind.Object: build.Writer!.AppendObject(record.Object!, token); break;
                            case AnalysisInventoryFormat.RecordKind.Path: build.Writer!.AppendPathWithParentRow(record.Path!, parentRow, token); break;
                            case AnalysisInventoryFormat.RecordKind.Fact: build.Writer!.AppendFact(record.Fact!, token); break;
                            case AnalysisInventoryFormat.RecordKind.Issue: build.Writer!.AppendIssue(record.Issue!, token); break;
                            default: throw Invalid();
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                }
            build.Revision = await build.Writer!.SealAsync(source.Workspace!, _io, _devices, header, cancellation).ConfigureAwait(false);
            build.Reader = await AnalysisIo.Run(_io, _devices, _ => build.Revision.Acquire(), cancellation).ConfigureAwait(false);
            // Schema/trailer and relationships alone cannot certify checked accounting.
            foreach (var metric in Enum.GetValues<AnalysisMetric>())
                await AnalysisDatasetSummarizer.RunAsync(build.Reader, metric, _io, _devices, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            result = new(build.Revision, _io, _devices); build.Revision = null;
            return result;
        }
        catch (OverflowException error) { throw new InvalidDataException("Imported inventory accounting exceeds its numeric limits.", error); }
        finally
        {
            try
            {
                try { if (map is not null) await map.DisposeAsync().ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(_io, _devices, build).ConfigureAwait(false); }
            }
            catch
            {
                if (result is not null) await result.DisposeAsync().ConfigureAwait(false);
                throw;
            }
            finally { AnalysisInventoryFormat.Release(reservation); GraphBuilds.Release(); }
        }
    }

    private sealed class DatasetBuild : IAnalysisScratchOwned
    {
        internal AnalysisIndexedGraph? Writer;
        internal AnalysisIndexedGraphRevision? Revision;
        internal AnalysisIndexedGraphReader? Reader;
        public void Dispose()
        {
            try { Interlocked.Exchange(ref Reader, null)?.Dispose(); }
            finally
            {
                try { Interlocked.Exchange(ref Revision, null)?.Dispose(); }
                finally { Interlocked.Exchange(ref Writer, null)?.Dispose(); }
            }
        }
    }
}
