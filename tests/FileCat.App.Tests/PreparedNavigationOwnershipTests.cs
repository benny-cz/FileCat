using Avalonia.Headless.XUnit;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Fixture = FileCat.App.Tests.ArchiveCreationWorkflowTests.Fixture;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class PreparedNavigationOwnershipTests
{
    private sealed class Lifetime : IDisposable
    {
        internal int Disposals;
        public void Dispose() => Disposals++;
    }
    private sealed class Held : ResourceProvider
    {
        internal readonly TaskCompletionSource<PreparedLocation> Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override string Scheme => "prepared-lifetime";
        public override string GetDisplayPath(Location location) => "Prepared lifetime";
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task<PreparedLocation>? PrepareNavigationAsync(Location location, CancellationToken ct) => Ready.Task;
    }

    [AvaloniaTheory, InlineData(0), InlineData(1), InlineData(2), InlineData(3)]
    public async Task Prepared_runtime_resources_live_until_navigation_replacement_close_or_abandonment(int mode)
    {
        using var f = await Fixture.Open(); var provider = new Held(); f.Services.Providers.Register(provider);
        var first = f.Vm.ActiveTab!;
        var tab = mode == 3 ? first.Panel.OpenTab(Location.FileSystem(f.Files)) : first;
        var destination = new Location(provider.Scheme, "owned"); var lifetime = new Lifetime();
        tab.Navigate(destination);
        if (mode == 1) tab.Navigate(Location.FileSystem(f.Files));
        if (mode == 2) Assert.True(tab.CancelPendingNavigation());
        if (mode == 3) tab.Panel.CloseTab(tab);
        provider.Ready.SetResult(new(destination, lifetime));
        if (mode == 0)
        {
            await Wait(() => tab.Location == destination && tab.Listing.State == ListingState.Complete);
            Assert.Equal(0, lifetime.Disposals);
            tab.Navigate(Location.FileSystem(f.Files));
        }
        await Wait(() => lifetime.Disposals == 1);
        Assert.Equal(Location.FileSystem(f.Files), first.Location);
        f.Services.Providers.Register(new Held()); // Disposal of the tab cannot call the completed preparation again.
        Assert.Equal(1, lifetime.Disposals);
    }

    private static async Task Wait(Func<bool> ready)
    {
        for (int i = 0; i < 250 && !ready(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(ready());
    }
}
