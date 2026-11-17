using System.Globalization;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    internal bool HasRuntimeBranchContext
    {
        get
        {
            lock (_gate)
                return _close is null && _session is { Completion.IsCompletedSuccessfully: true, SourceCloseFailures: 0 } &&
                    _runtimeLocations.Length != 0;
        }
    }

    /// <summary>
    /// Starts a separate scan containing only a directory from this runtime's own
    /// completed observations. Neither view text nor a saved locator can supply
    /// a new root. The branch acquires its own native handle and private store.
    /// </summary>
    internal async Task<AnalysisIndexedAcquisition> BeginBranchRefreshAsync(SessionIdentity expected, ulong directory,
        IProgress<AnalysisProgress>? progress = null, CancellationToken cancellation = default,
        Action<string>? preparationDeviceChanged = null)
    {
        AnalysisSession session;
        string[] locations;
        AnalysisComponentPreset components;
        lock (_gate)
        {
            if (!HasRuntimeBranchContext) throw new InvalidOperationException("Wait for the live analysis and its source cleanup to finish.");
            session = _session!;
            locations = (string[])_runtimeLocations.Clone();
            components = _runtimeComponents;
        }

        var selection = await CaptureBranchAsync(session, expected, directory, cancellation).ConfigureAwait(false);
        if (!int.TryParse(selection.Root.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal) ||
            ordinal < 1 || ordinal > locations.Length || ordinal.ToString(CultureInfo.InvariantCulture) != selection.Root.Id)
            throw new InvalidOperationException("The recorded folder has no verified runtime root.");

        var next = await PrepareRescanAsync(this, cancellation, preparationDeviceChanged).ConfigureAwait(false);
        // A branch has a different root set and separate totals. It can coexist
        // with the original analysis and leaves its full-scan successor available.
        next._rescanPredecessor = null;
        IAnalysisBackend? parent = null;
        IAnalysisBackend? branch = null;
        try
        {
            var location = locations[ordinal - 1];
            var device = PathUtil.GetDeviceKey(location);
            preparationDeviceChanged?.Invoke(device);
            parent = await AnalysisIo.Run(_services.Io, device, _ => PrepareRoot(location), cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            if (parent.Root.Assurance != IdentityAssurance.Verified || parent.Root.Scheme != selection.Root.Scheme ||
                parent.Root.NameRules != selection.Root.NameRules || parent.Root.TraversalBoundary != selection.Root.TraversalBoundary ||
                parent.Root.CanonicalLocator != selection.Root.CanonicalLocator || parent.Root.SourceIdentity != selection.Root.SourceIdentity)
                throw new AnalysisSourceChangedException();

            selection.RootComponent.RequireMatch(parent.RootEntry);
            if (selection.Children.Length == 0)
            {
                branch = parent;
                parent = null;
            }
            else
            {
                var opened = parent;
                branch = await AnalysisIo.Run(_services.Io, device, token => opened switch
                {
                    WindowsAnalysisBackend windows => (IAnalysisBackend)windows.OpenBranch(selection.Children, token),
                    UnixAnalysisBackend unix => unix.OpenBranch(selection.Children, token),
                    _ => throw new NotSupportedException("This source has no qualified branch refresh binding.")
                }, cancellation).ConfigureAwait(false);
                var retainedParent = parent;
                parent = null;
                await AnalysisIo.ReleaseAsync(_services.Io, retainedParent).ConfigureAwait(false);
            }

            cancellation.ThrowIfCancellationRequested();
            lock (_gate) ObjectDisposedException.ThrowIf(_close is not null, this);
            next.Start([branch], progress, components);
            branch = null;
            return next;
        }
        catch (Exception failure)
        {
            var closing = new List<Task> { next.DisposeAsync().AsTask() };
            if (parent is not null) closing.Add(AnalysisIo.ReleaseAsync(_services.Io, parent));
            if (branch is not null) closing.Add(AnalysisIo.ReleaseAsync(_services.Io, branch));
            try { await Task.WhenAll(closing).ConfigureAwait(false); }
            catch (Exception close) when (AnalysisCloseUnconfirmedException.IsPresent(close))
            { throw new AnalysisCloseUnconfirmedException(new AggregateException(failure, close)); }
            throw;
        }
    }

    private sealed record BranchSelection(RootBinding Root, AnalysisBranchComponent RootComponent, AnalysisBranchComponent[] Children);

    private static async Task<BranchSelection> CaptureBranchAsync(AnalysisSession session, SessionIdentity expected, ulong directory,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (directory == 0) throw new InvalidOperationException("Choose a captured folder to refresh.");

        if (session.DatasetDescriptor is not null)
        {
            await using var dataset = await session.AcquireDatasetAsync(cancellation).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The completed recorded analysis is unavailable.");
            var descriptor = dataset.Descriptor;
            return await ReadAsync(descriptor.Identity, descriptor.Phase, descriptor.Roots, dataset.ReadPathAsync).ConfigureAwait(false);
        }

        using var lease = session.AcquireSnapshot();
        var snapshot = lease.Snapshot;
        return await ReadAsync(snapshot.Identity, snapshot.Phase, snapshot.Roots, (id, _) =>
        {
            // Runtime snapshots are ordered by path ID. Lookup never loads or
            // reindexes the full namespace to recover a folder's ancestry.
            int low = 0, high = snapshot.Nodes.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                var node = snapshot.Nodes[middle];
                if (node.Id == id) return Task.FromResult(AnalysisPathMetadata.From(node));
                if (node.Id < id) low = middle + 1;
                else high = middle - 1;
            }
            throw new InvalidOperationException("The captured folder is unavailable in the completed analysis.");
        }).ConfigureAwait(false);

        async Task<BranchSelection> ReadAsync(SessionIdentity actual, AnalysisPhase phase, IReadOnlyList<RootBinding> roots,
            Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> read)
        {
            if (phase != AnalysisPhase.Finished || actual.SessionId != expected.SessionId || actual.Epoch != expected.Epoch ||
                actual.Revision != expected.Revision)
                throw new InvalidOperationException("Choose a folder in the finished observation revision.");

            var path = await read(directory, cancellation).ConfigureAwait(false);
            var root = roots.SingleOrDefault(item => item.Id == path.RootId);
            if (root is not { Scheme: "file", Kind: AnalysisKind.Directory, Assurance: IdentityAssurance.Verified,
                    Disposition: AnalysisRootDisposition.Active, ObservedObjectIdentity: not null, SourceIdentity: not null })
                throw new NotSupportedException("This location has no qualified live folder binding.");

            var chain = new List<AnalysisBranchComponent>();
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (path.RootId != root.Id || path.Kind != AnalysisKind.Directory || !path.IncludedByPolicy ||
                    path.MetadataState != MeasurementState.Known || path.Cloud == true || path.ObservedIdentity is null ||
                    path.Times is not { CreatedUtc: { } created, CreatedPrecisionTicks: { } precision })
                    throw new NotSupportedException("This branch lacks verified identity and creation-time evidence.");

                var component = new AnalysisBranchComponent(path.Name, path.ObservedIdentity, created, precision);
                if (path.ParentId == 0)
                {
                    if (path.ObservedIdentity != root.ObservedObjectIdentity) throw new AnalysisSourceChangedException();
                    chain.Reverse();
                    if (chain.Count != 0) AnalysisBranchComponent.Validate(chain, OperatingSystem.IsWindows());
                    return new(root, component, chain.ToArray());
                }

                if (chain.Count >= AnalysisBranchComponent.MaximumDepth || path.ParentId >= path.Id)
                    throw new NotSupportedException("This branch exceeds the qualified ancestry limit.");
                byte[] textBytes = AnalysisRawNames.Encode(path.NameEncoding, path.Name);
                if (!path.RawName.IsDefaultOrEmpty && !path.RawName.AsSpan().SequenceEqual(textBytes))
                    throw new NotSupportedException("The branch has a raw name that cannot be reopened losslessly.");
                chain.Add(component);
                path = await read(path.ParentId, cancellation).ConfigureAwait(false);
            }
        }
    }
}
