using System.Globalization;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    private readonly AnalysisRootChangeHints _changeHints = new();
    private readonly CancellationTokenSource _changeHintLifetime = new();
    private Task? _changeHintSetup;

    internal string? ReadRuntimeChangeHints()
    {
        lock (_gate)
        {
            if (_close is not null || _session is not { Completion.IsCompletedSuccessfully: true, SourceCloseFailures: 0 } session ||
                _runtimeLocations.Length == 0) return null;

            _changeHintSetup ??= StartRuntimeChangeHintsAsync(session, _runtimeLocations, _changeHintLifetime.Token);
            if (!_changeHintSetup.IsCompleted) return "Preparing direct change hints for the captured locations.";
            if (_changeHintSetup.IsFaulted) return "Live change hints stopped because their source setup did not finish safely.";
            return _changeHints.Describe();
        }
    }

    private async Task StartRuntimeChangeHintsAsync(AnalysisSession session, IReadOnlyList<string> locations, CancellationToken cancellation)
    {
        var snapshot = session.Snapshot();
        if (snapshot.Phase != AnalysisPhase.Finished) return;
        foreach (var root in snapshot.Roots)
        {
            if (cancellation.IsCancellationRequested) return;
            if (root is not { Disposition: AnalysisRootDisposition.Active, Scheme: "file", Kind: AnalysisKind.Directory,
                    Assurance: IdentityAssurance.Verified, ObservedObjectIdentity: not null } ||
                !int.TryParse(root.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal) ||
                ordinal < 1 || ordinal > locations.Count)
            {
                _changeHints.Record(root.Label, AnalysisRootChangeHints.State.Unmonitored);
                continue;
            }

            string location = locations[ordinal - 1];
            try
            {
                await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(location), token =>
                {
                    // Only the runtime-owned locator is examined. The capture
                    // supplies comparison evidence and never starts a watcher
                    // for an offline, covered, or private-state location.
                    var prepared = PrepareRoot(location);
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        if (prepared.Root.Scheme != root.Scheme || prepared.Root.Kind != root.Kind ||
                            prepared.Root.Assurance != IdentityAssurance.Verified || prepared.Root.NameRules != root.NameRules ||
                            prepared.Root.TraversalBoundary != root.TraversalBoundary || prepared.Root.CanonicalLocator != root.CanonicalLocator ||
                            prepared.Root.SourceIdentity != root.SourceIdentity || prepared.RootEntry.Identity != root.ObservedObjectIdentity)
                        {
                            _changeHints.Record(root.Label, AnalysisRootChangeHints.State.IdentityMismatch);
                            return false;
                        }

                        if (prepared.RootEntry.Cloud == true || !_changeHints.Watch(location, root.Label))
                            _changeHints.Record(root.Label, AnalysisRootChangeHints.State.Unmonitored);
                        return true;
                    }
                    finally { AnalysisIo.CloseOnWorker(prepared); }
                }, cancellation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch (Exception error) when (!AnalysisCloseUnconfirmedException.IsPresent(error) &&
                error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                _changeHints.Record(root.Label, AnalysisRootChangeHints.State.SourceUnverified);
            }
        }
    }

    private async Task CloseRuntimeChangeHintsAsync()
    {
        _changeHintLifetime.Cancel();
        try
        {
            if (_changeHintSetup is { } setup) await setup.ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (_changeHintSetup is null) _changeHints.Dispose();
                else await Task.Run(_changeHints.Dispose).ConfigureAwait(false);
            }
            finally { _changeHintLifetime.Dispose(); }
        }
    }
}
