using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;
using FileCat.Remote.Tests;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class NavigationIntentTests
{
    private sealed class HeldProvider(string scheme = "held-navigation-test") : ResourceProvider
    {
        private readonly TaskCompletionSource<Location> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Scheme => scheme;
        public CancellationToken Token { get; private set; }
        public Exception? ImmediateError { get; set; }
        public override string GetDisplayPath(Location location) => "Held destination " + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task<Location>? PrepareAsync(Location location, CancellationToken ct)
        {
            if (ImmediateError is { } error) throw error;
            Token = ct;
            return _ready.Task;
        }
        public void Complete(Location location) => _ready.TrySetResult(location);
        public void Fail(Exception error) => _ready.TrySetException(error);
    }

    private sealed class HeldConnector(Func<ISftpChannel>? connected = null) : ISftpConnector, IDisposable
    {
        private readonly ManualResetEventSlim _release = new();
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            Token = ct;
            Entered.TrySetResult(true);
            _release.Wait(TimeSpan.FromSeconds(10)); // deliberately ignore cancellation to test late completion
            return connected?.Invoke() ?? throw new IOException("Late connection failure");
        }
        public void Release() => _release.Set();
        public void Dispose() { _release.Set(); _release.Dispose(); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 250 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private static async Task Drain()
    {
        for (int i = 0; i < 15; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Late_preparation_success_cannot_replace_newer_location_marks_focus_or_history()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HeldProvider();
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            string newer = Directory.CreateDirectory(Path.Combine(root, "newer")).FullName;
            string obsolete = Directory.CreateDirectory(Path.Combine(root, "obsolete")).FullName;
            File.WriteAllText(Path.Combine(newer, "stay.txt"), "stay");
            tab.Navigate(new Location(provider.Scheme, "A"));
            long firstIntent = tab.NavigationGeneration;
            tab.Navigate(Location.FileSystem(newer), "stay.txt");
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Location == Location.FileSystem(newer));
            int store = Enumerable.Range(0, tab.Listing.Store.Count).Single(i => tab.Listing.Store[i].Name == "stay.txt");
            tab.Listing.SelectStoreMark(store);
            Assert.True(tab.Listing.FocusName("stay.txt"));
            var history = tab.BackHistory.ToArray();
            string? banner = tab.Banner;
            Assert.True(provider.Token.IsCancellationRequested);
            provider.Complete(Location.FileSystem(obsolete));
            await Drain();
            Assert.Equal(firstIntent + 1, tab.NavigationGeneration);
            Assert.Equal(Location.FileSystem(newer), tab.Location);
            Assert.Equal(history, tab.BackHistory);
            Assert.Equal(banner, tab.Banner);
            Assert.True(tab.Listing.IsMarked(store));
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal("stay.txt", focused.Name);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Late_failure_after_back_or_explicit_cancel_cannot_replace_the_current_banner()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HeldProvider();
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            string next = Directory.CreateDirectory(Path.Combine(root, "next")).FullName;
            tab.Navigate(Location.FileSystem(next));
            await Until(() => tab.Location == Location.FileSystem(next));
            tab.Navigate(new Location(provider.Scheme, "A"));
            tab.GoBack();
            Assert.True(provider.Token.IsCancellationRequested);
            Assert.NotEqual(Location.FileSystem(next), tab.Location);
            var history = tab.ForwardHistory.ToArray();
            string? banner = tab.Banner;
            provider.Fail(new IOException("Obsolete error"));
            await Drain();
            Assert.Equal(history, tab.ForwardHistory);
            Assert.Equal(banner, tab.Banner);
            Assert.DoesNotContain("Obsolete", tab.Banner ?? string.Empty);

        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_second_pending_destination_supersedes_the_first_before_either_commits()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var first = new HeldProvider("held-navigation-first");
            var second = new HeldProvider("held-navigation-second");
            services.Providers.Register(first);
            services.Providers.Register(second);
            var tab = vm.ActiveTab!;
            string obsolete = Directory.CreateDirectory(Path.Combine(root, "obsolete")).FullName;
            string wanted = Directory.CreateDirectory(Path.Combine(root, "wanted")).FullName;
            int history = tab.BackHistory.Count;
            tab.Navigate(new Location(first.Scheme, "A"));
            tab.Navigate(new Location(second.Scheme, "B"));
            Assert.True(first.Token.IsCancellationRequested);
            second.Complete(Location.FileSystem(wanted));
            await Until(() => tab.Location == Location.FileSystem(wanted));
            first.Complete(Location.FileSystem(obsolete));
            await Drain();
            Assert.Equal(Location.FileSystem(wanted), tab.Location);
            Assert.Equal(history + 1, tab.BackHistory.Count);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Forward_navigation_supersedes_an_unfinished_preparation()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HeldProvider();
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            string middle = Directory.CreateDirectory(Path.Combine(root, "middle")).FullName;
            string forward = Directory.CreateDirectory(Path.Combine(root, "forward")).FullName;
            string obsolete = Directory.CreateDirectory(Path.Combine(root, "obsolete")).FullName;
            tab.Navigate(Location.FileSystem(middle));
            tab.Navigate(Location.FileSystem(forward));
            tab.GoBack();
            Assert.Equal(Location.FileSystem(middle), tab.Location);
            tab.Navigate(new Location(provider.Scheme, "A"));
            tab.GoForward();
            Assert.True(provider.Token.IsCancellationRequested);
            provider.Complete(Location.FileSystem(obsolete));
            await Drain();
            Assert.Equal(Location.FileSystem(forward), tab.Location);
            Assert.Contains(Location.FileSystem(middle), tab.BackHistory);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Explicit_cancel_restores_the_prior_banner_and_invalidates_the_pending_result()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HeldProvider();
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            var original = tab.Location;
            string? originalBanner = tab.Banner;
            tab.Navigate(new Location(provider.Scheme, "A"));
            Assert.Contains("Opening", tab.Banner);
            window.Activate();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.True(provider.Token.IsCancellationRequested);
            Assert.Equal(originalBanner, tab.Banner);
            Assert.False(tab.CancelPendingNavigation());
            provider.Complete(Location.FileSystem(Directory.CreateDirectory(Path.Combine(root, "obsolete")).FullName));
            await Drain();
            Assert.Equal(original, tab.Location);
            Assert.Equal(originalBanner, tab.Banner);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Current_preparation_commits_once_with_original_focus_and_history()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HeldProvider();
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            var before = tab.Location;
            int history = tab.BackHistory.Count;
            string ready = Directory.CreateDirectory(Path.Combine(root, "ready")).FullName;
            File.WriteAllText(Path.Combine(ready, "focus.txt"), "focus");
            tab.Navigate(new Location(provider.Scheme, "A"), "focus.txt");
            long intent = tab.NavigationGeneration;
            provider.Complete(Location.FileSystem(ready));
            await Until(() => tab.Location == Location.FileSystem(ready) && tab.Listing.State == ListingState.Complete);
            Assert.Equal(intent, tab.NavigationGeneration);
            Assert.Equal(history + 1, tab.BackHistory.Count);
            Assert.Equal(before, tab.BackHistory[^1]);
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal("focus.txt", focused.Name);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public void A_current_synchronous_preparation_error_keeps_the_listing_and_explains_the_failure()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new HeldProvider { ImmediateError = new IOException("Mount failed") };
            services.Providers.Register(provider);
            var tab = vm.ActiveTab!;
            var original = tab.Location;
            var history = tab.BackHistory.ToArray();
            tab.Navigate(new Location(provider.Scheme, "A"));
            Assert.Equal(original, tab.Location);
            Assert.Equal(history, tab.BackHistory);
            Assert.Contains("Mount failed", tab.Banner);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task Closing_tab_rejects_late_preparation_and_connection_errors()
    {
        using var connector = new HeldConnector();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(connector);
        try
        {
            var provider = new HeldProvider();
            services.Providers.Register(provider);
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            var original = tab.Location;
            tab.Navigate(new Location(provider.Scheme, "A"));
            string? banner = tab.Banner;
            panel.OpenTab(Location.FileSystem(root));
            panel.CloseTab(tab);
            provider.Fail(new IOException("Late mount error"));
            await Drain();
            Assert.Equal(original, tab.Location);
            Assert.Equal(banner, tab.Banner);
            Assert.True(provider.Token.IsCancellationRequested);

            var remote = panel.ActiveTab!;
            Assert.True(services.Providers.TryParse("sftp://user@files.example/~", null, out var address));
            remote.Navigate(address!);
            await connector.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            string local = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            remote.Navigate(Location.FileSystem(local));
            Assert.True(connector.Token.IsCancellationRequested);
            connector.Release();
            await Drain();
            Assert.Equal(Location.FileSystem(local), remote.Location);
            Assert.DoesNotContain("Late connection failure", remote.Banner ?? string.Empty);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task A_late_successful_sftp_connection_cannot_navigate_away_from_a_newer_folder()
    {
        var server = new FakeSftpServer();
        using var connector = new HeldConnector(() => new FakeChannel(server));
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(connector);
        try
        {
            var tab = vm.ActiveTab!;
            Assert.True(services.Providers.TryParse("sftp://user@files.example/~", null, out var address));
            tab.Navigate(address!);
            await connector.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            string newer = Directory.CreateDirectory(Path.Combine(root, "newer")).FullName;
            tab.Navigate(Location.FileSystem(newer));
            var history = tab.BackHistory.ToArray();
            Assert.True(connector.Token.IsCancellationRequested);
            connector.Release();
            await Drain();
            Assert.Equal(Location.FileSystem(newer), tab.Location);
            Assert.Equal(history, tab.BackHistory);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
