using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class InterruptedCopyVersionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Versions()
    {
        foreach (int length in new[] { 4, 65537 })
            foreach (string change in new[] { "unchanged", "destination-only", "source-prefix", "both-preserved",
                "both-source-restamped", "source-tail", "destination-larger", "destination-time", "source-removed",
                "target-removed", "source-truncated", "target-link" })
                yield return [length, change];
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public async Task Cleanup_retains_the_reviewed_partial_version_across_content_and_metadata_changes(int length, string change)
    {
        using var f = await Fixture.Create(length);
        var reviewed = Assert.Single(JournalRecovery.ReviewCopies(f.Job).Incomplete);
        string before = Hash(f.Target)!; var stamp = File.GetLastWriteTimeUtc(f.Target);
        byte[] replacement = Enumerable.Repeat((byte)'b', length).ToArray();
        if (change is "destination-only" or "both-preserved" or "both-source-restamped")
        { File.WriteAllBytes(f.Target, replacement); File.SetLastWriteTimeUtc(f.Target, stamp); }
        if (change is "source-prefix" or "both-preserved" or "both-source-restamped")
        {
            var sourceStamp = File.GetLastWriteTimeUtc(f.Source);
            File.WriteAllBytes(f.Source, [..replacement, ..f.Tail]);
            File.SetLastWriteTimeUtc(f.Source, change == "both-source-restamped" ? sourceStamp.AddSeconds(5) : sourceStamp);
        }
        if (change == "source-tail") File.WriteAllBytes(f.Source, [..f.Prefix, ..System.Text.Encoding.ASCII.GetBytes("changed tail")]);
        if (change == "destination-larger") File.WriteAllBytes(f.Target, [..f.Prefix, (byte)'x']);
        if (change == "destination-time") File.SetLastWriteTimeUtc(f.Target, stamp.AddSeconds(5));
        if (change == "source-removed") File.Delete(f.Source);
        if (change == "target-removed") File.Delete(f.Target);
        if (change == "source-truncated") File.WriteAllBytes(f.Source, f.Prefix);
        if (change == "target-link")
        {
            File.WriteAllBytes(f.Other, replacement); File.Delete(f.Target);
            try { File.CreateSymbolicLink(f.Target, f.Other); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Assert.Skip("Owned file symbolic link unavailable: " + ex.GetType().Name); }
        }
        var info = new FileInfo(f.Target); string? approvedNow = Hash(f.Target);
        bool metadataMatches = info.Exists && info.Length == reviewed.Length && info.LastWriteTimeUtc == reviewed.ModifiedUtc && info.CreationTimeUtc == reviewed.CreatedUtc;
        int deleted = JournalRecovery.DeleteIncompleteCopies([reviewed], out var kept);
        bool unchanged = change is "unchanged" or "source-tail";
        bool targetExists = File.Exists(f.Target);
        output.WriteLine("COPY_VERSION " + JsonSerializer.Serialize(new { length, change, deleted, Kept = kept,
            ReviewedSHA256 = before, CurrentApprovalSHA256 = approvedNow, FinalSHA256 = Hash(f.Target),
            metadataMatches, targetExists, SourceSHA256 = Hash(f.Source),
            ReviewedLength = reviewed.Length, CurrentLength = info.Exists ? info.Length : (long?)null,
            ReviewedModifiedUtc = reviewed.ModifiedUtc, CurrentModifiedUtc = info.LastWriteTimeUtc,
            ReviewedCreatedUtc = reviewed.CreatedUtc, CurrentCreatedUtc = info.CreationTimeUtc,
            OwnedRealInitialCopy = true, EndRecordRemovedToModelInterruption = true,
            NativeDesktop = false, PhysicalSource = false, AtomicAliasIdentityQualified = false }));
        if (change is "both-preserved" or "both-source-restamped")
            Assert.True(metadataMatches, "The owned timestamp-preservation precondition must hold before the byte guard is qualified.");
        Assert.Equal(unchanged ? 1 : 0, deleted);
        Assert.Equal(!unchanged && change != "target-removed", targetExists);
        Assert.Equal(targetExists ? [f.Target] : Array.Empty<string>(), kept);
        if (change == "target-link") Assert.Equal(replacement, File.ReadAllBytes(f.Other));
        if (targetExists) Assert.Equal(approvedNow, Hash(f.Target));
    }

    [Theory]
    [InlineData("target-lock")]
    [InlineData("source-lock")]
    [InlineData("target-readonly")]
    public async Task Native_Windows_cleanup_refusal_keeps_the_partial_and_restores_fixture_state(string mode)
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Real Windows sharing/read-only refusal requires Windows.");
        using var f = await Fixture.Create(4); var reviewed = Assert.Single(JournalRecovery.ReviewCopies(f.Job).Incomplete);
        var attributes = File.GetAttributes(f.Target); FileStream? held = null;
        try
        {
            if (mode == "source-lock")
            {
                held = new FileStream(f.Source, FileMode.Open, FileAccess.Read, FileShare.None);
                Assert.Throws<IOException>(() => { using var denied = File.OpenRead(f.Source); });
            }
            if (mode == "target-lock")
            {
                held = new FileStream(f.Target, FileMode.Open, FileAccess.Read, FileShare.Read);
                Assert.Throws<IOException>(() => { using var denied = new FileStream(f.Target, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); });
            }
            if (mode == "target-readonly")
            {
                File.SetAttributes(f.Target, attributes | FileAttributes.ReadOnly);
                Assert.Throws<UnauthorizedAccessException>(() => { using var denied = File.OpenWrite(f.Target); });
            }
            int deleted = JournalRecovery.DeleteIncompleteCopies([reviewed], out var kept);
            output.WriteLine("COPY_REFUSAL " + JsonSerializer.Serialize(new { mode, deleted, Kept = kept,
                ActualNativeRefusalVerified = true, TargetSHA256 = Hash(f.Target), NativeDesktop = false,
                OwnedFileSystem = true, PhysicalSource = false, NoJournalClosureClaim = true }));
            Assert.Equal(0, deleted); Assert.Equal([f.Target], kept); Assert.Equal(f.Prefix, File.ReadAllBytes(f.Target));
        }
        finally { held?.Dispose(); if (File.Exists(f.Target)) File.SetAttributes(f.Target, attributes); }
    }

    [Fact]
    public async Task A_legacy_metadata_only_review_cannot_authorize_partial_deletion()
    {
        using var f = await Fixture.Create(4); var approved = Assert.Single(JournalRecovery.ReviewCopies(f.Job).Incomplete);
        var legacy = new IncompleteCopy(Path: approved.Path, Source: approved.Source, Length: approved.Length,
            ModifiedUtc: approved.ModifiedUtc, CreatedUtc: approved.CreatedUtc);
        var (path, source, length, modified, created) = legacy;
        Assert.Equal((approved.Path, approved.Source, approved.Length, approved.ModifiedUtc, approved.CreatedUtc),
            (path, source, length, modified, created));
        int deleted = JournalRecovery.DeleteIncompleteCopies([legacy], out var kept);
        output.WriteLine("COPY_LEGACY " + JsonSerializer.Serialize(new { deleted, Kept = kept, TargetSHA256 = Hash(f.Target),
            ExplicitMetadataOnlyModel = true, OwnedFileSystem = true, NativeDesktop = false, PhysicalSource = false }));
        Assert.Equal(0, deleted); Assert.Equal([f.Target], kept); Assert.Equal(f.Prefix, File.ReadAllBytes(f.Target));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1048564)]
    public async Task Empty_and_near_limit_direct_partials_delete_the_unchanged_prefix(int length)
    {
        using var f = await Fixture.Create(length); var reviewed = Assert.Single(JournalRecovery.ReviewCopies(f.Job).Incomplete);
        long sourceLength = new FileInfo(f.Source).Length;
        Assert.True(sourceLength < TransferExecutor.DirectCopyLimit);
        int deleted = JournalRecovery.DeleteIncompleteCopies([reviewed], out var kept);
        output.WriteLine("COPY_BOUNDARY " + JsonSerializer.Serialize(new { length, deleted, Kept = kept,
            ReviewedSHA256 = typeof(IncompleteCopy).GetProperty("ContentSHA256")?.GetValue(reviewed),
            ExpectedSHA256 = Convert.ToHexString(SHA256.HashData(f.Prefix)), OwnedFileSystem = true,
            NativeDesktop = false, PhysicalSource = false, SourceLength = sourceLength,
            ActualInitialSourceBelowDirectCopyLimit = sourceLength < TransferExecutor.DirectCopyLimit }));
        Assert.Equal(1, deleted); Assert.Empty(kept); Assert.False(File.Exists(f.Target));
    }

    [Fact]
    public async Task Native_POSIX_parent_write_refusal_keeps_the_partial_and_restores_fixture_mode()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Real ordinary-user POSIX directory permission refusal requires POSIX.");
        if (Environment.UserName == "root") Assert.Skip("An ordinary account is required to prove the POSIX unlink refusal.");
        using var f = await Fixture.Create(4); var reviewed = Assert.Single(JournalRecovery.ReviewCopies(f.Job).Incomplete);
        var mode = File.GetUnixFileMode(f.Destination);
        try
        {
            File.SetUnixFileMode(f.Destination, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
            Assert.Throws<UnauthorizedAccessException>(() => File.Delete(f.Target));
            int deleted = JournalRecovery.DeleteIncompleteCopies([reviewed], out var kept);
            output.WriteLine("COPY_REFUSAL " + JsonSerializer.Serialize(new { mode = "posix-parent-write", deleted, Kept = kept,
                ActualNativeRefusalVerified = true, TargetSHA256 = Hash(f.Target), NativeDesktop = false,
                OwnedFileSystem = true, PhysicalSource = false, NoJournalClosureClaim = true }));
            Assert.Equal(0, deleted); Assert.Equal([f.Target], kept); Assert.Equal(f.Prefix, File.ReadAllBytes(f.Target));
        }
        finally { File.SetUnixFileMode(f.Destination, mode); }
    }

    private static string? Hash(string path) => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
    private sealed class Fixture : IDisposable
    {
        private readonly TempDir _dir = new();
        public string Source = "", Target = "", Other = "", Destination = "";
        public byte[] Prefix = [], Tail = System.Text.Encoding.ASCII.GetBytes("source tail");
        public InterruptedJob Job = null!;
        public static async Task<Fixture> Create(int length)
        {
            var f = new Fixture();
            try
            {
                var src = f._dir.Dir("source"); f.Destination = f._dir.Dir("destination"); var journals = f._dir.Dir("journal");
                f.Source = Path.Join(src, "owned.bin"); f.Target = Path.Join(f.Destination, "owned.bin"); f.Other = Path.Join(src, "other.bin");
                f.Prefix = Enumerable.Repeat((byte)'a', length).ToArray(); File.WriteAllBytes(f.Source, [..f.Prefix, ..f.Tail]);
                var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
                var jobs = new JobManager(new PortableFileOperations(), providers, journals);
                jobs.DecisionRequested += d => d.Resolve(new Decision(DecisionAction.CancelJob));
                var job = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(f.Source, EntryKind.File)], Destination = Location.FileSystem(f.Destination) });
                var timer = Stopwatch.StartNew();
                while (!job.State.IsFinished()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30)); await Task.Delay(10, TestContext.Current.CancellationToken); }
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(File.ReadAllBytes(f.Source), File.ReadAllBytes(f.Target));
                var journal = Assert.Single(Directory.GetFiles(journals, "job-*.fcj"));
                File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
                f.Job = Assert.Single(JournalRecovery.Scan(journals));
                File.WriteAllBytes(f.Target, f.Prefix); File.SetCreationTimeUtc(f.Target, DateTime.UtcNow);
                // Keep mtime earlier than later writes, within recovery's two-second job-creation admission window.
                // An arbitrary historical mtime also makes Unix creation-time fallback historical and excludes the fixture.
                File.SetLastWriteTimeUtc(f.Target, f.Job.CreatedUtc.AddMilliseconds(-500));
                Assert.True(File.GetCreationTimeUtc(f.Target) >= f.Job.CreatedUtc.AddSeconds(-2),
                    "The owned partial must satisfy the actual creation-time admission window.");
                return f;
            }
            catch { f.Dispose(); throw; }
        }
        public void Dispose() => _dir.Dispose();
    }
}
