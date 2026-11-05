using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class ArchiveInterruptedRecoveryTests
{
    [AvaloniaTheory]
    [InlineData("reserved")]
    [InlineData("committed-warning")]
    [InlineData("unknown")]
    [InlineData("mixed")]
    [InlineData("legacy")]
    [InlineData("legacy-committed")]
    [InlineData("origin-warning")]
    public async Task Archive_rows_offer_read_only_reporting_and_keep_all_recorded_files(string state)
    {
        using var f = await Fixture.Create(state);
        f.Vm.Operations.LoadInterrupted([f.Interrupted]);
        f.Vm.Operations.IsOpen = true;
        Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout();
        var row = Assert.Single(f.Vm.Operations.Interrupted);
        Assert.False(row.CanCleanup); Assert.False(row.CanMarkReviewed); Assert.False(row.CanRunAgain); Assert.False(row.CanReviewNewTransfer);
        Assert.Contains(state is "committed-warning" or "origin-warning" ? "recorded as committed" : state is "unknown" or "legacy" or "legacy-committed" ? "incomplete or unsupported" : "not been confirmed", row.Details);
        var buttons = f.Window.GetVisualDescendants().OfType<Button>().Where(b => b.IsVisible).ToList();
        Assert.DoesNotContain(buttons, b => b.Content as string is "Clean up partial files" or "Mark reviewed…");
        Assert.Contains(buttons, b => AutomationProperties.GetName(b) == row.ReviewReportLabel);

        var report = Assert.IsType<ReportWindow>(await f.Vm.ShowInterruptedReportAsync(f.Interrupted));
        try
        {
            await Wait(() => report.Reading is not null); await report.Reading!;
            Assert.Contains("Archive recovery facts (read-only)", report.Text);
            Assert.Contains("have not been inspected", report.Text);
            Assert.Contains(JsonSerializer.Serialize(f.StagePath), report.Text);
            if (!state.StartsWith("legacy", StringComparison.Ordinal))
            {
                Assert.Contains(JsonSerializer.Serialize(f.OriginPath), report.Text);
                Assert.Contains("Declared captured origin sources: 1", report.Text);
                Assert.Contains("origin-object", report.Text);
            }
            else
            {
                Assert.Contains("facts v1", report.Text);
                Assert.DoesNotContain("Declared captured origin sources:", report.Text);
            }
            if (state == "committed-warning")
            {
                Assert.Contains(JsonSerializer.Serialize(f.RetainedPath), report.Text);
                Assert.Contains(new string('B', 64), report.Text);
            }
            if (state == "unknown") Assert.Contains("Journal review warning:", report.Text);
            report.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Copy all")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Wait(() => report.StatusText == "The whole report is on the clipboard.");
            Assert.Equal(report.Text, await ClipboardExtensions.TryGetTextAsync(report.Clipboard!));
        }
        finally { report.Close(); }
        f.AssertUnchanged();
    }

    [AvaloniaTheory]
    [InlineData("cleanup")]
    [InlineData("mark")]
    [InlineData("retry")]
    [InlineData("new-transfer")]
    [InlineData("report")]
    public async Task Every_generic_entry_rechecks_current_archive_facts_instead_of_trusting_a_stale_row(string action)
    {
        using var f = await Fixture.Create("reserved");
        var stale = f.Interrupted with { ArchiveTransactions = new([], false, null), Kind = nameof(JobKind.Copy) };
        if (action == "report")
        {
            var report = Assert.IsType<ReportWindow>(await f.Vm.ShowInterruptedReportAsync(stale));
            try
            {
                await Wait(() => report.Reading is not null); await report.Reading!;
                Assert.Contains("Archive recovery facts (read-only)", report.Text);
            }
            finally { report.Close(); }
        }
        else
        {
            bool result = action switch
            {
                "cleanup" => await f.Vm.CleanupInterruptedAsync(stale),
                "mark" => await f.Vm.MarkInterruptedReviewedAsync(stale),
                "retry" => await f.Vm.RunInterruptedAgainAsync(stale),
                _ => await f.Vm.ReviewNewInterruptedTransferAsync(stale),
            };
            Assert.False(result);
            Assert.False(((OverlayDialogService)f.Vm.Dialogs).IsOpen);
        }
        Assert.Empty(f.Services.Jobs.Jobs);
        f.AssertUnchanged();
    }

    private static async Task Wait(Func<bool> predicate)
    {
        var clock = Stopwatch.StartNew();
        while (!predicate())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public required AppServices Services;
        public required MainViewModel Vm;
        public required MainWindow Window;
        public required string Root, StagePath, RetainedPath, OriginPath;
        public required InterruptedJob Interrupted;
        public required CountingProvider Provider;
        public required byte[] JournalBytes;

        public static async Task<Fixture> Create(string state)
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup)
                await startup;
            string archive = Path.Combine(root, "owned.tar"), stagePath = Path.Combine(root, ".owned-stage"), retainedPath = Path.Combine(root, ".owned-retired");
            string manifestPath = Path.Combine(root, ".owned-manifest"), originPath = Path.Combine(root, ".owned-origin");
            File.WriteAllText(stagePath, "owned rebuilt archive"); File.WriteAllText(retainedPath, "owned original archive");
            File.WriteAllText(originPath, "owned provenance");
            var original = new ArchiveObjectVersion("original-object", 1, new string('A', 64));
            var stage = new ArchiveObjectVersion("stage-object", 2, new string('B', 64));
            var manifest = new ArchiveObjectVersion("manifest-object", 3, new string('C', 64));
            var origin = new ArchiveObjectVersion("origin-object", 512, new string('F', 64));
            var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = new(archive, null, [], Profile: profile) }, "Owned archive review", "owned", [], []);
            Guid nonce = Guid.NewGuid(); string journalPath;
            using (var writer = JobJournal.Create(services.Paths.JournalDirectory, job))
            {
                journalPath = writer.Path;
                writer.Intent("archive-stage", archive, null, stagePath); // Exercises the UI's former direct staged-intent loop.
                var phases = new[] { ArchiveTransactionPhase.Reserved, ArchiveTransactionPhase.Rebuilding, ArchiveTransactionPhase.Verifying,
                    ArchiveTransactionPhase.ReadyToPublish, ArchiveTransactionPhase.Publishing, ArchiveTransactionPhase.Reconciling,
                    ArchiveTransactionPhase.Cleaning, ArchiveTransactionPhase.CommittedWithRetainedArtifacts };
                if (state == "legacy-committed") phases[^1] = ArchiveTransactionPhase.Committed;
                for (int i = 0; i < (state is "committed-warning" or "mixed" or "origin-warning" or "legacy-committed" ? phases.Length : 1); i++)
                    writer.ArchiveTransaction(new(nonce, i + 1, profile, phases[i], archive, "parent-object", original, stagePath, stage.Identity,
                        i >= 3 ? stage : null, i >= 3 ? retainedPath : null, i >= 3 ? manifestPath : null, i >= 3 ? manifest : null,
                        i >= 3 ? new string('E', 64) : null, i >= 5 ? stage : null, i >= 5 ? original : null,
                        i == 7 && state != "legacy-committed" ? [state == "origin-warning" ? originPath : retainedPath] : [],
                        originPath, origin.Identity, 1, i >= 3 ? origin : null));
                if (state == "legacy-committed") writer.Finish(JobState.Completed, "historical completed archive");
                if (state == "mixed") writer.ArchiveTransaction(new(Guid.NewGuid(), 1, profile, ArchiveTransactionPhase.Reserved,
                    Path.Combine(root, "second.tar"), "parent-object", null, Path.Combine(root, ".second-stage"), "second-stage-object"));
            }
            if (state == "unknown")
            {
                string json = new JsonObject { ["t"] = "archive-future", ["v"] = 99 }.ToJsonString();
                File.AppendAllText(journalPath, $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n");
            }
            if (state.StartsWith("legacy", StringComparison.Ordinal))
            {
                var records = JobJournal.ReadAll(journalPath).Select(record => (JsonObject)record.Node.DeepClone()).ToArray();
                foreach (var record in records)
                {
                    if (record["archiveJournalVersion"] is not null) record["archiveJournalVersion"] = 1;
                    if (record["t"]?.GetValue<string>() == "archive-transaction")
                    {
                        record["v"] = 1;
                        foreach (string key in new[] { "originPath", "originIdentity", "originSources", "originEvidence" }) record.Remove(key);
                    }
                }
                File.WriteAllText(journalPath, string.Concat(records.Select(record =>
                {
                    string json = record.ToJsonString(); return $"{Crc32.HashToUInt32(Encoding.UTF8.GetBytes(json)):x8} {json}\n";
                })));
            }
            var interrupted = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
            var provider = new CountingProvider(services.Providers.Get(Schemes.FileSystem), stagePath, retainedPath, originPath);
            services.Providers.Register(provider);
            return new() { Services = services, Vm = vm, Window = window, Root = root, StagePath = stagePath, RetainedPath = retainedPath, OriginPath = originPath,
                Interrupted = interrupted, Provider = provider, JournalBytes = File.ReadAllBytes(journalPath) };
        }

        public void AssertUnchanged()
        {
            Assert.Equal(0, Provider.ArtifactOpens);
            Assert.Equal("owned rebuilt archive", File.ReadAllText(StagePath));
            Assert.Equal("owned original archive", File.ReadAllText(RetainedPath));
            Assert.Equal("owned provenance", File.ReadAllText(OriginPath));
            Assert.Equal(JournalBytes, File.ReadAllBytes(Interrupted.JournalPath));
            Assert.True(JournalRecovery.IsInterrupted(Interrupted));
        }
        public void Dispose() => AccessibilityTests.Close(Services, Window, Root);
    }

    private sealed class CountingProvider(ResourceProvider inner, string stage, string retained, string origin) : ResourceProvider
    {
        public int ArtifactOpens;
        public override string Scheme => inner.Scheme;
        public override string GetDisplayPath(Location l) => inner.GetDisplayPath(l);
        public override Location? GetParent(Location l) => inner.GetParent(l);
        public override Location? GetChildLocation(Location l, in EntryData e) => inner.GetChildLocation(l, e);
        public override LocationCapabilities GetCapabilities(Location l) => inner.GetCapabilities(l);
        public override Task EnumerateAsync(Location l, IEnumerationSink sink, CancellationToken ct) => inner.EnumerateAsync(l, sink, ct);
        public override IContentSource? OpenContent(ItemRef item)
        {
            if (item.FileSystemPath == stage || item.FileSystemPath == retained || item.FileSystemPath == origin) Interlocked.Increment(ref ArtifactOpens);
            return inner.OpenContent(item);
        }
    }
}
