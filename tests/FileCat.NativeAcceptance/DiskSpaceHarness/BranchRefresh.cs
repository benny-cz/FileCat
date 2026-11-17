using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _branchRefreshProbe;

    private async Task BranchRefreshProbeAsync()
    {
        try
        {
            var window = _window!;
            var token = _indexedLifetime.Token;
            if (OperatingSystem.IsWindows()) WindowsGuestInput.DesktopGuard();

            Click("Start analysis");
            await ReadyAsync(() => Button("Open recorded result")?.IsEnabled == true);
            Click("Open recorded result");
            await ReadyAsync(() => window.SessionCount == 2 && IndexedList() is not null);
            var list = IndexedList()!;
            list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single();
            Click("Explore selected folder");
            await ReadyAsync(() => list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "Photos"));
            list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "Photos");
            await ReadyAsync(() => Button("Toggle focused mark")?.IsEnabled == true && Button("Refresh branch")?.IsEnabled == true);
            Click("Toggle focused mark");
            Click("Add to review basket");
            await ReadyAsync(() => Marks(1, 1) && Hints()?.StartsWith("Direct changes watched at 1 of 1", StringComparison.Ordinal) == true);

            string external = Path.Combine(_fixture, "external-touch.txt");
            string added = Path.Combine(_fixture, "Photos", "after-scan.txt");
            await File.WriteAllTextAsync(external, "external test change", token);
            await File.WriteAllTextAsync(added, "new branch observation", token);
            await ReadyAsync(() => Hints()?.StartsWith("Possibly stale: fixture.", StringComparison.Ordinal) == true);
            if (window.SessionCount != 2 || list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "external-touch.txt"))
                throw new InvalidOperationException("A change hint altered the captured result.");
            string stale = Hints()!;
            RenderNativeFrame("branch-stale.png");

            Click("Refresh branch");
            await ReadyAsync(() => window.SessionCount == 3 && !ReferenceEquals(list, IndexedList()));
            var branchList = IndexedList()!;
            var branch = branchList.Items.OfType<AnalysisHierarchyRow>().Single();
            if (branch.Path.Name != "Photos" || !Marks(0, 0)) throw new InvalidOperationException("The branch has the wrong root or inherited review entries.");
            branchList.SelectedItem = branch;
            Click("Explore selected folder");
            await ReadyAsync(() => branchList.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "after-scan.txt"));
            var names = branchList.Items.OfType<AnalysisHierarchyRow>().Select(row => row.Path.Name).Order(StringComparer.Ordinal).ToArray();
            if (!names.SequenceEqual(new[] { "after-scan.txt", "photo.png" }))
                throw new InvalidOperationException("The refreshed result contains data outside the selected branch.");
            RenderNativeFrame("branch-refreshed.png");

            _tabs!.SelectedIndex = 1;
            await ReadyAsync(() => Marks(1, 1) && Hints()?.StartsWith("Possibly stale:", StringComparison.Ordinal) == true);
            if (await File.ReadAllTextAsync(external, token) != "external test change" ||
                await File.ReadAllTextAsync(added, token) != "new branch observation")
                throw new IOException("The new fixture inputs did not survive the refresh.");
            _branchRefreshProbe = new
            {
                finished = true,
                passed = true,
                sessions = window.SessionCount,
                branchRoot = branch.Path.Name,
                observed = names,
                hint = stale,
                originalMarksAndBasketKept = true,
                newFilesPreserved = true,
                input = "native window control events"
            };
        }
        catch (Exception error)
        {
            _branchRefreshProbe = new { finished = true, passed = false, error = error.ToString() };
            try { RenderNativeFrame("branch-failure.png"); }
            catch { /* The report retains the original error if capture is unavailable. */ }
        }
        WriteReport("branch-refresh-complete");

        Button? Button(string name) => _window!.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => button.IsEffectivelyVisible && Equals(button.Content, name));
        void Click(string name)
        {
            var button = Button(name);
            if (button?.IsEnabled != true) throw new InvalidOperationException($"Native command unavailable: {name}");
            button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }
        ListBox? IndexedList() => _window!.GetVisualDescendants().OfType<ListBox>()
            .SingleOrDefault(list => list.IsEffectivelyVisible && AutomationProperties.GetName(list) == "Indexed analysis entries");
        string? Hints() => _window!.GetVisualDescendants().OfType<TextBlock>()
            .SingleOrDefault(text => text.IsEffectivelyVisible && AutomationProperties.GetAutomationId(text) == "Captured source change hints")?.Text;
        bool Marks(int marked, int basket) => _window!.GetVisualDescendants().OfType<TextBlock>()
            .Any(text => text.IsEffectivelyVisible && AutomationProperties.GetAutomationId(text) == "Indexed captured selection" &&
                text.Text?.StartsWith($"{marked:N0} marked · {basket:N0} in review basket", StringComparison.Ordinal) == true);
    }
}
