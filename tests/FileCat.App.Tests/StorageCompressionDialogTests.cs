using Avalonia.Automation;
using Avalonia.Controls;
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
            new(1, target.Path), target.Source.Kind == EntryKind.Directory, null);
        public int Disposed;
        public bool IsAlive => Disposed == 0;
        public void Dispose() => Disposed++;
    }
    private sealed class Adapter : IStorageTransforms
    {
        public readonly List<Pin> Pins = [];
        public Action? Blocking;
        public bool Cached = true;
        public int Opens, Refreshes;
        public string? Missing;
        public StorageProblem? Unavailable(StorageAction action) => null;
        public IStorageReviewLease InspectRoot(StorageTarget target, StorageAction? action, CancellationToken ct)
        {
            Opens++; Blocking?.Invoke();
            if (target.Path == Missing) throw new FileNotFoundException("Unavailable selected root");
            var pin = new Pin(target); Pins.Add(pin); return pin;
        }
        public StorageObservation? EvaluatePinnedRoot(IStorageReviewLease root, StorageAction? action) => Cached ? root.Observation : null;
        public StorageObservation InspectPinnedRoot(IStorageReviewLease root, StorageAction? action, CancellationToken ct)
        { Refreshes++; Blocking?.Invoke(); return root.Observation; }
        public IStorageTraversal OpenReviewedRoot(IStorageReviewLease root, StorageAction action, CancellationToken ct) => throw new NotSupportedException();
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
    public async Task Immediate_submission_freezes_the_displayed_action_scope_and_all_original_locations(bool queue)
    {
        using var host = new Host(); var adapter = new Adapter();
        using var review = new StorageSelectionReview(adapter, [Item(directory: true), Item(@"C:\other")]);
        using var initial = new CancellationTokenSource(); int detached = 0;
        using var form = new StorageCompressionDialog(review, host.Io, "fixture", [true, false], initial.Token, () => detached++);
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
            using var choice = Assert.IsType<StorageCompressionChoice>(await showing.WaitAsync(TimeSpan.FromSeconds(10)));
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
        using var form = new StorageCompressionDialog(review, host.Io, "fixture", [false], initial.Token);
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
        using var form = new StorageCompressionDialog(review, host.Io, "fixture", [false]);
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
            using var choice = Assert.IsType<StorageCompressionChoice>(await showing);
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
        using var form = new StorageCompressionDialog(review, host.Io, "fixture", [false, true]);
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
        using var form = new StorageCompressionDialog(review, host.Io, "fixture", [false]);
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
}
