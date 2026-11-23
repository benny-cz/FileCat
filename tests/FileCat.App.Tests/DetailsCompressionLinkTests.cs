using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Records;
using Adapter = FileCat.App.Tests.StorageCommandIntegrationTests.Adapter;

namespace FileCat.App.Tests;

/// <summary>
/// I-DETAILS-C: the File-system details window's "NTFS compression for this item…" link. It reviews exactly the item the
/// report was opened for, in the main window, reading it afresh; never a panel's selection, marks or the Find it came from.
/// Until the review is actionable it belongs to the report: closing the report, canceling the review or shutting down retires
/// it. From then on it belongs to the main window, and closing the report changes nothing. Its job has no listing origin, so
/// completion clears no marks; it refreshes the affected folder as every job does. Without compression the link is absent and
/// the details still read.
/// </summary>
public sealed class DetailsCompressionLinkTests
{
    private const string WindowsOnly = "The review admits only local Windows paths, as compression does.";

    private sealed class Records : IFileRecords
    {
        public bool IsSupported => true;
        public string FileSystem = "NTFS";
        public FileObjectKind? Kind;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader invoked.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Full reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct) => new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = Kind ?? (Directory.Exists(path) ? FileObjectKind.Directory : FileObjectKind.File),
            Identity = ObservedFact<DetailsIdentity>.Known(new("fixture-volume", "fixture-id"), FactScope.Entry, "fixture"),
            FileSystem = ObservedFact<string>.Known(FileSystem, FactScope.Volume, "fixture"),
        };
    }

    private static async Task Until(Func<bool> condition, Func<string>? state = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Timed out" + (state is null ? "." : ": " + state()));
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task Settle(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<ReportWindow> OpenDetails(MainViewModel vm, List<ReportWindow> opened, string subject)
    {
        int before = opened.Count;
        Assert.True(vm.ShowFileRecordForPath(subject));
        await Until(() => opened.Count == before + 1 && opened[^1].Text.Contains(Path.GetFileName(subject), StringComparison.Ordinal));
        return opened[^1];
    }

    private static List<Job> Watch(JobManager jobs)
    {
        var added = new List<Job>();
        jobs.JobAdded += job => { if (job.Kind == JobKind.StorageTransform) lock (added) added.Add(job); };
        return added;
    }

    private static async Task ChooseCompress(Avalonia.Visual host)
    {
        await Until(() => host.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Compress"));
        host.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == "Compress").IsChecked = true;
        await Until(() => StorageCommandIntegrationTests.Primary(host, "Compress").IsEnabled);
    }

    [AvaloniaFact]
    public async Task The_link_reviews_exactly_the_captured_item_in_the_main_window_and_clears_no_marks()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        var adapter = new Adapter { Apply = Adapter.Changed };
        var (services, vm, window, root, state) = StorageCommandIntegrationTests.OpenForJobs(adapter);
        vm.FileRecordsForTests = new Records();
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        var jobs = Watch(services.Jobs);
        Task? reviewing = null;
        try
        {
            var tab = vm.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FindStoreIndex("a.txt") >= 0);
            string subject = Path.Join(root, "files", "a.txt");
            var report = await OpenDetails(vm, opened, subject);
            Assert.True(report.CompressionLinkForTest.IsVisible);
            Assert.True(report.CompressionLinkForTest.IsEnabled);

            // The panel moves on and marks both files: nothing the link reads.
            tab.Listing.MarkNames(["a.txt", "b.txt"], true);
            long marks = tab.Listing.MarkRevision;
            reviewing = report.OpenCompressionReviewForTestAsync();
            await Until(() => vm.Dialogs.IsOpen);
            // One review at a time, and the report says where it is.
            Assert.False(report.CompressionLinkForTest.IsEnabled);
            Assert.Equal("A compression review for this item is open in the main window.", ToolTip.GetTip(report.CompressionLinkForTest));
            await report.OpenCompressionReviewForTestAsync();
            Assert.Single(window.GetVisualDescendants().OfType<Expander>(), item => item.Header as string == "Item and its location");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("The item File-system details was opened for, and nothing else", StringComparison.Ordinal) == true);

            await StorageCommandIntegrationTests.ApproveCompression(window);
            await reviewing;
            var job = Assert.Single(jobs);
            Assert.Equal([subject], job.Request.Sources.Select(source => source.FileSystemPath));
            Assert.Equal([subject], adapter.Pins.Select(pin => pin.Target.Path).Distinct());
            Assert.Equal(0, vm.StorageOriginCount); // no listing origin: nothing to unmark
            int generation = tab.Listing.Generation;
            await Until(() => job.State.IsFinished() && tab.Listing.Generation != generation && !tab.Listing.IsRefreshing);
            Assert.True(job.State == JobState.Completed, $"{job.State}: {JobIssue.Summarize(job.Issues)}");
            Assert.Equal(1, adapter.Calls);
            // The affected folder was refreshed; its marks were not cleared.
            await Settle(TimeSpan.FromMilliseconds(200));
            Assert.Equal(2, tab.Listing.MarkedCount);
            Assert.Equal(marks, tab.Listing.MarkRevision);
            Assert.True(report.CompressionLinkForTest.IsEnabled);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (reviewing is not null) await reviewing;
            foreach (var report in opened) report.Close();
            StorageCommandIntegrationTests.CloseForJobs(services, window, root, state);
        }
    }

    [AvaloniaTheory]
    [InlineData("report")]
    [InlineData("shutdown")]
    public async Task Before_the_review_is_actionable_closing_the_report_or_shutting_down_retires_it(string retire)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var adapter = new Adapter { Blocking = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); } };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        vm.FileRecordsForTests = new Records();
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        Task? reviewing = null;
        try
        {
            var report = await OpenDetails(vm, opened, Path.Join(root, "files", "a.txt"));
            reviewing = report.OpenCompressionReviewForTestAsync();
            await Until(() => entered.IsSet && vm.Dialogs.IsOpen);

            if (retire == "report") report.Close(); else services.Io.Dispose();
            await Until(() => !vm.Dialogs.IsOpen);
            Assert.Empty(adapter.Pins); // the started inspection still owns its result
            release.Set();
            await reviewing;
            await Until(() => adapter.Pins.Count == 1 && adapter.Pins.All(pin => pin.Disposals == 1));
            await Settle(TimeSpan.FromMilliseconds(200));
            Assert.Empty(services.Jobs.Jobs);
            Assert.DoesNotContain(vm.RecentNotifications, item => item.IsError);
        }
        finally
        {
            release.Set(); ((OverlayDialogService)vm.Dialogs).CancelAll(); if (reviewing is not null) await reviewing;
            foreach (var report in opened) report.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task After_the_review_is_actionable_closing_the_report_leaves_it_to_the_main_window()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        var adapter = new Adapter { Apply = Adapter.Changed };
        var (services, vm, window, root, state) = StorageCommandIntegrationTests.OpenForJobs(adapter);
        vm.FileRecordsForTests = new Records();
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        var jobs = Watch(services.Jobs);
        Task? reviewing = null;
        try
        {
            string subject = Path.Join(root, "files", "b.txt");
            var report = await OpenDetails(vm, opened, subject);
            reviewing = report.OpenCompressionReviewForTestAsync();
            await ChooseCompress(window);

            report.Close();
            await Settle(TimeSpan.FromMilliseconds(250));
            Assert.True(vm.Dialogs.IsOpen);
            Assert.True(StorageCommandIntegrationTests.Primary(window, "Compress").IsEnabled);
            StorageCommandIntegrationTests.Primary(window, "Compress").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await reviewing;
            var job = Assert.Single(jobs);
            Assert.Equal([subject], job.Request.Sources.Select(source => source.FileSystemPath));
            await Until(() => job.State.IsFinished());
            Assert.True(job.State == JobState.Completed, $"{job.State}: {JobIssue.Summarize(job.Issues)}");
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (reviewing is not null) await reviewing;
            foreach (var report in opened) report.Close();
            StorageCommandIntegrationTests.CloseForJobs(services, window, root, state);
        }
    }

    [AvaloniaFact]
    public async Task Canceling_the_review_submits_nothing_and_the_link_opens_again()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        vm.FileRecordsForTests = new Records();
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        Task? reviewing = null;
        try
        {
            var report = await OpenDetails(vm, opened, Path.Join(root, "files", "a.txt"));
            reviewing = report.OpenCompressionReviewForTestAsync();
            await ChooseCompress(window);
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await reviewing;
            await Until(() => adapter.Pins.Count == 1 && adapter.Pins.All(pin => pin.Disposals == 1));
            Assert.Empty(services.Jobs.Jobs);
            Assert.True(report.CompressionLinkForTest.IsEnabled);
            Assert.DoesNotContain(vm.RecentNotifications, item => item.IsError);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (reviewing is not null) await reviewing;
            foreach (var report in opened) report.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// A folder's review offers this folder only, or with its existing contents, and shows both choices as text: a dialog
    /// once showed a choice whose label is a control as that control's type name ("Avalonia.Controls.TextBlock").
    /// </summary>
    [AvaloniaFact]
    public async Task A_folder_review_offers_this_folder_with_or_without_its_contents_as_text()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        vm.FileRecordsForTests = new Records();
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        Task? reviewing = null;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "files", "docs")).FullName;
            var report = await OpenDetails(vm, opened, folder);
            reviewing = report.OpenCompressionReviewForTestAsync();
            await Until(() => window.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "This folder only"));
            foreach (string label in new[] { "This folder only", "This folder and its existing contents" })
            {
                var choice = window.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == label);
                Assert.Contains(choice.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == label);
            }
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.StartsWith("Avalonia.Controls.", StringComparison.Ordinal) == true);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<RadioButton>(), button => AutomationProperties.GetName(button) == "Selected items only");
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await reviewing;
            Assert.Empty(services.Jobs.Jobs);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (reviewing is not null) await reviewing;
            foreach (var report in opened) report.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// Opened from Find, the report outlives it: the link reviews the item it was opened for, in the main window, after Find
    /// has closed, with no dependence on Find's results or marks.
    /// </summary>
    [AvaloniaFact]
    public async Task A_report_opened_from_Find_reviews_its_item_after_Find_has_closed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), WindowsOnly);
        var adapter = new Adapter { Apply = Adapter.Changed };
        var records = new Records();
        // The Find command checks the platform's own reader, so the platform carries the fake too.
        var previous = Core.Platform.PlatformFactory.WindowsFactory;
        (AppServices Services, MainViewModel Vm, MainWindow Window, string Root, string State) fixture;
        try
        {
            Core.Platform.PlatformFactory.WindowsFactory = () => new FileDetailsWindowTests.AdapterPlatform(records, null);
            fixture = StorageCommandIntegrationTests.OpenForJobs(adapter);
        }
        finally { Core.Platform.PlatformFactory.WindowsFactory = previous; }
        var (services, vm, window, root, state) = fixture;
        vm.FileRecordsForTests = records;
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        var jobs = Watch(services.Jobs);
        Task? reviewing = null;
        FindWindow? find = null;
        try
        {
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            var main = vm.ActiveTab!; long mainMarks = main.Listing.MarkRevision;
            find = await StorageCommandIntegrationTests.OpenSettledFind(vm);
            var results = find.ResultsTab!.Listing;
            Assert.True(results.FocusName("a.txt"));
            find.Run(CommandIds.FileRecord);
            await Until(() => opened.Count == 1 && opened[0].CompressionLinkForTest.IsVisible, () =>
                $"{opened.Count} reports; link visible {opened.FirstOrDefault()?.CompressionLinkForTest.IsVisible}; " +
                $"availability {find.StorageCommandAvailability.Reason}; focused {find.ResultsTab!.Listing.FocusedStoreIndex}; " +
                $"text {opened.FirstOrDefault()?.Text?[..Math.Min(300, opened[0].Text.Length)]}; " +
                $"notes {string.Join(" | ", vm.RecentNotifications.Select(note => note.Message))}");
            var report = opened[0];
            results.MarkNames(["a.txt", "b.txt"], true);
            find.Close();
            await Until(() => FindWindow.OpenWindows.Count == 0);

            reviewing = report.OpenCompressionReviewForTestAsync();
            await StorageCommandIntegrationTests.ApproveCompression(window);
            await reviewing;
            var job = Assert.Single(jobs);
            // Find spells the path as the disk does.
            Assert.Equal(Path.Join(root, "files", "a.txt"), Assert.Single(job.Request.Sources).FileSystemPath, ignoreCase: true);
            await Until(() => job.State.IsFinished());
            Assert.True(job.State == JobState.Completed, $"{job.State}: {JobIssue.Summarize(job.Issues)}");
            Assert.Equal(mainMarks, main.Listing.MarkRevision);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (reviewing is not null) await reviewing;
            find?.Close(); StorageCommandIntegrationTests.CloseFindWindows();
            foreach (var report in opened) report.Close();
            StorageCommandIntegrationTests.CloseForJobs(services, window, root, state);
        }
    }

    /// <summary>Without compression in this build the link is absent, and the details still read.</summary>
    [AvaloniaFact]
    public async Task Without_compression_the_link_is_absent_and_the_details_still_read()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: new NoStorageTransforms());
        vm.FileRecordsForTests = new Records();
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        try
        {
            var report = await OpenDetails(vm, opened, Path.Join(root, "files", "a.txt"));
            Assert.Contains("NTFS", report.Text, StringComparison.Ordinal);
            Assert.False(report.CompressionLinkForTest.IsVisible);
        }
        finally
        {
            foreach (var report in opened) report.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>
    /// The link says why it cannot open a review: an item that is no file or folder, a drive that is not NTFS, or a dialog
    /// already open in the main window. Reading the details again updates it.
    /// </summary>
    [AvaloniaFact]
    public async Task The_link_says_why_it_cannot_open_a_review()
    {
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        var records = new Records { FileSystem = "FAT32" };
        vm.FileRecordsForTests = records;
        var opened = new List<ReportWindow>();
        using var tracking = FileDetailsWindowTests.TrackReportWindows(opened);
        Task<bool>? confirm = null;
        try
        {
            var report = await OpenDetails(vm, opened, Path.Join(root, "files", "a.txt"));
            var link = report.CompressionLinkForTest;
            string? Reason() => ToolTip.GetTip(link) as string;
            Assert.True(link.IsVisible);
            Assert.False(link.IsEnabled);
            Assert.Equal("NTFS compression needs an NTFS drive; this item is on FAT32.", Reason());
            Assert.Equal(Reason(), AutomationProperties.GetHelpText(link));

            records.FileSystem = "NTFS"; records.Kind = FileObjectKind.SymbolicLink;
            await report.RefreshDetailsForTestAsync();
            Assert.Equal("NTFS compression applies to files and folders.", Reason());

            records.Kind = null;
            confirm = vm.Dialogs.ConfirmAsync("Fixture", "A dialog open in the main window.");
            await Until(() => vm.Dialogs.IsOpen);
            await report.RefreshDetailsForTestAsync();
            Assert.False(link.IsEnabled);
            Assert.Equal("Close the dialog in the main window first.", Reason());

            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await confirm;
            await report.RefreshDetailsForTestAsync();
            Assert.True(link.IsEnabled);
            Assert.StartsWith("Opens a review of this item's NTFS compression in the main window", Reason(), StringComparison.Ordinal);
            Assert.Empty(adapter.Pins);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (confirm is not null) await confirm;
            foreach (var report in opened) report.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
