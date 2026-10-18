using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.Core.Tools;
using FileCat.App.Services;
using FileCat.App.Controls;
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
        // The shared modal owns vertical scrolling. Tabs wrap with the available client width.
        var tabs = new TabControl { ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()) };

        // ---- Appearance
        // Choosing a theme shows it at once; Cancel goes back to the one in use.
        var themeNames = ThemeManager.Names;
        var theme = new ComboBox { ItemsSource = themeNames.Select(ThemeManager.DisplayName).ToList(), SelectedIndex = Math.Max(0, themeNames.ToList().IndexOf(s.Theme)), MinWidth = 220 };
        var themeDescription = Note(ThemeManager.Description(themeNames[theme.SelectedIndex]));
        theme.SelectionChanged += (_, _) =>
        {
            if (!vm.Services.SettingsReadOnly && theme.SelectedIndex >= 0)
            {
                themeDescription.Text = ThemeManager.Description(themeNames[theme.SelectedIndex]);
                ThemeManager.Apply(themeNames[theme.SelectedIndex]);
            }
        };
        var animations = new CheckBox { Content = "Animate theme effects", IsChecked = s.ThemeAnimations };
        var fontSize = new NumericUpDown { Minimum = 9, Maximum = 24, Increment = 1, Value = (decimal)s.FontSize, Width = 130, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var keyBar = new CheckBox { Content = "Show the function-key bar", IsChecked = s.ShowFunctionKeyBar };
        var toolbar = new CheckBox { Content = "Show the toolbar", IsChecked = s.ShowToolbar };
        var driveButtons = new CheckBox { Content = "Show place buttons above the panels (drives, folders, bookmarks, servers)", IsChecked = s.ShowDriveButtons };
        var cmdLine = new CheckBox { Content = "Show the command line", IsChecked = s.ShowCommandLine };
        // With seconds first; the shorter forms stay for those who chose them, and a format typed into the settings file
        // is offered as it is, so saving never replaces it.
        var dateFormats = new List<string> { "Culture", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm:ss", "MM/dd/yyyy h:mm:ss tt", "yyyy-MM-dd HH:mm", "dd.MM.yyyy HH:mm", "MM/dd/yyyy h:mm tt" };
        if (!dateFormats.Contains(s.DateFormat)) dateFormats.Add(s.DateFormat);
        var dateFormat = new ComboBox { ItemsSource = dateFormats, SelectedItem = s.DateFormat, MinWidth = 220 };
        tabs.Items.Add(new TabItem { Header = "Appearance", Content = Form(("Theme", theme), ("", themeDescription), ("", animations), ("List font size", fontSize), ("Date format", dateFormat), ("", keyBar), ("", toolbar), ("", driveButtons), ("", cmdLine),
            ("", Note("View → Theme… previews every theme. System high contrast takes priority, and the system's reduced-motion setting stops animations."))) });

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
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = string.Join(Environment.NewLine, s.SavedFilters.Select(f => $"{f.Name} = {f.Mask}")),
            PlaceholderText = "images = *.png;*.jpg;*.gif\ncode = *.cs;*.axaml|*.g.cs",
        };
        var single = new CheckBox { Content = "One FileCat window per profile (a new launch opens its paths in the running window)", IsChecked = s.SingleInstance };
        var awake = new CheckBox { Content = "Keep the computer awake while operations run", IsChecked = s.KeepAwakeDuringJobs };
        // Named as the copy dialog names them (stored as Native and ReadBack).
        var verify = new ComboBox { ItemsSource = new[] { "Size and metadata (fast)", "Read back and compare content" }, SelectedIndex = s.DefaultVerify == "ReadBack" ? 1 : 0, MinWidth = 220 };
        // D-57: how large a file beside a checksum or signature may be to be checked when shown.
        var verifyUpTo = new NumericUpDown { Minimum = 0, Maximum = 1 << 20, Increment = 64, FormatString = "0 MiB", Value = s.VerifyAutomaticallyUpToMiB, Width = 160, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        tabs.Items.Add(new TabItem { Header = "Behavior", Content = Form(("", hidden), ("", natural), ("", dirsFirst), ("", confirmRecycle), ("", sizeOnSpace), ("", sizeSlow), ("", anywhere), ("", single), ("", awake), ("Default copy verification", verify),
            ("Verify automatically up to", verifyUpTo),
            ("", Note("Files next to a checksum file or signature (SHA256SUMS, name.sha256, .minisig, .asc…) are checked when shown up to this size; larger ones, and files on the network, on request: File → Verify checksums and signatures. 0 checks only on request.")),
            ("Saved filters", savedFilters),
            ("", Note("One per line: name = mask. Use them as @name in any mask: select (Num+), quick filter, copy filters, Find, and compare."))) });

        // ---- Tools
        var editorExe = new TextBox { Text = s.Editor?.Executable ?? string.Empty, PlaceholderText = "auto: " + Core.Tools.ToolLauncher.DetectEditor().Executable };
        var editorArgs = new TextBox { Text = s.Editor is null ? "{files}" : ToolTextGrammar.FormatArguments(s.Editor.Arguments) };
        // "auto": Windows Terminal when installed, else PowerShell 7, else Windows PowerShell.
        var shell = new ComboBox { ItemsSource = OperatingSystem.IsWindows() ? new[] { "auto", "cmd", "powershell", "pwsh", "wt" } : new[] { "posix" }, SelectedItem = s.Terminal.Shell, MinWidth = 220 };
        var userCommands = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 120,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = Core.Tools.UserCommandsText.Format(s.UserCommands),
            PlaceholderText = OperatingSystem.IsWindows() ? "Edit in Notepad | notepad.exe | {file} | key=Ctrl+Alt+N" : "Edit in gedit | gedit | {file} | key=Ctrl+Alt+N",
        };
        var associations = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 90,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = Core.Tools.Associations.Format(s.Associations),
            PlaceholderText = @"*.pdf | view | C:\Tools\SumatraPDF.exe | {file}",
        };
        tabs.Items.Add(new TabItem
        {
            Header = "Tools",
            // Each box with its own explanation right below it.
            Content = Form(("Editor (F4) program", editorExe), ("Editor arguments", editorArgs),
                ("", Note("Arguments stay separate: quote an argument containing spaces or pipes; doubled quotes represent a literal quote, and backslashes stay literal. \"\" is one empty argument. An empty field means no arguments. The @json: array form preserves embedded line breaks.")), ("Command line shell", shell), ("User commands (F9)", userCommands),
                ("", Note("One command per line: Name | program | arguments | options. Group > Name puts a command in a submenu. Quote arguments that contain spaces. Options: key=HOTKEY, dir=FOLDER, shell. Complex saved definitions use an @tools: JSON array to preserve their complete structure. Tokens: {file} {files} {listfile} {dir} {target} {name} {prompt}. Programs must be real executables; batch files are refused when an argument contains shell metacharacters unless the command has the shell option.")),
                ("Associations", associations),
                ("", Note("Associations: mask | view, edit, or open | program | arguments | options. Quote exclusion masks (\"*.txt|secret*\") and arguments containing pipes or spaces. Optional name=, dir=, key=, shell preserve launch settings. F3, F4, and Enter use the first match; Alt+F3 always opens the internal viewer."))),
        });

        // ---- Columns
        var columnsEditor = new ColumnProfilesEditor(vm.Services.Columns);
        tabs.Items.Add(new TabItem { Header = "Columns", Content = columnsEditor.View });

        // ---- Keyboard
        var bindings = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 260,
            FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
            Text = string.Join(Environment.NewLine, s.KeyBindings.Select(kv => $"{kv.Key} = {string.Join(", ", kv.Value)}")),
            PlaceholderText = "command.id = Ctrl+Shift+X, F12",
        };
        var reference = new TextBlock { Text = "One binding per line; an empty box keeps the defaults. Easier: in the keyboard reference (F1), select a command and press F2, then the new keys. Command ids are listed there.", Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };
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
        {
            // Keep tabs, scrolling and text selection usable while preventing edits and preview side effects.
            foreach (var tab in tabs.Items.OfType<TabItem>())
                if (tab.Content is Control page)
                    foreach (var control in page.GetLogicalDescendants().OfType<Control>())
                        if (control is TextBox textBox) textBox.IsReadOnly = true;
                        else if (control is Button or ComboBox or CheckBox or NumericUpDown) control.IsEnabled = false;
            string reason = vm.Services.SettingsStatus switch
            {
                StateLoadStatus.NewerSchemaReadOnly => "These settings were written by a newer FileCat. They can be inspected here, but this version cannot change them.",
                StateLoadStatus.OversizedReadOnly => "The settings exceed the safe input limit. A backup or defaults are shown for inspection; the original file is preserved.",
                _ => "The settings could not be read safely. A backup or defaults are shown for inspection; the original file is preserved.",
            };
            body.Children.Insert(0, new TextBlock { Text = reason, Classes = { "warning" }, TextWrapping = TextWrapping.Wrap });
            await vm.Dialogs.ShowCustomAsync("Settings — read-only", body,
                [new DialogButton("Close", "close", IsDefault: true, IsCancel: true)]);
            return;
        }

        string originalExecutable = editorExe.Text ?? "", originalArguments = editorArgs.Text ?? "";
        string originalCommands = userCommands.Text ?? "", originalAssociations = associations.Text ?? "";
        var parsedBindings = s.KeyBindings;
        var parsedFilters = s.SavedFilters;
        var parsedCommands = s.UserCommands;
        var parsedAssociations = s.Associations;
        var parsedArguments = s.Editor?.Arguments ?? ["{files}"];
        string parsedExecutable = s.Editor?.Executable ?? "";
        bool Fail(string fieldName, string message, TextBox field, int page)
        {
            error.Text = fieldName + ": " + message;
            error.IsVisible = true;
            Avalonia.Automation.AutomationProperties.SetHelpText(field, error.Text);
            tabs.SelectedIndex = page;
            var owner = TopLevel.GetTopLevel(tabs);
            var previousFocus = owner?.FocusManager?.GetFocusedElement();
            Dispatcher.UIThread.Post(() =>
            {
                if (owner is not null && ReferenceEquals(TopLevel.GetTopLevel(field), owner) &&
                    ReferenceEquals(owner.FocusManager?.GetFocusedElement(), previousFocus) &&
                    field.IsEffectivelyVisible && field.IsEffectivelyEnabled)
                    field.Focus();
            }, DispatcherPriority.Input);
            return false;
        }
        bool Validate()
        {
            // Each attempt starts from the captured structured values; reverting an edit after an error cannot reuse stale parsed data.
            parsedArguments = s.Editor?.Arguments ?? ["{files}"];
            parsedExecutable = s.Editor?.Executable ?? "";
            error.IsVisible = false;
            foreach (var field in new[] { bindings, savedFilters, editorExe, editorArgs, userCommands, associations })
                Avalonia.Automation.AutomationProperties.SetHelpText(field, null);
            parsedBindings = ParseBindings(bindings.Text, vm.Services.Commands, out var bindingError);
            if (bindingError is not null) return Fail("Custom bindings", bindingError, bindings, 4);
            parsedFilters = ParseSavedFilters(savedFilters.Text, out var filterError);
            if (filterError is not null) return Fail("Saved filters", filterError, savedFilters, 1);
            if (editorExe.Text != originalExecutable && !ToolTextGrammar.TryParseField(editorExe.Text, out parsedExecutable, out var executableError))
                return Fail("Editor program", executableError!, editorExe, 2);
            if (editorArgs.Text != originalArguments && !ToolTextGrammar.TryParseArguments(editorArgs.Text, out parsedArguments, out var argumentError))
                return Fail("Editor arguments", argumentError!, editorArgs, 2);
            if (userCommands.Text != originalCommands)
            {
                parsedCommands = UserCommandsText.Parse(userCommands.Text, out var commandError);
                if (commandError is not null) return Fail("User commands", commandError, userCommands, 2);
            }
            else parsedCommands = s.UserCommands;
            if (associations.Text != originalAssociations)
            {
                parsedAssociations = Associations.Parse(associations.Text, out var associationError);
                if (associationError is not null) return Fail("Associations", associationError, associations, 2);
            }
            else parsedAssociations = s.Associations;
            return true;
        }
        var r = await vm.Dialogs.ShowCustomAsync("Settings", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("OK", "ok", IsDefault: true) { Validate = Validate }]);
        if (r as string != "ok")
        {
            ThemeManager.Apply(s.Theme);
            return;
        }

        s.Theme = theme.SelectedIndex >= 0 ? themeNames[theme.SelectedIndex] : "System";
        s.ThemeAnimations = animations.IsChecked == true;
        s.FontSize = (double)(fontSize.Value ?? 13);
        s.DateFormat = dateFormat.SelectedItem as string ?? "Culture";
        s.ShowFunctionKeyBar = keyBar.IsChecked == true;
        s.ShowToolbar = toolbar.IsChecked == true;
        s.ShowDriveButtons = driveButtons.IsChecked == true;
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
        s.DefaultVerify = verify.SelectedIndex == 1 ? "ReadBack" : "Native";
        int upTo = (int)(verifyUpTo.Value ?? 256);
        bool thresholdChanged = upTo != s.VerifyAutomaticallyUpToMiB;
        s.VerifyAutomaticallyUpToMiB = upTo;
        s.SavedFilters = parsedFilters;
        if (editorExe.Text != originalExecutable || editorArgs.Text != originalArguments)
        {
            var previous = s.Editor;
            s.Editor = string.IsNullOrWhiteSpace(parsedExecutable) ? null : new ToolDefinition
            {
                Name = previous?.Name ?? Path.GetFileNameWithoutExtension(parsedExecutable),
                Executable = parsedExecutable,
                Arguments = [.. parsedArguments],
                WorkingDirectory = previous?.WorkingDirectory ?? "{dir}",
                ShellMode = previous?.ShellMode ?? false,
                Hotkey = previous?.Hotkey,
                Intent = previous?.Intent,
                Mask = previous?.Mask,
                Children = previous?.Children,
            };
        }
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
        // Rows beside checksums show "not checked" or a result according to the new size.
        if (thresholdChanged) vm.Services.Metadata.Invalidate();
        return;
    }

    private static Control Form(params (string Label, Control Control)[] rows) => new DialogForm(rows) { Margin = new Thickness(8) };

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
