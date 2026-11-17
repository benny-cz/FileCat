using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    private readonly RootBinding?[] _privateWitnesses = new RootBinding?[3];
    private AnalysisSession? _rescanPredecessor;
    private string? _rescanProfileCorrelation;
    private string[] _runtimeLocations = [];
    private AnalysisComponentPreset _runtimeComponents;
    internal AnalysisSession RuntimeSession
    {
        get { lock (_gate) { ObjectDisposedException.ThrowIf(_close is not null, this); return _session ?? throw new InvalidOperationException("No runtime acquisition was started."); } }
    }
    internal bool HasRuntimeRescanContext
    {
        get { lock (_gate) return _close is null && _session is { Completion.IsCompletedSuccessfully: true, SourceCloseFailures: 0 } && _runtimeLocations.Length != 0; }
    }
    internal (string[] Locations, AnalysisComponentPreset Components) RuntimeSettings
    {
        get { lock (_gate) { ObjectDisposedException.ThrowIf(_close is not null, this); return (_runtimeLocations.ToArray(), _runtimeComponents); } }
    }

    internal static string RuntimeReopenLocator(IAnalysisBackend source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Root.Scheme != "file") throw new NotSupportedException("This acquisition has no qualified local rescan factory.");
        string locator = source.Root.CanonicalLocator;
        if (OperatingSystem.IsWindows() && locator.StartsWith("\\\\?\\", StringComparison.Ordinal))
        {
            locator = locator[4..];
            if (locator.Length < 3 || !char.IsAsciiLetter(locator[0]) || locator[1] != ':' || locator[2] != '\\')
                throw new NotSupportedException("The captured source has no qualified local drive spelling for rescan.");
        }
        return locator;
    }

    // Starts from this live owner's factories/settings, never from dataset or inventory locators.
    // The returned owner must remain alive through actual completion and view ownership.
    internal async Task<AnalysisIndexedAcquisition> BeginRescanAsync(IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellation = default, Action<string>? preparationDeviceChanged = null)
    {
        string[] locations; AnalysisComponentPreset components;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (!HasRuntimeRescanContext) throw new InvalidOperationException("Wait for the runtime acquisition and actual source closure.");
            locations = _runtimeLocations.ToArray(); components = _runtimeComponents;
        }
        var next = await PrepareRescanAsync(this, cancellation, preparationDeviceChanged).ConfigureAwait(false);
        var prepared = new List<IAnalysisBackend>();
        try
        {
            foreach (string location in locations)
            {
                cancellation.ThrowIfCancellationRequested(); string device = PathUtil.GetDeviceKey(location);
                preparationDeviceChanged?.Invoke(device);
                var source = await AnalysisIo.Run(_services.Io, device, _ => PrepareRoot(location), cancellation).ConfigureAwait(false);
                prepared.Add(source); cancellation.ThrowIfCancellationRequested();
            }
            next.Start(prepared.ToArray(), progress, components); prepared.Clear(); return next;
        }
        catch
        {
            var closes = prepared.Select(source => AnalysisIo.ReleaseAsync(_services.Io, source)).ToList();
            closes.Add(next.DisposeAsync().AsTask()); await Task.WhenAll(closes).ConfigureAwait(false); throw;
        }
    }

    // Only an actual runtime acquisition can issue this request. Imported inventories, catalog
    // records and their locators cannot become a refresh context or provide source factories.
    internal static async Task<AnalysisIndexedAcquisition> PrepareRescanAsync(AnalysisIndexedAcquisition previous,
        CancellationToken cancellation = default, Action<string>? preparationDeviceChanged = null)
    {
        ArgumentNullException.ThrowIfNull(previous); cancellation.ThrowIfCancellationRequested();
        AnalysisSession predecessor; RootBinding?[] witnesses; bool[] guards; string profile; bool ephemeral;
        lock (previous._gate)
        {
            ObjectDisposedException.ThrowIf(previous._close is not null, previous);
            predecessor = previous._session ?? throw new InvalidOperationException("Only a live runtime acquisition can be refreshed.");
            if (!predecessor.Completion.IsCompletedSuccessfully || predecessor.SourceCloseFailures != 0)
                throw new InvalidOperationException("Wait for actual source closure before refreshing.");
            witnesses = previous._privateWitnesses.ToArray(); guards = previous._ancestorGuard.ToArray();
            profile = previous.ProfileIdentity; ephemeral = previous.Profile is null;
        }
        var next = await PrepareAsync(previous._services, cancellation, previous._indexedStorage, preparationDeviceChanged).ConfigureAwait(false);
        try
        {
            cancellation.ThrowIfCancellationRequested();
            lock (previous._gate) ObjectDisposedException.ThrowIf(previous._close is not null, previous);
            for (int i = 0; i < witnesses.Length; i++)
            {
                var expected = witnesses[i]; var actual = next._privateWitnesses[i];
                if (expected?.ObservedObjectIdentity is null || actual is null || guards[i] != next._ancestorGuard[i] ||
                    actual.Scheme != expected.Scheme || actual.CanonicalLocator != expected.CanonicalLocator ||
                    actual.SourceIdentity != expected.SourceIdentity || actual.ObservedObjectIdentity != expected.ObservedObjectIdentity ||
                    actual.Kind != expected.Kind || actual.NameRules != expected.NameRules)
                    throw new InvalidOperationException("A private analysis boundary changed. Start a new analysis.");
            }
            if (ephemeral && next.Profile is null) next._rescanProfileCorrelation = profile;
            else if (next.ProfileIdentity != profile) throw new InvalidOperationException("The private profile changed. Start a new analysis.");
            next._rescanPredecessor = predecessor;
            return next;
        }
        catch { await next.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
