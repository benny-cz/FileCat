using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Independently retained published folder/group/Other scope. Its rows never bind a source.</summary>
public sealed class AnalysisCapturedViewReport : AnalysisCapturedReport
{
    private readonly AnalysisDatasetLease _source;
    private readonly AnalysisDatasetExplorer? _folders;
    private readonly AnalysisDatasetGroupExplorer? _groups;
    private readonly ulong _parent;
    private readonly string? _group;
    private readonly AnalysisTableSort _sort;
    private readonly bool _descending;
    private readonly AnalysisHierarchySort _folderSort;
    private readonly bool _references;
    private AnalysisCapturedViewReport(AnalysisDatasetLease source, AnalysisDatasetExplorer? folders, AnalysisDatasetGroupExplorer? groups,
        ulong parent, string? group, AnalysisTableSort sort, AnalysisHierarchySort folderSort, bool descending, bool references,
        DeviceIoScheduler io, AnalysisIoDevices devices, AnalysisReportHeader header, AnalysisReportFormat format) : base(io, devices, header, format)
    { _source = source; _folders = folders; _groups = groups; _parent = parent; _group = group; _sort = sort; _folderSort = folderSort; _descending = descending; _references = references; }

    internal static async Task<AnalysisCapturedViewReport> CreateAsync(AnalysisDatasetLease source, DeviceIoScheduler io, AnalysisIoDevices devices,
        AnalysisDatasetExplorer? folders, AnalysisDatasetGroupExplorer? groups, ulong parent, string? group, AnalysisReportFormat format,
        AnalysisTableSort sort, AnalysisHierarchySort folderSort, bool descending, CancellationToken cancellation)
    {
        if ((folders is null) == (groups is null)) throw new ArgumentException("Choose one captured view.");
        if (!Enum.IsDefined(format) || !Enum.IsDefined(sort) || !Enum.IsDefined(folderSort)) throw new ArgumentOutOfRangeException(nameof(format));
        var descriptor = folders?.Descriptor ?? groups!.Descriptor;
        if (source.Descriptor.Identity != descriptor.Identity || !source.Descriptor.Roots.Equals(descriptor.Roots))
            throw new InvalidOperationException("The report owner and view must share the same captured source revision.");
        AnalysisDatasetLease? retained = await source.RetainAsync(cancellation).ConfigureAwait(false);
        AnalysisDatasetExplorer? folderScope = null; AnalysisDatasetGroupExplorer? groupScope = null; Exception? failure = null;
        try
        {
            AnalysisTotals totals; AnalysisLogicalTotals logical; ulong totalRows; bool unknown, logicalUnknown, references = false;
            string label; AnalysisReportViewKind kind; AnalysisFilterSummary? filter; AnalysisReportBases? bases;
            if (folders is not null)
            {
                folderScope = await folders.CreateScopeAsync(parent, folders.GeometrySkipped, cancellation).ConfigureAwait(false);
                var folder = await folderScope.ReportParentAsync(parent, cancellation).ConfigureAwait(false);
                if (folder is not null && (folder.Path.Kind != AnalysisKind.Directory || !folder.IsVisible)) throw new InvalidOperationException("Choose a visible captured folder.");
                var first = await folderScope.ReadChildRowsAsync(parent, folderSort, 0, 1, cancellation, descending).ConfigureAwait(false); totalRows = first.Total;
                totals = folderScope.ReportIsSubset ? folderScope.Totals : folder?.Totals ?? folderScope.Totals;
                logical = folderScope.ReportIsSubset ? folderScope.LogicalTotals : folder?.Logical ?? folderScope.LogicalTotals;
                unknown = folderScope.ReportIsSubset ? folderScope.ReportUnknown : folder?.HasUnmeasuredEvidence ?? folderScope.ReportUnknown;
                logicalUnknown = folderScope.ReportIsSubset ? folderScope.ReportLogicalUnknown : folder?.HasUnmeasuredLogicalEvidence ?? folderScope.ReportLogicalUnknown;
                label = (folderScope.ReportIsSubset ? "Other small items in " : "") + (folder is null ? "All captured locations" : AnalysisNames.DisplayName(folder.Path));
                kind = folderScope.ReportIsSubset ? AnalysisReportViewKind.SmallItems : AnalysisReportViewKind.NamespaceRows;
                filter = folderScope.Filter; bases = folderScope.ReportBases;
            }
            else
            {
                group = group == AnalysisTree.RootKey ? null : group;
                groupScope = await groups!.CreateScopeAsync(group, groups.GeometrySkipped, cancellation).ConfigureAwait(false);
                var selected = group is null ? null : await groupScope.ReadGroupAsync(group, cancellation).ConfigureAwait(false);
                totalRows = group is null ? (await groupScope.ReadGroupsAsync(0, 1, cancellation, sort, descending).ConfigureAwait(false)).Total
                    : (await groupScope.ReadMemberRowsAsync(group, 0, 1, sort, descending, cancellation).ConfigureAwait(false)).Total;
                totals = groupScope.GeometrySkipped != 0 ? groupScope.Totals : selected?.Totals ?? groupScope.Totals;
                logical = groupScope.GeometrySkipped != 0 ? groupScope.LogicalTotals : selected?.MemberLogical ?? groupScope.LogicalTotals;
                references = selected?.Kind == AnalysisGroupKind.SharedAcrossGroups;
                unknown = totals.Missing.Count != 0 || !totals.CompatibleBasis || descriptor.Coverage != NamespaceCoverage.CompleteUnderPolicy;
                logicalUnknown = logical.UnknownFiles != 0;
                label = (groupScope.GeometrySkipped != 0 ? "Other small items in " : "") + (selected?.Definition.Label ?? "All " + AnalysisGroupingRules.Title(groupScope.Dimension) + " groups");
                kind = group is null ? groupScope.GeometrySkipped == 0 ? AnalysisReportViewKind.GroupOverview : AnalysisReportViewKind.SmallGroups
                    : groupScope.GeometrySkipped == 0 ? AnalysisReportViewKind.GroupMembers : AnalysisReportViewKind.SmallGroupMembers;
                filter = groupScope.Filter; bases = groupScope.ReportBases;
            }
            bool limited = format != AnalysisReportFormat.Csv && totalRows > AnalysisLimits.ChartLeaves;
            var request = filter?.Request;
            var header = new AnalysisReportHeader(folders?.Metric ?? groups!.Metric, AnalysisReportScope.FilteredView, descriptor.Identity.StartedUtc, descriptor.Identity.ObservedUtc,
                descriptor.Coverage, descriptor.Freshness, descriptor.Taint, totals, logical, label, limited ? AnalysisLimits.ChartLeaves : totalRows, limited)
            { ViewKind = kind, NamespaceRowsOnly = group is not null || folders is not null, IncludesAccountingReferences = groups is not null,
                HasUnmeasuredEvidence = unknown, HasUnmeasuredLogicalEvidence = logicalUnknown, MeasurementBases = bases, DetailedAllocation = descriptor.Policy.ComponentPreset == "detailed",
                TableOrder = folders is not null ? AnalysisReportOrder.Folder(folderSort, descending) : new(sort, descending), FilterTimeZone = descriptor.Identity.TimeZoneId,
                FilterContext = request is { IsEmpty: false } ? request.ReportContext() : null };
            var report = new AnalysisCapturedViewReport(retained, folderScope, groupScope, parent, group, sort, folderSort, descending, references, io, devices, header, format);
            retained = null; folderScope = null; groupScope = null; return report;
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            Exception? close = null;
            foreach (var owner in new IAsyncDisposable?[] { folderScope, groupScope, retained })
                try { if (owner is not null) await owner.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { close = close is null ? error : new AggregateException(close, error); }
            if (close is not null) throw AnalysisCloseUnconfirmedException.From(failure is null ? close : new AggregateException(failure, close));
        }
    }
    protected override async IAsyncEnumerable<AnalysisReportRow> RowsAsync(bool locations, [EnumeratorCancellation] CancellationToken cancellation)
    {
        ulong offset = 0;
        while (offset < Header.Rows)
        {
            ulong next;
            if (_folders is not null)
            {
                var page = await _folders.ReadChildRowsAsync(_parent, _folderSort, offset, cancellation: cancellation, descending: _descending).ConfigureAwait(false); next = page.NextOffset;
                foreach (var row in page.Rows.Take((int)Math.Min(Header.Rows - offset, (ulong)page.Rows.Length)))
                    yield return new(row.Path, locations ? await LocationAsync(row.Path, cancellation).ConfigureAwait(false) : null, row.Totals, row.Logical, row.NamespaceIncomplete, row.HasChargesElsewhere, row.IsFilterContext)
                    { HasUnmeasuredEvidence = row.HasUnmeasuredEvidence, HasUnmeasuredLogicalEvidence = row.HasUnmeasuredLogicalEvidence };
            }
            else if (_group is null)
            {
                var page = await _groups!.ReadGroupsAsync(offset, cancellation: cancellation, sort: _sort, descending: _descending).ConfigureAwait(false); next = page.NextOffset;
                foreach (var row in page.Rows.Take((int)Math.Min(Header.Rows - offset, (ulong)page.Rows.Length))) yield return AnalysisReportGroupRows.Group(row);
            }
            else
            {
                var page = await _groups!.ReadMemberRowsAsync(_group, offset, sort: _sort, descending: _descending, cancellation: cancellation).ConfigureAwait(false); next = page.NextOffset;
                foreach (var row in page.Rows.Take((int)Math.Min(Header.Rows - offset, (ulong)page.Rows.Length)))
                    yield return AnalysisReportGroupRows.Member(row, locations ? await LocationAsync(row.Member.Path, cancellation).ConfigureAwait(false) : null, _references);
            }
            if (next <= offset) throw new InvalidDataException("The captured view report made no progress."); offset = next;
        }
    }
    private async Task<string> LocationAsync(AnalysisPathMetadata path, CancellationToken cancellation)
    {
        var names = new List<string>(); var current = path; int length = 0;
        for (int depth = 0; current.ParentId != 0; depth++)
        {
            if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("The report lineage exceeds its bound.");
            string name = AnalysisNames.DisplayName(current); length = checked(length + name.Length + 1);
            if (length > AnalysisLimits.LocatorBytes) return "[Displayed location exceeds the report bound; use the full inventory for raw names.]";
            names.Add(name); current = await _source.ReadPathAsync(current.ParentId, cancellation).ConfigureAwait(false);
            if (current.RootId != path.RootId) throw new InvalidDataException("The captured report lineage changed roots.");
        }
        names.Reverse(); return AnalysisReportLocations.Append(_source.Descriptor.Roots.Single(root => root.Id == path.RootId), names);
    }
    protected override async ValueTask ReleaseAsync()
    {
        Exception? close = null;
        foreach (var owner in new IAsyncDisposable?[] { _folders, _groups, _source })
            try { if (owner is not null) await owner.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { close = close is null ? error : new AggregateException(close, error); }
        if (close is not null) throw AnalysisCloseUnconfirmedException.From(close);
    }
}

internal static class AnalysisReportGroupRows
{
    internal static AnalysisReportRow Group(AnalysisDatasetGroup group) => new(
        new(0, "", 0, group.Definition.Label, AnalysisKind.Special, null, null, null, null, MeasurementState.Known, true, "utf8", [], null, null),
        null, group.Totals, group.MemberLogical, false, false)
    { IsAccountingGroup = true, IsAccountingReference = group.Kind == AnalysisGroupKind.SharedAcrossGroups, LogicalNotApplicable = group.Kind == AnalysisGroupKind.DirectoryAllocation,
        HasUnmeasuredEvidence = group.Totals.Missing.Count != 0, HasUnmeasuredLogicalEvidence = group.MemberLogical.UnknownFiles != 0 };
    internal static AnalysisReportRow Member(AnalysisDatasetGroupRow row, string? location, bool reference) => new(row.Member.Path, location, row.Assigned,
        Logical(row.Member), row.Member.Path.MetadataState is not (MeasurementState.Known or MeasurementState.Excluded), row.SharedElsewhere)
    { IsAccountingReference = reference, LogicalNotApplicable = row.Member.Path.Kind != AnalysisKind.File,
        HasUnmeasuredEvidence = row.Assigned.Missing.Count != 0, HasUnmeasuredLogicalEvidence = row.Member.Path.Kind == AnalysisKind.File && !row.Member.LogicalLength.IsKnown };
    internal static AnalysisLogicalTotals Logical(AnalysisDatasetGroupMember member) => member.Path.Kind != AnalysisKind.File
        ? new(0, 0, ImmutableDictionary<MeasurementState, ulong>.Empty)
        : new(member.LogicalLength.Value ?? 0, 1, member.LogicalLength.IsKnown ? ImmutableDictionary<MeasurementState, ulong>.Empty : ImmutableDictionary<MeasurementState, ulong>.Empty.Add(member.LogicalLength.State, 1));
}

public sealed partial class AnalysisDatasetLease
{
    public Task<AnalysisCapturedViewReport> CaptureViewReportAsync(AnalysisDatasetExplorer folders, ulong parent, AnalysisReportFormat format,
        AnalysisHierarchySort sort = AnalysisHierarchySort.Metric, bool descending = true, CancellationToken cancellation = default) =>
        AnalysisCapturedViewReport.CreateAsync(this, _io, _device, folders, null, parent, null, format, AnalysisTableSort.ActiveMetric, sort, descending, cancellation);
    public Task<AnalysisCapturedViewReport> CaptureViewReportAsync(AnalysisDatasetGroupExplorer groups, string? group, AnalysisReportFormat format,
        AnalysisTableSort sort = AnalysisTableSort.ActiveMetric, bool descending = true, CancellationToken cancellation = default) =>
        AnalysisCapturedViewReport.CreateAsync(this, _io, _device, null, groups, 0, group, format, sort, AnalysisHierarchySort.Metric, descending, cancellation);
}
