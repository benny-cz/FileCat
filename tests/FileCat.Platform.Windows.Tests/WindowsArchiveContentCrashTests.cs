using System.Diagnostics;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Theory]
    [InlineData("snapshot-reserved")] [InlineData("snapshot-created-unrecorded")] [InlineData("snapshot-identified")]
    [InlineData("snapshot-created")] [InlineData("transaction-opened-unrecorded")] [InlineData("transaction-opened")]
    [InlineData("snapshot-sealed")] [InlineData("snapshot-recorded")] [InlineData("private-ready")] [InlineData("ready")]
    [InlineData("commit-intent")] [InlineData("native-committed")] [InlineData("committed-recorded")]
    [InlineData("rollback-before")] [InlineData("rollback-after")] [InlineData("rollback-recorded")]
    public async Task Real_content_process_crashes_preserve_the_original_or_committed_bytes_and_all_recorded_evidence(string point)
    {
        RequireWindows();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Run the explicit NTFS transaction qualification lane.");
        using var fixture = new Fixture(); string root = fixture.Root;
        File.WriteAllText(Path.Combine(root, "sentinel"), "owned unchanged sentinel");
        var checkout = new DirectoryInfo(AppContext.BaseDirectory);
        while (checkout is not null && !File.Exists(Path.Combine(checkout.FullName, "FileCat.slnx"))) checkout = checkout.Parent;
        Assert.NotNull(checkout);
        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        string harness = Path.Combine(checkout.FullName, "tests", "FileCat.CoreCrashHarness", "bin", configuration, "net10.0", "FileCat.CoreCrashHarness.dll");
        string probe = Path.Combine(checkout.FullName, "tests", "FileCat.WorkerProbe", "bin", configuration, "net10.0", "FileCat.WorkerProbe.dll");
        string runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
        var start = new ProcessStartInfo(Path.Combine(runtime, "dotnet.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { harness, "--archive-content-crash", root, point, typeof(WindowsArchiveDirectory).Assembly.Location, probe }) start.ArgumentList.Add(arg);
        using (var process = Process.Start(start)!)
        {
            var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                string? checkpoint = await process.StandardOutput.ReadLineAsync(timeout.Token);
                Assert.True(checkpoint == "archive-content-checkpoint:" + point, "Missing checkpoint: " + checkpoint + (process.HasExited ? " stderr: " + await errors : ""));
                Assert.False(process.HasExited); process.Kill(entireProcessTree: true); await process.WaitForExitAsync(timeout.Token);
                Assert.NotEqual(0, process.ExitCode); Assert.Equal("", await errors.WaitAsync(timeout.Token));
            }
            finally { await Reap(process); }
        }
        string[] before = Snapshot();
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "baseline.json")));
        var initial = baseline.RootElement; string journals = Path.Combine(root, "journals");
        var job = Assert.Single(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.Null(job.ArchiveTransactions.Problem); Assert.True(job.ArchiveTransactions.RequiresReview);
        var preparation = Assert.Single(job.ArchiveTransactions.Transactions); var facts = Assert.Single(job.ArchiveTransactions.ContentUpdates);
        Assert.Equal(point switch
        {
            "snapshot-reserved" or "snapshot-created-unrecorded" or "snapshot-identified" => ArchiveContentUpdatePhase.SnapshotReserved,
            "snapshot-created" or "transaction-opened-unrecorded" => ArchiveContentUpdatePhase.SnapshotCreated,
            "transaction-opened" or "snapshot-sealed" => ArchiveContentUpdatePhase.TransactionOpened,
            "snapshot-recorded" or "private-ready" or "rollback-before" or "rollback-after" => ArchiveContentUpdatePhase.SnapshotSealed,
            "ready" => ArchiveContentUpdatePhase.Ready,
            "commit-intent" or "native-committed" => ArchiveContentUpdatePhase.CommitIntent,
            "committed-recorded" => ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts,
            _ => ArchiveContentUpdatePhase.RolledBackWithRetainedArtifacts,
        }, facts.Phase);
        bool committed = point is "native-committed" or "committed-recorded";
        bool sealedSnapshot = point is not ("snapshot-reserved" or "snapshot-created-unrecorded" or "snapshot-identified" or
            "snapshot-created" or "transaction-opened-unrecorded" or "transaction-opened");
        Assert.Equal(point != "snapshot-reserved", File.Exists(facts.SnapshotPath));
        Assert.Equal(point is not ("snapshot-reserved" or "snapshot-created-unrecorded" or "snapshot-identified"), facts.SnapshotIdentity is not null);
        if (File.Exists(facts.SnapshotPath))
        {
            Assert.Equal(sealedSnapshot ? preparation.Original!.Length : 0, new FileInfo(facts.SnapshotPath).Length);
            if (sealedSnapshot)
            {
                Assert.Equal(preparation.Original!.Sha256, Hash(facts.SnapshotPath));
                Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(3).Bytes, File.ReadAllBytes(facts.SnapshotPath + ":Zone.Identifier"));
            }
        }
        Assert.Equal(point == "committed-recorded", facts.Published is not null);
        Assert.True(File.Exists(preparation.StagePath)); Assert.Equal(preparation.VerifiedStage!.Sha256, Hash(preparation.StagePath));
        using (var directory = WindowsArchiveDirectory.Open(root))
        using (var original = directory.OpenSource("original.tar"))
        {
            var observed = ((IArchiveRetainedFile)original).Observe(TestContext.Current.CancellationToken);
            Assert.Equal(preparation.Original!.Identity, observed.Version.Identity);
            Assert.Equal(committed ? preparation.VerifiedStage.Sha256 : preparation.Original.Sha256, observed.Version.Sha256);
            Assert.Equal(committed ? facts.PrivateMetadataSha256 : initial.GetProperty("MetadataDigest").GetString(), observed.MetadataDigest);
            Assert.Equal(committed ? facts.RequiredOriginSha256 : ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier,
                Convert.FromBase64String(initial.GetProperty("Origin").GetString()!)).Digest, observed.Origin.Digest);
        }
        using (var stream = File.OpenRead(preparation.ArchivePath))
        using (var reader = new TarReader(stream))
        {
            var entry = reader.GetNextEntry(); Assert.NotNull(entry); Assert.Equal("old", entry.Name);
            using var data = new MemoryStream(); entry.DataStream!.CopyTo(data);
            Assert.Equal(committed ? "replacement bytes"u8.ToArray() : "original bytes"u8.ToArray(), data.ToArray()); Assert.Null(reader.GetNextEntry());
        }
        foreach (var evidence in preparation.Sidecars) { Assert.NotNull(evidence.Version); Assert.Equal(evidence.Version.Sha256, Hash(evidence.Path)); }
        Assert.Equal("owned unchanged sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
        Assert.DoesNotContain(JobJournal.ReadAll(job.JournalPath), record => record.Type == "end");
        Assert.False(JournalRecovery.TryClose(job, "native content crash needs object-bound review"));
        Assert.Single(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        Assert.False(JournalRecovery.TryClose(job, "stale closure remains refused")); Assert.Equal(before, Snapshot());
        if (facts.SnapshotIdentity is { } snapshotIdentity)
        {
            using var directory = WindowsArchiveDirectory.Open(root);
            using var snapshot = directory.OpenRecoveryArtifact(Path.GetFileName(facts.SnapshotPath), Identity(snapshotIdentity), WindowsArchiveArtifactKind.Stage, facts.SnapshotMaximumLength);
            Assert.False(snapshot.CanWrite); var version = snapshot.CaptureVersion(TestContext.Current.CancellationToken);
            if (facts.Snapshot is not null) Assert.Equal(facts.Snapshot, version);
            Assert.Throws<ArchiveWriteException>(() => snapshot.BeginContentTransaction(((IArchiveRetainedFile)snapshot).Observe(TestContext.Current.CancellationToken), Guid.NewGuid(), TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        }
        string[] Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Hash(path)).ToArray();
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        static FileIdentity Identity(string value) => new(Convert.ToUInt64(value[..16], 16), value[17..]);
    }
    private static async Task Reap(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        // Cleanup must finish even when the test's cancellation token has fired.
        await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
    }
}
