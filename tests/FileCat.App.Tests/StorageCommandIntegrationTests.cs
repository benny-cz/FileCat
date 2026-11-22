using System.Collections.Immutable;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class StorageCommandIntegrationTests
{
    internal sealed class Pin(StorageTarget target) : IStorageReviewLease
    {
        private int _disposals;
        public StorageTarget Target => target;
        public StorageObservation Observation => new(StorageObservationState.Disabled, StorageObservationState.Disabled,
            new StorageObjectIdentity(1, target.Path) { VolumeBinding = "fixture-volume" }, target.Source.Kind == EntryKind.Directory, null);
        public int Disposals => Volatile.Read(ref _disposals);
        public bool IsAlive => Disposals == 0;
        public void Dispose() => Interlocked.Increment(ref _disposals);
    }

    /// <summary>Compression-only fake. Execution stays unsupported unless a test supplies the admitted native call.</summary>
    internal sealed class Adapter : IStorageTransforms
    {
        private readonly object _gate = new();
        private readonly List<Pin> _pins = [];
        private int _calls;
        public int AvailabilityCalls;
        public Action? Blocking;
        /// <summary>Runs on the job worker while it owns the native allowance, before its first held-object call.</summary>
        public Action? Opening;
        /// <summary>The fake admitted call. Null keeps job execution unsupported.</summary>
        public Func<StorageObservation, StorageMutationResult>? Apply;
        /// <summary>Main-data compression observed by a running job.</summary>
        public StorageObservationState Current = StorageObservationState.Disabled;
        public List<Pin> Pins { get { lock (_gate) return [.. _pins]; } }
        public int Calls => Volatile.Read(ref _calls);
        public StorageProblem? Unavailable(StorageAction action) { AvailabilityCalls++; return null; }
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        { Blocking?.Invoke(); var pin = new Pin(target); lock (_gate) _pins.Add(pin); return pin; }
        public StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action) => root.Observation;
        public StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct) => root.Observation;
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct)
        {
            if (Apply is null) throw new NotSupportedException("No fake mutation");
            Opening?.Invoke();
            return new Traversal(new Held((Pin)root, this));
        }
        internal static StorageMutationResult Changed(StorageObservation before) =>
            new(before, before with { Compression = StorageObservationState.Enabled }, StorageEffect.Changed, null) { NativeCallAttempted = true };
        internal static StorageMutationResult Uncertain(StorageObservation before) =>
            new(before, null, StorageEffect.Uncertain, new(StorageReasonKind.Unknown, "fixture-uncertain",
                "The admitted fixture call returned no trustworthy postcondition.", true)) { NativeCallAttempted = true };
        internal static StorageMutationResult Unexpected(StorageObservation before) =>
            throw new InvalidOperationException("This fixture expects no admitted native call.");

        private sealed class Held(Pin pin, Adapter adapter) : IStorageObjectLease
        {
            public string Path => pin.Target.Path;
            public StorageObjectIdentity Identity => pin.Observation.Identity;
            public StorageObservation Observe(CancellationToken ct) => pin.Observation with { Compression = adapter.Current };
            public StorageObservation PrepareMutation(StorageAction action, CancellationToken ct) => Observe(ct);
            public StorageMutationResult ApplyAdmitted(StorageAction action)
            {
                Interlocked.Increment(ref adapter._calls);
                return adapter.Apply!(Observe(CancellationToken.None));
            }
            public void Dispose() { }
        }
        private sealed class Traversal(Held root) : IStorageTraversal
        {
            public IStorageObjectLease Root => root;
            public IReadOnlyList<StorageObjectIdentity> RootAncestors(CancellationToken ct) => [];
            public IEnumerable<IStorageObjectLease> Children(IStorageObjectLease directory, CancellationToken ct) => [];
            public void Dispose() => root.Dispose();
        }
    }

    /// <summary>Observes storage admissions; optionally holds each admitted job before its first scheduling pass.</summary>
    private sealed class AdmittedJobs : IDisposable
    {
        private readonly JobManager _manager;
        private readonly bool _pause;
        private readonly List<Job> _jobs = [];
        public AdmittedJobs(JobManager manager, bool pause = true)
        {
            _manager = manager; _pause = pause;
            manager.JobAdded += OnAdded;
        }
        public List<Job> Jobs { get { lock (_jobs) return [.. _jobs]; } }
        private void OnAdded(Job job)
        {
            if (job.Kind != JobKind.StorageTransform) return;
            lock (_jobs) _jobs.Add(job);
            if (_pause) job.Pause();
        }
        public void Dispose() => _manager.JobAdded -= OnAdded;
    }

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!condition()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }
    /// <summary>Keeps the dispatcher running for a fixed time, so a late or repeated publication would be observed.</summary>
    private static async Task Settle(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10, TestContext.Current.CancellationToken); }
        Dispatcher.UIThread.RunJobs();
    }
    private static Task OnOwnThread(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    private static string Describe(Job job) => $"{job.State}: {JobIssue.Summarize(job.Issues) ?? "no issues"}; reasons: " +
        string.Join(" | ", job.StorageResult?.Reasons.Select(reason => $"{reason.Problem.Code}: {reason.Problem.Message}") ?? []);

    /// <summary>
    /// A main window whose jobs can run with fake held objects. On a qualified Windows client the job journal uses the
    /// native private evidence store, which refuses a namespace other identities can change (an inherited temp ACL may grant
    /// that). The state directory is therefore an explicit invoking-user namespace, as in the storage history fixtures.
    /// </summary>
    private static (AppServices Services, MainViewModel Vm, MainWindow Window, string Root, string State) OpenForJobs(Adapter adapter)
    {
        string state = Directory.CreateTempSubdirectory("filecat-storage-jobs-").FullName;
        var paths = FileCat.Core.State.AppPaths.Resolve(overrideRoot: state);
        var journals = Directory.CreateDirectory(paths.JournalDirectory);
        if (OperatingSystem.IsWindows())
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent(); string sid = identity.User!.Value;
            var security = new System.Security.AccessControl.DirectorySecurity();
            security.SetSecurityDescriptorSddlForm($"O:{sid}D:P(A;OICI;FA;;;{sid})(A;OICI;FA;;;SY)",
                System.Security.AccessControl.AccessControlSections.Owner | System.Security.AccessControl.AccessControlSections.Access);
            journals.SetAccessControl(security);
        }
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(statePaths: paths, storageTransforms: adapter);
        return (services, vm, window, root, state);
    }
    private static void CloseForJobs(AppServices services, Window window, string root, string state)
    {
        AccessibilityTests.Close(services, window, root);
        try { Directory.Delete(state, recursive: true); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    private static Button Primary(Visual root, string text) => Assert.Single(root.GetVisualDescendants().OfType<Button>(),
        button => button is not RadioButton && button.Content as string == text);
    private static async Task ApproveCompression(Visual host)
    {
        await Until(() => host.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Compress"));
        host.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == "Compress").IsChecked = true;
        await Until(() => Primary(host, "Compress").IsEnabled);
        Primary(host, "Compress").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private static async Task<ResultSet> ShowResults(AppServices services, TabViewModel tab, params (string Folder, string Name)[] items)
    {
        var set = services.ResultSets.Create("Captured roots", "fixture"); set.FullFolders = true;
        foreach (var (folder, name) in items) set.Add(new(Location.FileSystem(folder), name, EntryKind.File), "");
        set.IsComplete = true;
        tab.Navigate(ResultSetProvider.LocationOf(set));
        await Until(() => tab.Location?.Session == set.Id && tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == items.Length);
        return set;
    }
    /// <summary>Find over the fixture folder. Its originals are real local paths, which only Windows admits for storage capture.</summary>
    private static async Task<FindWindow> OpenSettledFind(MainViewModel vm)
    {
        Assert.True(vm.ActiveTab?.Location?.IsFileSystem, "Find opens on the active local folder.");
        Assert.Empty(FindWindow.OpenWindows);
        vm.Execute(CommandIds.FindFiles); await Until(() => FindWindow.OpenWindows.Count == 1);
        var find = FindWindow.OpenWindows.Single(); find.NamesBox.Text = "*.txt";
        find.StartSearch(RefineMode.Replace);
        await Until(() => find.StorageResultsSettled && find.ResultsTab?.Listing.TotalCount >= 2);
        return find;
    }
    /// <summary>A failed Find fixture must not leave its window to the next test.</summary>
    private static void CloseFindWindows() { foreach (var open in FindWindow.OpenWindows.ToList()) open.Close(); }
    private static Job ResultJob(IReadOnlyList<ItemRef> items, StorageAction action, string outcome = "partial")
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
        var roots = ImmutableRoots(completed, incomplete);
        var result = (efs, outcome) switch
        {
            (true, _) => new StorageResultSnapshot(action, scope, revision, new(Changed: 1, Uncertain: 1))
            { Roots = roots, Discovery = StorageCompleteness.UnknownRemainder, Canceled = true, ReviewPending = true },
            (false, "canceled") => new StorageResultSnapshot(action, scope, revision, new(Changed: 1, NotProcessed: 1))
            { Roots = roots, Discovery = StorageCompleteness.UnknownRemainder, Canceled = true },
            (false, "uncertain") => new StorageResultSnapshot(action, scope, revision, new(Changed: 1, Uncertain: 1))
            { Roots = roots, Discovery = StorageCompleteness.Incomplete, ReviewPending = true },
            _ => new StorageResultSnapshot(action, scope, revision, new(Changed: 1, Failed: 1))
            { Roots = roots, Discovery = StorageCompleteness.Incomplete },
        };
        job.SetStorageResult(result);
        // The canceled EFS row keeps its recorded Stop state; Compress rows use the product's terminal reducer.
        job.SetState(efs ? JobState.Canceled : result.FinalState());
        Assert.Equal(efs ? JobState.Canceled : outcome switch
        {
            "canceled" => JobState.Canceled,
            "uncertain" => JobState.Failed,
            _ => JobState.CompletedWithIssues,
        }, job.State);
        Assert.Empty(job.CompletedRootIndices); return job;
    }
    private static ImmutableArray<StorageRootResult> ImmutableRoots(StorageObjectIdentity completed, StorageObjectIdentity incomplete) =>
        [new(0, completed, StorageCompleteness.Complete, true), new(1, incomplete, StorageCompleteness.Incomplete, false)];

    [AvaloniaTheory]
    [InlineData(StorageAction.Compress, "refresh", "partial")]
    [InlineData(StorageAction.Compress, "marks", "partial")]
    [InlineData(StorageAction.Compress, "invert", "partial")]
    [InlineData(StorageAction.Compress, "navigation", "partial")]
    [InlineData(StorageAction.Compress, "closed", "partial")]
    [InlineData(StorageAction.Compress, "refresh", "canceled")]
    [InlineData(StorageAction.Compress, "marks", "canceled")]
    [InlineData(StorageAction.Compress, "navigation", "canceled")]
    [InlineData(StorageAction.Compress, "closed", "canceled")]
    [InlineData(StorageAction.Compress, "refresh", "uncertain")]
    [InlineData(StorageAction.Compress, "marks", "uncertain")]
    [InlineData(StorageAction.Compress, "navigation", "uncertain")]
    [InlineData(StorageAction.Compress, "closed", "uncertain")]
    [InlineData(StorageAction.DecryptEfs, "refresh", "canceled")]
    [InlineData(StorageAction.DecryptEfs, "marks", "canceled")]
    [InlineData(StorageAction.DecryptEfs, "invert", "canceled")]
    [InlineData(StorageAction.DecryptEfs, "navigation", "canceled")]
    [InlineData(StorageAction.DecryptEfs, "closed", "canceled")]
    public async Task Completion_only_unmarks_the_captured_selection_when_navigation_and_mark_intent_still_match(StorageAction action, string change, string outcome)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.ActiveTab!; var listing = tab.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            listing.MarkNames(["a.txt", "b.txt"], true);
            var origin = StorageSelectionOrigin.Capture(tab); var captured = MainViewModel.CaptureStorageItems(listing);
            var job = ResultJob(captured.Items, action, outcome); long revision = listing.MarkRevision;
            // A second tab shows the same folder with the same names marked. Only the captured origin may be cleaned.
            var other = vm.Workspace.Panels[1].ActiveTab!;
            await Until(() => other.Location == tab.Location && other.Listing.State == ListingState.Complete && other.Listing.VisibleCount == 3);
            other.Listing.MarkNames(["a.txt", "b.txt"], true);
            long otherMarks = other.Listing.MarkRevision, otherNavigation = other.NavigationGeneration;
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
            else if (change == "invert")
            {
                await vm.ExecuteAsync(CommandIds.MarkInvert);
                Assert.Equal(0, listing.MarkedCount);
                await vm.ExecuteAsync(CommandIds.MarkInvert);
                Assert.Equal(captured.Items.Select(item => item.Name).Order(), listing.GetSelection().Select(item => item.Name).Order());
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
            if (change == "closed")
            {
                Assert.True(listing.IsDisposed); Assert.False(origin.SameNavigation);
                origin.UnmarkCompleted(job); // a retired origin is a no-op: no exception, navigation or cleanup elsewhere
                Assert.Equal(2, other.Listing.MarkedCount); Assert.Equal(otherMarks, other.Listing.MarkRevision);
                Assert.Equal(origin.Location, other.Location); Assert.Equal(otherNavigation, other.NavigationGeneration);
                Assert.Same(other, vm.Workspace.Panels[1].ActiveTab);
                return;
            }
            origin.UnmarkCompleted(job);
            // Only the complete root of an unchanged origin is unmarked; the incomplete root always stays marked.
            Assert.Equal(change == "refresh" ? 1 : 2, listing.MarkedCount);
            Assert.Contains(captured.Items[1].Name, listing.GetSelection().Select(item => item.Name));
            if (change == "refresh") Assert.Equal(captured.Items[1].Name, Assert.Single(listing.GetSelection()).Name);
            Assert.Equal(2, other.Listing.MarkedCount); Assert.Equal(otherMarks, other.Listing.MarkRevision);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("canceled")]
    [InlineData("uncertain")]
    [InlineData("satisfied")]
    public async Task Compression_completion_refreshes_the_captured_result_tab_keeps_incomplete_marks_and_forgets_observations(string outcome)
    {
        var adapter = new Adapter
        {
            Current = outcome == "satisfied" ? StorageObservationState.Enabled : StorageObservationState.Disabled,
            Apply = outcome == "uncertain" ? Adapter.Uncertain : Adapter.Unexpected,
        };
        var (services, vm, window, root, state) = OpenForJobs(adapter);
        using var admitted = new AdmittedJobs(services.Jobs);
        Task? running = null;
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var tab = vm.ActiveTab!;
            await ShowResults(services, tab, (@"C:\first", "one.txt"), (@"C:\second", "two.txt"));
            tab.Listing.MarkNames(["one.txt", "two.txt"], true);
            // An observation-lane value (such as allocated size) for a captured original, and an ordinary cached value.
            var observed = new MetadataField("fixture-observation", "Fixture observation", MetadataCost.Cheap, _ => true,
                (_, _) => "observed", value => value as string ?? "", ObservationLane: true);
            var ordinary = observed with { Id = "fixture-ordinary", ObservationLane = false };
            services.Metadata.Register(observed); services.Metadata.Register(ordinary);
            const string path = @"C:\first\one.txt"; var entry = new EntryData("one.txt", EntryKind.File, 5, 1);
            await OnOwnThread(() =>
            {
                services.Metadata.Compute(observed.Id, path, entry, ct);
                services.Metadata.Compute(ordinary.Id, path, entry, ct);
            }).WaitAsync(TimeSpan.FromSeconds(15), ct);
            Assert.Equal(MetadataState.Available, services.Metadata.Peek(observed.Id, path, entry).State);

            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await ApproveCompression(window);
            await running;
            var job = Assert.Single(admitted.Jobs);
            Assert.Equal(JobState.Queued, job.State); Assert.True(job.IsPaused); Assert.Equal(1, vm.StorageOriginCount);
            Assert.Equal(MetadataState.Available, services.Metadata.Peek(observed.Id, path, entry).State);
            int generation = tab.Listing.Generation; long navigation = tab.NavigationGeneration; var location = tab.Location;

            if (outcome == "canceled") job.Cancel(); // Stop before the queued job started: the manager retires it unstarted
            else job.Resume();
            await Until(() => job.State.IsFinished() && vm.StorageOriginCount == 0 &&
                tab.Listing.Generation != generation && !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete);
            var expected = outcome switch { "canceled" => JobState.Canceled, "uncertain" => JobState.Failed, _ => JobState.Completed };
            Assert.True(job.State == expected, Describe(job));

            // The captured workspace result tab was refreshed in place.
            Assert.Same(tab, vm.ActiveTab); Assert.Equal(location, tab.Location); Assert.Equal(navigation, tab.NavigationGeneration);
            var result = Assert.IsType<StorageResultSnapshot>(job.StorageResult);
            bool complete = outcome == "satisfied";
            Assert.All(result.Roots, row => Assert.Equal(complete ? StorageCompleteness.Complete : StorageCompleteness.Incomplete, row.Completeness));
            // Incomplete roots stay marked. Guarded cleanup removes only complete roots of the unchanged origin.
            Assert.Equal(complete ? 0 : 2, tab.Listing.MarkedCount);
            // Observation-lane metadata is invalidated; unrelated cached values are not cleared wholesale.
            Assert.NotEqual(MetadataState.Available, services.Metadata.Peek(observed.Id, path, entry).State);
            Assert.Equal(MetadataState.Available, services.Metadata.Peek(ordinary.Id, path, entry).State);

            string title = Formatters.SafeName(job.Title);
            var notice = Assert.Single(vm.RecentNotifications, item => item.Message.StartsWith(title + ": ", StringComparison.Ordinal));
            switch (outcome)
            {
                case "canceled":
                    Assert.Equal(JobState.Canceled, job.State);
                    Assert.Equal(title + ": 0 changed, 0 already satisfied, 0 skipped, 0 failed, 0 uncertain, 2 not processed. " +
                        "Additional items may not have been discovered; counts cover observed items only. Stopped; completed changes remain.", notice.Message);
                    Assert.False(notice.IsError); Assert.Equal(0, adapter.Calls);
                    break;
                case "uncertain":
                    Assert.Equal(JobState.Failed, job.State); Assert.True(result.ReviewPending);
                    Assert.Contains("0 changed, 0 already satisfied, 0 skipped, 0 failed, 1 uncertain, 1 not processed.", notice.Message);
                    Assert.Contains("Result not confirmed: the effect of a native call remains uncertain.", notice.Message);
                    Assert.EndsWith(" Review storage changes in Operations (Ctrl+J).", notice.Message);
                    // Uncertainty stops: the second root was never offered a native call.
                    Assert.True(notice.IsError); Assert.Equal(1, adapter.Calls);
                    // The same session offers the pending storage review.
                    await Until(() => vm.Operations.Interrupted.Any(row => row.IsStorage));
                    break;
                default:
                    Assert.Equal(JobState.Completed, job.State);
                    Assert.Equal(title + ": 0 changed, 2 already satisfied, 0 skipped, 0 failed, 0 uncertain, 0 not processed. " +
                        "The requested scope was fully checked.", notice.Message);
                    Assert.False(notice.IsError); Assert.Equal(0, adapter.Calls);
                    break;
            }
            await Until(() => adapter.Pins.All(pin => pin.Disposals == 1));
            Assert.Equal(2, adapter.Pins.Count);
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (running is not null) await running;
            CloseForJobs(services, window, root, state);
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Closing_the_origin_tab_after_the_actionable_review_keeps_the_job_and_cleans_no_other_tab(bool duringReview)
    {
        // Already satisfied: completion would unmark both roots if its origin were still current.
        var adapter = new Adapter { Current = StorageObservationState.Enabled, Apply = Adapter.Unexpected };
        var (services, vm, window, root, state) = OpenForJobs(adapter);
        using var admitted = new AdmittedJobs(services.Jobs);
        Task? running = null;
        try
        {
            var panel = vm.Workspace.Panels[0]; var other = vm.Workspace.Panels[1].ActiveTab!;
            var set = await ShowResults(services, other, (@"C:\first", "one.txt"), (@"C:\second", "two.txt"));
            other.Listing.MarkNames(["one.txt", "two.txt"], true);
            var origin = panel.OpenTab(ResultSetProvider.LocationOf(set));
            await Until(() => origin.Listing.State == ListingState.Complete && origin.Listing.TotalCount == 2);
            origin.Listing.MarkNames(["one.txt", "two.txt"], true);
            Assert.Same(origin, vm.ActiveTab);
            long otherMarks = other.Listing.MarkRevision, otherNavigation = other.NavigationGeneration; var otherLocation = other.Location;

            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await Until(() => window.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Compress"));
            window.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == "Compress").IsChecked = true;
            await Until(() => Primary(window, "Compress").IsEnabled);
            if (duringReview)
            {
                // After the actionable milestone the review no longer belongs to the listing or its tab.
                panel.CloseTab(origin);
                await Settle(TimeSpan.FromMilliseconds(250));
                Assert.True(origin.Listing.IsDisposed); Assert.True(vm.Dialogs.IsOpen); Assert.True(Primary(window, "Compress").IsEnabled);
            }
            Primary(window, "Compress").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await running;
            var job = Assert.Single(admitted.Jobs); Assert.Equal(1, vm.StorageOriginCount);
            if (!duringReview) { panel.CloseTab(origin); Assert.True(origin.Listing.IsDisposed); }
            var active = panel.ActiveTab; int otherGeneration = other.Listing.Generation;

            job.Resume();
            await Until(() => job.State.IsFinished() && vm.StorageOriginCount == 0 &&
                other.Listing.Generation != otherGeneration && !other.Listing.IsRefreshing && other.Listing.State == ListingState.Complete);
            Assert.True(job.State == JobState.Completed, Describe(job));
            Assert.All(job.StorageResult!.Roots, row => Assert.Equal(StorageCompleteness.Complete, row.Completeness));
            // The unrelated tab with the same names marked was refreshed, never cleaned or navigated.
            Assert.Equal(2, other.Listing.MarkedCount); Assert.Equal(otherMarks, other.Listing.MarkRevision);
            Assert.Equal(otherLocation, other.Location); Assert.Equal(otherNavigation, other.NavigationGeneration);
            Assert.Same(active, panel.ActiveTab); Assert.DoesNotContain(origin, panel.Tabs);
            string title = Formatters.SafeName(job.Title);
            Assert.Contains(vm.RecentNotifications, item => item.Message.StartsWith(title + ": 0 changed, 2 already satisfied", StringComparison.Ordinal));
            Assert.DoesNotContain(vm.RecentNotifications, item => item.IsError);
            Assert.Equal(0, adapter.Calls);
            await Until(() => adapter.Pins.All(pin => pin.Disposals == 1));
        }
        finally
        {
            ((OverlayDialogService)vm.Dialogs).CancelAll(); if (running is not null) await running;
            CloseForJobs(services, window, root, state);
        }
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
            var disclosure = Assert.Single(window.GetVisualDescendants().OfType<Expander>(),
                item => item.Header as string == "Selected items (2)");
            disclosure.IsExpanded = true;
            var items = Assert.Single(Assert.IsType<StackPanel>(disclosure.Content).Children.OfType<TextBox>(), item => item.IsReadOnly);
            Assert.Equal("Selected items and original locations, select text to copy", AutomationProperties.GetName(items));
            string text = Assert.IsType<string>(items.Text);
            foreach (string path in new[] { @"C:\first\same", @"C:\second\same" })
                Assert.Contains("Original location: " + JsonSerializer.Serialize(path), text);
            tab.Navigate(Location.FileSystem(root));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Location?.Path == root);
            Assert.True(vm.Dialogs.IsOpen); Assert.True(Primary(window, "Compress").IsEnabled);
            Assert.Equal(text, items.Text);
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
            find?.Close(); CloseFindWindows(); AccessibilityTests.Close(services, window, root);
        }
    }

    /// <summary>The headless platform with the native Windows extra-data adapter, so Find can run its observed predicate.</summary>
    private sealed class ExtraDataPlatform : FileCat.Core.Platform.PortablePlatform
    {
        public ExtraDataPlatform() => HiddenData = new FileCat.Platform.Windows.WindowsHiddenData();
    }

    [AvaloniaFact]
    public async Task Find_refuses_storage_while_its_search_session_is_still_running()
    {
        var adapter = new Adapter();
        var previous = FileCat.Core.Platform.PlatformFactory.WindowsFactory;
        if (OperatingSystem.IsWindows()) FileCat.Core.Platform.PlatformFactory.WindowsFactory = () => new ExtraDataPlatform();
        (AppServices Services, MainViewModel Vm, MainWindow Window, string Root) fixture;
        try { fixture = AccessibilityTests.OpenMainWindow(storageTransforms: adapter); }
        finally { FileCat.Core.Platform.PlatformFactory.WindowsFactory = previous; }
        var (services, vm, window, root) = fixture;
        var permits = new List<IDisposable>(); FindWindow? find = null;
        try
        {
            Assert.SkipUnless(services.Platform.HiddenData.IsSupported,
                "The running-search fixture holds the extra-data predicate's observation admission.");
            var ct = TestContext.Current.CancellationToken;
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            string folder = vm.ActiveTab!.Location!.Path;
            // Hold every observation admission for the fixture device: the search thread waits at its first candidate.
            string device = PathUtil.GetDeviceKey(Path.Join(folder, "a.txt"));
            for (int i = 0; i < 4; i++) permits.Add(await DetailsAdmission.AcquireAsync(device, ct));
            vm.Execute(CommandIds.FindFiles); await Until(() => FindWindow.OpenWindows.Count == 1);
            find = FindWindow.OpenWindows.Single();
            var criteria = find.Criteria; criteria.Names = "*.txt";
            criteria.Advanced.CarriesHiddenData = true; criteria.Advanced.HiddenDataScope = HiddenSearch.ScopeId;
            find.Apply(criteria);
            find.StartSearch(RefineMode.Replace);
            Assert.Equal(string.Empty, find.Error);
            await Settle(TimeSpan.FromMilliseconds(600)); // several Find timer ticks while the session cannot finish

            Assert.False(find.Session!.Finished); Assert.False(find.IsIdle); Assert.False(find.StorageResultsSettled);
            var availability = find.StorageCommandAvailability;
            Assert.False(availability.Enabled);
            Assert.Equal("Stop the search or wait for results to finish updating before changing compression.", availability.Reason);
            find.Run(CommandIds.StorageAttributes);
            await Settle(TimeSpan.FromMilliseconds(200));
            Assert.False(find.Dialogs.IsOpen); Assert.Empty(adapter.Pins); Assert.Empty(services.Jobs.Jobs);
            Assert.Contains(vm.RecentNotifications, item => item.Message == availability.Reason);
            var files = find.FindMenu.Items.OfType<MenuItem>().First();
            files.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            var command = Assert.Single(files.Items.OfType<MenuItem>(), item => item.Header as string == "NTFS compression…");
            Assert.False(command.IsEnabled);
            Assert.Equal(availability.Reason, AutomationProperties.GetHelpText(command)); Assert.Equal(availability.Reason, ToolTip.GetTip(command));
            Assert.False(find.Session.Finished);

            foreach (var permit in permits) permit.Dispose();
            permits.Clear();
            // The released session finishes and publishes; only then does the Find-local gate settle.
            await Until(() => find.Session!.Finished && find.StorageResultsSettled);
            Assert.NotEqual(availability.Reason, find.StorageCommandAvailability.Reason);
        }
        finally
        {
            foreach (var permit in permits) permit.Dispose();
            find?.Close(); CloseFindWindows(); AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Find_coalesces_a_burst_of_storage_completions_into_one_refresh_of_its_results()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Find returns real originals; storage capture admits only local Windows paths.");
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        using var admitted = new AdmittedJobs(services.Jobs);
        FindWindow? find = null;
        try
        {
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            find = await OpenSettledFind(vm);
            var results = find.ResultsTab!;
            results.Listing.MarkNames(["a.txt"], true);
            for (int i = 1; i <= 3; i++)
            {
                find.Run(CommandIds.StorageAttributes);
                await ApproveCompression(find);
                await Until(() => admitted.Jobs.Count == i && !find.Dialogs.IsOpen && find.StorageCommandAvailability.Enabled);
            }
            Assert.Equal(3, vm.StorageOriginCount);
            var location = results.Location; var session = find.Session;
            Assert.False(results.Listing.IsRefreshing);
            int generation = results.Listing.Generation;

            // One burst: every terminal publication arrives before the dispatcher runs any posted refresh.
            foreach (var job in admitted.Jobs) job.Cancel();
            Assert.All(admitted.Jobs, job => Assert.Equal(JobState.Canceled, job.State));
            await Until(() => vm.StorageOriginCount == 0 && results.Listing.Generation != generation &&
                !results.Listing.IsRefreshing && results.Listing.State == ListingState.Complete);
            await Settle(TimeSpan.FromMilliseconds(500));
            Assert.Equal(generation + 1, results.Listing.Generation); // exactly one refresh for three completions
            Assert.Same(results, find.ResultsTab); Assert.Equal(location, results.Location); Assert.Same(session, find.Session);
            Assert.Equal(1, results.Listing.MarkedCount); // canceled roots are incomplete and stay marked
            Assert.True(find.StorageCommandAvailability.Enabled);
        }
        finally
        {
            find?.Close(); CloseFindWindows(); AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_Find_cancels_its_storage_review_before_or_after_the_actionable_milestone(bool afterMilestone)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Find returns real originals; storage capture admits only local Windows paths.");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var adapter = new Adapter();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        FindWindow? find = null;
        try
        {
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            var main = vm.ActiveTab!; var mainLocation = main.Location;
            find = await OpenSettledFind(vm);
            find.ResultsTab!.Listing.MarkNames(["a.txt", "b.txt"], true);
            if (!afterMilestone) adapter.Blocking = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); };
            find.Run(CommandIds.StorageAttributes);
            if (afterMilestone)
            {
                await Until(() => find.GetVisualDescendants().OfType<RadioButton>().Any(button => AutomationProperties.GetName(button) == "Compress"));
                find.GetVisualDescendants().OfType<RadioButton>().Single(button => AutomationProperties.GetName(button) == "Compress").IsChecked = true;
                await Until(() => Primary(find, "Compress").IsEnabled);
                Assert.Equal(2, adapter.Pins.Count);
            }
            else await Until(() => entered.IsSet);
            Assert.True(find.Dialogs.IsOpen);

            find.Close();
            await Until(() => FindWindow.OpenWindows.Count == 0);
            if (!afterMilestone)
            {
                Assert.Empty(adapter.Pins); // the started delegate still owns its result
                release.Set();
            }
            await Until(() => adapter.Pins.Count == (afterMilestone ? 2 : 1) && adapter.Pins.All(pin => pin.Disposals == 1));
            await Settle(TimeSpan.FromMilliseconds(200));
            Assert.All(adapter.Pins, pin => Assert.Equal(1, pin.Disposals));
            Assert.Empty(services.Jobs.Jobs); Assert.Equal(0, vm.StorageOriginCount);
            Assert.Same(main, vm.ActiveTab); Assert.Equal(mainLocation, main.Location);
            Assert.DoesNotContain(vm.RecentNotifications, item => item.IsError || item.Message.StartsWith("The selected listing changed", StringComparison.Ordinal));
        }
        finally
        {
            release.Set(); find?.Close(); CloseFindWindows();
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancel_preparation_and_show_operation_selects_the_running_blocker_in_the_owner_window(bool fromFind)
    {
        if (fromFind) Assert.SkipUnless(OperatingSystem.IsWindows(), "Find returns real originals; storage capture admits only local Windows paths.");
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var adapter = new Adapter
        {
            Apply = Adapter.Changed,
            Opening = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException(); },
        };
        var (services, vm, window, root, state) = OpenForJobs(adapter);
        using var admitted = new AdmittedJobs(services.Jobs, pause: false);
        FindWindow? find = null; Task? running = null;
        try
        {
            var tab = vm.ActiveTab!;
            await ShowResults(services, tab, (@"C:\captured", "original"));
            tab.Listing.FocusName("original");
            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await ApproveCompression(window);
            await running;
            var blocker = Assert.Single(admitted.Jobs);
            // The running job owns the storage native allowance until its worker returns.
            await Until(() => entered.IsSet || blocker.State.IsFinished());
            Assert.True(entered.IsSet, Describe(blocker));
            Assert.Same(blocker, StorageReviewResources.NativeBlocker);
            Assert.Equal(JobState.Running, blocker.State);
            int inspected = adapter.Pins.Count;

            Visual host;
            if (fromFind)
            {
                vm.Workspace.Activate(vm.Workspace.Panels[1]); // Find opens on the other panel's local folder
                find = await OpenSettledFind(vm);
                find.ResultsTab!.Listing.MarkNames(["a.txt"], true);
                find.Run(CommandIds.StorageAttributes); host = find;
            }
            else { running = vm.ExecuteAsync(CommandIds.StorageAttributes); host = window; }
            Button Show() => host.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Cancel preparation and show operation");
            await Until(() => host.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Cancel preparation and show operation" && button.IsVisible));
            string waiting = $"Waiting for the storage resource owned by {JsonSerializer.Serialize(blocker.Title)}. Cancel this preparation to show that operation.";
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == waiting);
            Assert.Equal("Cancel preparation and show the blocking operation", AutomationProperties.GetName(Show()));
            vm.Operations.IsOpen = false;

            Show().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (!fromFind) await running;
            await Until(() => vm.Operations.IsOpen && ReferenceEquals(vm.Operations.Selected?.Job, blocker));
            if (fromFind) { await Until(() => !find!.Dialogs.IsOpen); Assert.Contains(find, FindWindow.OpenWindows); }
            else Assert.False(vm.Dialogs.IsOpen);
            // The canceled preparation never inspected a root and never queued a second operation.
            Assert.Equal(inspected, adapter.Pins.Count); Assert.Same(blocker, Assert.Single(admitted.Jobs));
            Assert.Equal(JobState.Running, blocker.State);

            release.Set();
            await Until(() => blocker.State.IsFinished() && vm.StorageOriginCount == 0);
            Assert.Equal(JobState.Completed, blocker.State); Assert.Equal(1, adapter.Calls);
        }
        finally
        {
            release.Set(); ((OverlayDialogService)vm.Dialogs).CancelAll();
            if (running is not null) await running;
            find?.Close(); CloseFindWindows(); CloseForJobs(services, window, root, state);
        }
        Assert.Null(StorageReviewResources.NativeBlocker);
    }

    [AvaloniaTheory]
    [InlineData("refresh")]
    [InlineData("navigation")]
    [InlineData("closed")]
    public async Task Initial_listing_invalidation_closes_review_and_drains_a_late_native_pin_without_submitting(string change)
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var adapter = new Adapter { Blocking = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); } };
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: adapter);
        Task? running = null;
        try
        {
            var panel = vm.Workspace.Panels[0];
            var set = services.ResultSets.Create("Original selection", "fixture"); set.FullFolders = true;
            set.Add(new(Location.FileSystem(@"C:\captured"), "original", EntryKind.File), ""); set.IsComplete = true;
            var tab = panel.OpenTab(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.TotalCount == 1);
            tab.Listing.FocusName("original");
            Assert.Same(tab, vm.ActiveTab);
            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await Until(() => entered.IsSet);
            if (change == "refresh") tab.Refresh();
            else if (change == "navigation") tab.Navigate(Location.FileSystem(root));
            else panel.CloseTab(tab);
            await Until(() => !vm.Dialogs.IsOpen);
            Assert.False(running.IsCompleted); Assert.Empty(services.Jobs.Jobs);
            release.Set(); await running;
            Assert.Equal(1, Assert.Single(adapter.Pins).Disposals);
            Assert.Empty(services.Jobs.Jobs); Assert.Equal(0, vm.StorageOriginCount);
            const string changed = "The selected listing changed while checking. Nothing was changed.";
            var announcements = vm.RecentNotifications.Where(item => item.Message == changed).ToList();
            if (change == "refresh")
            {
                // The origin is still current: announce once and never restart automatically.
                Assert.Equal(1, Assert.Single(announcements).Occurrences);
                Assert.Equal(changed, vm.CurrentNotification?.Message);
            }
            else Assert.Empty(announcements); // a navigated or closed origin is no longer the listing being shown
            Assert.DoesNotContain(vm.RecentNotifications, item => item.IsError);
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
    public async Task Exit_during_an_admitted_storage_call_waits_for_that_call_and_starts_no_other()
    {
        var adapter = new Adapter();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        adapter.Apply = before =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(30)), "The test did not release the admitted call.");
            return Adapter.Changed(before);
        };
        var (services, vm, window, root, state) = OpenForJobs(adapter);
        using var admitted = new AdmittedJobs(services.Jobs, pause: false);
        Task? running = null;
        try
        {
            var tab = vm.ActiveTab!;
            await ShowResults(services, tab, (@"C:\first", "one.txt"), (@"C:\second", "two.txt"));
            tab.Listing.MarkNames(["one.txt", "two.txt"], true);
            running = vm.ExecuteAsync(CommandIds.StorageAttributes);
            await ApproveCompression(window);
            await running;
            var job = Assert.Single(admitted.Jobs);
            await Until(() => entered.IsSet);
            var exit = vm.ConfirmExitWithActiveWorkAsync(reviewAnalyses: false);
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == "Stop them and exit"));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains(job.Title, StringComparison.Ordinal) == true);
            Primary(window, "Stop them and exit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => job.Token.IsCancellationRequested);
            Assert.False(exit.IsCompleted); // the admitted call still owns the job and its journal
            release.Set();
            await Until(() => exit.IsCompleted);
            Assert.True(await exit, "Exit proceeds once the admitted call returned and the job retired.");
            Assert.True(job.State == JobState.Canceled, Describe(job));
            Assert.Equal(1, adapter.Calls);
            var result = Assert.IsType<StorageResultSnapshot>(job.StorageResult);
            Assert.Equal((1L, 1L), (result.Counts.Changed, result.Counts.NotProcessed));
            Assert.False(result.ReviewPending);
            Assert.False(services.Jobs.HasActiveWork);
        }
        finally { release.Set(); CloseForJobs(services, window, root, state); }
    }

    [AvaloniaFact]
    public async Task Interrupted_operation_actions_refuse_storage_history_without_touching_its_journal()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: new Adapter());
        try
        {
            string leaf = StorageJournalPolicy.FileName(DateTime.UtcNow, Guid.NewGuid());
            string directory = Directory.CreateDirectory(services.Paths.JournalDirectory).FullName, path = Path.Join(directory, leaf);
            byte[] bytes = "storage history the generic recovery actions must not touch"u8.ToArray();
            File.WriteAllBytes(path, bytes);
            var candidate = services.Jobs.StorageHistoryCandidate(leaf);
            Assert.True(candidate.IsStorageTransform);
            Assert.False(await vm.CleanupInterruptedAsync(candidate));
            Assert.False(await vm.MarkInterruptedReviewedAsync(candidate));
            Assert.Contains(vm.RecentNotifications, n => n.Message == "Use Review storage changes. Storage history cannot authorize partial-file cleanup.");
            Assert.Contains(vm.RecentNotifications, n => n.Message == "Use Review storage changes to inspect and acknowledge the exact retained history.");
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal([leaf], Directory.GetFileSystemEntries(directory).Select(Path.GetFileName).Where(name => name!.Contains(leaf[..20], StringComparison.Ordinal)));
            Assert.False(vm.Dialogs.IsOpen);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task The_default_composition_offers_compression_and_EFS_only_where_qualified()
    {
        // The headless test application registers no Windows platform (App.OnFrameworkInitializationCompleted does that
        // for the real app), so compose the shipped one here; otherwise this would only ever see the portable platform.
        var previousFactory = FileCat.Core.Platform.PlatformFactory.WindowsFactory;
        FileCat.Core.Platform.PlatformFactory.WindowsFactory = () => new FileCat.Platform.Windows.WindowsPlatform();
        AppServices services; MainViewModel vm; MainWindow window; string root;
        try { (services, vm, window, root) = AccessibilityTests.OpenMainWindow(); }
        finally { FileCat.Core.Platform.PlatformFactory.WindowsFactory = previousFactory; }
        try
        {
            await Until(() => vm.ActiveTab!.Listing.State == ListingState.Complete);
            vm.ActiveTab!.Listing.SetFocus(1);
            var availability = vm.GetAvailability(CommandIds.StorageAttributes);
            bool qualified = OperatingSystem.IsWindows() && FileCat.Platform.Windows.WindowsStorageReviewEvidence.IsSupportedHost;
            Assert.True(qualified == availability.Enabled, $"Qualified host {qualified}, command enabled {availability.Enabled}: {availability.Reason}");
            if (OperatingSystem.IsWindows())
                Assert.Equal(qualified, services.Platform.StorageTransforms is not FileCat.Core.FileSystem.NoStorageTransforms);
            if (!qualified) Assert.Contains("NTFS", availability.Reason);
            // EFS joins the released composition where the EFS edition gate also passes (Windows 11 Pro, Enterprise or
            // Education); everywhere else neither EFS command is advertised or available.
            bool efs = qualified && FileCat.Platform.Windows.WindowsStorageTransforms.IsEfsEdition;
            var protection = vm.GetAvailability(CommandIds.EfsProtection);
            var backup = vm.GetAvailability(CommandIds.EfsFileBackup);
            Assert.Equal(efs, vm.AdvertisesEfsCommand(CommandIds.EfsProtection));
            Assert.Equal(efs, vm.AdvertisesEfsCommand(CommandIds.EfsFileBackup));
            Assert.True(efs == protection.Enabled, $"EFS host {efs}, protection enabled {protection.Enabled}: {protection.Reason}");
            Assert.True(efs == backup.Enabled, $"EFS host {efs}, key backup enabled {backup.Enabled}: {backup.Reason}");
            if (qualified && !efs) Assert.Equal(FileCat.Platform.Windows.WindowsStorageTransforms.EfsEditionReason, protection.Reason);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Unsupported_build_keeps_compression_visible_with_the_same_reason_in_find_and_the_palette()
    {
        // An unavailable composition: an unqualified host, an elevated process or a non-Windows platform.
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: new NoStorageTransforms());
        FindWindow? find = null;
        var listItems = new List<MenuItem>();
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
            var availability = vm.GetAvailability(definition.Id);
            Assert.False(availability.Enabled);
            Assert.Contains("NTFS", availability.Reason);
            var palette = Assert.Single(vm.CommandSearchEntries(), entry => entry.Id == definition.Id);
            Assert.False(palette.Enabled);
            Assert.Equal(availability.Reason, palette.Reason);
            await vm.ExecuteAsync(definition.Id);
            Assert.False(vm.Dialogs.IsOpen);

            vm.Execute(CommandIds.FindFiles);
            await Until(() => FindWindow.OpenWindows.Count == 1);
            find = FindWindow.OpenWindows.Single();
            find.NamesBox.Text = "*.txt";
            find.StartSearch(RefineMode.Replace);
            await Until(() => find.StorageResultsSettled);
            var findFiles = find.FindMenu.Items.OfType<MenuItem>().First();
            findFiles.RaiseEvent(new RoutedEventArgs(MenuItem.SubmenuOpenedEvent));
            var findItem = Assert.Single(findFiles.Items.OfType<MenuItem>(), item => item.Header as string == definition.Title);
            Assert.False(findItem.IsEnabled);
            Assert.Equal(availability.Reason, AutomationProperties.GetHelpText(findItem));
            // The disabled reason is shown to pointer users too, not only to assistive technology.
            Assert.Equal(availability.Reason, ToolTip.GetTip(findItem)); Assert.True(ToolTip.GetShowOnDisabled(findItem));

            // The results list's own context menu forwards the same reason (its ShowListMenu path, not the menu bar's).
            using (AutomationProperties.HelpTextProperty.Changed.AddClassHandler<MenuItem>((item, _) => listItems.Add(item)))
                find.ShowContextMenu();
            var listItem = Assert.Single(listItems, item => item.Header as string == definition.Title);
            Assert.NotSame(findItem, listItem);
            Assert.False(listItem.IsEnabled);
            Assert.Equal(availability.Reason, AutomationProperties.GetHelpText(listItem));
            Assert.Equal(availability.Reason, ToolTip.GetTip(listItem)); Assert.True(ToolTip.GetShowOnDisabled(listItem));
            var menu = Assert.IsType<ContextMenu>(listItem.FindLogicalAncestorOfType<ContextMenu>());
            var entries = menu.Items.OfType<MenuItem>().Select(item => item.Header as string).ToList();
            int at = entries.IndexOf(definition.Title);
            Assert.Equal(services.Commands.Get(CommandIds.Checksum)!.Title, entries[at - 1]);
            Assert.Equal(services.Commands.Get(CommandIds.Pack)!.Title, entries[at + 1]);
            menu.Close();
            find.Run(definition.Id);
            Assert.False(find.Dialogs.IsOpen);
            Assert.Empty(services.Jobs.Jobs);
        }
        finally
        {
            foreach (var menu in listItems.Select(item => item.FindLogicalAncestorOfType<ContextMenu>()).OfType<ContextMenu>().Distinct()) menu.Close();
            find?.Close(); CloseFindWindows(); AccessibilityTests.Close(services, window, root);
        }
    }

    private sealed record CommandSurfaces(List<string> Primary, List<string> More, List<string> Toolbar, List<string> ToolbarMore,
        List<string> KeyBar, List<string> Inspect, string Checksum, string Verify);

    private static async Task<CommandSurfaces> CaptureSurfaces(IStorageTransforms? storage)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(storageTransforms: storage);
        try
        {
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3);
            Assert.True(listing.FocusName("a.txt"));
            static string Header(Control control) => control is MenuItem item ? item.Header as string ?? "?" : "-";
            var context = ContextMenuFactory.Build(vm).Items.OfType<Control>().ToList();
            var more = Assert.Single(context.OfType<MenuItem>(), item => item.Header as string == "More FileCat actions");
            var toolbar = window.Toolbar.GetVisualDescendants().OfType<Button>().Select(button => button.Tag as string ?? "?").ToList();
            var toolbarMore = (window.ToolbarMore.ContextMenu?.Items.OfType<MenuItem>() ?? []).SelectMany(section =>
                section.Items.OfType<Control>().Select(item => item is MenuItem { Tag: string id } ? section.Header + "/" + id : section.Header + "/" + Header(item))).ToList();
            var keys = new List<string>();
            foreach (var mods in new[] { KeyMods.None, KeyMods.Shift, KeyMods.Ctrl, KeyMods.Alt, KeyMods.Ctrl | KeyMods.Shift, KeyMods.Alt | KeyMods.Shift, KeyMods.Ctrl | KeyMods.Alt })
            {
                vm.UpdateKeyBar(mods);
                keys.AddRange(vm.KeyBar.Select(key => $"{mods}/F{key.Number}/{key.CommandId}/{key.Label}"));
            }
            vm.UpdateKeyBar(KeyMods.None);
            var file = window.MainMenu.Items.OfType<MenuItem>().Single(menu => menu.Header as string == "_File");
            var inspect = file.Items.OfType<MenuItem>().Single(menu => menu.Header as string == "_Inspect");
            return new([.. context.Select(Header)], [.. more.Items.OfType<Control>().Select(Header)], toolbar, toolbarMore, keys,
                [.. inspect.Items.OfType<MenuItem>().Select(item => item.Tag as string ?? "?")],
                services.Commands.Get(CommandIds.Checksum)!.Title, services.Commands.Get(CommandIds.VerifyChecksums)!.Title);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Compression_adds_one_more_actions_entry_without_changing_primary_menus_toolbar_or_key_bar()
    {
        var unsupported = await CaptureSurfaces(null);
        var adapter = new Adapter();
        var enabled = await CaptureSurfaces(adapter);
        Assert.True(adapter.AvailabilityCalls > 0);
        const string title = "NTFS compression…";

        // Primary item rows are identical, so their count is unchanged. No Attributes row or one-command submenu appears.
        Assert.Equal(unsupported.Primary, enabled.Primary);
        Assert.DoesNotContain(title, enabled.Primary);
        // The inapplicable build omits the item entry; the enabled build appends exactly one entry to More FileCat actions,
        // after the checksum and verification entries.
        Assert.DoesNotContain(title, unsupported.More);
        Assert.Equal(title, enabled.More[^1]);
        Assert.Equal(unsupported.More, enabled.More.Take(enabled.More.Count - 1));
        Assert.Single(enabled.More, entry => entry == title);
        int checksum = enabled.More.IndexOf(enabled.Checksum);
        Assert.InRange(checksum, 0, enabled.More.Count - 2);
        Assert.True(enabled.More.IndexOf(enabled.Verify) < enabled.More.Count - 1, string.Join(" | ", enabled.More));
        // No toolbar, toolbar overflow or key bar control was added for either build.
        Assert.Equal(unsupported.Toolbar, enabled.Toolbar); Assert.DoesNotContain(CommandIds.StorageAttributes, enabled.Toolbar);
        Assert.Equal(unsupported.ToolbarMore, enabled.ToolbarMore);
        Assert.DoesNotContain(enabled.ToolbarMore, entry => entry.EndsWith("/" + CommandIds.StorageAttributes, StringComparison.Ordinal));
        Assert.Equal(unsupported.KeyBar, enabled.KeyBar);
        Assert.DoesNotContain(enabled.KeyBar, key => key.Contains("/" + CommandIds.StorageAttributes + "/", StringComparison.Ordinal));
        // File → Inspect: the five existing entries gain one, directly after Change attributes and times. EFS stays hidden in C.
        string[] inspect = [CommandIds.Checksum, CommandIds.VerifyChecksums, CommandIds.Attributes, CommandIds.StorageAttributes,
            CommandIds.HiddenData, CommandIds.FileRecord];
        Assert.Equal(inspect, unsupported.Inspect); Assert.Equal(inspect, enabled.Inspect);
    }
}
