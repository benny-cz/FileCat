using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using FileCat.App.Services;

namespace FileCat.App.Views;

/// <summary>
/// The command search in the menu bar (plan §4.4, UX-010): typing lists the commands that match by title, synonym,
/// shortcut, or menu path, in a list under the box. Down/Up choose, Enter or a click runs the chosen command in the
/// active panel, F2 changes its shortcut, and Esc returns to the panel. With nothing typed, it lists recently used
/// commands. The list never takes the keyboard from the box.
/// </summary>
internal sealed class CommandSearchBar
{
    private readonly TextBox _box;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly TextBlock _hint;
    private readonly Func<IReadOnlyList<CommandSearch.Entry>> _entries;
    private readonly Func<IReadOnlyList<string>> _recent;
    private readonly Action<string> _run;
    private readonly Func<string, Task> _changeShortcut;
    private readonly Action _leave;
    // What the commands were when the box took the keyboard: availability is for the panel active then.
    private IReadOnlyList<CommandSearch.Entry> _snapshot = [];
    private IReadOnlyList<CommandSearch.Entry> _results = [];

    /// <param name="host">Where the list's popup lives (any panel around the box).</param>
    /// <param name="entries">Every command the search offers, as it applies now.</param>
    /// <param name="recent">Recently used command ids, most recent first.</param>
    /// <param name="run">Runs a chosen command (the box has closed and the panel has the keyboard back).</param>
    /// <param name="changeShortcut">F2: changes the chosen command's shortcut.</param>
    /// <param name="leave">Gives the keyboard back to the panel (Esc).</param>
    public CommandSearchBar(TextBox box, Panel host, Func<IReadOnlyList<CommandSearch.Entry>> entries, Func<IReadOnlyList<string>> recent,
        Action<string> run, Func<string, Task> changeShortcut, Action leave)
    {
        _box = box;
        _entries = entries;
        _recent = recent;
        _run = run;
        _changeShortcut = changeShortcut;
        _leave = leave;
        _list = new ListBox
        {
            Classes = { "choices" },
            Focusable = false,
            MaxHeight = 420,
            ItemTemplate = new FuncDataTemplate<CommandSearch.Entry>((entry, _) => Row(entry)),
        };
        AutomationProperties.SetName(_list, "Commands found");
        _hint = new TextBlock { Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8, 4, 8, 4) };
        var body = new DockPanel();
        DockPanel.SetDock(_hint, Dock.Bottom);
        body.Children.Add(_hint);
        body.Children.Add(_list);
        _popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.BottomEdgeAlignedRight,
            Child = new Border
            {
                Child = body,
                Width = 600,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2),
                [!Border.BackgroundProperty] = new DynamicResourceExtension("FcCard"),
                [!Border.BorderBrushProperty] = new DynamicResourceExtension("FcBorder"),
            },
        };
        host.Children.Add(_popup);
        box.GotFocus += (_, _) => Open();
        box.LostFocus += (_, _) =>
        {
            _popup.IsOpen = false;
            _box.Text = string.Empty;
        };
        box.TextChanged += (_, _) =>
        {
            if (_popup.IsOpen) Update();
        };
        box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        // A click on a command runs it (not one on the scroll bar); the box keeps the keyboard meanwhile.
        Controls.ListClicks.ChooseOnClick<CommandSearch.Entry>(_list, Choose);
    }

    /// <summary>The commands listed now; empty while the list is closed.</summary>
    public IReadOnlyList<CommandSearch.Entry> Shown => _popup.IsOpen ? _results : [];

    /// <summary>The command chosen in the list, if any.</summary>
    public CommandSearch.Entry? Selected => _popup.IsOpen ? _list.SelectedItem as CommandSearch.Entry : null;

    /// <summary>What the list's last line says (how to go on, or why nothing matches).</summary>
    public string? Hint => _popup.IsOpen ? _hint.Text : null;

    private void Open()
    {
        _snapshot = _entries();
        _popup.IsOpen = true;
        Update();
    }

    private void Update()
    {
        string query = (_box.Text ?? string.Empty).Trim();
        _results = CommandSearch.Rank(query, _snapshot, _recent());
        _list.ItemsSource = _results;
        _list.IsVisible = _results.Count > 0;
        if (_results.Count > 0)
        {
            _list.SelectedIndex = 0;
            _list.ScrollIntoView(0);
        }
        _hint.Text = query.Length == 0
            ? _results.Count == 0
                ? "Type what you want to do: \"recover\", \"new folder\", \"zip\", \"hidden files\"…"
                : "Recently used · type to search all commands"
            : _results.Count == 0
                ? $"No command matches \"{query}\". Other words may find it; F1 lists every shortcut."
                : "Enter runs · F2 changes the shortcut · Esc returns to the panel";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        int count = _results.Count;
        switch (e.Key)
        {
            case Key.Escape:
                _leave();
                break;
            case Key.Down when count > 0:
                Move(Math.Min(count - 1, _list.SelectedIndex + 1));
                break;
            case Key.Up when count > 0:
                Move(Math.Max(0, _list.SelectedIndex - 1));
                break;
            case Key.PageDown when count > 0:
                Move(Math.Min(count - 1, _list.SelectedIndex + 10));
                break;
            case Key.PageUp when count > 0:
                Move(Math.Max(0, _list.SelectedIndex - 10));
                break;
            case Key.Enter:
                if (Selected is { } chosen) Choose(chosen);
                break;
            case Key.F2:
                if (Selected is { } entry) _ = ChangeShortcutAsync(entry);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Move(int index)
    {
        _list.SelectedIndex = index;
        _list.ScrollIntoView(index);
    }

    private void Choose(CommandSearch.Entry entry)
    {
        _popup.IsOpen = false;
        _run(entry.Id);
    }

    /// <summary>After the shortcut dialog, the box shows the same search again, with the new shortcut.</summary>
    private async Task ChangeShortcutAsync(CommandSearch.Entry entry)
    {
        string query = _box.Text ?? string.Empty;
        await _changeShortcut(entry.Id);
        _box.Focus();
        _box.Text = query;
        _box.CaretIndex = query.Length;
        if (!_popup.IsOpen) Open();
        else
        {
            _snapshot = _entries();
            Update();
        }
        int index = _results.ToList().FindIndex(r => r.Id == entry.Id);
        if (index >= 0) Move(index);
    }

    private static Control Row(CommandSearch.Entry? entry)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Opacity = entry?.Enabled == false ? 0.6 : 1 };
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = entry?.Title, TextTrimming = TextTrimming.CharacterEllipsis });
        if (entry is not null)
        {
            // Where it is, and what it does in a line or two (the whole text on hover).
            string detail = CommandSearch.Detail(entry);
            var line = new TextBlock { Text = detail, Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTip.SetTip(line, detail);
            left.Children.Add(line);
        }
        grid.Children.Add(left);
        if (!string.IsNullOrEmpty(entry?.Gesture))
        {
            var gesture = new TextBlock
            {
                Text = entry.Gesture,
                Classes = { "muted", "small" },
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(gesture, 1);
            grid.Children.Add(gesture);
        }
        return grid;
    }
}
