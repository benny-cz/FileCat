using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Core.Resources;
using FileCat.Core.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsStorageReviewEvidenceTests
{
    [Fact]
    public void Held_history_cursor_streams_multiple_batches_and_binds_every_entry_to_native_identity()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-cursor-").FullName;
        try
        {
            string journal = CreateJournal(root);
            for (int i = 0; i < 600; i++) File.WriteAllText(Path.Join(root, $"unrelated-{i:D4}-" + new string('x', 110)), "metadata only");
            var backend = WindowsStorageReviewEvidence.Backend;
            using (var cursor = backend.OpenCursor(root, false, Stop))
            {
                var names = new HashSet<string>(StringComparer.Ordinal); int batches = 0;
                while (true)
                {
                    var entries = cursor.ReadBatch(Stop); if (entries.Count == 0) break; batches++;
                    foreach (var entry in entries)
                    {
                        Assert.True(names.Add(entry.Leaf)); Assert.NotEmpty(entry.Identity);
                        if (entry.Leaf == journal)
                        {
                            var observed = backend.ObserveJournal(root, journal, Stop);
                            Assert.Equal(cursor.DirectoryIdentity, observed.DirectoryIdentity);
                            Assert.Equal(observed.Identity, entry.Identity); Assert.Equal(observed.ByteLength, entry.ByteLength);
                        }
                    }
                }
                Assert.True(batches > 1); Assert.Equal(601, names.Count);
            }
            // Missing optional artifacts are an empty cursor over the same root, not another namespace.
            using (var empty = backend.OpenCursor(root, true, Stop))
            using (var owner = backend.OpenArtifacts(root, Stop))
            { Assert.Equal(owner.DirectoryIdentity, empty.DirectoryIdentity); Assert.Empty(empty.ReadBatch(Stop)); }
            using var discovery = JournalRecovery.OpenStorageHistory(root, backend, Stop);
            int pages = 0, rows = 0;
            while (true)
            {
                var page = discovery.ReadPage(Stop); pages++; rows += page.Rows.Length;
                Assert.InRange(page.Examined, 0, 64); Assert.InRange(page.HeaderBytes, 0, 1024 * 1024);
                Assert.All(page.Rows, row => Assert.DoesNotContain("damaged", row.Status));
                if (!page.More) break;
            }
            Assert.Equal(1, rows); Assert.True(pages >= 10);
            discovery.Dispose();
            StorageHistoryBookmark? after = null; pages = rows = 0;
            while (true)
            {
                var page = JournalRecovery.ReadStorageHistoryPage(root, after, false, backend, Stop);
                pages++; rows += page.Rows.Length; Assert.InRange(page.Examined, 0, 64);
                if (!page.More) break;
                after = Assert.IsType<StorageHistoryBookmark>(page.Continuation);
                // Each detached page retired the only native cursor before a separate interactive owner can open.
                using var evidence = backend.OpenJournal(root, journal, Stop);
                Assert.True(JournalRecovery.ReadStorageReviewStatus(evidence, Stop).ReviewPending);
            }
            Assert.Equal(1, rows); Assert.True(pages >= 10);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Held_history_cursor_rejects_redirected_namespaces_and_releases_its_lane_after_failure()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-cursor-refusal-").FullName;
        try
        {
            string leaf = CreateJournal(root); string link = Path.Join(root, "reviewed");
            Directory.CreateSymbolicLink(link, root);
            Assert.ThrowsAny<IOException>(() => WindowsStorageReviewEvidence.Backend.OpenCursor(root, true, Stop));
            Directory.Delete(link);
            using var cursor = WindowsStorageReviewEvidence.Backend.OpenCursor(root, false, Stop);
            Assert.Contains(cursor.ReadBatch(Stop), item => item.Leaf == leaf);
        }
        finally { Directory.Delete(root, true); }
    }

    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static string CreateJournal(string root, DateTime? createdUtc = null)
    {
        var observation = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(1, "historical-object"), false, null);
        DateTime created = createdUtc ?? DateTime.UtcNow;
        Guid job = Guid.NewGuid(); string leaf = StorageJournalPolicy.FileName(created, job);
        using var native = StorageReviewResources.EnterNative(Stop);
        using var file = WindowsStorageReviewEvidence.CreateJournal(root, leaf, Stop);
        var header = new StorageJournalHeader(job, created, StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid(),
            [new(0, 0, @"C:\source\file", observation)]);
        using var writer = new StorageTransformJournalWriter(file.Stream, () => file.Stream.Flush(true), header);
        writer.FlushIntent(0, @"C:\source\file", observation);
        return leaf;
    }
    private static void RequireNormalWindows()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS evidence fixture.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "This persistence fixture must run in the normal user process.");
    }

    [Fact]
    public void Manager_history_queries_use_private_authority_and_acknowledgment_aware_recovery_state()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-manager-").FullName;
        try
        {
            string leaf = CreateJournal(root), path = Path.Join(root, leaf);
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), root, ArchiveJobBackends.Empty,
                new NoStorageTransforms(), WindowsStorageReviewEvidence.CreateJournal, WindowsStorageReviewEvidence.Backend);
            var candidate = JournalRecovery.StoragePlaceholder(path);
            Assert.True(manager.IsInterrupted(candidate, Stop));
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                Assert.False(manager.IsInterrupted(candidate, Stop));
                Assert.True(manager.ReadStorageHistory(leaf, ct: Stop).EffectUncertain);
            }
            var reviewed = manager.ReadStorageReview(leaf, Stop);
            Assert.True(reviewed.ReviewPending);
            var acknowledged = manager.AcknowledgeStorageReview(reviewed.Current, "Reviewed exact private history through the manager.",
                StorageReviewChoice.AcknowledgeReviewedHistory, Stop);
            Assert.False(acknowledged.ReviewPending); Assert.True(acknowledged.Current.History.EffectUncertain);
            Assert.False(manager.IsInterrupted(candidate, Stop));
            var namedCandidate = manager.StorageHistoryCandidate(leaf);
            Assert.Equal(leaf, namedCandidate.StorageJournalLeaf); Assert.False(manager.IsInterrupted(namedCandidate, Stop));
            var page = manager.ReadStorageHistoryPage(retainedReviews: true, ct: Stop);
            Assert.Contains(page.Rows, row => row.RetainedReview && row.FileName == acknowledged.RetainedAcknowledgmentLeaf);
            var retained = manager.ReadRetainedStorageReview(acknowledged.RetainedAcknowledgmentLeaf!, Stop);
            Assert.Equal(acknowledged.Current.Evidence, retained.Acknowledgment.Reviewed.Evidence); Assert.True(retained.JournalPresent);
            using var export = new MemoryStream(); manager.CopyStorageReview(acknowledged.Current, export, Stop);
            Assert.Equal(acknowledged.Current.Evidence.Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(export.ToArray())));
            Assert.Throws<ArgumentException>(() => manager.ReadStorageHistory(path, ct: Stop));
            Assert.Throws<ArgumentException>(() => manager.ReadStorageHistory("..\\" + leaf, ct: Stop));
            Assert.True(manager.AccountStorageHistory(Stop).Complete);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Age_and_count_policy_preserves_pending_history_and_retires_retained_pairs_after_journals()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-age-").FullName;
        try
        {
            string pending = CreateJournal(root), old = CreateJournal(root, DateTime.UnixEpoch);
            using (var owner = WindowsStorageReviewEvidence.Open(root, old, Stop))
                _ = JournalRecovery.AcknowledgeStorageReview(owner, JournalRecovery.ReadStorageReview(owner, Stop), "Old reviewed history.",
                    StorageReviewChoice.AcknowledgeReviewedHistory, Stop);
            StorageEvidenceRetention.ApplyPolicy(root, WindowsStorageReviewEvidence.Backend, new HashSet<Guid>(), Stop);
            Assert.True(File.Exists(Path.Join(root, pending))); Assert.False(File.Exists(Path.Join(root, old)));
            Assert.Equal(2, Directory.EnumerateFiles(Path.Join(root, "reviewed")).Count());
            StorageEvidenceRetention.ApplyPolicy(root, WindowsStorageReviewEvidence.Backend, new HashSet<Guid>(), Stop, keep: 0);
            Assert.True(File.Exists(Path.Join(root, pending))); Assert.Empty(Directory.EnumerateFiles(Path.Join(root, "reviewed")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Byte_pressure_prunes_only_verified_history_and_recounts_before_admission()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-pressure-").FullName;
        try
        {
            string pendingLeaf = CreateJournal(root), reviewedLeaf = CreateJournal(root);
            using (var owner = WindowsStorageReviewEvidence.Open(root, reviewedLeaf, Stop))
                _ = JournalRecovery.AcknowledgeStorageReview(owner, JournalRecovery.ReadStorageReview(owner, Stop), "Reviewed for byte-pressure retirement.",
                    StorageReviewChoice.AcknowledgeReviewedHistory, Stop);
            var before = StorageEvidenceAccounting.Census(root, null, Stop, WindowsStorageReviewEvidence.Backend);
            Assert.True(before.Complete, before.Problem);
            long potential = 1024 * 1024;
            var budget = new StorageEvidenceBudget(before.ChargedBytes + potential - 1, potential);
            using (budget.Reserve(root, Guid.NewGuid(), Stop, WindowsStorageReviewEvidence.Backend))
            {
                Assert.True(File.Exists(Path.Join(root, pendingLeaf))); Assert.False(File.Exists(Path.Join(root, reviewedLeaf)));
                var after = StorageEvidenceAccounting.Census(root, null, Stop, WindowsStorageReviewEvidence.Backend);
                Assert.True(after.Complete, after.Problem); Assert.True(after.ChargedBytes < before.ChargedBytes);
            }
            Assert.True(StorageEvidenceRetention.PruneOldest(root, WindowsStorageReviewEvidence.Backend, new HashSet<Guid>(), Stop));
            Assert.False(StorageEvidenceRetention.PruneOldest(root, WindowsStorageReviewEvidence.Backend, new HashSet<Guid>(), Stop));
            Assert.True(File.Exists(Path.Join(root, pendingLeaf)));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Retained_disk_pair_loads_after_journal_pruning_and_checks_dependency_before_private_retirement()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-retained-").FullName;
        try
        {
            string leaf = CreateJournal(root); StorageReviewStatus acknowledged;
            var initial = StorageEvidenceAccounting.Census(root, null, Stop, WindowsStorageReviewEvidence.Backend);
            Assert.True(initial.Complete, initial.Problem); Assert.Equal(1, initial.PendingHistories); Assert.Equal(StorageJournalPolicy.ReviewWriteBytes, initial.ReservedReviewBytes);
            using (var owner = WindowsStorageReviewEvidence.Open(root, leaf, Stop))
                acknowledged = JournalRecovery.AcknowledgeStorageReview(owner, JournalRecovery.ReadStorageReview(owner, Stop), "Retained historical review.",
                    StorageReviewChoice.AcknowledgeReviewedHistory, Stop);
            var reviewed = StorageEvidenceAccounting.Census(root, null, Stop, WindowsStorageReviewEvidence.Backend);
            Assert.True(reviewed.Complete, reviewed.Problem); Assert.Equal(0, reviewed.PendingHistories); Assert.Equal(0, reviewed.ReservedReviewBytes);
            Guid association = acknowledged.Current.AssociationId;
            using (var retained = WindowsStorageReviewEvidence.OpenRetained(root, Stop))
            {
                var info = retained.Artifacts(association, StorageReviewNames.ArtifactCount, Stop).Single(i => i.Leaf.Contains("-ack-", StringComparison.Ordinal));
                var detail = JournalRecovery.ReadRetainedStorageReview(retained, info, Stop)!;
                Assert.True(detail.JournalPresent); Assert.False(JournalRecovery.PruneRetainedStorageReview(retained, detail, Stop));
            }
            using (var owner = WindowsStorageReviewEvidence.Open(root, leaf, Stop))
                Assert.True(JournalRecovery.PruneStorageJournal(owner, acknowledged.Current, Stop));
            var pruned = StorageEvidenceAccounting.Census(root, null, Stop, WindowsStorageReviewEvidence.Backend);
            Assert.True(pruned.Complete, pruned.Problem); Assert.Equal(0, pruned.Journals); Assert.Equal(2, pruned.Artifacts); Assert.Equal(0, pruned.ReservedReviewBytes);
            using (var retained = WindowsStorageReviewEvidence.OpenRetained(root, Stop))
            {
                var info = retained.Artifacts(association, StorageReviewNames.ArtifactCount, Stop).Single(i => i.Leaf.Contains("-ack-", StringComparison.Ordinal));
                var detail = JournalRecovery.ReadRetainedStorageReview(retained, info, Stop)!;
                Assert.False(detail.JournalPresent); Assert.True(detail.Report.Reviewed.History.EffectUncertain);
                Assert.True(JournalRecovery.PruneRetainedStorageReview(retained, detail, Stop));
            }
            Assert.Empty(Directory.EnumerateFiles(Path.Join(root, "reviewed")));
            var empty = StorageEvidenceAccounting.Census(root, null, Stop, WindowsStorageReviewEvidence.Backend);
            Assert.True(empty.Complete, empty.Problem); Assert.Equal(0, empty.ChargedBytes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Live_private_writer_keeps_its_prior_durable_prefix_readable_during_a_blocked_later_flush()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-live-").FullName;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Guid job = Guid.NewGuid(); string leaf = StorageJournalPolicy.FileName(DateTime.UnixEpoch, job);
        StorageJournalPrefix? prefix = null; Exception? failure = null;
        var worker = new Thread(() =>
        {
            try
            {
                using var native = StorageReviewResources.EnterNative(Stop);
                using var file = WindowsStorageReviewEvidence.CreateJournal(root, leaf, Stop); int flushes = 0;
                var before = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(9, "pending-live"), false, null);
                using var writer = new StorageTransformJournalWriter(file.Stream, () =>
                {
                    file.Stream.Flush(true);
                    if (++flushes == 2) { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), Stop)); }
                }, new(job, DateTime.UnixEpoch, StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid(), [new(0, 0, @"C:\source\file", before)]));
                prefix = writer.Prefix;
                writer.FlushIntent(0, @"C:\source\file", before);
            }
            catch (Exception error) { failure = error; entered.Set(); }
        }) { IsBackground = true };
        try
        {
            worker.Start(); Assert.True(entered.Wait(TimeSpan.FromSeconds(10), Stop)); Assert.Null(failure); Assert.NotNull(prefix);
            var prior = JournalRecovery.ReadStorageHistory(Path.Join(root, leaf), prefix, Stop);
            Assert.Equal(prefix, prior.Prefix); Assert.False(prior.Damaged); Assert.Null(prior.LastCall);
            var bound = JournalRecovery.ReadStorageHistory(Path.Join(root, leaf), prefix, WindowsStorageReviewEvidence.Backend, Stop);
            Assert.Equal(prefix, bound.Prefix); Assert.False(bound.Damaged); Assert.Null(bound.LastCall);
            var account = StorageEvidenceAccounting.Census(root, null, Stop, WindowsStorageReviewEvidence.Backend);
            Assert.True(account.Complete, account.Problem); Assert.Equal(1, account.PendingHistories);
        }
        finally
        {
            release.Set(); Assert.True(worker.Join(TimeSpan.FromSeconds(10))); Directory.Delete(root, true);
        }
        Assert.Null(failure);
    }

    [Fact]
    public void Manager_worker_adopts_the_native_private_writer_and_releases_it_before_terminal_review()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-worker-").FullName;
        try
        {
            string source = Path.Join(root, "source"); File.WriteAllText(source, "untouched user payload");
            string directory = Directory.CreateDirectory(Path.Join(root, "journal")).FullName;
            var adapter = new WindowsStorageTransforms();
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), directory, ArchiveJobBackends.Empty, adapter,
                WindowsStorageReviewEvidence.CreateJournal, WindowsStorageReviewEvidence.Backend);
            using var review = manager.PrepareStorageReview([ItemRef.ForFileSystemPath(source, EntryKind.File)], StorageAction.Compress, Stop);
            using var finished = new ManualResetEventSlim(); manager.JobFinished += _ => finished.Set();
            var job = manager.SubmitStorage(review, review.Approve(StorageAction.Compress, StorageScope.SelectedItems), _ => { });
            Assert.True(finished.Wait(TimeSpan.FromSeconds(10), Stop)); Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(1, job.StorageResult!.Counts.Changed); Assert.Equal(1, job.ItemsDone);
            string leaf = Path.GetFileName(Assert.Single(Directory.EnumerateFiles(directory)));
            using var evidence = WindowsStorageReviewEvidence.Open(directory, leaf, Stop);
            var status = JournalRecovery.ReadStorageReviewStatus(evidence, Stop);
            Assert.False(status.ReviewPending); Assert.True(status.Current.History.Ended);
            Assert.Equal("untouched user payload", File.ReadAllText(source));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Private_disk_report_acknowledgment_restart_and_journal_first_pruning_fit_three_handles()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-native-").FullName;
        try
        {
            string leaf = CreateJournal(root); StorageReviewStatus acknowledged;
            using (var owner = WindowsStorageReviewEvidence.Open(root, leaf, Stop))
            {
                var current = JournalRecovery.ReadStorageReview(owner, Stop);
                Assert.True(current.History.EffectUncertain);
                acknowledged = JournalRecovery.AcknowledgeStorageReview(owner, current, "Reviewed the recorded intent; the historical effect remains uncertain.",
                    StorageReviewChoice.AcknowledgeReviewedHistory, Stop);
                Assert.False(acknowledged.ReviewPending); Assert.True(acknowledged.Current.History.EffectUncertain);
                Assert.InRange(owner.MaximumHandles, 1, 3);
            }
            using (var owner = WindowsStorageReviewEvidence.Open(root, leaf, Stop))
            {
                var restored = JournalRecovery.ReadStorageReviewStatus(owner, Stop);
                Assert.False(restored.ReviewPending); Assert.NotNull(restored.Acknowledgment);
                Assert.Equal(acknowledged.Acknowledgment, restored.Acknowledgment);
                Assert.True(JournalRecovery.PruneStorageJournal(owner, restored.Current, Stop));
                Assert.InRange(owner.MaximumHandles, 1, 3);
            }
            Assert.False(File.Exists(Path.Join(root, leaf))); Assert.Equal(2, Directory.EnumerateFiles(Path.Join(root, "reviewed")).Count());
            foreach (string path in Directory.EnumerateFiles(Path.Join(root, "reviewed")))
            {
                var security = new FileInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                Assert.True(security.AreAccessRulesProtected);
                Assert.Equal(2, security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Count);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Active_writer_reparse_path_and_multiply_named_journal_are_refused_without_touching_history()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-refusal-").FullName;
        try
        {
            string leaf = CreateJournal(root), path = Path.Join(root, leaf); byte[] original = File.ReadAllBytes(path);
            using (var active = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read))
                Assert.ThrowsAny<IOException>(() => WindowsStorageReviewEvidence.Open(root, leaf, Stop));
            string alias = Path.Join(root, "same-journal"); Assert.True(CreateHardLink(alias, path, 0));
            Assert.ThrowsAny<IOException>(() => WindowsStorageReviewEvidence.Open(root, leaf, Stop)); File.Delete(alias);
            string link = Path.Join(root, "redirect"); Directory.CreateSymbolicLink(link, root);
            Assert.ThrowsAny<IOException>(() => WindowsStorageReviewEvidence.Open(link, leaf, Stop));
            Assert.ThrowsAny<IOException>(() => WindowsStorageReviewEvidence.Backend.OpenHistory!(link, leaf, Stop)); Directory.Delete(link);
            Assert.Equal(original, File.ReadAllBytes(path)); Assert.False(Directory.Exists(Path.Join(root, "reviewed")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Foreign_namespace_write_permissions_and_added_streams_are_unqualified_for_acknowledgment()
    {
        RequireNormalWindows(); string root = Directory.CreateTempSubdirectory("filecat-evidence-boundaries-").FullName;
        try
        {
            string leaf = CreateJournal(root), path = Path.Join(root, leaf);
            File.WriteAllText(path + ":unexpected:$DATA", "unqualified additional stream");
            Assert.ThrowsAny<IOException>(() => WindowsStorageReviewEvidence.Open(root, leaf, Stop)); File.Delete(path + ":unexpected:$DATA");
            var folder = new DirectoryInfo(root); var security = folder.GetAccessControl(AccessControlSections.Access);
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                FileSystemRights.Write | FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Allow));
            folder.SetAccessControl(security);
            Assert.Throws<UnauthorizedAccessException>(() => WindowsStorageReviewEvidence.Open(root, leaf, Stop));
        }
        finally { Directory.Delete(root, true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)] private static partial bool CreateHardLink(string name, string existing, nint security);
}
