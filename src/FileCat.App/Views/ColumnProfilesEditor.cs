using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.Controls;

namespace FileCat.App.Views;

/// <summary>
/// Settings → Columns: profiles in Alt+0–9 order with their columns. Widths are pixels; 0 fills the remaining
/// space. Changes apply on OK.
/// </summary>
internal sealed class ColumnProfilesEditor
{
    private sealed class Profile(string name, List<(string Key, double Width)> columns)
    {
        public string Name { get; set; } = name;
        public List<(string Key, double Width)> Columns { get; } = columns;
    }

    private readonly ColumnProfileSet _set;
    private readonly List<Profile> _profiles;
    private readonly ListBox _profileList = new() { MinWidth = 220, MinHeight = 240 };
    private readonly ListBox _columnList = new() { MinWidth = 300, MinHeight = 200 };
    private readonly TextBox _name = new() { MinWidth = 220 };
    private readonly NumericUpDown _width = new() { Minimum = 0, Maximum = 2000, Increment = 10, FormatString = "0", MinWidth = 140 };
    private readonly ComboBox _add = new() { MinWidth = 180, ItemsSource = ColumnProfileSet.Choices.Select(c => c.Title).ToList(), SelectedIndex = 0 };
    private bool _updating;

    public ColumnProfilesEditor(ColumnProfileSet set)
    {
        _set = set;
        _profiles = Load(set.Profiles);
        AutomationProperties.SetName(_profileList, "Column profiles");
        AutomationProperties.SetName(_columnList, "Columns of the selected profile");
        AutomationProperties.SetName(_name, "Profile name");
        AutomationProperties.SetName(_width, "Column width in pixels, 0 fills the remaining space");
        AutomationProperties.SetName(_add, "Column to add");

        _profileList.SelectionChanged += (_, _) => { if (!_updating) ShowProfile(); };
        _columnList.SelectionChanged += (_, _) => { if (!_updating) ShowColumn(); };
        _name.TextChanged += (_, _) =>
        {
            if (_updating || Selected is not { } p) return;
            p.Name = _name.Text ?? string.Empty;
            RefreshProfiles();
        };
        _width.ValueChanged += (_, _) =>
        {
            int ci = _columnList.SelectedIndex;
            if (_updating || Selected is not { } p || ci < 0 || ci >= p.Columns.Count) return;
            p.Columns[ci] = (p.Columns[ci].Key, (double)(_width.Value ?? 0));
            RefreshColumns();
        };

        var profileButtons = Buttons(
            ("New", () => AddProfile(new Profile("New profile", [("name", 0), ("size", 86), ("modified", 128)]))),
            ("Duplicate", () => { if (Selected is { } p) AddProfile(new Profile(p.Name + " copy", [.. p.Columns])); }),
            ("Remove", RemoveProfile),
            ("Up", () => MoveProfile(-1)),
            ("Down", () => MoveProfile(1)));
        var restore = new Button { Content = "Restore defaults" };
        restore.Click += (_, _) =>
        {
            _profiles.Clear();
            _profiles.AddRange(Load(ColumnProfiles.Defaults));
            RefreshProfiles(select: 0);
        };
        var addButton = new Button { Content = "Add" };
        addButton.Click += (_, _) => AddColumn();
        var columnButtons = Buttons(
            ("Remove", RemoveColumn),
            ("Up", () => MoveColumn(-1)),
            ("Down", () => MoveColumn(1)));

        var left = new StackPanel
        {
            Spacing = 6,
            Children = { Label("Profiles (Alt+0 is the first)"), _profileList, profileButtons, restore, Label("Name"), _name },
        };
        var right = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Label("Columns"), _columnList,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { _add, addButton } },
                columnButtons,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Label("Width"), _width, Note("pixels; 0 fills the remaining space") } },
                Note("Dragging a column border in a panel also stores its width here. The Name column is always kept."),
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,24,*"), Margin = new Thickness(8) };
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        View = new ScrollViewer { Content = grid, MaxHeight = 520 };
        RefreshProfiles(select: 0);
    }

    public Control View { get; }

    private Profile? Selected
    {
        get
        {
            int i = _profileList.SelectedIndex;
            return i >= 0 && i < _profiles.Count ? _profiles[i] : null;
        }
    }

    /// <summary>Stores the edited profiles; tabs pick them up immediately.</summary>
    public void Apply() =>
        _set.Replace(_profiles.Select((p, i) => (
            string.IsNullOrWhiteSpace(p.Name) ? $"Profile {i}" : p.Name.Trim(),
            p.Columns.Select(c => ColumnProfileSet.Create(c.Key, c.Width)).OfType<ColumnSpec>().ToArray())));

    private static List<Profile> Load(IEnumerable<(string Name, ColumnSpec[] Columns)> profiles) =>
        profiles.Select(p => new Profile(p.Name, p.Columns.Select(c => (ColumnProfileSet.KeyOf(c), c.Star ? 0d : c.Width)).ToList())).ToList();

    private void AddProfile(Profile p)
    {
        if (_profiles.Count >= ColumnProfileSet.MaxProfiles) return;
        _profiles.Add(p);
        RefreshProfiles(select: _profiles.Count - 1);
    }

    private void RemoveProfile()
    {
        int i = _profileList.SelectedIndex;
        if (i < 0 || _profiles.Count <= 1) return;
        _profiles.RemoveAt(i);
        RefreshProfiles(select: Math.Min(i, _profiles.Count - 1));
    }

    private void MoveProfile(int delta)
    {
        int i = _profileList.SelectedIndex, k = i + delta;
        if (i < 0 || k < 0 || k >= _profiles.Count) return;
        (_profiles[i], _profiles[k]) = (_profiles[k], _profiles[i]);
        RefreshProfiles(select: k);
    }

    private void AddColumn()
    {
        if (Selected is not { } p || _add.SelectedIndex < 0) return;
        var choice = ColumnProfileSet.Choices[_add.SelectedIndex];
        if (p.Columns.Any(c => c.Key == choice.Key)) return;
        p.Columns.Add((choice.Key, choice.DefaultWidth));
        RefreshColumns(select: p.Columns.Count - 1);
    }

    private void RemoveColumn()
    {
        int i = _columnList.SelectedIndex;
        if (Selected is not { } p || i < 0 || i >= p.Columns.Count || p.Columns[i].Key == "name") return;
        p.Columns.RemoveAt(i);
        RefreshColumns(select: Math.Min(i, p.Columns.Count - 1));
    }

    private void MoveColumn(int delta)
    {
        int i = _columnList.SelectedIndex, k = i + delta;
        if (Selected is not { } p || i < 0 || k < 0 || k >= p.Columns.Count) return;
        (p.Columns[i], p.Columns[k]) = (p.Columns[k], p.Columns[i]);
        RefreshColumns(select: k);
    }

    private void RefreshProfiles(int? select = null)
    {
        _updating = true;
        int index = select ?? _profileList.SelectedIndex;
        _profileList.ItemsSource = _profiles.Select((p, i) => $"Alt+{i}   {p.Name}").ToList();
        _profileList.SelectedIndex = Math.Clamp(index, 0, _profiles.Count - 1);
        _updating = false;
        if (select is not null) ShowProfile();
    }

    private void ShowProfile()
    {
        _updating = true;
        _name.Text = Selected?.Name ?? string.Empty;
        _updating = false;
        RefreshColumns(select: 0);
    }

    private void RefreshColumns(int? select = null)
    {
        _updating = true;
        int index = select ?? _columnList.SelectedIndex;
        var p = Selected;
        _columnList.ItemsSource = p?.Columns.Select(c =>
        {
            var title = ColumnProfileSet.Choices.FirstOrDefault(x => x.Key == c.Key)?.Title ?? c.Key;
            return $"{title}   —   {(c.Width <= 0 ? "fills" : c.Width.ToString("0") + " px")}";
        }).ToList() ?? [];
        _columnList.SelectedIndex = p is null || p.Columns.Count == 0 ? -1 : Math.Clamp(index, 0, p.Columns.Count - 1);
        _updating = false;
        ShowColumn();
    }

    private void ShowColumn()
    {
        _updating = true;
        var p = Selected;
        int i = _columnList.SelectedIndex;
        _width.Value = p is not null && i >= 0 && i < p.Columns.Count ? (decimal)Math.Max(0, p.Columns[i].Width) : 0;
        _updating = false;
    }

    private static StackPanel Buttons(params (string Text, Action Click)[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (text, click) in buttons)
        {
            var b = new Button { Content = text };
            b.Click += (_, _) => click();
            panel.Children.Add(b);
        }
        return panel;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Note(string text) =>
        new() { Text = text, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxWidth = 420, VerticalAlignment = VerticalAlignment.Center };
}
