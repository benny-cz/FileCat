using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

internal sealed record StorageEfsRecoveryKeyOption(string CertificateSha1, string? Subject, bool RequiresProviderAccess = false);

internal sealed record StorageEfsRecoverySelection(string CertificateSha1, nint OwnerWindow);

internal sealed record StorageEfsRecoveryDecision(string Invoker, string InvokerSid, ImmutableArray<StorageEfsRecoveryKeyOption> Keys,
    string? PreviousResult, StorageProblem? CleanupProblem, string? RetainedSetupPath, string? SelectedCertificateSha1 = null)
    : DecisionRequest("Choose EFS recovery", "Choose how you can recover the reviewed EFS key.")
{
    public override string ClassKey => "efs-recovery";
}
