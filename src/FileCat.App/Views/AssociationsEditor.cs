using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using FileCat.App.Controls;
using FileCat.Core.Selection;
using FileCat.Core.State;
using FileCat.Core.Tools;

namespace FileCat.App.Views;

/// <summary>Ordered, lossless Settings draft for first-match file associations.</summary>
internal sealed class AssociationsEditor
{
    private sealed class Entry(ToolDefinition tool)
    {
        public ToolDefinition Tool { get; } = tool;
        public string ArgumentsText { get; set; } = ToolTextGrammar.FormatArguments(tool.Arguments);
    }

    private readonly List<Entry> _draft;
    private readonly Func<IReadOnlyList<SavedFilter>> _savedFilters;
    private readonly ListBox _list = new() { Height = 160 };
    private readonly TextBox _name = new();
    private readonly TextBox _mask = new();
    private readonly ComboBox _intent = new() { ItemsSource = new[] { Associations.View, Associations.Edit, Associations.Open }, MinWidth = 140 };
    private readonly TextBox _program = new();
    private readonly TextBox _arguments = new();
    private readonly TextBox _directory = new();
    private readonly TextBox _hotkey = new();
    private readonly CheckBox _shell = new() { Content = "Allow shell interpretation for this association" };
    private readonly CheckBox _advanced = new() { Content = "Edit associations as text" };
    private readonly TextBox _raw = new() { AcceptsReturn = true, MinHeight = 120, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace") };
    private readonly TextBlock _rawError = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _duplicate = new() { Content = "Duplicate association" };
    private readonly Button _remove = new() { Content = "Remove association" };
    private readonly Button _up = new() { Content = "Move association up" };
    private readonly Button _down = new() { Content = "Move association down" };
    private readonly StackPanel _structured = new() { Spacing = 6 };
    private readonly StackPanel _rawPanel = new() { Spacing = 6, IsVisible = false };
    private readonly TextBlock _structuredError = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private Action? _retirePreview;
    private bool _updating;

    public AssociationsEditor(IEnumerable<ToolDefinition> associations, Func<IReadOnlyList<SavedFilter>> savedFilters)
    {
        _savedFilters = savedFilters;
        _draft = Clone(associations).Select(t => new Entry(t)).ToList();
        AutomationProperties.SetName(_list, "Association priority list");
        AutomationProperties.SetName(_name, "Association name");
        AutomationProperties.SetName(_mask, "Association mask");
        AutomationProperties.SetName(_intent, "Association action");
        AutomationProperties.SetName(_program, "Association program");
        AutomationProperties.SetName(_arguments, "Association arguments");
        AutomationProperties.SetName(_directory, "Association working directory");
        AutomationProperties.SetName(_hotkey, "Association shortcut");
        AutomationProperties.SetName(_raw, "Associations advanced text");
        _raw.Text = Associations.Format(_draft.Select(e => e.Tool), CurrentResolver());
        _raw.TextChanged += (_, _) => { _rawError.IsVisible = false; _retirePreview?.Invoke(); };
        _list.SelectionChanged += (_, _) => { if (!_updating) ShowSelected(); };
        foreach (var field in new[] { _name, _mask, _program, _arguments, _directory, _hotkey })
            field.TextChanged += (_, _) => UpdateSelected();
        _intent.SelectionChanged += (_, _) => UpdateSelected();
        _shell.IsCheckedChanged += (_, _) => UpdateSelected();

        var add = new Button { Content = "Add association" };
        add.Click += (_, _) =>
        {
            _draft.Add(new Entry(new ToolDefinition
            {
                Name = "New association", Mask = "*.txt", Intent = Associations.View,
                Arguments = ["{file}"],
            }));
            Refresh(_draft.Count - 1);
            _mask.Focus();
        };
        _duplicate.Click += (_, _) =>
        {
            if (Selected is not { } entry) return;
            _draft.Insert(_list.SelectedIndex + 1, new Entry(Clone([entry.Tool])[0]));
            Refresh(_list.SelectedIndex + 1);
            _mask.Focus();
        };
        _remove.Click += (_, _) =>
        {
            int index = _list.SelectedIndex;
            if (index < 0) return;
            _draft.RemoveAt(index);
            Refresh(Math.Min(index, _draft.Count - 1));
        };
        _up.Click += (_, _) => Move(-1);
        _down.Click += (_, _) => Move(1);
        _structured.Children.Add(new TextBlock { Text = "First matching mask for an action wins. Move a specific match above a broad one.", TextWrapping = TextWrapping.Wrap });
        _structured.Children.Add(_structuredError);
        _structured.Children.Add(_list);
        _structured.Children.Add(DialogLayout.Actions(add, _duplicate, _remove, _up, _down));
        _structured.Children.Add(Field("Name", _name));
        _structured.Children.Add(Field("Mask (use | for exclusions)", _mask));
        _structured.Children.Add(Field("Action", _intent));
        _structured.Children.Add(Field("Program", _program));
        var browse = new Button { Content = "Browse for association program…" };
        var browseError = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        browse.Click += async (_, _) =>
        {
            var owner = TopLevel.GetTopLevel(browse);
            if (owner is null) return;
            try
            {
                var picked = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Choose association program", AllowMultiple = false,
                });
                if (!ReferenceEquals(TopLevel.GetTopLevel(browse), owner) || picked.Count == 0) return;
                if (picked[0].TryGetLocalPath() is { } path && Path.IsPathFullyQualified(path))
                {
                    _program.Text = path;
                    browseError.IsVisible = false;
                }
                else
                {
                    browseError.Text = "Choose a program on this computer.";
                    browseError.IsVisible = true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                browseError.Text = "Could not open the program picker: " + ex.Message;
                browseError.IsVisible = true;
            }
        };
        _structured.Children.Add(browse);
        _structured.Children.Add(browseError);
        _structured.Children.Add(Field("Arguments", _arguments));
        _structured.Children.Add(Field("Working directory", _directory));
        _structured.Children.Add(Field("Shortcut (optional)", _hotkey));
        _structured.Children.Add(_shell);
        _structured.Children.Add(new TextBlock { Text = "Batch programs can interpret selected names as commands when shell interpretation is enabled. Prefer a normal executable.", TextWrapping = TextWrapping.Wrap, Classes = { "muted", "small" } });

        var sample = new TextBox { Text = "example.txt" };
        AutomationProperties.SetName(sample, "Association preview filename");
        var action = new ComboBox { ItemsSource = new[] { Associations.View, Associations.Edit, Associations.Open }, SelectedIndex = 0, MinWidth = 140 };
        AutomationProperties.SetName(action, "Association preview action");
        var preview = new SelectableTextBlock { Text = "Choose Preview first match to see which association wins. Nothing is launched.", TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(preview, "Association first match preview");
        sample.TextChanged += (_, _) => _retirePreview?.Invoke();
        action.SelectionChanged += (_, _) => _retirePreview?.Invoke();
        var previewButton = new Button { Content = "Preview first match" };
        previewButton.Click += (_, _) =>
        {
            if (!TryGet(out var definitions, out var error, out _))
            {
                preview.Text = "Fix the association draft before preview: " + error;
                return;
            }
            string name = Path.GetFileName(sample.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(name))
            {
                preview.Text = "Enter a sample filename.";
                return;
            }
            string intent = action.SelectedItem as string ?? Associations.View;
            var winner = Associations.Find(definitions, intent, name, CurrentResolver());
            preview.Text = winner is null
                ? $"{name} · {intent}: no custom association matches; the usual action applies."
                : $"{name} · {intent}: association {definitions.IndexOf(winner) + 1} wins ({winner.Mask}).\nProgram: {winner.Executable}\nArguments: {ToolTextGrammar.FormatArguments(winner.Arguments)}\nNo program was started.";
        };
        _structured.Children.Add(Field("Preview filename", sample));
        _structured.Children.Add(DialogLayout.Actions(action, previewButton));
        _structured.Children.Add(preview);
        _rawPanel.Children.Add(new TextBlock { Text = "Advanced definitions use mask | action | program | arguments | options, or the lossless @tools: form.", TextWrapping = TextWrapping.Wrap });
        _rawPanel.Children.Add(_raw);
        _rawPanel.Children.Add(_rawError);
        _advanced.IsCheckedChanged += (_, _) => SwitchMode();
        View = new StackPanel { Margin = new Thickness(8), Spacing = 6, Children = { _advanced, _structured, _rawPanel } };
        Refresh(_draft.Count > 0 ? 0 : -1);
        _retirePreview = () => preview.Text = "Preview out of date. Choose Preview first match again.";
    }

    public Control View { get; }

    public bool TryGet(out List<ToolDefinition> associations, out string? error, out TextBox field)
    {
        foreach (var text in new[] { _name, _mask, _program, _arguments, _directory, _hotkey, _raw })
            AutomationProperties.SetHelpText(text, null);
        field = _advanced.IsChecked == true ? _raw : _mask;
        Func<string, string?> resolve;
        try { resolve = CurrentResolver(); }
        catch (InvalidOperationException ex)
        {
            associations = [];
            error = "Saved filters: " + ex.Message;
            return false;
        }
        if (_advanced.IsChecked == true)
        {
            associations = Associations.Parse(_raw.Text, resolve, out error);
            return error is null;
        }
        associations = _draft.Select(e => e.Tool).ToList();
        for (int i = 0; i < _draft.Count; i++)
        {
            var entry = _draft[i];
            var tool = entry.Tool;
            string? maskError = null;
            if (string.IsNullOrWhiteSpace(tool.Name)) { error = $"Association {i + 1}, name: enter a name."; field = _name; }
            else if (string.IsNullOrWhiteSpace(tool.Mask) || !Mask.TryParse(tool.Mask, resolve, out _, out maskError))
            { error = $"Association {i + 1}, mask: {maskError ?? "enter a mask"}."; field = _mask; }
            else if (!Associations.IsIntent(tool.Intent)) { error = $"Association {i + 1}, action: choose view, edit or open."; field = _mask; }
            else if (string.IsNullOrWhiteSpace(tool.Executable)) { error = $"Association {i + 1}, program: enter a program."; field = _program; }
            else if (!ToolTextGrammar.TryParseArguments(entry.ArgumentsText, out var args, out var argumentError))
            { error = $"Association {i + 1}, arguments: {argumentError}"; field = _arguments; }
            else { tool.Arguments = args; continue; }
            Refresh(i);
            return false;
        }
        error = null;
        return true;
    }

    private Entry? Selected
    {
        get { int i = _list.SelectedIndex; return i >= 0 && i < _draft.Count ? _draft[i] : null; }
    }

    private void UpdateSelected()
    {
        if (_updating || Selected is not { } entry) return;
        _structuredError.IsVisible = false;
        var tool = entry.Tool;
        if (_name.Text != (tool.Name ?? string.Empty)) tool.Name = _name.Text ?? string.Empty;
        if (_mask.Text != (tool.Mask ?? string.Empty)) tool.Mask = _mask.Text ?? string.Empty;
        if (_intent.SelectedItem is string intent && intent != tool.Intent) tool.Intent = intent;
        if (_program.Text != (tool.Executable ?? string.Empty)) tool.Executable = _program.Text ?? string.Empty;
        if (_arguments.Text != entry.ArgumentsText) entry.ArgumentsText = _arguments.Text ?? string.Empty;
        if (_directory.Text != (tool.WorkingDirectory ?? string.Empty)) tool.WorkingDirectory = _directory.Text ?? string.Empty;
        if (_hotkey.Text != (tool.Hotkey ?? string.Empty)) tool.Hotkey = string.IsNullOrEmpty(_hotkey.Text) ? null : _hotkey.Text;
        if ((_shell.IsChecked == true) != tool.ShellMode) tool.ShellMode = _shell.IsChecked == true;
        Refresh(_list.SelectedIndex, show: false);
    }

    private void Move(int delta)
    {
        int i = _list.SelectedIndex, j = i + delta;
        if (i < 0 || j < 0 || j >= _draft.Count) return;
        (_draft[i], _draft[j]) = (_draft[j], _draft[i]);
        Refresh(j);
    }

    private void Refresh(int index, bool show = true)
    {
        _updating = true;
        _list.ItemsSource = _draft.Select((e, i) => $"{i + 1}. {e.Tool.Mask} · {e.Tool.Intent} → {e.Tool.Name}").ToList();
        _list.SelectedIndex = index >= 0 && _draft.Count > 0 ? Math.Min(index, _draft.Count - 1) : -1;
        _updating = false;
        _retirePreview?.Invoke();
        if (show) ShowSelected();
        else RefreshButtons();
    }

    private void ShowSelected()
    {
        _updating = true;
        var entry = Selected;
        var tool = entry?.Tool;
        _name.Text = tool?.Name ?? string.Empty;
        _mask.Text = tool?.Mask ?? string.Empty;
        _intent.SelectedItem = tool?.Intent;
        _program.Text = tool?.Executable ?? string.Empty;
        _arguments.Text = entry?.ArgumentsText ?? string.Empty;
        _directory.Text = tool?.WorkingDirectory ?? string.Empty;
        _hotkey.Text = tool?.Hotkey ?? string.Empty;
        _shell.IsChecked = tool?.ShellMode ?? false;
        foreach (var control in new Control[] { _name, _mask, _intent, _program, _arguments, _directory, _hotkey, _shell })
            control.IsEnabled = entry is not null;
        _updating = false;
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool selected = Selected is not null;
        _duplicate.IsEnabled = _remove.IsEnabled = selected;
        _up.IsEnabled = selected && _list.SelectedIndex > 0;
        _down.IsEnabled = selected && _list.SelectedIndex < _draft.Count - 1;
    }

    private void SwitchMode()
    {
        if (_updating) return;
        if (_advanced.IsChecked == true)
        {
            // Invalid typed arguments must remain in place; switching would otherwise discard them.
            _updating = true;
            _advanced.IsChecked = false;
            _updating = false;
            if (!TryGet(out var definitions, out var error, out var field))
            {
                _structuredError.Text = error;
                _structuredError.IsVisible = true;
                field.Focus();
                return;
            }
            _raw.Text = Associations.Format(definitions, CurrentResolver());
            _updating = true;
            _advanced.IsChecked = true;
            _updating = false;
            _rawError.IsVisible = false;
            _structured.IsVisible = false;
            _rawPanel.IsVisible = true;
        }
        else
        {
            var parsed = Associations.Parse(_raw.Text, CurrentResolver(), out var error);
            if (error is not null)
            {
                _updating = true;
                _advanced.IsChecked = true;
                _updating = false;
                _rawError.Text = error;
                _rawError.IsVisible = true;
                _raw.Focus();
                return;
            }
            _draft.Clear();
            _draft.AddRange(parsed.Select(t => new Entry(t)));
            _rawError.IsVisible = false;
            _structured.IsVisible = true;
            _rawPanel.IsVisible = false;
            Refresh(_draft.Count > 0 ? 0 : -1);
        }
    }

    private static Control Field(string title, Control control) => new StackPanel
    {
        Spacing = 3,
        Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold }, control },
    };

    private static List<ToolDefinition> Clone(IEnumerable<ToolDefinition> associations) =>
        JsonSerializer.Deserialize<List<ToolDefinition>>(JsonSerializer.Serialize(associations)) ?? [];

    private Func<string, string?> CurrentResolver()
    {
        var filters = _savedFilters();
        return name => filters.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))?.Mask;
    }
}
