using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    internal bool CanReviewContentReads => OperatingSystem.IsWindows() && HasRuntimeRescanContext && !_imports.HasUnconfirmedClose;

    internal async Task<AnalysisDuplicateReadPlan> PrepareDuplicateReadPlanAsync(AnalysisSelection marks, CancellationToken cancellation)
    {
        AnalysisSession session;
        IDisposable work;
        lock (_gate)
        {
            if (!CanReviewContentReads) throw new NotSupportedException("Content review requires a current, finished live NTFS analysis.");
            session = _session!;
            work = _imports.Enter();
        }
        using (work)
        {
            try
            {
                if (session.DatasetDescriptor is not null)
                {
                    await using var dataset = await session.AcquireDatasetAsync(cancellation).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("The finished indexed revision is unavailable.");
                    var descriptor = dataset.Descriptor;
                    RequireLiveDuplicateRevision(descriptor.Origin, descriptor.Phase, descriptor.Taint);
                    var estimate = await dataset.EstimateDuplicatesAsync(marks, cancellation).ConfigureAwait(false);
                    return await BuildReadPlanAsync(session, estimate, descriptor.Roots, dataset.ReadPathAsync, dataset.ReadObjectAsync,
                        cancellation).ConfigureAwait(false);
                }

                using var captured = session.AcquireSnapshot();
                var snapshot = captured.Snapshot;
                RequireLiveDuplicateRevision(snapshot.Origin, snapshot.Phase, snapshot.Taint);
                var preview = await AnalysisDuplicateEstimates.FromSnapshotAsync(captured, marks, cancellation).ConfigureAwait(false);
                return await Task.Run(async () =>
                {
                    var ids = preview.Rows.Where(row => row.State == AnalysisDuplicateCandidateState.Candidate)
                        .Select(row => FindPath(row.PathId).ObjectId).OfType<string>().ToHashSet(StringComparer.Ordinal);
                    var objects = new Dictionary<string, AnalysisObjectMetadata>(StringComparer.Ordinal);
                    foreach (var obj in snapshot.Objects)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (ids.Contains(obj.Id)) objects.Add(obj.Id, AnalysisObjectMetadata.From(obj));
                        if (objects.Count == ids.Count) break;
                    }
                    return await BuildReadPlanAsync(session, preview, snapshot.Roots,
                        (path, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(FindPath(path)); },
                        (id, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(objects.GetValueOrDefault(id)); },
                        cancellation).ConfigureAwait(false);
                }, cancellation).ConfigureAwait(false);

                AnalysisPathMetadata FindPath(ulong id)
                {
                    int low = 0, high = snapshot.Nodes.Length - 1;
                    while (low <= high)
                    {
                        int middle = low + (high - low) / 2;
                        var path = snapshot.Nodes[middle];
                        if (path.Id == id) return AnalysisPathMetadata.From(path);
                        if (path.Id < id) low = middle + 1;
                        else high = middle - 1;
                    }
                    throw new InvalidDataException("A captured duplicate path is missing from the live revision.");
                }
            }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                _imports.RecordUnconfirmedClose(error);
                throw;
            }
        }
    }

    private static void RequireLiveDuplicateRevision(AnalysisOrigin origin, AnalysisPhase phase, AnalysisTaint taint)
    {
        if (origin != AnalysisOrigin.LiveSession || phase != AnalysisPhase.Finished || taint != AnalysisTaint.OrdinarySensitive)
            throw new NotSupportedException("Read review requires a finished, ordinary live observation.");
    }

    private async Task<AnalysisDuplicateReadPlan> BuildReadPlanAsync(AnalysisSession session, AnalysisDuplicateEstimate estimate,
        ImmutableArray<RootBinding> roots, Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath,
        Func<string, CancellationToken, Task<AnalysisObjectMetadata?>> readObject, CancellationToken cancellation)
    {
        var active = roots.ToDictionary(root => root.Id, StringComparer.Ordinal);
        var items = ImmutableArray.CreateBuilder<AnalysisPrimaryReadItem>();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var cachedDirectories = new Dictionary<ulong, AnalysisPathMetadata>();
        long retainedBytes = 0;
        int refused = 0;
        foreach (var row in estimate.Rows)
        {
            cancellation.ThrowIfCancellationRequested();
            if (row.State != AnalysisDuplicateCandidateState.Candidate) continue;
            var root = active[row.RootId!];
            var leaf = await readPath(row.PathId, cancellation).ConfigureAwait(false);
            var item = leaf.Id == row.PathId
                ? await ReadPrimaryTargetAsync(root, leaf, ReadDirectoryAsync, readObject, cancellation).ConfigureAwait(false)
                : null;
            if (item is null || !distinct.Add(item.Target.Identity)) { refused++; continue; }
            retainedBytes += 512L + item.RelativePath.Length * 2L + item.Target.Identity.Length * 2L +
                item.Target.Version.Length * 2L + item.Target.Parents.Sum(part => 64L + part.Name.Length * 2L + part.Identity.Length * 2L);
            CheckSize();
            items.Add(item);
        }
        lock (_gate) RequirePrimaryReadRuntime(session);
        return new(estimate.Identity, items.ToImmutable(), refused,
            (scope, token) => ExecuteDuplicateReadAsync(session, estimate.Identity, scope, token));

        async Task<AnalysisPathMetadata> ReadDirectoryAsync(ulong id, CancellationToken token)
        {
            if (cachedDirectories.TryGetValue(id, out var parent)) return parent;
            parent = await readPath(id, token).ConfigureAwait(false);
            retainedBytes += 512L + parent.Name.Length * 2L + parent.RawName.Length + (parent.ObservedIdentity?.Length ?? 0) * 2L;
            CheckSize();
            cachedDirectories.Add(id, parent);
            return parent;
        }

        void CheckSize()
        {
            if (retainedBytes > AnalysisLimits.ReviewLocatorBytes)
                throw new InvalidOperationException("Narrow the marked scope before reviewing content paths.");
        }

    }

    private void RequirePrimaryReadRuntime(AnalysisSession session)
    {
        ObjectDisposedException.ThrowIf(_close is not null, this);
        if (!CanReviewContentReads || !ReferenceEquals(_session, session))
            throw new InvalidOperationException("The live analysis changed. Open the read from the current result.");
    }

    private async Task<AnalysisDuplicateReadResult> ExecuteDuplicateReadAsync(AnalysisSession session, SessionIdentity identity,
        AnalysisDuplicateReadScope scope, CancellationToken cancellation)
    {
        IDisposable work;
        string[] locations;
        lock (_gate)
        {
            RequirePrimaryReadRuntime(session);
            locations = (string[])_runtimeLocations.Clone();
            work = _imports.Enter();
        }
        using (work)
        {
            var rows = ImmutableArray.CreateBuilder<AnalysisDuplicateReadRow>(scope.Items.Length);
            try
            {
                foreach (var group in scope.Items.GroupBy(item => item.Root.Id))
                {
                    cancellation.ThrowIfCancellationRequested();
                    lock (_gate) RequirePrimaryReadRuntime(session);
                    var root = group.First().Root;
                    string location = PrimaryRuntimeLocation(root, locations);
                    var device = (AnalysisIoDevices)PathUtil.GetDeviceKey(location);
                    WindowsAnalysisBackend? source = null;
                    try
                    {
                        try
                        {
                            source = await AnalysisIo.Run(_services.Io, device, _ => OpenPrimarySource(location, root),
                                cancellation).ConfigureAwait(false);
                        }
                        catch (Exception error) when (ReadFailure(error) is not null)
                        {
                            foreach (var item in group) rows.Add(new(item, ReadFailure(error)!.Value));
                            continue;
                        }
                        foreach (var item in group)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            lock (_gate) RequirePrimaryReadRuntime(session);
                            try
                            {
                                var proof = await ReadPrimaryAsync(source, device, item.Target, cancellation).ConfigureAwait(false);
                                rows.Add(new(item, AnalysisDuplicateReadState.Read, proof));
                            }
                            catch (Exception error) when (ReadFailure(error) is not null)
                            { rows.Add(new(item, ReadFailure(error)!.Value)); }
                        }
                    }
                    finally
                    {
                        if (source is not null) await AnalysisIo.ReleaseAsync(_services.Io, source).ConfigureAwait(false);
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                lock (_gate) RequirePrimaryReadRuntime(session);
                return new(identity, rows.MoveToImmutable());
            }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                _imports.RecordUnconfirmedClose(error);
                throw;
            }
        }

        static AnalysisDuplicateReadState? ReadFailure(Exception error) => AnalysisCloseUnconfirmedException.IsPresent(error)
            ? null : error switch
            {
                AnalysisSourceChangedException or FileNotFoundException or DirectoryNotFoundException or
                    AnalysisEntryUnavailableException { State: MeasurementState.Changed } => AnalysisDuplicateReadState.Changed,
                NotSupportedException or AnalysisDeviceUnavailableException => AnalysisDuplicateReadState.Unavailable,
                IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException => AnalysisDuplicateReadState.Unreadable,
                _ => null
            };
    }

    private async Task<AnalysisPrimaryContentProof> ReadPrimaryAsync(WindowsAnalysisBackend source, AnalysisIoDevices device,
        AnalysisPrimaryContentTarget target, CancellationToken cancellation)
    {
        var reader = await AnalysisIo.Run(_services.Io, device, token => source.OpenPrimaryContent(target, token), cancellation).ConfigureAwait(false);
        try
        {
            while (await AnalysisIo.Run(_services.Io, device, reader.ReadNext, cancellation).ConfigureAwait(false)) { }
            var proof = await AnalysisIo.Run(_services.Io, device, reader.Complete, cancellation).ConfigureAwait(false);
            if (proof.PathId != target.PathId || proof.BytesRead != target.Bytes || proof.Kind != AnalysisContentEvidenceKind.StableContentProof)
                throw new InvalidDataException("The primary read did not confirm the reviewed object and byte count.");
            return proof;
        }
        finally { await AnalysisIo.ReleaseContentAsync(_services.Io, device, reader).ConfigureAwait(false); }
    }
}
