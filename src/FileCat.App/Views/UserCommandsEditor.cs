using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileCat.App.Controls;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.App.Views;

/// <summary>A private, ordered draft of F9 commands and their submenu tree.</summary>
internal sealed class UserCommandsEditor
{
    private sealed record Row(ToolDefinition Tool, List<ToolDefinition> Parent, int Index, int Depth);
    private readonly List<ToolDefinition> _draft;
    private readonly Dictionary<ToolDefinition, string> _argumentText = new(ReferenceEqualityComparer.Instance);
    private readonly ListBox _list = new() { Height = 180 };
    private readonly TextBox _name = new();
    private readonly TextBox _program = new();
    private readonly TextBox _arguments = new();
    private readonly TextBox _directory = new();
    private readonly TextBox _hotkey = new();
    private readonly CheckBox _shell = new() { Content = "Allow shell interpretation for this command" };
    private readonly CheckBox _advanced = new() { Content = "Edit user commands as text" };
    private readonly TextBox _raw = new() { AcceptsReturn = true, MinHeight = 120, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace") };
    private readonly TextBlock _error = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly ToolInvocationPreview _invocation;
    private readonly StackPanel _structured = new() { Spacing = 6 };
    private readonly StackPanel _rawPanel = new() { Spacing = 6, IsVisible = false };
    private readonly Button _duplicate = new() { Content = "Duplicate command or menu" };
    private readonly Button _remove = new() { Content = "Remove command or menu" };
    private readonly Button _up = new() { Content = "Move command or menu up" };
    private readonly Button _down = new() { Content = "Move command or menu down" };
    private List<Row> _rows = [];
    private bool _updating;

    public UserCommandsEditor(IEnumerable<ToolDefinition> commands, string sampleFolder)
    {
        _draft = Clone(commands);
        RememberArguments(_draft);
        _invocation = new ToolInvocationPreview("Command", _arguments, SnapshotInvocation, sampleFolder);
        AutomationProperties.SetName(_list, "User command and menu list");
        AutomationProperties.SetName(_name, "Selected command or menu name");
        AutomationProperties.SetName(_program, "Selected command program");
        AutomationProperties.SetName(_arguments, "Selected command arguments");
        AutomationProperties.SetName(_directory, "Selected command working directory");
        AutomationProperties.SetName(_hotkey, "Selected command shortcut");
        AutomationProperties.SetName(_raw, "User commands advanced text");
        _raw.Text = UserCommandsText.Format(_draft);
        _list.SelectionChanged += (_, _) => { if (!_updating) ShowSelected(); };
        foreach (var field in new[] { _name, _program, _arguments, _directory, _hotkey })
            field.TextChanged += (_, _) => UpdateSelected();
        _shell.IsCheckedChanged += (_, _) => UpdateSelected();

        var add = new Button { Content = "Add command" };
        add.Click += (_, _) => Add(new ToolDefinition { Name = "New command", Arguments = ["{file}"] });
        var addMenu = new Button { Content = "Add menu" };
        addMenu.Click += (_, _) => Add(new ToolDefinition { Name = "New menu", Children = [] });
        _duplicate.Click += (_, _) =>
        {
            if (Selected is not { } row) return;
            var duplicate = Clone([row.Tool])[0];
            duplicate.Name += " copy";
            row.Parent.Insert(row.Index + 1, duplicate);
            RememberArguments([duplicate]);
            Refresh(duplicate);
            _name.Focus();
        };
        _remove.Click += (_, _) =>
        {
            if (Selected is not { } row) return;
            row.Parent.RemoveAt(row.Index);
            Refresh(row.Parent.ElementAtOrDefault(Math.Min(row.Index, row.Parent.Count - 1)));
        };
        _up.Click += (_, _) => Move(-1);
        _down.Click += (_, _) => Move(1);
        _structured.Children.Add(new TextBlock { Text = "Select a menu to add inside it. Move controls reorder siblings; a menu moves with its commands.", TextWrapping = TextWrapping.Wrap });
        _structured.Children.Add(_list);
        _structured.Children.Add(DialogLayout.Actions(add, addMenu, _duplicate, _remove, _up, _down));
        _structured.Children.Add(Field("Name", _name));
        _structured.Children.Add(Field("Program", _program));
        var browse = new Button { Content = "Browse for command program…" };
        browse.Click += async (_, _) =>
        {
            var owner = TopLevel.GetTopLevel(browse);
            if (owner is null) return;
            try
            {
                var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose command program", AllowMultiple = false,
                });
                if (!ReferenceEquals(TopLevel.GetTopLevel(browse), owner) || picked.Count == 0) return;
                if (picked[0].TryGetLocalPath() is { } path && Path.IsPathFullyQualified(path)) _program.Text = path;
                else ShowError("Choose a program on this computer.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                ShowError("Could not open the program picker: " + ex.Message);
            }
        };
        _structured.Children.Add(browse);
        _structured.Children.Add(Field("Arguments", _arguments));
        _structured.Children.Add(_invocation.View);
        _structured.Children.Add(Field("Working directory", _directory));
        _structured.Children.Add(Field("Shortcut (optional)", _hotkey));
        _structured.Children.Add(_shell);
        _structured.Children.Add(new TextBlock { Text = "Batch programs can interpret selected names as commands when shell interpretation is enabled. Prefer a normal executable.", TextWrapping = TextWrapping.Wrap, Classes = { "muted", "small" } });
        _rawPanel.Children.Add(new TextBlock { Text = "Advanced text supports nested Group > Name entries and the lossless @tools: JSON form.", TextWrapping = TextWrapping.Wrap });
        _rawPanel.Children.Add(_raw);
        AdvancedTextFileExchange.AddTo(_rawPanel, _raw, "command");
        _advanced.IsCheckedChanged += (_, _) => SwitchMode();
        View = new StackPanel { Margin = new Thickness(8), Spacing = 6, Children = { _advanced, _structured, _rawPanel, _error } };
        Refresh(_draft.FirstOrDefault());
    }

    public Control View { get; }

    public bool TryGet(out List<ToolDefinition> commands, out string? error, out TextBox field)
    {
        foreach (var text in new[] { _name, _program, _arguments, _directory, _hotkey, _raw })
            AutomationProperties.SetHelpText(text, null);
        field = _advanced.IsChecked == true ? _raw : _name;
        if (_advanced.IsChecked == true)
        {
            commands = UserCommandsText.Parse(_raw.Text, out error);
            return error is null;
        }
        commands = _draft;
        foreach (var row in _rows)
        {
            var tool = row.Tool;
            string location = $"Item {_rows.IndexOf(row) + 1} ({tool.Name})";
            if (string.IsNullOrWhiteSpace(tool.Name))
            {
                error = $"{location}, name: enter a name.";
                field = _name;
            }
            else if (row.Depth >= 32)
            {
                error = $"{location}: use at most 31 submenu levels.";
            }
            else if (tool.Children is not null) continue;
            else if (string.IsNullOrWhiteSpace(tool.Executable))
            {
                error = $"{location}, program: enter a program.";
                field = _program;
            }
            else if (!ToolTextGrammar.TryParseArguments(_argumentText[tool], out var arguments, out var argumentError))
            {
                error = $"{location}, arguments: {argumentError}";
                field = _arguments;
            }
            else { tool.Arguments = arguments; continue; }
            Refresh(tool);
            ShowError(error);
            return false;
        }
        error = null;
        return true;
    }

    private Row? Selected => _list.SelectedIndex is var i && i >= 0 && i < _rows.Count ? _rows[i] : null;

    private void Add(ToolDefinition tool)
    {
        var selected = Selected;
        var parent = selected?.Tool.Children ?? selected?.Parent ?? _draft;
        int index = selected?.Tool.Children is not null ? parent.Count : selected is null ? parent.Count : selected.Index + 1;
        parent.Insert(index, tool);
        RememberArguments([tool]);
        Refresh(tool);
        _name.Focus();
    }

    private void Move(int delta)
    {
        if (Selected is not { } row) return;
        int next = row.Index + delta;
        if (next < 0 || next >= row.Parent.Count) return;
        (row.Parent[row.Index], row.Parent[next]) = (row.Parent[next], row.Parent[row.Index]);
        Refresh(row.Tool);
    }

    private void UpdateSelected()
    {
        if (_updating || Selected is not { } row) return;
        var tool = row.Tool;
        if (_name.Text != tool.Name) tool.Name = _name.Text ?? string.Empty;
        if (tool.Children is null)
        {
            if (_program.Text != tool.Executable) tool.Executable = _program.Text ?? string.Empty;
            if (_arguments.Text != _argumentText[tool]) _argumentText[tool] = _arguments.Text ?? string.Empty;
            if (_directory.Text != tool.WorkingDirectory) tool.WorkingDirectory = _directory.Text ?? string.Empty;
            if (_hotkey.Text != (tool.Hotkey ?? string.Empty)) tool.Hotkey = string.IsNullOrEmpty(_hotkey.Text) ? null : _hotkey.Text;
            if ((_shell.IsChecked == true) != tool.ShellMode) tool.ShellMode = _shell.IsChecked == true;
        }
        _error.IsVisible = false;
        _invocation.Retire();
        Refresh(tool, show: false);
    }

    private void Refresh(ToolDefinition? selected, bool show = true)
    {
        _rows = [];
        AddRows(_draft, 0);
        _updating = true;
        _list.ItemsSource = _rows.Select(r => new string(' ', r.Depth * 2) + (r.Tool.Children is null ? "Command: " : "Menu: ") + r.Tool.Name).ToList();
        _list.SelectedIndex = selected is null ? -1 : _rows.FindIndex(r => ReferenceEquals(r.Tool, selected));
        _updating = false;
        _invocation.Retire();
        if (show) ShowSelected();
        else RefreshButtons();
    }

    private void AddRows(List<ToolDefinition> tools, int depth)
    {
        for (int i = 0; i < tools.Count; i++)
        {
            _rows.Add(new Row(tools[i], tools, i, depth));
            if (tools[i].Children is { } children) AddRows(children, depth + 1);
        }
    }

    private void ShowSelected()
    {
        _updating = true;
        var tool = Selected?.Tool;
        bool command = tool is { Children: null };
        _name.Text = tool?.Name ?? string.Empty;
        _program.Text = command ? tool!.Executable : string.Empty;
        _arguments.Text = command ? _argumentText[tool!] : string.Empty;
        _directory.Text = command ? tool!.WorkingDirectory : string.Empty;
        _hotkey.Text = command ? tool!.Hotkey ?? string.Empty : string.Empty;
        _shell.IsChecked = command && tool!.ShellMode;
        _name.IsEnabled = tool is not null;
        foreach (var control in new Control[] { _program, _arguments, _directory, _hotkey, _shell }) control.IsEnabled = command;
        _updating = false;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        var row = Selected;
        _duplicate.IsEnabled = _remove.IsEnabled = row is not null;
        _up.IsEnabled = row is { Index: > 0 };
        _down.IsEnabled = row is not null && row.Index < row.Parent.Count - 1;
    }

    private void SwitchMode()
    {
        if (_updating) return;
        if (_advanced.IsChecked == true)
        {
            _updating = true;
            _advanced.IsChecked = false;
            _updating = false;
            if (!TryGet(out var commands, out var error, out var field))
            {
                ShowError(error!);
                field.Focus();
                return;
            }
            _raw.Text = UserCommandsText.Format(commands);
            _updating = true;
            _advanced.IsChecked = true;
            _updating = false;
            _error.IsVisible = false;
            _structured.IsVisible = false;
            _rawPanel.IsVisible = true;
        }
        else
        {
            var parsed = UserCommandsText.Parse(_raw.Text, out var error);
            if (error is not null)
            {
                _updating = true;
                _advanced.IsChecked = true;
                _updating = false;
                ShowError(error);
                _raw.Focus();
                return;
            }
            _draft.Clear();
            _draft.AddRange(parsed);
            _argumentText.Clear();
            RememberArguments(_draft);
            _error.IsVisible = false;
            _structured.IsVisible = true;
            _rawPanel.IsVisible = false;
            Refresh(_draft.FirstOrDefault());
        }
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.IsVisible = true;
    }

    private (ToolDefinition? Tool, string? Error) SnapshotInvocation()
    {
        if (Selected?.Tool is not { Children: null } selected) return (null, "Select a command first.");
        if (string.IsNullOrWhiteSpace(selected.Executable)) return (null, "Program: enter a program.");
        if (!ToolTextGrammar.TryParseArguments(_argumentText[selected], out var arguments, out var error))
            return (null, "Arguments: " + error);
        var tool = Clone([selected])[0];
        tool.Arguments = arguments;
        return (tool, null);
    }

    private void RememberArguments(IEnumerable<ToolDefinition> tools)
    {
        foreach (var tool in tools)
        {
            if (tool.Children is { } children) RememberArguments(children);
            else _argumentText[tool] = ToolTextGrammar.FormatArguments(tool.Arguments);
        }
    }

    private static Control Field(string title, Control control) => new StackPanel
    {
        Spacing = 3,
        Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, control },
    };

    private static List<ToolDefinition> Clone(IEnumerable<ToolDefinition> tools) =>
        JsonSerializer.Deserialize<List<ToolDefinition>>(JsonSerializer.Serialize(tools)) ?? [];
}
