using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

internal sealed record AnalysisPanelFolder(string DisplayPath, ImmutableArray<EntryData> Entries)
{
    internal const int MaximumEntries = 4096;
    internal const int MaximumNameBytes = 2 * 1024 * 1024;
    internal const string LimitMessage = "This folder exceeds the read-only panel limit of 4,096 entries or 2 MiB of displayed names.";
}

internal sealed partial class AnalysisIndexedAcquisition
{
    internal async Task<AnalysisPanelFolder> ReadPanelFolderAsync(SessionIdentity expected, ulong directory, CancellationToken cancellation)
    {
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        var runtime = await CaptureRuntimeBranchAsync(expected, directory, cancellation).ConfigureAwait(false);
        var folder = await OpenRuntimeBranchAsync(runtime, cancellation).ConfigureAwait(false);
        AnalysisPanelFolder listing;
        try
        {
            listing = await AnalysisIo.Run(_services.Io, folder.IoDeviceKey, token => Read(folder, runtime, token), cancellation)
                .ConfigureAwait(false);
        }
        finally { await AnalysisIo.ReleaseAsync(_services.Io, folder).ConfigureAwait(false); }

        cancellation.ThrowIfCancellationRequested();
        lock (_gate) ObjectDisposedException.ThrowIf(_close is not null, this);
        return listing;
    }

    private static AnalysisPanelFolder Read(IAnalysisBackend folder, RuntimeBranch runtime, CancellationToken cancellation)
    {
        var exclusions = new AnalysisFolderExclusions([folder], runtime.Session.ExclusionsForBranch(runtime.RootIndex, runtime.Selection.Children));
        var encoded = exclusions.EncodePaths([folder.Root], 0);
        var rows = ImmutableArray.CreateBuilder<EntryData>();
        int nameBytes = 0;
        folder.ValidateReadScope(folder.RootEntry);
        foreach (var observed in folder.Children(folder.RootEntry, cancellation))
        {
            cancellation.ThrowIfCancellationRequested();
            if (runtime.Session.IsPrivateAnalysisEntry(observed)) continue;
            var entry = AnalysisFolderExclusions.Apply(observed, encoded);
            var node = new AnalysisNode(0, folder.Root.Id, 0, entry.Name, entry.Kind) { NameEncoding = entry.NameEncoding, RawName = entry.RawName };
            string name = AnalysisNames.DisplayName(node, AnalysisLimits.RawNameBytes);
            nameBytes = checked(nameBytes + Encoding.UTF8.GetByteCount(name));
            if (rows.Count >= AnalysisPanelFolder.MaximumEntries || nameBytes > AnalysisPanelFolder.MaximumNameBytes)
                throw new NotSupportedException(AnalysisPanelFolder.LimitMessage);

            long length = entry.LogicalLength.Value is { } size && size <= (UInt128)long.MaxValue ? (long)size : -1;
            var flags = EntryFlags.ReadOnly | EntryTimes.FlagFor(TimeSpan.FromTicks((long)Math.Min(entry.ModifiedPrecisionTicks, (ulong)long.MaxValue)));
            if (entry.Kind == AnalysisKind.Link) flags |= EntryFlags.Link;
            if (entry.Cloud == true) flags |= EntryFlags.Offline;
            if (entry.MetadataState != MeasurementState.Known) flags |= EntryFlags.Unavailable;
            rows.Add(new(name, entry.Kind == AnalysisKind.Directory ? EntryKind.Directory : EntryKind.File, length, entry.ModifiedUtc?.UtcTicks ?? 0)
            {
                Created = entry.CreatedUtc?.UtcTicks ?? 0,
                Flags = flags
            });
        }
        folder.ValidateReadScope(folder.RootEntry);
        return new(AnalysisNames.DisplayPath(RuntimeReopenLocator(folder), folder.Root.NameRules), rows.ToImmutable());
    }
}
