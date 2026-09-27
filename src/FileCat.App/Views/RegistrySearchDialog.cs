using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Platform.Windows;

namespace FileCat.App.Views;

/// <summary>Registry-specific search fields; results reference original typed items.</summary>
public static class RegistrySearchDialog
{
    private sealed record Row(ItemRef Item, string Folder)
    {
        public string Name => Item.Name.Length == 0 ? "(Default)" : Item.Name;
        public string Kind => Item.Kind == EntryKind.RegistryKey ? "Key" : "Value";
    }

    public static async Task<SearchDialogResult> ShowAsync(MainViewModel vm, FileCat.Core.Resources.Location root)
    {
        var term = new TextBox { MinWidth = 520, PlaceholderText = "Name or stored data" };
        var rawText = new TextBox { MinWidth = 520, PlaceholderText = "Optional hex bytes, e.g. DE AD BE EF" };
        var keyNames = new CheckBox { Content = "Key names", IsChecked = true };
        var valueNames = new CheckBox { Content = "Value names", IsChecked = true };
        var typed = new CheckBox { Content = "Stored text and numbers" };
        var raw = new CheckBox { Content = "Raw bytes" };
        var recursive = new CheckBox { Content = "Subkeys", IsChecked = true };
        var matchCase = new CheckBox { Content = "Match case" };
        var start = new Button { Content = "Search", Classes = { "primary" }, IsDefault = true };
        var stop = new Button { Content = "Stop", IsEnabled = false };
        var status = new TextBlock { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };
        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap };
        var shown = new AvaloniaList<Row>();
        var results = new ListBox
        {
            Height = 240, MinWidth = 540, ItemsSource = shown, Classes = { "choices" },
            ItemTemplate = new FuncDataTemplate<Row>((row, _) =>
            {
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,2*,3*") };
                line.Children.Add(new TextBlock { Text = row?.Kind, Classes = { "muted" }, Width = 52 });
                var name = new TextBlock { Text = row?.Name, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(name, 1); line.Children.Add(name);
                var folder = new TextBlock { Text = row?.Folder, Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis };
                Grid.SetColumn(folder, 2); line.Children.Add(folder);
                return line;
            }),
        };
        Avalonia.Automation.AutomationProperties.SetName(term, "Registry search text");
        Avalonia.Automation.AutomationProperties.SetName(rawText, "Raw bytes as hexadecimal pairs");
        Avalonia.Automation.AutomationProperties.SetName(results, "Registry search results");
        var fields = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 155 };
        foreach (var c in new[] { keyNames, valueNames, typed, raw, recursive, matchCase }) fields.Children.Add(c);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        controls.Children.Add(stop);
        controls.Children.Add(start);
        var body = new StackPanel { Spacing = 8, Children =
        {
            new TextBlock { Text = $"Search below {vm.Services.Providers.Display(root)}. Registry links are listed by name but their targets are not searched.", TextWrapping = TextWrapping.Wrap },
            term, fields, rawText, controls, status, error, results,
            new TextBlock { Text = "The dialog displays the first 300 matches; Show in panel keeps the complete result set. Limits: 100,000 keys, 250,000 values, 100,000 matches.", Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap },
        } };
        ResultSet? set = null;
        CancellationTokenSource? running = null;
        Task? searchTask = null;
        void UpdateStatus(string message) => vm.Services.Ui.Post(() => status.Text = message);
        start.Click += (_, _) =>
        {
            if (searchTask is { IsCompleted: false }) return;
            error.Text = "";
            byte[]? pattern = null;
            if (raw.IsChecked == true)
            {
                if (!RegistryValueCodec.TryParse(3, rawText.Text ?? "", rawHex: true, out var bytes, out var bad) || bytes.Length == 0)
                {
                    error.Text = bad ?? "Enter at least one raw byte.";
                    return;
                }
                pattern = bytes;
            }
            var query = new RegistrySearchQuery(root, term.Text ?? "", keyNames.IsChecked == true,
                valueNames.IsChecked == true, typed.IsChecked == true, pattern, matchCase.IsChecked == true, recursive.IsChecked == true);
            if (query.Text.Length == 0 && pattern is null) { error.Text = "Enter text or raw bytes to find."; return; }
            if (!query.KeyNames && !query.ValueNames && !query.TypedData && pattern is null) { error.Text = "Choose at least one field."; return; }
            shown.Clear();
            set = vm.Services.ResultSets.Create("Registry: " + (query.Text.Length > 0 ? query.Text : Convert.ToHexString(pattern!)),
                $"Registry search below {vm.Services.Providers.Display(root)}");
            running = new CancellationTokenSource();
            start.IsEnabled = false;
            stop.IsEnabled = true;
            var currentSet = set;
            var cancel = running;
            searchTask = Task.Run(() =>
            {
                try
                {
                    var report = RegistrySearch.Run(query, (item, relative) =>
                    {
                        currentSet.Add(item, relative);
                        if (currentSet.Count % 128 == 0) currentSet.NotifyChanged();
                        if (currentSet.Count <= 300)
                            vm.Services.Ui.Post(() => shown.Add(new Row(item, vm.Services.Providers.Display(item.Parent))));
                    }, message => { lock (currentSet.Issues) currentSet.Issues.Add(message); }, cancel.Token);
                    currentSet.IsComplete = !report.StoppedAtLimit;
                    UpdateStatus($"{report.Matches:N0} matches · {report.KeysVisited:N0} keys · {report.ValuesVisited:N0} values · {report.LinksSkipped:N0} links skipped" +
                        (report.StoppedAtLimit ? " · stopped at a limit" : " · complete"));
                }
                catch (OperationCanceledException) { UpdateStatus($"Stopped · {currentSet.Count:N0} matches kept"); }
                catch (Exception ex) { lock (currentSet.Issues) currentSet.Issues.Add(ex.Message); UpdateStatus("Search stopped: " + ex.Message); }
                finally
                {
                    currentSet.NotifyChanged();
                    vm.Services.Ui.Post(() => { stop.IsEnabled = false; start.IsEnabled = true; });
                }
            });
            status.Text = "Searching…";
        };
        stop.Click += (_, _) => running?.Cancel();
        var result = await vm.Dialogs.ShowCustomAsync("Find in Registry", body,
            [new DialogButton("Close", "close", IsCancel: true), new DialogButton("Go to selected", "go"),
             new DialogButton("Show in panel", "show")], term);
        if (result as string == "go" && results.SelectedItem is Row selected)
        {
            running?.Cancel();
            return new SearchDialogResult(SearchDialogOutcome.GoTo, set, selected.Item, running);
        }
        if (result as string == "show" && set is not null)
            return new SearchDialogResult(SearchDialogOutcome.ShowInPanel, set, null, running);
        running?.Cancel();
        return new SearchDialogResult(SearchDialogOutcome.Closed, null, null, null);
    }
}
