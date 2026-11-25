using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class DeniedParentNavigationTests
{
    private sealed class DeniedFolderProvider : ResourceProvider
    {
        private int _enumerations;
        public int Enumerations => Volatile.Read(ref _enumerations);
        public override string Scheme => "denied-parent-test";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) =>
            location.Path == "blocked" ? new Location(Scheme, "parent") : null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            Interlocked.Increment(ref _enumerations);
            return location.Path.StartsWith("blocked", StringComparison.Ordinal)
                ? Task.FromException(new UnauthorizedAccessException("Owned denied folder"))
                : Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task Open_parent_row_escapes_a_denied_folder_instead_of_retrying_it()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var provider = new DeniedFolderProvider();
            services.Providers.Register(provider);
            var tab = vm.Workspace.ActiveTab!;
            foreach (string command in new[] { CommandIds.Open, CommandIds.Enter, CommandIds.OpenWithSystem })
            {
                tab.Navigate(new Location(provider.Scheme, "blocked"));
                await Until(() => tab.Listing.State == ListingState.Failed);
                Assert.Equal("blocked", tab.Location?.Path);
                Assert.True(tab.Listing.TryGetFocused(out var row));
                Assert.Equal(EntryKind.Parent, row.Kind);

                await vm.ExecuteAsync(command); // Open is the list's double-click action.
                await Until(() => tab.Location?.Path == "parent" && tab.Listing.State == ListingState.Complete);
                Assert.Equal("parent", tab.Location?.Path);
            }

            tab.Navigate(new Location(provider.Scheme, "blocked-root"));
            await Until(() => tab.Listing.State == ListingState.Failed);
            Assert.False(tab.Listing.TryGetFocused(out _));
            int attempts = provider.Enumerations;
            await vm.ExecuteAsync(CommandIds.Open);
            await Until(() => provider.Enumerations > attempts);
            Assert.Equal("blocked-root", tab.Location?.Path); // Without "..", Open still retries.
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>A share that lists only once someone signed in, as Windows reports it (I672).</summary>
    private sealed class CredentialsProvider : ResourceProvider
    {
        private int _signedIn;
        public int Enumerations;
        public const string Share = @"\\server\share";
        public void SignIn() => Volatile.Write(ref _signedIn, 1);
        public override string Scheme => Schemes.Network;
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) =>
            location.Path == Share ? new Location(Scheme, @"\\server") : null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            Interlocked.Increment(ref Enumerations);
            return location.Path != Share || Volatile.Read(ref _signedIn) == 1
                ? Task.CompletedTask
                : Task.FromException(new UnauthorizedAccessException(Share + " requires credentials (Access is denied.). Press Enter to sign in."));
        }
    }

    /// <summary>Windows' credential prompt, answered at once: records the server and lets the share list.</summary>
    private sealed class SignInShell(CredentialsProvider provider) : PortableShellServices
    {
        public List<string> SignIns { get; } = [];

        public override string? SignIn(string server, nint owner)
        {
            lock (SignIns) SignIns.Add(server);
            provider.SignIn();
            return null;
        }
    }

    private sealed class ShellPlatform : PortablePlatform
    {
        public ShellPlatform(IShellServices shell) => Shell = shell;
    }

    [AvaloniaFact]
    public async Task Enter_on_a_share_that_needs_credentials_signs_in_and_Backspace_still_goes_up()
    {
        var provider = new CredentialsProvider();
        var shell = new SignInShell(provider);
        var previous = PlatformFactory.WindowsFactory;
        AppServices services;
        MainViewModel vm;
        Views.MainWindow window;
        string root;
        try
        {
            if (OperatingSystem.IsWindows()) PlatformFactory.WindowsFactory = () => new ShellPlatform(shell);
            (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        }
        finally { PlatformFactory.WindowsFactory = previous; }
        try
        {
            services.Providers.Register(provider);
            var tab = vm.Workspace.ActiveTab!;

            // Backspace leaves the share without asking for credentials.
            tab.Navigate(new Location(provider.Scheme, CredentialsProvider.Share));
            await Until(() => tab.Listing.State == ListingState.Failed);
            await vm.ExecuteAsync(CommandIds.Parent);
            await Until(() => tab.Location?.Path == @"\\server" && tab.Listing.State == ListingState.Complete);
            Assert.Empty(shell.SignIns);

            tab.Navigate(new Location(provider.Scheme, CredentialsProvider.Share));
            await Until(() => tab.Listing.State == ListingState.Failed);
            Assert.Contains("Press Enter to sign in", tab.Listing.Error);
            Assert.True(tab.Listing.TryGetFocused(out var row));
            Assert.Equal(EntryKind.Parent, row.Kind); // The failed share's only row.
            await vm.ExecuteAsync(CommandIds.Open); // The Enter key (Ctrl+PgDn's "Enter folder" still goes up).
            if (OperatingSystem.IsWindows())
            {
                // Enter does what the share's notice says: Windows' prompt for the share, then the share is read again.
                await Until(() => tab.Listing.State == ListingState.Complete);
                Assert.Equal(CredentialsProvider.Share, tab.Location?.Path);
                Assert.Equal([CredentialsProvider.Share], shell.SignIns);
            }
            else
            {
                // FileCat asks for SMB credentials in its own dialog there; the parent row keeps its escape (I572).
                await Until(() => tab.Location?.Path == @"\\server" && tab.Listing.State == ListingState.Complete);
                Assert.Empty(shell.SignIns);
            }
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
