using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

internal static class AnalysisPanelResults
{
    internal const string LimitMessage = "Select up to 4,096 observed entries and 2 MiB of displayed paths to open them in the workspace.";

    internal static async Task<AnalysisPanelFolder> ReadAsync(IReadOnlySet<ulong> selected, string label, IReadOnlyList<RootBinding> roots, AnalysisTaint taint,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> read, CancellationToken cancellation)
    {
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        if (selected.Count == 0) throw new InvalidOperationException("Mark entries or focus one observed path first.");
        if (selected.Count > AnalysisPanelFolder.MaximumEntries) throw new NotSupportedException(LimitMessage);

        var lineage = new AnalysisReportLineage(roots, read);
        var rows = ImmutableArray.CreateBuilder<EntryData>(selected.Count);
        int nameBytes = 0;
        foreach (ulong id in selected.Order())
        {
            cancellation.ThrowIfCancellationRequested();
            var entry = await read(id, cancellation).ConfigureAwait(false);
            if (entry.Id != id) throw new InvalidDataException("A selected path is absent from this analysis revision.");
            string path = await lineage.ResolveAsync(entry, cancellation).ConfigureAwait(false);
            nameBytes = checked(nameBytes + Encoding.UTF8.GetByteCount(path));
            if (path.Length == 0) throw new NotSupportedException("A selected entry has no recorded location.");
            if (path == AnalysisReportLineage.TooLong || nameBytes > AnalysisPanelFolder.MaximumNameBytes)
                throw new NotSupportedException(LimitMessage);

            var flags = EntryFlags.ReadOnly;
            if (entry.Kind == AnalysisKind.Link) flags |= EntryFlags.Link;
            if (entry.Cloud == true) flags |= EntryFlags.Offline;
            if (entry.MetadataState != MeasurementState.Known) flags |= EntryFlags.Unavailable;
            var times = entry.Times;
            if (times is not null) flags |= EntryTimes.FlagFor(TimeSpan.FromTicks((long)Math.Min(times.ModifiedPrecisionTicks, (ulong)long.MaxValue)));
            rows.Add(new(path, entry.Kind == AnalysisKind.Directory ? EntryKind.Directory : EntryKind.File, modified: times?.ModifiedUtc?.UtcTicks ?? 0)
            {
                Created = times?.CreatedUtc?.UtcTicks ?? 0,
                Flags = flags
            });
        }
        return new($"{label} · {rows.Count:N0} observed paths", rows.MoveToImmutable(), FocusName: null, IsResultSet: true, Taint: taint);
    }
}
