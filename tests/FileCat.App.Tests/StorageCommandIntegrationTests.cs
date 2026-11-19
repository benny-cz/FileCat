using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class StorageCommandIntegrationTests
{
    private sealed class Pin(StorageTarget target) : IStorageReviewLease
    {
        public StorageTarget Target => target;
        public StorageObservation Observation => new(StorageObservationState.Disabled, StorageObservationState.Disabled,
            new StorageObjectIdentity(1, target.Path) { VolumeBinding = "fixture-volume" }, target.Source.Kind == EntryKind.Directory, null);
        public int Disposals;
        public bool IsAlive => Disposals == 0;
        public void Dispose() => Disposals++;
    }
    private sealed class Adapter : IStorageTransforms
    {
        public int AvailabilityCalls;
        public readonly List<Pin> Pins = [];
        public Action? Blocking;
        public StorageProblem? Unavailable(StorageAction action) { AvailabilityCalls++; return null; }
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        { Blocking?.Invoke(); var pin = new Pin(target); Pins.Add(pin); return pin; }
        public StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action) => root.Observation;
        public StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct) => root.Observation;
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) => throw new NotSupportedException("No fake mutation");
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }
    private static Button Primary(MainWindow window, string text) => Assert.Single(window.GetVisualDescendants().OfType<Button>(),
        button => button is not RadioButton && button.Content as string == text);
    private static Job ResultJob(IReadOnlyList<ItemRef> items, StorageAction action)
    {
        bool efs = action == StorageAction.DecryptEfs;
        const StorageScope scope = StorageScope.SelectedItems;
        var revision = Guid.NewGuid();
        var completed = new StorageObjectIdentity(1, new string('A', 32)) { VolumeBinding = "01234567-89ab-cdef-0123-456789abcdef" };
        var incomplete = completed with { ObjectId = new string('B', 32) };
        var before = new StorageObservation(StorageObservationState.Disabled,
            efs ? StorageObservationState.Enabled : StorageObservationState.Disabled, completed, false, null);
        var consent = efs ? StorageEfsConsent.Capture(revision, action, scope, [completed, incomplete], new(true)) : null;
        var request = new JobRequest
        {
            Kind = JobKind.StorageTransform,
            Sources = items,
            StorageTransform = new(action, scope, revision)
            {
                Roots = [new(0, 0, before), new(1, 1, before with { Identity = incomplete })],
                EfsConsent = consent,
            },
        };
        var job = new Job(request, "Storage change", "fixture", [], []);
        StorageOutcomeCounts counts = efs ? new(Changed: 1, Uncertain: 1) : new(Changed: 1, Failed: 1);
        job.SetStorageResult(new(action, scope, revision, counts)
        {
            Roots = [new(0, completed, StorageCompleteness.Complete, true), new(1, incomplete, StorageCompleteness.Incomplete, false)],
            Discovery = efs ? StorageCompleteness.UnknownRemainder : StorageCompleteness.Incomplete,
            Canceled = efs,
            ReviewPending = efs,
        });
        job.SetState(efs ? JobState.Canceled : JobState.CompletedWithIssues);
        Assert.Empty(job.CompletedRootIndices); return job;
    }

    [AvaloniaTheory]
    [InlineData(StorageAction.Compress, "refresh")]
    [InlineData(StorageAction.Compress, "marks")]
    [InlineData(StorageAction.Compress, "navigation")]
    [InlineData(StorageAction.Compress, "closed")]
    [InlineData(StorageAction.DecryptEfs, "refresh")]
    [InlineData(StorageAction.DecryptEfs, "marks")]
    [InlineData(StorageAction.DecryptEfs, "navigation")]
    [InlineData(StorageAction.DecryptEfs, "closed")]
    public async Task Completion_only_unmarks_the_captured_selection_when_navigation_and_mark_intent_still_match(StorageAction action, string change)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!; var listing = tab.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            listing.MarkNames(["a.txt", "b.txt"], true);
            var origin = StorageSelectionOrigin.Capture(tab); var captured = MainViewModel.CaptureStorageItems(listing);
            var job = ResultJob(captured.Items, action); long revision = listing.MarkRevision;
            if (change == "refresh")
            {
                int generation = listing.Generation; tab.Refresh();
                await Until(() => listing.Generation != generation && !listing.IsRefreshing && listing.State == ListingState.Complete);
                Assert.Equal(revision, listing.MarkRevision);
            }
            else if (change == "marks")
            {
                listing.MarkNames(["a.txt"], false); listing.MarkNames(["a.txt"], true);
                Assert.True(listing.MarkRevision > revision);
            }
            else if (change == "navigation")
            {
                tab.Navigate(Location.FileSystem(root));
                await Until(() => listing.State == ListingState.Complete && listing.Location?.Path == root);
                tab.Navigate(origin.Location);
                await Until(() => listing.State == ListingState.Complete && listing.Location == origin.Location);
                listing.MarkNames(["a.txt", "b.txt"], true);
            }
            else tab.Dispose();
            origin.UnmarkCompleted(job);
            if (change != "closed") Assert.Equal(change == "refresh" ? 1 : 2, listing.MarkedCount);
            if (change == "refresh") Assert.Equal(captured.Items[1].Name, Assert.Single(listing.GetSelection()).Name);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Bounded_capture_includes_hidden_marks_and_refuses_the_65th_before_native_inspection()
    {
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        try
        {
            var tab = vm.ActiveTab!; var listing = tab.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            listing.MarkNames(["a.txt", "b.txt"], true); tab.SetFilter("a*");
            await Until(() => listing.AppliedViewSpecVersion == listing.ViewSpecVersion && listing.VisibleCount == 2);
            var captured = MainViewModel.CaptureStorageItems(listing);
            Assert.Equal(2, captured.Items.Length); Assert.Equal(1, captured.Hidden.Count(value => value));
            Assert.Equal("b.txt", captured.Items[Array.IndexOf(captured.Hidden, true)].Name);
            string folder = tab.Location!.Path;
            for (int i = 0; i < 63; i++) File.WriteAllText(Path.Join(folder, $"extra-{i}.txt"), "fixture");
            tab.SetFilter(""); tab.Refresh();
            await Until(() => listing.State == ListingState.Complete && !listing.IsRefreshing && listing.TotalCount == 65);
            listing.MarkNames(Directory.EnumerateFiles(folder).Select(Path.GetFileName).Cast<string>(), true);
            Assert.Equal(65, listing.MarkedCount);
            vm.Execute(CommandIds.StorageAttributes);
            await Until(() => vm.CurrentNotification?.Message.Contains("Select at most 64 items", StringComparison.Ordinal) == true);
            Assert.Empty(adapter.Pins); Assert.Empty(services.Jobs.Jobs); Assert.False(vm.Dialogs.IsOpen);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Command_discloses_original_result_locations_and_detaches_only_after_actionable_review()
    {
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        try
        {
            var tab = vm.ActiveTab!;
            var set = services.ResultSets.Create("Captured roots", "fixture"); set.FullFolders = true;
            set.Add(new(Location.FileSystem(@"C:\first"), "same", EntryKind.File), "first");
            set.Add(new(Location.FileSystem(@"C:\second"), "same", EntryKind.File), "second"); set.IsComplete = true;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 2);
            tab.Listing.MarkNames(["same"], true);
            for (int i = 0; i < 30; i++) Assert.True(vm.GetAvailability(CommandIds.StorageAttributes).Enabled);
            Assert.Equal(1, adapter.AvailabilityCalls);
            vm.Execute(CommandIds.StorageAttributes);
            await Until(() => window.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Compress"));
            window.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == "Compress").IsChecked = true;
            await Until(() => Primary(window, "Compress").IsEnabled);
            Assert.Equal(2, adapter.Pins.Count);
            tab.Navigate(Location.FileSystem(root));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Location?.Path == root);
            Assert.True(vm.Dialogs.IsOpen); Assert.True(Primary(window, "Compress").IsEnabled);
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            await Until(() => adapter.Pins.All(pin => pin.Disposals == 1));
            Assert.Equal(new[] { @"C:\first\same", @"C:\second\same" }, adapter.Pins.Select(pin => pin.Target.Path));
            Assert.Empty(services.Jobs.Jobs);
        }
        finally { ((OverlayDialogService)vm.Dialogs).CancelAll(); AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("remote")]
    [InlineData("archive")]
    [InlineData("stream")]
    public async Task Mixed_or_lexically_refused_selection_discloses_every_original_without_native_inspection(string refusal)
    {
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        Task? running = null;
        try
        {
            var tab = vm.ActiveTab!;
            var set = services.ResultSets.Create("Mixed captured roots", "fixture"); set.FullFolders = true;
            var parent = refusal switch
            {
                "remote" => new Location(Schemes.Sftp, "/original/remote", session: "fixture-profile"),
                "archive" => new Location("zip", "nested/folder", Location.FileSystem(@"C:\archives\original.zip")),
                _ => Location.FileSystem(@"C:\refused"),
            };
            string name = refusal == "stream" ? "other:stream" : "other\nfile";
            set.Add(new(Location.FileSystem(@"C:\captured"), "local.txt", EntryKind.File), "");
            set.Add(new(parent, name, EntryKind.File), ""); set.IsComplete = true;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 2);
            tab.Listing.MarkNames(["local.txt", name], true); tab.SetFilter("local*");
            await Until(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion);
            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await Until(() => vm.Dialogs.IsOpen && window.GetVisualDescendants().OfType<Expander>().Any());
            var disclosure = Assert.Single(window.GetVisualDescendants().OfType<Expander>());
            Assert.True(disclosure.IsExpanded); Assert.Equal("Selected items (2)", disclosure.Header);
            var details = Assert.Single(disclosure.GetVisualDescendants().OfType<TextBox>(), box => box.IsReadOnly);
            string captured = details.Text!;
            Assert.Contains("local.txt", captured); Assert.Contains("C:\\\\captured\\\\local.txt", captured);
            Assert.Contains("Captured locator:", captured);
            Assert.Contains(refusal == "stream" ? "other:stream" : "other\\nfile", captured);
            Assert.Contains("Hidden mark: Yes", captured); Assert.Contains(StoragePaths.SourceProblem(new(parent, name, EntryKind.File))!.Message, captured);
            Assert.Empty(window.GetVisualDescendants().OfType<RadioButton>());
            Assert.Empty(adapter.Pins); Assert.Empty(services.Jobs.Jobs);
            tab.Navigate(Location.FileSystem(root));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Location?.Path == root);
            Assert.Equal(captured, details.Text);
            var search = Assert.Single(disclosure.GetVisualDescendants().OfType<TextBox>(), box => !box.IsReadOnly);
            search.Text = @"C:\captured";
            await Until(() => details.Text != captured);
            Assert.Contains("local.txt", details.Text); Assert.DoesNotContain(refusal == "stream" ? "other:stream" : "other\\nfile", details.Text);
            ((OverlayDialogService)vm.Dialogs).CancelAll(); await running;
            Assert.Empty(adapter.Pins); Assert.Empty(services.Jobs.Jobs);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (running is not null) await running;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Find_refuses_storage_until_final_result_or_group_publication_has_finished(bool duplicates)
    {
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        Task? blocked = null; FindWindow? find = null;
        try
        {
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            File.WriteAllText(Path.Join(vm.ActiveTab!.Location!.Path, "b.txt"), "alpha");
            vm.Execute(CommandIds.FindFiles); await Until(() => FindWindow.OpenWindows.Count == 1);
            find = FindWindow.OpenWindows.Single(); find.NamesBox.Text = "*.txt";
            if (!duplicates)
            {
                find.StartSearch(RefineMode.Replace);
                await Until(() => find.StorageCommandAvailability.Enabled);
            }
            // The initial enumeration and the final publication use the same result-set queue.
            // Queue a barrier for any replacement worker the watchdog starts during a slow test run.
            string device = services.ResultSets.GetDeviceKey(new Location(Schemes.ResultSet, string.Empty));
            int workers = 0;
            blocked = Task.WhenAll(Enumerable.Range(0, services.Io.MaxThreadsPerDevice).Select(_ => services.Io.RunOwned(device, IoPriority.Interactive, token =>
            {
                if (Interlocked.Increment(ref workers) == services.Io.ThreadsPerDevice) entered.Set();
                release.Wait(token);
                return true;
            }, TestContext.Current.CancellationToken)));
            await Until(() => entered.IsSet);
            find.StartSearch(duplicates ? RefineMode.Replace : RefineMode.Intersect, duplicates ? DuplicateCriteria.Size : null);
            await Until(() => find.Session!.Finished && find.IsIdle &&
                (!duplicates || services.ResultSets.Get(find.ResultsTab!.Location!)?.ShowsGroups == true));
            if (!duplicates)
            {
                Assert.Equal(ListingState.Complete, find.ResultsTab!.Listing.State);
                Assert.True(find.ResultsTab.Listing.IsRefreshing);
            }
            Assert.False(find.StorageCommandAvailability.Enabled);
            Assert.Contains("results to finish updating", find.StorageCommandAvailability.Reason);
            find.Run(CommandIds.StorageAttributes);
            Assert.False(find.Dialogs.IsOpen); Assert.Empty(adapter.Pins);
            release.Set(); await blocked;
            await Until(() => find.StorageCommandAvailability.Enabled);
            var files = find.FindMenu.Items.OfType<MenuItem>().First();
            var items = files.Items.OfType<MenuItem>().ToList();
            int at = items.FindIndex(item => item.Header as string == "NTFS compression…");
            Assert.True(at > 0); Assert.Equal(services.Commands.Get(CommandIds.Checksum)!.Title, items[at - 1].Header);
            Assert.Equal(services.Commands.Get(CommandIds.Pack)!.Title, items[at + 1].Header);
        }
        finally
        {
            release.Set(); if (blocked is not null) await blocked;
            find?.Close(); AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Initial_listing_invalidation_closes_review_and_drains_a_late_native_pin_without_submitting()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var adapter = new Adapter { Blocking = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); } };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        Task? running = null;
        try
        {
            var tab = vm.ActiveTab!;
            var set = services.ResultSets.Create("Original selection", "fixture"); set.FullFolders = true;
            set.Add(new(Location.FileSystem(@"C:\captured"), "original", EntryKind.File), ""); set.IsComplete = true;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 1);
            tab.Listing.FocusName("original");
            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await Until(() => entered.IsSet);
            tab.Refresh();
            await Until(() => !vm.Dialogs.IsOpen);
            Assert.False(running.IsCompleted); Assert.Empty(services.Jobs.Jobs);
            release.Set(); await running;
            Assert.Equal(1, Assert.Single(adapter.Pins).Disposals);
            Assert.Equal("The selected listing changed while checking. Nothing was changed.", vm.CurrentNotification?.Message);
        }
        finally
        {
            release.Set(); ((OverlayDialogService)vm.Dialogs).CancelAll(); if (running is not null) await running;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Origin_registration_precedes_JobAdded_and_failed_admission_removes_it_and_retires_its_pins()
    {
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        Task? running = null; bool observed = false;
        try
        {
            var tab = vm.ActiveTab!;
            var set = services.ResultSets.Create("Reviewed roots", "fixture"); set.FullFolders = true;
            set.Add(new(Location.FileSystem(@"C:\captured"), "original", EntryKind.File), ""); set.IsComplete = true;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 1);
            tab.Listing.FocusName("original");
            services.Jobs.JobAdded += _ => { observed = true; Assert.Equal(1, vm.StorageOriginCount); throw new InvalidOperationException("Reject this admission"); };
            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await Until(() => window.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Compress"));
            window.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == "Compress").IsChecked = true;
            await Until(() => Primary(window, "Compress").IsEnabled);
            Primary(window, "Compress").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => running);
            Assert.Equal("Reject this admission", error.Message);
            Assert.True(observed); Assert.Equal(0, vm.StorageOriginCount); Assert.Empty(services.Jobs.Jobs);
            Assert.Equal(1, Assert.Single(adapter.Pins).Disposals);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll();
            if (running is not null) try { await running; } catch (InvalidOperationException) { }
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Unsupported_build_keeps_main_menu_and_palette_command_visible_with_no_default_gesture()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            var definition = services.Commands.Get(CommandIds.StorageAttributes)!;
            Assert.Equal("NTFS compression…", definition.Title); Assert.Empty(definition.DefaultGestures);
            Assert.Contains("uncompress", definition.Keywords); Assert.Contains("main file data", definition.Description);
            Assert.True(MainMenuModel.IsPlatformApplicable(definition.Id));
            Assert.Equal("File › Inspect", MainMenuModel.PathOf(definition.Id));
            var inspect = MainMenuModel.Layout.SelectMany(menu => menu.Items).OfType<MainMenuModel.Submenu>().Single(menu => menu.Header == "_Inspect");
            Assert.Equal(CommandIds.StorageAttributes, inspect.Items[Array.IndexOf(inspect.Items, CommandIds.Attributes) + 1]);
            Assert.False(vm.GetAvailability(definition.Id).Enabled); Assert.Contains("NTFS", vm.GetAvailability(definition.Id).Reason);
            vm.Execute(definition.Id); Assert.False(vm.Dialogs.IsOpen); Assert.Empty(services.Jobs.Jobs);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
