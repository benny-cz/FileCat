using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
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

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 300 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
