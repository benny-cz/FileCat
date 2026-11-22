using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Permissions;
using FileCat.Core.Selection;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>
/// G-UX: each way into the permissions inspector reaches the object it names, through a deterministic reader so the
/// routes, not a platform's reader, are under test.
/// </summary>
public sealed class PermissionRouteTests
{
    private sealed class RecordingPermissions : IPermissionService
    {
        private readonly List<string> _locators = [];

        internal string[] Locators { get { lock (_locators) return [.. _locators]; } }

        public PermissionInspection Inspect(PermissionSubject subject, PermissionSections sections, CancellationToken cancellationToken)
        {
            lock (_locators) _locators.Add(subject.RawLocator);
            return PermissionSnapshots.Unavailable(subject, sections, PermissionReadStatus.Unsupported, "Route fixture reader.");
        }

        internal void Clear() { lock (_locators) _locators.Clear(); }
    }

    private static (AppServices Services, MainViewModel Vm, MainWindow Window, string Root, RecordingPermissions Permissions) Open()
    {
        if (MainViewModel.PermissionProcessRefusal() is not null) Assert.Skip("Requires an ordinary FileCat process.");
        var permissions = new RecordingPermissions();
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(permissionService: permissions);
        return (services, vm, window, root, permissions);
    }

    private static MenuItem PermissionsItem(ContextMenu menu) => Assert.IsAssignableFrom<IEnumerable<Control>>(menu.ItemsSource)
        .OfType<MenuItem>().Single(item => Equals(item.Header, "Permissions and ownership…"));

    private static bool InspectorOpen(Window window) => window.GetVisualDescendants().OfType<TextBlock>()
        .Any(text => text.Text?.Contains("Route fixture reader.", StringComparison.Ordinal) == true);

    private static void CloseInspector(Window window) => window.GetVisualDescendants().OfType<Button>()
        .Single(button => Equals(button.Content, "Close")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(25, ct);
        Assert.True(condition());
    }

    [AvaloniaFact]
    public async Task The_background_menu_inspects_the_folder_it_shows()
    {
        var (services, vm, window, root, permissions) = Open();
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var panel = vm.Workspace.Panels[0];
            var tab = panel.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete, ct);
            var item = PermissionsItem(ContextMenuFactory.BuildBackground(vm, panel, tab, tab.Location!));
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => InspectorOpen(window), ct);
            Assert.Equal([tab.Location!.Path], permissions.Locators);
            CloseInspector(window);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaFact]
    public async Task The_item_menu_inspects_the_focused_item()
    {
        var (services, vm, window, root, permissions) = Open();
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.FocusName("b.txt"), ct);
            var item = PermissionsItem(ContextMenuFactory.Build(vm));
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await Until(() => InspectorOpen(window), ct);
            Assert.Equal([Path.Combine(root, "files", "b.txt")], permissions.Locators);
            CloseInspector(window);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData("Visible items only", 1)]
    [InlineData("Include all", 2)]
    public async Task Hidden_marks_are_included_only_when_chosen(string choice, int inspected)
    {
        var (services, vm, window, root, permissions) = Open();
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            await Until(() => listing.State == ListingState.Complete && listing.VisibleCount == 3, ct);
            listing.MarkNames(["a.txt", "b.txt"], true);
            Assert.True(Mask.TryParse("a*", out var mask, out _));
            listing.Filter = mask;
            await Until(() => listing.VisibleCount == 2, ct); // the parent row and a.txt; b.txt stays marked behind the filter
            vm.Execute(CommandIds.PermissionsOwnership);
            var filter = await FindChoiceFilter(window, ct);
            filter.Text = choice;
            // The list filters on the next layout pass; Enter then takes the one remaining choice.
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = filter });
            await Until(() => InspectorOpen(window), ct);
            var expected = new[] { "a.txt", "b.txt" }.Take(inspected).Select(name => Path.Combine(root, "files", name)).Order();
            Assert.Equal(expected, permissions.Locators.Order());
            CloseInspector(window);
        }
        finally { AccessibilityTests.Close(services, window, root); }

        static async Task<TextBox> FindChoiceFilter(Window window, CancellationToken ct)
        {
            TextBox? found = null;
            await Until(() => (found = window.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(box => box.PlaceholderText == "Type to filter…" && box.IsEffectivelyVisible)) is not null &&
                window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Inspect selected permissions"), ct);
            return found!;
        }
    }
}
