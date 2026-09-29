using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using FileCat.App.ViewModels;
using FileCat.Core.Search;

namespace FileCat.App.Views;

/// <summary>Find's own dialogs (plan §11): advanced criteria, the folders it skips, and saved searches.</summary>
internal static class FindDialogs
{
    private static readonly string[] SizeUnits = ["bytes", "KB", "MB", "GB"];
    private static readonly string[] TimeUnits = ["seconds", "minutes", "hours", "days", "weeks", "months", "years"];

    private static void Name(Control control, string name) => Avalonia.Automation.AutomationProperties.SetName(control, name);

    private static TextBlock Muted(string text) => new() { Text = text, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 2) };

    /// <summary>
    /// Attributes as three-state boxes (checked: set, empty: not set, filled: either), size bounds with units, and
    /// modification and creation times. Returns the edited criteria, or null when canceled.
    /// </summary>
    public static async Task<AdvancedSearchCriteria?> AdvancedAsync(IDialogService dialogs, AdvancedSearchCriteria current)
    {
        var attributes = AdvancedSearchCriteria.Attributes.Select(a =>
        {
            var box = new CheckBox { Content = a.Attribute == FileAttributes.Directory ? "Folder" : char.ToUpperInvariant(a.Name[0]) + a.Name[1..], IsThreeState = true };
            ToolTip.SetTip(box, a.Attribute == FileAttributes.Directory
                ? "Checked finds folders only, empty finds files only, filled finds both"
                : $"Checked finds only {a.Name} items, empty only items that are not {a.Name}, filled finds both");
            return (a.Attribute, Box: box);
        }).ToList();
        var attributeRow = new WrapPanel { ItemSpacing = 14, LineSpacing = 4 };
        foreach (var (_, box) in attributes) attributeRow.Children.Add(box);

        TextBox Number(string name) => new() { Width = 90, HorizontalContentAlignment = HorizontalAlignment.Right, [Avalonia.Automation.AutomationProperties.NameProperty] = name };
        ComboBox Units(string[] units, string name) => new() { ItemsSource = units, Width = 110, [Avalonia.Automation.AutomationProperties.NameProperty] = name };
        var atLeast = Number("Size at least");
        var atLeastUnit = Units(SizeUnits, "Unit of the size at least");
        var atMost = Number("Size at most");
        var atMostUnit = Units(SizeUnits, "Unit of the size at most");
        var sizes = new WrapPanel
        {
            ItemSpacing = 8,
            LineSpacing = 4,
            Children =
            {
                Word("At least"), atLeast, atLeastUnit, new Border { Width = 16 }, Word("At most"), atMost, atMostUnit,
            },
        };

        var modified = new TimeEditor("Modified");
        var created = new TimeEditor("Created");
        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var reset = new Button { Content = "Clear all criteria", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };

        void Load(AdvancedSearchCriteria c)
        {
            foreach (var (attribute, box) in attributes)
                box.IsChecked = (c.AttributesSet & attribute) != 0 ? true : (c.AttributesClear & attribute) != 0 ? false : null;
            atLeast.Text = c.SizeAtLeast?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
            atLeastUnit.SelectedIndex = (int)c.SizeAtLeastUnit;
            atMost.Text = c.SizeAtMost?.ToString("0.##", CultureInfo.CurrentCulture) ?? string.Empty;
            atMostUnit.SelectedIndex = (int)c.SizeAtMostUnit;
            modified.Load(c.Modified);
            created.Load(c.Created);
        }

        // The criteria as edited, or the reason they cannot be used.
        AdvancedSearchCriteria? Read(out string? problem)
        {
            problem = null;
            var c = new AdvancedSearchCriteria();
            foreach (var (attribute, box) in attributes)
            {
                if (box.IsChecked == true) c.AttributesSet |= attribute;
                else if (box.IsChecked == false) c.AttributesClear |= attribute;
            }
            if (!Size(atLeast.Text, out var least)) problem = "At least: enter a number, such as 10 or 1.5.";
            else if (!Size(atMost.Text, out var most)) problem = "At most: enter a number, such as 10 or 1.5.";
            else
            {
                c.SizeAtLeast = least;
                c.SizeAtLeastUnit = (SizeUnit)Math.Max(0, atLeastUnit.SelectedIndex);
                c.SizeAtMost = most;
                c.SizeAtMostUnit = (SizeUnit)Math.Max(0, atMostUnit.SelectedIndex);
                if (least is { } l && most is { } m && AdvancedSearchCriteria.Bytes(l, c.SizeAtLeastUnit) > AdvancedSearchCriteria.Bytes(m, c.SizeAtMostUnit))
                    problem = "The size at least is more than the size at most, so nothing could match.";
            }
            problem ??= modified.Read(c.Modified) ?? created.Read(c.Created);
            return problem is null ? c : null;
        }

        void Validate()
        {
            Read(out var problem);
            error.Text = problem ?? string.Empty;
            error.IsVisible = problem is not null;
        }

        Load(current);
        foreach (var (_, box) in attributes) box.IsCheckedChanged += (_, _) => Validate();
        foreach (var box in new[] { atLeast, atMost }) box.TextChanged += (_, _) => Validate();
        foreach (var combo in new[] { atLeastUnit, atMostUnit }) combo.SelectionChanged += (_, _) => Validate();
        modified.Changed += Validate;
        created.Changed += Validate;
        reset.Click += (_, _) =>
        {
            Load(new AdvancedSearchCriteria());
            Validate();
        };

        var body = new StackPanel
        {
            Spacing = 4,
            MaxWidth = 640,
            Children =
            {
                Heading("Attributes"), attributeRow,
                Muted("Checked: the item has it · empty: it does not · filled: either"),
                Heading("Size"), sizes,
                Heading("Modified"), modified.View,
                Heading("Created"), created.View,
                Muted($"Dates such as {DateTime.Today:d} or {DateTime.Now:g}; a date alone covers that whole day."),
                error, reset,
            },
        };
        var answer = await dialogs.ShowCustomAsync("Advanced criteria", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("OK", "ok", IsDefault: true)],
            attributes[0].Box, canConfirm: () => Read(out _) is not null);
        return answer as string == "ok" ? Read(out _) : null;
    }

    private static TextBlock Word(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private static bool Size(string? text, out double? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var v) || v < 0 || double.IsInfinity(v)) return false;
        value = v;
        return true;
    }

    /// <summary>A time criterion's controls: any time, the last N units, or a range whose ends are optional.</summary>
    private sealed class TimeEditor
    {
        private readonly RadioButton _any, _within, _between;
        private readonly TextBox _amount = new() { Width = 70, HorizontalContentAlignment = HorizontalAlignment.Right };
        private readonly ComboBox _unit = new() { ItemsSource = TimeUnits, Width = 110 };
        private readonly TextBox _from = new() { Width = 170 };
        private readonly TextBox _to = new() { Width = 170 };

        public TimeEditor(string what)
        {
            string group = "time-" + what + "-" + Guid.NewGuid().ToString("N");
            _any = new RadioButton { Content = "Any time", GroupName = group };
            _within = new RadioButton { Content = "In the last", GroupName = group };
            _between = new RadioButton { Content = "From", GroupName = group };
            _from.PlaceholderText = "any start";
            _to.PlaceholderText = "any end";
            Name(_amount, what + " in the last");
            Name(_unit, what + " unit");
            Name(_from, what + " from");
            Name(_to, what + " to");
            View = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    _any,
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _within, _amount, _unit } },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _between, _from, Word("to"), _to } },
                },
            };
            foreach (var radio in new[] { _any, _within, _between }) radio.IsCheckedChanged += (_, _) => Changed?.Invoke();
            foreach (var box in new[] { _amount, _from, _to })
            {
                box.TextChanged += (_, _) => Changed?.Invoke();
                // Typing in a field chooses its line.
                box.GotFocus += (_, _) =>
                {
                    if (box == _amount) _within.IsChecked = true;
                    else _between.IsChecked = true;
                };
            }
            _unit.SelectionChanged += (_, _) => Changed?.Invoke();
        }

        public Control View { get; }

        public event Action? Changed;

        public void Load(TimeCriterion c)
        {
            _any.IsChecked = c.Mode == TimeFilterMode.Any;
            _within.IsChecked = c.Mode == TimeFilterMode.Within;
            _between.IsChecked = c.Mode == TimeFilterMode.Between;
            _amount.Text = c.Amount.ToString(CultureInfo.CurrentCulture);
            _unit.SelectedIndex = (int)c.Unit;
            _from.Text = Format(c.From);
            _to.Text = Format(c.To);
        }

        private static string Format(DateTime? t) => t is not { } v ? string.Empty
            : v.TimeOfDay == TimeSpan.Zero ? v.ToString("d", CultureInfo.CurrentCulture) : v.ToString("g", CultureInfo.CurrentCulture);

        /// <summary>Reads the controls into <paramref name="c"/>; returns why they cannot be used, or null.</summary>
        public string? Read(TimeCriterion c)
        {
            c.Unit = (TimeUnit)Math.Max(0, _unit.SelectedIndex);
            if (int.TryParse(_amount.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int amount) && amount > 0) c.Amount = amount;
            else if (_within.IsChecked == true) return "In the last: enter a whole number above zero.";
            c.Mode = _within.IsChecked == true ? TimeFilterMode.Within : _between.IsChecked == true ? TimeFilterMode.Between : TimeFilterMode.Any;
            if (c.Mode != TimeFilterMode.Between) return null;
            if (!When(_from.Text, out var from)) return $"From: \"{_from.Text}\" is not a date (such as {DateTime.Now:d}).";
            if (!When(_to.Text, out var to)) return $"To: \"{_to.Text}\" is not a date (such as {DateTime.Now:d}).";
            // A date without a time ends at the end of that day.
            if (to is { } end && end.TimeOfDay == TimeSpan.Zero && !(_to.Text ?? string.Empty).Contains(':')) to = end.AddDays(1).AddTicks(-1);
            c.From = from;
            c.To = to;
            if (from > to) return "The time range ends before it starts, so nothing could match.";
            return null;
        }

        private static bool When(string? text, out DateTime? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text)) return true;
            if (!DateTime.TryParse(text.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AllowWhiteSpaces, out var t)) return false;
            value = DateTime.SpecifyKind(t, DateTimeKind.Local);
            return true;
        }
    }

    /// <summary>The folders Find skips, each switchable (plan §11). Returns the edited list, or null when canceled.</summary>
    public static async Task<List<IgnoredFolderEntry>?> IgnoredFoldersAsync(IDialogService dialogs, IReadOnlyList<IgnoredFolderEntry> current)
    {
        var entries = new Avalonia.Collections.AvaloniaList<IgnoredFolderEntry>(current.Select(e => new IgnoredFolderEntry { Folder = e.Folder, Enabled = e.Enabled }));
        var list = new ListBox
        {
            Classes = { "choices" },
            MinHeight = 120,
            MaxHeight = 280,
            ItemsSource = entries,
            ItemTemplate = new FuncDataTemplate<IgnoredFolderEntry>((entry, _) =>
            {
                var box = new CheckBox { Content = entry?.Folder, IsChecked = entry?.Enabled ?? true };
                box.IsCheckedChanged += (_, _) =>
                {
                    if (entry is not null) entry.Enabled = box.IsChecked == true;
                };
                return box;
            }),
        };
        Name(list, "Ignored folders");
        var add = new TextBox { PlaceholderText = "node_modules, \\build, or a full path", MinWidth = 360 };
        Name(add, "Folder to skip");
        var addButton = new Button { Content = "Add" };
        var remove = new Button { Content = "Remove" };
        void Add()
        {
            var text = (add.Text ?? string.Empty).Trim();
            if (text.Length == 0) return;
            if (!entries.Any(e => string.Equals(e.Folder, text, StringComparison.OrdinalIgnoreCase))) entries.Add(new IgnoredFolderEntry { Folder = text });
            add.Text = string.Empty;
        }
        addButton.Click += (_, _) => Add();
        add.KeyDown += (_, e) =>
        {
            if (e.Key != Avalonia.Input.Key.Enter || string.IsNullOrWhiteSpace(add.Text)) return;
            e.Handled = true; // Enter adds the folder here; it does not close the dialog
            Add();
        };
        remove.Click += (_, _) =>
        {
            if (list.SelectedItem is IgnoredFolderEntry selected) entries.Remove(selected);
        };
        var body = new StackPanel
        {
            Spacing = 6,
            MaxWidth = 620,
            Children =
            {
                Muted("Find does not search inside these folders; the log lists each one it skipped. A name skips every folder of that name, a path starting with a separator skips that folder in each searched folder, and a full path skips one folder."),
                list,
                new DockPanel { Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, [DockPanel.DockProperty] = Dock.Right, Children = { addButton, remove } }, add } },
            },
        };
        var answer = await dialogs.ShowCustomAsync("Ignored folders", body,
            [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("OK", "ok", IsDefault: true)], add);
        return answer as string == "ok" ? [.. entries] : null;
    }

    /// <summary>One line describing saved criteria ("*.log in C:\Logs · containing "error" · at least 1 MB").</summary>
    public static string Describe(SearchCriteria c)
    {
        var parts = new List<string> { (string.IsNullOrWhiteSpace(c.Names) ? "*" : c.Names) + (c.LookIn.Length > 0 ? " in " + c.LookIn : string.Empty) };
        if (c.Text.Length > 0) parts.Add((c.Hex ? "bytes " : "containing ") + (c.Hex ? c.Text : $"\"{c.Text}\""));
        if (!c.Advanced.IsEmpty) parts.Add(c.Advanced.Summary());
        return string.Join(" · ", parts);
    }
}
