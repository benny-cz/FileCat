using System.Security.AccessControl;
using System.Security.Principal;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class StorageHistoryStartupUiTests
{
    [AvaloniaFact]
    public async Task Private_Windows_history_is_acknowledged_through_UI_and_retained_summary_survives_restart_without_its_journal()
    {
        Assert.SkipWhen(!FileCat.Platform.Windows.WindowsStorageTransforms.IsQualifiedHost, "Qualified Windows 11 x64 client private storage evidence and normal-user ACL fixture.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity, "Run this fixture in the disposable normal-user Windows test process.");
        string state = Directory.CreateTempSubdirectory("filecat-storage-startup-").FullName;
        var paths = AppPaths.Resolve(overrideRoot: state); string journalDirectory = Directory.CreateDirectory(paths.JournalDirectory).FullName;
        using var identity = WindowsIdentity.GetCurrent(); string sid = identity.User!.Value;
        var directorySecurity = new DirectorySecurity(); directorySecurity.SetSecurityDescriptorSddlForm($"O:{sid}D:P(A;OICI;FA;;;{sid})(A;OICI;FA;;;SY)", AccessControlSections.Owner | AccessControlSections.Access);
        new DirectoryInfo(journalDirectory).SetAccessControl(directorySecurity);
        Guid job = Guid.NewGuid(); string leaf = StorageJournalPolicy.FileName(DateTime.UtcNow, job), path = Path.Join(journalDirectory, leaf);
        var before = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(7, "historical-object"), false, null);
        var header = new StorageJournalHeader(job, DateTime.UtcNow, StorageAction.Compress, StorageScope.SelectedItems, Guid.NewGuid(), [new(0, 0, @"C:\historical\item", before)]);
        StorageJournalPrefix prefix;
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
        using (var writer = new StorageTransformJournalWriter(stream, () => stream.Flush(true), header))
        {
            for (int i = 1; i <= 70; i++)
            {
                var call = writer.FlushIntent(0, @"C:\historical\item", before);
                writer.FlushNotAttempted(call, StorageNoCallReason.Paused);
            }
            writer.FlushIntent(0, @"C:\historical\item", before);
            prefix = Assert.IsType<StorageJournalPrefix>(writer.Prefix);
        }
        var fileSecurity = new FileSecurity(); fileSecurity.SetSecurityDescriptorSddlForm($"O:{sid}D:P(A;;FA;;;{sid})(A;;FA;;;SY)", AccessControlSections.Owner | AccessControlSections.Access);
        new FileInfo(path).SetAccessControl(fileSecurity);
        try
        {
            var first = AccessibilityTests.OpenMainWindow(width: 640, height: 420, statePaths: paths);
            ReportWindow? report = null;
            ReportWindow? calls = null;
            ReportWindow? retainedReport = null;
            try
            {
                await Wait(() => !first.Vm.Operations.IsStorageHistoryLoading && first.Vm.Operations.Interrupted.Any(r => r.IsStorage));
                await Wait(() => first.Vm.Operations.StorageAccounting is { Complete: true, PendingHistories: 1 });
                Assert.Equal(2L * 1024 * 1024, first.Vm.Operations.StorageAccounting!.ReservedReviewBytes);
                var pending = Assert.Single(first.Vm.Operations.Interrupted, r => r.IsStorage);
                Assert.Equal(leaf, pending.Job.StorageJournalLeaf); Assert.Equal(leaf, pending.Job.JournalPath);
                report = Assert.IsType<ReportWindow>(await first.Vm.ShowInterruptedReportAsync(pending.Job));
                await Wait(() => report.Reading is not null); await report.Reading!;
                Assert.Contains("no matching durable result", report.Text);
                Assert.Single(report.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "View recorded calls…");
                calls = Assert.IsType<ReportWindow>(first.Vm.ShowStorageCallEvidence("Inactive history", leaf, prefix, activeAtCapture: false));
                var next = calls.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Next recorded call page");
                await Wait(() => calls.Reading?.IsCompleted == true && calls.Text.Contains("Displayed calls 1–64", StringComparison.Ordinal) && next.IsEnabled);
                Assert.True(next.IsEnabled); next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(() => calls.Reading?.IsCompleted == true && calls.Text.Contains("Displayed calls 65–71", StringComparison.Ordinal) && !next.IsEnabled);
                Assert.False(next.IsEnabled); Assert.DoesNotContain("Call 1;", calls.Text);
                Assert.Contains("No result is recorded at this prefix", calls.Text); Assert.DoesNotContain("Pending at this active snapshot", calls.Text);
                calls.Close();
                report.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Acknowledge reviewed history…")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(() => first.Vm.Dialogs.IsOpen);
                first.Window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Acknowledge reviewed history")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Wait(() => first.Vm.Operations.Interrupted.All(r => !r.IsStorage) && first.Vm.Operations.ReviewedRecovery.Any(r => r.IsStorage && r.StorageVerified));
                var retained = Assert.Single(first.Vm.Operations.ReviewedRecovery, r => r.IsStorage);
                Assert.Empty(retained.Report); Assert.True(retained.StorageEffectUncertain); Assert.NotNull(retained.StorageRetainedLeaf);
                Assert.False(await Task.Run(() => first.Services.Jobs.IsInterrupted(pending.Job, TestContext.Current.CancellationToken)));
                await Wait(() => first.Vm.Operations.StorageAccounting is { Complete: true, PendingHistories: 0 } && !first.Vm.Operations.IsStorageHistoryAccounting);
                Assert.Equal(0, first.Vm.Operations.StorageAccounting!.ReservedReviewBytes);
                retainedReport = Assert.IsType<ReportWindow>(await first.Vm.ShowRetainedStorageHistoryAsync(retained));
                await Wait(() => retainedReport.Reading?.IsCompleted == true);
                Assert.Single(retainedReport.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "View recorded calls…");
            }
            finally
            {
                retainedReport?.Close(); calls?.Close(); report?.Close();
                try
                {
                    // Reading the retained report invalidates the background census. Stop its
                    // replacements before simulating retention on the same journal below.
                    first.Vm.StopStorageHistoryDiscovery();
                    await Wait(() => !first.Vm.Operations.IsStorageHistoryAccounting && !first.Vm.Operations.IsStorageHistoryLoading);
                }
                finally { AccessibilityTests.Close(first.Services, first.Window, first.Root); }
            }

            // Simulate retention having removed this fixture's acknowledged inactive journal. The Core retention
            // deletion-order policy is tested separately; this fixture verifies the App's restart/read path.
            var release = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                try { File.Delete(path); break; }
                catch (IOException error) when ((error.HResult & 0xffff) == 32 && release.Elapsed < TimeSpan.FromSeconds(20))
                {
                    // Window closure requests cancellation; an admitted native reader still owns
                    // its handle until it actually returns. Never force its handle closed.
                    await Task.Delay(10, TestContext.Current.CancellationToken);
                }
            }
            var second = AccessibilityTests.OpenMainWindow(width: 640, height: 420, statePaths: paths);
            ReportWindow? restored = null;
            try
            {
                await Wait(() => !second.Vm.Operations.IsStorageHistoryLoading && second.Vm.Operations.ReviewedRecovery.Any(r => r.IsStorage));
                await Wait(() => second.Vm.Operations.StorageAccounting is { Complete: true, Journals: 0, Artifacts: 2 });
                var row = Assert.Single(second.Vm.Operations.ReviewedRecovery, r => r.IsStorage);
                Assert.False(row.StorageVerified); Assert.Empty(row.Report);
                restored = Assert.IsType<ReportWindow>(await second.Vm.ShowRetainedStorageHistoryAsync(row));
                await Wait(() => restored.Reading is not null); await restored.Reading!;
                Assert.Contains("Reviewed; effect remains uncertain", restored.Text);
                Assert.Contains("Detailed journal evidence is no longer present", restored.Text);
                Assert.Contains("historical-object", restored.Text); Assert.DoesNotContain(second.Vm.Operations.Interrupted, r => r.IsStorage);
                Assert.DoesNotContain(restored.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "View recorded calls…");
                Assert.True(Assert.Single(second.Vm.Operations.ReviewedRecovery, r => r.IsStorage).StorageVerified);
            }
            finally { restored?.Close(); AccessibilityTests.Close(second.Services, second.Window, second.Root); }
        }
        finally { Directory.Delete(state, true); }
    }

    private static async Task Wait(Func<bool> ready)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(20));
        while (!ready()) await Task.Delay(10, stop.Token);
    }
}
