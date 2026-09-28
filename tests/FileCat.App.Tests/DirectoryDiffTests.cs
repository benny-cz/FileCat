using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Compare;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class DirectoryDiffTests
{
    [AvaloniaFact]
    public async Task Ctrl_F10_with_subfolders_previews_differences_and_opens_them_as_results()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string l = Directory.CreateDirectory(Path.Combine(root, "L", "sub")).Parent!.FullName;
            string r = Directory.CreateDirectory(Path.Combine(root, "R", "sub")).Parent!.FullName;
            File.WriteAllText(Path.Combine(l, "same.txt"), "x");
            File.WriteAllText(Path.Combine(r, "same.txt"), "x");
            File.SetLastWriteTimeUtc(Path.Combine(r, "same.txt"), File.GetLastWriteTimeUtc(Path.Combine(l, "same.txt")));
            File.WriteAllText(Path.Combine(l, "sub", "only-left.txt"), "l");
            File.WriteAllText(Path.Combine(r, "sub", "only-right.txt"), "r");
            vm.Workspace.Panels[0].ActiveTab!.Navigate(Location.FileSystem(l));
            vm.Workspace.Panels[1].ActiveTab!.Navigate(Location.FileSystem(r));
            vm.Workspace.Activate(vm.Workspace.Panels[0]);
            foreach (var p in vm.Workspace.Panels)
                for (int i = 0; i < 250 && p.ActiveTab!.Listing.State != Core.Listing.ListingState.Complete; i++) await Task.Delay(20, ct);

            vm.Execute(CommandIds.CompareDirectories);
            for (int i = 0; i < 250 && !window.GetVisualDescendants().OfType<CheckBox>().Any(c => (c.Content as string)?.StartsWith("Include subfolders", StringComparison.Ordinal) == true); i++)
                await Task.Delay(20, ct);
            window.GetVisualDescendants().OfType<CheckBox>().Single(c => (c.Content as string)?.StartsWith("Include subfolders", StringComparison.Ordinal) == true).IsChecked = true;
            window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Compare").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (int i = 0; i < 250 && DirectoryDiffWindow.OpenWindows.Count == 0; i++) await Task.Delay(20, ct);
            var diff = Assert.Single(DirectoryDiffWindow.OpenWindows);
            for (int i = 0; i < 250 && diff.IsComparing; i++) await Task.Delay(20, ct);
            Assert.StartsWith("2 differences in 2 folders: 1 only left, 1 only right; 1 the same.", diff.Summary);
            Assert.Equal(["sub/only-left.txt", "sub/only-right.txt"], diff.ShownEntries.Select(e => e.RelativePath));

            diff.OpenSide(left: true);
            var results = vm.Workspace.Panels[0].ActiveTab!;
            Assert.Equal(Schemes.ResultSet, results.Location!.Scheme);
            for (int i = 0; i < 250 && !(results.Listing.State == Core.Listing.ListingState.Complete && results.Listing.VisibleCount >= 1); i++) await Task.Delay(20, ct);
            Assert.Equal(["only-left.txt"], Enumerable.Range(0, results.Listing.VisibleCount).Select(i => results.Listing.GetVisible(i).Name).Where(n => n != ".."));
            diff.Close();
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
