using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

public enum StorageCompleteness { Complete, Incomplete, UnknownRemainder }
public sealed record StorageOutcomeCounts(long Changed = 0, long AlreadySatisfied = 0, long Skipped = 0,
    long Failed = 0, long Uncertain = 0, long NotProcessed = 0)
{
    public long Successful => checked(Changed + AlreadySatisfied);
    public long Processed => checked(Successful + Skipped + Failed + Uncertain);
    public long Observed => checked(Processed + NotProcessed);
    public StorageOutcomeCounts Add(StorageEffect effect) => effect switch
    {
        StorageEffect.Changed => this with { Changed = checked(Changed + 1) },
        StorageEffect.AlreadySatisfied => this with { AlreadySatisfied = checked(AlreadySatisfied + 1) },
        StorageEffect.Skipped => this with { Skipped = checked(Skipped + 1) },
        StorageEffect.Failed => this with { Failed = checked(Failed + 1) },
        StorageEffect.Uncertain => this with { Uncertain = checked(Uncertain + 1) },
        StorageEffect.NotProcessed => this with { NotProcessed = checked(NotProcessed + 1) },
        _ => throw new ArgumentOutOfRangeException(nameof(effect)),
    };
}

public sealed record StorageReasonCount(StorageProblem Problem, long Count);
public sealed record StorageOutcomeExample(string Path, StorageEffect Effect, StorageProblem? Problem);
public sealed record StorageRootResult(int SourceIndex, StorageObjectIdentity? Identity, StorageCompleteness Completeness, bool Changed);

/// <summary>One coherent outcome, separate from sampled JobIssue rows and from any transfer progress estimate.</summary>
public sealed record StorageResultSnapshot(StorageAction Action, StorageScope Scope, Guid ReviewRevision, StorageOutcomeCounts Counts)
{
    public ImmutableArray<StorageRootResult> Roots { get; init; } = [];
    public ImmutableArray<StorageReasonCount> Reasons { get; init; } = [];
    public StorageCompleteness Discovery { get; init; } = StorageCompleteness.UnknownRemainder;
    public bool Canceled { get; init; }
    public bool PersistenceFault { get; init; }
    public bool ReviewPending { get; init; }
    public bool ExecutionRefused { get; init; }
    public ImmutableArray<StorageOutcomeExample> Examples { get; init; } = [];
    public long OmittedExamples { get; init; }
    public long AmbiguousDuplicates { get; init; }
    public bool ObservationCounts { get; init; }
    public StorageJournalPrefix? DurablePrefix { get; init; }
    public string? CurrentPath { get; init; }
    public DateTime? ObservedUtc { get; init; }
    public bool HasIssues => Reasons.Any(r => r.Count > 0) || Counts.Skipped + Counts.Failed + Counts.Uncertain + Counts.NotProcessed > 0;

    public JobState FinalState()
    {
        if (Counts.Uncertain > 0 || PersistenceFault || ReviewPending) return JobState.Failed;
        if (Canceled) return JobState.Canceled;
        bool allComplete = Discovery == StorageCompleteness.Complete && Roots.Length > 0 && Roots.All(r => r.Completeness == StorageCompleteness.Complete);
        if (allComplete && !HasIssues && Counts.Successful > 0) return JobState.Completed;
        if (Counts.Successful > 0) return JobState.CompletedWithIssues;
        if (Counts.Failed > 0 || ExecutionRefused || Reasons.Any(r => r.Count > 0 && r.Problem.IsError)) return JobState.Failed;
        return JobState.CompletedWithIssues;
    }
}
