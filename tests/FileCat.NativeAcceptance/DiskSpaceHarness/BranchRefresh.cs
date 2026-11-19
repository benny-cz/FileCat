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

            Click("Candidate recipes…");
            await ReadyAsync(() => Control<ComboBox>("Candidate recipe") is not null);
            if (Button("View candidates")?.IsEnabled != false) throw new InvalidOperationException("Candidate previews must require an explicit location.");
            Control<ComboBox>("Candidate recipe")!.SelectedItem = ".NET restore output";
            Control<ListBox>("Locations for candidate preview")!.SelectedIndex = 0;
            Click("View candidates");
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates")?.ItemCount == 1);
            Control<ListBox>("Captured recipe candidates")!.SelectedIndex = 0;
            Click("Inspect recorded candidate");
            await ReadyAsync(() => Control<TextBlock>("Recorded candidate evidence")?.Text?.Contains("RecipeProject/RecipeProject.csproj", StringComparison.Ordinal) == true);
            string recipeEvidence = Control<TextBlock>("Recorded candidate evidence")!.Text!;
            if (!recipeEvidence.Contains("RecipeProject/obj/project.assets.json", StringComparison.Ordinal) ||
                recipeEvidence.Contains("RecipeLookalike", StringComparison.Ordinal)) throw new InvalidOperationException("The recipe included the wrong captured path.");
            await RecipeFrameAsync("branch-recipe-evidence.png", !Program.SavedRecipeProbe, token);
            Click("Close preview");
            await ReadyAsync(() => Button("Close preview") is null && Marks(1, 1));
            bool savedRecipe = false;
            if (Program.SavedRecipeProbe)
            {
                Click("Candidate recipes…");
                await ReadyAsync(() => Button("Saved recipes…")?.IsEnabled == true);
                Click("Saved recipes…");
                await ReadyAsync(() => Button("Create saved filter…")?.IsEnabled == true);
                Click("Create saved filter…");
                await ReadyAsync(() => Control<TextBox>("Saved recipe filter") is not null);
                Control<TextBox>("Saved recipe name")!.Text = "Project restore evidence";
                Control<TextBox>("Saved recipe filter")!.Text = "path:RecipeProject AND ext=json";
                Control<ListBox>("Locations for saved recipe")!.SelectedIndex = 0;
                Click("Save and preview filter");
                await CheckSavedPreviewAsync();
                Click("Close preview");
                await ReadyAsync(() => Button("Close preview") is null);
                Click("Candidate recipes…");
                await ReadyAsync(() => Button("Saved recipes…")?.IsEnabled == true);
                Click("Saved recipes…");
                await ReadyAsync(() => Control<ListBox>("Saved filters for these observations")?.ItemCount == 1);
                Control<ListBox>("Saved filters for these observations")!.SelectedIndex = 0;
                Click("Preview saved filter");
                await CheckSavedPreviewAsync();
                await RecipeFrameAsync("branch-saved-recipe.png", true, token);
                Click("Close preview");
                await ReadyAsync(() => Button("Close preview") is null && Marks(1, 1));
                savedRecipe = true;
            }

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
                recipePreviewPassed = true,
                recipeEvidence,
                savedRecipePassed = savedRecipe,
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
        T? Control<T>(string name) where T : Avalonia.Controls.Control => _window!.GetVisualDescendants().OfType<T>()
            .SingleOrDefault(control => control.IsEffectivelyVisible &&
                (AutomationProperties.GetName(control) == name || AutomationProperties.GetAutomationId(control) == name));
        async Task CheckSavedPreviewAsync()
        {
            await ReadyAsync(() => Control<ListBox>("Captured recipe candidates")?.ItemCount == 1);
            var candidate = Control<ListBox>("Captured recipe candidates")!.Items[0] as string;
            if (candidate?.Contains("project.assets.json", StringComparison.Ordinal) != true)
                throw new InvalidOperationException("The saved filter preview has the wrong captured file.");
            Control<ListBox>("Captured recipe candidates")!.SelectedIndex = 0;
            Click("Inspect recorded candidate");
            await ReadyAsync(() => Control<TextBlock>("Recorded candidate evidence")?.Text?.Contains("RecipeProject/obj/project.assets.json", StringComparison.Ordinal) == true);
        }
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

    private async Task RecipeFrameAsync(string frame, bool compositor, CancellationToken token)
    {
        RenderNativeFrame(frame);
        if (!compositor || !Program.ReplacementCompositorCapture) return;
        string request = Path.Combine(_run, "capture.request"), response = Path.Combine(_run, "capture.response");
        File.Delete(response);
        string pending = request + ".pending";
        await File.WriteAllTextAsync(pending, frame, token);
        File.Move(pending, request);
        await ReadyAsync(() => File.Exists(response));
        if (await File.ReadAllTextAsync(response, token) != frame)
            throw new IOException("The compositor capture did not confirm the recipe preview frame.");
        File.Delete(request);
        File.Delete(response);
    }
}
