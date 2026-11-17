using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    internal async Task<AnalysisCapturedIndexedComparisonBundle> CaptureBundleAsync(AnalysisComparisonView view,
        string previousLabel = "Previous observations", ImmutableDictionary<string, string>? previousRootDisplayIds = null,
        ImmutableDictionary<string, string>? currentRootDisplayIds = null, CancellationToken cancellation = default)
    {
        AnalysisComparisonView.Validate(view);
        if (view.Metric != Metric) throw new ArgumentException("Choose the applied comparison metric.", nameof(view));
        cancellation.ThrowIfCancellationRequested(); var retained = Retain(); bool transferred = false;
        try
        {
            if (view.FocusNode != 0) await retained.ReadPathAsync(view.PreviousFocus, view.FocusNode, cancellation).ConfigureAwait(false);
            var result = new AnalysisCapturedIndexedComparisonBundle(retained, _io, _devices, view,
                ChartContext.Structural ? RootMapping : null, previousLabel, previousRootDisplayIds, currentRootDisplayIds);
            transferred = true; return result;
        }
        finally { if (!transferred) await retained.DisposeAsync().ConfigureAwait(false); }
    }
}

/// <summary>Entire exact indexed inputs and inert view metadata, with no materialized inventory arrays.</summary>
internal sealed partial class AnalysisCapturedIndexedComparisonBundle : AnalysisCapturedOutput
{
    private AnalysisIndexedComparison.ReadLease? _inputs;
    private readonly AnalysisIoDevices _devices;
    private readonly AnalysisComparisonView _view;
    private readonly ImmutableDictionary<string, string>? _mapping, _previousRootIds, _currentRootIds;
    private readonly string _previousLabel;
    private readonly Guid _id = Guid.NewGuid();
    internal AnalysisTaint Taint { get; }

    internal AnalysisCapturedIndexedComparisonBundle(AnalysisIndexedComparison.ReadLease inputs, DeviceIoScheduler io,
        AnalysisIoDevices devices, AnalysisComparisonView view, ImmutableDictionary<string, string>? mapping, string previousLabel,
        ImmutableDictionary<string, string>? previousRootIds, ImmutableDictionary<string, string>? currentRootIds) : base(io, devices)
    {
        _inputs = inputs; _devices = devices; _view = view; _mapping = mapping; _previousLabel = previousLabel;
        _previousRootIds = previousRootIds; _currentRootIds = currentRootIds;
        Taint = AnalysisPrivacy.Join(inputs.Descriptor(true).Taint, inputs.Descriptor(false).Taint);
    }
    protected override Task<AnalysisExportReceipt> ExportCoreAsync(string name, AnalysisIoDevices devices,
        Func<AnalysisExportDestination> createDestination, bool redactPaths, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.ValidateComparisonName(name);
        if (redactPaths) throw new ArgumentException("A comparison bundle contains both entire inventories and exact names. Choose a human report to omit paths.", nameof(redactPaths));
        var inputs = _inputs ?? throw new ObjectDisposedException(nameof(AnalysisCapturedIndexedComparisonBundle));
        var allDevices = devices; foreach (string key in _devices.Keys) allDevices = allDevices.Include(key);
        return AnalysisInventoryFormat.ExportNewAsync(Io, allDevices, name, createDestination,
            (sink, token) => AnalysisInventoryFormat.WritePreparedPipeAsync(sink, Io, allDevices,
                (stream, stop) => inputs.UseAsync(async state =>
                {
                    await AnalysisComparisonFormat.WriteIndexedAsync(stream, _id, state.Previous.Graph, state.Current.Graph,
                        inputs.Descriptor(true), inputs.Descriptor(false), _view, _mapping, _previousLabel, _previousRootIds, _currentRootIds,
                        Io, allDevices, stop).ConfigureAwait(false);
                    return true;
                }), token), cancellation);
    }
    protected override async ValueTask ReleaseAsync()
    {
        var inputs = _inputs; _inputs = null;
        if (inputs is not null) await inputs.DisposeAsync().ConfigureAwait(false);
    }
}
