using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisCoordinator
{
    private static void RequireAdmission()
    {
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
    }

    private static void ValidateStart(IReadOnlyList<IAnalysisBackend> sources, AnalysisComponentPreset components, AnalysisStorageOptions? storage)
    {
        if (sources.Count is < 1 or > AnalysisLimits.Roots || sources.Any(source => source is null))
            throw new ArgumentException("Select between 1 and 32 prepared roots.", nameof(sources));
        if (!Enum.IsDefined(components)) throw new ArgumentOutOfRangeException(nameof(components));
        storage?.Validate();
    }

    /// <summary>
    /// Starts a new observation epoch from newly prepared roots after the previous producer has
    /// actually closed. A runtime session is required; inventory identities are never authority.
    /// The previous immutable snapshot/dataset remains independently owned by its existing leases.
    /// Source ownership transfers only when this method successfully returns.
    /// </summary>
    public AnalysisSession Rescan(AnalysisSession previous, IReadOnlyList<IAnalysisBackend> sources,
        IProgress<AnalysisProgress>? progress = null, AnalysisComponentPreset components = AnalysisComponentPreset.Standard,
        AnalysisStorageOptions? storage = null)
    {
        RequireAdmission();
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(sources);
        var prepared = sources.ToArray(); ValidateStart(prepared, components, storage);
        var identity = previous.ReserveNextEpoch(io, _profileIdentity, prepared);
        try { return new(io, prepared, progress, _profileIdentity, components, storage, identity); }
        catch { previous.RollbackEpochReservation(); throw; }
    }
}

public sealed partial class AnalysisSession
{
    // Runtime-only witnesses captured before private exclusions/coverage normalization. They are
    // never populated from an imported inventory or decoded as filesystem capabilities.
    private readonly ImmutableArray<RootBinding> _selectedRoots;
    private bool _epochSuccessorReserved;
    internal bool RuntimeHasSuccessor { get { lock (_sync) return _epochSuccessorReserved; } }

    internal SessionIdentity ReserveNextEpoch(DeviceIoScheduler io, string profile, IReadOnlyList<IAnalysisBackend> sources)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ntfs is not null) throw new InvalidOperationException("Start a standard analysis or explicitly approve a new fast NTFS scan.");
            if (!ReferenceEquals(io, _io) || profile != _identity.HostProfile)
                throw new InvalidOperationException("A rescan must use its original runtime scheduler and profile.");
            if (!Completion.IsCompletedSuccessfully || !_workerFinished || _sourceCloseFailures != 0 || _final is null)
                throw new InvalidOperationException("Wait for actual producer and source closure before rescanning.");
            if (_epochSuccessorReserved) throw new InvalidOperationException("This epoch already has a successor. Rescan the latest runtime epoch.");
            if (sources.Count != _selectedRoots.Length) throw new InvalidOperationException("Changed root selection requires a new analysis.");
            for (int i = 0; i < sources.Count; i++)
            {
                var expected = _selectedRoots[i]; var actual = sources[i].Root; var entry = sources[i].RootEntry;
                if (_sources.Any(old => ReferenceEquals(old, sources[i])) || expected.ObservedObjectIdentity is null ||
                    actual.Scheme != expected.Scheme || actual.CanonicalLocator != expected.CanonicalLocator ||
                    actual.SourceIdentity != expected.SourceIdentity ||
                    expected.Scheme != "file" && actual.ConnectionIdentityReference != expected.ConnectionIdentityReference ||
                    actual.TraversalBoundary != expected.TraversalBoundary || actual.NameRules != expected.NameRules ||
                    entry.Kind != expected.Kind || entry.Identity != expected.ObservedObjectIdentity)
                    throw new InvalidOperationException("A selected root changed or cannot be revalidated. Choose the location in a new analysis.");
            }
            var next = _final.Identity with { Epoch = checked(_identity.Epoch + 1), Revision = checked(_revision + 1) };
            _epochSuccessorReserved = true;
            return next;
        }
    }

    internal void RollbackEpochReservation() { lock (_sync) _epochSuccessorReserved = false; }
}
