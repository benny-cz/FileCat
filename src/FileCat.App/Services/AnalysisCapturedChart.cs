using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileCat.App.Controls.DiskSpace;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.App.Services;

internal enum AnalysisChartFormat { Svg, Png }

/// <summary>Admits image working memory before allocation and owns the captured scope through actual output close.</summary>
internal sealed class AnalysisCapturedChart : AnalysisCapturedOutput
{
    private const long Budget = AnalysisLimits.MemoryBytes / 4;
    private static long s_reserved;
    private long _reservation;
    private IAsyncDisposable? _source;
    private MemoryStream? _encoded;
    private readonly AnalysisChartFormat _format;
    private readonly bool _redacted;
    internal static long ReservedBytes => Interlocked.Read(ref s_reserved);
    private AnalysisCapturedChart(DeviceIoScheduler io, IAsyncDisposable source, AnalysisChartFormat format, bool redacted, long reservation)
        : base(io, null) { _source = source; _format = format; _redacted = redacted; _reservation = reservation; }

    // Ownership of source transfers on success. On failure the caller still owns it.
    internal static Task<AnalysisCapturedChart> CaptureAsync(DeviceIoScheduler io, AnalysisCapturedReport source, AnalysisChartControl chart,
        AnalysisChartFormat format, int width, int height, bool redact, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source);
        return CaptureCoreAsync(io, source, source.Header, chart, format, width, height, redact, null, cancellation);
    }

    // Both owners transfer only after the scene is completely encoded. Failure leaves them with the caller.
    internal static Task<AnalysisCapturedChart> CaptureComparisonAsync(DeviceIoScheduler io, AnalysisCapturedReport source,
        IAsyncDisposable comparisonOwner, AnalysisChartControl chart, AnalysisComparisonChartContext comparison,
        AnalysisChartFormat format, int width, int height, bool redact, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(comparisonOwner); ArgumentNullException.ThrowIfNull(comparison);
        return CaptureCoreAsync(io, new ComparisonOwners(source, comparisonOwner), source.Header, chart, format, width, height, redact, comparison, cancellation);
    }

    private static async Task<AnalysisCapturedChart> CaptureCoreAsync(DeviceIoScheduler io, IAsyncDisposable source, AnalysisReportHeader header, AnalysisChartControl chart,
        AnalysisChartFormat format, int width, int height, bool redact, AnalysisComparisonChartContext? comparison, CancellationToken cancellation)
    {
        Dispatcher.UIThread.VerifyAccess(); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(chart);
        AnalysisChartScene.ValidateSize(width, height); if (!Enum.IsDefined(format)) throw new ArgumentOutOfRangeException(nameof(format));
        cancellation.ThrowIfCancellationRequested();
        long reservation = checked(16L * 1024 * 1024 + (format == AnalysisChartFormat.Png ? (long)width * height * 20 : 0));
        while (true)
        {
            long current = Interlocked.Read(ref s_reserved);
            if (reservation > Budget - current) throw new InvalidOperationException("Chart export memory is in use or this image is too large. Choose a smaller image or wait for other chart exports to finish.");
            if (Interlocked.CompareExchange(ref s_reserved, current + reservation, current) == current) break;
        }
        var captured = new AnalysisCapturedChart(io, source, format, redact, reservation);
        try
        {
            var scene = chart.CaptureExportScene(header, width, height, redact, comparison);
            int limit = checked((int)(format == AnalysisChartFormat.Png ? (long)width * height * 5 + 65536 : 8L * 1024 * 1024));
            var encoded = captured._encoded = new LimitedImageStream(limit);
            if (format == AnalysisChartFormat.Svg)
                await Task.Run(() => scene.WriteSvg(encoded, cancellation), CancellationToken.None);
            else
            {
                var canvas = new AnalysisChartCanvas(scene); canvas.Measure(new Size(width, height)); canvas.Arrange(new Rect(0, 0, width, height));
                using var image = new RenderTargetBitmap(new PixelSize(width, height)); image.Render(canvas); cancellation.ThrowIfCancellationRequested(); image.Save(encoded, PngBitmapEncoderOptions.Default);
            }
            cancellation.ThrowIfCancellationRequested(); return captured;
        }
        catch
        {
            captured._source = null; await captured.DisposeAsync(); throw;
        }
    }
    protected override Task<AnalysisExportReceipt> ExportCoreAsync(string name, AnalysisIoDevices devices,
        Func<AnalysisExportDestination> createDestination, bool redactPaths, CancellationToken cancellation)
    {
        string extension = _format == AnalysisChartFormat.Svg ? ".svg" : ".png";
        AnalysisExportDestination.ValidateName(name);
        if (!name.EndsWith(extension, StringComparison.Ordinal) || redactPaths != _redacted)
            throw new ArgumentException("The chart format or redaction differs from the reviewed capture.");
        return AnalysisInventoryFormat.ExportNewAsync(Io, devices, name, createDestination, async (sink, token) =>
        {
            var bytes = _encoded ?? throw new ObjectDisposedException(nameof(AnalysisCapturedChart));
            if (!bytes.TryGetBuffer(out var buffer)) throw new InvalidOperationException("The captured chart buffer is unavailable.");
            for (int position = 0; position < buffer.Count;)
            {
                int offset = position, length = Math.Min(64 * 1024, buffer.Count - position);
                await AnalysisIo.Run(Io, devices, stop =>
                { stop.ThrowIfCancellationRequested(); sink.Stream.Write(buffer.Array!, buffer.Offset + offset, length); sink.FlushBatch(); return true; }, token).ConfigureAwait(false);
                position += length;
            }
        }, cancellation);
    }
    protected override async ValueTask ReleaseAsync()
    {
        var source = _source; _source = null; Exception? failure = null;
        try { if (source is not null) await source.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
        try { _encoded?.Dispose(); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        finally { _encoded = null; long reservation = Interlocked.Exchange(ref _reservation, 0); Interlocked.Add(ref s_reserved, -reservation); }
        if (failure is not null) throw AnalysisCloseUnconfirmedException.From(failure);
    }
    private sealed class ComparisonOwners(AnalysisCapturedReport report, IAsyncDisposable comparison) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            Exception? failure = null;
            try { await report.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
            try { await comparison.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) throw AnalysisCloseUnconfirmedException.From(failure);
        }
    }
    private sealed class LimitedImageStream : MemoryStream
    {
        private readonly int _limit;
        internal LimitedImageStream(int limit) : base(limit) { _limit = limit; }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override void SetLength(long value) { if (value < 0 || value > _limit) throw new IOException("The encoded chart exceeds its memory limit."); base.SetLength(value); }
        private void Check(int count) { if (Position > _limit - count) throw new IOException("The encoded chart exceeds its memory limit."); }
    }
}
