using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisGroupChartRemainder(string? Parent, int Skipped);

public sealed partial class AnalysisGroupedChart
{
    private AnalysisGroupedChart(AnalysisMetric metric, DateTimeOffset observed, string scope, Dictionary<string, AnalysisTreeItem> items,
        Dictionary<string, ImmutableArray<AnalysisTreeItem>> children, Dictionary<string, AnalysisGroupChartRemainder> remainders, bool limited)
    {
        Metric = metric; ObservedUtc = observed; Scope = scope; Items = items.ToImmutableDictionary(StringComparer.Ordinal);
        _geometryChildren = children.ToImmutableDictionary(StringComparer.Ordinal); Remainders = remainders.ToImmutableDictionary(StringComparer.Ordinal); DetailLimited = limited;
        RemainderKey = remainders.Keys.FirstOrDefault(key => Items[key].ParentKey == scope); RemainderOffset = RemainderKey is null ? 0 : (ulong)remainders[RemainderKey].Skipped;
    }
    public static async Task<AnalysisGroupedChart> BuildAsync(AnalysisDatasetGroupExplorer explorer, string? groupKey = null, CancellationToken cancellation = default)
    {
        if (groupKey == AnalysisTree.RootKey) groupKey = null;
        var group = groupKey is null ? null : await explorer.ReadGroupAsync(groupKey, cancellation).ConfigureAwait(false);
        var totals = group?.Totals ?? explorer.Totals; var logical = group?.MemberLogical ?? explorer.LogicalTotals;
        string scope = explorer.OtherScopeKey ?? groupKey ?? AnalysisTree.RootKey;
        string name = explorer.OtherScopeKey is not null ? "Other small items" : group is not null ? AnalysisNames.Display(group.Definition.Label, 256) : "All groups — " + AnalysisGroupingRules.Title(explorer.Dimension);
        var root = ScopeItem(scope, explorer.OtherScopeKey is not null ? groupKey ?? AnalysisTree.RootKey : AnalysisTree.RootKey, name, totals, logical);
        var items = new Dictionary<string, AnalysisTreeItem>(StringComparer.Ordinal); var children = new Dictionary<string, ImmutableArray<AnalysisTreeItem>>(StringComparer.Ordinal);
        var remainders = new Dictionary<string, AnalysisGroupChartRemainder>(StringComparer.Ordinal); long bytes = 0; bool limited = false;
        if (!Add(root)) throw new IOException("The bounded grouped root could not fit.");
        if (explorer.OtherScopeKey is not null && group is not null) Add(GroupItem(group, explorer.Dimension, explorer.Metric, AnalysisTree.RootKey));
        var queue = new Queue<(string? Parent, string Key, int Skipped, AnalysisTotals Totals, AnalysisLogicalTotals Logical)>();
        queue.Enqueue((groupKey, scope, explorer.GeometrySkipped, totals, logical));
        while (queue.TryDequeue(out var parent))
        {
            cancellation.ThrowIfCancellationRequested();
            if (items.Count + 34 > AnalysisLimits.ChartLeaves || bytes + 32 * 2048 > AnalysisLimits.PendingBytes / 4) { limited = true; continue; }
            UInt128 total = Area(parent.Totals, explorer.Metric); if (total == 0) continue;
            var page = await explorer.ReadGeometryAsync(parent.Parent, maximum: 32, cancellation: cancellation).ConfigureAwait(false);
            var rows = ImmutableArray.CreateBuilder<AnalysisTreeItem>(); UInt128 shown = 0, shownLogical = 0, shownUnverified = 0; ulong shownFiles = 0; int used = 0;
            foreach (var source in page.Rows)
            {
                if (source.Value == 0) break; var item = source with { ParentKey = parent.Key };
                if (!Add(item)) { limited = true; break; }
                rows.Add(item); used++; shown = checked(shown + item.Value); shownLogical = checked(shownLogical + item.LogicalSize);
                shownUnverified = checked(shownUnverified + item.IdentityUnverified); shownFiles = checked(shownFiles + item.Files);
                if (parent.Parent is null && item.GroupDefinition is { } definition)
                {
                    var detail = await explorer.ReadGroupAsync(definition.Key, cancellation).ConfigureAwait(false);
                    queue.Enqueue((definition.Key, item.Key, 0, detail.Totals, detail.MemberLogical));
                }
            }
            if (shown > total || shownLogical > parent.Logical.KnownBytes || shownFiles > parent.Logical.Files || shownUnverified > parent.Totals.IdentityUnverified)
                throw new InvalidDataException("Grouped chart children exceed their captured accounting or member observations.");
            if (shown < total)
            {
                if (used == 0) throw new IOException("The bounded grouped geometry made no progress. The previous view remains displayed.");
                string key = AnalysisTree.OtherKey(parent.Key, used);
                var remainder = new AnalysisTreeItem(key, parent.Key, "Other small items", null, AnalysisKind.Special, true, true, total - shown,
                    parent.Logical.KnownBytes - shownLogical, parent.Logical.Files - shownFiles, parent.Totals.Missing.Count != 0, false, "other", [], [])
                    { IdentityUnverified = parent.Totals.IdentityUnverified - shownUnverified, LogicalUnknown = parent.Logical.UnknownFiles != 0 };
                if (!Add(remainder)) throw new IOException("The bounded grouped remainder could not fit. The previous view remains displayed.");
                rows.Add(remainder); remainders.Add(key, new(parent.Parent, checked(parent.Skipped + used))); limited = true;
            }
            children.Add(parent.Key, rows.ToImmutable());
        }
        return new(explorer.Metric, explorer.Descriptor.Identity.ObservedUtc, scope, items, children, remainders, limited);
        bool Add(AnalysisTreeItem item)
        {
            long cost = 768L + item.Name.Length * 2L + item.Key.Length * 2L + item.ParentKey.Length * 2L + (item.NameEvidence?.Length ?? 0) * 2L;
            if (items.ContainsKey(item.Key)) return true;
            if (items.Count >= AnalysisLimits.ChartLeaves || cost > AnalysisLimits.PendingBytes / 4 - bytes) return false;
            items.Add(item.Key, item); bytes += cost; return true;
        }
        AnalysisTreeItem ScopeItem(string key, string parent, string label, AnalysisTotals amount, AnalysisLogicalTotals paths) =>
            new(key, parent, label, null, AnalysisKind.Directory, true, true, Area(amount, explorer.Metric), paths.KnownBytes, paths.Files,
                !amount.CompatibleBasis || amount.Missing.Count != 0 || explorer.Metric == AnalysisMetric.UniqueFileSize && amount.IdentityUnverified != 0 || explorer.OtherScopeKey is not null && explorer.Unknown,
                false, "folder", [], []) { IdentityUnverified = amount.IdentityUnverified, LogicalUnknown = paths.UnknownFiles != 0,
                    ReferenceLogical = group?.Kind == AnalysisGroupKind.SharedAcrossGroups ? paths : null };
    }
}
