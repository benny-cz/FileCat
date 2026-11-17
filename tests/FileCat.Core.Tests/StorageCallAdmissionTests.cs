using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StorageCallAdmissionTests
{
    private static readonly StorageObservation Before = new(StorageObservationState.Disabled, StorageObservationState.Disabled, new(1, "live"), false, null);
    private static (Job Job, StorageJournalHeader Header) Make()
    {
        Guid revision = Guid.NewGuid(); var request = new JobRequest
        {
            Kind = JobKind.StorageTransform, Sources = [new(Location.FileSystem(@"C:\source"), "file", EntryKind.File)],
            StorageTransform = new(StorageAction.Compress, StorageScope.SelectedItems, revision) { Roots = [new(0, 0, Before)] },
        };
        var job = new Job(request, "Compression test", "test", [], []); job.SetState(JobState.Queued); job.SealAdmission();
        Assert.Equal(JobStartDisposition.Started, job.TryStart());
        return (job, JobJournal.CaptureStorageHeader(request, job.Id, job.CreatedUtc));
    }
    private static StorageMutationResult Changed() => new(Before, Before with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null) { NativeCallAttempted = true };

    [Fact]
    public void Stop_before_admission_pairs_NotAttempted_and_admits_no_call()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var call = writer.FlushIntent(0, @"C:\source\file", Before); job.Cancel();
        Assert.Equal(StorageCallAdmission.Canceled, job.TryAdmitStorageCall(call));
        writer.FlushNotAttempted(call, StorageNoCallReason.Canceled);
        Assert.Equal(StorageCallAdmission.Stale, job.TryAdmitStorageCall(call)); Assert.Null(writer.Collector.Pending);
    }
    [Fact]
    public void Pause_denial_consumes_that_intent_and_resume_requires_a_fresh_durable_intent()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var first = writer.FlushIntent(0, @"C:\source\file", Before); job.Pause();
        Assert.Equal(StorageCallAdmission.Paused, job.TryAdmitStorageCall(first)); job.Resume();
        Assert.Equal(StorageCallAdmission.Stale, job.TryAdmitStorageCall(first));
        writer.FlushNotAttempted(first, StorageNoCallReason.Paused);
        var next = writer.FlushIntent(0, @"C:\source\file", Before);
        Assert.Equal(StorageCallAdmission.Admitted, job.TryAdmitStorageCall(next)); writer.FlushResult(next, Changed());
    }
    [Fact]
    public void Stop_after_admission_cannot_steal_the_call_and_result_flush_still_closes_it()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var call = writer.FlushIntent(0, @"C:\source\file", Before);
        Assert.Equal(StorageCallAdmission.Admitted, job.TryAdmitStorageCall(call)); job.Cancel();
        int invoked = 0; StorageMutationResult Native() { invoked++; return Changed(); }
        writer.FlushResult(call, Native());
        Assert.Equal(1, invoked); Assert.True(call.IsSettled); Assert.Null(writer.Collector.Pending);
        var next = writer.FlushIntent(0, @"C:\source\file", Before);
        Assert.Equal(StorageCallAdmission.Canceled, job.TryAdmitStorageCall(next)); writer.FlushNotAttempted(next, StorageNoCallReason.Canceled);
    }
    [Fact]
    public void Another_writer_or_consumed_receipt_never_obtains_job_call_authority()
    {
        var (job, header) = Make(); using var firstData = new MemoryStream(); using var otherData = new MemoryStream();
        using var first = new StorageTransformJournalWriter(firstData, () => { }, header);
        using var other = new StorageTransformJournalWriter(otherData, () => { }, header);
        job.AttachStorageWriter(first); Assert.Throws<InvalidOperationException>(() => job.AttachStorageWriter(other));
        var wrong = other.FlushIntent(0, @"C:\source\file", Before); Assert.Equal(StorageCallAdmission.Stale, job.TryAdmitStorageCall(wrong));
        var call = first.FlushIntent(0, @"C:\source\file", Before); Assert.Equal(StorageCallAdmission.Admitted, job.TryAdmitStorageCall(call));
        Assert.Equal(StorageCallAdmission.Busy, job.TryAdmitStorageCall(call)); first.FlushResult(call, Changed());
        Assert.Equal(StorageCallAdmission.Stale, job.TryAdmitStorageCall(call));
    }

    [Fact]
    public void Faulted_writer_cannot_admit_a_previously_durable_intent()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var call = writer.FlushIntent(0, @"C:\source\file", Before); writer.Fault();
        Assert.Equal(StorageCallAdmission.Stale, job.TryAdmitStorageCall(call)); Assert.True(writer.NeedsRecovery);
    }

    [Fact]
    public void An_unadmitted_or_closed_writer_cannot_accept_native_call_authority()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var call = writer.FlushIntent(0, @"C:\source\file", Before);
        Assert.Throws<InvalidOperationException>(() => writer.FlushResult(call, Changed()));
        Assert.NotNull(writer.Collector.Pending);
        writer.Dispose();
        Assert.Equal(StorageCallAdmission.Stale, job.TryAdmitStorageCall(call));
    }

    [Fact]
    public void Stop_is_visible_and_control_lock_is_free_while_cancellation_callbacks_reenter_admission()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var call = writer.FlushIntent(0, @"C:\source\file", Before);
        StorageCallAdmission? decision = null;
        using var callbackDone = new ManualResetEventSlim();
        using var registration = job.Token.Register(() =>
        {
            var thread = new Thread(() => { decision = job.TryAdmitStorageCall(call); callbackDone.Set(); }) { IsBackground = true };
            thread.Start(); Assert.True(callbackDone.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        });
        job.Cancel(); Assert.True(callbackDone.IsSet); Assert.Equal(StorageCallAdmission.Canceled, decision);
        writer.FlushNotAttempted(call, StorageNoCallReason.Canceled);
    }
}
