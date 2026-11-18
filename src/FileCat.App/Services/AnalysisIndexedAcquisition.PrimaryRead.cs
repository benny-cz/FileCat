using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.Services;

internal sealed record AnalysisPrimaryPreview(AnalysisPrimaryReadItem Item, int BytesShown, string Format, string Content);

internal sealed partial class AnalysisIndexedAcquisition
{
    internal async Task<AnalysisPrimaryPreview> PreviewPrimaryAsync(SessionIdentity identity, ulong path, CancellationToken cancellation)
    {
        AnalysisSession session;
        IDisposable work;
        string[] locations;
        lock (_gate)
        {
            if (!CanReviewContentReads)
                throw new NotSupportedException("Preview requires a finished live analysis on local NTFS.");
            session = _session!;
            locations = (string[])_runtimeLocations.Clone();
            work = _imports.Enter();
        }
        using (work)
        {
            try
            {
                var item = await PreparePreviewTargetAsync(session, identity, path, cancellation).ConfigureAwait(false);
                string location = PrimaryRuntimeLocation(item.Root, locations);
                var protectedIds = await _services.AnalysisProtection.CaptureDirectoryIdentitiesAsync(cancellation).ConfigureAwait(false);
                lock (_gate) RequirePrimaryReadRuntime(session);
                var preview = await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(location), token =>
                {
                    var source = OpenPrimarySource(location, item.Root);
                    using var close = new AnalysisWorkerClose(source);
                    string displayScope = item.Root.Kind == AnalysisKind.Directory ? Path.Combine(location, item.RelativePath) : location;
                    if (AnalysisProtectedObjects.IsSystemManagedLocation(displayScope) || HasProtectedAncestor(location, protectedIds, token) ||
                        item.Target.RootDirectory is { } root && protectedIds.Contains(root.Identity) ||
                        item.Target.Parents.Any(parent => protectedIds.Contains(parent.Identity)))
                        throw new NotSupportedException("The selected file is within a protected FileCat or system location.");
                    lock (_gate) RequirePrimaryReadRuntime(session);
                    var bytes = source.ReadPrimaryPreview(item.Target, token);
                    try { return FormatPreview(item, bytes); }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                lock (_gate) RequirePrimaryReadRuntime(session);
                return preview;
            }
            catch (Exception error) when (AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                _imports.RecordUnconfirmedClose(error);
                throw;
            }
        }
    }

    private static async Task<AnalysisPrimaryReadItem> PreparePreviewTargetAsync(AnalysisSession session, SessionIdentity identity,
        ulong path, CancellationToken cancellation)
    {
        if (session.DatasetDescriptor is not null)
        {
            await using var owner = await session.AcquireDatasetAsync(cancellation).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The finished indexed revision is unavailable.");
            var descriptor = owner.Descriptor;
            CheckPreviewRevision(descriptor.Identity, descriptor.Origin, descriptor.Phase, descriptor.Freshness, descriptor.Taint);
            return await TargetAsync(descriptor.Roots, owner.ReadPathAsync, owner.ReadObjectAsync, cancellation).ConfigureAwait(false);
        }
        using var captured = session.AcquireSnapshot();
        var snapshot = captured.Snapshot;
        CheckPreviewRevision(snapshot.Identity, snapshot.Origin, snapshot.Phase, snapshot.Freshness, snapshot.Taint);
        return await Task.Run(() => TargetAsync(snapshot.Roots, (id, token) =>
        {
            token.ThrowIfCancellationRequested();
            int first = 0, last = snapshot.Nodes.Length - 1;
            while (first <= last)
            {
                int middle = first + (last - first) / 2;
                var node = snapshot.Nodes[middle];
                if (node.Id == id) return Task.FromResult(AnalysisPathMetadata.From(node));
                if (node.Id < id) first = middle + 1;
                else last = middle - 1;
            }
            throw new AnalysisSourceChangedException();
        }, (id, token) =>
        {
            foreach (var obj in snapshot.Objects)
            {
                token.ThrowIfCancellationRequested();
                if (obj.Id == id) return Task.FromResult<AnalysisObjectMetadata?>(AnalysisObjectMetadata.From(obj));
            }
            return Task.FromResult<AnalysisObjectMetadata?>(null);
        }, cancellation), cancellation).ConfigureAwait(false);

        void CheckPreviewRevision(SessionIdentity current, AnalysisOrigin origin, AnalysisPhase phase, AnalysisFreshness freshness, AnalysisTaint taint)
        {
            if (current != identity) throw new AnalysisSourceChangedException();
            RequireLiveDuplicateRevision(origin, phase, taint);
            if (freshness != AnalysisFreshness.Observed)
                throw new NotSupportedException("Preview requires a fresh captured file version.");
        }

        async Task<AnalysisPrimaryReadItem> TargetAsync(IEnumerable<RootBinding> roots,
            Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath,
            Func<string, CancellationToken, Task<AnalysisObjectMetadata?>> readObject, CancellationToken token)
        {
            var leaf = await readPath(path, token).ConfigureAwait(false);
            var root = roots.SingleOrDefault(candidate => candidate.Id == leaf.RootId);
            if (leaf.Id == path && root is not null &&
                await ReadPrimaryTargetAsync(root, leaf, readPath, readObject, token).ConfigureAwait(false) is { } target)
                return target;
            throw new NotSupportedException("This entry has no qualified current primary-content binding.");
        }
    }

    private static async Task<AnalysisPrimaryReadItem?> ReadPrimaryTargetAsync(RootBinding root, AnalysisPathMetadata leaf,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath,
        Func<string, CancellationToken, Task<AnalysisObjectMetadata?>> readObject, CancellationToken cancellation)
    {
        if (root is not { Scheme: "file", NameRules: AnalysisNameRules.Windows, Assurance: IdentityAssurance.Verified,
                Disposition: AnalysisRootDisposition.Active, ObservedObjectIdentity: not null, SourceIdentity: not null } ||
            leaf.ObjectId is not { } objectId || leaf.Kind != AnalysisKind.File || !HasBinding(leaf, root.Id) || !OrdinaryName(leaf))
            return null;
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
                var parent = await readPath(child.ParentId, cancellation).ConfigureAwait(false);
                if (parent.Id != child.ParentId || parent.Kind != AnalysisKind.Directory || !HasBinding(parent, root.Id) ||
                    (parent.ParentId != 0 && !OrdinaryName(parent))) return null;
                var component = new AnalysisBranchComponent(parent.Name, parent.ObservedIdentity!, parent.Times!.CreatedUtc!.Value, 1);
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
        if (leaf.Name.Length + parents.Sum(part => part.Name.Length + 1) > 8192) return null;
        var target = new AnalysisPrimaryContentTarget(leaf.Id, directory, [.. parents], leaf.Name, leaf.ObservedIdentity!,
            version, leaf.Times!.CreatedUtc!.Value, 1, (long)size);
        return new(root, target, string.Join('\\', parents.Select(part => part.Name).Append(leaf.Name)));

        static bool HasBinding(AnalysisPathMetadata path, string rootId) => path.RootId == rootId && path is
            { Cloud: false, IncludedByPolicy: true, MetadataState: MeasurementState.Known, ObservedIdentity.Length: > 0,
                Times: { CreatedUtc: not null, CreatedPrecisionTicks: 1 } };
        static bool OrdinaryName(AnalysisPathMetadata path) => path.NameEncoding == "utf16le" &&
            AnalysisNames.IsOrdinaryChild(path.Name, windows: true) &&
            path.RawName.AsSpan().SequenceEqual(AnalysisRawNames.Encode(path.NameEncoding, path.Name));
    }

    private static string PrimaryRuntimeLocation(RootBinding root, string[] locations)
    {
        if (!int.TryParse(root.Id, NumberStyles.None, CultureInfo.InvariantCulture, out int ordinal) ||
            ordinal < 1 || ordinal > locations.Length || ordinal.ToString(CultureInfo.InvariantCulture) != root.Id)
            throw new InvalidDataException("The reviewed location has no matching runtime binding.");
        return locations[ordinal - 1];
    }

    private static WindowsAnalysisBackend OpenPrimarySource(string location, RootBinding root)
    {
        var source = new WindowsAnalysisBackend(location);
        try
        {
            var now = source.Root;
            if (now.Assurance != IdentityAssurance.Verified || now.Scheme != root.Scheme || now.Kind != root.Kind ||
                now.CanonicalLocator != root.CanonicalLocator || now.SourceIdentity != root.SourceIdentity ||
                now.NameRules != root.NameRules || now.TraversalBoundary != root.TraversalBoundary ||
                source.RootEntry.Identity != root.ObservedObjectIdentity)
                throw new AnalysisSourceChangedException();
            return source;
        }
        catch { AnalysisIo.CloseOnWorker(source); throw; }
    }

    private static AnalysisPrimaryPreview FormatPreview(AnalysisPrimaryReadItem item, byte[] bytes)
    {
        if (bytes.Length == 0) return new(item, 0, "", "");
        var guess = TextDecoding.Detect(bytes);
        if (!guess.LooksBinary)
        {
            string text = guess.Encoding.GetString(bytes, guess.PreambleLength, bytes.Length - guess.PreambleLength)
                .ReplaceLineEndings("\n").Replace("\t", "    ", StringComparison.Ordinal);
            return new(item, bytes.Length, guess.Encoding.WebName,
                string.Join("\n", text.Split('\n').Select(line => AnalysisNames.Display(line, AnalysisPrimaryContentTarget.PreviewBytes * 4))));
        }
        var hex = new StringBuilder();
        for (int offset = 0; offset < bytes.Length; offset += 16)
        {
            int length = Math.Min(16, bytes.Length - offset);
            hex.Append(offset.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");
            for (int i = 0; i < length; i++)
                hex.Append(bytes[offset + i].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
            hex.AppendLine();
        }
        return new(item, bytes.Length, "Hexadecimal", hex.ToString());
    }
}
