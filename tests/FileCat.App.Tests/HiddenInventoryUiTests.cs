using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.HiddenData;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class HiddenInventoryUiTests
{
    private sealed class Data : IHiddenData
    {
        public bool IsSupported => true;
        public bool SupportsEaValueQuery => true;
        public int Queries, ValueBearingQueries, Previews;
        public IReadOnlyList<HiddenItem> List(string path) => throw new InvalidOperationException("Legacy inventory invoked.");
        public HiddenInventory ReadInventory(string path, HiddenInventoryRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref Queries);
            if (request.IncludeEaValues) Interlocked.Increment(ref ValueBearingQueries);
            return new(path, DateTimeOffset.UtcNow, request.IncludeEaValues ? [new("ONLY.EA", HiddenKind.NtfsAttribute, 8)] : [],
                [new(HiddenKind.Stream, HiddenInventoryState.Complete, 0, "fixture"),
                 new(HiddenKind.NtfsAttribute, request.IncludeEaValues ? HiddenInventoryState.Complete : HiddenInventoryState.NotInspected, request.IncludeEaValues ? 1 : 0, "fixture")], "fixture")
                { BoundToObject = true, ValueBearingQueryIssued = request.IncludeEaValues };
        }
        public byte[] Read(string path, HiddenItem item, int max) { Previews++; throw new InvalidOperationException(); }
        public Stream Open(string path, HiddenItem item) => throw new InvalidOperationException();
        public void Delete(string path, HiddenItem item) => throw new InvalidOperationException();
    }
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task Empty_inventory_exposes_keyboard_EA_action_and_acquires_values_only_after_its_scoped_review()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(width: 760);
        var data = new Data(); services.Providers.Register(new HiddenDataProvider(data));
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            tab.Navigate(HiddenDataProvider.Of(Path.Combine(root, "files", "a.txt")));
            await Until(() => tab.Listing.State == ListingState.Complete);
            Assert.Equal(0, tab.Listing.TotalCount); Assert.True(tab.CanReadSupplementaryEas);
            Assert.Contains("extended attributes: not inspected", tab.SupplementaryInventoryText);
            Assert.Contains("not inspected", tab.StatusLeft); Assert.Equal(0, data.ValueBearingQueries); Assert.Equal(0, data.Previews);
            Button Action() => Assert.Single(window.GetVisualDescendants().OfType<Button>(), b =>
                b.IsEffectivelyVisible && AutomationProperties.GetName(b) == "Read extended attributes");
            Assert.True(Action().Focus());
            Action().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => vm.Dialogs.IsOpen);
            var overlay = window.FindControl<Panel>("OverlayHost")!;
            await Until(() => overlay.GetVisualDescendants().OfType<SelectableTextBlock>().Any(b => b.Text?.Contains("names and value bytes", StringComparison.Ordinal) == true));
            Assert.Equal(0, data.ValueBearingQueries);
            overlay.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Read extended attributes")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 1);
            Assert.Equal(1, data.ValueBearingQueries); Assert.Equal(0, data.Previews);
            Assert.True(tab.Listing.SupplementaryInventory!.IsComplete);
            Assert.Contains("1 extended attributes", tab.StatusLeft);
            Assert.Equal(3, tab.Listing.GetItemRef(tab.Listing.GetStoreIndex(tab.Listing.HasParentRow ? 1 : 0)).Ordinal);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
