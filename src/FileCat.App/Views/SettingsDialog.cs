using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.State;

namespace FileCat.App.Views;

/// <summary>Settings grouped by task; every change is applied immediately after OK and saved to settings.json.</summary>
public static class SettingsDialog
{
    public static async Task ShowAsync(MainViewModel vm)
    {
        var s = vm.Services.Settings;
        var tabs = new TabControl { MinWidth = 720, MinHeight = 420 };

        // ---- Appearance
        // Choosing a theme shows it at once; Cancel goes back to the one in use.
        var themeNames = ThemeManager.Names;
        var theme = new ComboBox { ItemsSource = themeNames.Select(ThemeManager.DisplayName).ToList(), SelectedIndex = Math.Max(0, themeNames.ToList().IndexOf(s.Theme)), MinWidth = 220 };
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedIndex >= 0) ThemeManager.Apply(themeNames[theme.SelectedIndex]);
        };
        var animations = new CheckBox { Content = "Animated themes move (rain, drifting light, glitches)", IsChecked = s.ThemeAnimations };
        var fontSize = new NumericUpDown { Minimum = 9, Maximum = 24, Increment = 1, Value = (decimal)s.FontSize, Width = 130, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var keyBar = new CheckBox { Content = "Show the function-key bar", IsChecked = s.ShowFunctionKeyBar };
        var cmdLine = new CheckBox { Content = "Show the command line", IsChecked = s.ShowCommandLine };
        var dateFormat = new ComboBox { ItemsSource = new[] { "Culture", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm", "MM/dd/yyyy h:mm tt" }, SelectedItem = s.DateFormat, MinWidth = 220 };
        tabs.Items.Add(new TabItem { Header = "Appearance", Content = Form(("Theme", theme), ("", animations), ("List font size", fontSize), ("Date format", dateFormat), ("", keyBar), ("", cmdLine),
            ("", Note("View → Theme… shows every theme with a preview. When Windows high contrast is on, FileCat always uses its high-contrast theme; the system's reduce-motion setting stops animations."))) });

        // ---- Behavior
        var hidden = new CheckBox { Content = "Show hidden and system items (dimmed)", IsChecked = s.ShowHidden };
        var natural = new CheckBox { Content = "Natural sort (file2 before file10)", IsChecked = s.NaturalSort };
        var dirsFirst = new CheckBox { Content = "Folders before files", IsChecked = s.DirectoriesFirst };
        var confirmRecycle = new CheckBox { Content = "Ask before moving items to the Recycle Bin", IsChecked = s.ConfirmRecycle };
        var sizeOnSpace = new CheckBox { Content = "Space computes the size of a marked folder", IsChecked = s.SizeFolderOnSpace };
        var sizeSlow = new CheckBox { Content = "…also on network, removable, and cloud locations", IsChecked = s.SizeFolderOnSlowLocations };
        var anywhere = new CheckBox { Content = "Quick search matches anywhere in the name (instead of the beginning)", IsChecked = s.QuickSearchMatchAnywhere };
        var savedFilters = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 80,
            MinWidth = 460,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = string.Join(Environment.NewLine, s.SavedFilters.Select(f => $"{f.Name} = {f.Mask}")),
            PlaceholderText = "images = *.png;*.jpg;*.gif\ncode = *.cs;*.axaml|*.g.cs",
        };
        var single = new CheckBox { Content = "One FileCat window per profile (a new launch opens its paths in the running window)", IsChecked = s.SingleInstance };
        var awake = new CheckBox { Content = "Keep the computer awake while operations run", IsChecked = s.KeepAwakeDuringJobs };
        var verify = new ComboBox { ItemsSource = new[] { "Native", "ReadBack" }, SelectedItem = s.DefaultVerify, MinWidth = 220 };
        tabs.Items.Add(new TabItem { Header = "Behavior", Content = Form(("", hidden), ("", natural), ("", dirsFirst), ("", confirmRecycle), ("", sizeOnSpace), ("", sizeSlow), ("", anywhere), ("", single), ("", awake), ("Default copy verification", verify),
            ("Saved filters", savedFilters),
            ("", Note("One per line: name = mask. Use them as @name in any mask: select (Num+), quick filter, copy filters, Find, and compare."))) });

        // ---- Tools
        var editorExe = new TextBox { Text = s.Editor?.Executable ?? string.Empty, PlaceholderText = "auto: " + Core.Tools.ToolLauncher.DetectEditor().Executable, MinWidth = 460 };
        var editorArgs = new TextBox { Text = s.Editor is null ? "{files}" : string.Join(" ", s.Editor.Arguments), MinWidth = 460 };
        var shell = new ComboBox { ItemsSource = OperatingSystem.IsWindows() ? new[] { "cmd", "powershell", "pwsh", "wt" } : new[] { "posix" }, SelectedItem = s.Terminal.Shell, MinWidth = 220 };
        var userCommands = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 120,
            MinWidth = 460,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = Core.Tools.UserCommandsText.Format(s.UserCommands),
        };
        var associations = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 90,
            MinWidth = 460,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = Core.Tools.Associations.Format(s.Associations),
            PlaceholderText = @"*.pdf | view | C:\Tools\SumatraPDF.exe | {file}",
        };
        tabs.Items.Add(new TabItem
        {
            Header = "Tools",
            Content = Form(("Editor (F4) program", editorExe), ("Editor arguments", editorArgs), ("Command line shell", shell), ("User commands (F9)", userCommands),
                ("Associations", associations),
                ("", Note("Associations, one per line: mask | view, edit, or open | program | arguments. F3, F4, and Enter use the first matching line; Alt+F3 always opens the internal viewer.")),
                ("", Note("One command per line: Name | program | arguments | options. \"Group > Name\" puts a command in a submenu. Quote arguments that contain spaces. Options: key=HOTKEY, dir=FOLDER, shell. Tokens: {file} {files} {listfile} {dir} {target} {name} {prompt}. Programs must be real executables; batch files are refused when an argument contains shell metacharacters unless the command has the shell option."))),
        });

        // ---- Columns
        var columnsEditor = new ColumnProfilesEditor(vm.Services.Columns);
        tabs.Items.Add(new TabItem { Header = "Columns", Content = columnsEditor.View });

        // ---- Keyboard
        var bindings = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 260,
            MinWidth = 600,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = string.Join(Environment.NewLine, s.KeyBindings.Select(kv => $"{kv.Key} = {string.Join(", ", kv.Value)}")),
            PlaceholderText = "command.id = Ctrl+Shift+X, F12   (one per line; empty uses the defaults)",
        };
        var reference = new TextBlock { Text = "Command ids are listed in the keyboard reference (F1) and the command palette (Ctrl+Shift+P).", Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };
        tabs.Items.Add(new TabItem { Header = "Keyboard", Content = Form(("Custom bindings", bindings), ("", reference)) });

        // ---- Privacy
        var diag = new CheckBox { Content = "Diagnostic mode: include file paths in the local log (off by default)", IsChecked = s.DiagnosticMode };
        var updates = new CheckBox { Content = "Check for new versions (only notifies; never downloads). Off means FileCat makes no network requests.", IsChecked = s.CheckForUpdates };
        bool helper = vm.Services.ShellPictures is not null;
        var shellPictures = new CheckBox
        {
            Content = "Shell pictures: thumbnails in quick view and programs' own icons, from Windows' handlers run in a separate restricted process",
            IsChecked = s.ShellPictures,
            IsEnabled = helper,
        };
        var shellSlow = new CheckBox
        {
            Content = "…also on network and removable drives (their handlers may contact other computers)",
            IsChecked = s.ShellPicturesOnNetworkAndRemovable,
            IsEnabled = helper,
        };
        var shellNote = Note(helper
            ? "Shortcuts, internet shortcuts, libraries, themes, desktop.ini, and cloud placeholders are never handed to the Shell." +
              (vm.Services.ShellPictures?.DisabledReason is { } off ? " " + off : string.Empty)
            : "This build has no Shell helper, so icons come from file types only.");
        var paths = new SelectableTextBlock { Text = $"Settings: {vm.Services.Paths.SettingsDirectory}\nLocal data: {vm.Services.Paths.LocalDirectory}{(vm.Services.Paths.IsPortable ? "\n(portable mode)" : string.Empty)}", TextWrapping = TextWrapping.Wrap };
        tabs.Items.Add(new TabItem { Header = "Privacy", Content = Form(("", diag), ("", updates), ("", shellPictures), ("", shellSlow), ("", shellNote), ("Storage", paths)) });

        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var body = new StackPanel { Spacing = 6, Children = { tabs, error } };
        if (vm.Services.SettingsReadOnly)
            body.Children.Insert(0, new TextBlock { Text = "These settings were written by a newer FileCat and are read-only in this version.", Classes = { "warning" } });

        while (true)
        {
            var r = await vm.Dialogs.ShowCustomAsync("Settings", body, [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("OK", "ok", IsDefault: true)]);
            if (r as string != "ok")
            {
                ThemeManager.Apply(s.Theme);
                return;
            }

            var parsedBindings = ParseBindings(bindings.Text, vm.Services.Commands, out var bindingError);
            if (bindingError is not null)
            {
                error.Text = bindingError;
                error.IsVisible = true;
                tabs.SelectedIndex = 4;
                continue;
            }
            var parsedFilters = ParseSavedFilters(savedFilters.Text, out var filterError);
            if (filterError is not null)
            {
                error.Text = filterError;
                error.IsVisible = true;
                tabs.SelectedIndex = 1;
                continue;
            }
            var parsedAssociations = Core.Tools.Associations.Parse(associations.Text, out var associationError);
            var parsedCommands = Core.Tools.UserCommandsText.Parse(userCommands.Text, out var commandError);
            commandError ??= associationError;
            if (commandError is not null)
            {
                error.Text = commandError;
                error.IsVisible = true;
                tabs.SelectedIndex = 2;
                continue;
            }

            s.Theme = theme.SelectedIndex >= 0 ? themeNames[theme.SelectedIndex] : "System";
            s.ThemeAnimations = animations.IsChecked == true;
            s.FontSize = (double)(fontSize.Value ?? 13);
            s.DateFormat = dateFormat.SelectedItem as string ?? "Culture";
            s.ShowFunctionKeyBar = keyBar.IsChecked == true;
            s.ShowCommandLine = cmdLine.IsChecked == true;
            s.ShowHidden = hidden.IsChecked == true;
            s.NaturalSort = natural.IsChecked == true;
            s.DirectoriesFirst = dirsFirst.IsChecked == true;
            s.ConfirmRecycle = confirmRecycle.IsChecked == true;
            s.SizeFolderOnSpace = sizeOnSpace.IsChecked == true;
            s.SizeFolderOnSlowLocations = sizeSlow.IsChecked == true;
            s.QuickSearchMatchAnywhere = anywhere.IsChecked == true;
            s.SingleInstance = single.IsChecked == true;
            s.KeepAwakeDuringJobs = awake.IsChecked == true;
            s.DefaultVerify = verify.SelectedItem as string ?? "Native";
            s.SavedFilters = parsedFilters;
            s.Editor = string.IsNullOrWhiteSpace(editorExe.Text) ? null : new ToolDefinition
            {
                Name = Path.GetFileNameWithoutExtension(editorExe.Text.Trim()),
                Executable = editorExe.Text.Trim().Trim('"'),
                Arguments = (editorArgs.Text ?? "{files}").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList(),
            };
            s.Terminal.Shell = shell.SelectedItem as string ?? s.Terminal.Shell;
            s.UserCommands = parsedCommands;
            s.Associations = parsedAssociations;
            s.KeyBindings = parsedBindings;
            s.DiagnosticMode = diag.IsChecked == true;
            s.CheckForUpdates = updates.IsChecked == true;
            s.ShellPictures = shellPictures.IsChecked == true;
            s.ShellPicturesOnNetworkAndRemovable = shellSlow.IsChecked == true;
            columnsEditor.Apply();
            vm.ApplySettings();
            return;
        }
    }

    private static Control Form(params (string Label, Control Control)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8) };
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var (label, control) = rows[i];
            if (label.Length > 0)
            {
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 12, 4) };
                Grid.SetRow(l, i);
                grid.Children.Add(l);
                // Screen readers announce the field by its visible label.
                if (string.IsNullOrEmpty(Avalonia.Automation.AutomationProperties.GetName(control)))
                    Avalonia.Automation.AutomationProperties.SetName(control, label);
            }
            control.Margin = new Thickness(0, 4);
            Grid.SetRow(control, i);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        return new ScrollViewer { Content = grid, MaxHeight = 520 };
    }

    /// <summary>"name = mask" lines; every mask must parse, including references between saved filters.</summary>
    private static List<SavedFilter> ParseSavedFilters(string? text, out string? error)
    {
        error = null;
        var list = new List<SavedFilter>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            var name = eq > 0 ? line[..eq].Trim() : string.Empty;
            if (name.Length == 0 || name.AsSpan().IndexOfAny("*?;,|\"@ ") >= 0)
            {
                error = $"\"{line}\": expected name = mask, with a name of letters, digits, - or _.";
                return list;
            }
            if (list.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                error = $"The saved filter \"{name}\" is defined twice.";
                return list;
            }
            list.Add(new SavedFilter { Name = name, Mask = line[(eq + 1)..].Trim() });
        }
        var previous = Core.Selection.Mask.SavedFilters;
        Core.Selection.Mask.SavedFilters = n => list.FirstOrDefault(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))?.Mask;
        try
        {
            foreach (var f in list)
            {
                if (!Core.Selection.Mask.TryParse(f.Mask, out _, out var maskError))
                {
                    error = $"Saved filter \"{f.Name}\": {maskError}";
                    return list;
                }
            }
        }
        finally
        {
            Core.Selection.Mask.SavedFilters = previous;
        }
        return list;
    }

    private static TextBlock Note(string text) => new() { Text = text, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 };

    private static Dictionary<string, string[]> ParseBindings(string? text, CommandRegistry registry, out string? error)
    {
        error = null;
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                error = $"\"{line}\": expected command.id = Gesture.";
                return result;
            }
            var id = line[..eq].Trim();
            if (registry.Get(id) is null)
            {
                error = $"Unknown command id \"{id}\".";
                return result;
            }
            var gestures = line[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var g in gestures)
            {
                if (!KeyChord.TryParse(g, out _))
                {
                    error = $"\"{g}\" is not a key combination (e.g. Ctrl+Shift+F5, Num+, Alt+Left).";
                    return result;
                }
            }
            result[id] = gestures;
        }
        return result;
    }
}
