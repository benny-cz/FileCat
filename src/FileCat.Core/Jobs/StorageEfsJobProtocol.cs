using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

/// <summary>EFS needs a pathname protocol, distinct from the ordinary held compression adapter.</summary>
internal interface IStorageEfsJobAdapter
{
    StorageProblem? ScopeUnavailable(StorageScope scope);
    IStorageTraversal OpenReviewedEfsRoot(IStorageReviewLease root, Job job, int sourceIndex,
        StorageEfsRecoverySession recovery, CancellationToken ct);
}

internal interface IStorageEfsObjectLease : IStorageObjectLease
{
    StorageEfsRecoveryApproval? RecoveryApproval { get; }
}

/// <summary>One lazy recovery choice shared inside the synchronous owner of a reviewed EFS job.</summary>
internal sealed class StorageEfsRecoverySession(StorageEfsConsent consent)
{
    private StorageEfsRecoveryApproval? _approval;
    internal StorageEfsConsent Consent { get; } = consent;

    internal StorageEfsRecoveryApproval Resolve(Job job, string invokerSid,
        Func<Job, string, StorageEfsConsent, CancellationToken, StorageEfsRecoveryApproval?> decide, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var choice = _approval ?? decide(job, invokerSid, Consent, ct);
        if (choice is null) job.Cancel();
        job.Token.ThrowIfCancellationRequested();
        ct.ThrowIfCancellationRequested();
        if (choice is null || !choice.Matches(Consent, invokerSid))
            throw new StorageInspectionException(new(StorageReasonKind.IdentityChanged, "efs-recovery-scope",
                "The EFS recovery choice belongs to a different invoking user or review.", true));
        return _approval = choice;
    }
}
