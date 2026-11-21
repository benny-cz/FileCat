using System.Collections.Immutable;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class StorageObjectCallRunnerTests
{
    private static readonly StorageObservation Before = new(StorageObservationState.Disabled, StorageObservationState.Disabled, new(9, "held"), false, null);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static (Job Job, StorageJournalHeader Header) Make() => Make([("file", Before)]);
    private static (Job Job, StorageJournalHeader Header) Make(params (string Name, StorageObservation Observation)[] roots)
    {
        var request = new JobRequest
        {
            Kind = JobKind.StorageTransform, Sources = roots.Select(root => new ItemRef(Location.FileSystem(@"C:\source"), root.Name, EntryKind.File)).ToArray(),
            StorageTransform = new(StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid())
            { Roots = roots.Select((root, i) => new StorageReviewedRoot(i, i, root.Observation)).ToImmutableArray() },
        };
        var job = new Job(request, "Storage held-call test", "test", [], []); job.SetState(JobState.Queued); job.SealAdmission();
        Assert.Equal(JobStartDisposition.Started, job.TryStart());
        return (job, JobJournal.CaptureStorageHeader(request, job.Id, job.CreatedUtc));
    }
    private static StorageObservation Named(string name) => Before with { Identity = new(9, name) };
    private sealed class Held : IStorageObjectLease
    {
        internal StorageObservation State = Before;
        internal int Observations, Preparations, Calls;
        internal Func<StorageMutationResult>? Call;
        internal string Name = "file";
        public string Path => @"C:\source\" + Name;
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

    [Fact]
    public async Task Pause_after_admission_lets_the_admitted_call_return_and_flush_and_writes_no_intent_until_resume()
    {
        var (job, header) = Make(("first", Named("first")), ("second", Named("second")));
        using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () => Interlocked.Increment(ref flushes), header);
        job.AttachStorageWriter(writer);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); var ct = Ct;
        var first = new Held { Name = "first", State = Named("first") };
        first.Call = () =>
        {
            entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(30), ct));
            return new(first.State, first.State with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null) { NativeCallAttempted = true };
        };
        var second = new Held { Name = "second", State = Named("second") };
        // One worker, as the executor drives it: the admitted object, then the next object.
        var worker = Task.Factory.StartNew(() => (StorageObjectCallRunner.Run(job, writer, 0, first), StorageObjectCallRunner.Run(job, writer, 1, second)),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10), Ct));
            job.Pause(); // after atomic admission, while the native call is in flight
            Assert.Equal(JobState.Pausing, job.State); Assert.Equal(1L, writer.Collector.Pending?.Call);
            release.Set();
            Assert.True(SpinWait.SpinUntil(() => job.State == JobState.Paused, TimeSpan.FromSeconds(10)));
            // The admitted call returned and its observed result became durable before the safe pause boundary.
            Assert.Equal(1, first.Calls); Assert.Null(writer.Collector.Pending);
            Assert.Equal(StorageCallDisposition.Observed, writer.Collector.LastOutcome!.Disposition); Assert.Equal(1, writer.Collector.LastOutcome.Call);
            Assert.Equal(3, writer.Prefix!.Sequence); Assert.Equal(3, Volatile.Read(ref flushes));
            // While paused the next object is not observed, prepared or given a durable intent.
            Assert.Equal(0, second.Observations + second.Preparations + second.Calls); Assert.Equal(1, writer.Collector.LastIntent!.Call);
            job.Resume();
            var (admitted, next) = await worker.WaitAsync(TimeSpan.FromSeconds(30), Ct);
            Assert.Equal(StorageEffect.Changed, admitted.Result.Effect); Assert.False(admitted.MustStop); Assert.False(admitted.Canceled);
            Assert.Equal(StorageEffect.Changed, next.Result.Effect); Assert.Equal(1, second.Observations); Assert.Equal(1, second.Calls);
            Assert.Equal(2, writer.Collector.LastIntent!.Call); Assert.Null(writer.Collector.Pending); Assert.Equal(5, Volatile.Read(ref flushes));
        }
        finally { release.Set(); job.Cancel(); }
    }

    [Fact]
    public void Intent_flush_failure_admits_no_native_call_and_keeps_the_unconfirmed_intent_pending()
    {
        var (job, header) = Make(); using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () => { if (++flushes == 2) throw new IOException("Intent flush fixture."); }, header);
        job.AttachStorageWriter(writer); var held = new Held();
        var result = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.Equal(0, held.Calls); Assert.Equal(1, held.Preparations); Assert.False(result.Result.NativeCallAttempted);
        Assert.Equal(StorageEffect.NotProcessed, result.Result.Effect); Assert.True(result.PersistenceFault); Assert.True(result.MustStop);
        Assert.False(result.Canceled); Assert.Equal("storage-evidence-failed", result.PersistenceProblem!.Code);
        // The intent became pending before its write; an unconfirmed write is never rolled back or published as a prefix.
        Assert.NotNull(writer.Collector.Pending); Assert.True(writer.IsFaulted); Assert.True(writer.NeedsRecovery); Assert.Equal(1, writer.Prefix!.Sequence);
        var retry = StorageObjectCallRunner.Run(job, writer, 0, held);
        Assert.True(retry.PersistenceFault); Assert.Equal(0, held.Calls); Assert.Equal(JobState.Running, job.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_failed_no_call_pairing_after_denied_admission_never_calls_and_stays_recovery_pending(bool stop)
    {
        var (job, header) = Make(); using var data = new MemoryStream(); int flushes = 0;
        using var writer = new StorageTransformJournalWriter(data, () =>
        {
            if (++flushes == 2) { if (stop) job.Cancel(); else job.Pause(); } // the intent is durable; Stop or Pause precedes admission
            else if (flushes == 3) throw new IOException("No-call pairing fixture.");
        }, header);
        job.AttachStorageWriter(writer); var held = new Held();
        var result = StorageObjectCallRunner.Run(job, writer, 0, held); // returns instead of waiting at a pause boundary
        Assert.Equal(0, held.Calls); Assert.False(result.Result.NativeCallAttempted); Assert.Equal(StorageEffect.NotProcessed, result.Result.Effect);
        Assert.True(result.PersistenceFault); Assert.True(result.MustStop); Assert.Equal("storage-evidence-failed", result.PersistenceProblem!.Code);
        Assert.Equal(stop ? JobState.Stopping : JobState.Pausing, job.State);
        // Live interpretation keeps the durable intent unresolved, and a later reader still needs review.
        Assert.NotNull(writer.Collector.Pending); Assert.Null(writer.Collector.LastOutcome); Assert.True(writer.NeedsRecovery); Assert.Equal(2, writer.Prefix!.Sequence);
        var read = StorageTransformJournalReader.Read(new MemoryStream(data.ToArray()), ct: Ct);
        Assert.True(read.Collector.ReviewPending); Assert.False(read.Collector.Ended);
    }
}
