using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using FileCat.App.Services;

namespace FileCat.App.Controls;

/// <summary>
/// Folder suggestions under a path text box (a panel's location box, a copy's destination), as Explorer's address bar
/// offers them: typing a full path lists the folders that complete it, read in the background. Down/Up choose, Tab
/// completes to the chosen (or first) folder and goes on with its folders, Enter or a click chooses, Esc closes the
/// list. The list never takes the keyboard from the box, and it takes Up/Down and Tab only while it is shown.
/// </summary>
internal sealed class PathCompletion
{
    private readonly TextBox _box;
    private readonly Popup _popup;
    private readonly ListBox _list;
    private readonly Func<bool> _includeHidden;
    private readonly Func<string?> _unchanged;

    /// <param name="host">Where the list's popup lives (any panel around the box).</param>
    /// <param name="includeHidden">Whether hidden folders are suggested ("Show hidden").</param>
    /// <param name="unchanged">A text that needs no suggestions (what the box showed before any typing).</param>
    /// <param name="handleKeys">
    /// False when the box's owner calls <see cref="HandleKey"/> first thing in its own key handler (it has keys of its own).
    /// </param>
    public PathCompletion(TextBox box, Panel host, Func<bool> includeHidden, Func<string?>? unchanged = null, bool handleKeys = true)
    {
        _box = box;
        _includeHidden = includeHidden;
        _unchanged = unchanged ?? (() => null);
        _list = new ListBox { Classes = { "choices", "suggestions" }, Focusable = false, MaxHeight = 320 };
        Avalonia.Automation.AutomationProperties.SetName(_list, "Folder suggestions");
        _popup = new Popup
        {
            PlacementTarget = box,
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            Child = new Border
            {
                Child = _list,
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(2),
                [!Border.BackgroundProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FcCard"),
                [!Border.BorderBrushProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("FcBorder"),
            },
        };
        host.Children.Add(_popup);
        box.TextChanged += (_, _) =>
        {
            if (box.IsFocused && box.Text != _unchanged()) Update();
            else Close();
        };
        box.LostFocus += (_, _) => Close();
        if (handleKeys)
        {
            box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (HandleKey(e)) e.Handled = true;
            }, RoutingStrategies.Tunnel);
        }
        _list.Tapped += (_, _) =>
        {
            if (_list.SelectedItem is not string path) return;
            Close();
            Chosen?.Invoke(path);
        };
    }

    /// <summary>A suggestion chosen with Enter or a click.</summary>
    public event Action<string>? Chosen;

    /// <summary>The folders suggested now; empty while none are shown.</summary>
    public IReadOnlyList<string> Shown => _popup.IsOpen ? _list.Items.OfType<string>().ToList() : [];

    public void Close()
    {
        _wanted = null;
        _popup.IsOpen = false;
    }

    // The text to suggest for (null: closed), and whether a lookup runs. Both are touched on the UI thread only.
    private string? _wanted;
    private bool _looking;

    private void Update()
    {
        _wanted = _box.Text ?? string.Empty;
        // Match the list's width to the box's (the popup is placed under it).
        _list.MinWidth = _box.Bounds.Width;
        if (!_looking) _ = LookAsync();
    }

    /// <summary>
    /// One lookup at a time, for the latest text: an unreachable network path can hold a lookup for half a minute, and
    /// typing meanwhile must not start more of them.
    /// </summary>
    private async Task LookAsync()
    {
        _looking = true;
        try
        {
            while (true)
            {
                await Task.Delay(120); // typing settles first
                if (_wanted is not { } text) return;
                bool hidden = _includeHidden();
                var found = await Task.Run(() => PathSuggestions.For(text, hidden, CancellationToken.None));
                if (_wanted is null || !_box.IsFocused) return;
                if (_wanted != text) continue; // typed on meanwhile: look again, for what is there now
                // Nothing to offer when the one folder found is what is already typed.
                if (found.Count == 0 || found.Count == 1 && string.Equals(found[0], text.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                {
                    _popup.IsOpen = false;
                    return;
                }
                _list.ItemsSource = found;
                _list.SelectedIndex = -1;
                _popup.IsOpen = true;
                return;
            }
        }
        finally
        {
            _looking = false;
        }
    }

    /// <summary>The list's keys while it is shown; false for any other key (the box handles it as before).</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        if (!_popup.IsOpen || _list.ItemCount == 0) return false;
        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1);
                _list.ScrollIntoView(_list.SelectedIndex);
                return true;
            case Key.Up:
                _list.SelectedIndex = Math.Max(-1, _list.SelectedIndex - 1);
                if (_list.SelectedIndex >= 0) _list.ScrollIntoView(_list.SelectedIndex);
                return true;
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                string chosen = _list.SelectedItem as string ?? _list.Items.OfType<string>().First();
                _box.Text = chosen + Path.DirectorySeparatorChar;
                _box.CaretIndex = _box.Text.Length;
                return true;
            case Key.Enter when _list.SelectedItem is string selected:
                Close();
                Chosen?.Invoke(selected);
                return true;
            case Key.Escape:
                Close();
                return true;
            default:
                return false;
        }
    }
}
