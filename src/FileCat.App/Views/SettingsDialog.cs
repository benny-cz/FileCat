using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Input;
using System.Text;
using System.Text.Json;
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
        var contentFontSize = new NumericUpDown { Minimum = 9, Maximum = 32, Increment = 1,
            Value = (decimal)ReadabilityPolicy.ContentSize(s.ContentFontSize), Width = 130, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var density = new ComboBox { ItemsSource = new[] { "Compact", "Comfortable" },
            SelectedIndex = ReadabilityPolicy.Comfortable(s.RowDensity) ? 1 : 0, MinWidth = 220 };
        var keyBar = new CheckBox { Content = "Show the function-key bar", IsChecked = s.ShowFunctionKeyBar };
        var toolbar = new CheckBox { Content = "Show the toolbar", IsChecked = s.ShowToolbar };
        var classicToolbar = new CheckBox { Content = "Classic full toolbar (all commands)", IsChecked = s.ClassicToolbar };
        var toolbarNote = Note("The focused toolbar keeps frequent actions in a stable row; More holds the remaining commands. The classic option restores the complete toolbar. Menus and shortcuts work in either layout.");
        var allPlaces = new CheckBox { Content = "Keep the location menu order in the Places row", IsChecked = !s.CompactPlaces };
        var placesNote = Note("When unchecked, common and pinned Places appear first. The row fills the available width; the » menu and Alt+F1/Alt+F2 reach places that do not fit. Pin or unpin from a place's menu.");
        var driveButtons = new CheckBox { Content = "Show place buttons above the panels (drives, folders, bookmarks, servers)", IsChecked = s.ShowDriveButtons };
        var cmdLine = new CheckBox { Content = "Show the command line", IsChecked = s.ShowCommandLine };
        // With seconds first; the shorter forms stay for those who chose them, and a format typed into the settings file
        // is offered as it is, so saving never replaces it.
        var dateFormats = new List<string> { "Culture", "yyyy-MM-dd HH:mm:ss", "dd.MM.yyyy HH:mm:ss", "MM/dd/yyyy h:mm:ss tt", "yyyy-MM-dd HH:mm", "dd.MM.yyyy HH:mm", "MM/dd/yyyy h:mm tt" };
        if (!dateFormats.Contains(s.DateFormat)) dateFormats.Add(s.DateFormat);
        var dateFormat = new ComboBox { ItemsSource = dateFormats, SelectedItem = s.DateFormat, MinWidth = 220 };
        tabs.Items.Add(new TabItem { Header = "Appearance", Content = Form(("Theme", theme), ("", themeDescription), ("", animations), ("Interface density", density), ("List font size", fontSize), ("Text, hex and comparison size", contentFontSize), ("", Note("Content size starts new viewers and comparisons at this value; Ctrl+plus/minus and the visible size controls adjust the open view.")), ("Date format", dateFormat), ("", keyBar), ("", toolbar), ("", classicToolbar), ("", toolbarNote), ("", driveButtons), ("", allPlaces), ("", placesNote), ("", cmdLine),
            ("", Note("View → Theme… previews every theme. System high contrast takes priority. FileCat follows reduced-motion settings on Windows, macOS, and GNOME; on other desktops, turn off Animate theme effects here."))) });

        // ---- Behavior
        var hidden = new CheckBox { Content = "Show hidden and system items (dimmed)", IsChecked = s.ShowHidden };
        var natural = new CheckBox { Content = "Natural sort (file2 before file10)", IsChecked = s.NaturalSort };
        var dirsFirst = new CheckBox { Content = "Folders before files", IsChecked = s.DirectoriesFirst };
        var confirmRecycle = new CheckBox { Content = "Ask before moving items to the Recycle Bin", IsChecked = s.ConfirmRecycle };
        var sizeOnSpace = new CheckBox { Content = "Space computes the size of a marked folder", IsChecked = s.SizeFolderOnSpace };
        var sizeSlow = new CheckBox { Content = "…also on network, removable, and cloud locations", IsChecked = s.SizeFolderOnSlowLocations };
        BindDependentOption(sizeOnSpace, sizeSlow, supported: true,
            "Turn on folder sizing with Space to use this option.");
        var anywhere = new CheckBox { Content = "Quick search matches anywhere in the name (instead of the beginning)", IsChecked = s.QuickSearchMatchAnywhere };
        var filtersEditor = new SavedFiltersEditor(s.SavedFilters);
        var single = new CheckBox { Content = "One FileCat window per profile (a new launch opens its paths in the running window)", IsChecked = s.SingleInstance };
        var awake = new CheckBox { Content = "Keep the computer awake while operations run", IsChecked = s.KeepAwakeDuringJobs };
        // Named as the copy dialog names them (stored as Native and ReadBack).
        var verify = new ComboBox { ItemsSource = new[] { "Size and metadata (fast)", "Read back and compare content" }, SelectedIndex = s.DefaultVerify == "ReadBack" ? 1 : 0, MinWidth = 220 };
        // D-57: how large a file beside a checksum or signature may be to be checked when shown.
        var verifyUpTo = new NumericUpDown { Minimum = 0, Maximum = 1 << 20, Increment = 64, FormatString = "0 MiB", Value = s.VerifyAutomaticallyUpToMiB, Width = 160, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        tabs.Items.Add(new TabItem { Header = "Behavior", Content = Form(("", hidden), ("", natural), ("", dirsFirst), ("", confirmRecycle), ("", sizeOnSpace), ("", sizeSlow), ("", anywhere), ("", single), ("", awake), ("Default copy verification", verify),
            ("Verify automatically up to", verifyUpTo),
            ("", Note("Files next to a checksum file or signature (SHA256SUMS, name.sha256, .minisig, .asc…) are checked when shown up to this size; larger ones, and files on the network, on request: File → Verify checksums and signatures. 0 checks only on request.")),
            ("Saved filters", filtersEditor.View),
            ("", Note("Use filters as @name in any mask: select (Num+), quick filter, copy filters, Find, and compare."))) });

        // ---- Tools
        var editorExe = new TextBox { Text = s.Editor?.Executable ?? string.Empty, PlaceholderText = "auto: " + Core.Tools.ToolLauncher.DetectEditor().Executable };
        var editorArgs = new TextBox { Text = s.Editor is null ? "{files}" : ToolTextGrammar.FormatArguments(s.Editor.Arguments) };
        var editorShellMode = new CheckBox
        {
            Content = "Allow shell interpretation for batch programs",
            IsChecked = s.Editor?.ShellMode ?? false,
        };
        var browseEditor = new Button { Content = "Browse for editor…" };
        var browseStatus = Note(string.Empty);
        browseEditor.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(browseEditor);
            if (top is null) return;
            try
            {
                var picked = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose editor program",
                    AllowMultiple = false,
                });
                if (!ReferenceEquals(TopLevel.GetTopLevel(browseEditor), top) || picked.Count == 0) return;
                if (picked[0].TryGetLocalPath() is { } path && Path.IsPathFullyQualified(path))
                {
                    editorExe.Text = path;
                    browseStatus.Text = string.Empty;
                }
                else
                    browseStatus.Text = "Choose a program on this computer.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                browseStatus.Text = "Could not open the program picker: " + ex.Message;
            }
        };
        var tokens = new ComboBox { ItemsSource = ToolLauncher.Placeholders, SelectedIndex = 0, MinWidth = 150 };
        Avalonia.Automation.AutomationProperties.SetName(tokens, "Argument token to add");
        var previewResult = new SelectableTextBlock
        {
            Text = "Choose Preview invocation to inspect the resolved argument vector. Nothing is run.",
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
        };
        Avalonia.Automation.AutomationProperties.SetName(previewResult, "Editor invocation preview");
        var addToken = new Button { Content = "Add token" };
        addToken.Click += (_, _) =>
        {
            if (tokens.SelectedItem is not string token) return;
            if (!ToolTextGrammar.TryParseArguments(editorArgs.Text, out var arguments, out var parseError))
            {
                Avalonia.Automation.AutomationProperties.SetHelpText(editorArgs, parseError);
                previewResult.Text = "Editor arguments: " + parseError;
                editorArgs.Focus();
                return;
            }
            Avalonia.Automation.AutomationProperties.SetHelpText(editorArgs, null);
            arguments.Add(token);
            editorArgs.Text = ToolTextGrammar.FormatArguments(arguments);
            editorArgs.Focus();
        };
        var previewFolder = vm.ActiveTab?.Location is { IsFileSystem: true } localLocation
            ? localLocation.Path : vm.Services.Paths.LocalDirectory;
        var previewFile = new TextBox { Text = Path.Combine(previewFolder, "example file.txt") };
        var previewButton = new Button { Content = "Preview invocation" };
        int previewGeneration = 0;
        (string Program, string Arguments, string Sample, bool ShellMode)? previewSnapshot =
            (editorExe.Text ?? string.Empty, editorArgs.Text ?? string.Empty, previewFile.Text ?? string.Empty, editorShellMode.IsChecked == true);
        void RetirePreview(object? _, EventArgs __)
        {
            if (previewSnapshot is { } snapshot && snapshot ==
                (editorExe.Text ?? string.Empty, editorArgs.Text ?? string.Empty, previewFile.Text ?? string.Empty, editorShellMode.IsChecked == true)) return;
            previewGeneration++;
            previewSnapshot = null;
            previewResult.Text = "Preview out of date. Choose Preview invocation again.";
        }
        editorExe.TextChanged += RetirePreview;
        editorArgs.TextChanged += RetirePreview;
        previewFile.TextChanged += RetirePreview;
        editorShellMode.IsCheckedChanged += (_, _) => RetirePreview(null, EventArgs.Empty);
        previewButton.Click += async (_, _) =>
        {
            int request = ++previewGeneration;
            var owner = TopLevel.GetTopLevel(previewButton);
            if (owner is null) return;
            string programText = editorExe.Text ?? string.Empty;
            string argumentsText = editorArgs.Text ?? string.Empty;
            string sampleText = previewFile.Text ?? string.Empty;
            string toolName = s.Editor?.Name ?? "Editor";
            bool shellMode = editorShellMode.IsChecked == true;
            string tempDirectory = vm.Services.Paths.LocalDirectory;
            previewSnapshot = (programText, argumentsText, sampleText, shellMode);
            previewResult.Text = "Checking invocation…";
            string result = await Task.Run(() =>
            {
                try
                {
                    if (!Path.IsPathFullyQualified(sampleText)) return "Preview file: enter a full local path.";
                    if (!ToolTextGrammar.TryParseArguments(argumentsText, out var arguments, out var parseError))
                        return "Editor arguments: " + parseError;
                    if (!ToolTextGrammar.TryParseField(programText, out var executable, out var fieldError))
                        return "Editor program: " + fieldError;
                    var tool = string.IsNullOrWhiteSpace(executable) ? ToolLauncher.DetectEditor() : new ToolDefinition
                    {
                        Name = toolName,
                        Executable = executable,
                        Arguments = arguments,
                        ShellMode = shellMode,
                    };
                    var plan = ToolLauncher.Preview(tool,
                        new ToolContext([sampleText], Path.GetDirectoryName(sampleText) ?? sampleText),
                        tempDirectory, out var warning);
                    var text = new StringBuilder("Sample invocation; no program or list file was created.\n");
                    if (string.IsNullOrWhiteSpace(executable))
                        text.Append("Automatic editor selected.\n");
                    foreach (var (program, values) in plan)
                    {
                        text.Append("Program: ").Append(program).Append("\nArguments (").Append(values.Count).Append("):\n");
                        for (int i = 0; i < values.Count; i++)
                            text.Append(i + 1).Append(": ").Append(JsonSerializer.Serialize(values[i])).Append('\n');
                    }
                    if (tool.Arguments.Contains("{listfile}")) text.Append("A list file is created only when this tool is launched.\n");
                    if (warning is not null) text.Append(warning);
                    return text.Length <= 16384 ? text.ToString() : text.ToString(0, 16384) + "\nPreview truncated; inspect the argument fields for the full values.";
                }
                catch (Exception ex) when (ex is ToolLaunchException or ArgumentException or IOException or UnauthorizedAccessException)
                {
                    return "Preview could not resolve the invocation: " + ex.Message;
                }
            });
            if (request == previewGeneration && ReferenceEquals(TopLevel.GetTopLevel(previewButton), owner))
                previewResult.Text = result;
        };
        // "auto": Windows Terminal when installed, else PowerShell 7, else Windows PowerShell.
        var shell = new ComboBox { ItemsSource = OperatingSystem.IsWindows() ? new[] { "auto", "cmd", "powershell", "pwsh", "wt" } : new[] { "posix" }, SelectedItem = s.Terminal.Shell, MinWidth = 220 };
        var userCommandsEditor = new UserCommandsEditor(s.UserCommands, previewFolder);
        var associationsEditor = new AssociationsEditor(s.Associations, filtersEditor.Snapshot, previewFolder);
        tabs.Items.Add(new TabItem
        {
            Header = "Tools",
            // Each box with its own explanation right below it.
            Content = Form(("Editor (F4) program", editorExe), ("", browseEditor), ("", browseStatus), ("Editor arguments", editorArgs),
                ("Add argument token", DialogLayout.Actions(tokens, addToken)),
                ("Preview file (sample only)", previewFile), ("", previewButton), ("", previewResult),
                ("", editorShellMode),
                ("", Note("Batch programs normally reject selected names with shell characters such as &, | or >. Enable this only if you trust both the batch program and the names it receives; a normal executable is safer.")),
                ("", Note("Arguments stay separate: quote an argument containing spaces or pipes; doubled quotes represent a literal quote, and backslashes stay literal. \"\" is one empty argument. An empty field means no arguments. The @json: array form preserves embedded line breaks.")), ("Command line shell", shell), ("User commands (F9)", userCommandsEditor.View),
                ("", Note("One command per line: Name | program | arguments | options. Group > Name puts a command in a submenu. Quote arguments that contain spaces. Options: key=HOTKEY, dir=FOLDER, shell. Complex saved definitions use an @tools: JSON array to preserve their complete structure. Tokens: {file} {files} {listfile} {dir} {target} {name} {prompt}. Programs must be real executables; batch files are refused when an argument contains shell metacharacters unless the command has the shell option.")),
                ("Associations", associationsEditor.View),
                ("", Note("F3, F4, and Enter use the first matching association for that action; Alt+F3 always opens the internal viewer."))),
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
        var chooseShortcut = new Button { Content = "Find and change a shortcut…" };
        var shortcutStatus = Note("Changes here stay in this Settings draft until OK.");
        var contextPreview = new TextBlock { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 };
        Avalonia.Automation.AutomationProperties.SetName(contextPreview, "Find shortcut context preview");
        void UpdateContextPreview()
        {
            var draft = ParseBindings(bindings.Text, vm.Services.Commands, out var parseError);
            if (parseError is not null) { contextPreview.Text = parseError; return; }
            var map = new Keymap(vm.Services.Commands, draft);
            if (map.Conflicts.Count > 0)
            {
                contextPreview.Text = "Shortcut conflicts in one context: " + string.Join(" ", map.Conflicts);
                return;
            }
            var overlaps = vm.Services.Commands.All
                .Where(def => def.Context is CommandContext.Find or CommandContext.FindCriteria or CommandContext.FindResults)
                .SelectMany(def => map.GetChords(def.Id).Select(chord => (def, chord)))
                .SelectMany(use => map.Uses(use.chord)
                    .Where(other => other.Context == CommandContext.Panel)
                    .Select(other => $"{use.chord.ToDisplayString()}: {use.def.Title} in {CommandContextNames.Display(use.def.Context)}; {other.Title} in file panels."))
                .Distinct()
                .ToArray();
            contextPreview.Text = overlaps.Length == 0 ? "No Find/panel shortcuts overlap."
                : "Context overlaps keep both bindings; Find uses its own action while open. " + string.Join(" ", overlaps);
        }
        bindings.TextChanged += (_, _) => UpdateContextPreview();
        UpdateContextPreview();
        chooseShortcut.Click += async (_, _) =>
        {
            var draft = ParseBindings(bindings.Text, vm.Services.Commands, out var parseError);
            if (parseError is not null)
            {
                shortcutStatus.Text = "Fix custom bindings first: " + parseError;
                Avalonia.Automation.AutomationProperties.SetHelpText(bindings, parseError);
                bindings.Focus();
                return;
            }
            string? selected = null;
            while (true)
            {
                var keymap = new Keymap(vm.Services.Commands, draft);
                var entries = vm.Services.Commands.All
                    .Where(def => !def.Id.StartsWith(CommandIds.BookmarkSetPrefix, StringComparison.Ordinal)
                        && !def.Id.StartsWith(CommandIds.BookmarkGoPrefix, StringComparison.Ordinal)
                        && !def.Id.StartsWith(CommandIds.BookmarkTargetPrefix, StringComparison.Ordinal)
                        && !def.Id.StartsWith(CommandIds.ColumnProfilePrefix, StringComparison.Ordinal))
                    .Select(def => new KeyboardHelpEntry(def.Id, def.Title, def.Category,
                        keymap.GetGestureText(def.Id), def.Description, true, null,
                        CommandContextNames.Display(def.Context),
                        def.DefaultGestures.Length == 0 ? "unbound" : string.Join(", ", def.DefaultGestures))).ToArray();
                var choice = await vm.Dialogs.KeyboardReferenceAsync(entries, selected, remapOnly: true);
                if (choice is null) return;
                selected = choice.CommandId;
                var changed = await vm.CaptureShortcutAsync(choice.CommandId, draft);
                if (changed is not { } result) continue;
                draft = result.Bindings;
                bindings.Text = string.Join(Environment.NewLine,
                    draft.Select(kv => $"{kv.Key} = {string.Join(", ", kv.Value)}"));
                shortcutStatus.Text = result.Message + " Save Settings to keep this change.";
            }
        };
        tabs.Items.Add(new TabItem { Header = "Keyboard", Content = Form(("Custom bindings", bindings), ("", chooseShortcut), ("", shortcutStatus), ("", contextPreview), ("", reference)) });

        // ---- Privacy
        var diag = new CheckBox { Content = "Diagnostic mode: include file paths in the local log (off by default)", IsChecked = s.DiagnosticMode };
        var updates = new CheckBox { Content = "Automatically check for updates", IsChecked = s.CheckForUpdates };
        var updateNote = Note("At most once a day at startup, FileCat checks for a newer release and notifies you. It does not download or install update packages. Help → Check for updates and network locations still connect when you request them.");
        bool helper = vm.Services.ShellPictures is not null;
        var shellPictures = new CheckBox
        {
            Content = "Shell pictures: thumbnails in quick view and programs' own icons, from Windows' handlers run in a separate restricted process",
            IsChecked = s.ShellPictures,
            IsEnabled = helper,
        };
        if (!helper)
        {
            ToolTip.SetTip(shellPictures, "This build has no Shell picture helper.");
            ToolTip.SetShowOnDisabled(shellPictures, true);
            Avalonia.Automation.AutomationProperties.SetHelpText(shellPictures, "This build has no Shell picture helper.");
        }
        var shellSlow = new CheckBox
        {
            Content = "…also on network and removable drives (their handlers may contact other computers)",
            IsChecked = s.ShellPicturesOnNetworkAndRemovable,
        };
        BindDependentOption(shellPictures, shellSlow, helper,
            "Turn on Shell pictures to use this option.", "This build has no Shell picture helper.");
        var shellNote = Note(helper
            ? "Shortcuts, internet shortcuts, libraries, themes, desktop.ini, and cloud placeholders are never handed to the Shell." +
              (vm.Services.ShellPictures?.DisabledReason is { } off ? " " + off : string.Empty)
            : "This build has no Shell helper, so icons come from file types only.");
        var paths = new SelectableTextBlock { Text = $"Settings: {vm.Services.Paths.SettingsDirectory}\nLocal data: {vm.Services.Paths.LocalDirectory}{(vm.Services.Paths.IsPortable ? "\n(portable mode)" : string.Empty)}", TextWrapping = TextWrapping.Wrap };
        tabs.Items.Add(new TabItem { Header = "Privacy", Content = Form(("", diag), ("", updates), ("", updateNote), ("", shellPictures), ("", shellSlow), ("", shellNote), ("Storage", paths)) });

        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        // Search the actual labels and explanations in every tab, including tabs not yet selected.
        var search = new TextBox { PlaceholderText = "Search settings", MinWidth = 220 };
        Avalonia.Automation.AutomationProperties.SetName(search, "Search settings");
        var results = new ListBox { MaxHeight = 190, IsVisible = false };
        Avalonia.Automation.AutomationProperties.SetName(results, "Matching settings");
        var resultHint = new TextBlock { Classes = { "muted", "small" }, IsVisible = false };
        var goToResult = new Button { Content = "Go to setting", IsVisible = false };
        var searchEntries = tabs.Items.OfType<TabItem>().SelectMany((tab, index) =>
            (tab.Content as Control)?.GetLogicalDescendants().OfType<Control>()
                .Select(control => (Tab: index, Control: control, Text: control switch
                {
                    SelectableTextBlock selectable => selectable.Text,
                    TextBlock label => label.Text,
                    CheckBox check => check.Content as string,
                    Button button => button.Content as string,
                    _ => Avalonia.Automation.AutomationProperties.GetName(control),
                }))
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Text))
                .Select(entry => (entry.Tab, entry.Control, Text: entry.Text!, TabName: tab.Header as string ?? "Settings"))
            ?? []).ToList();
        List<(int Tab, Control Control, string Text, string TabName)> matches = [];
        void UpdateSearch()
        {
            var terms = (search.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            results.IsVisible = resultHint.IsVisible = goToResult.IsVisible = terms.Length > 0;
            matches = terms.Length == 0 ? [] : searchEntries
                .Where(entry => terms.All(term => entry.Text.Contains(term, StringComparison.CurrentCultureIgnoreCase)))
                .Take(40).ToList();
            results.ItemsSource = matches.Select(entry => entry.TabName + " · " +
                (entry.Text.Length <= 110 ? entry.Text : entry.Text[..107] + "…")).ToArray();
            results.SelectedIndex = matches.Count > 0 ? 0 : -1;
            resultHint.Text = matches.Count == 0 ? "No settings match." :
                matches.Count == 40 ? "Showing the first 40 matches." : $"{matches.Count} matching settings.";
            goToResult.IsEnabled = matches.Count > 0;
        }
        void GoToSetting()
        {
            int index = results.SelectedIndex;
            if (index < 0 || index >= matches.Count) return;
            var entry = matches[index];
            tabs.SelectedIndex = entry.Tab;
            Dispatcher.UIThread.Post(() =>
            {
                if (TopLevel.GetTopLevel(entry.Control) is null) return;
                entry.Control.BringIntoView();
                if (entry.Control.Focusable && entry.Control.IsEffectivelyEnabled) entry.Control.Focus();
            }, DispatcherPriority.Loaded);
        }
        search.TextChanged += (_, _) => UpdateSearch();
        search.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && matches.Count > 0) { e.Handled = true; GoToSetting(); }
        };
        results.DoubleTapped += (_, _) => GoToSetting();
        goToResult.Click += (_, _) => GoToSetting();
        var body = new StackPanel { Spacing = 6, Children = { search, results, resultHint, goToResult, tabs, error } };
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
            foreach (var field in new[] { bindings, editorExe, editorArgs })
                Avalonia.Automation.AutomationProperties.SetHelpText(field, null);
            parsedBindings = ParseBindings(bindings.Text, vm.Services.Commands, out var bindingError);
            if (bindingError is not null) return Fail("Custom bindings", bindingError, bindings, 4);
            var bindingConflicts = new Keymap(vm.Services.Commands, parsedBindings).Conflicts;
            if (bindingConflicts.Count > 0) return Fail("Custom bindings", string.Join(" ", bindingConflicts), bindings, 4);
            if (!filtersEditor.TryGet(out parsedFilters, out var filterError, out var filterField))
                return Fail("Saved filters", filterError!, filterField, 1);
            if (editorExe.Text != originalExecutable && !ToolTextGrammar.TryParseField(editorExe.Text, out parsedExecutable, out var executableError))
                return Fail("Editor program", executableError!, editorExe, 2);
            if (editorArgs.Text != originalArguments && !ToolTextGrammar.TryParseArguments(editorArgs.Text, out parsedArguments, out var argumentError))
                return Fail("Editor arguments", argumentError!, editorArgs, 2);
            if (!userCommandsEditor.TryGet(out parsedCommands, out var commandError, out var commandField))
                return Fail("User commands", commandError!, commandField, 2);
            if (!associationsEditor.TryGet(out parsedAssociations, out var associationError, out var associationField))
                return Fail("Associations", associationError!, associationField, 2);
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
        s.ContentFontSize = ReadabilityPolicy.ContentSize((double)(contentFontSize.Value ?? 13));
        s.RowDensity = density.SelectedIndex == 1 ? "Comfortable" : "Compact";
        s.DateFormat = dateFormat.SelectedItem as string ?? "Culture";
        s.ShowFunctionKeyBar = keyBar.IsChecked == true;
        s.ShowToolbar = toolbar.IsChecked == true;
        s.ClassicToolbar = classicToolbar.IsChecked == true;
        s.CompactPlaces = allPlaces.IsChecked != true;
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
        if (editorExe.Text != originalExecutable || editorArgs.Text != originalArguments ||
            (editorShellMode.IsChecked == true) != (s.Editor?.ShellMode ?? false))
        {
            var previous = s.Editor;
            s.Editor = string.IsNullOrWhiteSpace(parsedExecutable) ? null : new ToolDefinition
            {
                Name = previous?.Name ?? Path.GetFileNameWithoutExtension(parsedExecutable),
                Executable = parsedExecutable,
                Arguments = [.. parsedArguments],
                WorkingDirectory = previous?.WorkingDirectory ?? "{dir}",
                ShellMode = editorShellMode.IsChecked == true,
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

    /// <summary>Keep the subordinate draft choice while its parent is off, and explain why it is unavailable.</summary>
    internal static void BindDependentOption(CheckBox parent, CheckBox child, bool supported,
        string parentReason, string? unsupportedReason = null)
    {
        child.Margin = new Thickness(20, 0, 0, 0);
        ToolTip.SetShowOnDisabled(child, true);
        void Update()
        {
            child.IsEnabled = supported && parent.IsChecked == true;
            string help = !supported ? unsupportedReason ?? "This option is unavailable in this build."
                : parent.IsChecked == true ? "Additional option for " + parent.Content
                : parentReason;
            ToolTip.SetTip(child, help);
            Avalonia.Automation.AutomationProperties.SetHelpText(child, help);
        }
        parent.IsCheckedChanged += (_, _) => Update();
        Update();
    }

    /// <summary>"name = mask" lines; every mask must parse, including references between saved filters.</summary>
    internal static List<SavedFilter> ParseSavedFilters(string? text, out string? error)
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
        string? Resolve(string n) => list.FirstOrDefault(f => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase))?.Mask;
        foreach (var f in list)
        {
            if (!Core.Selection.Mask.TryParse(f.Mask, Resolve, out _, out var maskError))
            {
                error = $"Saved filter \"{f.Name}\": {maskError}";
                return list;
            }
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
