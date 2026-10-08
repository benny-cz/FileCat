using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
namespace FileCat.Core.Tests;
public sealed class InterruptedCopySelectionTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (int length in new[] { 4, 65537 })
            foreach (string selection in new[] { "single-file", "two-folders", "manifest" })
                foreach (bool selected in new[] { true, false }) yield return [length, selection, selected];
    }
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Cleanup_review_never_admits_a_file_outside_the_actual_job_selection(int length, string selection, bool selected)
    {
        using var dir = new TempDir(); var src = dir.Dir("source"); var dst = dir.Dir("destination"); var journalDir = dir.Dir("journals");
        int count = selection == "manifest" ? JobJournal.HeaderSampleSize + 1 : selection == "two-folders" ? 2 : 1;
        var prefix = Enumerable.Repeat((byte)'a', length).ToArray(); byte[] full = [..prefix, ..System.Text.Encoding.ASCII.GetBytes("source tail")];
        var sources = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string folder = selection == "two-folders" && i == 1 ? dir.Dir("second-source") : src;
            string path = Path.Join(folder, "selected-" + i + ".bin"); File.WriteAllBytes(path, full); sources.Add(path);
        }
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var operations = new CapturingOperations(journalDir); var jobs = new JobManager(operations, providers, journalDir);
        jobs.DecisionRequested += d => d.Resolve(new Decision(DecisionAction.CancelJob));
        var initial = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = sources.Select(p => ItemRef.ForFileSystemPath(p, EntryKind.File)).ToArray(), Destination = Location.FileSystem(dst) });
        var timer = Stopwatch.StartNew();
        while (!initial.State.IsFinished()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30)); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.Equal(JobState.Completed, initial.State);
        var journal = Assert.Single(Directory.GetFiles(journalDir, "job-*.fcj"));
        File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
        // Completion removed the source sidecar. Restore the exact actual writer bytes captured before copying;
        // together with removing the end record, this models the interrupted journal without inventing its selection.
        if (operations.Manifest is not null) File.WriteAllBytes(JobJournal.ManifestPathOf(journal), operations.Manifest);
        var job = Assert.Single(JournalRecovery.Scan(journalDir));
        var loaded = Assert.IsAssignableFrom<IReadOnlyList<string>>(JournalRecovery.LoadSources(job)); Assert.Equal(sources.Count, loaded.Count);
        string source = selected ? sources[^1] : Path.Join(Path.GetDirectoryName(sources[^1])!, "unselected.bin");
        if (!selected) File.WriteAllBytes(source, full);
        string target = Path.Join(dst, Path.GetFileName(source)); File.WriteAllBytes(target, prefix); File.SetCreationTimeUtc(target, DateTime.UtcNow);
        bool inSelection = loaded.Contains(source, PathUtil.SafetyComparer); Assert.Equal(selected, inSelection);
        var review = JournalRecovery.ReviewCopies(job); int deleted = JournalRecovery.DeleteIncompleteCopies(review.Incomplete, out var kept);
        output.WriteLine("COPY_SELECTION " + JsonSerializer.Serialize(new { length, selection, selected, inSelection,
            SourceCount = job.SourceCount, SampleCount = job.Sources.Count, ManifestPresent = job.ManifestPath is not null,
            Incomplete = review.Incomplete.Select(c => c.Path).ToArray(), review.LimitReached, deleted, Kept = kept,
            TargetExists = File.Exists(target), TargetSHA256 = File.Exists(target) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) : null,
            ExpectedSHA256 = Convert.ToHexString(SHA256.HashData(prefix)), SourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
            OwnedRealInitialCopy = true, EndRecordRemovedToModelInterruption = true, ActualWriterManifestCapturedBeforeCopyAndRestored = operations.Manifest is not null,
            ManifestSHA256 = operations.Manifest is null ? null : Convert.ToHexString(SHA256.HashData(operations.Manifest)), NativeDesktop = false, PhysicalSource = false,
            AtomicAliasIdentityQualified = false }));
        Assert.Equal(selected ? 1 : 0, review.Incomplete.Count); Assert.Equal(selected ? 1 : 0, deleted);
        Assert.Equal(!selected, File.Exists(target)); if (!selected) Assert.Equal(prefix, File.ReadAllBytes(target));
    }
    [Theory]
    [InlineData(4, "missing")]
    [InlineData(65537, "missing")]
    [InlineData(4, "torn")]
    [InlineData(65537, "torn")]
    public async Task An_unavailable_actual_source_manifest_keeps_the_partial_and_reports_incomplete_review(int length, string change)
    {
        using var dir = new TempDir(); var src = dir.Dir("source"); var dst = dir.Dir("destination"); var journals = dir.Dir("journals");
        var prefix = Enumerable.Repeat((byte)'a', length).ToArray();
        var paths = Enumerable.Range(0, JobJournal.HeaderSampleSize + 1).Select(i => Path.Join(src, "selected-" + i + ".bin")).ToArray();
        foreach (var path in paths) File.WriteAllBytes(path, [..prefix, (byte)'z']);
        var job = await CopyInterrupted(paths, dst, journals); Assert.NotNull(job.ManifestPath);
        if (change == "missing") File.Delete(job.ManifestPath); else File.WriteAllLines(job.ManifestPath!, paths.Take(paths.Length - 1));
        Assert.Null(JournalRecovery.LoadSources(job));
        var target = Path.Join(dst, Path.GetFileName(paths[^1])); File.WriteAllBytes(target, prefix); File.SetCreationTimeUtc(target, DateTime.UtcNow);
        var review = JournalRecovery.ReviewCopies(job); int deleted = JournalRecovery.DeleteIncompleteCopies(review.Incomplete, out var kept);
        output.WriteLine("COPY_SELECTION_UNAVAILABLE " + JsonSerializer.Serialize(new { length, change, job.SourceCount,
            IncompleteCount = review.Incomplete.Count, review.LimitReached, deleted, TargetExists = File.Exists(target),
            TargetSHA256 = File.Exists(target) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) : null,
            ExpectedSHA256 = Convert.ToHexString(SHA256.HashData(prefix)), ActualWriterManifestCapturedAndRestored = true,
            NativeDesktop = false, PhysicalSource = false }));
        Assert.True(review.LimitReached); Assert.Empty(review.Incomplete); Assert.Equal(0, deleted); Assert.Equal(prefix, File.ReadAllBytes(target));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(65537, false)]
    [InlineData(4, true)]
    [InlineData(65537, true)]
    public async Task A_selected_directory_covers_its_child_but_not_a_prefix_sibling(int length, bool sibling)
    {
        using var dir = new TempDir(); var root = dir.Dir("selected"); var beside = dir.Dir("selected-extra"); var dst = dir.Dir("destination"); var journals = dir.Dir("journals");
        var prefix = Enumerable.Repeat((byte)'a', length).ToArray();
        var chosen = Path.Join(root, "child.bin"); var otherChosen = Path.Join(beside, "other.bin");
        File.WriteAllBytes(chosen, [..prefix, (byte)'z']); File.WriteAllBytes(otherChosen, [..prefix, (byte)'z']);
        var job = await CopyInterrupted([root, otherChosen], dst, journals);
        string source = sibling ? Path.Join(beside, "unselected.bin") : chosen;
        string target = sibling ? Path.Join(dst, "unselected.bin") : Path.Join(dst, "selected", "child.bin");
        if (sibling) File.WriteAllBytes(source, [..prefix, (byte)'z']);
        File.WriteAllBytes(target, prefix); File.SetCreationTimeUtc(target, DateTime.UtcNow);
        var review = JournalRecovery.ReviewCopies(job); int deleted = JournalRecovery.DeleteIncompleteCopies(review.Incomplete, out var kept);
        output.WriteLine("COPY_SELECTION_DIRECTORY " + JsonSerializer.Serialize(new { length, sibling, IncompleteCount = review.Incomplete.Count,
            deleted, TargetExists = File.Exists(target), TargetSHA256 = File.Exists(target) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(target))) : null,
            ExpectedSHA256 = Convert.ToHexString(SHA256.HashData(prefix)), ActualSelectedRoots = job.Sources,
            ActualSource = source, SourceSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),
            NativeDesktop = false, PhysicalSource = false, AtomicAliasIdentityQualified = false }));
        Assert.Equal(sibling ? 0 : 1, review.Incomplete.Count); Assert.Equal(sibling ? 0 : 1, deleted);
        Assert.Equal(sibling, File.Exists(target)); if (sibling) Assert.Equal(prefix, File.ReadAllBytes(target));
    }

    [Theory]
    [InlineData("over-budget")]
    [InlineData("count-mismatch")]
    public async Task A_synthetic_unknown_or_inconsistent_selection_count_cannot_authorize_cleanup(string mode)
    {
        using var dir = new TempDir(); var src = dir.Dir("source"); var dst = dir.Dir("destination"); var journals = dir.Dir("journals");
        var paths = new[] { Path.Join(src, "one.bin"), Path.Join(src, "two.bin") }; foreach (var p in paths) File.WriteAllText(p, "source bytes");
        var actual = await CopyInterrupted(paths, dst, journals);
        var job = actual with { SourceCount = mode == "over-budget" ? 1001 : 1 };
        var target = Path.Join(dst, "one.bin"); File.WriteAllText(target, "source"); File.SetCreationTimeUtc(target, DateTime.UtcNow);
        var review = JournalRecovery.ReviewCopies(job); int deleted = JournalRecovery.DeleteIncompleteCopies(review.Incomplete, out var kept);
        output.WriteLine("COPY_SELECTION_COUNT " + JsonSerializer.Serialize(new { mode, SourceCount = job.SourceCount,
            ActualSourceCount = actual.SourceCount, IncompleteCount = review.Incomplete.Count, review.LimitReached, deleted,
            TargetExists = File.Exists(target), TargetText = File.Exists(target) ? File.ReadAllText(target) : null,
            SyntheticRecordCountMutation = true, OwnedRealInitialCopy = true, NativeDesktop = false, PhysicalSource = false }));
        Assert.True(review.LimitReached); Assert.Empty(review.Incomplete); Assert.Equal(0, deleted); Assert.Equal("source", File.ReadAllText(target));
    }

    private static async Task<InterruptedJob> CopyInterrupted(IReadOnlyList<string> sources, string destination, string journals)
    {
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); var ops = new CapturingOperations(journals);
        var jobs = new JobManager(ops, providers, journals); jobs.DecisionRequested += d => d.Resolve(new Decision(DecisionAction.CancelJob));
        var initial = jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = sources.Select(p => ItemRef.ForFileSystemPath(p, Directory.Exists(p) ? EntryKind.Directory : EntryKind.File)).ToArray(), Destination = Location.FileSystem(destination) });
        var timer = Stopwatch.StartNew();
        while (!initial.State.IsFinished()) { Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30)); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.Equal(JobState.Completed, initial.State); var journal = Assert.Single(Directory.GetFiles(journals, "job-*.fcj"));
        File.WriteAllLines(journal, File.ReadAllLines(journal).Where(l => !l.Contains("\"t\":\"end\"", StringComparison.Ordinal)));
        if (ops.Manifest is not null) File.WriteAllBytes(JobJournal.ManifestPathOf(journal), ops.Manifest);
        return Assert.Single(JournalRecovery.Scan(journals));
    }

    private sealed class CapturingOperations(string journals) : PortableFileOperations
    {
        public byte[]? Manifest;
        private bool _captured;
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            if (!_captured)
            {
                _captured = true; var paths = Directory.GetFiles(journals, "*.sources");
                if (paths.Length > 0) Manifest = File.ReadAllBytes(Assert.Single(paths));
            }
            base.CopyFile(source, destination, options, progress, ct);
        }
    }
}
