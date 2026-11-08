using FileCat.Core.Archives;
using FileCat.Core.Resources;

namespace FileCat.Core.Jobs;

public sealed partial class JobManager
{
    internal ArchiveJobAvailability ArchiveAvailability(ArchivePlan plan) => _archiveBackends.Availability(plan);

    /// <summary>Consumes password ownership, including on refusal. Native leases are acquired at execution time.</summary>
    internal Job SubmitArchive(ArchivePlan plan, ArchiveJobPassword? password = null, QueueMode mode = QueueMode.Start,
        string? description = null, Action<Job>? register = null)
    {
        // A rejected second submission must not erase the first job's live password.
        if (password is not null && !password.TryClaimForJob())
            throw new InvalidOperationException("This archive password already belongs to a job.");
        ArchiveJobOperation operation;
        IReadOnlyList<ItemRef> sources = [];
        try
        {
            ArgumentNullException.ThrowIfNull(plan);
            if (plan.Profile is null || !Path.IsPathFullyQualified(plan.ArchivePath) || !Enum.IsDefined(mode) ||
                plan.Changes is null || plan.Changes.Count > ArchiveWritePolicy.MaxMembers)
                throw new ArgumentException("An explicit archive profile, absolute target and bounded changes are required.", nameof(plan));
            ArchiveJobPassword.Require(plan.Profile, password);
            var changes = new ArchiveChange[plan.Changes.Count];
            ItemRef[]? moveSources = plan.MoveSources ? new ItemRef[changes.Length] : null;
            if (moveSources is { Length: 0 }) throw new ArgumentException("An archive move requires selected local sources.", nameof(plan));
            long retainedCharacters = 0;
            for (int i = 0; i < changes.Length; i++)
            {
                var change = plan.Changes[i] ?? throw new ArgumentException("An archive change is missing.", nameof(plan));
                retainedCharacters += (long)(change.MemberPath?.Length ?? 0) + (change.SourcePath?.Length ?? 0) +
                    (change.NewMemberPath?.Length ?? 0);
                if (retainedCharacters > ArchiveWritePolicy.MaxRetainedNameChars)
                    throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded, "The queued archive changes exceed the retained-name limit.");
                changes[i] = change;
                if (moveSources is not null)
                {
                    if (change.Kind is not (ArchiveChangeKind.AddFile or ArchiveChangeKind.AddFolder) ||
                        change.SourcePath is null || !Path.IsPathFullyQualified(change.SourcePath))
                        throw new ArgumentException("Archive moves require explicit local file/folder additions.", nameof(plan));
                    moveSources[i] = ItemRef.ForFileSystemPath(Path.GetFullPath(change.SourcePath), change.Kind == ArchiveChangeKind.AddFolder ? EntryKind.Directory : EntryKind.File);
                }
            }
            if (moveSources is not null) sources = Array.AsReadOnly(moveSources);
            operation = new(plan with { ArchivePath = Path.GetFullPath(plan.ArchivePath), Changes = Array.AsReadOnly(changes) }, password);
        }
        catch { password?.Dispose(); throw; }
        return Submit(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = operation.Plan, Sources = sources, Mode = mode, Description = description },
            null, register, operation);
    }

    private void ReleaseArchiveOperation(Job job)
    {
        ArchiveJobOperation? operation;
        lock (_lock) _archiveOperations.Remove(job, out operation);
        operation?.Dispose();
    }
}
