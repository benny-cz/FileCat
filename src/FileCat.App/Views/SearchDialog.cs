using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Selection;

namespace FileCat.App.Views;

public enum SearchDialogOutcome
{
    Closed,
    ShowInPanel,
    GoTo,
}

public sealed record SearchDialogResult(SearchDialogOutcome Outcome, ResultSet? Set, ItemRef? Item, CancellationTokenSource? Running);

/// <summary>
/// Find files (Alt+F7): streams results live, can skip the folder being searched, and sends results to a
/// panel as a result set whose items are the originals (plan §11).
/// </summary>
public static class SearchDialog
{
    private sealed record Row(ItemRef Item, string Folder)
    {
        public override string ToString() => Item.Name;
    }

    /// <param name="within">A result set to search within (the active tab shows it): matches form a narrower set.</param>
    public static async Task<SearchDialogResult> ShowAsync(MainViewModel vm, string root, ResultSet? within = null)
    {
        var history = vm.Services.History;
        var withinItems = within?.Snapshot();
        var roots = new TextBox
        {
            Text = withinItems is null ? root : $"{within!.Title} ({withinItems.Count:N0} items)",
            IsReadOnly = withinItems is not null,
            MinWidth = 560,
        };
        var names = new TextBox { Text = history.SearchNames.FirstOrDefault() ?? "*", PlaceholderText = "* (mask: *.cs;*.axaml|*Test*, /regex/)" };
        var text = new TextBox { Text = string.Empty, PlaceholderText = "text inside files (optional)" };
        var matchCase = new CheckBox { Content = "Match case" };
        var regex = new CheckBox { Content = "Regular expression" };
        var subfolders = new CheckBox { Content = "Subfolders", IsChecked = true };
        var hidden = new CheckBox { Content = "Hidden items", IsChecked = vm.Services.Settings.ShowHidden };
        var minKb = new TextBox { PlaceholderText = "min KB", Width = 90 };
        var maxKb = new TextBox { PlaceholderText = "max KB", Width = 90 };
        var days = new TextBox { PlaceholderText = "any age", Width = 90 };
        var start = new Button { Content = "Search", Classes = { "primary" }, IsDefault = true };
        var stop = new Button { Content = "Stop", IsEnabled = false };
        var skip = new Button { Content = "Skip current folder", IsEnabled = false };
        var status = new TextBlock { Classes = { "small", "muted" }, TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { Classes = { "error" }, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        var rows = new Avalonia.Collections.AvaloniaList<Row>();
        var list = new ListBox
        {
            Classes = { "choices" },
            Height = 260,
            ItemsSource = rows,
            ItemTemplate = new FuncDataTemplate<Row>((r, _) =>
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*") };
                g.Children.Add(new TextBlock { Text = r?.Item.Name, TextTrimming = TextTrimming.CharacterEllipsis });
                var folder = new TextBlock { Text = r?.Folder, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 0, 0) };
                Grid.SetColumn(folder, 1);
                g.Children.Add(folder);
                return g;
            }),
        };
        Avalonia.Automation.AutomationProperties.SetName(minKb, "Minimum size in KB");
        Avalonia.Automation.AutomationProperties.SetName(maxKb, "Maximum size in KB");
        Avalonia.Automation.AutomationProperties.SetName(days, "Modified within the last days");
        Avalonia.Automation.AutomationProperties.SetName(list, "Search results");
        int shownCount = 0;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto") };
        void Row(int r, string label, Control c)
        {
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 10, 3) };
            Grid.SetRow(l, r);
            Grid.SetRow(c, r);
            Grid.SetColumn(c, 1);
            c.Margin = new Thickness(0, 3);
            if (c is not Panel) Avalonia.Automation.AutomationProperties.SetName(c, label.TrimEnd(':'));
            grid.Children.Add(l);
            grid.Children.Add(c);
        }
        Row(0, withinItems is null ? "Search in:" : "Search within:", roots);
        if (withinItems is not null) subfolders.IsEnabled = false;
        Row(1, "Names:", names);
        Row(2, "Containing:", text);
        var options = new WrapPanel { ItemSpacing = 10, LineSpacing = 4 };
        foreach (var c in new Control[] { matchCase, regex, subfolders, hidden, new TextBlock { Text = "Size:", VerticalAlignment = VerticalAlignment.Center }, minKb, maxKb,
                     new TextBlock { Text = "Modified in last days:", VerticalAlignment = VerticalAlignment.Center }, days })
            options.Children.Add(c);
        Row(3, "Options:", options);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { start, stop, skip } };
        var body = new StackPanel { Spacing = 6, Children = { grid, actions, error, status, list, new TextBlock { Text = "Enter on a result goes to it · results in a panel act on the original items", Classes = { "muted", "small" } } } };

        ResultSet? set = null;
        SearchSession? session = null;
        CancellationTokenSource? cts = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void Refresh()
        {
            if (set is null || session is null) return;
            var snapshot = set.Snapshot();
            for (int i = shownCount; i < snapshot.Count && rows.Count < 5000; i++) rows.Add(new Row(snapshot[i].Item, snapshot[i].Relative));
            shownCount = snapshot.Count;
            var inaccessible = session.Inaccessible.Count;
            status.Text = (session.Finished ? (set.IsComplete ? "Finished" : "Stopped") : $"Searching {session.CurrentFolder}") +
                          $" · {session.FoldersVisited:N0} folders" + (session.FilesExamined > 0 ? $" · {session.FilesExamined:N0} files read" : "") +
                          $" · {session.Matches:N0} found" +
                          (inaccessible > 0 ? $" · {inaccessible:N0} not accessible (not searched)" : "") +
                          (session.RegexTimedOut ? " · some regex matches timed out" : "") +
                          (rows.Count >= 5000 ? " · list shows the first 5,000; the panel shows all" : "");
            if (session.Finished)
            {
                timer.Stop();
                start.IsEnabled = true;
                stop.IsEnabled = false;
                skip.IsEnabled = false;
            }
        }
        timer.Tick += (_, _) => Refresh();

        start.Click += (_, _) =>
        {
            error.IsVisible = false;
            if (!Mask.TryParse(string.IsNullOrWhiteSpace(names.Text) ? "*" : names.Text, out var mask, out var maskError))
            {
                error.Text = "Names: " + maskError;
                error.IsVisible = true;
                return;
            }
            var rootList = withinItems is not null ? [] : (roots.Text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            var missing = rootList.FirstOrDefault(r => !Directory.Exists(r));
            if (withinItems is null && (rootList.Count == 0 || missing is not null))
            {
                error.Text = rootList.Count == 0 ? "Enter a folder to search." : $"\"{missing}\" is not a folder.";
                error.IsVisible = true;
                return;
            }
            long? Kb(TextBox b) => long.TryParse(b.Text, out var v) && v >= 0 ? v * 1024 : null;
            var query = new SearchQuery
            {
                Roots = rootList,
                Names = mask,
                Text = string.IsNullOrEmpty(text.Text) ? null : text.Text,
                MatchCase = matchCase.IsChecked == true,
                Regex = regex.IsChecked == true,
                Recursive = subfolders.IsChecked == true,
                IncludeHidden = hidden.IsChecked == true,
                MinSize = Kb(minKb),
                MaxSize = Kb(maxKb),
                ModifiedAfterUtc = int.TryParse(days.Text, out var d) && d > 0 ? DateTime.UtcNow.AddDays(-d) : null,
                WithinResults = withinItems,
            };
            if (!SearchSession.TryValidate(query, out var qerr))
            {
                error.Text = qerr!;
                error.IsVisible = true;
                return;
            }
            AppServices.RememberText(history.SearchNames, names.Text ?? "*");
            if (!string.IsNullOrEmpty(text.Text)) AppServices.RememberText(history.SearchTexts, text.Text);
            cts?.Cancel();
            rows.Clear();
            shownCount = 0;
            set = vm.Services.ResultSets.Create(TitleOf(query), query.Describe());
            session = new SearchSession(query, set);
            cts = new CancellationTokenSource();
            var token = cts.Token;
            var s = session;
            _ = Task.Run(() => s.Run(token), token);
            start.IsEnabled = false;
            stop.IsEnabled = true;
            skip.IsEnabled = true;
            timer.Start();
        };
        stop.Click += (_, _) => cts?.Cancel();
        skip.Click += (_, _) => session?.SkipCurrentFolder();
        list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && list.SelectedItem is Row) e.Handled = true;
        };

        var result = await vm.Dialogs.ShowCustomAsync(withinItems is null ? "Find files" : "Find within results", body,
            [new DialogButton("Close", SearchDialogOutcome.Closed, IsCancel: true), new DialogButton("Go to", SearchDialogOutcome.GoTo), new DialogButton("Show in panel", SearchDialogOutcome.ShowInPanel)],
            names);
        timer.Stop();
        var outcome = result as SearchDialogOutcome? ?? SearchDialogOutcome.Closed;
        switch (outcome)
        {
            case SearchDialogOutcome.GoTo:
                cts?.Cancel();
                return new SearchDialogResult(outcome, set, (list.SelectedItem as Row)?.Item ?? rows.FirstOrDefault()?.Item, null);
            case SearchDialogOutcome.ShowInPanel when set is not null:
                return new SearchDialogResult(outcome, set, null, session is { Finished: false } ? cts : null);
            default:
                cts?.Cancel();
                return new SearchDialogResult(SearchDialogOutcome.Closed, null, null, null);
        }
    }

    private static string TitleOf(SearchQuery q)
    {
        var what = !string.IsNullOrEmpty(q.Text) ? $"\"{q.Text}\"" : q.Names?.Text ?? "*";
        return $"Search {what}";
    }
}
