using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StorageObjectCallRunnerTests
{
    private static readonly StorageObservation Before = new(StorageObservationState.Disabled, StorageObservationState.Disabled, new(9, "held"), false, null);
    private static (Job Job, StorageJournalHeader Header) Make()
    {
        var request = new JobRequest
        {
            Kind = JobKind.StorageTransform, Sources = [new(Location.FileSystem(@"C:\source"), "file", EntryKind.File)],
            StorageTransform = new(StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid()) { Roots = [new(0, 0, Before)] },
        };
        var job = new Job(request, "Storage held-call test", "test", [], []); job.SetState(JobState.Queued); job.SealAdmission();
        Assert.Equal(JobStartDisposition.Started, job.TryStart());
        return (job, JobJournal.CaptureStorageHeader(request, job.Id, job.CreatedUtc));
    }
    private sealed class Held : IStorageObjectLease
    {
        internal StorageObservation State = Before;
        internal int Observations, Preparations, Calls;
        internal Func<StorageMutationResult>? Call;
        public string Path => @"C:\source\file";
        public StorageObjectIdentity Identity => State.Identity;
        public StorageObservation Observe(CancellationToken ct) { ct.ThrowIfCancellationRequested(); Observations++; return State; }
        public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Preparations++; return State; }
        public StorageMutationResult ApplyAdmitted(StorageAction action)
        {
            Calls++;
            return Call?.Invoke() ?? new(State, State with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null) { NativeCallAttempted = true };
        }
        public void Dispose() { }
    }

    [Fact]
    public void A_satisfied_object_gets_no_mutation_rights_intent_or_native_call()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var held = new Held { State = Before with { Compression = StorageObservationState.Enabled } };
        var result = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.Equal(StorageEffect.AlreadySatisfied, result.Result.Effect); Assert.Equal(0, held.Preparations); Assert.Equal(0, held.Calls);
        Assert.Equal(1, writer.Prefix!.Sequence); Assert.False(result.MustStop);
    }

    [Fact]
    public void Stop_during_intent_flush_records_a_no_call_and_never_enters_native_mutation()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () => { if (++flushes == 2) job.Cancel(); }, header);
        job.AttachStorageWriter(writer); var held = new Held(); var result = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.True(result.Canceled); Assert.Equal(0, held.Calls); Assert.Equal(StorageEffect.NotProcessed, result.Result.Effect);
        Assert.Equal(StorageNoCallReason.Canceled, writer.Collector.LastOutcome!.NoCallReason); Assert.Null(writer.Collector.Pending);
    }

    [Fact]
    public void Stop_after_admission_keeps_native_return_postcondition_and_result_flush_owned()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () => flushes++, header); job.AttachStorageWriter(writer);
        var held = new Held { Call = () => { job.Cancel(); return new(Before, Before with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null) { NativeCallAttempted = true }; } };
        var result = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.True(result.Canceled); Assert.Equal(StorageEffect.Changed, result.Result.Effect); Assert.Equal(1, held.Calls);
        Assert.Equal(3, flushes); Assert.Equal(StorageCallDisposition.Observed, writer.Collector.LastOutcome!.Disposition); Assert.Null(writer.Collector.Pending);
    }

    [Fact]
    public async Task Pause_after_intent_consumes_it_and_resume_reobserves_and_prepares_before_a_new_intent()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () => { if (++flushes == 2) job.Pause(); }, header);
        job.AttachStorageWriter(writer); var held = new Held();
        var worker = Task.Run(() => StorageObjectCallRunner.Run(job, writer, 0, held), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(SpinWait.SpinUntil(() => job.State == JobState.Paused, TimeSpan.FromSeconds(5)));
            Assert.Equal(0, held.Calls); Assert.Equal(StorageNoCallReason.Paused, writer.Collector.LastOutcome!.NoCallReason);
            job.Resume();
            var result = await worker;
            Assert.Equal(StorageEffect.Changed, result.Result.Effect); Assert.Equal(2, held.Observations); Assert.Equal(2, held.Preparations);
            Assert.Equal(2, writer.Collector.LastIntent!.Call); Assert.Equal(1, held.Calls); Assert.Equal(5, flushes);
        }
        finally { job.Cancel(); await worker; }
    }

    [Fact]
    public void A_result_flush_failure_preserves_the_observed_change_and_requires_whole_job_stop()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () => { if (++flushes == 3) throw new IOException("fault fixture"); }, header);
        job.AttachStorageWriter(writer); var held = new Held(); var result = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.Equal(StorageEffect.Changed, result.Result.Effect); Assert.True(result.PersistenceFault); Assert.True(result.MustStop);
        Assert.Equal(1, held.Calls); Assert.Equal(2, writer.Prefix!.Sequence); Assert.True(writer.NeedsRecovery);
    }

    [Fact]
    public void Journal_reservation_failure_stops_before_native_call_and_leaves_fixed_closure_capacity()
    {
        var (job, header) = Make(); using var data = new MemoryStream();
        using var writer = new StorageTransformJournalWriter(data, () => { }, header, recordLimit: 6); job.AttachStorageWriter(writer);
        var held = new Held(); var result = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.True(result.LimitReached); Assert.Equal(0, held.Calls); Assert.Equal(1, writer.Prefix!.Sequence); Assert.Null(writer.Collector.Pending);
    }

    [Fact]
    public void An_exception_from_an_admitted_adapter_is_durable_uncertainty_and_admits_no_next_call()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); using var writer = new StorageTransformJournalWriter(data, () => { }, header);
        job.AttachStorageWriter(writer); var held = new Held { Call = () => throw new IOException("admitted fixture") };
        var result = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.Equal(StorageEffect.Uncertain, result.Result.Effect); Assert.True(result.MustStop); Assert.True(writer.Collector.EffectUncertain);
        Assert.Equal(StorageEffect.Uncertain, writer.Collector.LastOutcome!.Result!.Effect); Assert.Equal(1, held.Calls);
    }
}
