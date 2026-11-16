using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Independent captured accounting rows and both immutable inputs, with no source or operation authority.</summary>
public sealed class AnalysisCapturedComparisonReport : AnalysisCapturedOutput
{
    private AnalysisSnapshotLease? _previous, _current;
    private AnalysisComparisonResult.ReadLease? _owner;
    private ImmutableArray<AnalysisComparisonRow> _rows;
    private long _reservation;
    private readonly AnalysisComparisonResult _summary;
    public AnalysisComparisonView View { get; }
    public AnalysisReportFormat Format { get; }
    public AnalysisReportScope Scope { get; }
    public int Rows => _rows.Length;
    public int MatchingRows { get; }
    public bool DetailLimited => Rows != MatchingRows;

    private AnalysisCapturedComparisonReport(DeviceIoScheduler io, AnalysisSnapshotLease previous, AnalysisSnapshotLease current,
        AnalysisComparisonResult.ReadLease owner, AnalysisComparisonResult summary, ImmutableArray<AnalysisComparisonRow> rows,
        AnalysisComparisonView view, AnalysisReportFormat format, bool entire, int matching, long reservation) : base(io, null)
    {
        _previous = previous; _current = current; _owner = owner; _summary = summary; _rows = rows; View = view; Format = format;
        Scope = entire ? AnalysisReportScope.EntireResult : AnalysisReportScope.FilteredView; MatchingRows = matching; _reservation = reservation;
    }

    public static async Task<AnalysisCapturedComparisonReport> CaptureAsync(DeviceIoScheduler io, AnalysisSnapshotLease previous,
        AnalysisSnapshotLease current, AnalysisComparisonResult comparison, AnalysisComparisonView view, AnalysisReportFormat format,
        bool entire = false, CancellationToken cancellation = default,
        IReadOnlyDictionary<string, string>? previousRootDisplayIds = null, IReadOnlyDictionary<string, string>? currentRootDisplayIds = null)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(comparison); AnalysisComparisonView.Validate(view);
        if (!Enum.IsDefined(format) || comparison.Metric != view.Metric) throw new ArgumentException("Choose the applied comparison metric and a supported report format.");
        AnalysisSnapshotLease? before = null, now = null; AnalysisComparisonResult.ReadLease? rows = null; long reservation = 0;
        try
        {
            before = previous.Retain(); now = current.Retain(); rows = comparison.Retain();
            if (rows.Identity(true) != before.Snapshot.Identity || rows.Identity(false) != now.Snapshot.Identity)
                throw new InvalidOperationException("Comparison report inputs must match the captured accounting revision.");
            AnalysisInventoryFormat.Reserve(checked(256 * 1024L + rows.Rows.Length * 32L), ref reservation);
            if (entire) view = view with { Cohort = AnalysisComparisonCohort.All, Query = "", ShowUnknown = false };
            var oldSnapshot = before.Snapshot; var currentSnapshot = now.Snapshot; var held = rows;
            var captured = await Task.Run(() =>
            {
                using var queryLease = string.IsNullOrWhiteSpace(view.Query) ? null : held.Retain();
                using var query = queryLease is null ? null : AnalysisComparisonQuery.Apply(oldSnapshot, currentSnapshot,
                    view.Metric, queryLease, view.Query, view.CaseSensitive, view.QuerySide, cancellation, previousRootDisplayIds, currentRootDisplayIds);
                IEnumerable<AnalysisComparisonRow> selected = query is null ? held.Rows : view.ShowUnknown ? query.Unknown : query.Matches;
                var matching = AnalysisComparisonProjection.Apply(selected, view.Cohort, view.Order).Select(row =>
                { cancellation.ThrowIfCancellationRequested(); return row; }).ToImmutableArray();
                return (Rows: format == AnalysisReportFormat.Csv ? matching : matching.Take(AnalysisLimits.ChartLeaves).ToImmutableArray(), Count: matching.Length);
            }, CancellationToken.None).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var result = new AnalysisCapturedComparisonReport(io, before, now, rows, comparison, captured.Rows, view, format, entire, captured.Count, reservation);
            before = now = null; rows = null; reservation = 0; return result;
        }
        finally { try { rows?.Dispose(); } finally { try { before?.Dispose(); } finally { now?.Dispose(); AnalysisInventoryFormat.Release(reservation); } } }
    }

    protected override Task<AnalysisExportReceipt> ExportCoreAsync(string name, AnalysisIoDevices devices,
        Func<AnalysisExportDestination> createDestination, bool redactPaths, CancellationToken cancellation)
    {
        string extension = Format switch { AnalysisReportFormat.Markdown => ".md", AnalysisReportFormat.Html => ".html", _ => ".csv" };
        AnalysisExportDestination.ValidateName(name);
        if (!name.EndsWith(extension, StringComparison.Ordinal)) throw new ArgumentException("The comparison report format and filename extension differ.");
        return AnalysisInventoryFormat.ExportNewAsync(Io, devices, name, createDestination,
            (sink, token) => AnalysisInventoryFormat.WritePreparedPipeAsync(sink, Io, devices,
                (stream, stop) => WriteAsync(stream, redactPaths, stop), token), cancellation);
    }

    private async Task WriteAsync(Stream output, bool redact, CancellationToken cancellation)
    {
        var previous = _previous!.Snapshot; var current = _current!.Snapshot;
        using var writer = new StreamWriter(output, new UTF8Encoding(false, true), 64 * 1024, leaveOpen: true);
        string scope = Scope == AnalysisReportScope.EntireResult ? "Entire unfiltered comparison" : "Captured filtered comparison";
        var compatible = _summary.Compatibility;
        string context = $"{scope}; {AnalysisHumanReport.MetricName(View.Metric)}. {AnalysisHumanReport.MetricDefinition(View.Metric)} " +
            $"Previous observations UTC: {Interval(previous.Identity)}; current observations UTC: {Interval(current.Identity)}. " +
            $"Same policy: {compatible.SamePolicy}; same basis: {compatible.SameBasis}; same scope: {compatible.SameScope}; verified identity context: {compatible.VerifiedIdentityContext}. " +
            $"Previous coverage: {previous.Coverage}; freshness: {previous.Freshness}. Current coverage: {current.Coverage}; freshness: {current.Freshness}. " +
            $"Full input totals (not sums of displayed rows): previous {Totals(_summary.PreviousTotals)}; current {Totals(_summary.CurrentTotals)}; net observed change {Delta(_summary.NetChange)}. " +
            $"Cohort: {View.Cohort}; order: {View.Order}; query side: {View.QuerySide}; uncertain query cohort: {View.ShowUnknown}. " +
            "Query: " + (redact ? "omitted" : AnalysisHumanReport.Safe(View.Query.Length == 0 ? "none" : View.Query)) + ". " +
            $"Declared item rows: {Rows}; matching accounting rows: {MatchingRows}. " +
            (DetailLimited ? "Detail is limited to the first 5,000 ranked rows; choose full-result CSV for all accounting rows. " : "") +
            (redact ? "Names, paths, source identifiers and query text are omitted throughout. " : "Sensitive displayed paths are included; exact raw values remain in machine inventories. ") +
            (_owner!.Taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? "Contains privileged-derived observations. " : "") +
            (_owner.Taint.HasFlag(AnalysisTaint.ImportedUnverified) ? "Contains imported, unverified observations. " : "") +
            "Shared aliases stay in one accounting row. Attribution movement is not new consumption or freed storage. Unknown values are not zero; unchanged metadata does not prove unchanged contents. No separate insight results or file contents are included. Reports do not authenticate sources or grant file actions.";
        string[] headings = ["Change", "Previous paths", "Current paths", "Previous value / state", "Current value / state", "Signed difference", "Relative change", "Evidence", "Notes"];
        if (Format == AnalysisReportFormat.Csv)
        {
            await Line(Csv(["row_kind", "format_version", "export_scope", "metric", "change", "previous_paths", "current_paths", "previous_value", "previous_state", "current_value", "current_state", "signed_delta", "relative_percent", "comparison_evidence", "previous_observed_utc", "current_observed_utc", "paths_redacted", "spreadsheet_safe", "declared_item_rows", "notes"]));
            await Line(Csv(Record("metadata", null, context)));
        }
        else if (Format == AnalysisReportFormat.Html)
        {
            await Line("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>FileCat comparison report</title><style>body{font:1rem system-ui,sans-serif;margin:2rem;color:#171717;background:#fff}table{border-collapse:collapse}td,th{border:1px solid #777;padding:.5rem;overflow-wrap:anywhere}</style></head><body><h1>FileCat comparison report</h1><p>" + AnalysisHumanReport.Html(context) + "</p><table><thead><tr>" + string.Concat(headings.Select(value => "<th>" + AnalysisHumanReport.Html(value) + "</th>")) + "</tr></thead><tbody>");
        }
        else await Line("# FileCat comparison report\n\n" + AnalysisHumanReport.Markdown(context) + "\n\n| " + string.Join(" | ", headings) + " |\n| " + string.Join(" | ", headings.Select(_ => "---")) + " |");
        foreach (var row in _rows)
        {
            cancellation.ThrowIfCancellationRequested();
            string note = redact ? "Recorded identities and detailed source explanations omitted." : AnalysisHumanReport.Safe(row.Explanation);
            if (Format == AnalysisReportFormat.Csv) await Line(Csv(Record("item", row, note)));
            else
            {
                string[] cells = [Change(row.Change), Paths(row.Previous, redact), Paths(row.Current, redact), Value(row.Previous), Value(row.Current), Delta(row.Delta), Relative(row), row.Evidence.ToString(), note];
                await Line(Format == AnalysisReportFormat.Html ? "<tr>" + string.Concat(cells.Select(value => "<td>" + AnalysisHumanReport.Html(value) + "</td>")) + "</tr>"
                    : "| " + string.Join(" | ", cells.Select(AnalysisHumanReport.Markdown)) + " |");
            }
        }
        if (Format == AnalysisReportFormat.Html) await Line("</tbody></table></body></html>");
        await writer.FlushAsync(cancellation).ConfigureAwait(false);
        Task Line(string value) => writer.WriteLineAsync(value.AsMemory(), cancellation);
        string[] Record(string kind, AnalysisComparisonRow? row, string notes) => [kind, "1", scope, AnalysisHumanReport.MetricName(View.Metric), row is null ? "" : Change(row.Change),
            Paths(row?.Previous, redact), Paths(row?.Current, redact), Number(row?.Previous), row?.Previous?.Value.State.ToString() ?? "Not observed", Number(row?.Current), row?.Current?.Value.State.ToString() ?? "Not observed",
            row is null ? Delta(_summary.NetChange) : Delta(row.Delta), row is null ? "" : Relative(row), row?.Evidence.ToString() ?? "", Interval(previous.Identity), Interval(current.Identity), redact ? "true" : "false", "true", Rows.ToString(CultureInfo.InvariantCulture), notes];
    }
    private static string Csv(IEnumerable<string> values) => string.Join(',', values.Select(AnalysisHumanReport.CsvCell));
    private static string Interval(SessionIdentity value) => value.StartedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + " – " + value.ObservedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Number(AnalysisComparisonEntity? value) => value?.Value.IsKnown == true ? value.Value.Value!.Value.ToString(CultureInfo.InvariantCulture) : "";
    private static string Value(AnalysisComparisonEntity? value) => value is null ? "Not observed" : value.Value.IsKnown ? Number(value) : value.Value.State.ToString();
    private static string Delta(AnalysisDelta? delta) => delta is not { } d ? "Unknown" : (d.Magnitude == 0 ? "" : d.Negative ? "-" : "+") + d.Magnitude.ToString(CultureInfo.InvariantCulture);
    private static string Relative(AnalysisComparisonRow row) => row.Delta is null ? "Unknown" : row.RelativePercent is { } value ? value.ToString("G17", CultureInfo.InvariantCulture) + "%" : "New / zero baseline";
    private static string Change(AnalysisChange change) => change switch { AnalysisChange.NoObservedMetadataChange => "No observed metadata change", AnalysisChange.PreviouslyUnknown => "Previously unknown", AnalysisChange.Attribution => "Attribution movement", _ => change.ToString() };
    private static string Totals(AnalysisTotals value) => (value.CompatibleBasis ? value.Known.ToString(CultureInfo.InvariantCulture) + " known; " + value.IdentityUnverified.ToString(CultureInfo.InvariantCulture) + " identity-unverified" : "Incomparable bases") +
        "; missing " + string.Join(", ", value.Missing.OrderBy(pair => pair.Key).Select(pair => pair.Key + ": " + pair.Value.ToString(CultureInfo.InvariantCulture)));
    private static string Paths(AnalysisComparisonEntity? entity, bool redact) => redact || entity is null ? "" : string.Join("; ", entity.Paths.Take(32).Select(AnalysisHumanReport.Safe)) +
        (entity.Paths.Length > 32 ? $" [first 32 of {entity.Paths.Length.ToString(CultureInfo.InvariantCulture)} displayed aliases; use the machine inventory for all raw names]" : "");
    protected override ValueTask ReleaseAsync()
    {
        var failures = new List<Exception>();
        try { _owner?.Dispose(); } catch (Exception error) { failures.Add(error); }
        try { _previous?.Dispose(); } catch (Exception error) { failures.Add(error); }
        try { _current?.Dispose(); } catch (Exception error) { failures.Add(error); }
        _rows = []; _previous = _current = null; _owner = null; AnalysisInventoryFormat.Release(Interlocked.Exchange(ref _reservation, 0));
        if (failures.Count != 0) throw AnalysisCloseUnconfirmedException.From(new AggregateException(failures));
        return ValueTask.CompletedTask;
    }
}
