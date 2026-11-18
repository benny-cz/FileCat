using System.Collections.Immutable;
using System.Globalization;
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
        var items = ImmutableArray.CreateBuilder<AnalysisDuplicateReadItem>();
        var distinct = new HashSet<string>(StringComparer.Ordinal);
        var cachedDirectories = new Dictionary<ulong, AnalysisPathMetadata>();
        long retainedBytes = 0;
        int refused = 0;
        foreach (var row in estimate.Rows)
        {
            cancellation.ThrowIfCancellationRequested();
            if (row.State != AnalysisDuplicateCandidateState.Candidate) continue;
            var root = active[row.RootId!];
            var item = await ReadTargetAsync(root, row.PathId).ConfigureAwait(false);
            if (item is null || !distinct.Add(item.Target.Identity)) { refused++; continue; }
            retainedBytes += 512L + item.RelativePath.Length * 2L + item.Target.Identity.Length * 2L +
                item.Target.Version.Length * 2L + item.Target.Parents.Sum(part => 64L + part.Name.Length * 2L + part.Identity.Length * 2L);
            CheckSize();
            items.Add(item);
        }
        lock (_gate) CheckDuplicateRuntime(session);
        return new(estimate.Identity, items.ToImmutable(), refused,
            (scope, token) => ExecuteDuplicateReadAsync(session, estimate.Identity, scope, token));

        async Task<AnalysisDuplicateReadItem?> ReadTargetAsync(RootBinding root, ulong id)
        {
            if (root is not { Scheme: "file", NameRules: AnalysisNameRules.Windows, Assurance: IdentityAssurance.Verified,
                    Disposition: AnalysisRootDisposition.Active, ObservedObjectIdentity: not null, SourceIdentity: not null })
                return null;
            var leaf = await readPath(id, cancellation).ConfigureAwait(false);
            if (leaf.Id != id || leaf.ObjectId is not { } objectId || leaf.Kind != AnalysisKind.File ||
                !HasBinding(leaf, root.Id) || !OrdinaryName(leaf)) return null;
            var obj = await readObject(objectId, cancellation).ConfigureAwait(false);
            if (obj is null || obj.VerifiedIdentity != leaf.ObservedIdentity || obj.Id != objectId || obj.LogicalLength.Value is not { } size ||
                size > (UInt128)long.MaxValue || obj.VersionEvidence is not { } version || !WindowsAnalysisBackend.HasPrimaryVersion(version))
                return null;
            var parents = new List<AnalysisBranchComponent>();
            AnalysisBranchComponent? directory = null;
            if (leaf.ParentId == 0)
            {
                if (root.Kind != AnalysisKind.File || root.ObservedObjectIdentity != leaf.ObservedIdentity) return null;
            }
            else
            {
                if (root.Kind != AnalysisKind.Directory) return null;
                var child = leaf;
                while (child.ParentId != 0)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (child.ParentId >= child.Id || parents.Count >= AnalysisBranchComponent.MaximumDepth - 1) return null;
                    if (!cachedDirectories.TryGetValue(child.ParentId, out var parent))
                    {
                        parent = await readPath(child.ParentId, cancellation).ConfigureAwait(false);
                        if (parent.Id != child.ParentId || parent.Kind != AnalysisKind.Directory || !HasBinding(parent, root.Id) ||
                            (parent.ParentId != 0 && !OrdinaryName(parent))) return null;
                        retainedBytes += 512L + parent.Name.Length * 2L + parent.RawName.Length + parent.ObservedIdentity!.Length * 2L;
                        CheckSize();
                        cachedDirectories.Add(parent.Id, parent);
                    }
                    if (parent.RootId != root.Id) return null;
                    var component = new AnalysisBranchComponent(parent.Name, parent.ObservedIdentity!,
                        parent.Times!.CreatedUtc!.Value, 1);
                    if (parent.ParentId == 0)
                    {
                        if (parent.ObservedIdentity != root.ObservedObjectIdentity) return null;
                        directory = component;
                    }
                    else parents.Add(component);
                    child = parent;
                }
                parents.Reverse();
            }
            int characters = leaf.Name.Length + parents.Sum(part => part.Name.Length + 1);
            if (characters > 8192) return null;
            var target = new AnalysisPrimaryContentTarget(leaf.Id, directory, [.. parents], leaf.Name, leaf.ObservedIdentity!,
                version, leaf.Times!.CreatedUtc!.Value, 1, (long)size);
            return new(root, target, string.Join('\\', parents.Select(part => part.Name).Append(leaf.Name)));
        }

        void CheckSize()
        {
            if (retainedBytes > AnalysisLimits.ReviewLocatorBytes)
                throw new InvalidOperationException("Narrow the marked scope before reviewing content paths.");
        }

        static bool HasBinding(AnalysisPathMetadata path, string root) => path.RootId == root && path is
            { Cloud: false, IncludedByPolicy: true, MetadataState: MeasurementState.Known, ObservedIdentity.Length: > 0,
                Times: { CreatedUtc: not null, CreatedPrecisionTicks: 1 } };
        static bool OrdinaryName(AnalysisPathMetadata path) => path.NameEncoding == "utf16le" &&
            AnalysisNames.IsOrdinaryChild(path.Name, windows: true) &&
            path.RawName.AsSpan().SequenceEqual(AnalysisRawNames.Encode(path.NameEncoding, path.Name));
    }

    private void CheckDuplicateRuntime(AnalysisSession session)
    {
        ObjectDisposedException.ThrowIf(_close is not null, this);
        if (!CanReviewContentReads || !ReferenceEquals(_session, session))
            throw new InvalidOperationException("The live analysis changed. Reopen duplicate review from the current result.");
    }

    private async Task<AnalysisDuplicateReadResult> ExecuteDuplicateReadAsync(AnalysisSession session, SessionIdentity identity,
        AnalysisDuplicateReadScope scope, CancellationToken cancellation)
    {
        IDisposable work;
        string[] locations;
        lock (_gate)
        {
            CheckDuplicateRuntime(session);
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
                    lock (_gate) CheckDuplicateRuntime(session);
                    var root = group.First().Root;
                    if (!int.TryParse(root.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal) ||
                        ordinal < 1 || ordinal > locations.Length || ordinal.ToString(CultureInfo.InvariantCulture) != root.Id)
                        throw new InvalidDataException("The reviewed location has no matching runtime binding.");
                    string location = locations[ordinal - 1];
                    var device = (AnalysisIoDevices)PathUtil.GetDeviceKey(location);
                    WindowsAnalysisBackend? source = null;
                    try
                    {
                        try
                        {
                            source = await AnalysisIo.Run(_services.Io, device, _ => new WindowsAnalysisBackend(location),
                                cancellation).ConfigureAwait(false);
                            var now = source.Root;
                            if (now.Assurance != IdentityAssurance.Verified || now.Scheme != root.Scheme || now.Kind != root.Kind ||
                                now.CanonicalLocator != root.CanonicalLocator || now.SourceIdentity != root.SourceIdentity ||
                                now.NameRules != root.NameRules || now.TraversalBoundary != root.TraversalBoundary ||
                                source.RootEntry.Identity != root.ObservedObjectIdentity)
                                throw new AnalysisSourceChangedException();
                        }
                        catch (Exception error) when (ReadFailure(error) is not null)
                        {
                            foreach (var item in group) rows.Add(new(item, ReadFailure(error)!.Value));
                            continue;
                        }
                        foreach (var item in group)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            lock (_gate) CheckDuplicateRuntime(session);
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
                lock (_gate) CheckDuplicateRuntime(session);
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
