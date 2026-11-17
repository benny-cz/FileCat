using System.Globalization;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    internal Task<AnalysisCapturedIndexedComparisonReport> CaptureReportAsync(AnalysisComparisonView view, AnalysisReportFormat format,
        Query? applied = null, bool entire = false, CancellationToken cancellation = default) =>
        AnalysisCapturedIndexedComparisonReport.CreateAsync(this, _io, _devices, view, format, applied, entire, cancellation);
}

/// <summary>Paged immutable accounting rows; neither aliases nor complete comparison inputs become arrays.</summary>
internal sealed class AnalysisCapturedIndexedComparisonReport : AnalysisCapturedOutput
{
    private readonly AnalysisIndexedComparison _summary;
    private AnalysisIndexedComparison.ReadLease? _reader;
    private AnalysisIndexedComparison.Query? _query;
    private readonly AnalysisDatasetDescriptor _previous, _current;
    private long _reservation;
    internal AnalysisComparisonView View { get; }
    internal AnalysisReportFormat Format { get; }
    internal AnalysisReportScope Scope { get; }
    internal ulong MatchingRows { get; }
    internal ulong Rows { get; }
    internal bool DetailLimited => Rows != MatchingRows;
    internal AnalysisTaint Taint { get; }

    private AnalysisCapturedIndexedComparisonReport(AnalysisIndexedComparison summary, DeviceIoScheduler io, AnalysisIoDevices devices,
        AnalysisIndexedComparison.ReadLease? reader, AnalysisIndexedComparison.Query? query, AnalysisComparisonView view,
        AnalysisReportFormat format, bool entire, long reservation) : base(io, devices)
    {
        _summary = summary; _reader = reader; _query = query; View = view; Format = format; Taint = summary.Taint;
        Scope = entire ? AnalysisReportScope.EntireResult : AnalysisReportScope.FilteredView;
        _previous = query?.Descriptor(true) ?? reader!.Descriptor(true); _current = query?.Descriptor(false) ?? reader!.Descriptor(false);
        MatchingRows = query?.Count ?? summary.Count; Rows = format == AnalysisReportFormat.Csv ? MatchingRows : Math.Min(MatchingRows, (ulong)AnalysisLimits.ChartLeaves);
        _reservation = reservation;
    }
    internal static async Task<AnalysisCapturedIndexedComparisonReport> CreateAsync(AnalysisIndexedComparison comparison, DeviceIoScheduler io,
        AnalysisIoDevices devices, AnalysisComparisonView view, AnalysisReportFormat format, AnalysisIndexedComparison.Query? applied,
        bool entire, CancellationToken cancellation)
    {
        AnalysisComparisonView.Validate(view);
        if (!Enum.IsDefined(format) || comparison.Metric != view.Metric) throw new ArgumentException("Choose the applied comparison metric and a supported report format.");
        if (applied is not null && (!applied.BelongsTo(comparison) || !SameSelection(applied.View, view)))
            throw new InvalidOperationException("The report must retain the exact applied comparison view.");
        if (entire) view = view with { Cohort = AnalysisComparisonCohort.All, Query = "", ShowUnknown = false, QuerySide = AnalysisComparisonSide.CurrentOrPrevious };
        AnalysisIndexedComparison.ReadLease? reader = null; AnalysisIndexedComparison.Query? query = null; long reservation = 0;
        try
        {
            cancellation.ThrowIfCancellationRequested(); AnalysisInventoryFormat.Reserve(8L * 1024 * 1024, ref reservation);
            if (!entire && applied is not null) query = await applied.RetainAsync(cancellation).ConfigureAwait(false);
            else if (!string.IsNullOrWhiteSpace(view.Query) || view.Cohort != AnalysisComparisonCohort.All || view.Order != AnalysisComparisonOrder.AbsoluteDifference || view.QuerySide != AnalysisComparisonSide.CurrentOrPrevious || view.ShowUnknown)
                query = await comparison.QueryAsync(view, cancellation: cancellation).ConfigureAwait(false);
            else reader = comparison.Retain();
            cancellation.ThrowIfCancellationRequested();
            var result = new AnalysisCapturedIndexedComparisonReport(comparison, io, devices, reader, query, view, format, entire, reservation);
            reader = null; query = null; reservation = 0; return result;
        }
        finally
        {
            try { await AnalysisIndexedComparison.CloseAsync(query, reader).ConfigureAwait(false); }
            finally { AnalysisInventoryFormat.Release(reservation); }
        }
    }
    private static bool SameSelection(AnalysisComparisonView a, AnalysisComparisonView b) =>
        (a.Metric, a.Cohort, a.Order, a.Query, a.QuerySide, a.CaseSensitive, a.ShowUnknown) ==
        (b.Metric, b.Cohort, b.Order, b.Query, b.QuerySide, b.CaseSensitive, b.ShowUnknown);
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
    private Task<AnalysisDatasetPage<AnalysisIndexedComparisonRow>> PageAsync(ulong offset, CancellationToken cancellation) =>
        _query is not null ? _query.ReadAsync(offset, cancellation: cancellation) : _reader!.ReadAsync(offset, cancellation: cancellation);
    private Task<AnalysisDatasetPage<AnalysisPathMetadata>> MembersAsync(bool previous, ulong entity, ulong offset, CancellationToken cancellation) =>
        _query is not null ? _query.ReadMembersAsync(previous, entity, offset, 1, cancellation) : _reader!.ReadMembersAsync(previous, entity, offset, 1, cancellation);
    private Task<AnalysisPathMetadata> PathAsync(bool previous, ulong path, CancellationToken cancellation) =>
        _query is not null ? _query.ReadPathAsync(previous, path, cancellation) : _reader!.ReadPathAsync(previous, path, cancellation);

    private async Task WriteAsync(Stream output, bool redact, CancellationToken cancellation)
    {
        using var writer = new StreamWriter(output, new UTF8Encoding(false, true), 64 * 1024, leaveOpen: true);
        var before = redact ? null : new AnalysisReportLineage(_previous, (path, token) => PathAsync(true, path, token));
        var now = redact ? null : new AnalysisReportLineage(_current, (path, token) => PathAsync(false, path, token));
        string scope = Scope == AnalysisReportScope.EntireResult ? "Entire unfiltered comparison" : "Captured filtered comparison";
        var compatible = _summary.Compatibility;
        string context = $"{scope}; {AnalysisHumanReport.MetricName(View.Metric)}. {AnalysisHumanReport.MetricDefinition(View.Metric)} " +
            $"Previous observations UTC: {Interval(_previous.Identity)}; current observations UTC: {Interval(_current.Identity)}. " +
            $"Same policy: {compatible.SamePolicy}; same basis: {compatible.SameBasis}; same scope: {compatible.SameScope}; verified identity context: {compatible.VerifiedIdentityContext}. " +
            $"Previous coverage: {_previous.Coverage}; freshness: {_previous.Freshness}. Current coverage: {_current.Coverage}; freshness: {_current.Freshness}. " +
            $"Full input totals (not sums of displayed rows): previous {Totals(_summary.PreviousTotals)}; current {Totals(_summary.CurrentTotals)}; net observed change {Delta(_summary.NetChange)}. " +
            $"Cohort: {View.Cohort}; order: {View.Order}; query side: {View.QuerySide}; case sensitive: {View.CaseSensitive}; uncertain query cohort: {View.ShowUnknown}. " +
            "Query: " + (redact ? "omitted" : AnalysisHumanReport.Safe(View.Query.Length == 0 ? "none" : View.Query)) + ". " +
            $"Declared item rows: {Rows}; matching accounting rows: {MatchingRows}. " +
            (DetailLimited ? "Detail is limited to the first 5,000 ranked rows; choose CSV for all accounting rows. " : "") +
            (redact ? "Names, paths, source identifiers and query text are omitted throughout. " : "Sensitive displayed paths are included; exact raw values remain in machine inventories. ") +
            (Taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? "Contains privileged-derived observations. " : "") +
            (Taint.HasFlag(AnalysisTaint.ImportedUnverified) ? "Contains imported, unverified observations. " : "") +
            "Shared aliases stay in one accounting row. Attribution movement is not new consumption or freed storage. Unknown values are not zero; unchanged metadata does not prove unchanged contents. " +
            "Paths are escaped display text and may be abbreviated. Each path cell has a bounded display size; use machine inventories for complete raw names and aliases. No separate insight results or file contents are included. Reports do not authenticate sources or grant file actions.";
        string[] headings = ["Change", "Previous paths", "Current paths", "Previous value / state", "Current value / state", "Signed difference", "Relative change", "Evidence", "Notes"];
        if (Format == AnalysisReportFormat.Csv)
        {
            await Line(Csv(["row_kind", "format_version", "export_scope", "metric", "change", "previous_paths", "current_paths", "previous_value", "previous_state", "current_value", "current_state", "signed_delta", "relative_percent", "comparison_evidence", "previous_observed_utc", "current_observed_utc", "paths_redacted", "spreadsheet_safe", "declared_item_rows", "notes"]));
            await Line(Csv(Record(null, "", "", context)));
        }
        else if (Format == AnalysisReportFormat.Html)
            await Line("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>FileCat comparison report</title><style>body{font:1rem system-ui,sans-serif;margin:2rem;color:#171717;background:#fff}table{border-collapse:collapse}td,th{border:1px solid #777;padding:.5rem;overflow-wrap:anywhere}</style></head><body><h1>FileCat comparison report</h1><p>" + AnalysisHumanReport.Html(context) + "</p><table><thead><tr>" + string.Concat(headings.Select(value => "<th>" + AnalysisHumanReport.Html(value) + "</th>")) + "</tr></thead><tbody>");
        else await Line("# FileCat comparison report\n\n" + AnalysisHumanReport.Markdown(context) + "\n\n| " + string.Join(" | ", headings) + " |\n| " + string.Join(" | ", headings.Select(_ => "---")) + " |");
        ulong written = 0;
        while (written < Rows)
        {
            cancellation.ThrowIfCancellationRequested(); var page = await PageAsync(written, cancellation).ConfigureAwait(false);
            if (page.Rows.Length == 0 || page.Total != MatchingRows) throw new InvalidDataException("The captured comparison report page did not advance.");
            foreach (var row in page.Rows)
            {
                if (written == Rows) break; cancellation.ThrowIfCancellationRequested();
                string oldPaths = await PathsAsync(row.Previous, true, before, cancellation).ConfigureAwait(false);
                string newPaths = await PathsAsync(row.Current, false, now, cancellation).ConfigureAwait(false);
                string note = redact ? "Recorded identities and detailed source explanations omitted." : AnalysisHumanReport.Safe(row.Explanation);
                if (Format == AnalysisReportFormat.Csv) await Line(Csv(Record(row, oldPaths, newPaths, note)));
                else
                {
                    string[] cells = [Change(row.Change), oldPaths, newPaths, Value(row.Previous), Value(row.Current), Delta(row.Delta), Relative(row), row.Evidence.ToString(), note];
                    await Line(Format == AnalysisReportFormat.Html ? "<tr>" + string.Concat(cells.Select(value => "<td>" + AnalysisHumanReport.Html(value) + "</td>")) + "</tr>"
                        : "| " + string.Join(" | ", cells.Select(AnalysisHumanReport.Markdown)) + " |");
                }
                written++;
            }
        }
        if (Format == AnalysisReportFormat.Html) await Line("</tbody></table></body></html>");
        await writer.FlushAsync(cancellation).ConfigureAwait(false);
        Task Line(string text) => writer.WriteLineAsync(text.AsMemory(), cancellation);
        string[] Record(AnalysisIndexedComparisonRow? row, string oldPaths, string newPaths, string notes) => [row is null ? "metadata" : "item", "1", scope, AnalysisHumanReport.MetricName(View.Metric), row is null ? "" : Change(row.Change),
            oldPaths, newPaths, Number(row?.Previous), row?.Previous?.Value.State.ToString() ?? "Not observed", Number(row?.Current), row?.Current?.Value.State.ToString() ?? "Not observed",
            row is null ? Delta(_summary.NetChange) : Delta(row.Delta), row is null ? "" : Relative(row), row?.Evidence.ToString() ?? "", Interval(_previous.Identity), Interval(_current.Identity), redact ? "true" : "false", "true", Rows.ToString(CultureInfo.InvariantCulture), notes];
    }
    private async Task<string> PathsAsync(AnalysisComparisonAccountingEntity? entity, bool previous, AnalysisReportLineage? lineage, CancellationToken cancellation)
    {
        if (entity is null || lineage is null) return "";
        const int limit = 16 * 1024; var text = new StringBuilder(); ulong shown = 0;
        while (shown < entity.ObservedPaths && shown < 32)
        {
            var page = await MembersAsync(previous, entity.Id, shown, cancellation).ConfigureAwait(false);
            if (page.Total != entity.ObservedPaths || page.Rows.Length != 1) throw new InvalidDataException("The captured comparison aliases changed.");
            string path = AnalysisHumanReport.Safe(await lineage.ResolveAsync(page.Rows[0], cancellation).ConfigureAwait(false));
            if (path.Length + 2 > limit - text.Length) break;
            if (shown != 0) text.Append("; "); text.Append(path); shown++;
        }
        if (shown != entity.ObservedPaths) text.Append(" [first ").Append(shown.ToString(CultureInfo.InvariantCulture)).Append(" of ").Append(entity.ObservedPaths.ToString(CultureInfo.InvariantCulture)).Append(" displayed aliases; use machine inventories for complete raw names]");
        return text.ToString();
    }
    private static string Csv(IEnumerable<string> values) => string.Join(',', values.Select(AnalysisHumanReport.CsvCell));
    private static string Interval(SessionIdentity value) => value.StartedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) + " – " + value.ObservedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Number(AnalysisComparisonAccountingEntity? value) => value?.Value.IsKnown == true ? value.Value.Value!.Value.ToString(CultureInfo.InvariantCulture) : "";
    private static string Value(AnalysisComparisonAccountingEntity? value) => value is null ? "Not observed" : value.Value.IsKnown ? Number(value) : value.Value.State.ToString();
    private static string Delta(AnalysisDelta? delta) => delta is not { } d ? "Unknown" : (d.Magnitude == 0 ? "" : d.Negative ? "-" : "+") + d.Magnitude.ToString(CultureInfo.InvariantCulture);
    private static string Relative(AnalysisIndexedComparisonRow row) => row.Delta is null ? "Unknown" : row.RelativePercent is { } value ? value.ToString("G17", CultureInfo.InvariantCulture) + "%" : "New / zero baseline";
    private static string Change(AnalysisChange change) => change switch { AnalysisChange.NoObservedMetadataChange => "No observed metadata change", AnalysisChange.PreviouslyUnknown => "Previously unknown", AnalysisChange.Attribution => "Attribution movement", _ => change.ToString() };
    private static string Totals(AnalysisTotals value) => (value.CompatibleBasis ? value.Known.ToString(CultureInfo.InvariantCulture) + " known; " + value.IdentityUnverified.ToString(CultureInfo.InvariantCulture) + " identity-unverified" : "Incomparable bases") +
        "; missing " + string.Join(", ", value.Missing.OrderBy(pair => pair.Key).Select(pair => pair.Key + ": " + pair.Value.ToString(CultureInfo.InvariantCulture)));
    protected override async ValueTask ReleaseAsync()
    {
        var query = _query; var reader = _reader; _query = null; _reader = null;
        try { await AnalysisIndexedComparison.CloseAsync(query, reader).ConfigureAwait(false); }
        finally { AnalysisInventoryFormat.Release(Interlocked.Exchange(ref _reservation, 0)); }
    }
}
