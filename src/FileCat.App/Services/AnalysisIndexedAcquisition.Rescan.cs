using FileCat.Core.Analysis;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    private readonly RootBinding?[] _privateWitnesses = new RootBinding?[3];
    private AnalysisSession? _rescanPredecessor;
    private string? _rescanProfileCorrelation;

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
