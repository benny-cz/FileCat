using System.Collections.Immutable;
using FileCat.Core.Diagnostics;
using FileCat.Core.Permissions;

namespace FileCat.Core.Jobs;

public sealed partial class Job
{
    private ReviewedPermissionOperation? _permissionOperation;
    internal ReviewedPermissionOperation? PermissionOperation
    {
        get => Volatile.Read(ref _permissionOperation);
        init
        {
            _permissionOperation = value;
            if (value is null) return;
            PermissionReviewScope = value.Scope;
            ReviewedTreeSummary = value.Tree?.Summary;
            PermissionTreeTemplate = value.TreeSource?.Subject.DisplayPath;
            PermissionUsesServerPath = value.UsesServerPath;
            PermissionDevices = value.Targets.Select(target =>
                target.ServerPath is { } path ? (PermissionAuthorityKind.RemoteSession, path.Device, 0ul) :
                    (target.Binding!.Identity.Authority.Kind, target.Binding.Identity.Authority.Id, target.Binding.Identity.Object.VolumeSerial)).ToImmutableHashSet();
        }
    }

    internal PermissionScope? PermissionReviewScope { get; private init; }
    internal PermissionTreeSummary? ReviewedTreeSummary { get; private init; }
    internal string? PermissionTreeTemplate { get; private init; }
    internal bool PermissionUsesServerPath { get; private init; }
    internal ImmutableHashSet<(PermissionAuthorityKind Kind, string Authority, ulong Device)> PermissionDevices { get; private init; } = [];

    private IReadOnlyList<PermissionResult> _permissionResults = ImmutableArray<PermissionResult>.Empty;
    public IReadOnlyList<PermissionResult> PermissionResults => Volatile.Read(ref _permissionResults);
    internal void RecordPermissionResults(ImmutableArray<PermissionResult> results) => Volatile.Write(ref _permissionResults, results);
    internal ReviewedPermissionOperation? TakePermissionOperation() => Interlocked.Exchange(ref _permissionOperation, null);

    internal bool AdmitPermissionCall()
    {
        lock (_startLock)
            return State == JobState.Running && _runGate.IsSet && !IsCancellationRequested;
    }
}

public sealed partial class JobManager
{
    internal Job SubmitPermissions(ReviewedPermissionOperation operation, Action<Job>? register = null)
    {
        operation.TransferToJob();
        try
        {
            if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) throw new InvalidOperationException(refusal);
            if (_createPermissionJournal is null) throw new InvalidOperationException("Private permissions history is unavailable.");
            return Submit(new JobRequest
            {
                Kind = JobKind.Permissions, Sources = operation.Targets.Select(target => target.Source).ToImmutableArray(), Mode = QueueMode.Queue
            }, null, register, permissions: operation);
        }
        catch { operation.ReleaseFromJob(); throw; }
    }

    private static bool ReleasePermissionOperation(Job job)
    {
        if (job.TakePermissionOperation() is not { } operation) return true;
        if (job.PermissionResults.Count == 0)
            job.RecordPermissionResults(operation.Targets.Select((target, index) =>
                new PermissionResult(index, target.Binding?.Identity, PermissionVerdict.Unattempted, [])).ToImmutableArray());
        try { operation.ReleaseFromJob(); return true; }
        catch (Exception error)
        {
            AppLog.Warn("Permission binding release could not be confirmed", error);
            job.AddIssue(new(IssueSeverity.Error, "", "Release of the reviewed permission bindings could not be confirmed.", StepOutcome.Uncertain));
            return false;
        }
    }
}
