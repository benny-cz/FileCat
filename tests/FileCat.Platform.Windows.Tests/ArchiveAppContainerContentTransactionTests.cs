using System.Formats.Tar;
using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar, "commit")]
    [InlineData(ArchiveWriteFormat.Tar, "rollback")]
    [InlineData(ArchiveWriteFormat.Tar, "abandon")]
    [InlineData(ArchiveWriteFormat.Tar, "cancel-before-commit")]
    [InlineData(ArchiveWriteFormat.Tar, "close-backup-before-commit")]
    [InlineData(ArchiveWriteFormat.Tar, "close-output-before-commit")]
    [InlineData(ArchiveWriteFormat.Tar, "commit-with-parent-disposed")]
    [InlineData(ArchiveWriteFormat.Tar, "journaled-commit")]
    [InlineData(ArchiveWriteFormat.TarGzip, "journaled-commit")]
    [InlineData(ArchiveWriteFormat.TarBzip2, "journaled-commit")]
    [InlineData(ArchiveWriteFormat.TarZstd, "journaled-commit")]
    [InlineData(ArchiveWriteFormat.Tar, "journaled-cleanup")]
    [InlineData(ArchiveWriteFormat.TarGzip, "journaled-cleanup")]
    [InlineData(ArchiveWriteFormat.TarBzip2, "journaled-cleanup")]
    [InlineData(ArchiveWriteFormat.TarZstd, "journaled-cleanup")]
    [InlineData(ArchiveWriteFormat.Tar, "journaled-cleanup-canceled")]
    [InlineData(ArchiveWriteFormat.Tar, "journaled-cleanup-closed-snapshot")]
    [InlineData(ArchiveWriteFormat.Tar, "journaled-cleanup-changed-snapshot")]
    [InlineData(ArchiveWriteFormat.TarGzip, "commit")]
    [InlineData(ArchiveWriteFormat.TarGzip, "rollback")]
    [InlineData(ArchiveWriteFormat.TarGzip, "abandon")]
    [InlineData(ArchiveWriteFormat.TarBzip2, "commit")]
    [InlineData(ArchiveWriteFormat.TarBzip2, "rollback")]
    [InlineData(ArchiveWriteFormat.TarBzip2, "abandon")]
    [InlineData(ArchiveWriteFormat.TarZstd, "commit")]
    [InlineData(ArchiveWriteFormat.TarZstd, "rollback")]
    [InlineData(ArchiveWriteFormat.TarZstd, "abandon")]
    public async Task Native_content_transaction_retains_the_old_archive_and_publishes_only_freshly_verified_bytes_and_origin(
        ArchiveWriteFormat format, string outcome)
    {
        string executable = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1")
            Assert.Skip("Run the explicit native transactional archive qualification lane.");
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-content-transaction-").FullName;
        try
        {
            string path = Path.Combine(root, "original.tar");
            using (var data = File.Create(path))
            using (var wrapper = ContentWrapper(data, format, writing: true))
            using (var writer = new TarWriter(wrapper, TarEntryFormat.Pax, leaveOpen: true))
            using (var before = new MemoryStream("before"u8.ToArray()))
                writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = before,
                    ModificationTime = DateTimeOffset.UnixEpoch });
            File.SetLastWriteTimeUtc(path, DateTime.UnixEpoch.AddSeconds(1700000000));
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.NotContentIndexed);
            byte[] oldMark = "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://original.invalid/\r\n"u8.ToArray();
            File.WriteAllBytes(path + ":Zone.Identifier", oldMark);
            byte[] oldBytes = File.ReadAllBytes(path);
            File.WriteAllBytes(Path.Combine(root, "input"), "after"u8.ToArray());
            File.WriteAllBytes(Path.Combine(root, "input") + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n"u8.ToArray());
            File.SetLastWriteTimeUtc(Path.Combine(root, "input"), DateTime.UnixEpoch.AddSeconds(1700000000));
            using var directory = WindowsArchiveDirectory.Open(root);
            using var input = directory.OpenSource("input");
            using var original = directory.OpenSource("original.tar");
            var baseline = ((IArchiveRetainedFile)original).Observe(ct);
            var metadata = original.CaptureOuterMetadata(ct);
            var inputState = ((IArchiveRetainedFile)input).Observe(ct);
            var profile = new ArchiveWriteProfile(format);
            var assessment = await new ArchiveAssessmentClient((nonce, cpu) => ArchiveAppContainer.Start(executable, "archive-assess " + nonce, cpu))
                .AssessAsync(new(profile, baseline.Version.Length, baseline.Version.Sha256),
                    () => ((IArchiveRetainedFile)original).OpenReadView(baseline, ct), ct);
            var plan = ArchiveTarTransformPlan.Create(profile, assessment, [inputState.Input(3)],
                [new(ArchiveChangeKind.Replace, new(assessment.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 3)],
                [new("added", ArchiveMemberType.File, 3)], ct);
            using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                Archive = new(path, null, [], Profile: profile) }, "native content transaction qualification", "owned", [], []));
            using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, path, plan, new(original, baseline),
                new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, inputState) },
                (nonce, kind, maximum) => directory.ReserveArtifact(nonce, (WindowsArchiveArtifactKind)kind, maximum), directory.CreateArtifact,
                new((nonce, cpu) => ArchiveAppContainer.Start(executable, "archive-transform " + nonce, cpu)),
                new((nonce, cpu) => ArchiveAppContainer.Start(executable, "archive-verify " + nonce, cpu)), ct);
            bool journaled = outcome.StartsWith("journaled-", StringComparison.Ordinal);
            using var backup = journaled ? null : directory.CreateArtifact(Guid.NewGuid(), WindowsArchiveArtifactKind.Stage, baseline.Version.Length);
            var required = ArchiveWindowsOriginZone.Parse(baseline.Origin).RaiseTo(4);
            if (journaled)
            {
                using var result = WindowsArchiveContentUpdate.Publish(journal, prepared, original,
                    new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, inputState) }, TimeSpan.FromSeconds(30), ct);
                Assert.Equal(baseline.Version.Identity, result.Published.Version.Identity);
                Assert.Equal(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, result.Facts.Phase);
                Assert.True(journal.NeedsRecovery);
                var recovered = JournalRecovery.ReadArchiveTransactions(journal.Path);
                Assert.Null(recovered.Problem); Assert.True(recovered.RequiresReview);
                Assert.Equal(result.Facts.ToJson().ToJsonString(), Assert.Single(recovered.ContentUpdates).ToJson().ToJsonString());
                Assert.Equal(oldBytes, ReadContentShared(result.Snapshot.DisplayPath));
                Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(3).Bytes, ReadContentShared(result.Snapshot.DisplayPath + ":Zone.Identifier"));
                Assert.Equal(((IArchiveRetainedFile)result.Snapshot).Observe(ct).MetadataDigest, Assert.Single(recovered.ContentCleanups).Artifacts[^1].MetadataSha256);
                Assert.Equal(new[] { "SnapshotReserved", "SnapshotCreated", "TransactionOpened", "SnapshotSealed", "Ready", "CommitIntent", "CommittedWithRetainedArtifacts" },
                    JobJournal.ReadAll(journal.Path).Where(record => record.Type == ArchiveContentUpdateFacts.RecordType).Select(record => record.Get("phase")).ToArray());
                if (outcome == "journaled-cleanup")
                {
                    var cleaned = result.Cleanup(journal, ct);
                    Assert.True(cleaned.IsResolved); Assert.Equal(cleaned.Artifacts.Count, cleaned.DeletedCount);
                    Assert.Same(cleaned, result.Cleanup(journal, ct));
                    Assert.All(cleaned.Artifacts, artifact => Assert.False(File.Exists(artifact.Path)));
                    Assert.False(journal.NeedsRecovery); Assert.False(JournalRecovery.ReadArchiveTransactions(journal.Path).RequiresReview);
                    journal.Finish(JobState.Completed, "native update and exact private artifact cleanup complete");
                }
                else if (outcome.StartsWith("journaled-cleanup-", StringComparison.Ordinal))
                {
                    if (outcome == "journaled-cleanup-canceled")
                    {
                        using var canceled = new CancellationTokenSource(); canceled.Cancel();
                        Assert.Throws<OperationCanceledException>(() => result.Cleanup(journal, canceled.Token));
                    }
                    else if (outcome == "journaled-cleanup-closed-snapshot")
                    { result.Snapshot.Dispose(); Assert.Throws<ObjectDisposedException>(() => result.Cleanup(journal, ct)); }
                    else
                    {
                        File.SetLastWriteTimeUtc(result.Snapshot.DisplayPath, DateTime.UnixEpoch.AddSeconds(1700001234));
                        Assert.Throws<ArchiveWriteException>(() => result.Cleanup(journal, ct));
                    }
                    Assert.Equal(ArchiveContentCleanupPhase.NeedsReview, result.CleanupFacts.Phase); Assert.Equal(0, result.CleanupFacts.DeletedCount);
                    Assert.All(result.CleanupFacts.Artifacts, artifact => Assert.True(File.Exists(artifact.Path)));
                    Assert.True(journal.NeedsRecovery); Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "cleanup refused"));
                }
                else Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "retained artifacts require review"));
            }
            else using (var transaction = original.BeginContentTransaction(baseline, Guid.NewGuid(), TimeSpan.FromSeconds(30), ct))
            {
                Assert.NotEqual(Guid.Empty, transaction.TransactionId);
                Assert.False(original.CanRead); // The original's counted parent lease was transferred.
                Assert.Throws<ObjectDisposedException>(() => ((IArchiveRetainedFile)original).Observe(ct));
                var retained = transaction.RetainOriginal(backup!, ct);
                Assert.Equal(baseline.Version.Sha256, retained.Version.Sha256);
                Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(3).Digest, retained.Origin.Digest);
                Assert.Throws<InvalidOperationException>(() => transaction.RetainOriginal(backup!, ct));
                using (var view = ((IArchiveRetainedFile)backup!).OpenReadView(retained, ct))
                { using var copy = new MemoryStream(); view.CopyTo(copy); Assert.Equal(oldBytes, copy.ToArray()); }
                Assert.Throws<ArchiveWriteException>(() => transaction.PrepareOutput((WindowsArchiveFile)prepared.Stage, prepared.Verified, ArchiveWindowsOriginZone.ZoneOnly(2), ct));
                var ready = transaction.PrepareOutput((WindowsArchiveFile)prepared.Stage, prepared.Verified, required, ct);
                Assert.Equal(prepared.Verified.Stage.Version.Sha256, ready.PrivateOutput.Version.Sha256);
                Assert.Equal(oldBytes, ReadContentShared(path));
                Assert.Equal(oldMark, ReadContentShared(path + ":Zone.Identifier"));
                Assert.ThrowsAny<IOException>(() => { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); writer.Flush(); });
                Assert.Throws<InvalidOperationException>(() => transaction.PrepareOutput((WindowsArchiveFile)prepared.Stage, prepared.Verified, required, ct));
                if (outcome is "close-backup-before-commit" or "close-output-before-commit")
                {
                    if (outcome == "close-backup-before-commit") backup!.Dispose(); else prepared.Stage.Dispose();
                    Assert.Throws<ObjectDisposedException>(() => transaction.Commit(ready, ct)); transaction.Rollback();
                }
                else if (outcome is "commit" or "cancel-before-commit" or "commit-with-parent-disposed")
                {
                    Assert.Throws<ArchiveWriteException>(() => transaction.Commit(new(transaction, ready.PrivateOutput, ready.RetainedOriginal), ct));
                    if (outcome == "commit-with-parent-disposed") directory.Dispose();
                    if (outcome == "cancel-before-commit")
                    {
                        using var canceled = new CancellationTokenSource(); canceled.Cancel();
                        Assert.Throws<OperationCanceledException>(() => transaction.Commit(ready, canceled.Token));
                        Assert.Equal(oldBytes, ReadContentShared(path));
                    }
                    var committed = transaction.Commit(ready, ct);
                    Assert.Equal(baseline.Version.Identity, committed.Version.Identity);
                    Assert.Equal(prepared.Verified.Stage.Version.Sha256, committed.Version.Sha256);
                    Assert.Equal(required.Bytes, ReadContentShared(path + ":Zone.Identifier"));
                    Assert.True(transaction.ReviewPublished(ready, ct).Matches(committed));
                    Assert.Throws<InvalidOperationException>(() => transaction.Commit(ready, ct));
                    Assert.Throws<ArchiveWriteException>(transaction.Rollback);
                    // The normal reader has adopted the deny-write pin before the transactional pin closes.
                    Assert.ThrowsAny<IOException>(() => { using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
                }
                else if (outcome == "rollback") { transaction.Rollback(); transaction.Rollback(); }
            }
            using var actualDirectory = WindowsArchiveDirectory.Open(root);
            using var actualFile = actualDirectory.OpenSource("original.tar");
            var actual = actualFile.CaptureOuterMetadata(ct);
            Assert.Equal(metadata.Identity, actual.Identity); Assert.Equal(metadata.CreationTime, actual.CreationTime);
            Assert.Equal(metadata.Attributes, actual.Attributes);
            Assert.Equal(metadata.SecurityDescriptor.ToArray(), actual.SecurityDescriptor.ToArray());
            bool committedOutcome = journaled || outcome is "commit" or "cancel-before-commit" or "commit-with-parent-disposed";
            Assert.Equal(committedOutcome ? required.Bytes : oldMark, ReadContentShared(path + ":Zone.Identifier"));
            if (!committedOutcome) { Assert.Equal(oldBytes, ReadContentShared(path)); Assert.Equal(metadata.LastWriteTime, actual.LastWriteTime); }
            else
            {
                using var data = new MemoryStream(ReadContentShared(path));
                using var wrapper = ContentWrapper(data, format, writing: false);
                using var reader = new TarReader(wrapper);
                var members = new Dictionary<string, byte[]>();
                while (reader.GetNextEntry() is { } entry)
                { using var bytes = new MemoryStream(); entry.DataStream!.CopyTo(bytes); members.Add(entry.Name, bytes.ToArray()); }
                Assert.Equal(new[] { "added", "old" }, members.Keys.Order().ToArray());
                Assert.All(members.Values, bytes => Assert.Equal("after"u8.ToArray(), bytes));
            }
            if (backup is not null) Assert.True(File.Exists(backup.DisplayPath));
            Assert.Equal(outcome != "journaled-cleanup", File.Exists(((WindowsArchiveFile)prepared.Stage).DisplayPath));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static byte[] ReadContentShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray();
    }
    private static Stream ContentWrapper(Stream data, ArchiveWriteFormat format, bool writing) => format switch
    {
        ArchiveWriteFormat.Tar => data,
        ArchiveWriteFormat.TarGzip => writing ? new GZipStream(data, CompressionLevel.Optimal, leaveOpen: true) : new GZipStream(data, CompressionMode.Decompress, leaveOpen: true),
        ArchiveWriteFormat.TarBzip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(data,
            writing ? SharpCompress.Compressors.CompressionMode.Compress : SharpCompress.Compressors.CompressionMode.Decompress, false, leaveOpen: true),
        ArchiveWriteFormat.TarZstd => writing ? new SharpCompress.Compressors.ZStandard.CompressionStream(data, leaveOpen: true) : new SharpCompress.Compressors.ZStandard.DecompressionStream(data, leaveOpen: true),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };
}
