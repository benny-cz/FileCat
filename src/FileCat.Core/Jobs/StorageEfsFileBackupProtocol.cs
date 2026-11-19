using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

internal interface IStorageEfsFileBackup
{
    StorageEfsFileBackupReview InspectFiles(ImmutableArray<StorageTarget> targets, CancellationToken ct);
    StorageEfsFileBackupResult BackUpFileKey(StorageEfsFileBackupReview review, string certificateSha1, nint ownerWindow, CancellationToken ct);
}

internal sealed record StorageEfsFileBackupItem(StorageTarget Target, StorageObjectIdentity? Identity,
    ImmutableArray<string> Certificates, StorageProblem? Problem);

internal sealed record StorageEfsFileBackupKey(string CertificateSha1, string? Subject, StorageEfsKeyState State,
    StorageProblem? Problem, bool RequiresProviderAccess = false)
{
    internal bool CanBackUp => State == StorageEfsKeyState.MatchedAvailable ||
        (State == StorageEfsKeyState.InteractionRequired && RequiresProviderAccess);
}

internal sealed record StorageEfsFileBackupReview(IStorageEfsFileBackup Adapter, string InvokerSid,
    ImmutableArray<StorageEfsFileBackupItem> Items, ImmutableArray<StorageEfsFileBackupKey> Keys);

internal sealed record StorageEfsFileBackupResult(bool WindowsReportedExport, bool Canceled,
    ImmutableArray<int> CoveredItems, StorageProblem? Problem);
