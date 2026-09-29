using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

    [AvaloniaFact]
    public async Task Synchronize_previews_every_step_and_runs_only_the_chosen_ones_as_jobs()
    {
        // Other tests may have comparisons open at the same time: this one compares changed.txt.
        static bool ChangedTxt(CompareWindow w) => w.Title == "Compare: changed.txt ↔ changed.txt";
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string l = Directory.CreateDirectory(Path.Combine(root, "L", "sub")).Parent!.FullName;
            string r = Directory.CreateDirectory(Path.Combine(root, "R", "sub")).Parent!.FullName;
            File.WriteAllText(Path.Combine(l, "sub", "new.txt"), "new");
            File.WriteAllText(Path.Combine(l, "changed.txt"), "newer");
            File.WriteAllText(Path.Combine(r, "changed.txt"), "old");
            File.SetLastWriteTimeUtc(Path.Combine(r, "changed.txt"), DateTime.UtcNow.AddDays(-3));
            File.WriteAllText(Path.Combine(r, "extra.txt"), "only right");
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

            // Enter on a file present on both sides compares their contents.
            Assert.True(diff.Select("changed.txt"), string.Join(", ", diff.ShownEntries.Select(e => e.RelativePath)));
            var differences = diff.GetVisualDescendants().OfType<ListBox>().Single();
            (differences.ContainerFromItem(differences.SelectedItem!) as Control)?.Focus();
            diff.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            for (int i = 0; i < 250 && !CompareWindow.OpenWindows.Any(ChangedTxt); i++) await Task.Delay(20, ct);
            Assert.Single(CompareWindow.OpenWindows, ChangedTxt).Close();

            var sync = diff.OpenSync()!;
            Assert.StartsWith("Will copy 1, replace 1.", sync.Summary);
            sync.Choose(sourceIsLeft: true, SyncMode.Mirror);
            Assert.Single(sync.Items, i => i.Action == SyncAction.Remove && i.Include);
            // Without a Recycle Bin (the portable test platform), removals wait for an explicit permanent deletion.
            Assert.StartsWith(sync.TargetRecycles ? "Will copy 1, replace 1, remove 1." : "Will copy 1, replace 1.", sync.Summary);
            // The user keeps the extra item, by keyboard (Space on the step): Mirror removes only what stays chosen.
            var steps = sync.GetVisualDescendants().OfType<ListBox>().Single();
            for (int i = 0; i < 250 && sync.FocusManager?.GetFocusedElement() is not ListBoxItem; i++) await Task.Delay(20, ct);
            Assert.IsType<ListBoxItem>(sync.FocusManager?.GetFocusedElement()); // the steps have the keyboard when the preview opens
            var remove = sync.Items.Single(i => i.Action == SyncAction.Remove);
            steps.SelectedItem = remove;
            (steps.ContainerFromItem(remove) as Control)?.Focus();
            sync.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.False(remove.Include);
            sync.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null); // the keyboard stays on the step
            Assert.True(remove.Include);
            sync.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            Assert.False(remove.Include);
            sync.Run();
            for (int i = 0; i < 500 && !(File.Exists(Path.Combine(r, "sub", "new.txt")) && File.ReadAllText(Path.Combine(r, "changed.txt")) == "newer"); i++)
                await Task.Delay(20, ct);
            Assert.Equal("new", File.ReadAllText(Path.Combine(r, "sub", "new.txt")));
            Assert.Equal("newer", File.ReadAllText(Path.Combine(r, "changed.txt")));
            Assert.True(File.Exists(Path.Combine(r, "extra.txt")));
            for (int i = 0; i < 250 && services.Jobs.HasActiveWork; i++) await Task.Delay(20, ct);
        }
        finally
        {
            foreach (var w in CompareWindow.OpenWindows.Where(ChangedTxt).ToList()) w.Close(); // not other tests' comparisons
            foreach (var w in DirectoryDiffWindow.OpenWindows.ToList()) w.Close();
            AccessibilityTests.Close(services, window, root);
        }
    }
}
