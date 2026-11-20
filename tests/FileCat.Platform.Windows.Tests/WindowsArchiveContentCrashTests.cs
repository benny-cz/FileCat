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
    [InlineData("cleanup-cataloged")] [InlineData("cleanup-completed")]
    [InlineData("cleanup-intent-0")] [InlineData("cleanup-unlinked-0")] [InlineData("cleanup-recorded-0")]
    [InlineData("cleanup-intent-1")] [InlineData("cleanup-unlinked-1")] [InlineData("cleanup-recorded-1")]
    [InlineData("cleanup-intent-2")] [InlineData("cleanup-unlinked-2")] [InlineData("cleanup-recorded-2")]
    [InlineData("cleanup-intent-3")] [InlineData("cleanup-unlinked-3")] [InlineData("cleanup-recorded-3")]
    [InlineData("cleanup-intent-4")] [InlineData("cleanup-unlinked-4")] [InlineData("cleanup-recorded-4")]
    [InlineData("cleanup-intent-5")] [InlineData("cleanup-unlinked-5")] [InlineData("cleanup-recorded-5")]
    [InlineData("recover:snapshot-recorded")] [InlineData("recover:private-ready")]
    [InlineData("recover:native-committed")] [InlineData("recover:committed-recorded")]
    [InlineData("recover:cleanup-intent-0")] [InlineData("recover:cleanup-unlinked-0")]
    [InlineData("recover:cleanup-recorded-2")] [InlineData("recover:cleanup-unlinked-5")]
    [InlineData("changed-target:committed-recorded")] [InlineData("changed-artifact:committed-recorded")]
    [InlineData("stale-journal:committed-recorded")] [InlineData("busy-journal:committed-recorded")]
    [InlineData("missing-catalog:snapshot-created")]
    public async Task Real_content_process_crashes_preserve_the_original_or_committed_bytes_and_all_recorded_evidence(string point)
    {
        RequireWindows();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Run the explicit NTFS transaction qualification lane.");
        string mode = ""; int separator = point.IndexOf(':');
        if (separator >= 0) { mode = point[..separator]; point = point[(separator + 1)..]; }
        using var fixture = new CrashFixture(); string root = fixture.Root;
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
        fixture.AwaitReleasedHandles();
        if (point.StartsWith("cleanup-", StringComparison.Ordinal))
        { AssertCleanupCrash(root, point); if (mode.Length != 0) AssertRecovery(root, mode); return; }
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
        if (mode.Length != 0) AssertRecovery(root, mode);
        string[] Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Hash(path)).ToArray();
        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        static FileIdentity Identity(string value) => new(Convert.ToUInt64(value[..16], 16), value[17..]);
    }
    private static void AssertRecovery(string root, string mode)
    {
        var job = Assert.Single(JournalRecovery.Scan(Path.Combine(root, "journals"), keepFinished: 0, maxAge: TimeSpan.Zero));
        var content = Assert.Single(job.ArchiveTransactions.ContentUpdates);
        var catalog = job.ArchiveTransactions.ContentCleanups.SingleOrDefault();
        string expected = Hash(job.JournalPath); string target = Path.Combine(root, "original.tar"); string originalTarget = Hash(target);
        if (mode == "changed-target") File.WriteAllBytes(target, "a later user version"u8.ToArray());
        if (mode == "changed-artifact") File.SetLastWriteTimeUtc(catalog!.Artifacts[^1].Path, DateTime.UnixEpoch.AddSeconds(1700001234));
        if (mode == "stale-journal")
        {
            string json = "{\"t\":\"note\",\"msg\":\"a later journal revision\"}";
            File.AppendAllText(job.JournalPath, $"{FileCat.Core.Jobs.Crc32.HashToUInt32(System.Text.Encoding.UTF8.GetBytes(json)):x8} {json}\n");
        }
        string[] before = Snapshot();
        if (mode != "recover")
        {
            if (mode == "busy-journal")
            {
                using var busy = new FileStream(job.JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                Refused();
            }
            else Refused();
            Assert.Equal(before, Snapshot()); return;
            void Refused()
            {
                var error = Record.Exception(() => { using var rejected = WindowsArchiveCleanupRecovery.Open(job.JournalPath, expected, content.Nonce, TestContext.Current.CancellationToken); });
                Assert.True(error is ArchiveWriteException or IOException or System.ComponentModel.Win32Exception, error?.ToString());
            }
        }
        using (var recovered = WindowsArchiveCleanupRecovery.Open(job.JournalPath, expected, content.Nonce, TestContext.Current.CancellationToken))
        {
            uint absent = 0, remaining = 0;
            for (int i = catalog!.DeletedCount; i < catalog.Artifacts.Count; i++)
            { remaining |= 1u << i; if (!File.Exists(catalog.Artifacts[i].Path)) absent |= 1u << i; }
            var outcome = recovered.ResumeCleanup(TestContext.Current.CancellationToken);
            Assert.True(outcome.IsResolved); Assert.Equal(catalog!.Artifacts.Count, outcome.ReviewedCount);
            Assert.Equal(absent, outcome.AbsentMask); Assert.Equal(remaining & ~absent, outcome.DeletedMask);
            Assert.Equal(originalTarget, Hash(target)); Assert.All(catalog.Artifacts, artifact => Assert.False(File.Exists(artifact.Path)));
            Assert.Same(outcome, recovered.ResumeCleanup(TestContext.Current.CancellationToken));
            Assert.False(JournalRecovery.ReadArchiveTransactions(job.JournalPath).RequiresReview);
            recovered.Finish("native recovery retained the verified archive and reconciled every private artifact");
        }
        var final = JournalRecovery.ReadArchiveTransactions(job.JournalPath);
        Assert.False(final.RequiresReview); Assert.True(Assert.Single(final.ContentRecoveries).IsResolved);
        Assert.Equal(originalTarget, Hash(target)); Assert.Equal("owned unchanged sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
        static string Hash(string path)
        { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(SHA256.HashData(stream)); }
        string[] Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Hash(path)).ToArray();
    }
    private static void AssertCleanupCrash(string root, string point)
    {
        string[] before = Snapshot(); string journals = Path.Combine(root, "journals");
        var job = Assert.Single(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        var recovery = job.ArchiveTransactions; Assert.Null(recovery.Problem);
        var content = Assert.Single(recovery.ContentUpdates); var cleanup = Assert.Single(recovery.ContentCleanups);
        var preparation = Assert.Single(recovery.Transactions);
        bool complete = point == "cleanup-completed", cataloged = point == "cleanup-cataloged";
        string[] parts = point.Split('-'); int index = parts.Length == 3 ? int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : 0;
        int physical = complete ? cleanup.Artifacts.Count : cataloged ? 0 : parts[1] is "unlinked" or "recorded" ? index + 1 : index;
        int recorded = complete ? cleanup.Artifacts.Count : cataloged ? 0 : parts[1] == "recorded" ? index + 1 : index;
        Assert.Equal(6, cleanup.Artifacts.Count); Assert.Equal(recorded, cleanup.DeletedCount);
        Assert.Equal(complete ? ArchiveContentCleanupPhase.Completed : cataloged ? ArchiveContentCleanupPhase.Cataloged :
            parts[1] == "recorded" ? ArchiveContentCleanupPhase.Deleted : ArchiveContentCleanupPhase.DeleteIntent, cleanup.Phase);
        Assert.Equal(!complete, recovery.RequiresReview); Assert.Equal(complete, recovery.IsContentResolved(content.Nonce));
        Assert.Equal(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, content.Phase);
        using var directory = WindowsArchiveDirectory.Open(root);
        using (var target = directory.OpenSource("original.tar"))
        {
            var actual = ((IArchiveRetainedFile)target).Observe(TestContext.Current.CancellationToken);
            Assert.Equal(content.Published, actual.Version); Assert.Equal(content.PrivateMetadataSha256, actual.MetadataDigest);
            Assert.Equal(content.RequiredOriginSha256, actual.Origin.Digest);
        }
        for (int i = 0; i < cleanup.Artifacts.Count; i++)
        {
            var artifact = cleanup.Artifacts[i]; Assert.Equal(i >= physical, File.Exists(artifact.Path));
            if (i < physical) continue;
            var id = new FileIdentity(Convert.ToUInt64(artifact.Version.Identity[..16], 16), artifact.Version.Identity[17..]);
            using var held = directory.OpenRecoveryArtifact(Path.GetFileName(artifact.Path), id, (WindowsArchiveArtifactKind)artifact.Kind,
                i == cleanup.Artifacts.Count - 1 ? cleanup.Artifacts[i].Version.Length : i == 0 ? preparation.StageMaximumLength :
                preparation.Sidecars.Single(sidecar => sidecar.Path == artifact.Path).MaximumLength);
            var observed = ((IArchiveRetainedFile)held).Observe(TestContext.Current.CancellationToken);
            Assert.Equal(artifact.Version, observed.Version); Assert.Equal(artifact.MetadataSha256, observed.MetadataDigest);
            Assert.Equal(artifact.OriginSha256, observed.Origin.Digest); Assert.False(held.CanWrite);
            Assert.Throws<ArchiveWriteException>(() => ((IArchiveContentUpdateStage)held).DeleteOwnedCleanup(artifact, cleanup.ParentIdentity, TestContext.Current.CancellationToken));
        }
        // The old byte snapshot is last: it survives every earlier artifact's deletion and all recorded intents.
        Assert.Equal(physical < cleanup.Artifacts.Count, File.Exists(content.SnapshotPath));
        using (var stream = File.OpenRead(preparation.ArchivePath))
        using (var reader = new TarReader(stream))
        {
            var entry = reader.GetNextEntry(); Assert.NotNull(entry); Assert.Equal("old", entry.Name);
            using var bytes = new MemoryStream(); entry.DataStream!.CopyTo(bytes); Assert.Equal("replacement bytes"u8.ToArray(), bytes.ToArray()); Assert.Null(reader.GetNextEntry());
        }
        Assert.Equal("owned unchanged sentinel", File.ReadAllText(Path.Combine(root, "sentinel")));
        Assert.DoesNotContain(JobJournal.ReadAll(job.JournalPath), record => record.Type == "end");
        Assert.Single(JournalRecovery.Scan(journals, keepFinished: 0, maxAge: TimeSpan.Zero));
        if (!complete) Assert.False(JournalRecovery.TryClose(job, "cleanup outcome remains unconfirmed"));
        Assert.Equal(before, Snapshot());
        if (complete) Assert.True(JournalRecovery.TryClose(job, "all owned artifact deletions were recorded"));
        string[] Snapshot() => Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => Path.GetRelativePath(root, path) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    }
    private static async Task Reap(Process process)
    {
        if (!process.HasExited) process.Kill(entireProcessTree: true);
        // Cleanup must finish even when the test's cancellation token has fired.
        await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
    }

    private sealed class CrashFixture : IDisposable
    {
        private readonly Fixture fixture = new();
        public string Root => fixture.Root;
        public void AwaitReleasedHandles() => AfterRundown(() =>
        {
            var held = new List<FileStream>();
            try
            {
                foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                    held.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None));
            }
            finally { foreach (var file in held) file.Dispose(); }
        }, "exclusive evidence-readiness check");
        public void Dispose() => AfterRundown(fixture.Dispose, "cleanup");
        private static void AfterRundown(Action action, string operation)
        {
            // The killed process is reaped above, but Windows can still be completing native handle/transaction
            // rundown. Retry only the two sharing errors, only for this owned crash fixture, and report any delay.
            var elapsed = Stopwatch.StartNew(); int retries = 0;
            while (true)
            {
                try { action(); break; }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && elapsed.Elapsed < TimeSpan.FromSeconds(5))
                { retries++; Thread.Sleep(25); }
            }
            if (retries != 0) TestContext.Current.TestOutputHelper!.WriteLine($"Crash fixture {operation} waited {elapsed.ElapsedMilliseconds} ms for {retries} sharing violations after process exit.");
        }
    }
}
