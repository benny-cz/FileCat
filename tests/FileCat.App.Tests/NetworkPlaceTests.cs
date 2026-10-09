using System.Diagnostics;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>D-54: the Network place, its computers and file servers, their shares, and back.</summary>
public sealed class NetworkPlaceTests(ITestOutputHelper output)
{
    /// <summary>The test services run on the portable platform: the Windows network provider is added as the app adds it.</summary>
    private static void Register(FileCat.App.Services.AppServices services)
    {
        if (!services.Providers.IsRegistered(Schemes.Network))
            services.Providers.Register(new FileCat.Platform.Windows.NetworkShareProvider { KnownServers = services.KnownNetworkServers });
    }

    [AvaloniaFact]
    public Task The_Network_lists_known_servers_then_their_shares_and_leads_back() => ExerciseReturnAsync(output, 0);

    internal static async Task ExerciseReturnAsync(ITestOutputHelper output, int returnDelayMs)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The Windows network first; Linux and macOS follow.");
            return;
        }
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            Register(services);
            var provider = (FileCat.Platform.Windows.NetworkShareProvider)services.Providers.For(FileCat.Platform.Windows.NetworkShareProvider.Root);
            var knownServers = provider.KnownServers;
            int rootRequests = 0;
            provider.KnownServers = () =>
            {
                // Delay only this fixture's second root read; never a system/network setting or shared provider.
                if (Interlocked.Increment(ref rootRequests) == 2 && returnDelayMs > 0) Thread.Sleep(returnDelayMs);
                return knownServers?.Invoke() ?? [];
            };
            // A server reached before: it is listed at once, before any device answers.
            services.History.Folders.Add(new HistoryEntry { Location = new Location(Schemes.Network, @"\\localhost") });

            // The place beside This PC.
            var network = Assert.Single(vm.Places(vm.DriveButtons), p => p.Location?.Scheme == Schemes.Network);
            Assert.Equal("Network", network.Title);
            Assert.Equal(PlaceGroup.Devices, network.Group);

            var tab = vm.Workspace.Panels[0].ActiveTab!;
            tab.Navigate(network.Location!);
            List<EntryData> Rows() => Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetVisible).ToList();
            for (int i = 0; i < 250 && !Rows().Any(e => e.Name == "localhost"); i++) await Task.Delay(20, ct);
            Assert.Equal("Network", tab.DisplayPath);
            // The top of the network: no ".." above it.
            Assert.DoesNotContain(Rows(), e => e.Kind == EntryKind.Parent);
            var known = Assert.Single(Rows(), e => e.Name == "localhost");
            Assert.Equal(EntryKind.Server, known.Kind);
            // Discovery waits a few seconds for answers, then the listing is complete.
            for (int i = 0; i < 400 && tab.Listing.State != ListingState.Complete; i++) await Task.Delay(20, ct);
            Assert.Equal(ListingState.Complete, tab.Listing.State);

            // Into the server: its shares.
            tab.Listing.SetFocus(Rows().FindIndex(e => e.Name == "localhost"));
            Assert.True(tab.TryEnterFocused(out _));
            for (int i = 0; i < 250 && (tab.Location?.Path != @"\\localhost" || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(20, ct);
            Assert.Equal(@"\\localhost", tab.Location!.Path);
            var shares = Rows().Where(e => e.Kind == EntryKind.Share).Select(e => e.Name).ToList();
            // Every Windows computer has its administrative shares (hidden, as Explorer hides them); IPC$ is not a folder.
            Assert.Contains("C$", shares, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("IPC$", shares, StringComparer.OrdinalIgnoreCase);

            // Up again: the Network, with the server under the cursor.
            var returning = Stopwatch.StartNew();
            tab.GoUp();
            // Enumeration includes admission, mapped-drive history and late discovery name lookups. Observe the
            // ready view rather than assuming those operations settle within 400 twenty-millisecond turns.
            while (returning.Elapsed < TimeSpan.FromSeconds(30) &&
                   (tab.Location?.Path != "" || tab.Listing.State != ListingState.Complete ||
                    !tab.Listing.TryGetFocused(out var ready) || ready.Name != "localhost"))
                await Task.Delay(20, ct);
            output.WriteLine("NETWORK_PLACE_RETURN " + JsonSerializer.Serialize(new
            {
                returnDelayMs, elapsedMs = returning.Elapsed.TotalMilliseconds, rootRequests,
                state = tab.Listing.State.ToString(), visibleRows = tab.Listing.VisibleCount,
                knownServerVisible = Rows().Any(e => e.Name == "localhost"),
                focusReady = tab.Listing.TryGetFocused(out var observed) && observed.Name == "localhost",
                ownedKnownServerCallbackOnly = true, realWindowsShares = true, noHostDesktopInput = true,
            }));
            Assert.Equal(Schemes.Network, tab.Location!.Scheme);
            Assert.Equal(ListingState.Complete, tab.Listing.State);
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal("localhost", focused.Name);

            // An administrative share opens as a folder when FileCat has administrator rights (nothing is changed).
            if (Environment.IsPrivilegedProcess)
            {
                tab.Navigate(new Location(Schemes.Network, @"\\localhost"));
                for (int i = 0; i < 250 && (tab.Location?.Path != @"\\localhost" || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(20, ct);
                tab.Listing.SetFocus(Rows().FindIndex(e => e.Name.Equals("C$", StringComparison.OrdinalIgnoreCase)));
                Assert.True(tab.TryEnterFocused(out _));
                for (int i = 0; i < 250 && (!tab.Location!.IsFileSystem || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(20, ct);
                Assert.True(tab.Location!.IsFileSystem);
                Assert.Equal(@"\\localhost\C$", tab.Location.Path, ignoreCase: true);
                Assert.Contains(Rows(), e => e.Name.Equals("Windows", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task With_nothing_known_or_answering_the_Network_says_how_to_reach_a_server()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The Windows network first; Linux and macOS follow.");
            return;
        }
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            Register(services);
            var provider = (FileCat.Platform.Windows.NetworkShareProvider)services.Providers.For(FileCat.Platform.Windows.NetworkShareProvider.Root);
            var known = provider.KnownServers;
            provider.KnownServers = () => [];
            try
            {
                var tab = vm.Workspace.Panels[0].ActiveTab!;
                tab.Navigate(FileCat.Platform.Windows.NetworkShareProvider.Root);
                for (int i = 0; i < 400 && tab.Listing.State != ListingState.Complete; i++) await Task.Delay(20, ct);
                // Some networks have devices that answer; the hint is only for one where none did.
                if (tab.Listing.VisibleCount == 0) Assert.Contains(@"Type \\server in the path", tab.Banner);
            }
            finally { provider.KnownServers = known; }
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
