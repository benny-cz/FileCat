using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.Core.Content;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;
using ResourceProvider = FileCat.Core.Resources.ResourceProvider;

namespace FileCat.App.Tests;

public sealed class PanelPreviewReadinessTests(ITestOutputHelper output)
{
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo Load = typeof(PanelRetirementLifetimeTests).GetMethod("Load", BindingFlags.Static | BindingFlags.NonPublic)!;

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_panel_retirement_checkpoint_waits_for_an_already_running_preview(bool binary)
    {
        var (services, vm, main, root) = AccessibilityTests.OpenMainWindow();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        byte[] known = binary ? Enumerable.Range(0, 32768).Select(i => (byte)(19 + i * 47)).ToArray() : "Owned current preview\n"u8.ToArray();
        int opens = 0;
        var pane = new QuickViewPane();
        var preview = new Window { Content = pane, Width = 600, Height = 400 };
        Task? first = null, second = null;
        try
        {
            services.Providers.Register(new Provider(() =>
            {
                Interlocked.Increment(ref opens); entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Owned preview release missing.");
                return new MemoryContentSource("known", known);
            }, known.Length));
            var tab = vm.ActiveTab!;
            tab.Navigate(new Location("panelreadinessfixture", "owned"));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.FocusName("known"));
            pane.Attach(tab); preview.Show();
            ((DispatcherTimer)typeof(QuickViewPane).GetField("_debounce", Fields)!.GetValue(pane)!).Stop();
            first = (Task)Load.Invoke(null, [pane])!;
            await Until(() => entered.IsSet);
            Assert.False(first.IsCompleted);
            Assert.Null(typeof(QuickViewPane).GetField("_reader", Fields)!.GetValue(pane));
            second = (Task)Load.Invoke(null, [pane])!;
            bool returnedWithoutReader = second.IsCompleted;
            release.Set();
            await first; await second;
            var reader = Assert.IsType<PagedReader>(typeof(QuickViewPane).GetField("_reader", Fields)!.GetValue(pane));
            byte[] actual = new byte[known.Length];
            bool exact = reader.Read(0, actual) == actual.Length && actual.SequenceEqual(known);
            output.WriteLine("PANEL_PREVIEW_READINESS " + JsonSerializer.Serialize(new
            {
                binary, returnedWithoutReader, opens, exact, knownBytes = known.Length,
                controlledExistingRequestAtSameKey = true, ownedProviderOpenGate = true,
                noClaimOfHistoricalHostedFailureCause = true,
            }));
            Assert.True(exact); Assert.Equal(1, opens);
            Assert.False(returnedWithoutReader, "The fixture must wait for the current reader; a same-key loader return is not publication.");
        }
        finally
        {
            release.Set();
            if (first is not null) await first;
            if (second is not null) await second;
            pane.Attach(null); preview.Close();
            AccessibilityTests.Close(services, main, root);
        }
    }

    private static async Task Until(Func<bool> ready)
    {
        for (int i = 0; i < 500 && !ready(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(ready(), "The owned preview readiness control did not reach its checkpoint.");
    }

    private sealed class Provider(Func<IContentSource> open, int length) : ResourceProvider
    {
        public override string Scheme => "panelreadinessfixture";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override IContentSource OpenContent(ItemRef item) => open();
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch([new EntryData { Name = "known", Kind = EntryKind.File, Size = length }]);
            return Task.CompletedTask;
        }
    }
}
