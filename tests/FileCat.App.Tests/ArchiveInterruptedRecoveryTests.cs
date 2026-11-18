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
    [Fact]
    public void Legacy_ZIP_recovery_offers_a_report_with_all_names_and_withholds_automatic_cleanup()
    {
        string root = Path.GetTempPath();
        string archive = Path.Join(root, "original.zip");
        string stage = Path.Join(root, ".filecat-zip-owned.tmp");
        string backup = Path.Join(root, ".filecat-zip-owned.bak");
        var job = new InterruptedJob(Path.Join(root, "owned.fcj"), nameof(JobKind.ArchiveUpdate), "Legacy ZIP update",
            DateTime.UtcNow, [], null, [new(1, JobJournal.LegacyZipUpdateOp, archive, null, stage) { RecoveryPath = backup }], [], 0, 0);
        var row = new InterruptedJobViewModel(job);
        Assert.False(row.CanCleanup);
        Assert.True(row.CanMarkReviewed);
        Assert.Contains("manual review", row.Details);
        string report = InterruptedRecoveryReport.Format(job, "Inspection only", [], [], new([], [], false), false);
        Assert.Contains("manual review", report);
        Assert.Contains(JsonSerializer.Serialize(archive), report);
        Assert.Contains(JsonSerializer.Serialize(stage), report);
        Assert.Contains(JsonSerializer.Serialize(backup), report);
    }

    [AvaloniaTheory]
    [InlineData(ArchiveContentRestorationPhase.CommitIntent)]
    [InlineData(ArchiveContentRestorationPhase.Committed)]
    [InlineData(ArchiveContentRestorationPhase.NeedsReview)]
    public async Task Restoration_reports_distinguish_a_recorded_commit_from_interruption_and_escape_the_linked_journal(ArchiveContentRestorationPhase phase)
    {
        using var f = await Fixture.Create("content-committed"); var content = Assert.Single(f.Interrupted.ArchiveTransactions.ContentUpdates);
        string linked = Path.Combine(Path.GetDirectoryName(f.Interrupted.JournalPath)!, "restore\ncopy.jsonl");
        var node = new JsonObject
        {
            ["t"] = "archive-content-restoration", ["v"] = 1, ["nonce"] = content.Nonce.ToString("N"), ["sequence"] = phase == ArchiveContentRestorationPhase.CommitIntent ? 1 : 2,
            ["attempt"] = 1, ["phase"] = phase.ToString(), ["content"] = new string('A', 64), ["cleanup"] = new string('B', 64),
            ["restoreNonce"] = Guid.NewGuid().ToString("N"), ["restoreJournal"] = linked, ["transaction"] = Guid.NewGuid().ToString("N"),
            ["before"] = Version(content.PrivateOutput!), ["beforeMetadata"] = content.PrivateMetadataSha256, ["beforeOrigin"] = content.RequiredOriginSha256,
            ["restored"] = Version(new(content.PrivateOutput!.Identity, content.Snapshot!.Length, content.Snapshot.Sha256)),
            ["restoredMetadata"] = content.PrivateMetadataSha256, ["restoredOrigin"] = content.RequiredOriginSha256,
        };
        var job = f.Interrupted with { ArchiveTransactions = f.Interrupted.ArchiveTransactions with { ContentRestorations = [ArchiveContentRestorationFacts.Parse(node)] } };
        string summary = InterruptedRecoveryReport.ArchiveSummary(job);
        Assert.Contains(phase == ArchiveContentRestorationPhase.Committed ? "recorded as restored" : "restoration was interrupted", summary);
        string report = InterruptedRecoveryReport.Format(job, "read-only restoration report", [], [], new([], [], false), false);
        Assert.Contains("Linked fresh restore journal (not inspected)", report); Assert.Contains("restore\\ncopy", report); Assert.DoesNotContain("restore\ncopy", report);
        Assert.Contains("its own rollback snapshot", report); Assert.Contains("does not confirm publication", report);
        static JsonObject Version(ArchiveObjectVersion value) => new() { ["identity"] = value.Identity, ["length"] = value.Length, ["sha256"] = value.Sha256 };
    }
    [AvaloniaTheory]
    [InlineData("reserved")]
    [InlineData("unrecorded-stage")]
    [InlineData("sidecar-reserved")]
    [InlineData("committed-warning")]
    [InlineData("unknown")]
    [InlineData("mixed")]
    [InlineData("legacy")]
    [InlineData("legacy-committed")]
    [InlineData("origin-warning")]
    [InlineData("content-committed")]
    [InlineData("content-intent")]
    [InlineData("content-mixed")]
    public async Task Archive_rows_offer_read_only_reporting_and_keep_all_recorded_files(string state)
    {
        using var f = await Fixture.Create(state);
        f.Vm.Operations.LoadInterrupted([f.Interrupted]);
        f.Vm.Operations.IsOpen = true;
        Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout();
        var row = Assert.Single(f.Vm.Operations.Interrupted);
        Assert.False(row.CanCleanup); Assert.False(row.CanMarkReviewed); Assert.False(row.CanRunAgain); Assert.False(row.CanReviewNewTransfer);
        Assert.Contains(state is "committed-warning" or "origin-warning" or "content-committed" ? "recorded as committed" : state is "unknown" or "legacy" or "legacy-committed" ? "incomplete or unsupported" : "not been confirmed", row.Details);
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
            if (state == "unrecorded-stage")
            {
                Assert.Contains("Stage creation identity was not recorded", report.Text);
                Assert.Contains("Reserved stage byte limit:", report.Text);
                Assert.DoesNotContain("Recorded stage identity: null", report.Text);
            }
            if (state == "sidecar-reserved")
            {
                Assert.Contains("Reserved sidecar ExpectedOutput (not inspected)", report.Text);
                Assert.Contains("Reserved sidecar VerifiedOutput (not inspected)", report.Text);
                Assert.Contains("Sidecar creation identity was not recorded", report.Text);
            }
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
            if (state.StartsWith("content-", StringComparison.Ordinal))
            {
                var content = Assert.Single(f.Interrupted.ArchiveTransactions.ContentUpdates);
                Assert.Contains("Same-object archive update", report.Text);
                Assert.Contains(JsonSerializer.Serialize(content.SnapshotPath), report.Text);
                Assert.Contains("Retained original-byte snapshot (not inspected)", report.Text);
                Assert.Contains("no recorded path grants restore or deletion authority", report.Text);
            }
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
                bool contentUpdate = state.StartsWith("content-", StringComparison.Ordinal);
                for (int i = 0; i < (contentUpdate ? 4 : state is "committed-warning" or "mixed" or "origin-warning" or "legacy-committed" ? phases.Length : 1); i++)
                    writer.ArchiveTransaction(new(nonce, i + 1, profile, contentUpdate && i == 3 ? ArchiveTransactionPhase.Verifying : phases[i], archive, "parent-object", original, stagePath, state == "unrecorded-stage" ? null : stage.Identity,
                        i >= 3 ? stage : null, i >= 3 ? retainedPath : null, i >= 3 ? manifestPath : null, i >= 3 ? manifest : null,
                        i >= 3 ? new string('E', 64) : null, i >= 5 ? stage : null, i >= 5 ? original : null,
                        i == 7 && state != "legacy-committed" ? [state == "origin-warning" ? originPath : retainedPath] : [],
                        originPath, origin.Identity, 1, i >= 3 ? origin : null,
                        sidecars: state == "sidecar-reserved" ?
                        [new(ArchiveSidecarRole.ExpectedOutput, manifestPath, 4096),
                         new(ArchiveSidecarRole.OriginEvidence, originPath, 4096, origin.Identity, originSources: 1),
                         new(ArchiveSidecarRole.VerifiedOutput, Path.Combine(root, ".owned-observed-manifest"), 4096)] : null));
                if (contentUpdate)
                {
                    var baseline = new ArchiveFileObservation(original, "parent-object", Path.GetFileName(archive), new string('E', 64),
                        ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier), null, null);
                    var reservation = new ArchiveArtifactReservation(new object(), nonce, ArchiveArtifactKind.Stage, root, "parent-object", original.Length);
                    File.WriteAllText(reservation.Path, "owned content snapshot");
                    var content = ArchiveContentUpdateFacts.Reserve(writer.CurrentArchiveTransaction(nonce), reservation, baseline, baseline.Origin);
                    writer.ArchiveContentUpdate(content);
                    Add(content.Advance(ArchiveContentUpdatePhase.SnapshotCreated, snapshotIdentity: "content-snapshot", creationPolicy: new string('E', 64)));
                    Add(content.Advance(ArchiveContentUpdatePhase.TransactionOpened, transactionId: Guid.NewGuid()));
                    Add(content.Advance(ArchiveContentUpdatePhase.SnapshotSealed, snapshot: new("content-snapshot", original.Length, original.Sha256)));
                    Add(content.Advance(ArchiveContentUpdatePhase.Ready, privateOutput: new(new(original.Identity, stage.Length, stage.Sha256), "parent-object",
                        baseline.LeafName, new string('F', 64), baseline.Origin, null, null)));
                    Add(content.Advance(ArchiveContentUpdatePhase.CommitIntent));
                    if (state != "content-intent") Add(content.Advance(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, published: content.PrivateOutput));
                    void Add(ArchiveContentUpdateFacts next) { writer.ArchiveContentUpdate(next); content = next; }
                }
                if (state == "legacy-committed") writer.Finish(JobState.Completed, "historical completed archive");
                if (state is "mixed" or "content-mixed") writer.ArchiveTransaction(new(Guid.NewGuid(), 1, profile, ArchiveTransactionPhase.Reserved,
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
                        foreach (string key in new[] { "originPath", "originIdentity", "originSources", "originEvidence", "stageMaximum", "sidecars", "finalized" }) record.Remove(key);
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
            foreach (var content in Interrupted.ArchiveTransactions.ContentUpdates)
                Assert.Equal("owned content snapshot", File.ReadAllText(content.SnapshotPath));
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
