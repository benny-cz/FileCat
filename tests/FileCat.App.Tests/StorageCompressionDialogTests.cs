using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class StorageCompressionDialogTests
{
    private static ItemRef Item(string folder = @"C:\original", bool directory = false) =>
        new(Location.FileSystem(folder), "same\nname", directory ? EntryKind.Directory : EntryKind.File);
    private sealed class Pin(StorageTarget target) : IStorageReviewLease
    {
        public StorageTarget Target => target;
        public StorageObservation Observation { get; set; } = new(StorageObservationState.Disabled, StorageObservationState.Disabled,
            new(1, Guid.NewGuid().ToString("N").ToUpperInvariant()) { VolumeBinding = "11111111-1111-1111-1111-111111111111" },
            target.Source.Kind == EntryKind.Directory, null);
        public int Disposed;
        public bool IsAlive => Disposed == 0;
        public void Dispose() => Disposed++;
    }
    private sealed class Adapter : IStorageTransforms, IStorageEfsJobAdapter
    {
        public readonly List<Pin> Pins = [];
        public Action? Blocking;
        public bool Cached = true;
        public int Opens, Refreshes, MaxActive;
        private int _active;
        public string? Missing;
        public StorageAction? OpenedAction;
        /// <summary>Action-specific facts for a pinned root, as a native adapter reports its eligibility reason.</summary>
        public Func<Pin, StorageAction?, StorageObservation>? For;
        public StorageProblem? Unavailable(StorageAction action) => null;
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        {
            Opens++; OpenedAction = action; Block();
            if (target.Path == Missing) throw new FileNotFoundException("Unavailable selected root");
            var pin = new Pin(target); if (For is { } observe) pin.Observation = observe(pin, action);
            Pins.Add(pin); return pin;
        }
        public StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action) => Cached ? Observe(root, action) : null;
        public StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct)
        { Refreshes++; Block(); return Observe(root, action); }
        private StorageObservation Observe(IStorageReviewLease root, StorageAction? action) => For?.Invoke((Pin)root, action) ?? root.Observation;
        private void Block()
        {
            int active = Interlocked.Increment(ref _active);
            try
            {
                int max;
                while (active > (max = Volatile.Read(ref MaxActive)) && Interlocked.CompareExchange(ref MaxActive, active, max) != max) { }
                Blocking?.Invoke();
            }
            finally { Interlocked.Decrement(ref _active); }
        }
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) => throw new NotSupportedException();
        public StorageProblem? ScopeUnavailable(StorageScope scope) => null;
        public IStorageTraversal OpenReviewedEfsRoot(IStorageReviewLease root, Job job, int sourceIndex,
            StorageEfsRecoverySession recovery, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Host : IDisposable
    {
        public readonly DeviceIoScheduler Io = new();
        public readonly Grid Overlay = new();
        public readonly Window Window;
        public readonly OverlayDialogService Dialogs;
        public Host()
        {
            var fallback = new TextBox();
            Window = new Window { Content = new Grid { Children = { fallback, Overlay } }, Width = 640, Height = 420 };
            Dialogs = new(Overlay, () => fallback); Window.Show();
        }
        public Button Button(string label) => Assert.Single(Overlay.GetVisualDescendants().OfType<Button>(), button => button is not RadioButton && button.Content as string == label);
        public void ChooseAction(int index) => Scope(index == 1 ? "Compress" : "Remove compression").IsChecked = true;
        public RadioButton Scope(string name) => Assert.Single(Overlay.GetVisualDescendants().OfType<RadioButton>(), radio => AutomationProperties.GetName(radio) == name);
        public CheckBox Consent(string phrase) => Assert.Single(Overlay.GetVisualDescendants().OfType<CheckBox>(),
            box => box.IsVisible && AutomationProperties.GetName(box)?.Contains(phrase, StringComparison.Ordinal) == true);
        public void Dispose() { Dialogs.CancelAll(); Io.Dispose(); Window.Close(); }
    }
    private static async Task Until(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!ready()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(10, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Efs_consent_is_explicit_and_clears_on_action_scope_or_inspection_changes(bool decrypt)
    {
        using var host = new Host(); var adapter = new Adapter { Cached = false };
        using var review = new StorageSelectionReview(adapter, [Item(directory: true), Item(@"C:\other")]);
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [false, true], efs: true);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => host.Overlay.GetVisualDescendants().OfType<RadioButton>()
                .Any(button => AutomationProperties.GetName(button) == "Encrypt with EFS"));
            Assert.False(form.Ready);
            Assert.Equal(0, adapter.Opens);
            Assert.False(host.Button("Choose action").IsEnabled);
            Assert.True(host.Button("Cancel").IsDefault);
            host.Scope("Encrypt with EFS").IsChecked = true;
            await Until(() => form.Ready);
            Assert.Equal(StorageAction.EncryptEfs, adapter.OpenedAction);
            var path = host.Consent("concurrent link-change");
            var streams = host.Consent("remove compression from all data streams");
            Assert.False(path.IsChecked);
            Assert.False(streams.IsChecked);
            path.IsChecked = true;
            Assert.False(host.Button("Encrypt with EFS").IsEnabled);
            streams.IsChecked = true;
            Assert.True(host.Button("Encrypt with EFS").IsEnabled);
            Assert.False(host.Button("Encrypt with EFS").IsDefault);

            if (decrypt)
            {
                host.Scope("Decrypt").IsChecked = true;
                Assert.False(path.IsChecked);
                Assert.False(streams.IsVisible);
                var protection = host.Consent("selected files will lose EFS protection");
                Assert.Contains("Existing contents stay unchanged", AutomationProperties.GetName(protection));
                Assert.False(protection.IsChecked);
                path.IsChecked = true;
                protection.IsChecked = true;
                host.Scope("Selected folders and their existing contents").IsChecked = true;
                Assert.False(path.IsChecked);
                Assert.False(protection.IsChecked);
                Assert.Contains("processed files inside them", AutomationProperties.GetName(protection));
                path.IsChecked = true;
                Assert.False(host.Button("Queue").IsEnabled);
                protection.IsChecked = true;
            }
            else
            {
                host.Button("Retry inspection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.False(path.IsChecked);
                Assert.False(streams.IsChecked);
                path.IsChecked = true;
                streams.IsChecked = true;
            }

            string action = decrypt ? "Decrypt" : "Encrypt with EFS";
            await Until(() => host.Button(action).IsEnabled && host.Button("Queue").IsEnabled);
            host.Button(decrypt ? "Queue" : action).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var choice = Assert.IsType<StorageChangeChoice>(await showing);
            var consent = Assert.IsType<StorageEfsConsent>(choice.Approval.EfsConsent);
            Assert.Equal(choice.Roots.Revision, consent.ReviewRevision);
            Assert.Equal(decrypt ? StorageAction.DecryptEfs : StorageAction.EncryptEfs, consent.Action);
            Assert.Equal(decrypt ? StorageScope.ExistingDescendants : StorageScope.SelectedItems, consent.Scope);
            Assert.Equal(decrypt ? QueueMode.Queue : QueueMode.Start, choice.Mode);
            Assert.Equal(new StorageEfsAcknowledgments(true, !decrypt), consent.Acknowledgments);
            Assert.Equal<IEnumerable<StorageObjectIdentity>>(adapter.Pins.Select(pin => pin.Observation.Identity), consent.RootIdentities);
            Assert.Equal(2, adapter.Opens);
            Assert.All(adapter.Pins, pin => Assert.True(pin.IsAlive));
        }
        finally { host.Dialogs.CancelAll(); await showing; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Immediate_submission_freezes_the_displayed_action_scope_and_all_original_locations(bool queue)
    {
        using var host = new Host(); var adapter = new Adapter();
        using var review = new StorageSelectionReview(adapter, [Item(directory: true), Item(@"C:\other")]);
        using var initial = new CancellationTokenSource(); int detached = 0;
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [true, false], initial.Token, () => detached++);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => form.Ready);
            Assert.Equal(1, detached); initial.Cancel();
            Assert.False(showing.IsCompleted);
            Assert.False(host.Button("Choose action").IsEnabled);
            Assert.Single(host.Overlay.GetVisualDescendants().OfType<Expander>()).IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            var text = Assert.Single(host.Overlay.GetVisualDescendants().OfType<TextBox>(), box => box.IsReadOnly);
            Assert.Contains("C:\\\\original", text.Text); Assert.Contains("C:\\\\other", text.Text);
            Assert.Contains("same\\nname", text.Text); Assert.Contains("Hidden mark: Yes", text.Text);
            Assert.Contains("Provider: Local filesystem", text.Text);
            var search = Assert.Single(host.Overlay.GetVisualDescendants().OfType<TextBox>(), box => AutomationProperties.GetName(box) == "Search selected items by name or original location");
            search.Text = "other";
            await Until(() => text.Text?.Contains("C:\\\\original", StringComparison.Ordinal) == false);
            Assert.DoesNotContain("C:\\\\original", text.Text); Assert.Contains("C:\\\\other", text.Text);
            search.Text = "";
            Assert.Equal(2, adapter.Opens);
            Assert.Equal("Selected items only", AutomationProperties.GetName(Assert.Single(host.Overlay.GetVisualDescendants().OfType<RadioButton>(), r => r.IsChecked == true)));
            host.ChooseAction(1);
            Assert.True(host.Button("Compress").IsEnabled);
            host.Scope("Selected folders and their existing contents").IsChecked = true;
            host.ChooseAction(2);
            Assert.True(host.Button("Remove compression").IsEnabled);
            if (queue) host.Button("Queue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            using var choice = Assert.IsType<StorageChangeChoice>(await showing.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(StorageAction.RemoveCompression, choice.Approval.Action);
            Assert.Equal(StorageScope.ExistingDescendants, choice.Approval.Scope);
            Assert.Equal(queue ? QueueMode.Queue : QueueMode.Start, choice.Mode);
            Assert.Equal(2, choice.Roots.Sources.Length); Assert.Equal(2, adapter.Opens); Assert.Equal(0, adapter.Refreshes);
            Assert.All(adapter.Pins, pin => Assert.Equal(0, pin.Disposed));
            choice.Dispose(); Assert.All(adapter.Pins, pin => Assert.Equal(1, pin.Disposed));
        }
        finally { host.Dialogs.CancelAll(); await showing; }
    }

    [AvaloniaFact]
    public async Task Cancellation_during_initial_native_work_drains_before_retiring_the_late_pin()
    {
        using var host = new Host(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var adapter = new Adapter { Blocking = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); } };
        using var review = new StorageSelectionReview(adapter, [Item()]); using var initial = new CancellationTokenSource();
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [false], initial.Token);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => entered.IsSet); initial.Cancel(); Dispatcher.UIThread.RunJobs();
            Assert.False(showing.IsCompleted); Assert.False(form.Ready);
            release.Set(); Assert.Null(await showing);
            Assert.Equal(1, Assert.Single(adapter.Pins).Disposed);
        }
        finally { release.Set(); host.Dialogs.CancelAll(); await showing; }
    }

    [AvaloniaFact]
    public async Task Changes_during_reinspection_replace_pending_work_and_keep_the_successful_original_pin()
    {
        using var host = new Host(); var adapter = new Adapter();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var review = new StorageSelectionReview(adapter, [Item(directory: true)]);
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [false]);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => form.Ready);
            adapter.Cached = false;
            adapter.Blocking = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException(); };
            host.ChooseAction(1); await Until(() => entered.IsSet);
            for (int i = 0; i < 30; i++) host.ChooseAction(i % 2 + 1);
            host.ChooseAction(2);
            host.Scope("Selected folders and their existing contents").IsChecked = true;
            Assert.False(host.Button("Remove compression").IsEnabled);
            adapter.Blocking = null; release.Set();
            await Until(() => host.Button("Remove compression").IsEnabled);
            host.Button("Remove compression").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var choice = Assert.IsType<StorageChangeChoice>(await showing);
            Assert.Equal(StorageAction.RemoveCompression, choice.Approval.Action); Assert.Equal(StorageScope.ExistingDescendants, choice.Approval.Scope);
            Assert.Equal(1, adapter.Opens); Assert.Equal(2, adapter.Refreshes);
            Assert.Equal(0, Assert.Single(adapter.Pins).Disposed);
        }
        finally { release.Set(); host.Dialogs.CancelAll(); await showing; }
    }

    [AvaloniaFact]
    public async Task A_refused_root_expands_complete_disclosure_and_retry_preserves_successful_siblings()
    {
        using var host = new Host(); var adapter = new Adapter { Missing = @"C:\missing\same" + "\nname" };
        using var review = new StorageSelectionReview(adapter, [Item(), Item(@"C:\missing")]);
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [false, true]);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => form.Ready);
            Assert.True(Assert.Single(host.Overlay.GetVisualDescendants().OfType<Expander>()).IsExpanded);
            host.ChooseAction(1);
            await Until(() => form.PendingWork?.IsCompleted == true);
            Assert.False(host.Button("Compress").IsEnabled); Assert.Single(adapter.Pins);
            adapter.Missing = null;
            host.Button("Retry inspection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => host.Button("Compress").IsEnabled);
            Assert.Equal(2, adapter.Pins.Count); Assert.All(adapter.Pins, pin => Assert.Equal(0, pin.Disposed));
            host.Dialogs.CancelAll(); Assert.Null(await showing);
        }
        finally { host.Dialogs.CancelAll(); await showing; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_resource_wait_can_only_navigate_to_the_actual_typed_job_owner(bool jobOwned)
    {
        using var host = new Host(); var adapter = new Adapter();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var owner = new Job(new JobRequest { Kind = JobKind.StorageTransform, Sources = [Item()] }, "Existing storage operation", "fixture", [], []);
        var holding = Task.Run(() =>
        {
            using var native = StorageReviewResources.EnterNative(TestContext.Current.CancellationToken, jobOwned ? owner : null);
            entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        });
        await Until(() => entered.IsSet);
        using var review = new StorageSelectionReview(adapter, [Item()]);
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [false]);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            var show = form.Body.Children.OfType<Button>().Single(button => button.Content as string == "Cancel preparation and show operation");
            await Until(() => show.IsVisible == jobOwned);
            if (jobOwned) show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); else host.Dialogs.CancelAll();
            Assert.Null(await showing.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(jobOwned ? owner : null, form.RequestedOperation); Assert.Equal(0, adapter.Opens);
        }
        finally { release.Set(); host.Dialogs.CancelAll(); await showing; await holding; }
        Assert.Null(StorageReviewResources.NativeBlocker);
    }

    private static string[] VisibleTexts(Host host) => host.Overlay.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty).ToArray();

    [AvaloniaFact]
    public async Task Retry_while_root_inspection_is_blocked_keeps_one_check_in_flight_and_only_the_latest_request()
    {
        using var host = new Host(); var adapter = new Adapter();
        // Native calls 1 and 2 (initial root pinning) and 4 (a later pinned-root refresh) block on their own gates.
        var gates = new[] { 1, 2, 4 }.ToDictionary(call => call, _ => (Entered: new ManualResetEventSlim(), Release: new ManualResetEventSlim()));
        int calls = 0;
        adapter.Blocking = () =>
        {
            if (!gates.TryGetValue(Interlocked.Increment(ref calls), out var gate)) return;
            gate.Entered.Set();
            if (!gate.Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
        };
        using var review = new StorageSelectionReview(adapter, [Item(), Item(@"C:\other")]);
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [false, false]);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => gates[1].Entered.IsSet);
            var work = Assert.IsAssignableFrom<Task>(form.PendingWork);
            var retry = host.Button("Retry inspection");
            host.ChooseAction(1);
            for (int i = 0; i < 20; i++) retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            host.ChooseAction(2);
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            // Every request replaced only the pending latest revision: no second native check started.
            Assert.Equal(1, adapter.Opens); Assert.Equal(1, adapter.MaxActive); Assert.Same(work, form.PendingWork);
            Assert.False(form.Ready); Assert.False(host.Button("Remove compression").IsEnabled); Assert.False(host.Button("Queue").IsEnabled);
            Assert.Contains("Checking selected items: 0 of 2…", VisibleTexts(host));

            gates[1].Release.Set();
            await Until(() => gates[2].Entered.IsSet);
            // The superseded completion returned: it neither adopted nor published its pin, and it enabled nothing.
            Assert.Equal(1, Assert.Single(adapter.Pins).Disposed);
            Assert.Equal(2, adapter.Opens); Assert.Equal(1, adapter.MaxActive); Assert.Same(work, form.PendingWork);
            Assert.False(form.Ready); Assert.False(host.Button("Remove compression").IsEnabled);

            gates[2].Release.Set();
            await Until(() => host.Button("Remove compression").IsEnabled);
            Assert.True(form.Ready);
            Assert.Equal(3, adapter.Opens); Assert.Equal(new[] { 1, 0, 0 }, adapter.Pins.Select(pin => pin.Disposed));

            // After the milestone, Retry refreshes the adopted pins. Retries during that blocked native refresh are replaced
            // the same way: the old completion cannot republish, enable or dispose the review's pins, and nothing re-pins.
            retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => gates[4].Entered.IsSet);
            var refreshing = form.PendingWork;
            for (int i = 0; i < 5; i++) retry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(host.Button("Remove compression").IsEnabled); Assert.True(form.Ready);
            Assert.Equal(1, adapter.Refreshes); Assert.Same(refreshing, form.PendingWork);
            gates[4].Release.Set();
            await Until(() => host.Button("Remove compression").IsEnabled);
            Assert.Equal(3, adapter.Opens); Assert.Equal(3, adapter.Refreshes); Assert.Equal(1, adapter.MaxActive);
            Assert.Equal(new[] { 1, 0, 0 }, adapter.Pins.Select(pin => pin.Disposed));

            host.Button("Remove compression").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            using var choice = Assert.IsType<StorageChangeChoice>(await showing);
            Assert.Equal(StorageAction.RemoveCompression, choice.Approval.Action);
            // The approval is bound to the pins adopted by the latest check, never to the superseded pin.
            Assert.Equal<IEnumerable<StorageObjectIdentity>>(adapter.Pins.Skip(1).Select(pin => pin.Observation.Identity),
                choice.Roots.Roots.Select(root => root.Observation.Identity));
            choice.Dispose();
            Assert.Equal(new[] { 1, 1, 1 }, adapter.Pins.Select(pin => pin.Disposed));
        }
        finally
        {
            foreach (var gate in gates.Values) gate.Release.Set();
            host.Dialogs.CancelAll(); await showing;
            foreach (var gate in gates.Values) { gate.Entered.Dispose(); gate.Release.Dispose(); }
        }
    }

    [AvaloniaTheory]
    [InlineData("read-only")]
    [InlineData("efs")]
    [InlineData("stale")]
    public async Task Stale_and_capability_refused_roots_expand_the_disclosure_with_their_reason_and_block_submission(string refusal)
    {
        using var host = new Host(); var adapter = new Adapter();
        var problem = refusal switch
        {
            "read-only" => new StorageProblem(StorageReasonKind.PermissionOrPolicy, "read-only-file", "The file is read-only; its attributes will not be cleared.", true),
            "efs" => new StorageProblem(StorageReasonKind.FileCatBoundary, "efs-conflict", "This item is encrypted with EFS. Compression cannot remove that protection.", false),
            _ => null,
        };
        // Like the native adapter, the refusal depends on the requested action: removal needs no change on these roots.
        if (problem is not null)
            adapter.For = (pin, action) => pin.Target.Path.StartsWith(@"C:\other", StringComparison.Ordinal)
                ? pin.Observation with { Efs = refusal == "efs" ? StorageObservationState.Enabled : StorageObservationState.Disabled,
                    Ineligible = action == StorageAction.Compress ? problem : null }
                : pin.Observation with { Ineligible = null };
        using var review = new StorageSelectionReview(adapter, [Item(), Item(@"C:\other")]);
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [false, true]);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => form.Ready);
            var disclosure = Assert.Single(host.Overlay.GetVisualDescendants().OfType<Expander>());
            Assert.False(disclosure.IsExpanded);
            var items = Assert.Single(Assert.IsType<StackPanel>(disclosure.Content).Children.OfType<TextBox>(), box => box.IsReadOnly);
            if (refusal == "stale")
            {
                // The captured path now names a different object: the next native check of the original pin finds the replacement.
                adapter.Cached = false;
                var replaced = adapter.Pins[1];
                replaced.Observation = replaced.Observation with { Identity = new(1, Guid.NewGuid().ToString("N").ToUpperInvariant()) { VolumeBinding = "11111111-1111-1111-1111-111111111111" } };
            }
            host.ChooseAction(1);
            await Until(() => form.PendingWork?.IsCompleted != false && !review.State.NeedsInspection);
            string reason = problem?.Message ?? "The original lifetime binding changed. Close this review and select the item again.";
            Assert.True(disclosure.IsExpanded);
            Assert.Contains("Some selected items cannot use this action. Review Selected items below, retry inspection, or cancel and change the selection.", VisibleTexts(host));
            string[] rows = items.Text!.Split("\n\n");
            Assert.Equal(2, rows.Length);
            Assert.Contains("Eligibility: \"Inspection complete\"", rows[0]);
            Assert.Contains("Original location: \"C:\\\\other\\\\same\\nname\"", rows[1]);
            Assert.Contains("Hidden mark: Yes", rows[1]);
            Assert.Contains("Eligibility: " + System.Text.Json.JsonSerializer.Serialize(reason), rows[1]);
            Assert.False(host.Button("Compress").IsEnabled); Assert.False(host.Button("Queue").IsEnabled);
            host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(showing.IsCompleted);

            host.ChooseAction(2);
            await Until(() => form.PendingWork?.IsCompleted != false && !review.State.NeedsInspection);
            if (refusal == "stale")
            {
                // A lost binding is sticky across actions and Retry; the review never retargets or re-pins the captured path.
                Assert.False(host.Button("Remove compression").IsEnabled);
                host.Button("Retry inspection").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Until(() => form.PendingWork?.IsCompleted != false && !review.State.NeedsInspection);
                Assert.False(host.Button("Remove compression").IsEnabled);
                Assert.Contains("Eligibility: " + System.Text.Json.JsonSerializer.Serialize(reason), items.Text);
                Assert.Equal(2, adapter.Opens);
                host.Dialogs.CancelAll(); Assert.Null(await showing);
            }
            else
            {
                // Removal is eligible for the same captured roots, so the action-specific refusal no longer blocks.
                await Until(() => host.Button("Remove compression").IsEnabled);
                Assert.DoesNotContain("Eligibility: " + System.Text.Json.JsonSerializer.Serialize(reason), items.Text);
                Assert.Equal(2, adapter.Opens); Assert.Equal(0, adapter.Refreshes);
                host.Dialogs.CancelAll(); Assert.Null(await showing);
            }
            Assert.All(adapter.Pins, pin => Assert.Equal(1, pin.Disposed));
        }
        finally { host.Dialogs.CancelAll(); await showing; }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Compression_review_has_fixed_wording_and_exactly_two_actions_without_efs_controls(bool folders)
    {
        using var host = new Host(); var adapter = new Adapter();
        using var review = new StorageSelectionReview(adapter, [Item(directory: folders), Item(@"C:\other")]);
        using var form = new StorageChangeDialog(review, host.Io, "fixture", [true, false]);
        var showing = form.ShowAsync(host.Dialogs);
        try
        {
            await Until(() => form.Ready);
            var texts = VisibleTexts(host);
            Assert.Contains("NTFS compression…", texts);
            Assert.Contains("2 selected items. Includes 1 marked items hidden by the filter.", texts);
            Assert.Contains("Compression: 0 enabled, 2 disabled, 0 unknown or unreadable, 0 unsupported.", texts);
            Assert.Contains("Current state of selected items. Folder contents have not been inspected.", texts);
            Assert.Contains("Choose an explicit action for every selected item.", texts);
            Assert.Contains("Choose an action to continue.", texts);
            Assert.Contains("Changes the main file data or folder default. Alternate data streams keep their own compression settings.", texts);
            Assert.Contains("Storage changes run separately from other tracked file operations. Later queued operations may wait while this change is queued, paused, or waiting for your decision.", texts);
            const string selectedOnly = "Changes the selected folder's default for newly created content. Existing files and subfolders are not converted.";
            const string recursive = "Includes hidden items. Links and unsupported items are left unchanged and reported. Files added or moved while this runs may be missed.";
            Assert.Equal(folders, texts.Contains(selectedOnly));
            // Two usable actions; scope choices exist only with a folder. No EFS action, consent or wording in the C form.
            string[] radios = [.. host.Overlay.GetVisualDescendants().OfType<RadioButton>().Select(radio => AutomationProperties.GetName(radio) ?? "")];
            Assert.Equal(folders ? ["Compress", "Remove compression", "Selected items only", "Selected folders and their existing contents"]
                : ["Compress", "Remove compression"], radios);
            Assert.Empty(host.Overlay.GetVisualDescendants().OfType<CheckBox>());
            Assert.DoesNotContain(texts, text => text.Contains("EFS", StringComparison.Ordinal) || text.Contains("ncrypt", StringComparison.OrdinalIgnoreCase));
            // Labeled actions only; the generic dialog shell's own icon controls are not part of this form.
            string[] buttons = [.. host.Overlay.GetVisualDescendants().OfType<Button>()
                .Where(button => button.TemplatedParent is null && button is not (ToggleButton or RepeatButton) && button.IsEffectivelyVisible)
                .Select(button => button.Content as string).OfType<string>()];
            Assert.Equal(["Cancel", "Choose action", "Queue", "Retry inspection"], buttons.Order());
            Assert.True(host.Button("Cancel").IsCancel);
            var primary = host.Button("Choose action");
            Assert.True(primary.IsDefault); Assert.False(primary.IsEnabled);

            host.ChooseAction(1);
            texts = VisibleTexts(host);
            Assert.Contains("NTFS compression may reduce disk space used. Results depend on the file and performance may change.", texts);
            Assert.Contains("Every selected item passed inspection for this action.", texts);
            Assert.Same(primary, host.Button("Compress"));
            Assert.True(primary.IsEnabled); Assert.Equal("Compress", AutomationProperties.GetName(primary));
            Assert.Equal("Selected items only. Changes main data or folder defaults. Alternate data streams keep their compression settings.",
                AutomationProperties.GetHelpText(primary));
            host.ChooseAction(2);
            Assert.Contains("Removing compression can require additional free space.", VisibleTexts(host));
            Assert.Equal("Remove compression", primary.Content); Assert.True(primary.IsEnabled);
            if (folders)
            {
                host.Scope("Selected folders and their existing contents").IsChecked = true;
                Assert.Contains(recursive, VisibleTexts(host)); Assert.DoesNotContain(selectedOnly, VisibleTexts(host));
                Assert.StartsWith("Selected folders and their existing contents. ", AutomationProperties.GetHelpText(primary));
            }
            Assert.DoesNotContain(VisibleTexts(host), text => text.Contains("EFS", StringComparison.Ordinal));
            // Presentation changes use the retained facts: no further native work for wording or availability.
            Assert.Equal(2, adapter.Opens); Assert.Equal(0, adapter.Refreshes);
            host.Dialogs.CancelAll(); Assert.Null(await showing);
            Assert.All(adapter.Pins, pin => Assert.Equal(1, pin.Disposed));
        }
        finally { host.Dialogs.CancelAll(); await showing; }
    }
}
