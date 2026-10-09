using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

/// <summary>
/// In-window modal dialogs on a dimmed backdrop. Keyboard-first: Enter confirms, Esc cancels, Tab cycles
/// inside the dialog, and focus returns to where it was when the dialog closes (plan §4.3).
/// </summary>
public sealed class OverlayDialogService(Panel host, Func<IInputElement?> fallbackFocus) : IDialogService
{
    private int _open;

    public bool IsOpen => _open > 0;

    /// <summary>Cancels every open dialog, the topmost first, as Esc would (their window is closing).</summary>
    public void CancelAll()
    {
        for (int i = _sessions.Count - 1; i >= 0; i--)
            if (i < _sessions.Count) _sessions[i].OnEscape();
    }

    private sealed class Session
    {
        public required Border Layer;
        public IInputElement? PreviousFocus;
        public required Action OnEscape;
    }

    // Open dialogs, the topmost last, and the window whose Esc reaches them wherever the keyboard is.
    private readonly List<Session> _sessions = [];
    private TopLevel? _escapeRoot;
    // An event's Source can retain the closed dialog; the guard is needed only while that event is routed.
    private WeakReference<KeyEventArgs>? _escapeClosing;

    /// <summary>
    /// Esc closes the topmost dialog even when the keyboard is outside it (a click elsewhere, focus that never
    /// arrived): the dialog's own handler takes Esc pressed inside it, including what its controls close first.
    /// </summary>
    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        // A dialog that closed itself on this Esc: the one below it stays.
        if (_escapeClosing is { } remembered && remembered.TryGetTarget(out var closing) && ReferenceEquals(e, closing)) return;
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || _sessions.Count == 0) return;
        var top = _sessions[^1];
        if (e.Source is Visual source && (ReferenceEquals(source, top.Layer) || top.Layer.IsVisualAncestorOf(source))) return;
        e.Handled = true;
        top.OnEscape();
    }

    private Session Show(Control card, Control? initialFocus, bool top, Action onEscape)
    {
        var focusManager = TopLevel.GetTopLevel(host)?.FocusManager;
        var session = new Session
        {
            PreviousFocus = focusManager?.GetFocusedElement(),
            Layer = new Border
            {
                Classes = { "backdrop" },
                Child = card,
            },
            OnEscape = onEscape,
        };
        if (_escapeRoot is null && TopLevel.GetTopLevel(host) is { } root)
        {
            _escapeRoot = root;
            root.AddHandler(InputElement.KeyDownEvent, OnRootKeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        }
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
        card.Margin = top ? new Thickness(24, 56, 24, 24) : new Thickness(24);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        session.Layer.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _escapeClosing = new(e);
                onEscape();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        session.Layer.PointerPressed += (_, e) =>
        {
            if (ReferenceEquals(e.Source, session.Layer)) e.Handled = true;
        };
        host.Children.Add(session.Layer);
        host.IsVisible = true;
        _open++;
        _sessions.Add(session);
        // A list takes focus through its selected item, so its arrow keys work at once.
        Dispatcher.UIThread.Post(() => Controls.ListKeys.Focus(initialFocus ?? card), DispatcherPriority.Input);
        return session;
    }

    private void Close(Session s)
    {
        _sessions.Remove(s);
        host.Children.Remove(s.Layer);
        _open = Math.Max(0, _open - 1);
        host.IsVisible = host.Children.Count > 0;
        var restore = s.PreviousFocus is Visual v && TopLevel.GetTopLevel(v) is not null ? s.PreviousFocus : fallbackFocus();
        Dispatcher.UIThread.Post(() => restore?.Focus(), DispatcherPriority.Input);
    }

    private static Border Card(string title, Control body, Control? buttons, double maxWidth = 560)
    {
        var titleLine = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                new Border { Classes = { "dialogAccent" }, Width = 4, Height = 20 },
                new TextBlock { Text = title, Classes = { "dialogTitle" } },
            },
        };
        var stack = new StackPanel();
        stack.Children.Add(new Border { Classes = { "dialogHeader" }, Child = titleLine });
        stack.Children.Add(new Border { Classes = { "dialogBody" }, Child = body });
        if (buttons is not null)
            stack.Children.Add(new Border { Classes = { "dialogFooter" }, Child = buttons });
        return new Border { Classes = { "card" }, Child = stack, MaxWidth = maxWidth, MinWidth = Math.Min(360, maxWidth), ClipToBounds = true };
    }

    private static StackPanel ButtonRow(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var b in buttons) row.Children.Add(b);
        return row;
    }

    public Task<PromptResult?> PromptAsync(PromptOptions o)
    {
        var tcs = new TaskCompletionSource<PromptResult?>();
        var box = new TextBox { Text = o.Text, MinWidth = 420, AcceptsReturn = false };
        Avalonia.Automation.AutomationProperties.SetName(box, string.IsNullOrEmpty(o.Message) ? o.Title : o.Message);
        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var check = o.CheckboxText is null ? null : new CheckBox { Content = o.CheckboxText, IsChecked = o.CheckboxValue };
        var ok = new Button { Content = o.ConfirmText, Classes = { "primary" }, IsDefault = true };
        var cancel = new Button { Content = "Cancel" };
        var body = new StackPanel { Spacing = 6 };
        if (!string.IsNullOrEmpty(o.Message)) body.Children.Add(new TextBlock { Text = o.Message, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(box);
        body.Children.Add(error);
        if (check is not null) body.Children.Add(check);
        var hints = new List<string>();
        if (o.Hint is not null) hints.Add(o.Hint);
        if (o.History is { Count: > 0 }) hints.Add("↑↓ history");
        if (hints.Count > 0) body.Children.Add(new TextBlock { Text = string.Join(" · ", hints), Classes = { "muted", "small" } });
        Session? session = null;
        bool held = false; // the box has held text: emptying it is worth a word
        void Validate()
        {
            var text = box.Text ?? string.Empty;
            var msg = o.Validate?.Invoke(text);
            // An empty box nobody has typed in yet is the first step, not a mistake: the button waits without a scolding.
            held |= text.Length > 0;
            bool quiet = !held;
            error.Text = msg ?? string.Empty;
            error.IsVisible = msg is not null && !quiet;
            ok.IsEnabled = msg is null;
        }
        void Finish(PromptResult? r)
        {
            if (tcs.Task.IsCompleted) return;
            Close(session!);
            tcs.TrySetResult(r);
        }
        box.TextChanged += (_, _) => Validate();
        int historyIndex = -1;
        box.KeyDown += (_, e) =>
        {
            if (o.History is not { Count: > 0 } h) return;
            if (e.Key == Key.Up && historyIndex < h.Count - 1) historyIndex++;
            else if (e.Key == Key.Down && historyIndex > 0) historyIndex--;
            else return;
            box.Text = h[historyIndex];
            box.CaretIndex = box.Text.Length;
            e.Handled = true;
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && ok.IsEnabled)
            {
                e.Handled = true;
                Finish(new PromptResult(box.Text ?? string.Empty, check?.IsChecked == true));
            }
        };
        ok.Click += (_, _) => Finish(new PromptResult(box.Text ?? string.Empty, check?.IsChecked == true));
        cancel.Click += (_, _) => Finish(null);
        Validate();
        session = Show(Card(o.Title, body, ButtonRow(cancel, ok)), box, top: true, () => Finish(null));
        Dispatcher.UIThread.Post(() =>
        {
            var t = box.Text ?? string.Empty;
            if (o.SelectStem)
            {
                int dot = t.LastIndexOf('.');
                box.SelectionStart = 0;
                box.SelectionEnd = dot > 0 ? dot : t.Length;
            }
            else
            {
                box.SelectAll();
            }
        }, DispatcherPriority.Input);
        return tcs.Task;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false, string cancelText = "Cancel")
    {
        message = OneLineBreak(message);
        var tcs = new TaskCompletionSource<bool>();
        var ok = new Button { Content = confirmText, IsDefault = true, Classes = { danger ? "danger" : "primary" } };
        var cancel = new Button { Content = cancelText, IsCancel = true };
        var text = MessageBody(message);
        Session? session = null;
        void Finish(bool r)
        {
            if (tcs.Task.IsCompleted) return;
            Close(session!);
            tcs.TrySetResult(r);
        }
        ok.Click += (_, _) => Finish(true);
        cancel.Click += (_, _) => Finish(false);
        var card = Card(title, text, ButtonRow(cancel, ok), 680);
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.Source is not Button)
            {
                e.Handled = true;
                Finish(true);
            }
        };
        session = Show(card, ok, top: false, () => Finish(false));
        return tcs.Task;
    }

    /// <summary>
    /// One kind of line break in dialog text: Avalonia's text layout (notably headless) mishandles carriage returns
    /// in wrapped text, so Environment.NewLine from callers becomes a plain line feed.
    /// </summary>
    private static string OneLineBreak(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    /// <summary>
    /// A message as selectable text, a block per line, with space where a blank line separated paragraphs: a line
    /// break inside one wrapped text makes Avalonia's text layout (headless, at least) add empty lines without end.
    /// </summary>
    private static Control MessageBody(string message)
    {
        var lines = OneLineBreak(message).Split('\n');
        if (lines.Length == 1) return new SelectableTextBlock { HorizontalAlignment = HorizontalAlignment.Left, Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 620 };
        var body = new StackPanel { MaxWidth = 620 };
        bool paragraph = false;
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                paragraph = body.Children.Count > 0;
                continue;
            }
            body.Children.Add(new SelectableTextBlock
            {
                Text = line,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, paragraph ? 10 : 0, 0, 0),
            });
            paragraph = false;
        }
        return body;
    }

    public Task AlertAsync(string title, string message)
    {
        message = OneLineBreak(message);
        var tcs = new TaskCompletionSource();
        var ok = new Button { Content = "OK", IsDefault = true, Classes = { "primary" } };
        Session? session = null;
        void Finish()
        {
            if (tcs.Task.IsCompleted) return;
            Close(session!);
            tcs.TrySetResult();
        }
        ok.Click += (_, _) => Finish();
        session = Show(Card(title, MessageBody(message), ButtonRow(ok), 680), ok, top: false, Finish);
        return tcs.Task;
    }

    public Task<object?> ShowCustomAsync(string title, Control content, IReadOnlyList<DialogButton> buttons, Control? initialFocus = null,
        Func<bool>? canConfirm = null, DialogCloser? closer = null)
    {
        var tcs = new TaskCompletionSource<object?>();
        Session? session = null;
        DispatcherTimer? requery = null;
        void Finish(object? r)
        {
            if (tcs.Task.IsCompleted) return;
            requery?.Stop();
            Close(session!);
            tcs.TrySetResult(r);
        }
        var gated = new List<(Button Button, Func<bool> Available)>();
        DialogButton? confirm = null;
        Func<bool>? confirmAvailable = null;
        var btns = buttons.Select(b =>
        {
            var btn = new Button { Content = b.Text, IsDefault = b.IsDefault, IsCancel = b.IsCancel };
            if (b.IsDanger) btn.Classes.Add("danger");
            else if (b.IsDefault) btn.Classes.Add("primary");
            var available = b.IsDefault && canConfirm is not null
                ? b.IsAvailable is { } own ? () => own() && canConfirm() : canConfirm
                : b.IsAvailable;
            if (available is not null) gated.Add((btn, available));
            if (b.IsDefault && confirm is null) (confirm, confirmAvailable) = (b, available);
            btn.Click += (_, _) => { if (available?.Invoke() != false) Finish(b.Result); };
            return btn;
        }).ToArray();
        // Buttons show whether they would do anything (the predicates are cheap and side-effect free).
        if (gated.Count > 0)
        {
            void Requery()
            {
                foreach (var (button, available) in gated) button.IsEnabled = available();
            }
            Requery();
            requery = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            requery.Tick += (_, _) => Requery();
            requery.Start();
        }
        var card = Card(title, content, ButtonRow(btns), 760);
        // Enter confirms as soon as the dialog would: whether the default button may act is asked when Enter is pressed,
        // not only when the 200 ms re-check last enabled it, so Enter right after typing is not lost. A button or a
        // multi-line box that used Enter keeps it; a focused list item takes Enter for itself, and in a dialog Enter
        // there still confirms (unless the content acted on it already and so ended the dialog).
        card.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || tcs.Task.IsCompleted || e.Source is not Visual source) return;
            bool inListItem = source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item && card.IsVisualAncestorOf(item);
            if (e.Handled && !inListItem || confirm is null || confirmAvailable?.Invoke() == false) return;
            e.Handled = true;
            Finish(confirm.Result);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        // Enter on a check box or an option confirms, as in Windows dialogs (Space toggles it): Avalonia would click it.
        card.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || tcs.Task.IsCompleted || e.Source is not (CheckBox or RadioButton)) return;
            e.Handled = true;
            if (confirm is not null && confirmAvailable?.Invoke() != false) Finish(confirm.Result);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var cancel = buttons.FirstOrDefault(b => b.IsCancel);
        session = Show(card, initialFocus ?? btns.FirstOrDefault(b => b.IsDefault), top: false, () => Finish(cancel?.Result));
        closer?.Attach(Finish);
        return tcs.Task;
    }

    public Task<KeyboardReferenceChoice?> KeyboardReferenceAsync(IReadOnlyList<KeyboardHelpEntry> commands, string? selectedId = null)
    {
        var tcs = new TaskCompletionSource<KeyboardReferenceChoice?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var search = new TextBox { PlaceholderText = "Search command, shortcut, or command ID", MinWidth = 220 };
        Avalonia.Automation.AutomationProperties.SetName(search, "Search keyboard reference");
        var categories = new[] { "All categories" }.Concat(commands.Select(c => c.Category).Distinct().Order()).ToArray();
        var category = new ComboBox { ItemsSource = categories, SelectedIndex = 0, MinWidth = 130, Margin = new Thickness(8, 0, 0, 0) };
        Avalonia.Automation.AutomationProperties.SetName(category, "Command category");
        var list = new ListBox
        {
            MinHeight = 180,
            MaxHeight = 390,
            MinWidth = 400,
            Classes = { "choices" },
            ItemTemplate = new FuncDataTemplate<KeyboardHelpEntry>((item, _) =>
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
                var left = new StackPanel();
                left.Children.Add(new TextBlock { Text = item?.Title, TextTrimming = TextTrimming.CharacterEllipsis });
                left.Children.Add(new TextBlock { Text = item?.Category + " · " + item?.Id + (item?.Enabled == false ? " · unavailable" : string.Empty), Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(left);
                var keys = new TextBlock { Text = string.IsNullOrEmpty(item?.Gestures) ? "Unbound" : item.Gestures, Classes = { "muted" }, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(keys, 1);
                row.Children.Add(keys);
                return row;
            }),
        };
        Avalonia.Automation.AutomationProperties.SetName(list, "Commands and current shortcuts");
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, MinHeight = 24 };
        var count = new TextBlock { Classes = { "muted", "small" } };
        var run = new Button { Content = "Run command", Classes = { "primary" }, IsDefault = true };
        var change = new Button { Content = "Change shortcut…" };
        Avalonia.Controls.ToolTip.SetTip(change, "Press a new shortcut for the selected command (F2)");
        var close = new Button { Content = "Close", IsCancel = true };
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        filters.Children.Add(search);
        Grid.SetColumn(category, 1);
        filters.Children.Add(category);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = "Move: arrows, Page Up/Down, Home/End · Mark: Space or Insert · Shift+movement marks a range · Type to quick-search in a panel · Ctrl+0–9 opens a bookmark; Ctrl+Shift+0–9 sets one · Esc leaves search or stops loading.",
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(filters);
        body.Children.Add(count);
        body.Children.Add(list);
        body.Children.Add(detail);
        body.Children.Add(new TextBlock { Text = "Shortcuts reflect your current settings. Select a command and press Enter to run it; F2 changes its shortcut; Esc closes help.", Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap });
        Session? session = null;
        void Finish(KeyboardReferenceChoice? choice)
        {
            if (tcs.Task.IsCompleted) return;
            Close(session!);
            tcs.TrySetResult(choice);
        }
        void Accept()
        {
            if (list.SelectedItem is KeyboardHelpEntry { Enabled: true } selected) Finish(new KeyboardReferenceChoice(selected.Id, ChangeShortcut: false));
        }
        void ChangeShortcut()
        {
            if (list.SelectedItem is KeyboardHelpEntry selected) Finish(new KeyboardReferenceChoice(selected.Id, ChangeShortcut: true));
        }
        void Apply()
        {
            var keepId = (list.SelectedItem as KeyboardHelpEntry)?.Id ?? selectedId;
            var terms = (search.Text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var group = category.SelectedItem as string;
            var filtered = commands.Where(c =>
                (group == "All categories" || c.Category == group) &&
                terms.All(t => (c.Title + " " + c.Category + " " + c.Gestures + " " + c.Id + " " + c.Description).Contains(t, StringComparison.CurrentCultureIgnoreCase)))
                .OrderBy(c => c.Category).ThenBy(c => c.Title).ToArray();
            list.ItemsSource = filtered;
            list.SelectedItem = filtered.FirstOrDefault(c => c.Id == keepId) ?? filtered.FirstOrDefault();
            if (list.SelectedIndex >= 0) list.ScrollIntoView(list.SelectedIndex);
            count.Text = $"{filtered.Length} of {commands.Count} commands";
            run.IsEnabled = list.SelectedItem is KeyboardHelpEntry { Enabled: true };
        }
        search.TextChanged += (_, _) => Apply();
        category.SelectionChanged += (_, _) => Apply();
        list.SelectionChanged += (_, _) =>
        {
            var item = list.SelectedItem as KeyboardHelpEntry;
            detail.Text = item is null ? "No matching command." :
                !item.Enabled ? "Unavailable: " + (item.UnavailableReason ?? "not available here") :
                string.IsNullOrWhiteSpace(item.Description) ? item.Title : item.Description;
            run.IsEnabled = item?.Enabled == true;
            change.IsEnabled = item is not null;
        };
        search.KeyDown += (_, e) =>
        {
            int n = (list.ItemsSource as KeyboardHelpEntry[])?.Length ?? 0;
            if (e.Key == Key.Enter) { e.Handled = true; Accept(); return; }
            if (e.Key == Key.F2) { e.Handled = true; ChangeShortcut(); return; }
            if (n == 0) return;
            int next = e.Key switch
            {
                Key.Down => Math.Min(n - 1, list.SelectedIndex + 1),
                Key.Up => Math.Max(0, list.SelectedIndex - 1),
                Key.PageDown => Math.Min(n - 1, list.SelectedIndex + 10),
                Key.PageUp => Math.Max(0, list.SelectedIndex - 10),
                _ => -1,
            };
            if (next < 0) return;
            list.SelectedIndex = next;
            list.ScrollIntoView(next);
            e.Handled = true;
        };
        Controls.ListKeys.OnKey(list, Key.Enter, _ =>
        {
            Accept();
            return true;
        });
        Controls.ListKeys.OnKey(list, Key.F2, _ =>
        {
            ChangeShortcut();
            return true;
        });
        list.DoubleTapped += (_, _) => Accept();
        run.Click += (_, _) => Accept();
        change.Click += (_, _) => ChangeShortcut();
        close.Click += (_, _) => Finish(null);
        Apply();
        session = Show(Card("Keyboard reference", body, ButtonRow(close, change, run), 840), search, top: false, () => Finish(null));
        return tcs.Task;
    }

    public Task<ChoiceResult> ChooseAsync(ChoiceOptions o)
    {
        var tcs = new TaskCompletionSource<ChoiceResult>();
        var deleted = new List<int>();
        var pinToggled = new HashSet<int>();
        var all = o.Items.Select((item, index) => new ChoiceRow(item, index)).ToList();
        var filter = new TextBox { PlaceholderText = "Type to filter…", MinWidth = 520 };
        Avalonia.Automation.AutomationProperties.SetName(filter, "Filter " + o.Title);
        // Icon updates borrow realized controls; filtering must not keep retired rows and their bitmaps alive.
        var icons = new System.Runtime.CompilerServices.ConditionalWeakTable<Image, ChoiceRow>();
        var list = new ListBox
        {
            Classes = { "choices" },
            MaxHeight = 420,
            MinHeight = 60,
            ItemTemplate = new FuncDataTemplate<ChoiceRow>((row, _) =>
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                if (row?.Item.Icon is { } icon)
                {
                    var image = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Source = icon() };
                    icons.Add(image, row);
                    g.Children.Add(image);
                }
                var left = new StackPanel { Orientation = Orientation.Vertical };
                Grid.SetColumn(left, 1);
                left.Children.Add(new TextBlock
                {
                    Text = (row?.Pinned == true ? "📌 " : string.Empty) + row?.Item.Title,
                    FontWeight = row?.Item.IsHeader == true ? FontWeight.Bold : FontWeight.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                if (!string.IsNullOrEmpty(row?.Item.Detail))
                    left.Children.Add(new TextBlock { Text = row.Item.Detail, Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });
                g.Children.Add(left);
                if (!string.IsNullOrEmpty(row?.Item.Gesture))
                {
                    var gesture = new TextBlock { Text = row.Item.Gesture, Classes = { "muted", "small" }, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    Grid.SetColumn(gesture, 2);
                    g.Children.Add(gesture);
                }
                return g;
            }),
        };
        Avalonia.Automation.AutomationProperties.SetName(list, o.Title);
        var hint = new TextBlock
        {
            Text = o.Hint ?? "Type to filter · Enter chooses · Esc closes" + (o.AllowDelete ? " · Ctrl+Del removes" : string.Empty) + (o.AllowPin ? " · Insert pins" : string.Empty),
            Classes = { "muted", "small" },
            TextWrapping = TextWrapping.Wrap,
        };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(filter);
        body.Children.Add(list);
        body.Children.Add(hint);
        Session? session = null;
        List<ChoiceRow> current = all;

        void Apply()
        {
            var q = (filter.Text ?? string.Empty).Trim();
            if (o.Search is { } search) current = search(q).Select(i => all[i]).Where(r => !r.Deleted).ToList();
            else if (q.Length == 0) current = all.Where(r => !r.Deleted).ToList();
            else
            {
                var terms = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                current = all.Where(r => !r.Deleted && terms.All(t => r.Haystack.Contains(t, StringComparison.CurrentCultureIgnoreCase))).ToList();
                if (current.Count == 0) current = all.Where(r => !r.Deleted && IsSubsequence(q, r.Item.Title)).ToList();
            }
            list.ItemsSource = current;
            if (current.Count > 0)
            {
                int pre = q.Length == 0 ? current.FindIndex(r => r.Index == o.SelectedIndex) : 0;
                list.SelectedIndex = Math.Max(0, pre);
                list.ScrollIntoView(list.SelectedIndex);
            }
        }
        Action? iconsLoaded = null;
        if (o.Icons?.Native is { } native)
        {
            iconsLoaded = () =>
            {
                foreach (var (image, row) in icons) image.Source = row.Item.Icon?.Invoke();
            };
            native.IconsLoaded += iconsLoaded;
        }
        void Finish(ChoiceResult r)
        {
            if (tcs.Task.IsCompleted) return;
            if (iconsLoaded is not null) o.Icons!.Native!.IconsLoaded -= iconsLoaded;
            Close(session!);
            tcs.TrySetResult(r);
        }
        void Accept(bool alternate)
        {
            if (list.SelectedItem is ChoiceRow row && !row.Item.IsHeader) Finish(new ChoiceResult(row.Index, alternate, deleted) { PinToggled = [.. pinToggled] });
        }
        filter.TextChanged += (_, _) => Apply();
        // An accelerator typed into the empty filter chooses its item at once (a drive letter in the location menu).
        if (o.Accelerators is { Count: > 0 } accelerators)
            filter.AddHandler(InputElement.TextInputEvent, (_, e) =>
            {
                if (string.IsNullOrEmpty(filter.Text) && e.Text is { Length: 1 } typed && accelerators.TryGetValue(char.ToUpperInvariant(typed[0]), out int index))
                {
                    e.Handled = true;
                    Finish(new ChoiceResult(index, false, deleted) { PinToggled = [.. pinToggled] });
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Tunneling: the text box would otherwise take Ctrl+Delete (delete word) before the chooser sees it.
        filter.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            int count = current.Count;
            if (count == 0 && e.Key != Key.Escape) return;
            switch (e.Key)
            {
                case Key.Down: list.SelectedIndex = Math.Min(count - 1, list.SelectedIndex + 1); break;
                case Key.Up: list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); break;
                case Key.PageDown: list.SelectedIndex = Math.Min(count - 1, list.SelectedIndex + 10); break;
                case Key.PageUp: list.SelectedIndex = Math.Max(0, list.SelectedIndex - 10); break;
                case Key.Enter: Accept((e.KeyModifiers & KeyModifiers.Shift) != 0); break;
                case Key.Insert when o.AllowPin:
                    if (list.SelectedItem is ChoiceRow pin && !pin.Item.IsHeader)
                    {
                        pin.Pinned = !pin.Pinned;
                        if (!pinToggled.Add(pin.Index)) pinToggled.Remove(pin.Index);
                        int keep = list.SelectedIndex;
                        Apply();
                        list.SelectedIndex = Math.Min(keep, current.Count - 1);
                    }
                    break;
                case Key.Delete when o.AllowDelete && (e.KeyModifiers & KeyModifiers.Control) != 0:
                    if (list.SelectedItem is ChoiceRow del)
                    {
                        del.Deleted = true;
                        deleted.Add(del.Index);
                        int keep = list.SelectedIndex;
                        Apply();
                        list.SelectedIndex = Math.Min(keep, current.Count - 1);
                    }
                    break;
                default: return;
            }
            if (list.SelectedIndex >= 0) list.ScrollIntoView(list.SelectedIndex);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        list.DoubleTapped += (_, _) => Accept(false);
        Controls.ListKeys.OnKey(list, Key.Enter, e =>
        {
            Accept((e.KeyModifiers & KeyModifiers.Shift) != 0);
            return true;
        });
        Apply();
        session = Show(Card(o.Title, body, null, 720), filter, top: true, () => Finish(new ChoiceResult(-1, false, deleted) { PinToggled = [.. pinToggled] }));
        return tcs.Task;
    }

    private static bool IsSubsequence(string query, string text)
    {
        int i = 0;
        foreach (var c in text)
        {
            if (i < query.Length && char.ToUpperInvariant(c) == char.ToUpperInvariant(query[i])) i++;
        }
        return i == query.Length;
    }

    private sealed class ChoiceRow(ChoiceItem item, int index)
    {
        public ChoiceItem Item { get; } = item;
        public int Index { get; } = index;
        public bool Deleted { get; set; }
        public bool Pinned { get; set; } = item.Pinned;
        public string Haystack { get; } = item.Title + " " + item.Detail + " " + item.Gesture;
    }
}
