using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>
/// Bulk rename (Ctrl+M, Total Commander's Multi-Rename Tool; plan §23.3, OPS-007): rules are previewed live for every
/// item, the plan runs only without problems, and Undo renames everything back (guarded by identity).
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxBulkRename = 50_000;

    private async Task BulkRenameAsync()
    {
        var sel = SourceSelection();
        if (sel is null) return;
        var items = sel.Value.Items.ToList();
        if (items.Any(i => i.FileSystemPath is null))
        {
            Notify("Bulk rename works on files and folders on disk. Archive members are renamed with F2.", true);
            return;
        }
        if (items.Count > MaxBulkRename) { Notify($"Rename at most {MaxBulkRename:N0} items at a time.", true); return; }

        TextBox Box(string text, string name, double width = 200)
        {
            var box = new TextBox { Text = text, MinWidth = width };
            Avalonia.Automation.AutomationProperties.SetName(box, name);
            return box;
        }
        var nameMask = Box("[N]", "Name mask", 260);
        var extMask = Box("[E]", "Extension mask", 120);
        var search = Box("", "Search for");
        var replace = Box("", "Replace with");
        var regex = new CheckBox { Content = "Regular expression", VerticalAlignment = VerticalAlignment.Center };
        var matchCase = new CheckBox { Content = "Match case", VerticalAlignment = VerticalAlignment.Center };
        var caseBox = new ComboBox { ItemsSource = new[] { "Unchanged", "lower case", "UPPER CASE", "First letter upper", "Title Case" }, SelectedIndex = 0, MinWidth = 160 };
        Avalonia.Automation.AutomationProperties.SetName(caseBox, "Letter case");
        var counterStart = Box("1", "Counter start", 60);
        var counterStep = Box("1", "Counter step", 50);
        var counterDigits = Box("1", "Counter digits", 40);
        var preview = new ListBox { Height = 280, MinWidth = 700 };
        Avalonia.Automation.AutomationProperties.SetName(preview, "Preview of new names");
        var summary = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 720 };
        var editorButton = new Button { Content = "Edit names in editor…" };
        IReadOnlyList<string>? explicitNames = null;
        IReadOnlyList<RenamePreview> rows = [];

        RenameRules Rules() => new(nameMask.Text ?? "", extMask.Text ?? "", search.Text ?? "", replace.Text ?? "", regex.IsChecked == true,
            matchCase.IsChecked == true, (RenameCase)Math.Max(0, caseBox.SelectedIndex),
            long.TryParse(counterStart.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 1,
            long.TryParse(counterStep.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : 1,
            int.TryParse(counterDigits.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 12) : 1);

        void Refresh()
        {
            rows = BulkRenamePlanner.Preview(items, Rules(), p => File.Exists(p) || Directory.Exists(p), explicitNames);
            int changing = rows.Count(r => r.Changes), problems = rows.Count(r => r.Problem is not null);
            preview.ItemsSource = rows.Take(2000).Select(r => r.Problem is not null ? $"⚠ {r.OldName}  →  {r.NewName}    ({r.Problem})"
                : r.Changes ? $"{r.OldName}  →  {r.NewName}" : $"{r.OldName}  (unchanged)").ToList();
            summary.Text = $"{changing:N0} of {rows.Count:N0} names change" + (problems > 0 ? $" · {problems:N0} problems block the rename" : "") +
                           (rows.Count > 2000 ? " · the first 2,000 are shown" : "") +
                           (explicitNames is not null ? " · names come from the editor; change a rule to go back to the rules" : "");
            summary.Classes.Set("error", problems > 0);
        }
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        void RulesChanged()
        {
            explicitNames = null;
            debounce.Stop();
            debounce.Start();
        }
        foreach (var box in new[] { nameMask, extMask, search, replace, counterStart, counterStep, counterDigits }) box.TextChanged += (_, _) => RulesChanged();
        regex.IsCheckedChanged += (_, _) => RulesChanged();
        matchCase.IsCheckedChanged += (_, _) => RulesChanged();
        caseBox.SelectionChanged += (_, _) => RulesChanged();
        editorButton.Click += async (_, _) =>
        {
            if (await EditNamesInEditorAsync(rows) is { } names)
            {
                explicitNames = names;
                Refresh();
            }
        };

        StackPanel Row(params Control[] controls)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var c in controls)
            {
                if (c is TextBlock t) t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(c);
            }
            return row;
        }
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Row(new TextBlock { Text = "Name:" }, nameMask, new TextBlock { Text = "Extension:" }, extMask),
                new TextBlock
                {
                    Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 720,
                    Text = "[N] name · [N2-5] characters 2 to 5 · [N3-] from 3 on · [E] extension · [C] counter · [P] folder · [YMD] date · [hms] time; other text is kept as typed",
                },
                Row(new TextBlock { Text = "Search:" }, search, new TextBlock { Text = "Replace:" }, replace, regex, matchCase),
                Row(new TextBlock { Text = "Case:" }, caseBox, new TextBlock { Text = "Counter from" }, counterStart,
                    new TextBlock { Text = "step" }, counterStep, new TextBlock { Text = "digits" }, counterDigits),
                preview,
                summary,
                Row(editorButton),
            },
        };
        Refresh();
        var answer = await Dialogs.ShowCustomAsync($"Rename {Formatters.Plural(items.Count, "item", "items")}", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Rename", "rename", IsDefault: true)], nameMask,
            () => rows.All(r => r.Problem is null) && rows.Any(r => r.Changes));
        debounce.Stop();
        if (answer as string != "rename") return;
        Refresh();
        if (rows.Any(r => r.Problem is not null) || !rows.Any(r => r.Changes)) return;
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Rename,
            Sources = items,
            NewNames = rows.Select(r => r.NewName).ToList(),
            Description = $"Rename {Formatters.Plural(rows.Count(r => r.Changes), "item", "items")}",
        });
        if (ActiveTab is { } tab)
        {
            Track(job, tab);
            tab.Listing.RememberOperation(items);
        }
    }

    /// <summary>
    /// The editor round-trip: one new name per line in the configured editor; FileCat reads the file back when you say
    /// you are done (an editor's process exit is not a reliable "done" signal).
    /// </summary>
    private async Task<IReadOnlyList<string>?> EditNamesInEditorAsync(IReadOnlyList<RenamePreview> rows)
    {
        Directory.CreateDirectory(Services.Paths.TempDirectory);
        string file = Path.Combine(Services.Paths.TempDirectory, $"rename-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(file, rows.Select(r => r.NewName));
        try
        {
            LaunchEditor(file);
            while (true)
            {
                if (!await Dialogs.ConfirmAsync("Edit names in the editor",
                        $"Change the names in the editor (one per line, {rows.Count:N0} lines, same order), save the file, then choose Load names.",
                        "Load names"))
                    return null;
                var lines = (await File.ReadAllLinesAsync(file)).ToList();
                while (lines.Count > rows.Count && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
                if (lines.Count == rows.Count) return lines;
                await Dialogs.AlertAsync("Line count changed",
                    $"The file has {lines.Count:N0} lines, but {rows.Count:N0} names are needed. Keep one line per item in the same order, save, and load again.");
            }
        }
        finally
        {
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }
}
