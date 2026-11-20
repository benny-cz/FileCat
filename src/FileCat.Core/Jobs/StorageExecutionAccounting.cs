using System.Collections.Immutable;
using System.Text.Json;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>One final object/observation disposition; root summaries overlap and are never added to global totals.</summary>
internal sealed class StorageExecutionAccounting(StorageTransformRequest request)
{
    private sealed class Root
    {
        internal StorageOutcomeCounts Counts = new();
        internal bool Reached, Finished, Incomplete, Changed;
    }
    private readonly Root[] _roots = request.Roots.Select(_ => new Root()).ToArray();
    private readonly Dictionary<(StorageReasonKind Kind, string Code, bool Error), StorageReasonCount> _reasons = [];
    private readonly List<StorageOutcomeExample> _examples = [];
    private int _reasonBytes, _exampleBytes;
    private long _omitted;
    internal StorageOutcomeCounts Counts { get; private set; } = new();
    internal bool UnknownRemainder, Canceled, PersistenceFault, ReviewPending;
    internal long AmbiguousDuplicates;
    internal bool ObservationCounts;
    internal bool Reached(ulong sources) => Bits(sources).All(i => _roots[i].Reached);
    internal void Reach(ulong sources) { foreach (int i in Bits(sources)) _roots[i].Reached = true; }
    internal void Incomplete(ulong owners, bool unknown)
    {
        foreach (int i in Bits(owners)) _roots[i].Incomplete = true;
        UnknownRemainder |= unknown;
    }
    internal void Finish(ulong selected, bool complete)
    {
        foreach (int i in Bits(selected)) { _roots[i].Finished = true; _roots[i].Incomplete |= !complete; }
    }
    internal void Outcome(ulong owners, string path, StorageEffect effect, StorageProblem? problem, bool called)
    {
        Counts = Counts.Add(effect);
        foreach (int i in Bits(owners))
        {
            _roots[i].Counts = _roots[i].Counts.Add(effect);
            _roots[i].Changed |= effect == StorageEffect.Changed;
        }
        if (problem is not null) Reason(problem);
        if (!called) Example(new(path, effect, problem));
    }
    internal void Reason(StorageProblem problem)
    {
        var key = (problem.Kind, problem.Code, problem.IsError);
        if (_reasons.TryGetValue(key, out var existing)) { _reasons[key] = existing with { Count = checked(existing.Count + 1) }; return; }
        int bytes = JsonSerializer.SerializeToUtf8Bytes(problem, StorageTransformJournalCollector.Json).Length + 64;
        if (_reasons.Count < 52 && _reasonBytes + bytes <= 16 * 1024)
        { _reasons.Add(key, new(problem, 1)); _reasonBytes += bytes; return; }
        // Reserve twelve bounded aggregate categories (kind/severity). Exact native reasons remain in call records;
        // the summary preserves every occurrence even when its specific code cannot fit.
        var grouped = new StorageProblem(problem.Kind, "other-storage-issues", "Other storage issues in this category; exact native-call causes remain in the call records.", problem.IsError);
        key = (grouped.Kind, grouped.Code, grouped.IsError);
        _reasons[key] = _reasons.TryGetValue(key, out existing) ? existing with { Count = checked(existing.Count + 1) } : new(grouped, 1);
    }
    private void Example(StorageOutcomeExample example)
    {
        if (example.Path.Length > 32767) { _omitted++; return; }
        int bytes = JsonSerializer.SerializeToUtf8Bytes(example, StorageTransformJournalCollector.Json).Length;
        if (example.Problem is not null)
        {
            int optional = _examples.FindLastIndex(item => item.Problem is null);
            if (optional >= 0 && (_examples.Count == 16 || _exampleBytes + bytes > 32 * 1024))
            { _exampleBytes -= JsonSerializer.SerializeToUtf8Bytes(_examples[optional], StorageTransformJournalCollector.Json).Length; _examples.RemoveAt(optional); _omitted++; }
        }
        if (_examples.Count == 16 || _exampleBytes + bytes > 32 * 1024) { _omitted++; return; }
        _examples.Add(example); _exampleBytes += bytes;
    }
    internal StorageRootResult[] RootResults() => request.Roots.Select((root, i) => new StorageRootResult(i, root.Observation.Identity,
        _roots[i].Reached && _roots[i].Finished && !_roots[i].Incomplete ? StorageCompleteness.Complete : StorageCompleteness.Incomplete, _roots[i].Changed)).ToArray();
    internal StorageJournalRootSummary RootSummary(int index)
    {
        var result = RootResults()[index];
        return new(index, StorageJournalCounts.From(_roots[index].Counts), result.Completeness, result.Changed);
    }
    internal StorageResultSnapshot Snapshot(StorageJournalPrefix? prefix)
    {
        var roots = RootResults();
        return new(request.Action, request.Scope, request.ReviewRevision, Counts)
        {
            Roots = roots.ToImmutableArray(), Reasons = _reasons.Values.ToImmutableArray(),
            Discovery = UnknownRemainder ? StorageCompleteness.UnknownRemainder : roots.All(r => r.Completeness == StorageCompleteness.Complete) ? StorageCompleteness.Complete : StorageCompleteness.Incomplete,
            Canceled = Canceled, PersistenceFault = PersistenceFault, ReviewPending = ReviewPending,
            Examples = _examples.ToImmutableArray(), OmittedExamples = _omitted, AmbiguousDuplicates = AmbiguousDuplicates,
            ObservationCounts = ObservationCounts, DurablePrefix = prefix,
        };
    }
    private IEnumerable<int> Bits(ulong mask)
    {
        for (int i = 0; i < _roots.Length; i++) if ((mask & (1UL << i)) != 0) yield return i;
    }
}
