using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class ArchiveRetainedCleanupUiTests
{
    [AvaloniaTheory]
    [InlineData(ArchiveWriteFormat.Rar5, false)] [InlineData(ArchiveWriteFormat.Rar5, true)]
    [InlineData(ArchiveWriteFormat.SevenZip, false)] [InlineData(ArchiveWriteFormat.SevenZip, true)]
    public async Task RAR_and_7z_retained_updates_offer_the_same_reviewed_cleanup_journey(ArchiveWriteFormat format, bool approve)
    {
        using var f = await Fixture.Create(approve ? "approve" : "cancel", format);
        Assert.True(Assert.Single(f.Vm.Operations.Interrupted).CanCleanup);
        var operation = f.Vm.CleanupInterruptedAsync(f.Job);
        await Wait(() => f.Button("Keep archive and clean up") is not null);
        f.AssertAllPresent(); f.Click(approve ? "Keep archive and clean up" : "Cancel");
        Assert.Equal(approve, await operation);
        Assert.All(f.Catalog.Artifacts, artifact => Assert.Equal(!approve, File.Exists(artifact.Path)));
        Assert.Equal(f.Published.Version.Sha256, Fixture.Hash(f.Archive));
    }
    [AvaloniaTheory]
    [InlineData(1280, 800)]
    [InlineData(900, 640)]
    [InlineData(640, 420)]
    public async Task Completed_archive_cleanup_report_has_a_visible_action_and_concise_accessible_row(int width, int height)
    {
        using var f = await Fixture.Create("cancel");
        var record = new RecoveryReviewRecord(f.Job.JournalPath, f.Job.Title, f.Job.Kind, DateTime.UtcNow,
            "Archive cleanup completed", "Private full report detail belongs in its viewer, not the row name. " + f.Archive, true);
        f.Vm.Operations.Interrupted.Clear(); f.Vm.Operations.AddReviewedRecovery(record); f.Vm.Operations.UpdateSummary();
        f.Vm.Operations.AnnounceRecovery("Recovery cleanup finished; the report is retained.");
        f.Window.Width = width; f.Window.Height = height;
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout(); await Task.Delay(10); }
        var view = Assert.Single(f.Window.GetVisualDescendants().OfType<OperationsView>());
        var viewport = view.FindControl<ScrollViewer>("RecoveryScroll")!;
        var list = Assert.Single(view.GetVisualDescendants().OfType<ListBox>(), l => AutomationProperties.GetName(l) == "Reviewed recovery reports");
        var item = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>());
        string name = ControlAutomationPeer.CreatePeerForElement(item).GetName();
        var action = Assert.IsType<Button>(f.Button("Open recovery report"));
        double top = action.TranslatePoint(default, viewport)!.Value.Y, bottom = top + action.Bounds.Height;
        Assert.True(name == record.Display && top >= -.5 && bottom <= viewport.Viewport.Height + .5,
            $"Report row name must match its concise display ({name.Length}/{record.Display.Length} characters), with its action visible: {top}..{bottom} in {viewport.Viewport.Height} DIP.");
        f.AssertAllPresent(); Assert.Null(f.Lease);
    }

    [AvaloniaTheory]
    [InlineData(1280, 800)]
    [InlineData(900, 640)]
    [InlineData(640, 420)]
    public async Task Archive_cleanup_review_action_has_a_visible_pointer_target_without_scrolling_empty_job_history(int width, int height)
    {
        using var f = await Fixture.Create("cancel"); f.Window.Width = width; f.Window.Height = height;
        for (int i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout(); await Task.Delay(10); }
        var view = Assert.Single(f.Window.GetVisualDescendants().OfType<OperationsView>());
        var viewport = view.FindControl<ScrollViewer>("RecoveryScroll")!;
        var action = Assert.IsType<Button>(f.Button("Review retained files…"));
        double top = action.TranslatePoint(default, viewport)!.Value.Y;
        double bottom = top + action.Bounds.Height;
        Assert.True(top >= -.5 && bottom <= viewport.Viewport.Height + .5,
            $"Archive cleanup must be visible without scrolling: {top}..{bottom} in {viewport.Viewport.Height} DIP.");
        f.AssertAllPresent(); Assert.Null(f.Lease);
    }

    [AvaloniaTheory]
    [InlineData("cancel")]
    [InlineData("approve")]
    [InlineData("changed-target")]
    [InlineData("stop-after-first")]
    [InlineData("fail-after-unlink")]
    [InlineData("cancel-acquisition")]
    public async Task Archive_cleanup_approval_uses_a_retained_scope_and_keeps_honest_outcomes_when_interrupted(string mode)
    {
        using var f = await Fixture.Create(mode); byte[] before = Fixture.Bytes(f.Job.JournalPath);
        var row = Assert.Single(f.Vm.Operations.Interrupted); Assert.True(row.CanCleanup); Assert.Equal("Review retained files…", row.CleanupLabel);
        Assert.Contains("Review retained files", row.Details);
        Task<bool> operation = f.Vm.CleanupInterruptedAsync(f.Job);
        if (mode == "cancel-acquisition")
        {
            await Wait(() => f.Acquired); f.Vm.StopInterruptedReview(f.Job); f.Release.Set();
            Assert.False(await operation); Assert.True(f.Lease!.Disposed); Assert.False(f.Dialogs.IsOpen);
            Assert.Equal(before, Fixture.Bytes(f.Job.JournalPath)); f.AssertAllPresent(); return;
        }
        await Wait(() => f.Button("Keep archive and clean up") is not null);
        Dispatcher.UIThread.RunJobs(); f.Window.UpdateLayout();
        Assert.Contains("backup from before this operation", string.Join(" ", f.Window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text)));
        Assert.Equal("Cancel", Assert.IsType<Button>(f.Window.FocusManager!.GetFocusedElement()).Content);
        Assert.Equal(before, Fixture.Bytes(f.Job.JournalPath)); f.AssertAllPresent();
        if (mode == "changed-target") File.WriteAllText(f.Archive, "later user contents");
        f.Click(mode == "cancel" ? "Cancel" : "Keep archive and clean up");
        if (mode == "stop-after-first")
        {
            await Wait(() => f.Unlinked); f.Vm.StopInterruptedReview(f.Job); f.Release.Set();
        }
        bool result = await operation;
        Assert.True(f.Lease!.Disposed); Assert.Equal(mode == "approve", result);
        if (mode != "changed-target") Assert.Equal(f.Published.Version.Sha256, Fixture.Hash(f.Archive));
        if (mode == "cancel")
        { Assert.Equal(before, Fixture.Bytes(f.Job.JournalPath)); f.AssertAllPresent(); Assert.Empty(RecoveryReviewStore.Load(f.Services.Paths.JournalDirectory)); return; }
        var record = Assert.Single(RecoveryReviewStore.Load(f.Services.Paths.JournalDirectory));
        Assert.Equal(result, record.ClosureConfirmed);
        Assert.Equal(result, JobJournal.ReadAll(f.Job.JournalPath).Any(record => record.Type == "end"));
        Assert.Contains(JsonSerializer.Serialize(f.Archive), record.Report);
        Assert.DoesNotContain("later user contents", record.Report);
        var recovery = JournalRecovery.ReadArchiveTransactions(f.Job.JournalPath);
        if (mode == "approve")
        {
            Assert.All(f.Catalog.Artifacts, item => Assert.False(File.Exists(item.Path)));
            Assert.Equal(6, f.Lease.Deleted); Assert.True(Assert.Single(recovery.ContentRecoveries).IsResolved);
            Assert.Contains("Recovery tracking closure confirmed", record.Report);
            Assert.DoesNotContain("has not yet been confirmed", record.Report); return;
        }
        if (mode == "changed-target") { Assert.Equal("later user contents", File.ReadAllText(f.Archive)); f.AssertAllPresent(); return; }
        Assert.False(File.Exists(f.Catalog.Artifacts[0].Path));
        Assert.All(f.Catalog.Artifacts.Skip(1), item => Assert.True(File.Exists(item.Path)));
        var facts = Assert.Single(recovery.ContentRecoveries); Assert.Equal(ArchiveContentRecoveryPhase.NeedsReview, facts.Phase);
        Assert.Equal(mode == "stop-after-first" ? 1u : 0u, facts.DeletedMask);
        Assert.Contains(mode == "stop-after-first" ? "Recorded deletion" : "Unresolved; fresh review required", record.Report);
        // A second explicit approval freshly observes any unrecorded absence, then cleans only remaining files.
        f.Mode = "approve"; operation = f.Vm.CleanupInterruptedAsync(f.Job);
        await Wait(() => f.Button("Keep archive and clean up") is not null); f.Click("Keep archive and clean up"); Assert.True(await operation);
        facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(f.Job.JournalPath).ContentRecoveries);
        Assert.True(facts.IsResolved); Assert.Equal(mode == "fail-after-unlink" ? 1u : 0u, facts.AbsentMask);
        Assert.All(f.Catalog.Artifacts, item => Assert.False(File.Exists(item.Path)));
        Assert.Equal(f.Published.Version.Sha256, Fixture.Hash(f.Archive));
    }

    [AvaloniaFact]
    public async Task Archive_cleanup_is_unavailable_without_a_native_adapter_and_cannot_be_forced_through_a_stale_row()
    {
        using var f = await Fixture.Create("approve"); f.Services.ArchiveCleanupLease = null;
        f.Vm.Operations.LoadInterrupted([f.Job]); Assert.False(Assert.Single(f.Vm.Operations.Interrupted).CanCleanup);
        Assert.False(await f.Vm.CleanupInterruptedAsync(f.Job)); Assert.Null(f.Lease); f.AssertAllPresent(); Assert.False(f.Dialogs.IsOpen);
    }

    private static async Task Wait(Func<bool> predicate)
    {
        var timer = Stopwatch.StartNew();
        while (!predicate())
        { Dispatcher.UIThread.RunJobs(); if (timer.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Archive cleanup UI did not reach its checkpoint."); await Task.Delay(10); }
    }

    // This portable UI fixture supplies deterministic native observations. Real NTFS authority is tested separately.
    private sealed class Fixture : IDisposable
    {
        public required AppServices Services; public required MainViewModel Vm; public required MainWindow Window;
        public required string Root, Archive, Mode; public required InterruptedJob Job;
        public required ArchiveTransactionFacts Preparation; public required ArchiveContentUpdateFacts Content;
        public required ArchiveContentCleanupFacts Catalog; public required ArchiveFileObservation Published;
        public FakeLease? Lease; public volatile bool Acquired, Unlinked;
        public readonly ManualResetEventSlim Release = new(false);
        public OverlayDialogService Dialogs => (OverlayDialogService)Vm.Dialogs;
        public Button? Button(string content) => Window.GetVisualDescendants().OfType<Button>().FirstOrDefault(button => button.IsVisible && button.Content as string == content);
        public void Click(string content) => Assert.IsType<Button>(Button(content)).RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        public void AssertAllPresent() => Assert.All(Catalog.Artifacts, item => Assert.True(File.Exists(item.Path)));
        public static string Hash(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); return Convert.ToHexString(SHA256.HashData(stream)); }
        public static byte[] Bytes(string path) { using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); }
        public static async Task<Fixture> Create(string mode, ArchiveWriteFormat format = ArchiveWriteFormat.Tar)
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
            try
            {
                if (typeof(MainWindow).GetField("_interruptedStartup", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(window) is Task startup) await startup;
                var absent = ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier); Guid nonce = Guid.NewGuid();
                var profile = format switch { ArchiveWriteFormat.Rar5 => ArchiveWriteProfile.ForRar5(),
                    ArchiveWriteFormat.SevenZip => ArchiveWriteProfile.ForSevenZip(), _ => new ArchiveWriteProfile(format) };
                string archive = Path.Combine(root, "archive.tar"), stage = Path.Combine(root, ".stage"), snapshot = Path.Combine(root, ".snapshot");
                File.WriteAllBytes(archive, Enumerable.Repeat((byte)'a', 1024).ToArray()); File.Copy(archive, snapshot);
                File.WriteAllBytes(stage, Enumerable.Repeat((byte)'b', 2048).ToArray());
                var original = new ArchiveFileObservation(new("archive", 1024, Hash(archive)), "parent", "archive.tar", new string('C', 64), absent, null, null);
                var saved = new ArchiveFileObservation(new("snapshot", 1024, Hash(snapshot)), "parent", Path.GetFileName(snapshot), new string('F', 64), absent, null, null);
                var stageVersion = new ArchiveObjectVersion("stage", 2048, Hash(stage));
                var sidecars = new[] { ArchiveSidecarRole.ExpectedOutput, ArchiveSidecarRole.OriginEvidence, ArchiveSidecarRole.VerifiedOutput, ArchiveSidecarRole.AssessedSource }.Select(role =>
                {
                    string path = Path.Combine(root, "." + role); File.WriteAllBytes(path, new byte[512]);
                    return new ArchiveSidecarFacts(role, path, 512, role.ToString(), new(role.ToString(), 512, Hash(path)), role == ArchiveSidecarRole.OriginEvidence ? 1 : null);
                }).ToArray();
                ArchiveTransactionFacts Prep(int sequence, ArchiveTransactionPhase phase) => new(nonce, sequence, profile, phase,
                    archive, "parent", original.Version, stage, stageVersion.Identity, sequence == 5 ? stageVersion : null,
                    manifestPath: sidecars[0].Path, manifest: sidecars[0].Version, originEvidencePath: sidecars[1].Path, originEvidenceIdentity: sidecars[1].Identity,
                    originEvidenceSources: 1, originEvidence: sidecars[1].Version, stageMaximumLength: 2048, sidecars: sidecars, finalizedStage: sequence >= 4 ? stageVersion : null);
                using var writer = JobJournal.Create(services.Paths.JournalDirectory, new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
                    Archive = new(archive, null, [], Profile: profile) }, "retained cleanup UI fixture", "owned", [], []));
                foreach (var (sequence, phase) in new[] { (1, ArchiveTransactionPhase.Reserved), (2, ArchiveTransactionPhase.Rebuilding), (3, ArchiveTransactionPhase.Finalizing), (4, ArchiveTransactionPhase.Verifying), (5, ArchiveTransactionPhase.Verifying) }) writer.ArchiveTransaction(Prep(sequence, phase));
                var preparation = Prep(5, ArchiveTransactionPhase.Verifying);
                var reservation = new ArchiveArtifactReservation(new object(), nonce, ArchiveArtifactKind.Stage, root, "parent", 1024);
                // The exact reserved filename is part of the durable catalog.
                File.Move(snapshot, reservation.Path); saved = new(saved.Version, saved.ParentIdentity,
                    Path.GetFileName(reservation.Path), saved.MetadataDigest, saved.Origin, null, null);
                var content = ArchiveContentUpdateFacts.Reserve(preparation, reservation, original, absent); writer.ArchiveContentUpdate(content);
                writer.ArchiveContentUpdate(content = content.Advance(ArchiveContentUpdatePhase.SnapshotCreated, snapshotIdentity: "snapshot", creationPolicy: new string('E', 64)));
                writer.ArchiveContentUpdate(content = content.Advance(ArchiveContentUpdatePhase.TransactionOpened, transactionId: Guid.NewGuid()));
                writer.ArchiveContentUpdate(content = content.Advance(ArchiveContentUpdatePhase.SnapshotSealed, snapshot: saved.Version));
                var rows = new List<ArchiveContentCleanupArtifact> { new(stage, ArchiveArtifactKind.Stage, stageVersion, new string('F', 64), absent.Digest) };
                rows.AddRange(sidecars.Select(sidecar => new ArchiveContentCleanupArtifact(sidecar.Path, sidecar.Role == ArchiveSidecarRole.OriginEvidence ? ArchiveArtifactKind.Provenance : ArchiveArtifactKind.Manifest,
                    sidecar.Version!, new string('F', 64), absent.Digest))); rows.Add(ArchiveContentCleanupArtifact.Capture(reservation.Path, ArchiveArtifactKind.Stage, saved));
                var catalog = ArchiveContentCleanupFacts.Reserve(preparation, content, rows); writer.ArchiveContentCleanup(catalog);
                var published = new ArchiveFileObservation(new("archive", stageVersion.Length, stageVersion.Sha256), "parent", "archive.tar", new string('F', 64), absent, null, null);
                writer.ArchiveContentUpdate(content = content.Advance(ArchiveContentUpdatePhase.Ready, privateOutput: published));
                writer.ArchiveContentUpdate(content = content.Advance(ArchiveContentUpdatePhase.CommitIntent));
                writer.ArchiveContentUpdate(content = content.Advance(ArchiveContentUpdatePhase.CommittedWithRetainedArtifacts, published: published.Version));
                string journalPath = writer.Path; writer.Dispose(); File.WriteAllBytes(archive, File.ReadAllBytes(stage));
                var job = Assert.Single(JournalRecovery.Scan(services.Paths.JournalDirectory));
                var fixture = new Fixture { Services = services, Vm = vm, Window = window, Root = root, Archive = archive, Mode = mode, Job = job,
                    Preparation = preparation, Content = content, Catalog = catalog, Published = published };
                services.ArchiveCleanupLease = (_, _, _) =>
                {
                    fixture.Lease = new(fixture); fixture.Acquired = true;
                    if (fixture.Mode == "cancel-acquisition" && !fixture.Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException();
                    return fixture.Lease;
                };
                vm.Operations.LoadInterrupted([job]); vm.Operations.IsOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return fixture;
            }
            catch { AccessibilityTests.Close(services, window, root); throw; }
        }
        public void Dispose() { Release.Set(); Lease?.Dispose(); AccessibilityTests.Close(Services, Window, Root); Release.Dispose(); }
    }
    private sealed class FakeLease : IArchiveContentCleanupRecoveryLease
    {
        private readonly Fixture f;
        public bool Disposed; public int Deleted;
        public JobJournal Journal { get; }
        public ArchiveContentCleanupFacts Catalog => f.Catalog;
        public ArchiveContentRecoveryFacts Candidate { get; }
        public FakeLease(Fixture f)
        {
            this.f = f; Journal = JobJournal.ReopenArchiveRecovery(f.Job.JournalPath, new FileStream(f.Job.JournalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read), Fixture.Hash(f.Job.JournalPath), () => { }, CancellationToken.None);
            Candidate = ArchiveContentRecoveryFacts.Begin(f.Preparation, f.Content, f.Catalog, f.Published);
        }
        public bool HasArtifact(int index) => File.Exists(Catalog.Artifacts[index].Path);
        public void RequireArtifactAbsent(int index) { if (HasArtifact(index)) throw new IOException("A different file occupies the retained name."); }
        public void ReviewTarget(CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (Fixture.Hash(f.Archive) != f.Published.Version.Sha256) throw new IOException("The archive changed after review."); }
        public void ReviewArtifact(int index, CancellationToken ct) { ct.ThrowIfCancellationRequested(); var row = Catalog.Artifacts[index]; if (Fixture.Hash(row.Path) != row.Version.Sha256) throw new IOException("A retained file changed after review."); }
        public void DeleteArtifact(int index, CancellationToken ct)
        {
            ReviewArtifact(index, ct); File.Delete(Catalog.Artifacts[index].Path); Deleted++;
            if (index == 0 && f.Mode == "fail-after-unlink") throw new IOException("Injected failure after unlink.");
            if (index == 0 && f.Mode == "stop-after-first") { f.Unlinked = true; if (!f.Release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); }
        }
        public void Dispose() { if (Disposed) return; Disposed = true; Journal.Dispose(); }
    }
}
