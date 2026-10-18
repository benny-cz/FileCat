using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Controls;
using FileCat.Core.State;

namespace FileCat.App.Views;

/// <summary>Typed Settings draft for saved filters, with an explicit raw-text exchange path.</summary>
internal sealed class SavedFiltersEditor
{
    private readonly List<SavedFilter> _draft;
    private readonly ListBox _list = new() { Height = 150 };
    private readonly TextBox _name = new();
    private readonly TextBox _mask = new();
    private readonly TextBox _raw = new()
    {
        AcceptsReturn = true,
        MinHeight = 100,
        FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"),
    };
    private readonly CheckBox _advanced = new() { Content = "Edit filter definitions as text" };
    private readonly TextBlock _rawError = new() { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _duplicate = new() { Content = "Duplicate filter" };
    private readonly Button _remove = new() { Content = "Remove filter" };
    private readonly Button _up = new() { Content = "Move filter up" };
    private readonly Button _down = new() { Content = "Move filter down" };
    private readonly StackPanel _structured = new() { Spacing = 6 };
    private readonly StackPanel _rawPanel = new() { Spacing = 6, IsVisible = false };
    private bool _updating;

    public SavedFiltersEditor(IEnumerable<SavedFilter> filters)
    {
        _draft = filters.Select(f => new SavedFilter { Name = f.Name, Mask = f.Mask }).ToList();
        AutomationProperties.SetName(_list, "Saved filter list");
        AutomationProperties.SetName(_name, "Selected filter name");
        AutomationProperties.SetName(_mask, "Selected filter mask");
        AutomationProperties.SetName(_raw, "Saved filters advanced text");
        _raw.Text = Format(_draft);
        _raw.TextChanged += (_, _) => _rawError.IsVisible = false;
        _list.SelectionChanged += (_, _) => { if (!_updating) ShowSelected(); };
        _name.TextChanged += (_, _) => UpdateSelected();
        _mask.TextChanged += (_, _) => UpdateSelected();
        var add = new Button { Content = "Add filter" };
        add.Click += (_, _) =>
        {
            _draft.Add(new SavedFilter { Name = UniqueName("new-filter"), Mask = "*" });
            Refresh(_draft.Count - 1);
            _name.Focus();
        };
        _duplicate.Click += (_, _) =>
        {
            if (Selected is not { } f) return;
            _draft.Insert(_list.SelectedIndex + 1, new SavedFilter { Name = UniqueName(f.Name + "-copy"), Mask = f.Mask });
            Refresh(_list.SelectedIndex + 1);
            _name.Focus();
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
        _structured.Children.Add(new TextBlock { Text = "Choose a filter to edit its name and mask. Exclude patterns with |, for example *.cs|*.g.cs.", TextWrapping = TextWrapping.Wrap });
        _structured.Children.Add(_list);
        _structured.Children.Add(DialogLayout.Actions(add, _duplicate, _remove, _up, _down));
        _structured.Children.Add(new TextBlock { Text = "Name", FontWeight = FontWeight.SemiBold });
        _structured.Children.Add(_name);
        _structured.Children.Add(new TextBlock { Text = "Mask", FontWeight = FontWeight.SemiBold });
        _structured.Children.Add(_mask);
        _rawPanel.Children.Add(new TextBlock { Text = "One filter per line: name = mask. Return to the list to check and edit individual fields.", TextWrapping = TextWrapping.Wrap });
        _rawPanel.Children.Add(_raw);
        _rawPanel.Children.Add(_rawError);
        _advanced.IsCheckedChanged += (_, _) => SwitchMode();
        View = new StackPanel
        {
            Margin = new Thickness(8), Spacing = 6,
            Children = { _advanced, _structured, _rawPanel },
        };
        Refresh(_draft.Count > 0 ? 0 : -1);
    }

    public Control View { get; }

    public bool TryGet(out List<SavedFilter> filters, out string? error, out TextBox field)
    {
        AutomationProperties.SetHelpText(_name, null);
        AutomationProperties.SetHelpText(_mask, null);
        AutomationProperties.SetHelpText(_raw, null);
        field = _advanced.IsChecked == true ? _raw : _mask;
        string text = _advanced.IsChecked == true ? _raw.Text ?? string.Empty : Format(_draft);
        filters = SettingsDialog.ParseSavedFilters(text, out error);
        if (error is null) return true;
        if (_advanced.IsChecked != true)
        {
            string message = error;
            int item = _draft.FindIndex(f => message.Contains($"\"{f.Name}\"", StringComparison.Ordinal));
            if (item >= 0) Refresh(item);
            // A bad name is more useful to fix in its own field; mask errors belong in the mask field.
            field = error.Contains("expected name", StringComparison.Ordinal) || error.Contains("defined twice", StringComparison.Ordinal)
                ? _name : _mask;
        }
        return false;
    }

    private SavedFilter? Selected
    {
        get
        {
            int i = _list.SelectedIndex;
            return i >= 0 && i < _draft.Count ? _draft[i] : null;
        }
    }

    private void UpdateSelected()
    {
        if (_updating || Selected is not { } f) return;
        f.Name = _name.Text ?? string.Empty;
        f.Mask = _mask.Text ?? string.Empty;
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
        _list.ItemsSource = _draft.Select((f, i) => $"{i + 1}. {(string.IsNullOrWhiteSpace(f.Name) ? "(unnamed)" : f.Name)}  —  {f.Mask}").ToList();
        _list.SelectedIndex = index >= 0 && _draft.Count > 0 ? Math.Min(index, _draft.Count - 1) : -1;
        _updating = false;
        if (show) ShowSelected();
        else RefreshButtons();
    }

    private void ShowSelected()
    {
        _updating = true;
        _name.Text = Selected?.Name ?? string.Empty;
        _mask.Text = Selected?.Mask ?? string.Empty;
        _name.IsEnabled = _mask.IsEnabled = Selected is not null;
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
            _raw.Text = Format(_draft);
            _rawError.IsVisible = false;
            _structured.IsVisible = false;
            _rawPanel.IsVisible = true;
        }
        else
        {
            var parsed = SettingsDialog.ParseSavedFilters(_raw.Text, out var error);
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
            _draft.AddRange(parsed);
            _rawError.IsVisible = false;
            _structured.IsVisible = true;
            _rawPanel.IsVisible = false;
            Refresh(_draft.Count > 0 ? 0 : -1);
        }
    }

    private string UniqueName(string proposal)
    {
        string stem = string.IsNullOrWhiteSpace(proposal) ? "filter" : proposal.Trim();
        string candidate = stem;
        for (int i = 2; _draft.Any(f => string.Equals(f.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++)
            candidate = stem + i;
        return candidate;
    }

    private static string Format(IEnumerable<SavedFilter> filters) => string.Join(Environment.NewLine, filters.Select(f => $"{f.Name} = {f.Mask}"));
}
