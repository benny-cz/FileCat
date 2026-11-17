using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

internal static class AnalysisSavedRecipeDialog
{
    internal sealed record Choice(AnalysisSavedRecipe? Recipe)
    {
        internal bool Create => Recipe is null;
    }

    internal static async Task<Choice?> ChooseAsync(IDialogService dialogs, ImmutableArray<AnalysisSavedRecipe> recipes,
        CancellationToken cancellation)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(Text(recipes.IsEmpty ? "No filters saved for these captured observations." : "Choose a saved filter to preview its recorded matches."));
        var closer = new DialogCloser();
        if (!recipes.IsEmpty)
        {
            var choices = new ListBox { ItemsSource = recipes, MaxHeight = 220,
                ItemTemplate = new FuncDataTemplate<AnalysisSavedRecipe>((recipe, _) => Text(recipe is null ? "" :
                    AnalysisNames.Display(recipe.Name, 120) + " · " + AnalysisNames.Display(recipe.Query, 400))) };
            ScrollViewer.SetHorizontalScrollBarVisibility(choices, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(choices, "Saved filters for these observations");
            var preview = new Button { Content = "Preview saved filter", IsEnabled = false };
            choices.SelectionChanged += (_, _) => preview.IsEnabled = choices.SelectedItem is AnalysisSavedRecipe;
            preview.Click += (_, _) =>
            {
                if (choices.SelectedItem is AnalysisSavedRecipe selected) closer.Close(new Choice(selected));
            };
            body.Children.Add(choices);
            body.Children.Add(preview);
        }
        using var close = cancellation.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        cancellation.ThrowIfCancellationRequested();
        return await dialogs.ShowCustomAsync("Saved recipes", body,
            [new("Close", false, IsDefault: true, IsCancel: true), new("Create saved filter…", new Choice(Recipe: null))], closer: closer) as Choice;
    }

    internal static async Task<AnalysisSavedRecipe?> CreateAsync(IDialogService dialogs, SessionIdentity identity,
        ImmutableArray<RootBinding> roots, AnalysisMetric metric, string queryText, CancellationToken cancellation)
    {
        var name = new TextBox { MaxLength = 120, PlaceholderText = "Recipe name" };
        var query = new TextBox { MaxLength = AnalysisLimits.QueryBytes, Text = queryText, PlaceholderText = "Example: ext=zip size>=500MiB" };
        var caseSensitive = new CheckBox { Content = "Case sensitive" };
        var locations = new ListBox { ItemsSource = roots.Where(root => root.Disposition == AnalysisRootDisposition.Active).ToArray(),
            SelectionMode = SelectionMode.Multiple, MaxHeight = 160,
            ItemTemplate = new FuncDataTemplate<RootBinding>((root, _) => Text(root is null ? "" : AnalysisRecipeDialog.LocationLabel(root))) };
        ScrollViewer.SetHorizontalScrollBarVisibility(locations, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(name, "Saved recipe name");
        AutomationProperties.SetName(query, "Saved recipe filter");
        AutomationProperties.SetName(locations, "Locations for saved recipe");
        var error = Text("");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
        var save = new Button { Content = "Save and preview filter", IsEnabled = false };
        var body = new StackPanel { Spacing = 8, Children =
        {
            Text("Save a declarative filter for these captured observations. Reopen the same saved analysis to use it later."),
            name, query, caseSensitive, Text("Choose captured locations"), locations, error, save
        } };
        var closer = new DialogCloser();
        void Ready()
        {
            save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text) && !string.IsNullOrWhiteSpace(query.Text) && locations.SelectedItems?.Count > 0;
            error.Text = "";
        }
        name.TextChanged += (_, _) => Ready();
        query.TextChanged += (_, _) => Ready();
        locations.SelectionChanged += (_, _) => Ready();
        save.Click += (_, _) =>
        {
            if (!save.IsEnabled) return;
            var selected = locations.SelectedItems!.Cast<RootBinding>().Select(root => root.Id).ToImmutableArray();
            try
            {
                var recipe = AnalysisSavedRecipe.Create(Guid.NewGuid(), name.Text!.Trim(), identity, roots,
                    selected, query.Text!.Trim(), caseSensitive.IsChecked == true, metric);
                closer.Close(recipe);
            }
            catch (Exception failure) when (failure is FormatException or OverflowException or InvalidDataException)
            { error.Text = AnalysisNames.Display(failure.Message, 360); }
        };
        using var close = cancellation.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        cancellation.ThrowIfCancellationRequested();
        return await dialogs.ShowCustomAsync("Save a custom recipe", body, [new("Close", false, IsDefault: true, IsCancel: true)],
            initialFocus: name, closer: closer, resizable: true) as AnalysisSavedRecipe;
    }

    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };
}
