using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Two independently retained immutable inputs and applied view; never a live source binding.</summary>
public sealed class AnalysisCapturedComparisonBundle : AnalysisCapturedOutput
{
    private AnalysisSnapshotLease? _previous, _current;
    private readonly AnalysisComparisonView _view;
    private readonly ImmutableDictionary<string, string>? _roots, _previousRootIds, _currentRootIds;
    private readonly string _previousLabel;
    private readonly Guid _id = Guid.NewGuid();
    public AnalysisTaint Taint { get; }
    private AnalysisCapturedComparisonBundle(DeviceIoScheduler io, AnalysisSnapshotLease previous, AnalysisSnapshotLease current,
        AnalysisComparisonView view, ImmutableDictionary<string, string>? roots, string previousLabel,
        ImmutableDictionary<string, string>? previousRootIds, ImmutableDictionary<string, string>? currentRootIds) : base(io, null)
    {
        _previous = previous; _current = current; _view = view; _roots = roots; _previousLabel = previousLabel;
        _previousRootIds = previousRootIds; _currentRootIds = currentRootIds;
        Taint = AnalysisPrivacy.Join(previous.Snapshot.Taint, current.Snapshot.Taint);
    }
    public static AnalysisCapturedComparisonBundle Capture(DeviceIoScheduler io, AnalysisSnapshotLease previous, AnalysisSnapshotLease current,
        AnalysisComparisonView view, ImmutableDictionary<string, string>? roots = null, string previousLabel = "Previous observations",
        ImmutableDictionary<string, string>? previousRootIds = null, ImmutableDictionary<string, string>? currentRootIds = null)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(previousLabel); AnalysisComparisonView.Validate(view);
        AnalysisSnapshotLease? before = null, now = null;
        try
        {
            before = previous.Retain(); now = current.Retain();
            return new(io, before, now, view, roots, previousLabel, previousRootIds, currentRootIds);
        }
        catch { try { before?.Dispose(); } finally { now?.Dispose(); } throw; }
    }
    protected override Task<AnalysisExportReceipt> ExportCoreAsync(string name, AnalysisIoDevices devices,
        Func<AnalysisExportDestination> createDestination, bool redactPaths, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.ValidateComparisonName(name);
        if (redactPaths) throw new ArgumentException("A comparison bundle includes both complete inventories, exact names, locations and identifiers. Choose a human report to omit paths.", nameof(redactPaths));
        var previous = (_previous ?? throw new ObjectDisposedException(nameof(AnalysisCapturedComparisonBundle))).Snapshot;
        var current = (_current ?? throw new ObjectDisposedException(nameof(AnalysisCapturedComparisonBundle))).Snapshot;
        return AnalysisInventoryFormat.ExportNewAsync(Io, devices, name, createDestination,
            (sink, token) => AnalysisInventoryFormat.WritePreparedPipeAsync(sink, Io, devices, (stream, stop) =>
            {
                AnalysisComparisonFormat.Write(stream, _id, previous, current, _view, _roots, _previousLabel, stop, _previousRootIds, _currentRootIds);
                return Task.CompletedTask;
            }, token), cancellation);
    }
    protected override ValueTask ReleaseAsync()
    {
        var previous = _previous; var current = _current; _previous = _current = null; Exception? failure = null;
        try { previous?.Dispose(); } catch (Exception error) { failure = error; }
        try { current?.Dispose(); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) throw AnalysisCloseUnconfirmedException.From(failure);
        return ValueTask.CompletedTask;
    }
}
