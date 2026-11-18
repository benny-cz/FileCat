using System.Collections.Immutable;
using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

internal static class AnalysisRecipeDialog
{
    internal abstract record Choice
    {
        internal sealed record BuiltIn(AnalysisRecipeRequest Request) : Choice;
        internal sealed record Saved : Choice;
    }

    private sealed record Location(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    internal static async Task<Choice?> ChooseAsync(IDialogService dialogs, ImmutableArray<RootBinding> roots,
        AnalysisMetric metric, bool hasPrivateStore, CancellationToken lifetime)
    {
        var locations = roots.Where(root => root.Disposition == AnalysisRootDisposition.Active)
            .Select(root => new Location(root.Id, LocationLabel(root))).ToArray();
        if (locations.Length == 0)
        {
            await dialogs.AlertAsync("Candidate recipes", "This analysis has no recorded locations available for a preview.");
            return null;
        }
        var recipe = new ComboBox { ItemsSource = AnalysisRecipes.Definitions.Select(definition => definition.Title).ToArray(), SelectedIndex = 0 };
        var scope = new ListBox { ItemsSource = locations, SelectionMode = SelectionMode.Multiple, MaxHeight = 150,
            ItemTemplate = new FuncDataTemplate<Location>((location, _) => Text(location?.Label ?? "")) };
        ScrollViewer.SetHorizontalScrollBarVisibility(scope, ScrollBarVisibility.Disabled);
        AutomationProperties.SetName(recipe, "Candidate recipe");
        AutomationProperties.SetName(scope, "Locations for candidate preview");
        var bytes = new TextBox { Text = "1GiB", MaxLength = 48 };
        var days = new TextBox { Text = "180", MaxLength = 5 };
        AutomationProperties.SetName(bytes, "Minimum file size");
        AutomationProperties.SetName(days, "Minimum modification age in days");
        var thresholds = new StackPanel { Spacing = 4, Children = { Text("Minimum file size"), bytes, Text("Minimum modification age in days"), days } };
        var rule = Text("");
        var validation = Text("");
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Polite);
        var body = new StackPanel { Spacing = 8, Children = { recipe, rule, Text("Choose captured locations"), scope, thresholds, validation } };
        AnalysisRecipeRequest? request = null;
        var preview = new Button { Content = "View candidates" };
        body.Children.Add(preview);
        void Update()
        {
            request = null;
            string validationText = "";
            if (recipe.SelectedIndex < 0)
            {
                preview.IsEnabled = false;
                return;
            }
            var definition = AnalysisRecipes.Definitions[recipe.SelectedIndex];
            rule.Text = definition.Rule + " " + definition.Limitations;
            thresholds.IsVisible = definition.Recipe == AnalysisMetadataRecipe.OldLargeFiles;
            var selected = scope.SelectedItems!.Cast<Location>().ToArray();
            UInt128? minimumBytes = null;
            int? minimumDays = null;
            if (definition.Recipe == AnalysisMetadataRecipe.LargestFiles && metric == AnalysisMetric.Files)
                validationText = "Choose a byte measure in the analysis to rank the largest files.";
            else if (selected.Length == 0)
                validationText = "Choose at least one captured location.";
            else if (definition.Recipe == AnalysisMetadataRecipe.DownloadsInstallers &&
                (selected.Length != 1 || !roots.Any(root => root.Id == selected[0].Id && root.Kind == AnalysisKind.Directory)))
                validationText = "Choose one captured folder to use as Downloads.";
            else if (thresholds.IsVisible)
            {
                try { minimumBytes = AnalysisFilter.ParseBytes(bytes.Text?.Trim() ?? ""); }
                catch (Exception error) when (error is FormatException or OverflowException) { validationText = "Enter a size such as 1GiB or 500MiB."; }
                if (!int.TryParse(days.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int age) || age is < 1 or > 36_500)
                    validationText = "Enter a whole number of days from 1 to 36,500.";
                else minimumDays = age;
            }
            if (validationText.Length == 0)
                request = new(definition.Recipe, [.. selected.Select(location => location.Id)], minimumBytes, minimumDays);
            validation.Text = validationText;
            validation.IsVisible = validationText.Length != 0;
            preview.IsEnabled = request is not null;
        }
        var closer = new DialogCloser();
        recipe.SelectionChanged += (_, _) => Update();
        scope.SelectionChanged += (_, _) => Update();
        bytes.TextChanged += (_, _) => Update();
        days.TextChanged += (_, _) => Update();
        preview.Click += (_, _) => { if (request is not null) closer.Close(new Choice.BuiltIn(request)); };
        Update();
        using var close = lifetime.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        lifetime.ThrowIfCancellationRequested();
        var actions = new List<DialogButton> { new("Close", false, IsDefault: true, IsCancel: true) };
        if (hasPrivateStore) actions.Add(new("Saved recipes…", new Choice.Saved()));
        return await dialogs.ShowCustomAsync("Candidate recipes", body, actions,
            initialFocus: recipe, closer: closer, resizable: true) as Choice;
    }

    internal static async Task<ImmutableArray<ulong>> ShowAsync(IDialogService dialogs, AnalysisRecipePreview preview, ImmutableArray<RootBinding> roots,
        Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath, bool canStage, CancellationToken lifetime)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        var locations = roots.ToDictionary(root => root.Id, LocationLabel, StringComparer.Ordinal);
        var shortLocations = preview.RootIds.Select((id, index) => (Id: id, Label: $"Location {index + 1:N0}"))
            .ToDictionary(location => location.Id, location => location.Label, StringComparer.Ordinal);
        bool multipleLocations = preview.RootIds.Length > 1;
        var body = new StackPanel { Spacing = 8 };
        var scope = preview.RootIds.Take(8).Select(id => multipleLocations ? shortLocations[id] + " — " + locations[id] : locations[id]);
        body.Children.Add(Text("Captured locations — " + string.Join(", ", scope) +
            (preview.RootIds.Length > 8 ? $" and {preview.RootIds.Length - 8:N0} more" : "")));
        body.Children.Add(Text($"Observation {preview.Identity.ObservedUtc.ToLocalTime():g} · revision {preview.Identity.Revision:N0} · rule version {preview.Definition.Version:N0}."));
        body.Children.Add(Text(preview.Definition.Rule + " " + preview.Definition.Limitations));
        if (preview.MinimumBytes is { } size && preview.MinimumAgeDays is { } age)
            body.Children.Add(Text($"File size at least {DiskSpaceViewModel.Bytes(size)} and modified more than {age:N0} days before observation."));
        if (preview.Coverage != NamespaceCoverage.CompleteUnderPolicy)
            body.Children.Add(Text("Partial observations. Unobserved entries may change this preview."));
        if (preview.CoverageNote is { } coverageNote)
            body.Children.Add(Text(coverageNote));
        string measure = preview.Metric switch
        {
            AnalysisMetric.OnDisk => "recorded allocation",
            AnalysisMetric.UniqueFileSize => "recorded size with verified identity",
            AnalysisMetric.Files => "recorded file count",
            _ => "recorded file size"
        };
        string summary = preview.Definition.Recipe switch
        {
            AnalysisMetadataRecipe.LargestFolders => $"{preview.Candidates:N0} {(preview.Candidates == 1 ? "ranked folder" : "ranked folders")} · {preview.Unknown:N0} unmeasured · {preview.Excluded:N0} excluded." +
                (preview.Rows.IsEmpty ? "" : $" Showing {preview.Rows.Length:N0} by {measure}."),
            AnalysisMetadataRecipe.AllocationDifference => $"{preview.Candidates:N0} file paths with measured differences · {preview.Unknown:N0} uncertain · {preview.Excluded:N0} excluded." +
                (preview.Rows.IsEmpty ? "" : $" Showing {preview.Rows.Length:N0} ranked by absolute difference."),
            AnalysisMetadataRecipe.ManySmallFiles => $"{preview.Candidates:N0} {(preview.Candidates == 1 ? "folder meets" : "folders meet")} the count threshold · " +
                $"{preview.Unknown:N0} {(preview.Unknown == 1 ? "folder has" : "folders have")} uncertain counts near the threshold." +
                (preview.Rows.IsEmpty ? "" : $" Showing {preview.Rows.Length:N0} ranked by observed small-file count."),
            AnalysisMetadataRecipe.UnmeasuredLocations => $"{preview.Candidates:N0} captured issue records in scope." +
                (preview.Rows.IsEmpty ? "" : $" Showing {preview.Rows.Length:N0} ordered by recorded state."),
            AnalysisMetadataRecipe.LargeExtraData => "",
            _ => $"{preview.Candidates:N0} {(preview.Candidates == 1 ? "candidate" : "candidates")} · {preview.Unknown:N0} uncertain · {preview.Excluded:N0} excluded. " +
                (preview.Rows.IsEmpty ? "No definite candidates to show." : $"Showing {preview.Rows.Length:N0} ranked by {measure}. Shared files can appear under more than one name.")
        };
        if (summary.Length != 0) body.Children.Add(Text(summary));
        var closer = new DialogCloser();
        var included = new HashSet<ulong>();
        bool includeRows = canStage && preview.Rows.Any(preview.CanInclude);
        if (preview.SupportsStaging && preview.Rows.Length != 0 && !canStage)
            body.Children.Add(Text("Adding recipe candidates to the review basket requires a finished live analysis."));
        Task? inspection = null;
        if (preview.Rows.Length > 0)
        {
            var rows = new ListBox { MaxHeight = 270, MinHeight = 90, ItemTemplate = DialogLayout.WrappingListText,
                ItemsSource = preview.Rows.Select(row => $"{row.DisplayName}\n" + RowEvidence(row)).ToArray() };
            ScrollViewer.SetHorizontalScrollBarVisibility(rows, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(rows, "Captured recipe candidates");
            body.Children.Add(rows);
            var details = Text("");
            AutomationProperties.SetName(details, "Recorded candidate evidence");
            AutomationProperties.SetLiveSetting(details, AutomationLiveSetting.Polite);
            var inspect = new Button { Content = "Inspect recorded candidate", IsEnabled = false };
            rows.SelectionChanged += (_, _) =>
            {
                details.Text = "";
                inspect.IsEnabled = rows.SelectedIndex >= 0 && (inspection is null || inspection.IsCompleted);
            };
            inspect.Click += (_, _) =>
            {
                if (rows.SelectedIndex < 0 || inspection is { IsCompleted: false }) return;
                int index = rows.SelectedIndex;
                inspect.IsEnabled = false;
                inspection = InspectAsync(index);
            };
            body.Children.Add(inspect);
            body.Children.Add(details);

            if (includeRows)
            {
                var include = new Button { Content = "Include selected candidate" };
                var exclude = new Button { Content = "Exclude selected candidate" };
                var stage = new Button { Content = "Add included to review basket" };
                var count = Text("");
                AutomationProperties.SetLiveSetting(count, AutomationLiveSetting.Polite);
                body.Children.Add(count);
                body.Children.Add(new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { include, exclude, stage } });
                rows.SelectionChanged += (_, _) => UpdateInclusion();
                include.Click += (_, _) => ChangeInclusion(true);
                exclude.Click += (_, _) => ChangeInclusion(false);
                stage.Click += (_, _) =>
                {
                    if (included.Count != 0)
                        closer.Close(preview.Rows.Where(row => included.Contains(row.PathId)).Select(row => row.PathId).ToImmutableArray());
                };
                UpdateInclusion();

                void ChangeInclusion(bool add)
                {
                    int index = rows.SelectedIndex;
                    if (index < 0 || !preview.CanInclude(preview.Rows[index])) return;
                    ulong id = preview.Rows[index].PathId;
                    if (add) included.Add(id);
                    else included.Remove(id);
                    string evidence = details.Text ?? "";
                    rows.ItemsSource = preview.Rows.Select(row => $"{row.DisplayName}\n" +
                        (included.Contains(row.PathId) ? "Included for review · " : "") + RowEvidence(row)).ToArray();
                    rows.SelectedIndex = index;
                    details.Text = evidence;
                    UpdateInclusion();
                }

                void UpdateInclusion()
                {
                    var row = rows.SelectedIndex >= 0 ? preview.Rows[rows.SelectedIndex] : null;
                    bool eligible = row is not null && preview.CanInclude(row);
                    bool isIncluded = eligible && included.Contains(row!.PathId);
                    include.IsEnabled = eligible && !isIncluded;
                    exclude.IsEnabled = isIncluded;
                    stage.IsEnabled = included.Count != 0;
                    count.Text = $"{included.Count:N0} included. FileCat checks live identity and protection before adding references.";
                }
            }

            async Task InspectAsync(int index)
            {
                var candidate = preview.Rows[index];
                var token = cancellation.Token;
                try
                {
                    string described = candidate.PathId == 0 ? "Issue recorded for " + locations[candidate.RootId] :
                        "Recorded path — " + await RecordedPathAsync(candidate.PathId, candidate.RootId, readPath, token);
                    if (candidate.PathId != 0 && multipleLocations)
                        described += "\nCaptured location — " + locations[candidate.RootId];
                    var finding = candidate.Finding;
                    if (finding.ContextPath is { } context)
                        described += "\nContext folder — " + await RecordedPathAsync(context, candidate.RootId, readPath, token);
                    if (finding.MarkerPath is { } marker)
                        described += "\nContext record — " + await RecordedPathAsync(marker, candidate.RootId, readPath, token);
                    if (finding.AdditionalMarkerPath is { } second)
                        described += "\nContext record — " + await RecordedPathAsync(second, candidate.RootId, readPath, token);
                    if (!token.IsCancellationRequested && rows.SelectedIndex == index) details.Text = described;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
                {
                    if (!token.IsCancellationRequested && rows.SelectedIndex == index)
                        details.Text = "Recorded evidence unavailable. " + AnalysisNames.Display(error.Message, 240);
                }
                finally
                {
                    if (!token.IsCancellationRequested) inspect.IsEnabled = rows.SelectedIndex >= 0;
                }
            }
        }
        using var close = lifetime.Register(() => Dispatcher.UIThread.Post(() => closer.Close(null)));
        lifetime.ThrowIfCancellationRequested();
        try
        {
            var result = await dialogs.ShowCustomAsync(preview.Definition.Title, body,
                [new DialogButton("Close preview", false, IsDefault: true, IsCancel: true)], closer: closer, resizable: true);
            return result is ImmutableArray<ulong> paths ? paths : [];
        }
        finally
        {
            cancellation.Cancel();
            if (inspection is not null) await inspection;
        }

        string RowEvidence(AnalysisRecipeCandidate row)
        {
            var fields = new List<string>(4);
            if (multipleLocations) fields.Add(shortLocations[row.RootId]);
            if (row.PathId != 0) fields.Add($"Captured entry {row.PathId:N0}");
            if (DetailAmount(row) is { } detail) fields.Add(detail);
            if (row.Finding.Evidence is { } evidence) fields.Add(evidence);
            return string.Join(" · ", fields);
        }

        string? DetailAmount(AnalysisRecipeCandidate row) => preview.Definition.Recipe switch
        {
            AnalysisMetadataRecipe.UnmeasuredLocations => null,
            AnalysisMetadataRecipe.ManySmallFiles => row.RecordedBytes.Value is { } size ? "known single-link allocation " + DiskSpaceViewModel.Bytes(size) : null,
            AnalysisMetadataRecipe.AllocationDifference => "difference " + DiskSpaceViewModel.Bytes(row.RecordedBytes.Value!.Value),
            AnalysisMetadataRecipe.LargestFolders when row.RecordedFilePaths is { } count => $"{count:N0} {(count == 1 ? "file path" : "file paths")}",
            _ => row.RecordedBytes.Value is { } amount ? DiskSpaceViewModel.Bytes(amount) : "Size unavailable"
        };
    }

    internal static async Task<string> RecordedPathAsync(ulong id, string root, Func<ulong, CancellationToken, Task<AnalysisPathMetadata>> readPath,
        CancellationToken cancellation)
    {
        var segments = new Stack<string>();
        int characters = 0;
        for (int depth = 0; depth <= AnalysisLimits.Depth; depth++)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = await readPath(id, cancellation);
            if (path.Id != id || path.RootId != root) throw new InvalidDataException("The candidate's recorded location is inconsistent.");
            string name = AnalysisNames.DisplayName(path, 256);
            if (characters + name.Length > 800)
            {
                segments.Push("…");
                return string.Join('/', segments);
            }
            segments.Push(name);
            characters += name.Length + 1;
            if (path.ParentId == 0) return string.Join('/', segments);
            id = path.ParentId;
        }
        throw new InvalidDataException("The candidate's recorded path exceeds the depth limit.");
    }

    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap };

    internal static string LocationLabel(RootBinding root) => root.Label == root.DisplayLocator
        ? AnalysisNames.Display(root.Label, 240)
        : AnalysisNames.Display(root.Label, 120) + " · " + AnalysisNames.Display(root.DisplayLocator, 240, escapeTrailing: true);
}
