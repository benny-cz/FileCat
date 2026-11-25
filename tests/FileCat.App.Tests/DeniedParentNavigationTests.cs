using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
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

    /// <summary>
    /// The file system as Windows' provider reports it, but for one share whose server refuses the logon until someone
    /// signed in: I672's share case, as c9's Samba share answers.
    /// </summary>
    private sealed class LogonRefusingFileSystem : LocalFileSystemProvider
    {
        // On this computer, so that what FileCat asks of the share outside the listing (its folder watcher, free space)
        // fails at once instead of waiting seconds for a name lookup: no such share exists.
        public const string Server = @"\\localhost";
        public const string Share = Server + @"\fc-logon-refusing-share";
        private int _signedIn;
        public void SignIn() => Volatile.Write(ref _signedIn, 1);
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            if (!location.Path.StartsWith(Share, StringComparison.OrdinalIgnoreCase))
                return base.EnumerateAsync(location, sink, ct);
            return Volatile.Read(ref _signedIn) == 1
                ? Task.CompletedTask
                : Task.FromException(new FileCat.Platform.Windows.NetworkAuthenticationRequiredException(Share, 1326));
        }
    }

    /// <summary>Windows' credential prompt, answered at once: records the server or share and lets it list.</summary>
    private sealed class SignInShell(Action signIn) : PortableShellServices
    {
        public List<string> SignIns { get; } = [];

        public override string? SignIn(string server, nint owner)
        {
            lock (SignIns) SignIns.Add(server);
            signIn();
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
        var shell = new SignInShell(provider.SignIn);
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

    [AvaloniaFact]
    public async Task Enter_in_a_share_whose_server_refused_the_logon_signs_in_to_the_share()
    {
        // The first test's share is the Network's; a share opened as a folder (the share, or a folder in it) fails in the
        // file system, where Windows refused the logon with its own words and the parent row went up on rc.4 (c9, I672).
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows' sign-in for a share's folders (UNC paths)."); return; }
        var files = new LogonRefusingFileSystem();
        var shell = new SignInShell(files.SignIn);
        var previous = PlatformFactory.WindowsFactory;
        AppServices services;
        MainViewModel vm;
        Views.MainWindow window;
        string root;
        try
        {
            PlatformFactory.WindowsFactory = () => new ShellPlatform(shell);
            (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        }
        finally { PlatformFactory.WindowsFactory = previous; }
        try
        {
            services.Providers.Register(files);
            services.Providers.Register(new CredentialsProvider()); // the share's server lists without a sign-in
            var tab = vm.Workspace.ActiveTab!;
            string sub = LogonRefusingFileSystem.Share + @"\sub";

            // Backspace leaves the share for its server without asking for credentials.
            tab.Navigate(Location.FileSystem(LogonRefusingFileSystem.Share));
            await Until(() => tab.Listing.State == ListingState.Failed);
            Assert.Contains("requires credentials", tab.Listing.Error);
            Assert.Contains("Press Enter to sign in", tab.Listing.Error);
            await vm.ExecuteAsync(CommandIds.Parent);
            await Until(() => tab.Location?.Path == LogonRefusingFileSystem.Server && tab.Listing.State == ListingState.Complete);
            Assert.Empty(shell.SignIns);

            // Enter on a folder of the share, whose only row is "..": Windows' prompt for the share (the connection is the
            // share's, not the folder's), then the folder is read again where it is.
            tab.Navigate(Location.FileSystem(sub));
            await Until(() => tab.Listing.State == ListingState.Failed);
            Assert.True(tab.Listing.TryGetFocused(out var row));
            Assert.Equal(EntryKind.Parent, row.Kind);
            await vm.ExecuteAsync(CommandIds.Open);
            await Until(() => tab.Listing.State == ListingState.Complete);
            Assert.Equal(sub, tab.Location?.Path);
            Assert.Equal([LogonRefusingFileSystem.Share], shell.SignIns);
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
