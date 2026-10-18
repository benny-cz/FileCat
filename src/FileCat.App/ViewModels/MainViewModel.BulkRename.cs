using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.Controls;
using FileCat.App.Views;
using FileCat.Core.Jobs;
using FileCat.Core.FileSystem;
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
        using var capturedSelection = sel.Value.Items as IDisposable;
        if (sel.Value.Items.Count > MaxBulkRename) { Notify($"Rename at most {MaxBulkRename:N0} items at a time.", true); return; }
        var sourceTab = sel.Value.Tab;
        var sourceLocation = sourceTab.Location;
        var allItems = sel.Value.Items.ToList();
        var visibleSelection = sourceTab.Listing.GetSelection(includeHiddenMarks: false);
        using var capturedVisibleSelection = visibleSelection as IDisposable;
        var visibleItems = visibleSelection.ToList();
        var items = sel.Value.HiddenMarked > 0 ? visibleItems : allItems;
        if (allItems.Any(i => i.FileSystemPath is null))
        {
            Notify("Bulk rename works on files and folders on disk. Archive members are renamed with F2.", true);
            return;
        }
        // Both scopes are captured before a dialog/editor can yield. Persistent Commander marks remain untouched.
        var hidden = new CheckBox
        {
            Content = $"Include {Formatters.Plural(sel.Value.HiddenMarked, "marked item", "marked items")} hidden by the filter",
            IsChecked = false,
            IsVisible = sel.Value.HiddenMarked > 0,
        };
        var scopeNotice = new TextBlock
        {
            Text = $"{Formatters.Plural(sel.Value.HiddenMarked, "marked item is", "marked items are")} hidden by the current filter. Only visible marked items are included unless you choose otherwise.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 720, IsVisible = sel.Value.HiddenMarked > 0,
            Classes = { "warning" },
        };
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
        var preview = new BulkRenameReview();
        var summary = new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, TextWrapping = TextWrapping.Wrap, MaxWidth = 720 };
        var editorButton = new Button { Content = "Edit names in editor…" };
        IReadOnlyList<string>? explicitNames = null;
        IReadOnlyList<RenamePreview> rows = [];
        RenameRules? plannedFor = null;
        int scopeRevision = 0, plannedScope = -1;
        bool dialogOpen = true;

        RenameRules Rules() => new(nameMask.Text ?? "", extMask.Text ?? "", search.Text ?? "", replace.Text ?? "", regex.IsChecked == true,
            matchCase.IsChecked == true, (RenameCase)Math.Max(0, caseBox.SelectedIndex),
            long.TryParse(counterStart.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? s : 1,
            long.TryParse(counterStep.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var st) ? st : 1,
            int.TryParse(counterDigits.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? Math.Clamp(d, 1, 12) : 1);

        void Refresh()
        {
            plannedFor = Rules();
            plannedScope = scopeRevision;
            rows = BulkRenamePlanner.Preview(items, plannedFor, p => File.Exists(p) || Directory.Exists(p), explicitNames);
            int changing = rows.Count(r => r.Changes), problems = rows.Count(r => r.Problem is not null);
            preview.SetRows(rows);
            summary.Text = $"{changing:N0} of {rows.Count:N0} names change" + (problems > 0 ? $" · {problems:N0} problems block the rename" : "") +
                           (sel.Value.HiddenMarked > 0 ? hidden.IsChecked == true ? $" · includes {sel.Value.HiddenMarked:N0} hidden marked items" : $" · {sel.Value.HiddenMarked:N0} hidden marked items excluded" : "") +
                           (explicitNames is not null ? " · names come from the editor; change a rule to go back to the rules" : "");
            summary.Classes.Set("error", problems > 0);
        }
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };
        hidden.IsCheckedChanged += (_, _) =>
        {
            if (!dialogOpen) return;
            scopeRevision++;
            plannedScope = -1;
            items = hidden.IsChecked == true ? allItems : visibleItems;
            explicitNames = null;
            debounce.Stop();
            Refresh();
        };
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
            int editedScope = scopeRevision;
            if (await EditNamesInEditorAsync(rows) is { } names)
            {
                if (!dialogOpen) return;
                if (editedScope != scopeRevision)
                {
                    Notify("The rename scope changed while the names were being edited. Review the current items and edit their names again.", true);
                    return;
                }
                explicitNames = names;
                Refresh();
            }
        };

        static Control Field(string label, Control input) => new StackPanel
        {
            Spacing = 2,
            Children = { new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, input },
        };
        var body = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                scopeNotice,
                hidden,
                DialogLayout.Actions(Field("Name:", nameMask), Field("Extension:", extMask)),
                new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 720,
                    Text = "[N] name · [N2-5] characters 2 to 5 · [N3-] from 3 on · [E] extension · [C] counter · [P] folder · [YMD] date · [hms] time; other text is kept as typed",
                },
                DialogLayout.Actions(Field("Search:", search), Field("Replace:", replace)),
                DialogLayout.Actions(regex, matchCase, Field("Case:", caseBox)),
                DialogLayout.Actions(Field("Counter from", counterStart), Field("Step", counterStep), Field("Digits", counterDigits)),
                preview,
                summary,
                DialogLayout.Actions(editorButton),
            },
        };
        Refresh();
        var answer = await Dialogs.ShowCustomAsync("Bulk rename", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Rename", "rename", IsDefault: true)], nameMask,
            // Only a preview of the rules as they stand can run (explicit names replace the rules until one changes).
            () => plannedScope == scopeRevision && (explicitNames is not null || plannedFor == Rules()) && rows.All(r => r.Problem is null) && rows.Any(r => r.Changes));
        dialogOpen = false;
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
        if (sourceLocation is not null) _jobOrigins[job] = (sourceTab, sourceLocation);
        if (!sourceTab.Listing.IsDisposed && sourceTab.Location == sourceLocation)
            sourceTab.Listing.RememberOperation(items);
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
                (IReadOnlyList<string> lines, int count) edited;
                try { edited = await Task.Run(() => ReadEditedNames(file, rows.Count)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    await Dialogs.AlertAsync("Name list cannot be loaded", ex.Message);
                    continue;
                }
                if (edited.count == rows.Count) return edited.lines;
                await Dialogs.AlertAsync("Line count changed",
                    $"The file has {edited.count:N0} lines, but {rows.Count:N0} names are needed. Keep one line per item in the same order, save, and load again.");
            }
        }
        finally
        {
            try { File.Delete(file); }
            catch (IOException) { }
        }
    }

    internal static (IReadOnlyList<string> lines, int count) ReadEditedNames(string file, int expected)
    {
        // The external editor can replace this private path with a large file. Check and
        // read the same opened handle, then retain only the names this operation can use.
        long maxBytes = Math.Min(64L * 1024 * 1024, (long)expected * 4096 + 4096);
        using var stream = OperatingSystem.IsWindows()
            ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)
            : new FileStream(UnixFiles.OpenRegularFileForRead(file), FileAccess.Read);
        if (stream.Length > maxBytes)
            throw new InvalidDataException($"The edited name list exceeds its {maxBytes:N0}-byte limit. Save a shorter list and load it again.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            throw new InvalidDataException("The edited name list grew while being read. Save it and load it again.");
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);
        var names = new List<string>(Math.Min(expected, 4096));
        int count = 0, trailingEmpty = 0;
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 4096)
                throw new InvalidDataException("An edited name is longer than 4,096 characters. Shorten it and load the list again.");
            count++;
            trailingEmpty = line.Length == 0 ? trailingEmpty + 1 : 0;
            if (names.Count < expected) names.Add(line);
        }
        // Match the old editor round-trip: ignore surplus blank lines only after
        // the expected number of entries (an intentional blank entry still fails preview).
        count = Math.Max(count - trailingEmpty, Math.Min(count, expected));
        return (names, count);
    }
}
