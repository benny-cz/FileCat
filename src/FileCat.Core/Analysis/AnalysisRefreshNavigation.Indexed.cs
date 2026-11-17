namespace FileCat.Core.Analysis;

public static partial class AnalysisRefreshNavigation
{
    /// <summary>Map navigation through exact captured child indexes, without loading the namespace.</summary>
    public static async Task<ulong?> MapAsync(AnalysisDatasetLease previous, AnalysisDatasetLease current, ulong path,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(current);
        cancellation.ThrowIfCancellationRequested();
        var before = previous.Descriptor; var after = current.Descriptor;
        if (before.Identity.SessionId != after.Identity.SessionId || after.Identity.Epoch != before.Identity.Epoch + 1) return null;
        if (path == 0) return 0;
        await using var oldOwner = await previous.RetainAsync(cancellation).ConfigureAwait(false);
        await using var newOwner = await current.RetainAsync(cancellation).ConfigureAwait(false);
        var selected = await oldOwner.ReadPathAsync(path, cancellation).ConfigureAwait(false);
        if (selected.ObservedIdentity is null || selected.Times is not { CreatedUtc: not null, CreatedPrecisionTicks: not null }) return null;
        var root = before.Roots.SingleOrDefault(r => r.Id == selected.RootId);
        if (root?.SourceIdentity is null || root.ObservedObjectIdentity is null) return null;
        int ordinal = -1;
        for (int i = 0; i < after.Roots.Length; i++)
        {
            var candidate = after.Roots[i];
            if (candidate.Scheme != root.Scheme || candidate.CanonicalLocator != root.CanonicalLocator || candidate.SourceIdentity != root.SourceIdentity ||
                candidate.ObservedObjectIdentity != root.ObservedObjectIdentity || candidate.NameRules != root.NameRules) continue;
            if (ordinal != -1) return null;
            ordinal = i;
        }
        if (ordinal == -1) return null;
        var chain = new List<AnalysisPathMetadata>(); long bytes = 0;
        var node = selected;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            // This is an optional navigation hint. Oversized ancestry falls back to the root;
            // it cannot borrow an unbounded allocation from the full captured namespace.
            bytes = checked(bytes + 256L + node.Name.Length * 2L + node.RawName.Length);
            if (node.RootId != selected.RootId || chain.Count > AnalysisLimits.Depth || bytes > AnalysisLimits.BatchBytes) return null;
            chain.Add(node);
            if (node.ParentId == 0) break;
            node = await oldOwner.ReadPathAsync(node.ParentId, cancellation).ConfigureAwait(false);
        }
        ulong parent = 0; AnalysisPathMetadata? matched = null;
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            matched = await newOwner.FindRefreshChildAsync(ordinal, parent, chain[i], cancellation).ConfigureAwait(false);
            if (matched is null) return null;
            parent = matched.Id;
        }
        return matched is not null && matched.Kind == selected.Kind && matched.ObservedIdentity == selected.ObservedIdentity &&
            matched.Times?.CreatedUtc == selected.Times.CreatedUtc && matched.Times?.CreatedPrecisionTicks == selected.Times.CreatedPrecisionTicks
            ? matched.Id : null;
    }
}

public sealed partial class AnalysisDatasetLease
{
    internal Task<AnalysisPathMetadata?> FindRefreshChildAsync(int root, ulong parent, AnalysisPathMetadata expected, CancellationToken cancellation) =>
        _owned.RunAsync<AnalysisPathMetadata?>((reader, token) =>
        {
            if ((uint)root >= (uint)Descriptor.Roots.Length) throw new ArgumentOutOfRangeException(nameof(root));
            reader.RequireParent(parent, token);
            var keyNode = new AnalysisNode(0, Descriptor.Roots[root].Id, parent, expected.Name, expected.Kind)
            { NameEncoding = expected.NameEncoding, RawName = expected.RawName };
            var range = reader.Find(AnalysisIndexedGraphRevision.Children, AnalysisGraphKeys.Child(new(keyNode, root, 0)), cancellation: token);
            if (range.Count != 1) return null;
            var actual = reader.ReadPathRow(reader.ReadIndex(AnalysisIndexedGraphRevision.Children, range.First, token).Value, token);
            return actual.ParentId == parent && actual.RootId == Descriptor.Roots[root].Id && actual.Name == expected.Name && actual.NameEncoding == expected.NameEncoding &&
                (actual.RawName.IsEmpty ? AnalysisRawNames.Encode(actual.NameEncoding, actual.Name).AsSpan() : actual.RawName.AsSpan())
                    .SequenceEqual(expected.RawName.IsEmpty ? AnalysisRawNames.Encode(expected.NameEncoding, expected.Name) : expected.RawName.AsSpan())
                ? AnalysisPathMetadata.From(actual) : null;
        }, cancellation);
}
