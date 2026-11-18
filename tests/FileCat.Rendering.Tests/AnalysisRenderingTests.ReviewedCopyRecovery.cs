using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ReviewedCopyRecoveryReportsExactEffectsAndKeepsFiles()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        var token = TestContext.Current.CancellationToken;
        string input = Path.Combine(fixture.Root, "journal-test-source"), output = Path.Combine(fixture.Root, "journal-test-output");
        Directory.CreateDirectory(input);
        Directory.CreateDirectory(output);
        var sources = new[] { "committed.bin", "collision.bin", "uncertain.bin", "unprocessed.bin" };
        foreach (string name in sources) File.WriteAllText(Path.Combine(input, name), "source data");
        File.WriteAllText(Path.Combine(output, "collision.bin"), "existing data");
        var owner = new RecoveryTestOwner();
        var plan = new ReviewedAnalysisCopyOperation(Location.FileSystem(output),
            [.. sources.Select(name => new ReviewedAnalysisCopyEntry(new ItemRef(Location.FileSystem(input), name, EntryKind.File), 11))],
            (index, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                string name = sources[index];
                string destinationPath = Path.Combine(output, name);
                if (index == 1) throw new AnalysisExportTargetExistsException(name, new IOException("Live test collision"));
                Assert.InRange(index, 0, 2);
                File.Copy(Path.Combine(input, name), destinationPath, overwrite: false);
                if (index == 2) throw new AnalysisExportUncertainException(name, new IOException("Live test uncertain publication"));
                return Task.CompletedTask;
            }, owner);
        var job = await fixture.Services.Jobs.SubmitReviewedAnalysisCopyAsync(plan, token);
        await Until(() => job.State.IsFinished());
        Assert.True(owner.Released);
        Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Equal(1, job.ItemsDone);
        Assert.Equal(1, job.ItemsSkipped);
        var history = Assert.Single(JournalRecovery.Scan(fixture.Services.Paths.JournalDirectory));
        Assert.True(history.IsReviewedAnalysisCopy);
        Assert.False(history.CanContinueTransfer);
        Assert.Empty(JournalRecovery.FindStagedLeftovers(history));
        Assert.Empty(JournalRecovery.FindIncompleteCopies(history));
        Assert.Equal(Path.Combine(output, "uncertain.bin"), Assert.Single(history.OpenIntents).Target);
        Assert.Equal(new StepOutcome?[] { StepOutcome.Committed, StepOutcome.Skipped, StepOutcome.Uncertain },
            history.ReviewedCopyReceipts.Select(receipt => receipt.Outcome));
        Assert.Equal(sources.Take(3), history.ReviewedCopyReceipts.Select(receipt => Path.GetFileName(receipt.Destination)));
        string report = InterruptedRecoveryReport.Format(history, "Review", [], [], new([], [], false), false);
        Assert.Contains("Copy committed", report, StringComparison.Ordinal);
        Assert.Contains("Copy skipped", report, StringComparison.Ordinal);
        Assert.Contains("Publication unconfirmed", report, StringComparison.Ordinal);
        Assert.True(JournalRecovery.TryClose(history, "Reviewed for this fixture"));
        Assert.Empty(JournalRecovery.Scan(fixture.Services.Paths.JournalDirectory));
        Assert.Equal("source data", File.ReadAllText(Path.Combine(output, "committed.bin")));
        Assert.Equal("existing data", File.ReadAllText(Path.Combine(output, "collision.bin")));
        Assert.Equal("source data", File.ReadAllText(Path.Combine(output, "uncertain.bin")));
        Assert.False(File.Exists(Path.Combine(output, "unprocessed.bin")));
        Assert.Equal(4, Directory.GetFiles(input).Length);
    }

    private sealed class RecoveryTestOwner : IAsyncDisposable
    {
        internal bool Released { get; private set; }
        public ValueTask DisposeAsync() { Released = true; return ValueTask.CompletedTask; }
    }
}
