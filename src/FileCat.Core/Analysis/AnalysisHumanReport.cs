using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Text;

namespace FileCat.Core.Analysis;

public enum AnalysisReportFormat { Markdown, Html, Csv }
public enum AnalysisReportScope { FilteredView, SelectedRows, EntireResult }

/// <summary>Captured accounting, never recomputed from the display rows and never a source capability.</summary>
public sealed record AnalysisReportHeader(AnalysisMetric Metric, AnalysisReportScope Scope, DateTimeOffset StartedUtc, DateTimeOffset ObservedUtc,
    NamespaceCoverage Coverage, AnalysisFreshness Freshness, AnalysisTaint Taint, AnalysisTotals Totals, AnalysisLogicalTotals Logical,
    string ScopeLabel, ulong Rows, bool DetailLimited = false)
{
    public bool SummaryDescribesSourceScope { get; init; }
    public bool NamespaceRowsOnly { get; init; }
    public bool HasUnmeasuredEvidence { get; init; }
    public bool HasUnmeasuredLogicalEvidence { get; init; }
    public AnalysisReportFilterContext? FilterContext { get; init; }
    public AnalysisReportBases? MeasurementBases { get; init; }
    public bool DetailedAllocation { get; init; }
}
public sealed record AnalysisReportRow(AnalysisPathMetadata Path, string? DisplayLocation, AnalysisTotals Totals, AnalysisLogicalTotals Logical,
    bool NamespaceIncomplete, bool HasChargesElsewhere, bool IsFilterContext = false)
{
    public bool IsAccountingGroup { get; init; }
    public bool HasUnmeasuredEvidence { get; init; }
    public bool HasUnmeasuredLogicalEvidence { get; init; }
}

/// <summary>Inert streamed human output. Redaction omits all source names, locators and identifiers, including hidden metadata.</summary>
public static partial class AnalysisHumanReport
{
    public static async Task WriteAsync(Stream output, AnalysisReportHeader header, IAsyncEnumerable<AnalysisReportRow> rows,
        AnalysisReportFormat format, bool redactPaths, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(rows);
        if (!Enum.IsDefined(format) || !Enum.IsDefined(header.Metric) || !Enum.IsDefined(header.Scope) || !Enum.IsDefined(header.Coverage) ||
            !Enum.IsDefined(header.Freshness) || header.ObservedUtc < header.StartedUtc || header.Rows > AnalysisLimits.Observations ||
            header.Taint == 0 || (header.Taint & ~AnalysisPrivacy.All) != 0 ||
            format == AnalysisReportFormat.Csv && header.DetailLimited ||
            format != AnalysisReportFormat.Csv && header.Rows > AnalysisLimits.ChartLeaves || header.ScopeLabel.Length > AnalysisLimits.LocatorBytes)
            throw new InvalidDataException("The captured report exceeds its format or detail limits. Choose fewer human-report rows or a full CSV export.");
        if (header.FilterContext is { } filterContext && (filterContext.Query is null || filterContext.Query.Length > AnalysisLimits.QueryBytes) ||
            header.MeasurementBases is { } bases && (bases.Labels.IsDefault || bases.Labels.Length > AnalysisLimits.Components || bases.Labels.Any(label => label is null || label.Length > 256) || bases.Labels.Sum(label => label.Length) > 65536))
            throw new InvalidDataException("The captured report context exceeds its bounds.");
        using var writer = new StreamWriter(output, new UTF8Encoding(false, true), 64 * 1024, leaveOpen: true);
        string metric = MetricName(header.Metric), scope = ScopeName(header.Scope);
        string label = redactPaths ? "Captured scope" : Safe(header.ScopeLabel);
        string observed = header.StartedUtc.ToUniversalTime().ToString("u") + " – " + header.ObservedUtc.ToUniversalTime().ToString("u");
        string coverage = header.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "Traversal complete under captured policy" : "PARTIAL OBSERVATIONS — not a complete inventory";
        string privacy = (redactPaths ? "Names, locators and source identifiers omitted." : "Sensitive displayed locations included; raw identities and names are available only in a full .fcdsa inventory.") +
            (header.Taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? " Includes privileged-derived observations." : "");
        string accounting = "Folder rollups overlap: do not sum parent and child rows. Shared allocations and charges elsewhere are not exclusive reclaimable space. Unknown measurements are not zero." +
            (header.SummaryDescribesSourceScope ? " The summary describes the captured source scope; selected rows are not added together." : "") +
            (header.NamespaceRowsOnly ? " The table contains real namespace rows. Synthetic accounting buckets are omitted; their charges remain in the source summary." : "");
        const string fidelity = "Display text is escaped and may be abbreviated. Reports contain observations, not authenticated provenance or permission to access recorded sources.";
        string context = Context(header, redactPaths);
        if (format == AnalysisReportFormat.Csv)
        {
            await Line(Csv(["row_kind", "format_version", "export_scope", "metric", "item", "display_location", "entry_kind", "active_known", "identity_unverified", "active_missing", "basis_comparable",
                "file_size_known_bytes", "file_size_missing", "file_paths", "namespace_incomplete", "charges_elsewhere", "filter_context", "coverage", "observed_utc", "paths_redacted", "spreadsheet_safe", "declared_item_rows", "notes"]));
            await Line(CsvRow("metadata", label, "", "", header.Totals, header.Logical, header.Coverage != NamespaceCoverage.CompleteUnderPolicy, false, false,
                MetricDefinition(header.Metric) + " " + context + " " + privacy + " " + accounting + " " + fidelity + " Formula-leading display text is prefixed with an apostrophe."));
        }
        else if (format == AnalysisReportFormat.Html)
        {
            await Line("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>FileCat disk space report</title><style>body{font:1rem system-ui,sans-serif;max-width:80rem;margin:2rem auto;padding:0 1rem;color:#171717;background:#fff}table{border-collapse:collapse;width:100%}td,th{border:1px solid #777;padding:.5rem;vertical-align:top;overflow-wrap:anywhere;text-align:left}td{font-variant-numeric:tabular-nums}.notice{font-weight:bold}</style></head><body><h1>FileCat disk space report</h1>");
            foreach (string value in new[] { scope + ": " + label, "Observed UTC: " + observed, "Metric: " + metric + ". " + MetricDefinition(header.Metric), coverage,
                "Freshness: " + header.Freshness, privacy, context, Summary(header), accounting, fidelity,
                header.DetailLimited ? "Top captured items only; detail is limited. Use a separately labelled full-result CSV or entire inventory for all observations." : "Captured report rows: " + header.Rows.ToString(CultureInfo.InvariantCulture),
                "Insights: no separate insight results were captured for this report." }) await Line("<p>" + Html(value) + "</p>");
            await Line("<h2>Captured items</h2><table><thead><tr><th>Item</th><th>Displayed location</th><th>Known active value</th><th>Identity unverified</th><th>Missing states</th><th>Known file-size bytes</th><th>File paths</th><th>Caveats</th></tr></thead><tbody>");
        }
        else
        {
            await Line("# FileCat disk space report\n");
            foreach (string value in new[] { scope + ": " + label, "Observed UTC: " + observed, "Metric: " + metric + ". " + MetricDefinition(header.Metric), coverage,
                "Freshness: " + header.Freshness, privacy, context, Summary(header), accounting, fidelity,
                header.DetailLimited ? "Top captured items only; detail is limited. Use a separately labelled full-result CSV or entire inventory for all observations." : "Captured report rows: " + header.Rows.ToString(CultureInfo.InvariantCulture),
                "Insights: no separate insight results were captured for this report." }) await Line(Markdown(value) + "\n");
            await Line("## Captured items\n\n| Item | Displayed location | Known active value | Identity unverified | Missing states | Known file-size bytes | File paths | Caveats |\n| --- | --- | ---: | ---: | --- | ---: | ---: | --- |");
        }
        ulong count = 0;
        await foreach (var row in rows.WithCancellation(cancellation).ConfigureAwait(false))
        {
            cancellation.ThrowIfCancellationRequested();
            if (++count > header.Rows || !Enum.IsDefined(row.Path.Kind) || row.Path.Name.Length > AnalysisLimits.RawNameBytes * 2 || row.DisplayLocation?.Length > AnalysisLimits.LocatorBytes)
                throw new InvalidDataException("The report rows differ from the declared captured scope or exceed display bounds.");
            string name = redactPaths ? "Item " + count.ToString(CultureInfo.InvariantCulture) : AnalysisNames.DisplayName(row.Path);
            string location = redactPaths ? "" : Safe(row.DisplayLocation ?? "");
            bool boundary = !row.IsAccountingGroup && row.Path.Kind is AnalysisKind.Link or AnalysisKind.Special;
            string known = boundary ? header.Metric == AnalysisMetric.Files ? "Not counted" : "Not measured" : row.Totals.CompatibleBasis ? Number(row.Totals.Known) : "Incomparable bases";
            string unverified = boundary ? "Not measured" : row.Totals.CompatibleBasis ? Number(row.Totals.IdentityUnverified) : "Incomparable bases";
            string missing = Evidence(row.Totals.Missing, row.HasUnmeasuredEvidence);
            string caveat = string.Join("; ", new[] { row.NamespaceIncomplete ? "Namespace incomplete" : "", row.HasChargesElsewhere ? "Charges elsewhere" : "", row.IsFilterContext ? "Filter context" : "", Evidence(row.Logical.Missing, row.HasUnmeasuredLogicalEvidence) }.Where(text => text.Length != 0));
            if (format == AnalysisReportFormat.Csv)
                await Line(CsvRow("item", name, location, row.IsAccountingGroup ? "Accounting group" : row.Path.Kind.ToString(), row.Totals, row.Logical, row.NamespaceIncomplete, row.HasChargesElsewhere, row.IsFilterContext, "", boundary, row.HasUnmeasuredEvidence, row.HasUnmeasuredLogicalEvidence));
            else
            {
                string[] cells = [name, location, known, unverified, missing, boundary ? "Not measured" : Number(row.Logical.KnownBytes), boundary ? "Not counted" : row.Logical.Files.ToString(CultureInfo.InvariantCulture), caveat];
                if (format == AnalysisReportFormat.Html) await Line("<tr>" + string.Concat(cells.Select(cell => "<td>" + Html(cell) + "</td>")) + "</tr>");
                else await Line("| " + string.Join(" | ", cells.Select(Markdown)) + " |");
            }
        }
        if (count != header.Rows) throw new InvalidDataException("The report ended before every declared captured row was written.");
        if (format == AnalysisReportFormat.Html) await Line("</tbody></table></body></html>");
        await writer.FlushAsync(cancellation).ConfigureAwait(false);
        Task Line(string value) => writer.WriteLineAsync(value.AsMemory(), cancellation);
        string CsvRow(string kind, string item, string location, string entryKind, AnalysisTotals totals, AnalysisLogicalTotals logical, bool incomplete, bool elsewhere, bool context, string notes, bool boundary = false, bool activeUnknown = false, bool logicalUnknown = false) =>
            Csv([kind, "1", scope, metric, item, location, entryKind, boundary ? "" : totals.CompatibleBasis ? Number(totals.Known) : "Incomparable bases",
                boundary ? "" : totals.CompatibleBasis ? Number(totals.IdentityUnverified) : "Incomparable bases", boundary ? "Not measured" : Evidence(totals.Missing, activeUnknown || kind == "metadata" && header.HasUnmeasuredEvidence), totals.CompatibleBasis ? "true" : "false",
                boundary ? "" : Number(logical.KnownBytes), boundary ? "Not measured" : Evidence(logical.Missing, logicalUnknown || kind == "metadata" && header.HasUnmeasuredLogicalEvidence), boundary ? "" : logical.Files.ToString(CultureInfo.InvariantCulture), incomplete ? "true" : "false", elsewhere ? "true" : "false", context ? "true" : "false",
                coverage, observed, redactPaths ? "true" : "false", "true", header.Rows.ToString(CultureInfo.InvariantCulture), notes]);
    }
    internal static string Safe(string value) => AnalysisNames.Display(value, 4096, escapeTrailing: true);
    private static string Context(AnalysisReportHeader header, bool redact)
    {
        static string Choice(bool? value) => value is null ? "not captured" : value.Value ? "yes" : "no";
        string filter = header.FilterContext is { } captured ? "Captured filter query: " + (redact ? "omitted" : captured.Query.Length == 0 ? captured.RootScoped is null ? "query text not captured" : "no text predicate" : Safe(captured.Query)) +
            "; case sensitive: " + Choice(captured.CaseSensitive) + "; captured-location restriction: " + Choice(captured.RootScoped) + "; unknown-results cohort: " + Choice(captured.UnknownOnly) + ". " : "No display predicate limits this captured report scope. ";
        string basis = header.Metric != AnalysisMetric.OnDisk ? "The active metric uses captured logical observations; native allocation bases do not define it."
            : header.MeasurementBases is not { } values ? "Provider allocation basis labels were not captured. Use the full inventory for recorded facts."
            : "Source-scope provider allocation bases: " + (redact ? "labels omitted" : values.Labels.IsEmpty ? "none recorded" : string.Join("; ", values.Labels.Select(Safe))) +
                (values.DetailLimited ? ". Basis label detail is limited; use the full inventory for every fact." : ".") + " This source context does not assign an exclusive or reclaimable basis to each table row.";
        return filter + (header.DetailedAllocation ? "Detailed allocation policy. " : "Standard allocation policy. ") + basis;
    }
    internal static string Html(string value) => WebUtility.HtmlEncode(value);
    internal static string Markdown(string value)
    {
        var output = new StringBuilder();
        foreach (char c in value) { if ("\\`*_{}[]()<>#+-.!|~".Contains(c, StringComparison.Ordinal)) output.Append('\\'); output.Append(c); }
        return output.ToString();
    }
    internal static string CsvCell(string value)
    {
        var first = value.AsSpan().TrimStart();
        if (!first.IsEmpty && first[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
    private static string Csv(IEnumerable<string> values) => string.Join(',', values.Select(CsvCell));
    private static string Number(UInt128 value) => value.ToString(CultureInfo.InvariantCulture);
    private static string Evidence(ImmutableDictionary<MeasurementState, ulong> states, bool unknown) =>
        (unknown ? "Unmeasured evidence remains; known values are subtotals. " : "") + States(states);
    private static string States(ImmutableDictionary<MeasurementState, ulong> states)
    {
        if (states.Any(pair => !Enum.IsDefined(pair.Key))) throw new InvalidDataException("A report measurement state is unsupported.");
        return string.Join("; ", states.OrderBy(pair => pair.Key).Select(pair => pair.Key + ": " + pair.Value.ToString(CultureInfo.InvariantCulture)));
    }
    private static string Summary(AnalysisReportHeader header) => "Known active: " + (header.Totals.CompatibleBasis ? Number(header.Totals.Known) : "Incomparable bases") +
        "; identity unverified: " + (header.Totals.CompatibleBasis ? Number(header.Totals.IdentityUnverified) : "Incomparable bases") + "; missing active: " + Evidence(header.Totals.Missing, header.HasUnmeasuredEvidence) +
        "; known file-size bytes: " + Number(header.Logical.KnownBytes) + "; file paths: " + header.Logical.Files.ToString(CultureInfo.InvariantCulture) + "; missing logical: " + Evidence(header.Logical.Missing, header.HasUnmeasuredLogicalEvidence);
    private static string ScopeName(AnalysisReportScope scope) => scope switch
    { AnalysisReportScope.FilteredView => "Captured filtered view", AnalysisReportScope.SelectedRows => "Captured selected rows", _ => "Entire unfiltered result" };
    private static string MetricName(AnalysisMetric metric) => metric switch
    { AnalysisMetric.FileSize => "File size", AnalysisMetric.UniqueFileSize => "Unique file size", AnalysisMetric.OnDisk => "On disk — reported", _ => "Files" };
    private static string MetricDefinition(AnalysisMetric metric) => metric switch
    {
        AnalysisMetric.FileSize => "Logical bytes per observed file path; aliases can count repeatedly.",
        AnalysisMetric.UniqueFileSize => "Verified file identity is counted once within scope; unverified identity is separate.",
        AnalysisMetric.OnDisk => "Provider-reported allocation on compatible bases; this is not exclusive or guaranteed reclaimable space.",
        _ => "Observed file paths; unknown namespace coverage is separate."
    };
}
