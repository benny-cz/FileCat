using System.Collections.Immutable;
using System.Globalization;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.Services;

internal sealed record AnalysisRecipeStaging(ImmutableArray<ulong> Paths, int Protected, int Unavailable)
{
    internal string Describe(int added)
    {
        string text = added == 1 ? "Added 1 captured reference to the review basket." :
            $"Added {added:N0} captured references to the review basket.";
        if (Paths.Length != added) text += $" Already in the basket {Paths.Length - added:N0}.";
        if (Protected != 0) text += $" Protected entries kept {Protected:N0}.";
        if (Unavailable != 0) text += $" Changed or unverified entries kept {Unavailable:N0}.";
        return text;
    }
}

internal sealed partial class AnalysisIndexedAcquisition
{
    private enum CandidateCheck { Qualified, Protected, Unavailable }
    private sealed record CandidateBinding(RootBinding Root, AnalysisPathMetadata Leaf,
        AnalysisBranchComponent? RootDirectory, AnalysisBranchComponent[] Parents);

    internal bool CanStageRecipe => HasRuntimeRescanContext && !_imports.HasUnconfirmedClose;

    // Admission returns reviewed observation IDs only. A future operation must rebind
    // its whole approved scope independently and may never treat these IDs as a grant.
    internal async Task<AnalysisRecipeStaging> QualifyRecipeStagingAsync(AnalysisRecipePreview preview, ImmutableArray<ulong> selected,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> read, CancellationToken cancellation)
    {
        if (!preview.SupportsStaging || selected.IsDefaultOrEmpty || selected.Length > AnalysisRecipePreviews.MaximumRows ||
            selected.Length != selected.Distinct().Count())
            throw new ArgumentException("Choose distinct file candidates from the current preview.", nameof(selected));

        var eligible = preview.Rows.Where(preview.CanInclude).ToDictionary(row => row.PathId);
        if (selected.Any(id => !eligible.ContainsKey(id)))
            throw new ArgumentException("An included entry is outside the current preview.", nameof(selected));

        AnalysisSession session;
        IDisposable work;
        string[] locations;
        lock (_gate)
        {
            if (!CanStageRecipe) throw new InvalidOperationException("Staging needs a finished live analysis.");
            session = _session!;
            locations = (string[])_runtimeLocations.Clone();
            work = _imports.Enter();
        }
        using (work)
        {
            try
            {
                if (session.DatasetDescriptor is { } descriptor)
                {
                    RequireRevision(descriptor.Identity, descriptor.Phase, descriptor.Origin, descriptor.Taint);
                    return await CheckAsync(descriptor.Roots).ConfigureAwait(false);
                }

                using var retained = session.AcquireSnapshot();
                var snapshot = retained.Snapshot;
                RequireRevision(snapshot.Identity, snapshot.Phase, snapshot.Origin, snapshot.Taint);
                return await CheckAsync(snapshot.Roots).ConfigureAwait(false);
            }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                _imports.RecordUnconfirmedClose(error);
                throw;
            }
        }

        void RequireRevision(SessionIdentity identity, AnalysisPhase phase, AnalysisOrigin origin, AnalysisTaint taint)
        {
            if (identity != preview.Identity || phase != AnalysisPhase.Finished || origin != AnalysisOrigin.LiveSession ||
                taint != AnalysisTaint.OrdinarySensitive)
                throw new InvalidOperationException("Open the recipe from the current finished live observation.");
        }

        async Task<AnalysisRecipeStaging> CheckAsync(ImmutableArray<RootBinding> roots)
        {
            var protectedIds = await _services.AnalysisProtection.CaptureDirectoryIdentitiesAsync(cancellation).ConfigureAwait(false);
            var paths = ImmutableArray.CreateBuilder<ulong>();
            int blocked = 0, unavailable = 0;
            foreach (ulong id in selected)
            {
                cancellation.ThrowIfCancellationRequested();
                var binding = await ReadCandidateBindingAsync(eligible[id], roots, read, cancellation).ConfigureAwait(false);
                if (binding is null || !int.TryParse(binding.Root.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal) ||
                    ordinal < 1 || ordinal > locations.Length || ordinal.ToString(CultureInfo.InvariantCulture) != binding.Root.Id)
                {
                    unavailable++;
                    continue;
                }
                string location = locations[ordinal - 1];
                CandidateCheck result;
                try
                {
                    result = await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(location),
                        token => CheckCandidate(location, binding, protectedIds, token), cancellation).ConfigureAwait(false);
                }
                catch (Exception error) when (!AnalysisCloseUnconfirmedException.IsPresent(error) &&
                    error is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    result = CandidateCheck.Unavailable;
                }
                switch (result)
                {
                    case CandidateCheck.Qualified: paths.Add(id); break;
                    case CandidateCheck.Protected: blocked++; break;
                    default: unavailable++; break;
                }
            }

            cancellation.ThrowIfCancellationRequested();
            lock (_gate)
                if (!CanStageRecipe || !ReferenceEquals(_session, session))
                    throw new InvalidOperationException("The live observation changed while reviewing candidates.");
            return new(paths.ToImmutable(), blocked, unavailable);
        }
    }

    private static async Task<CandidateBinding?> ReadCandidateBindingAsync(AnalysisRecipeCandidate row, ImmutableArray<RootBinding> roots,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> read, CancellationToken cancellation)
    {
        var root = roots.FirstOrDefault(item => item.Id == row.RootId);
        if (root is not { Scheme: "file", Kind: AnalysisKind.Directory or AnalysisKind.File, Assurance: IdentityAssurance.Verified,
                Disposition: AnalysisRootDisposition.Active, ObservedObjectIdentity: not null, SourceIdentity: not null })
            return null;
        var leaf = await read(row.PathId, cancellation).ConfigureAwait(false);
        if (leaf.Id != row.PathId || leaf.Kind != AnalysisKind.File || !CanBind(leaf) || leaf.Times?.ChangedUtc is null)
            return null;
        if (leaf.ParentId == 0)
            return root.Kind == AnalysisKind.File && leaf.ObservedIdentity == root.ObservedObjectIdentity
                ? new(root, leaf, null, []) : null;
        if (root.Kind != AnalysisKind.Directory || !HasOrdinaryName(leaf)) return null;

        var child = leaf;
        var parents = new List<AnalysisBranchComponent>();
        for (int depth = 0; depth < AnalysisBranchComponent.MaximumDepth; depth++)
        {
            cancellation.ThrowIfCancellationRequested();
            if (child.ParentId >= child.Id) return null;
            var parent = await read(child.ParentId, cancellation).ConfigureAwait(false);
            if (parent.Id != child.ParentId || parent.Kind != AnalysisKind.Directory || !CanBind(parent)) return null;
            var times = parent.Times!;
            var component = new AnalysisBranchComponent(parent.Name, parent.ObservedIdentity!, times.CreatedUtc!.Value, times.CreatedPrecisionTicks!.Value);
            if (parent.ParentId == 0)
            {
                if (parent.ObservedIdentity != root.ObservedObjectIdentity) return null;
                parents.Reverse();
                return new(root, leaf, component, parents.ToArray());
            }
            if (!HasOrdinaryName(parent)) return null;
            parents.Add(component);
            child = parent;
        }
        return null;

        bool CanBind(AnalysisPathMetadata path) => path.RootId == root.Id && path is { Cloud: not true, IncludedByPolicy: true,
            MetadataState: MeasurementState.Known, ObservedIdentity.Length: > 0, Times: { CreatedUtc: not null, CreatedPrecisionTicks: > 0 } };
        bool HasOrdinaryName(AnalysisPathMetadata path) =>
            path.NameEncoding == (OperatingSystem.IsWindows() ? "utf16le" : "unix-bytes") &&
            AnalysisNames.IsOrdinaryChild(path.Name, OperatingSystem.IsWindows()) &&
            path.RawName.AsSpan().SequenceEqual(AnalysisRawNames.Encode(path.NameEncoding, path.Name));
    }

    private static CandidateCheck CheckCandidate(string location, CandidateBinding binding, IReadOnlySet<string> protectedIds, CancellationToken cancellation)
    {
        var source = PrepareRoot(location);
        using var ownedSource = new AnalysisWorkerClose(source);
        var root = binding.Root;
        if (source.Root.Assurance != IdentityAssurance.Verified || source.Root.SourceIdentity != root.SourceIdentity ||
            source.Root.CanonicalLocator != root.CanonicalLocator || source.Root.NameRules != root.NameRules ||
            source.Root.TraversalBoundary != root.TraversalBoundary || source is UnixAnalysisBackend unixSource && !unixSource.HasWholeCandidateMount())
            return CandidateCheck.Unavailable;
        if (HasProtectedAncestor(RuntimeReopenLocator(source), protectedIds, cancellation))
            return CandidateCheck.Protected;
        if (binding.RootDirectory is not { } rootDirectory)
            return MatchesFile(source, binding.Leaf) && source.ContainsRoot(source) == AnalysisRootContainment.Covered
                ? CandidateCheck.Qualified : CandidateCheck.Unavailable;
        rootDirectory.RequireMatch(source.RootEntry);
        if (protectedIds.Contains(rootDirectory.Identity) || binding.Parents.Any(part => protectedIds.Contains(part.Identity)))
            return CandidateCheck.Protected;

        IAnalysisBackend? branch = binding.Parents.Length == 0 ? null : source switch
        {
            WindowsAnalysisBackend windows => windows.OpenBranch(binding.Parents, cancellation),
            UnixAnalysisBackend unix => unix.OpenBranch(binding.Parents, cancellation),
            _ => throw new NotSupportedException("This source cannot bind the recorded folder.")
        };
        try
        {
            var parent = branch ?? source;
            var current = PrepareRoot(Path.Combine(RuntimeReopenLocator(parent), binding.Leaf.Name));
            using var ownedFile = new AnalysisWorkerClose(current);
            if (AnalysisProtectedObjects.IsSystemManagedLocation(RuntimeReopenLocator(current)))
                return CandidateCheck.Protected;
            if (!MatchesFile(current, binding.Leaf) ||
                !AnalysisRootPaths.TryRelative(parent.Root, current.Root, StringComparison.Ordinal, out string relative) ||
                relative != binding.Leaf.Name || source.ContainsRoot(current) != AnalysisRootContainment.Covered ||
                branch is not null && branch.ContainsRoot(current) != AnalysisRootContainment.Covered)
                return CandidateCheck.Unavailable;
            return CandidateCheck.Qualified;
        }
        finally { if (branch is not null) AnalysisIo.CloseOnWorker(branch); }
    }

    private static bool MatchesFile(IAnalysisBackend current, AnalysisPathMetadata captured)
    {
        var leaf = current.RootEntry;
        var times = captured.Times!;
        return current.Root.Assurance == IdentityAssurance.Verified && leaf is { Kind: AnalysisKind.File, Cloud: not true } &&
            leaf.Identity == captured.ObservedIdentity && leaf.CreatedUtc == times.CreatedUtc && leaf.ChangedUtc == times.ChangedUtc &&
            leaf.CreatedPrecisionTicks == times.CreatedPrecisionTicks && leaf.ChangedPrecisionTicks == times.ChangedPrecisionTicks &&
            leaf.Links.Value == 1;
    }

    private static bool HasProtectedAncestor(string location, IReadOnlySet<string> protectedIds, CancellationToken cancellation)
    {
        if (AnalysisProtectedObjects.IsSystemManagedLocation(location)) return true;
        string? path = Path.GetDirectoryName(location);
        for (int depth = 0; path is not null; depth++, path = Path.GetDirectoryName(path))
        {
            cancellation.ThrowIfCancellationRequested();
            if (depth >= AnalysisLimits.Depth) throw new NotSupportedException("The source ancestry exceeds its review limit.");
            var directory = PrepareRoot(path);
            using var owned = new AnalysisWorkerClose(directory);
            if (directory.RootEntry is not { Kind: AnalysisKind.Directory, Cloud: not true, Identity: { } id } ||
                directory.Root.Assurance != IdentityAssurance.Verified)
                throw new NotSupportedException("A source ancestor could not be verified.");
            directory.ValidateReadScope(directory.RootEntry);
            if (protectedIds.Contains(id)) return true;
        }
        return false;
    }
}
