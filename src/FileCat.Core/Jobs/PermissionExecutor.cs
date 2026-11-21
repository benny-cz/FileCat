using System.Collections.Immutable;
using FileCat.Core.Permissions;

namespace FileCat.Core.Jobs;

internal sealed class PermissionExecutor(Job job, JobJournal journal, ReviewedPermissionOperation review) : IJobExecutor
{
    public void Execute()
    {
        if (ReviewedPermissionOperation.ProcessRefusal() is { } refusal) throw new InvalidOperationException(refusal);
        var results = review.Targets.Select((target, index) => new PermissionResult(index, target.Binding?.Identity,
            PermissionVerdict.Unattempted, [])).ToArray();
        job.AddTotals(results.Length, 0);
        try
        {
            for (int index = 0; index < review.Targets.Length && !job.IsCancellationRequested; index++)
            {
                var target = review.Targets[index];
                job.SetCurrent(target.Subject.DisplayPath);
                var components = ImmutableArray.CreateBuilder<PermissionComponentResult>(target.Components.Length);
                PermissionReadBack? observedFinal = null;
                PermissionRevision? finalRevision = null;
                try
                {
                    foreach (var component in target.Components)
                    {
                        job.Checkpoint();
                        if (!RunComponent(index, target, component, components)) break;
                    }

                    // The earlier read-backs remain historical evidence. A later component can invalidate them,
                    // including during a failed compound change or after cancellation was requested.
                    if (components.Any(result => result.NativeCall is not null) || components.Count == target.Components.Length &&
                        components.All(result => result.Verdict == PermissionVerdict.AlreadyMatching))
                    {
                        observedFinal = Observe(target, target.Final.Sections);
                        finalRevision = Revision(observedFinal, target.Final.Sections);
                    }
                    var verdict = Verdict(target, components, observedFinal, finalRevision);
                    results[index] = new(index, target.Binding?.Identity, verdict, components.ToImmutable(),
                        observedFinal is null ? null : LimitDetail(observedFinal.Detail));
                    journal.PermissionObject(index, verdict, target.Final, finalRevision);
                    Count(index, verdict, target.Subject.DisplayPath);
                }
                catch (OperationCanceledException) when (job.IsCancellationRequested)
                {
                    if (components.Count == 0) break;
                    if (components.Any(result => result.NativeCall is not null))
                    {
                        observedFinal = Observe(target, target.Final.Sections);
                        finalRevision = Revision(observedFinal, target.Final.Sections);
                    }
                    var verdict = Verdict(target, components, observedFinal, finalRevision);
                    results[index] = new(index, target.Binding?.Identity, verdict, components.ToImmutable());
                    journal.PermissionObject(index, verdict, target.Final, finalRevision);
                    Count(index, verdict, target.Subject.DisplayPath);
                    break;
                }
                catch
                {
                    if (components.Any(result => result.NativeCall is not null))
                        results[index] = new(index, target.Binding?.Identity, PermissionVerdict.Uncertain, components.ToImmutable(),
                            "A permissions result could not be recorded durably. Inspect the current object before reviewing another change.");
                    else
                        results[index] = new(index, target.Binding?.Identity, PermissionVerdict.FailedBeforeChange, components.ToImmutable());
                    throw;
                }
            }
        }
        finally
        {
            job.SetCurrent(null);
            job.RecordPermissionResults(results.ToImmutableArray());
        }
    }

    private bool RunComponent(int index, ReviewedPermissionTarget target, ReviewedPermissionComponent component,
        ImmutableArray<PermissionComponentResult>.Builder components)
    {
        PermissionReadStatus before = CheckBefore(target, component, out var detail);
        if (before != PermissionReadStatus.Present)
        {
            components.Add(Result(component, before == PermissionReadStatus.Stale ? PermissionVerdict.Conflict : PermissionVerdict.FailedBeforeChange,
                detail: detail ?? "The original object, authority or reviewed values could not be verified."));
            return false;
        }
        if (component.Before == component.Expected)
        {
            components.Add(Result(component, PermissionVerdict.AlreadyMatching, observed: component.Before.Digest));
            return true;
        }

        int step = journal.PermissionIntent(index, target, component);
        PermissionComponentResult result;
        try
        {
            while (true)
            {
                job.Checkpoint();
                // The flush or a pause may have taken time. Recheck the original subject and dependency values.
                before = CheckBefore(target, component, out detail);
                if (before != PermissionReadStatus.Present)
                {
                    result = Result(component, before == PermissionReadStatus.Stale ? PermissionVerdict.Conflict : PermissionVerdict.FailedBeforeChange, detail: detail);
                    break;
                }
                if (job.AdmitPermissionCall())
                {
                    result = Apply(target, component);
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (job.IsCancellationRequested)
        {
            result = Result(component, PermissionVerdict.Unattempted);
        }
        components.Add(result);
        journal.PermissionOutcome(step, result);
        return Applied(result.Verdict);
    }

    private PermissionReadStatus CheckBefore(ReviewedPermissionTarget target, ReviewedPermissionComponent component, out string? detail)
    {
        detail = null;
        try
        {
            var snapshot = target.Mutation.CheckCurrent(component.Before.Sections, job.Token);
            if (target.ServerPath is { } path)
            {
                if (snapshot.ServerState?.Target != path) return PermissionReadStatus.Stale;
            }
            else
            {
                if (snapshot.Binding is null) return PermissionReadStatus.Failed;
                if (target.Binding != snapshot.Binding) return PermissionReadStatus.Stale;
            }
            var revision = PermissionRevision.Read(snapshot, component.Before.Sections);
            if (revision is null) return PermissionReadStatus.Failed;
            if (revision != component.Before) return PermissionReadStatus.Stale;
            if (review.TreeSource is { } source)
            {
                var observed = source.Mutation.CheckCurrent(source.Revision.Sections, job.Token);
                if (observed.Binding?.Identity != source.Binding.Identity || observed.Binding.ReadOnly != false ||
                    PermissionRevision.Read(observed, source.Revision.Sections) != source.Revision) return PermissionReadStatus.Stale;
            }
            return PermissionReadStatus.Present;
        }
        catch (PermissionBindingConflictException error) { detail = LimitDetail(error.Message); return PermissionReadStatus.Stale; }
        catch (IOException) { return PermissionReadStatus.Failed; }
        catch (UnauthorizedAccessException) { return PermissionReadStatus.Denied; }
    }

    private PermissionComponentResult Apply(ReviewedPermissionTarget target, ReviewedPermissionComponent component)
    {
        PermissionMutationReturn? call;
        try { call = target.Mutation.Apply(component.Change, job.Token); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { call = null; }
        if (call?.BindingConflict == true)
            return Result(component, PermissionVerdict.Conflict, error: call.NativeError,
                detail: LimitDetail(call.Detail) ?? "The original binding changed before the native write was dispatched.");
        if (call?.CanceledBeforeChange == true)
            return Result(component, PermissionVerdict.Unattempted, detail: LimitDetail(call.Detail));
        if (call?.FailedWithoutDispatch == true)
            return Result(component, PermissionVerdict.FailedBeforeChange, error: call.NativeError, detail: LimitDetail(call.Detail));
        var readback = Observe(target, component.Before.Sections | component.Expected.Sections);
        PermissionRevision? observed = null;
        PermissionVerdict verdict = PermissionVerdict.Uncertain;
        if (readback is not null)
        {
            observed = Revision(readback, component.Expected.Sections);
            if (observed == component.Expected) verdict = readback.FolderChanged ? PermissionVerdict.AppliedWithFolderDrift : PermissionVerdict.Verified;
            // An unchanged root is insufficient evidence of no descendant effects once Windows has been called.
            else if (review.Scope != PermissionScope.NativePropagation && !readback.Reconnected && call?.RejectedBeforeChange == true &&
                Revision(readback, component.Before.Sections) == component.Before)
                verdict = PermissionVerdict.FailedBeforeChange;
        }
        return Result(component, verdict, component.NativeCall, readback?.Verification ?? PermissionVerification.Unavailable, observed?.Digest,
            call?.NativeError, LimitDetail(readback?.Detail ?? call?.Detail) ?? (verdict == PermissionVerdict.Uncertain
                ? "The requested state could not be verified on the original object." : null));
    }

    private static PermissionRevision? Revision(PermissionReadBack? readback, PermissionSections sections)
    {
        if (readback?.Verification is not (PermissionVerification.SameObject or PermissionVerification.ServerPath)) return null;
        // A helper may have read security that the ordinary process cannot read. Its receipt covers exactly one scope.
        if (readback.AttestedRevision is { } attested)
            return attested.Sections == sections ? attested : null;
        try { return PermissionRevision.Read(readback.Snapshot, sections); }
        catch (InvalidDataException) { return null; }
    }

    private static PermissionReadBack? Observe(ReviewedPermissionTarget target, PermissionSections sections)
    {
        try
        {
            var readback = target.Mutation.Observe(sections);
            bool sameTarget = target.ServerPath is { } path
                ? readback.Verification == PermissionVerification.ServerPath && readback.Snapshot.ServerState?.Target == path
                : readback.Verification == PermissionVerification.SameObject && readback.Snapshot.Binding?.Identity == target.Binding!.Identity;
            if (sameTarget)
                return readback;
            return readback with { Verification = PermissionVerification.Unavailable, FolderChanged = false };
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        { return null; }
    }

    private static PermissionVerdict Verdict(ReviewedPermissionTarget target, IReadOnlyList<PermissionComponentResult> components,
        PermissionReadBack? readback, PermissionRevision? final)
    {
        if (components.Count == 0) return PermissionVerdict.Unattempted;
        bool wrote = components.Any(component => component.Verdict is PermissionVerdict.Verified or PermissionVerdict.AppliedWithFolderDrift);
        if (components.Count == target.Components.Length && components.All(component => Applied(component.Verdict)))
        {
            if (final != target.Final) return wrote && final is not null ? PermissionVerdict.Partial : PermissionVerdict.Uncertain;
            if (!wrote) return PermissionVerdict.AlreadyMatching;
            return readback?.FolderChanged == true || components.Any(component => component.Verdict == PermissionVerdict.AppliedWithFolderDrift)
                ? PermissionVerdict.AppliedWithFolderDrift : PermissionVerdict.Verified;
        }
        if (wrote) return PermissionVerdict.Partial;
        return components[^1].Verdict;
    }

    private void Count(int index, PermissionVerdict verdict, string path)
    {
        if (Applied(verdict)) { job.ItemDone(); job.RootCompleted(index); return; }
        if (verdict is PermissionVerdict.Unattempted or PermissionVerdict.Skipped) { job.ItemSkipped(); return; }
        job.ItemFailed();
        job.RootFailed(index);
        var outcome = verdict switch
        {
            PermissionVerdict.Partial => StepOutcome.PartiallyApplied,
            PermissionVerdict.Uncertain => StepOutcome.Uncertain,
            _ => StepOutcome.Failed
        };
        job.AddIssue(new(IssueSeverity.Error, path, verdict switch
        {
            PermissionVerdict.Conflict => "The original permissions or binding changed. Review the current state before trying again.",
            PermissionVerdict.Partial => "Some components changed. Inspect the current state before reviewing a correction.",
            PermissionVerdict.Uncertain => "The final permissions could not be verified. Inspect the current state.",
            _ => "The reviewed component could not be applied."
        }, outcome));
    }

    private static bool Applied(PermissionVerdict verdict) => verdict is PermissionVerdict.Verified or PermissionVerdict.AlreadyMatching or PermissionVerdict.AppliedWithFolderDrift;
    private static string? LimitDetail(string? detail) => detail is { Length: > 512 } ? detail[..512] : detail;

    private static PermissionComponentResult Result(ReviewedPermissionComponent component, PermissionVerdict verdict, string? call = null,
        PermissionVerification verification = PermissionVerification.NotAttempted, string? observed = null, int? error = null, string? detail = null) =>
        new(component.Change.Section, verdict, call, verification, component.Before.Digest, component.Expected.Digest, observed, error, detail);
}
