using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

// Job admission scans every known journal root in this process. Keep each test's teardown outside other job admissions.
[CollectionDefinition("Windows storage job evidence")]
public sealed class WindowsStorageJobEvidenceCollection;

[Collection("Windows storage job evidence")]
public sealed class WindowsStorageExecutorTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static void RequireNormal(bool qualifyNativeArm64 = false)
    {
        Assert.SkipWhen(!WindowsStorageTransforms.SupportsHost(qualifyNativeArm64), "Requires an admitted Windows 11 NTFS qualification host.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "Storage jobs must run in the normal user process.");
    }
    private static JobManager Manager(string root, IStorageTransforms? transforms = null, bool qualifyNativeArm64 = false) =>
        new(new PortableFileOperations(), new ProviderRegistry(), Directory.CreateDirectory(Path.Join(root, "journals")).FullName,
            ArchiveJobBackends.Empty, transforms ?? new WindowsStorageTransforms(qualifyNativeArm64: qualifyNativeArm64),
            qualifyNativeArm64 ? WindowsStorageReviewEvidence.CreateArm64QualificationJournal : WindowsStorageReviewEvidence.CreateJournal,
            qualifyNativeArm64 ? WindowsStorageReviewEvidence.Arm64QualificationBackend : WindowsStorageReviewEvidence.Backend);
    private static Job Run(JobManager manager, StorageScope scope, params ItemRef[] sources) => RunAction(manager, StorageAction.Compress, scope, sources);
    private static Job RunAction(JobManager manager, StorageAction action, StorageScope scope, params ItemRef[] sources)
    {
        using var review = manager.PrepareStorageReview(sources, action, Stop);
        Assert.All(review.Roots, root => Assert.True(root.Observation.Ineligible is null,
            $"Root {root.SourceIndex}: {root.Observation.Ineligible?.Code} {root.Observation.Ineligible?.Message}"));
        using var finished = new ManualResetEventSlim();
        void Finished(Job _) => finished.Set();
        manager.JobFinished += Finished;
        try
        {
            var job = manager.SubmitStorage(review, review.Approve(action, scope), _ => { });
            Assert.True(finished.Wait(TimeSpan.FromSeconds(45), Stop)); return job;
        }
        finally { manager.JobFinished -= Finished; }
    }
    private static bool Compressed(string path) => (File.GetAttributes(path) & FileAttributes.Compressed) != 0;

    [Fact]
    public void Detaching_the_owned_volume_after_intent_does_not_mutate_or_replay_the_prepared_call() =>
        DetachOwnedVolume(afterSet: false);

    [Fact]
    public void Detaching_after_native_compression_returns_retains_uncertainty_without_replay() =>
        DetachOwnedVolume(afterSet: true);

    private static void DetachOwnedVolume(bool afterSet)
    {
        RequireNormal();
        string? control = Environment.GetEnvironmentVariable(afterSet
            ? "FILECAT_STORAGE_NATIVE_DEVICE_LOSS_AFTER_SET_CONTROL" : "FILECAT_STORAGE_NATIVE_DEVICE_LOSS_CONTROL");
        if (string.IsNullOrWhiteSpace(control)) Assert.Skip("Requires the isolated virtual disk device-loss fixture.");
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Stop);
        stop.CancelAfter(TimeSpan.FromSeconds(90));
        string target = File.ReadAllText(Path.Join(control, "target.txt")).Trim();
        var drive = new DriveInfo(Path.GetPathRoot(target)!);
        Assert.Equal("FCDeviceLoss", drive.VolumeLabel);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.InRange(drive.TotalSize, 32 * 1024 * 1024, 320 * 1024 * 1024);
        var fs = new WindowsFileOperations();
        string identity = Assert.IsType<string>(fs.GetFileIdentity(target));
        byte[] before = SHA256.HashData(File.ReadAllBytes(target));
        Assert.False(Compressed(target));
        var source = ItemRef.ForFileSystemPath(target, EntryKind.File);
        var setReturns = new List<bool>();
        IStorageTransforms adapter = afterSet ? new WindowsStorageTransforms(compressionSetReturnedForQualification: succeeded =>
        {
            setReturns.Add(succeeded);
            Detach();
        }) : new DetachBeforeMutation(Detach);
        string evidence = Directory.CreateTempSubdirectory("filecat-storage-detach-").FullName;
        try
        {
            var manager = Manager(evidence, adapter);
            var published = new List<InterruptedJob>();
            manager.StorageReviewRequired += published.Add;
            var interrupted = Run(manager, StorageScope.SelectedItems, source);
            var result = Assert.IsType<StorageResultSnapshot>(interrupted.StorageResult);
            Assert.True(result.DurablePrefix is not null, JobIssue.Summarize(interrupted.Issues));
            Assert.Equal(JobState.Failed, interrupted.State);
            Assert.Equal(afterSet ? new StorageOutcomeCounts(Uncertain: 1) : new StorageOutcomeCounts(NotProcessed: 1), result.Counts);
            Assert.Equal(StorageCompleteness.Incomplete, Assert.Single(result.Roots).Completeness);
            Assert.Equal(afterSet, result.ReviewPending);
            string name = StorageJournalPolicy.FileName(interrupted.CreatedUtc, interrupted.Id);
            var call = Assert.Single(manager.ReadStorageCalls(name, result.DurablePrefix!, ct: stop.Token).Calls);
            Assert.Equal(afterSet ? StorageCallDisposition.Observed : StorageCallDisposition.NotAttempted, call.Disposition);
            Assert.Equal(afterSet ? "postcondition-unreadable" : "precall-observation-failed", call.Result?.Problem?.Code);
            Assert.Equal(afterSet, call.Result!.NativeCallAttempted);
            Assert.Null(call.Result.After);
            var reviewed = manager.ReadStorageReview(name, stop.Token);
            Assert.Equal(!afterSet, reviewed.Current.History.Ended);
            Assert.Equal(afterSet, reviewed.Current.History.EffectUncertain);
            Assert.Equal(afterSet, reviewed.ReviewPending);
            if (afterSet)
            {
                Assert.True(Assert.Single(setReturns), "The native compression SET must succeed before detachment.");
                Assert.Equal(name, Assert.Single(published).StorageJournalLeaf);
            }
            else Assert.Empty(published);
            File.Copy(Path.Join(evidence, "journals", name), Path.Join(control, "interrupted.journal"));
            File.WriteAllText(Path.Join(control, "retired"), "");
            WaitForFixture("reattached");

            Assert.Equal(identity, fs.GetFileIdentity(target));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(target)));
            Assert.Equal(afterSet, Compressed(target));
            var reopened = Manager(evidence);
            if (afterSet)
            {
                var candidate = reopened.StorageHistoryCandidate(name);
                Assert.True(reopened.IsInterrupted(candidate, stop.Token));
                var retained = reopened.ReadStorageReview(name, stop.Token);
                Assert.Equal(reviewed.Current.Evidence, retained.Current.Evidence);
                var acknowledged = reopened.AcknowledgeStorageReview(retained.Current, "Reviewed the device-loss operation.",
                    StorageReviewChoice.AcknowledgeReviewedHistory, stop.Token);
                Assert.False(reopened.IsInterrupted(candidate, stop.Token));
                Assert.True(acknowledged.Current.History.EffectUncertain);
                Assert.True(Compressed(target));
                Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(target)));
            }
            var retry = RunAction(reopened, afterSet ? StorageAction.RemoveCompression : StorageAction.Compress, StorageScope.SelectedItems, source);
            Assert.Equal(JobState.Completed, retry.State);
            Assert.Equal(1, retry.StorageResult!.Counts.Changed);
            Assert.Equal(!afterSet, Compressed(target));
            Assert.Equal(identity, fs.GetFileIdentity(target));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(target)));
            File.Copy(Path.Join(evidence, "journals", StorageJournalPolicy.FileName(retry.CreatedUtc, retry.Id)), Path.Join(control, "fresh-retry.journal"));
        }
        finally { Directory.Delete(evidence, recursive: true); }

        void Detach()
        {
            File.WriteAllText(Path.Join(control, "prepared"), "");
            WaitForFixture("detached");
        }

        void WaitForFixture(string marker)
        {
            string path = Path.Join(control, marker);
            while (!File.Exists(path))
            {
                stop.Token.ThrowIfCancellationRequested();
                if (File.Exists(Path.Join(control, "aborted"))) throw new IOException("The isolated device-loss fixture could not proceed.");
                stop.Token.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(50));
            }
        }
    }

    private sealed class DetachBeforeMutation(Action waitForDeviceLoss) : IStorageTransforms
    {
        private readonly WindowsStorageTransforms _native = new();
        public StorageProblem? Unavailable(StorageAction action) => _native.Unavailable(action);
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct) =>
            _native.InspectRoot(target, action, ct);
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) =>
            new Traversal(_native.OpenReviewedRoot(root, action, ct), waitForDeviceLoss);

        private sealed class Traversal(IStorageTraversal native, Action waitForDeviceLoss) : IStorageTraversal
        {
            public IStorageObjectLease Root { get; } = new ObjectLease(native.Root, waitForDeviceLoss);
            public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct) => native.RootAncestors(ct);
            public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct) =>
                throw new NotSupportedException("This fixture submits one selected file.");
            public void Dispose() => native.Dispose();
        }

        private sealed class ObjectLease(IStorageObjectLease native, Action waitForDeviceLoss) : IStorageObjectLease
        {
            public string Path => native.Path;
            public StorageObjectIdentity Identity => native.Identity;
            public StorageObservation Observe(CancellationToken ct) => native.Observe(ct);
            public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct) => native.PrepareMutation(action, ct);
            public StorageMutationResult ApplyAdmitted(StorageAction action)
            {
                waitForDeviceLoss();
                return native.ApplyAdmitted(action);
            }
            public void Dispose() => native.Dispose();
        }
    }

    [Fact]
    public void Removing_compression_with_insufficient_volume_capacity_retains_payload_and_reports_the_native_failure()
    {
        RequireNormal();
        string? canary = Environment.GetEnvironmentVariable("FILECAT_STORAGE_NATIVE_FULL_TARGET");
        if (string.IsNullOrWhiteSpace(canary)) Assert.Skip("Set FILECAT_STORAGE_NATIVE_FULL_TARGET to a canary on a small disposable NTFS fixture volume.");
        Assert.True(File.Exists(canary));
        var volume = new DriveInfo(Path.GetPathRoot(canary)!);
        Assert.StartsWith("FCFresh", volume.VolumeLabel, StringComparison.Ordinal);
        Assert.Equal("NTFS", volume.DriveFormat);
        Assert.InRange(volume.TotalSize, 32 * 1024 * 1024, 320 * 1024 * 1024);

        string evidence = Directory.CreateTempSubdirectory("filecat-storage-full-volume-").FullName;
        string owned = Path.Join(Path.GetDirectoryName(canary), "filecat-space-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(owned);
            string target = Path.Join(owned, "compressed.bin");
            File.WriteAllBytes(target, []);
            var source = ItemRef.ForFileSystemPath(target, EntryKind.File);
            var manager = Manager(evidence);
            var buffer = new byte[1024 * 1024];
            buffer.AsSpan().Fill(0x71);
            const int logicalMiB = 32;
            TestContext.Current.TestOutputHelper?.WriteLine($"Fixture volume {volume.TotalSize} bytes, {volume.AvailableFreeSpace} free before writing.");
            using (var file = new FileStream(target, FileMode.Open, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.WriteThrough))
            {
                for (int i = 0; i < logicalMiB; i++)
                {
                    Stop.ThrowIfCancellationRequested();
                    file.Write(buffer);
                }
            }
            Assert.False(Compressed(target));
            long beforeCompression = volume.AvailableFreeSpace;
            Assert.Equal(JobState.Completed, Run(manager, StorageScope.SelectedItems, source).State);
            Assert.True(Compressed(target));
            long saved = volume.AvailableFreeSpace - beforeCompression;
            Assert.True(saved >= 16 * 1024 * 1024, $"The prepared compressible file must actually free storage, observed {saved} bytes.");

            var fs = new WindowsFileOperations();
            var id = fs.GetFileIdentity(target);
            Assert.NotNull(id);
            Assert.False(File.GetAttributes(target).HasFlag(FileAttributes.SparseFile));
            byte[] before = Checksum(target);
            string filler = Path.Join(owned, "owned-capacity-reserve.bin");
            const long remaining = 2 * 1024 * 1024;
            Assert.True(volume.AvailableFreeSpace > remaining);
            using (var file = new FileStream(filler, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.SetLength(volume.AvailableFreeSpace - remaining);
            Assert.False(Compressed(filler));
            Assert.InRange(volume.AvailableFreeSpace, 1, 2 * remaining);

            var attempted = RunAction(manager, StorageAction.RemoveCompression, StorageScope.SelectedItems, source);
            var result = attempted.StorageResult!;
            var calls = manager.ReadStorageCalls(StorageJournalPolicy.FileName(attempted.CreatedUtc, attempted.Id), result.DurablePrefix!, ct: Stop);
            var call = Assert.Single(calls.Calls);
            TestContext.Current.TestOutputHelper?.WriteLine($"{saved} bytes saved during setup. Removal outcome {call.Result?.Effect}, state {attempted.State}, error {call.Result?.Problem?.Code} {call.Result?.Problem?.Message}.");
            Assert.Equal(StorageCallDisposition.Observed, call.Disposition);
            Assert.Equal(StorageEffect.Failed, call.Result!.Effect);
            Assert.Equal(JobState.Failed, attempted.State);
            Assert.Equal("compression-disk-full", call.Result.Problem?.Code);
            Assert.Contains("112", call.Result.Problem?.Message); // ERROR_DISK_FULL
            Assert.Equal(1, result.Counts.Failed);
            Assert.Equal(0, result.Counts.Changed);
            Assert.True(Compressed(target));
            Assert.Equal(id, fs.GetFileIdentity(target));
            Assert.Equal((long)logicalMiB * buffer.Length, new FileInfo(target).Length);
            Assert.Equal(before, Checksum(target));

            File.Delete(filler);
            var retried = RunAction(manager, StorageAction.RemoveCompression, StorageScope.SelectedItems, source);
            Assert.Equal(JobState.Completed, retried.State);
            Assert.Equal(1, retried.StorageResult!.Counts.Changed);
            Assert.False(Compressed(target));
            Assert.Equal(id, fs.GetFileIdentity(target));
            Assert.Equal(before, Checksum(target));
        }
        finally
        {
            if (Directory.Exists(owned)) Directory.Delete(owned, recursive: true);
            Directory.Delete(evidence, recursive: true);
        }

        static byte[] Checksum(string path)
        {
            using var file = File.OpenRead(path);
            return SHA256.HashData(file);
        }
    }

    [Fact]
    public void Recursive_job_covers_reverse_order_overlaps_hidden_files_and_named_streams_without_double_counts()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireNormal(qualifyNativeArm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-job-tree-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(temp, "folder")).FullName;
            string nested = Directory.CreateDirectory(Path.Join(folder, "nested")).FullName;
            string file = Path.Join(nested, "file"), hidden = Path.Join(folder, "hidden");
            File.WriteAllText(file, "main bytes remain available"); File.WriteAllText(file + ":sentinel:$DATA", "named-stream bytes remain available");
            File.WriteAllText(hidden, "hidden bytes remain available"); File.SetAttributes(hidden, FileAttributes.Hidden);
            var manager = Manager(temp, qualifyNativeArm64: qualifyNativeArm64);
            Assert.Equal(JobState.Completed, Run(manager, StorageScope.SelectedItems, ItemRef.ForFileSystemPath(folder, EntryKind.Directory)).State);
            var job = Run(manager, StorageScope.ExistingDescendants, ItemRef.ForFileSystemPath(file, EntryKind.File),
                ItemRef.ForFileSystemPath(folder, EntryKind.Directory), ItemRef.ForFileSystemPath(folder, EntryKind.Directory));
            Assert.Equal(JobState.Completed, job.State); Assert.Equal(3, job.StorageResult!.Counts.Changed); Assert.Equal(1, job.StorageResult.Counts.AlreadySatisfied);
            Assert.Equal(4, job.ItemsDone); Assert.Equal(4, job.ItemsTotal); Assert.Equal(3, job.StorageResult.Roots.Length);
            Assert.All(job.StorageResult.Roots, root => Assert.Equal(StorageCompleteness.Complete, root.Completeness));
            Assert.True(Compressed(folder) && Compressed(nested) && Compressed(file) && Compressed(hidden));
            Assert.Equal("main bytes remain available", File.ReadAllText(file));
            Assert.Equal("named-stream bytes remain available", File.ReadAllText(file + ":sentinel:$DATA"));
            Assert.Equal("hidden bytes remain available", File.ReadAllText(hidden));
            var status = manager.ReadStorageReview(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), Stop);
            Assert.True(status.Current.History.Ended); Assert.False(status.ReviewPending); Assert.NotNull(job.StorageResult.DurablePrefix);
            var removed = RunAction(manager, StorageAction.RemoveCompression, StorageScope.ExistingDescendants,
                ItemRef.ForFileSystemPath(file, EntryKind.File), ItemRef.ForFileSystemPath(folder, EntryKind.Directory));
            Assert.Equal(JobState.Completed, removed.State); Assert.Equal(4, removed.StorageResult!.Counts.Changed);
            Assert.False(Compressed(folder) || Compressed(nested) || Compressed(file) || Compressed(hidden));
            Assert.Equal("main bytes remain available", File.ReadAllText(file)); Assert.Equal("named-stream bytes remain available", File.ReadAllText(file + ":sentinel:$DATA"));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void Readonly_file_and_reparse_subtree_are_skipped_while_independent_siblings_are_changed()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireNormal(qualifyNativeArm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-job-exclusions-").FullName;
        string? readonlyFile = null, link = null;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(temp, "folder")).FullName;
            string outside = Directory.CreateDirectory(Path.Join(temp, "outside")).FullName;
            string sentinel = Path.Join(outside, "sentinel"), allowed = Path.Join(folder, "allowed");
            File.WriteAllText(sentinel, "outside scope"); File.WriteAllText(allowed, "allowed bytes");
            readonlyFile = Path.Join(folder, "readonly"); File.WriteAllText(readonlyFile, "readonly bytes"); File.SetAttributes(readonlyFile, FileAttributes.ReadOnly);
            link = Path.Join(folder, "redirect"); Directory.CreateSymbolicLink(link, outside);
            var job = Run(Manager(temp, qualifyNativeArm64: qualifyNativeArm64), StorageScope.ExistingDescendants,
                ItemRef.ForFileSystemPath(folder, EntryKind.Directory));
            Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal(2, job.StorageResult!.Counts.Changed); Assert.Equal(2, job.StorageResult.Counts.Skipped);
            Assert.Equal(StorageCompleteness.UnknownRemainder, job.StorageResult.Discovery);
            Assert.True(Compressed(allowed)); Assert.False(Compressed(readonlyFile)); Assert.False(Compressed(sentinel));
            Assert.Equal("outside scope", File.ReadAllText(sentinel)); Assert.Equal("readonly bytes", File.ReadAllText(readonlyFile));
        }
        finally
        {
            if (readonlyFile is not null && File.Exists(readonlyFile)) File.SetAttributes(readonlyFile, FileAttributes.Normal);
            if (link is not null && Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void Native_access_denial_preserves_independent_siblings()
    {
        bool qualifyNativeArm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
        RequireNormal(qualifyNativeArm64);
        string temp = Directory.CreateTempSubdirectory("filecat-storage-job-denial-").FullName;
        string? denied = null; FileSecurity? original = null;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(temp, "folder")).FullName;
            denied = Path.Join(folder, "denied"); string allowed = Path.Join(folder, "allowed");
            File.WriteAllText(denied, "denied bytes"); File.WriteAllText(allowed, "allowed bytes");
            var info = new FileInfo(denied); original = info.GetAccessControl();
            var security = info.GetAccessControl();
            using var identity = WindowsIdentity.GetCurrent();
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Deny));
            info.SetAccessControl(security);
            Assert.Throws<UnauthorizedAccessException>(() => { using var refused = File.OpenRead(denied); });
            var job = Run(Manager(temp, qualifyNativeArm64: qualifyNativeArm64), StorageScope.ExistingDescendants,
                ItemRef.ForFileSystemPath(folder, EntryKind.Directory));
            Assert.Equal(JobState.CompletedWithIssues, job.State); Assert.Equal(2, job.StorageResult!.Counts.Changed); Assert.Equal(1, job.StorageResult.Counts.Failed);
            Assert.True(Compressed(allowed));
            Restore(info, original); original = null;
            Assert.False(Compressed(denied)); Assert.Equal("denied bytes", File.ReadAllText(denied));
        }
        finally
        {
            if (denied is not null && original is not null) Restore(new FileInfo(denied), original);
            Directory.Delete(temp, true);
        }
    }

    private static void Restore(FileInfo file, FileSecurity original)
    {
        var restored = new FileSecurity();
        restored.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(), AccessControlSections.Access);
        file.SetAccessControl(restored);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sharing_conflict_can_retry_the_same_held_file_or_skip_without_any_native_intent(bool retry)
    {
        RequireNormal(); string temp = Directory.CreateTempSubdirectory("filecat-storage-job-sharing-").FullName;
        try
        {
            string file = Path.Join(temp, "file"); File.WriteAllText(file, "preserved sharing bytes");
            using var blocker = File.OpenHandle(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var manager = Manager(temp); int decisions = 0;
            manager.DecisionRequested += pending =>
            {
                decisions++; if (retry) blocker.Dispose(); pending.Resolve(new(retry ? DecisionAction.Retry : DecisionAction.Skip));
            };
            var job = Run(manager, StorageScope.SelectedItems, ItemRef.ForFileSystemPath(file, EntryKind.File)); blocker.Dispose();
            Assert.Equal(1, decisions); Assert.Equal(retry ? JobState.Completed : JobState.CompletedWithIssues, job.State);
            Assert.Equal(retry ? 1 : 0, job.StorageResult!.Counts.Changed); Assert.Equal(retry ? 0 : 1, job.StorageResult.Counts.Skipped);
            Assert.Equal(retry, Compressed(file)); Assert.Equal("preserved sharing bytes", File.ReadAllText(file));
            var status = manager.ReadStorageReview(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), Stop);
            Assert.False(status.ReviewPending); Assert.True(status.Current.History.Ended);
            if (!retry) Assert.Null(status.Current.History.LastCall);
        }
        finally { Directory.Delete(temp, true); }
    }
}
