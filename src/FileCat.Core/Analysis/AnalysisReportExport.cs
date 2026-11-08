using System.Threading.Channels;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisHumanReport
{
    /// <summary>Exports only the explicitly captured report scope; the caller retains its immutable row owner until this task actually finishes.</summary>
    public static Task<AnalysisExportReceipt> ExportNewAsync(DeviceIoScheduler io, AnalysisIoDevices devices, string name,
        Func<AnalysisExportDestination> createDestination, AnalysisReportHeader header, IAsyncEnumerable<AnalysisReportRow> rows,
        AnalysisReportFormat format, bool redactPaths, CancellationToken cancellation = default) =>
        AnalysisInventoryFormat.ExportReportNewAsync(io, devices, name, createDestination, header, rows, format, redactPaths, cancellation);
}

public static partial class AnalysisInventoryFormat
{
    internal static Task<AnalysisExportReceipt> ExportReportNewAsync(DeviceIoScheduler io, AnalysisIoDevices devices, string name,
        Func<AnalysisExportDestination> createDestination, AnalysisReportHeader header, IAsyncEnumerable<AnalysisReportRow> rows,
        AnalysisReportFormat format, bool redactPaths, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(rows);
        string extension = format switch { AnalysisReportFormat.Markdown => ".md", AnalysisReportFormat.Html => ".html", AnalysisReportFormat.Csv => ".csv", _ => throw new ArgumentOutOfRangeException(nameof(format)) };
        AnalysisExportDestination.ValidateName(name);
        if (!name.EndsWith(extension, StringComparison.Ordinal)) throw new ArgumentException("The selected report format and filename extension differ.", nameof(name));
        return ExportNewAsync(io, devices, name, createDestination,
            (sink, token) => WriteHumanReportPipeAsync(sink, io, devices, header, rows, format, redactPaths, token), cancellation);
    }
    private static async Task WriteHumanReportPipeAsync(IAnalysisInventoryWriteSink output, DeviceIoScheduler io, AnalysisIoDevices devices,
        AnalysisReportHeader header, IAsyncEnumerable<AnalysisReportRow> rows, AnalysisReportFormat format, bool redactPaths, CancellationToken cancellation)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var pipe = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(2) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var producer = Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                using var stream = new PreparedPipeStream(pipe.Writer, stop.Token);
                await AnalysisHumanReport.WriteAsync(stream, header, rows, format, redactPaths, stop.Token).ConfigureAwait(false);
            }
            catch (Exception error) { failure = error; throw; }
            finally { pipe.Writer.TryComplete(failure); }
        }, CancellationToken.None);
        Exception? failed = null;
        try
        {
            while (await pipe.Reader.WaitToReadAsync(stop.Token).ConfigureAwait(false))
                while (pipe.Reader.TryRead(out var bytes))
                    await AnalysisIo.Run(io, devices, token =>
                    { token.ThrowIfCancellationRequested(); output.Stream.Write(bytes); output.FlushBatch(); return true; }, stop.Token).ConfigureAwait(false);
        }
        catch (Exception error) { failed = error; }
        finally
        {
            stop.Cancel(); pipe.Writer.TryComplete();
            try { await producer.ConfigureAwait(false); }
            catch (Exception error)
            {
                if (failed is null) failed = error;
                else if (error is not OperationCanceledException && !ReferenceEquals(error, failed)) failed = new AggregateException(failed, error);
            }
        }
        if (failed is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed).Throw();
    }
}
