using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// V01: a planned operation keeps its exact scope and destination whatever happens in the window before it runs. Two
/// folders hold files of one name and different content; marked files are copied with F5 and, while the copy waits on a
/// name conflict, new files arrive beside the marked ones, the target panel goes elsewhere, the panels swap and a third
/// panel is added. Direct manifests of the folders before and after are the reference.
/// </summary>
public sealed class OperationScopeTests
{
    private static Dictionary<string, string> Manifest(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllText, StringComparer.Ordinal);

    private static List<string> MarkedNames(ListingModel listing) =>
        [.. Enumerable.Range(0, listing.VisibleCount).Where(listing.IsVisibleMarked).Select(i => listing.GetVisible(i).Name.ToString()).Order(StringComparer.Ordinal)];

    [AvaloniaFact]
    public async Task A_queued_copy_keeps_its_items_and_destination_whatever_the_window_does_meanwhile()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string a = Directory.CreateDirectory(Path.Combine(root, "A")).FullName;
            string b = Directory.CreateDirectory(Path.Combine(root, "B")).FullName;
            string c = Directory.CreateDirectory(Path.Combine(root, "C")).FullName;
            File.WriteAllText(Path.Combine(a, "x.txt"), "A-x");
            File.WriteAllText(Path.Combine(a, "y.txt"), "A-y");
            File.WriteAllText(Path.Combine(a, "z.log"), "A-z");
            File.WriteAllText(Path.Combine(b, "x.txt"), "B-x");
            var source = vm.Workspace.Panels[0];
            var target = vm.Workspace.Panels[1];
            source.ActiveTab!.Navigate(Location.FileSystem(a));
            target.ActiveTab!.Navigate(Location.FileSystem(b));
            vm.Workspace.Activate(source);
            async Task Listed(ListingModel listing, int count)
            {
                for (int i = 0; i < 300 && !(listing.State == ListingState.Complete && listing.VisibleCount == count); i++) await Task.Delay(20, ct);
            }
            var listing = source.ActiveTab!.Listing;
            await Listed(listing, 4); // .., x.txt, y.txt, z.log
            listing.MarkNames(["x.txt", "y.txt"], true);
            Assert.Equal(["x.txt", "y.txt"], MarkedNames(listing));

            // F5 to the target panel; its dialog's Enter starts the copy, which stops at B's own x.txt.
            vm.Execute(CommandIds.Copy);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            Assert.True(dialogs.IsOpen);
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Job? job = null;
            for (int i = 0; i < 500 && (job = services.Jobs.Jobs.FirstOrDefault()) is not { State: JobState.AwaitingDecision }; i++) await Task.Delay(20, ct);
            Assert.NotNull(job);
            Assert.Equal(JobState.AwaitingDecision, job.State);
            Assert.Equal([Path.Combine(a, "x.txt"), Path.Combine(a, "y.txt")], job.Request.Sources.Select(s => s.FileSystemPath!).Order(StringComparer.Ordinal));
            Assert.Equal(b, job.Request.Destination!.Path.TrimEnd(Path.DirectorySeparatorChar));
            var beforeA = Manifest(a);

            // Meanwhile: new files beside the marked ones, the target elsewhere, the panels swapped, a third panel added.
            File.WriteAllText(Path.Combine(a, "w.txt"), "A-w, arrived later");
            File.WriteAllText(Path.Combine(a, "x2.txt"), "A-x2, arrived later");
            await Listed(listing, 6);
            Assert.Equal(["x.txt", "y.txt"], MarkedNames(listing)); // the marks do not grow with what arrives
            target.ActiveTab!.Navigate(Location.FileSystem(c));
            vm.Execute(CommandIds.SwapPanels);
            vm.Execute(CommandIds.AddPanel);
            await Task.Delay(100, ct);

            // Replace B's x.txt: the copy goes on as it was planned.
            job.Decision!.Resolve(new Decision(DecisionAction.Replace));
            for (int i = 0; i < 500 && !job.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(new Dictionary<string, string> { ["x.txt"] = "A-x", ["y.txt"] = "A-y" }, Manifest(b));
            Assert.Empty(Manifest(c));
            var afterA = Manifest(a);
            Assert.Equal(beforeA.Count + 2, afterA.Count);
            foreach (var (name, content) in beforeA) Assert.Equal(content, afterA[name]);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public Task Marks_the_filter_hides_are_said_and_copied_when_left_included() => HiddenMarks(include: true);

    [AvaloniaFact]
    public Task Marks_the_filter_hides_are_said_and_left_out_when_unticked() => HiddenMarks(include: false);

    /// <summary>V01: a mark the panel's filter hides is said in F5's dialog, and copied only while it is included.</summary>
    private static async Task HiddenMarks(bool include)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string a = Directory.CreateDirectory(Path.Combine(root, "A")).FullName;
            string b = Directory.CreateDirectory(Path.Combine(root, "B")).FullName;
            File.WriteAllText(Path.Combine(a, "x.txt"), "A-x");
            File.WriteAllText(Path.Combine(a, "y.txt"), "A-y");
            var source = vm.Workspace.Panels[0];
            source.ActiveTab!.Navigate(Location.FileSystem(a));
            vm.Workspace.Panels[1].ActiveTab!.Navigate(Location.FileSystem(b));
            vm.Workspace.Activate(source);
            var listing = source.ActiveTab!.Listing;
            for (int i = 0; i < 300 && !(listing.State == ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);
            listing.MarkNames(["x.txt", "y.txt"], true);
            source.ActiveTab.SetFilter("x*"); // y.txt stays marked, out of sight
            for (int i = 0; i < 300 && listing.VisibleCount != 2; i++) await Task.Delay(20, ct); // .. and x.txt
            Assert.Equal(["x.txt"], MarkedNames(listing));

            vm.Execute(CommandIds.Copy);
            var dialogs = (OverlayDialogService)vm.Dialogs;
            for (int i = 0; i < 250 && !dialogs.IsOpen; i++) await Task.Delay(20, ct);
            var hidden = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.CheckBox>()
                .Single(box => (box.Content as string)?.StartsWith("Include 1 marked item hidden by the filter", StringComparison.Ordinal) == true);
            Assert.True(hidden.IsChecked);
            if (!include) hidden.IsChecked = false;
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Job? job = null;
            for (int i = 0; i < 500 && (job = services.Jobs.Jobs.FirstOrDefault()) is not { } || job is { } j && !j.State.IsFinished(); i++) await Task.Delay(20, ct);
            Assert.Equal(JobState.Completed, job!.State);
            Assert.Equal(include ? new Dictionary<string, string> { ["x.txt"] = "A-x", ["y.txt"] = "A-y" } : new Dictionary<string, string> { ["x.txt"] = "A-x" }, Manifest(b));
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
