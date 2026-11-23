using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Permissions;

namespace FileCat.App.Views;

internal sealed class PosixAccessEditor : StackPanel
{
    private readonly PosixAccessDraft _draft;
    private readonly bool _isDefault;
    private readonly ListBox _rows = new Controls.CountedListBox { MinHeight = 85, MaxHeight = 230, ItemsPanel = PermissionInspectorView.AllRows(), ItemTemplate =
        new FuncDataTemplate<string>((text, _) => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }) };
    private readonly ComboBox _kind = new() { ItemsSource = new[] { "User", "Group" }, SelectedIndex = 0 };
    private readonly TextBox _principal = new() { PlaceholderText = "Numeric user or group ID", MinWidth = 160 };
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly Button _add = new() { Content = "Add entry" };
    private readonly Button _remove = new() { Content = "Remove selected entry" };
    private readonly Button _collapse = new() { Content = "Use base access only" };
    private readonly Button _toggleDefault = new();
    private readonly Button _copy = new() { Content = "Copy selected account ID" };
    private readonly CheckBox[] _selectedRights = RightsControls("Selected entry"), _newRights = RightsControls("New entry");
    private bool _rendering;

    internal PosixAccessEditor(PermissionSnapshot snapshot, Func<string, Task>? copy, bool isDefault = false)
    {
        _isDefault = isDefault;
        _draft = new(snapshot, isDefault);
        Spacing = 7;
        AutomationProperties.SetName(_rows, isDefault ? "Default ACL entries" : "Access ACL entries");
        AutomationProperties.SetName(_kind, "New ACL account type");
        AutomationProperties.SetName(_principal, "New ACL account ID");
        AutomationProperties.SetLiveSetting(_problem, AutomationLiveSetting.Polite);
        Children.Add(new TextBlock { Text = isDefault ? "Inherited by new children" : "Access now", FontWeight = FontWeight.SemiBold });
        Children.Add(_rows);
        Children.Add(Row(_selectedRights[0], _selectedRights[1], _selectedRights[2], _remove, _copy));
        _copy.IsVisible = copy is not null;
        _copy.Click += async (_, _) =>
        {
            if (copy is not null && _rows.SelectedIndex >= 0 && _draft.Rows[_rows.SelectedIndex].Principal is { } principal)
                await copy(principal.Id);
        };
        Children.Add(new TextBlock { Text = "Add a named entry", FontWeight = FontWeight.SemiBold });
        Children.Add(Row(_kind, _principal, _newRights[0], _newRights[1], _newRights[2], _add));
        Children.Add(_problem);
        Children.Add(isDefault ? _toggleDefault : _collapse);

        _rows.SelectionChanged += (_, _) => { if (!_rendering) RefreshSelection(); };
        foreach (var box in _selectedRights)
            box.IsCheckedChanged += (_, _) =>
            {
                if (_rendering || _rows.SelectedIndex < 0) return;
                int index = _rows.SelectedIndex;
                _draft.SetRights(index, ReadRights(_selectedRights));
                RefreshRows(index);
                Changed?.Invoke();
            };
        _principal.PropertyChanged += (_, change) =>
        {
            if (change.Property != TextBox.TextProperty) return;
            ValidateEntry();
            Changed?.Invoke();
        };
        _kind.SelectionChanged += (_, _) => { ValidateEntry(); Changed?.Invoke(); };
        _principal.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
            e.Handled = true;
            AddEntry();
        }, RoutingStrategies.Tunnel);
        _add.Click += (_, _) => AddEntry();
        _remove.Click += (_, _) =>
        {
            int index = _rows.SelectedIndex;
            if (index < 0 || !_draft.Remove(index)) return;
            RefreshRows(index);
            Changed?.Invoke();
        };
        _collapse.Click += (_, _) => { _draft.Collapse(); RefreshRows(0); Changed?.Invoke(); };
        _toggleDefault.Click += (_, _) =>
        {
            if (_draft.Rows.IsEmpty) _draft.CreateDefault();
            else _draft.RemoveDefault();
            RefreshRows(0);
            Changed?.Invoke();
        };
        RefreshRows(0);
    }

    internal event Action? Changed;
    internal ChangePosixAcl Change => new(_isDefault, _draft.Rows);
    internal bool PendingEntry => !string.IsNullOrEmpty(_principal.Text);

    private void AddEntry()
    {
        if (!_draft.TryAdd(_kind.SelectedIndex == 1, _principal.Text, ReadRights(_newRights), out int index)) return;
        _principal.Text = string.Empty;
        foreach (var box in _newRights) box.IsChecked = false;
        RefreshRows(index);
        Changed?.Invoke();
    }

    private void RefreshRows(int index)
    {
        _rendering = true;
        byte mask = PosixAccessDraft.Mask(_draft.Rows);
        _rows.ItemsSource = _draft.Rows.Select(row => $"{PrincipalLabel(row)} · {DescribeRights(row, mask)}").ToArray();
        _rows.SelectedIndex = Math.Min(index, _draft.Rows.Length - 1);
        _collapse.IsEnabled = _draft.Rows.Length > 3;
        bool hasRows = !_draft.Rows.IsEmpty;
        _toggleDefault.Content = hasRows ? "Remove the default ACL" : "Create default ACL from current access";
        _kind.IsEnabled = hasRows;
        _principal.IsEnabled = hasRows;
        if (!hasRows) _principal.Text = string.Empty;
        foreach (var box in _newRights) box.IsEnabled = hasRows;
        RefreshSelection();
        ValidateEntry();
    }

    private void RefreshSelection()
    {
        _rendering = true;
        var selected = _rows.SelectedIndex >= 0 ? _draft.Rows[_rows.SelectedIndex] : (PosixAclEntry?)null;
        for (int i = 0; i < _selectedRights.Length; i++)
        {
            _selectedRights[i].IsEnabled = selected.HasValue;
            _selectedRights[i].IsChecked = selected is { } row && (row.Rights & (4 >> i)) != 0;
        }
        _remove.IsEnabled = selected is { } entry && PosixAccessDraft.IsNamed(entry);
        _copy.IsEnabled = selected?.Principal is not null;
        _rendering = false;
    }

    private void ValidateEntry()
    {
        string? error = _draft.ValidateNewPrincipal(_kind.SelectedIndex == 1, _principal.Text, out _);
        _add.IsEnabled = error is null;
        _problem.IsVisible = PendingEntry && error is not null;
        _problem.Text = _problem.IsVisible ? error : null;
        AutomationProperties.SetHelpText(_principal, _problem.Text ?? "Enter a numeric account ID, then choose Add entry.");
    }

    private static CheckBox[] RightsControls(string label) => new[] { "Read", "Write", "Execute" }.Select(text =>
    {
        var box = new CheckBox { Content = text };
        AutomationProperties.SetName(box, $"{label} {text.ToLowerInvariant()}");
        return box;
    }).ToArray();

    private static byte ReadRights(CheckBox[] boxes) => (byte)((boxes[0].IsChecked == true ? 4 : 0) |
        (boxes[1].IsChecked == true ? 2 : 0) | (boxes[2].IsChecked == true ? 1 : 0));

    private static WrapPanel Row(params Control[] controls)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }

    internal static string PrincipalLabel(PosixAclEntry row) => row.Tag switch
    {
        PosixAclTag.NamedUser => "User " + Formatters.SafeName(row.Principal!.Value.Id),
        PosixAclTag.NamedGroup => "Group " + Formatters.SafeName(row.Principal!.Value.Id),
        PosixAclTag.OwningGroup => "Owning group",
        PosixAclTag.Mask => "Group-class limit",
        PosixAclTag.Other => "Others",
        _ => "Owner"
    };

    internal static string DescribeRights(PosixAclEntry row, byte mask)
    {
        string configured = Rights(row.Rights);
        return row.Tag is PosixAclTag.OwningGroup or PosixAclTag.NamedUser or PosixAclTag.NamedGroup
            ? $"configured {configured} · effective {Rights(PosixAccessDraft.EffectiveRights(row, mask))}" : configured;
    }

    private static string Rights(int bits) => $"{((bits & 4) != 0 ? 'r' : '-')}{((bits & 2) != 0 ? 'w' : '-')}{((bits & 1) != 0 ? 'x' : '-')}";
}
