using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeReviewedCopyQueuesOnlyApprovedEntriesAndKeepsLateCollisions(bool indexed)
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        string work = Path.Combine(Path.GetTempPath(), "filecat-copy-queue-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(work, "input"), output = Path.Combine(work, "output");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        var names = new[] { "copy.bin", "swap.bin", "collision.bin", "skip.bin" };
        foreach (string name in names) File.WriteAllText(Path.Combine(input, name), "source " + name);
        File.WriteAllText(Path.Combine(output, "collision.bin"), "existing");
        var token = TestContext.Current.CancellationToken;
        try
        {
            await using var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
            var session = await StartAcquiredAsync(acquisition, fixture, input);
            var (identity, paths) = await FinishedFilesAsync(session, token);
            var ids = paths.Values.ToImmutableHashSet();
            if (!OperatingSystem.IsWindows())
            {
                await Assert.ThrowsAsync<NotSupportedException>(() => acquisition.PrepareReviewedCopyAsync(identity, ids, output, token));
                Assert.Equal("existing", File.ReadAllText(Path.Combine(output, "collision.bin")));
                return;
            }

            using var styles = new ProductStyles();
            using var ui = new HistoryDialogFixture(720, 640, 20);
            var jobs = fixture.Services.Jobs;
            jobs.MaxConcurrent = 0;
            int baseline = AnalysisNativeHandles.Count;
            await using var review = await acquisition.PrepareReviewedCopyAsync(identity, ids, output, token);
            Assert.Equal(4, review.Rows.Length);
            Assert.Contains("already exists", Assert.Single(review.Rows, row => row.PathId == paths["collision.bin"]).Refusal);
            Assert.Single(Directory.EnumerateFileSystemEntries(output));
            var dialog = DiskSpaceWindow.ReviewCopyChoicesAsync(ui.Dialogs, review.Destination, review.Rows, token);
            await Until(() => ui.Dialogs.IsOpen);
            Frame(ui.Window);
            Assert.True(ui.Find("Cancel / Keep files").IsDefault);
            Assert.False(ui.Find("Queue copy of 3 files").IsDefault);
            var skip = ui.Window.GetVisualDescendants().OfType<CheckBox>()
                .Single(box => AutomationLabel(box)?.EndsWith("skip.bin", StringComparison.Ordinal) == true);
            Assert.True(skip.Focus());
            ui.Window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            ui.Window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Assert.False(skip.IsChecked);
            ui.Click("Queue copy of 2 files");
            var included = Assert.IsType<ImmutableHashSet<ulong>>(await dialog);
            Assert.True(included.SetEquals([paths["copy.bin"], paths["swap.bin"]]));
            var job = await review.SubmitAsync(included, token);
            Assert.Equal(JobState.Queued, job.State);
            Assert.Throws<ArgumentException>(() => jobs.Submit(job.Request));
            await Assert.ThrowsAsync<InvalidOperationException>(() => review.SubmitAsync(included, token));
            Assert.ThrowsAny<IOException>(() => File.Open(Path.Combine(input, "copy.bin"), FileMode.Open, FileAccess.Write).Dispose());
            using (File.Open(Path.Combine(input, "skip.bin"), FileMode.Open, FileAccess.Write)) { }
            File.WriteAllText(Path.Combine(output, "swap.bin"), "late arrival");
            jobs.MaxConcurrent = 1;
            jobs.Schedule();
            await Until(() => job.State.IsFinished(), () => string.Join('\n', job.Issues.Select(issue => issue.Message)));
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Equal(1, job.ItemsDone);
            Assert.Equal(1, job.ItemsSkipped);
            Assert.Equal("source copy.bin", File.ReadAllText(Path.Combine(output, "copy.bin")));
            Assert.Equal("late arrival", File.ReadAllText(Path.Combine(output, "swap.bin")));
            Assert.Equal("existing", File.ReadAllText(Path.Combine(output, "collision.bin")));
            Assert.Equal(3, Directory.EnumerateFileSystemEntries(output).Count());
            Assert.Equal(4, Directory.EnumerateFileSystemEntries(input).Count());
            Assert.Equal(baseline, AnalysisNativeHandles.Count);
            string journal = Assert.Single(Directory.EnumerateFiles(fixture.Services.Paths.JournalDirectory, "*.fcj"));
            var records = JobJournal.ReadAll(journal);
            Assert.Equal(new[] { nameof(StepOutcome.Committed), nameof(StepOutcome.Skipped) },
                records.Where(record => record.Type == "done").Select(record => record.Get("outcome")));
            Assert.Equal("end", records[^1].Type);
            Assert.Empty(JournalRecovery.Scan(fixture.Services.Paths.JournalDirectory));

            jobs.MaxConcurrent = 0;
            await using var canceledReview = await acquisition.PrepareReviewedCopyAsync(identity, [paths["skip.bin"]], output, token);
            var canceledJob = await canceledReview.SubmitAsync([paths["skip.bin"]], token);
            Assert.Equal(JobState.Queued, canceledJob.State);
            canceledJob.Cancel();
            await Until(() => canceledJob.State.IsFinished());
            Assert.Equal(JobState.Canceled, canceledJob.State);
            Assert.False(File.Exists(Path.Combine(output, "skip.bin")));
            Assert.Equal(baseline, AnalysisNativeHandles.Count);
        }
        finally
        {
            var active = fixture.Services.Jobs.Jobs.Where(job => !job.State.IsFinished()).ToArray();
            foreach (var job in active) job.Cancel();
            if (active.Length > 0) await Until(() => active.All(job => job.State.IsFinished()));
            Directory.Delete(work, recursive: true);
        }

        static async Task<(SessionIdentity Identity, Dictionary<string, ulong> Paths)> FinishedFilesAsync(AnalysisSession session, CancellationToken cancellation)
        {
            await using var dataset = await session.AcquireDatasetAsync(cancellation);
            if (dataset is not null)
            {
                var page = await dataset.ReadPathsAsync(0, cancellation: cancellation);
                return (dataset.Descriptor.Identity, page.Rows.Where(row => row.Kind == AnalysisKind.File).ToDictionary(row => row.Name, row => row.Id));
            }
            var snapshot = session.Snapshot();
            return (snapshot.Identity, snapshot.Nodes.Where(row => row.Kind == AnalysisKind.File).ToDictionary(row => row.Name, row => row.Id));
        }
    }

    [Fact]
    public async Task NativeReviewedCopyPreservesStreamsAndKeepsExistingFiles()
    {
        OrdinaryPreparation();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Throws<PlatformNotSupportedException>(() =>
                new WindowsAnalysisPrivateDirectory.ReviewedOutputFolder(Path.GetTempPath(), null));
            return;
        }
        using var fixture = new PreparationFixture();
        string work = Path.Combine(Path.GetTempPath(), "filecat-reviewed-copy-" + Guid.NewGuid().ToString("N"));
        string input = Path.Combine(work, "source"), output = Path.Combine(work, "destination");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        string path = Path.Combine(input, "source.bin"), copied = Path.Combine(output, "copy.bin");
        byte[] data = new byte[AnalysisLimits.BatchBytes + 15];
        new Random(42).NextBytes(data);
        byte[] extra = [9, 8, 7, 0, 6], mark = "[ZoneTransfer]\r\nZoneId=3\r\n"u8.ToArray();
        File.WriteAllBytes(path, data);
        File.WriteAllBytes(path + ":fixture-extra", extra);
        File.WriteAllBytes(path + ":Zone.Identifier", mark);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        File.SetAttributes(path, FileAttributes.Archive | FileAttributes.Hidden);
        var token = TestContext.Current.CancellationToken;
        var devices = new AnalysisIoDevices(PathUtil.GetDeviceKey(input), PathUtil.GetDeviceKey(output));
        int prior = AnalysisNativeHandles.Count;
        try
        {
            await AnalysisIo.Run(fixture.Services.Io, devices, cancellation =>
            {
                using var source = new WindowsAnalysisBackend(input);
                var root = source.RootEntry;
                var child = Assert.Single(source.Children(root, cancellation));
                var directory = new AnalysisBranchComponent(root.Name, root.Identity!, root.CreatedUtc!.Value, root.CreatedPrecisionTicks!.Value);
                var target = new AnalysisPrimaryContentTarget(2, directory, [], child.Name, child.Identity!, child.Version,
                    child.CreatedUtc!.Value, child.CreatedPrecisionTicks!.Value, (long)child.LogicalLength.Value!.Value);
                var unprotected = new HashSet<string>(StringComparer.Ordinal);
                using var review = source.ReviewCopy(target, unprotected, cancellation);
                Assert.Equal(3, review.Streams.Length);
                using var destination = new WindowsAnalysisPrivateDirectory.ReviewedOutputFolder(output, null);
                Assert.Empty(Directory.EnumerateFileSystemEntries(output));
                Assert.ThrowsAny<IOException>(() => Directory.Move(output, output + "-renamed"));
                Assert.ThrowsAny<IOException>(() => review.CopyTo(destination, "protected.bin",
                    new HashSet<string>(StringComparer.Ordinal) { destination.Identity }, cancellation));
                Assert.Empty(Directory.EnumerateFileSystemEntries(output));

                Assert.ThrowsAny<IOException>(() => File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
                Assert.ThrowsAny<IOException>(() => File.Open(path + ":fixture-extra", FileMode.Open, FileAccess.Write, FileShare.ReadWrite).Dispose());
                Assert.ThrowsAny<IOException>(() => Directory.Move(input, input + "-renamed"));
                var receipt = review.CopyTo(destination, "copy.bin", unprotected, cancellation);
                Assert.Equal(data.Length + mark.Length + extra.Length, receipt.Bytes);
                Assert.Equal(3, receipt.Streams);
                Assert.Throws<AnalysisExportTargetExistsException>(() =>
                    review.CopyTo(destination, "copy.bin", unprotected, cancellation));
                review.Dispose();
                using (var changed = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                {
                    changed.SetLength(0);
                    changed.Write([1, 2, 3]);
                }
                Assert.Throws<AnalysisSourceChangedException>(() =>
                    source.ReviewCopy(target, unprotected, cancellation));
                return true;
            }, token);
            Assert.Equal(data, File.ReadAllBytes(copied));
            Assert.Equal(extra, File.ReadAllBytes(copied + ":fixture-extra"));
            Assert.Equal(mark, File.ReadAllBytes(copied + ":Zone.Identifier"));
            Assert.Equal(FileAttributes.Archive | FileAttributes.Hidden, File.GetAttributes(copied));
            Assert.Equal(new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc), File.GetLastWriteTimeUtc(copied));
            Assert.Equal(copied, Assert.Single(Directory.EnumerateFileSystemEntries(output)));
            Assert.Equal(prior, AnalysisNativeHandles.Count);
        }
        finally { Directory.Delete(work, recursive: true); }
    }
}
