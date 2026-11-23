using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.Core.FileSystem;
using FileCat.Core.Permissions;

namespace FileCat.App.Views;

internal sealed class UnixPermissionModeEditor : StackPanel
{
    private static readonly (string Label, UnixFileMode[] Bits)[] Rows =
    [
        ("Owner", [UnixFileMode.UserRead, UnixFileMode.UserWrite, UnixFileMode.UserExecute]),
        ("Group", [UnixFileMode.GroupRead, UnixFileMode.GroupWrite, UnixFileMode.GroupExecute]),
        ("Others", [UnixFileMode.OtherRead, UnixFileMode.OtherWrite, UnixFileMode.OtherExecute])
    ];

    private readonly Func<UnixFileMode, bool?> _initial;
    private UnixPermissionModeDraft _draft;
    private readonly TextBox _octal = new() { Width = 80 };
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private readonly List<(CheckBox Box, UnixFileMode Bit)> _boxes = [];
    private bool _updating;

    internal UnixPermissionModeEditor(Func<UnixFileMode, bool?> initial, bool groupClassLimit)
    {
        _initial = initial;
        _draft = new(initial);
        Spacing = 4;
        // Only a selection whose objects differ has no octal value; a screen reader also reads the placeholder.
        if (Enum.GetValues<UnixFileMode>().Any(bit => bit is not UnixFileMode.None && initial(bit) is null))
            _octal.PlaceholderText = "mixed";
        string[] headings = ["Read", "Write", "Execute"];
        var grid = new Grid
        {
            ColumnDefinitions = new("*,Auto,Auto,Auto"), RowDefinitions = new("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Left
        };
        for (int c = 0; c < headings.Length; c++)
            Add(grid, new TextBlock { Text = headings[c] }, 0, c + 1);
        for (int r = 0; r < Rows.Length; r++)
        {
            var (label, bits) = Rows[r];
            if (r == 1 && groupClassLimit) label = "Group-class limit";
            Add(grid, new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap }, r + 1, 0);
            for (int c = 0; c < bits.Length; c++)
                Add(grid, Box($"{label} {headings[c].ToLowerInvariant()}", bits[c]), r + 1, c + 1);
        }
        Children.Add(grid);
        var special = new WrapPanel { Orientation = Orientation.Horizontal };
        special.Children.Add(Box("Set user ID", UnixFileMode.SetUser, "Set user ID"));
        special.Children.Add(Box("Set group ID", UnixFileMode.SetGroup, "Set group ID"));
        special.Children.Add(Box("Sticky", UnixFileMode.StickyBit, "Sticky"));
        Children.Add(special);
        AutomationProperties.SetName(_octal, "Permissions as octal");
        AutomationProperties.SetLiveSetting(_error, AutomationLiveSetting.Polite);
        Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            { new TextBlock { Text = "Octal", VerticalAlignment = VerticalAlignment.Center }, _octal, _summary } });
        Children.Add(_error);
        RefreshDraft();

        _octal.PropertyChanged += (_, change) =>
        {
            if (_updating || change.Property != TextBox.TextProperty) return;
            _draft.EditOctal(_octal.Text);
            RefreshDraft();
            Changed?.Invoke();
        };
    }

    internal event Action? Changed;

    internal bool HasChanges => TryGetChange(out var change) && change.TouchedBits != 0;
    internal bool TryGetChange(out ChangePermissionMode change) => _draft.TryGetChange(_octal.Text, out change);

    internal void Discard()
    {
        _draft = new(_initial);
        RefreshDraft();
        Changed?.Invoke();
    }

    private CheckBox Box(string name, UnixFileMode bit, string? caption = null)
    {
        var box = new CheckBox { Content = caption, MinWidth = caption is null ? 0 : 24 };
        AutomationProperties.SetName(box, name);
        _boxes.Add((box, bit));
        box.IsCheckedChanged += (_, _) =>
        {
            if (_updating) return;
            _draft.ChangeBit(bit, box.IsChecked);
            RefreshDraft();
            Changed?.Invoke();
        };
        return box;
    }

    private void RefreshDraft()
    {
        _updating = true;
        _octal.Text = _draft.Octal;
        if (_draft.IsValid)
            foreach (var (box, bit) in _boxes)
            {
                box.IsThreeState = _initial(bit) is null;
                box.IsChecked = _draft.State(bit);
            }
        _updating = false;

        const string invalid = "Enter three or four octal digits (0–7), or use the permission checkboxes.";
        _error.IsVisible = !_draft.IsValid;
        _error.Text = _draft.IsValid ? string.Empty : invalid;
        AutomationProperties.SetHelpText(_octal, _draft.IsValid ? string.Empty : invalid);
        _summary.Text = !_draft.IsValid ? string.Empty : _draft.Mode is { } mode ? UnixPermissions.Format(mode)
            : "Mixed. Unedited bits keep their current values.";
    }

    private static void Add(Grid grid, Control child, int row, int column)
    {
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        grid.Children.Add(child);
    }
}
