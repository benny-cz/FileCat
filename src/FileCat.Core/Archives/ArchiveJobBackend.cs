using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Archives;

/// <summary>Trusted application composition supplies only backends qualified for this process and native recipe.</summary>
internal interface IArchiveJobBackend
{
    ArchiveWriteFormat Format { get; }
    ArchiveJobAvailability Availability(ArchivePlan plan);
    ArchiveTargetReviewer? TargetReviewer => null;
    // Return only after all workers, borrowed secret grants and native transaction/cleanup leases have settled.
    void Execute(ArchiveJobExecution execution);
}

internal sealed record ArchiveJobAvailability(bool Available, string? Reason = null)
{
    internal static ArchiveJobAvailability Ready { get; } = new(true);
    internal static ArchiveJobAvailability Unqualified { get; } = new(false,
        "Writing this archive profile is not available until its worker and safe-save support are qualified. The archive was not changed.");
}

/// <summary>A borrowed execution context, never part of a request, journal, session or finished-job history.</summary>
internal sealed record ArchiveJobExecution(Job Job, ArchivePlan Plan, ArchiveJobPassword? Password,
    IFileSystemOperations Files, JobJournal Journal);

/// <summary>Per-manager typed dispatch. A missing or refusing backend never enters the legacy ZIP route.</summary>
internal sealed class ArchiveJobBackends
{
    private readonly IReadOnlyDictionary<ArchiveWriteFormat, IArchiveJobBackend> backends;
    internal static ArchiveJobBackends Empty { get; } = new([]);

    internal ArchiveJobBackends(IEnumerable<IArchiveJobBackend> backends)
    {
        ArgumentNullException.ThrowIfNull(backends);
        var captured = new Dictionary<ArchiveWriteFormat, IArchiveJobBackend>();
        foreach (var backend in backends)
        {
            ArgumentNullException.ThrowIfNull(backend);
            if (!Enum.IsDefined(backend.Format) || !captured.TryAdd(backend.Format, backend))
                throw new ArgumentException("An archive format must have one explicit backend.", nameof(backends));
        }
        this.backends = captured;
    }

    internal ArchiveJobAvailability Availability(ArchivePlan plan) =>
        plan.Profile is { } profile && backends.TryGetValue(profile.Format, out var backend)
            ? backend.Availability(plan) : ArchiveJobAvailability.Unqualified;

    internal bool CanReview(ArchivePlan plan) => Availability(plan).Available && plan.Profile is { } profile &&
        backends.TryGetValue(profile.Format, out var backend) && backend.TargetReviewer is not null;

    internal Task<ArchiveTargetReview> ReviewUpdateAsync(string path, ArchiveWriteProfile profile, ArchiveJobPassword? password, CancellationToken ct,
        bool preserveEncryption = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var probe = new ArchivePlan(path, null, [], Profile: profile);
        var state = Availability(probe);
        if (!state.Available || !backends.TryGetValue(profile.Format, out var backend) || backend.TargetReviewer is not { } reviewer)
            throw new ArchiveWriteException(ArchiveWriteReason.WorkerUnavailable, state.Reason ?? ArchiveJobAvailability.Unqualified.Reason!);
        return reviewer.ReviewAsync(path, profile, password, ct, preserveEncryption);
    }

    internal IJobExecutor Create(Job job, IFileSystemOperations fs, JobJournal journal, ArchiveJobOperation? operation)
    {
        if (operation is null || !ReferenceEquals(job.Request.Archive, operation.Plan))
            return new RefusedExecutor(job, fs, journal, ArchiveJobAvailability.Unqualified.Reason!);
        return new BackendExecutor(this, job, fs, journal, operation);
    }

    private sealed class BackendExecutor(ArchiveJobBackends owner, Job job, IFileSystemOperations fs,
        JobJournal journal, ArchiveJobOperation operation) : IJobExecutor
    {
        public void Execute()
        {
            job.Checkpoint();
            var available = owner.Availability(operation.Plan);
            if (!available.Available)
            {
                new RefusedExecutor(job, fs, journal, available.Reason ?? ArchiveJobAvailability.Unqualified.Reason!).Execute();
                return;
            }
            var profile = operation.Plan.Profile!;
            job.Checkpoint();
            ArchiveJobPassword.Require(profile, operation.Password);
            owner.backends[profile.Format].Execute(new(job, operation.Plan, operation.Password, fs, journal));
        }
    }
}

/// <summary>Queue ownership ends after execution and reconciliation, before publishing a terminal state.</summary>
internal sealed class ArchiveJobOperation(ArchivePlan plan, ArchiveJobPassword? password) : IDisposable
{
    internal ArchivePlan Plan { get; } = plan;
    private ArchiveJobPassword? password = password;
    internal ArchiveJobPassword? Password => password;
    public void Dispose() => Interlocked.Exchange(ref password, null)?.Dispose();
}
