using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Captured report rows sharing the actual-completion lifetime used by chart outputs.</summary>
public abstract class AnalysisCapturedReport : AnalysisCapturedOutput
{
    public AnalysisReportHeader Header { get; }
    public AnalysisReportFormat Format { get; }
    internal AnalysisCapturedReport(DeviceIoScheduler io, AnalysisIoDevices? devices, AnalysisReportHeader header, AnalysisReportFormat format)
        : base(io, devices) { Header = header; Format = format; }
    protected abstract IAsyncEnumerable<AnalysisReportRow> RowsAsync(bool locations, CancellationToken cancellation);
    protected override Task<AnalysisExportReceipt> ExportCoreAsync(string name, AnalysisIoDevices devices,
        Func<AnalysisExportDestination> createDestination, bool redactPaths, CancellationToken cancellation) =>
        AnalysisHumanReport.ExportNewAsync(Io, devices, name, createDestination, Header,
            RowsAsync(!redactPaths, cancellation), Format, redactPaths, cancellation);
}

internal static class AnalysisReportLocations
{
    internal static string Append(RootBinding root, IReadOnlyList<string> names)
    {
        string value = AnalysisNames.Display(root.DisplayLocator, 4096);
        if (names.Count != 0)
        {
            bool windows = root.NameRules == AnalysisNameRules.Windows;
            value = (windows ? value.TrimEnd('/', '\\') : value.TrimEnd('/')) + (windows ? "\\" : "/") + string.Join(windows ? "\\" : "/", names);
        }
        return value.Length <= AnalysisLimits.LocatorBytes ? value : "[Displayed location exceeds the report bound; use the full inventory for raw names.]";
    }
}
