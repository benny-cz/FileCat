using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.App.Views;

internal enum FileListDestination { Viewer, Clipboard, TextFile }
internal sealed record FileListDialogResult(FileListRule Rule, bool SelectedOnly, FileListDestination Destination);

/// <summary>The one place where a user composes, previews, saves and runs a metadata-only file-list rule.</summary>
internal static class FileListDialog
{
    internal static async Task<FileListDialogResult?> ShowAsync(MainViewModel vm, TabViewModel tab)
    {
        var saved = vm.Services.Settings.FileListRules ??= [];
        var allRules = FileListRule.BuiltIns.Concat(saved.Where(r => r is not null &&
            !string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 80 && FileListExport.Validate(r) is null &&
            !FileListRule.BuiltIns.Any(b => b.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase))))
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(50).ToList();
        var rules = new ComboBox { ItemsSource = allRules.Select(r => r.Name).ToArray(), MinWidth = 230 };
        var format = new TextBox { Text = allRules[0].Template, MinWidth = 360,
            PlaceholderText = "For example, {Filename}.{Ext} - to remove" };
        AutomationProperties.SetName(format, "Line format with file-list fields");
        var recursive = new CheckBox { Content = "Include subfolders (do not follow links)" };
        var files = new CheckBox { Content = "Files", IsChecked = true };
        var folders = new CheckBox { Content = "Folders", IsChecked = true };
        var scope = new ComboBox { ItemsSource = new[] { "All items in this folder", "Selected or focused items" },
            SelectedIndex = tab.Listing.HasMarks ? 1 : 0, MinWidth = 230 };
        var destination = new ComboBox { ItemsSource = new[] { "Open in viewer", "Copy to clipboard", "Save as text file…" },
            SelectedIndex = 0, MinWidth = 230 };
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MinHeight = 44, MaxHeight = 64 };
        AutomationProperties.SetName(preview, "Example formatted line");
        var feedback = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "small" } };
        var save = new Button { Content = "Save rule as…" };
        var remove = new Button { Content = "Remove saved rule" };

        FileListRule Current() => new("Current", format.Text ?? string.Empty,
            recursive.IsChecked == true, files.IsChecked == true, folders.IsChecked == true);

        void Update()
        {
            var rule = Current();
            var error = FileListExport.Validate(rule);
            feedback.Text = error ?? "Choose fields above. Use {{ and }} for literal braces; control characters in names are escaped.";
            preview.Text = error is not null ? "Fix the format to see an example." : Example(rule, tab, vm);
            remove.IsEnabled = rules.SelectedIndex >= FileListRule.BuiltIns.Length;
        }

        rules.SelectionChanged += (_, _) =>
        {
            if (rules.SelectedIndex < 0 || rules.SelectedIndex >= allRules.Count) return;
            var rule = allRules[rules.SelectedIndex];
            format.Text = rule.Template;
            recursive.IsChecked = rule.Recursive;
            files.IsChecked = rule.IncludeFiles;
            folders.IsChecked = rule.IncludeFolders;
            Update();
        };
        format.PropertyChanged += (_, e) => { if (e.Property == TextBox.TextProperty) Update(); };
        recursive.IsCheckedChanged += (_, _) => Update();
        files.IsCheckedChanged += (_, _) => Update();
        folders.IsCheckedChanged += (_, _) => Update();

        save.Click += async (_, _) =>
        {
            if (FileListExport.Validate(Current()) is { } error)
            {
                feedback.Text = error;
                format.Focus();
                return;
            }
            var response = await vm.Dialogs.PromptAsync(new PromptOptions("Save file-list rule", "Give this reusable format a name:")
            {
                Text = rules.SelectedIndex >= FileListRule.BuiltIns.Length ? allRules[rules.SelectedIndex].Name : string.Empty,
                Validate = name => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80
                    ? "Use a name of 1–80 characters."
                    : FileListRule.BuiltIns.Any(b => b.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
                        ? "That name belongs to a built-in rule." : null,
            });
            if (response is null) return;
            string name = response.Text.Trim();
            var rule = Current() with { Name = name };
            int existing = saved.FindIndex(r => r is not null && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0) saved[existing] = rule;
            else if (saved.Count < 50) saved.Add(rule);
            else { feedback.Text = "You can save up to 50 custom rules. Remove one first."; return; }
            vm.Services.SaveSettings();
            int slot = allRules.FindIndex(r => r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (slot >= 0) allRules[slot] = rule;
            else { slot = allRules.Count; allRules.Add(rule); }
            rules.ItemsSource = allRules.Select(r => r.Name).ToArray();
            rules.SelectedIndex = slot;
            feedback.Text = $"Saved “{name}”. Select it here next time.";
        };
        remove.Click += (_, _) =>
        {
            if (rules.SelectedIndex < FileListRule.BuiltIns.Length || rules.SelectedIndex >= allRules.Count) return;
            string name = allRules[rules.SelectedIndex].Name;
            saved.RemoveAll(r => r is not null && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            allRules.RemoveAt(rules.SelectedIndex);
            vm.Services.SaveSettings();
            rules.ItemsSource = allRules.Select(r => r.Name).ToArray();
            rules.SelectedIndex = 0;
        };

        var tokens = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 6, LineSpacing = 6 };
        var fieldHints = new (string Token, string Meaning)[]
        {
            ("{Name}", "full name"), ("{Filename}", "name without extension"),
            ("{Ext}", "extension without dot"), ("{Size}", "bytes"),
            ("{Modified}", "UTC modified time; {Created} inserts creation time"),
            ("{Path}", "full path"), ("{RelativePath}", "path from this folder"),
            ("{Type}", "File or Folder"),
        };
        foreach (var (token, meaning) in fieldHints)
        {
            var button = new Button { Content = token, Padding = new Thickness(6, 2) };
            ToolTip.SetTip(button, $"{token}: {meaning}. Insert at the cursor.");
            button.Click += (_, _) =>
            {
                int a = Math.Min(format.SelectionStart, format.SelectionEnd);
                int b = Math.Max(format.SelectionStart, format.SelectionEnd);
                string text = format.Text ?? string.Empty;
                a = Math.Clamp(a, 0, text.Length); b = Math.Clamp(b, a, text.Length);
                format.Text = text[..a] + token + text[b..];
                format.SelectionStart = format.SelectionEnd = a + token.Length;
                format.Focus();
            };
            tokens.Children.Add(button);
        }

        static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold };
        static Grid LabeledChoice(string text, ComboBox choice)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("145,*"), ColumnSpacing = 8 };
            row.Children.Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(choice, 1);
            row.Children.Add(choice);
            return row;
        }
        var content = new StackPanel { Spacing = 5, MinWidth = 430, MaxWidth = 600,
            Children =
            {
                LabeledChoice("What to list", scope),
                new TextBlock { Text = "Marked items take priority over the focused item. “All items” includes entries hidden by a panel filter.",
                    TextWrapping = TextWrapping.Wrap, Classes = { "small" } },
                recursive,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { files, folders } },
                LabeledChoice("Format each line", rules), format, tokens,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { save, remove } },
                feedback, Label("Example"), preview,
                LabeledChoice("Send the list to", destination),
                new TextBlock { Text = "Metadata only. The export stops if a folder cannot be read; no partial list is copied or saved.",
                    TextWrapping = TextWrapping.Wrap, Classes = { "small" } },
            } };
        rules.SelectedIndex = 0;
        Update();
        var result = await vm.Dialogs.ShowCustomAsync("Create file list", content,
            [new DialogButton("Create list", true, IsDefault: true), new DialogButton("Cancel", false, IsCancel: true)],
            format, () => FileListExport.Validate(Current()) is null &&
                (scope.SelectedIndex != 1 || tab.Listing.HasMarks ||
                 tab.Listing.TryGetFocused(out var focused) && focused.Kind != EntryKind.Parent), resizable: true,
            behavior: new DialogBehavior { DismissOnBackdrop = false, PreferredSize = new Size(700, 520) });
        return result is true ? new FileListDialogResult(Current(), scope.SelectedIndex == 1,
            (FileListDestination)Math.Max(0, destination.SelectedIndex)) : null;
    }

    private static string Example(FileListRule rule, TabViewModel tab, MainViewModel vm)
    {
        if (tab.Location is not { } root) return "Open a folder to preview a line.";
        for (int i = 0; i < Math.Min(tab.Listing.VisibleCount, 32); i++)
        {
            var entry = tab.Listing.GetVisible(i);
            if (entry.Kind is not (EntryKind.File or EntryKind.Directory)) continue;
            string path = root.IsFileSystem ? Path.Join(root.Path, entry.Name)
                : vm.Services.Providers.For(root).GetDisplayPath(root).TrimEnd('/', '\\') + "/" + entry.Name;
            return FileListExport.Format(rule.Template, entry, path, entry.Name);
        }
        return "No file or folder is visible yet. The selected format will be used for each exported entry.";
    }
}
