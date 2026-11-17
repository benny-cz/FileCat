using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsStorageTraversalTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_real_held_call_obeys_durable_intent_and_Stop_on_both_sides_of_admission(bool stopBeforeAdmission)
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-storage-call-journal-").FullName;
        try
        {
            string path = Path.Join(root, "item"); File.WriteAllText(path, "held journal/native integration payload");
            var adapter = new WindowsStorageTransforms();
            var preparation = Review(adapter, path, EntryKind.File, StorageAction.Compress);
            using var review = preparation.Review; using var owner = preparation.Owner;
            var request = new JobRequest
            {
                Kind = JobKind.StorageTransform, Sources = review.Sources,
                StorageTransform = new(StorageAction.Compress, StorageScope.SelectedItems, review.Revision) { Roots = review.Roots },
            };
            var job = new Job(request, "Native durable compression", "fixture", [], []);
            job.SetState(JobState.Queued); job.SealAdmission(); Assert.Equal(JobStartDisposition.Started, job.TryStart());
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken);
            string evidence = Path.Join(root, "fixture-evidence.fcj");
            using var stream = new FileStream(evidence, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
            int flushes = 0;
            using var writer = new StorageTransformJournalWriter(stream, () =>
            {
                stream.Flush(true);
                if (++flushes == 2 && stopBeforeAdmission) job.Cancel();
            }, JobJournal.CaptureStorageHeader(request, job.Id, job.CreatedUtc));
            job.AttachStorageWriter(writer);
            using var traversal = adapter.OpenReviewedRoot(owner.Leases[0], StorageAction.Compress, TestContext.Current.CancellationToken);
            var controlled = new BeforeNativeCall(traversal.Root, () =>
            {
                var prefix = writer.Prefix!; Assert.Equal(2, prefix.Sequence);
                using var input = new FileStream(evidence, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var intent = StorageTransformJournalReader.Read(input, job.Id, prefix.ByteLength, prefix.Sha256, TestContext.Current.CancellationToken);
                Assert.False(intent.Collector.Damaged); Assert.NotNull(intent.Collector.Pending);
                Assert.Equal(owner.Leases[0].Observation.Identity, intent.Collector.Pending.Identity);
                job.Cancel(); // after Core admission; the real native call must still return and persist its postcondition
            });
            var execution = StorageObjectCallRunner.Run(job, writer, 0, controlled);
            Assert.True(execution.Canceled); Assert.False(execution.PersistenceFault);
            Assert.Equal(stopBeforeAdmission ? 0 : 1, controlled.Calls);
            Assert.Equal(stopBeforeAdmission ? StorageEffect.NotProcessed : StorageEffect.Changed, execution.Result.Effect);
            Assert.Equal(stopBeforeAdmission ? StorageObservationState.Disabled : StorageObservationState.Enabled,
                traversal.Root.Observe(CancellationToken.None).Compression);
            Assert.Null(writer.Collector.Pending); Assert.Equal(3, flushes);
            var counts = stopBeforeAdmission ? new StorageOutcomeCounts(NotProcessed: 1) : new StorageOutcomeCounts(Changed: 1);
            var completeness = stopBeforeAdmission ? StorageCompleteness.Incomplete : StorageCompleteness.Complete;
            writer.RootSummary(new(0, StorageJournalCounts.From(counts), completeness, !stopBeforeAdmission));
            writer.Finish(new(request.StorageTransform.Action, request.StorageTransform.Scope, review.Revision, counts)
            {
                Canceled = true, Discovery = completeness,
                Roots = [new(0, owner.Leases[0].Observation.Identity, completeness, !stopBeforeAdmission)],
            });
            Assert.False(writer.NeedsRecovery);
            using var final = new FileStream(evidence, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var collected = StorageTransformJournalReader.Read(final, ct: TestContext.Current.CancellationToken);
            Assert.Equal(writer.Prefix, collected.Prefix); Assert.True(collected.Collector.Ended); Assert.False(collected.Collector.ReviewPending);
            Assert.Equal(JobState.Canceled, collected.Collector.ToSnapshot(collected.Collector.Summary!).FinalState());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class BeforeNativeCall(IStorageObjectLease held, Action beforeCall) : IStorageObjectLease
    {
        public StorageObjectIdentity Identity => held.Identity;
        internal int Calls;
        public string Path => held.Path;
        public StorageObservation Observe(CancellationToken ct) => held.Observe(ct);
        public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct) => held.PrepareMutation(action, ct);
        public StorageMutationResult ApplyAdmitted(StorageAction action) { Calls++; beforeCall(); return held.ApplyAdmitted(action); }
        public void Dispose() { } // the surrounding traversal owns the borrowed held object
    }
}
