using System.Collections.Immutable;
using System.Numerics;
using FileCat.Core.Analysis;

namespace FileCat.App.Services;

internal sealed record AnalysisDuplicateReadItem(RootBinding Root, AnalysisPrimaryContentTarget Target, string RelativePath);

internal sealed record AnalysisDuplicateReadScope(ImmutableArray<AnalysisDuplicateReadItem> Items, BigInteger Bytes,
    int WithoutSizePeer)
{
    internal const long MaximumBytes = 8L * 1024 * 1024 * 1024;
    internal bool CanRead => Items.Length >= 2 && Bytes <= MaximumBytes;
}

internal enum AnalysisDuplicateReadState { Read, Changed, Unreadable, Unavailable }

internal sealed record AnalysisDuplicateReadRow(AnalysisDuplicateReadItem Item, AnalysisDuplicateReadState State,
    AnalysisPrimaryContentProof? Proof = null);

internal sealed record AnalysisDuplicateReadResult(SessionIdentity Identity, ImmutableArray<AnalysisDuplicateReadRow> Rows)
{
    internal ImmutableArray<ImmutableArray<AnalysisDuplicateReadRow>> Groups =>
        [.. Rows.Where(row => row.Proof is { Kind: AnalysisContentEvidenceKind.StableContentProof })
            .GroupBy(row => (row.Proof!.BytesRead, row.Proof.Sha256))
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Key.BytesRead)
            .ThenBy(group => group.Min(row => row.Item.Target.PathId))
            .Select(group => group.OrderBy(row => row.Item.Target.PathId).ToImmutableArray())];
}

internal sealed class AnalysisDuplicateReadPlan(SessionIdentity identity, ImmutableArray<AnalysisDuplicateReadItem> items,
    int withoutBinding, Func<AnalysisDuplicateReadScope, CancellationToken, Task<AnalysisDuplicateReadResult>> run)
{
    private int _used;
    internal SessionIdentity Identity { get; } = identity;
    internal ImmutableArray<AnalysisDuplicateReadItem> Items { get; } = items;
    internal int WithoutBinding { get; } = withoutBinding;

    internal AnalysisDuplicateReadScope Select(ImmutableHashSet<ulong> selected)
    {
        var choices = Items.Where(item => selected.Contains(item.Target.PathId)).ToArray();
        if (choices.Length != selected.Count) throw new InvalidOperationException("The selected entries differ from the captured read review.");
        var peers = choices.GroupBy(item => item.Target.Bytes).Where(group => group.Count() > 1).SelectMany(group => group)
            .OrderBy(item => item.Target.PathId).ToImmutableArray();
        return new(peers, peers.Aggregate(BigInteger.Zero, (bytes, item) => bytes + item.Target.Bytes), choices.Length - peers.Length);
    }

    internal Task<AnalysisDuplicateReadResult> ReadAsync(ImmutableHashSet<ulong> confirmed, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var scope = Select(confirmed);
        if (!scope.CanRead) throw new InvalidOperationException("Choose at least two same-size files within the read limit.");
        if (Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("Open a new content review to confirm another read.");
        return run(scope, cancellation);
    }
}
