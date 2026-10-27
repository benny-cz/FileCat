using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;

namespace FileCat.App.Views;

/// <summary>
/// In-window modal dialogs on a dimmed backdrop. Keyboard-first: Enter confirms, Esc cancels, Tab cycles
/// inside the dialog, and focus returns to where it was when the dialog closes (plan §4.3).
/// </summary>
public sealed class OverlayDialogService(Panel host, Func<IInputElement?> fallbackFocus,
    Action<FileListControl>? announceRestoredRow = null) : IDialogService
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
        public required Control InitialFocus;
        public IInputElement? PreviousFocus;
        public required Action OnEscape;
    }

    // Open dialogs, the topmost last, and the window whose Esc reaches them wherever the keyboard is.
    private readonly List<Session> _sessions = [];
    private long _focusRevision;
    private TopLevel? _escapeRoot;
    // An event's Source can retain the closed dialog; the guard is needed only while that event is routed.
    private WeakReference<KeyEventArgs>? _escapeClosing;

    internal static bool IsInCardOrPopup(Visual source, Visual card)
    {
        if (ReferenceEquals(source, card) || card.IsVisualAncestorOf(source)) return true;
        // ComboBox and completion menus can render in a separate PopupRoot.
        // Their content still belongs to the active modal even though it is
        // no longer a visual descendant of the card itself.
        return card.GetVisualDescendants().OfType<Popup>().Any(popup =>
            popup.IsOpen && popup.Child is Visual child &&
            (ReferenceEquals(source, child) || child.IsVisualAncestorOf(source)));
    }

    private static bool IsInCardOrPopup(Visual source, Session session) =>
        session.Layer.Child is Visual card && IsInCardOrPopup(source, card);

    /// <summary>
    /// Esc closes the topmost dialog even when the keyboard is outside it (a click elsewhere, focus that never
    /// arrived): the dialog's own handler takes Esc pressed inside it, including what its controls close first.
    /// </summary>
    private void OnRootKeyDown(object? sender, KeyEventArgs e)
    {
        // A dialog that closed itself on this Esc: the one below it stays.
        if (_escapeClosing is { } remembered && remembered.TryGetTarget(out var closing) && ReferenceEquals(e, closing)) return;
        if (e.Handled || e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None || _sessions.Count == 0) return;
        var top = _sessions[^1];
        if (e.Source is Visual source && (ReferenceEquals(source, top.Layer) ||
            IsInCardOrPopup(source, top))) return;
        e.Handled = true;
        top.OnEscape();
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_sessions.Count == 0 || e.Source is not Visual source) return;
        var top = _sessions[^1];
        // At minimum window sizes a native hit can land on a footer or other
        // window chrome outside the overlay's arranged hit surface. Treat it
        // like the backdrop before the obscured control receives its press.
        if (IsInCardOrPopup(source, top)) return;
        e.Handled = true;
        top.OnEscape();
    }

    private void OnRootGotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TopLevel root || _sessions.Count == 0 || e.Source is not Visual source) return;
        var currentSession = _sessions[^1];
        if (currentSession.Layer.IsVisualAncestorOf(source) || IsInCardOrPopup(source, currentSession)) return;
        long focusRevision = _focusRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (focusRevision != _focusRevision || !IsCurrent(currentSession)) return;
            var focused = root.FocusManager?.GetFocusedElement();
            if (focused is Visual visual && (currentSession.Layer.IsVisualAncestorOf(visual) ||
                IsInCardOrPopup(visual, currentSession))) return;
            var target = CanFocus(currentSession.InitialFocus) ? currentSession.InitialFocus : currentSession.Layer.Child;
            if (CanFocus(target)) Controls.ListKeys.Focus(target!);
        }, DispatcherPriority.Input);
    }

    private Session Show(Control card, Control? initialFocus, bool top, Action onEscape)
    {
        var focusManager = TopLevel.GetTopLevel(host)?.FocusManager;
        var session = new Session
        {
            PreviousFocus = focusManager?.GetFocusedElement(),
            InitialFocus = initialFocus ?? card,
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
            root.AddHandler(InputElement.PointerPressedEvent, OnRootPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            root.AddHandler(InputElement.GotFocusEvent, OnRootGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
        }
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.Focusable = true;
        card.VerticalAlignment = top ? VerticalAlignment.Top : VerticalAlignment.Center;
        card.Margin = top ? new Thickness(24, 56, 24, 24) : new Thickness(24);
        KeyboardNavigation.SetTabNavigation(card, KeyboardNavigationMode.Cycle);
        session.Layer.AddHandler(InputElement.GotFocusEvent, (_, e) =>
        {
            if (!IsCurrent(session) || e.Source is not Control target || !session.Layer.IsVisualAncestorOf(target)) return;
            // TextBox owns a precise caret request, including its internal scroll offset. Revealing the
            // whole editor afterward can hide that caret when the editor is taller than the modal body.
            if ((target as TextBox ?? target.FindAncestorOfType<TextBox>()) is { } editor)
            {
                RevealEditorFocus(session, target, editor);
                return;
            }
            long revision = _focusRevision;
            Dispatcher.UIThread.Post(() =>
            {
                if (revision == _focusRevision && IsCurrent(session) && CanFocus(target) &&
                    session.Layer.IsVisualAncestorOf(target) && ReferenceEquals(TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement(), target))
                    target.BringIntoView();
            }, DispatcherPriority.Loaded);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        bool layoutRevealQueued = false;
        session.Layer.LayoutUpdated += (_, _) =>
        {
            if (layoutRevealQueued || !IsCurrent(session)) return;
            var focused = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement() as TextBox;
            if (focused is null || !session.Layer.IsVisualAncestorOf(focused)) return;
            layoutRevealQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                layoutRevealQueued = false;
                if (IsCurrent(session) && ReferenceEquals(TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement(), focused))
                    RevealShortEditorInDialog(focused);
            }, DispatcherPriority.Background);
        };
        session.Layer.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && IsCurrent(session))
            {
                e.Handled = true;
                _escapeClosing = new(e);
                onEscape();
            }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        session.Layer.PointerPressed += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, session.Layer) || !IsCurrent(session)) return;
            e.Handled = true;
            onEscape();
        };
        if (card is DialogCard dialogCard)
            dialogCard.CloseRequested += () =>
            {
                if (IsCurrent(session)) onEscape();
            };
        if (_sessions.Count > 0) _sessions[^1].Layer.IsEnabled = false;
        host.Children.Add(session.Layer);
        host.IsVisible = true;
        _open++;
        _sessions.Add(session);
        // A headless or partially themed host can have no backdrop brush. Keep the
        // modal layer in the pointer hit tree after styles have had a layout pass.
        Dispatcher.UIThread.Post(() =>
        {
            if (_sessions.Contains(session) && session.Layer.Background is null)
                session.Layer.Background = Brushes.Transparent;
            if (_sessions.Contains(session) && card is Border border && border.Background is null)
                border.Background = Brushes.Transparent;
        }, DispatcherPriority.Loaded);
        if (card is DialogCard announced)
            Dispatcher.UIThread.Post(() =>
            {
                if (IsCurrent(session)) announced.AnnounceOpened();
            }, DispatcherPriority.Background);
        long revision = ++_focusRevision;
        // A list takes focus through its selected item, so its arrow keys work at once.
        Dispatcher.UIThread.Post(() =>
        {
            if (revision != _focusRevision || !IsCurrent(session)) return;
            var current = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
            // Preserve a choice inside the dialog; focus left behind on the invoking
            // command field or panel must not continue to own a visible modal.
            if (current is Visual visual && session.Layer.IsVisualAncestorOf(visual)) return;
            var target = CanFocus(session.InitialFocus) ? session.InitialFocus : card;
            if (CanFocus(target)) Controls.ListKeys.Focus(target);
        }, DispatcherPriority.Input);
        return session;
    }

    private void RevealEditorFocus(Session session, Control focusTarget, TextBox editor)
    {
        long revision = _focusRevision;
        bool OwnsFocus() => revision == _focusRevision && IsCurrent(session) && CanFocus(editor) &&
            session.Layer.IsVisualAncestorOf(editor) && ReferenceEquals(TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement(), focusTarget);
        // Refocusing an unchanged caret does not necessarily cause a native caret-change notification.
        // Reveal it inside the editor first, then reveal that precise rectangle in the enclosing modal.
        // Loaded runs after pending layout; neither callback forces synchronous layout or steals focus.
        Dispatcher.UIThread.Post(() =>
        {
            if (!OwnsFocus() || !editor.IsArrangeValid) return;
            if (editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault() is not { } presenter) return;
            presenter.BringIntoView(presenter.TextLayout.HitTestTextPosition(editor.CaretIndex));
            Dispatcher.UIThread.Post(() =>
            {
                if (!OwnsFocus() || !editor.IsArrangeValid || !presenter.IsArrangeValid) return;
                var caret = presenter.TextLayout.HitTestTextPosition(editor.CaretIndex);
                if (presenter.TranslatePoint(caret.TopLeft, editor) is not { } position) return;
                editor.BringIntoView(new Rect(position, new Size(Math.Max(1, caret.Width), Math.Max(1, caret.Height))));
                RevealShortEditorInDialog(editor);
            }, DispatcherPriority.Loaded);
        }, DispatcherPriority.Loaded);
    }

    private static void RevealShortEditorInDialog(TextBox editor)
    {
        // A native text presenter may consume the caret request inside its own
        // viewport. Later validation text can also move the editor after focus.
        var body = editor.GetVisualAncestors().OfType<ScrollViewer>()
            .FirstOrDefault(view => view.Name == "DialogBodyScroll");
        if (body is null || editor.Bounds.Height > body.Viewport.Height) return;
        var viewport = body.GetVisualDescendants().OfType<ScrollContentPresenter>().FirstOrDefault();
        if (viewport is null || editor.TranslatePoint(default, viewport) is not { } field) return;
        double delta = field.Y < 0 ? field.Y :
            Math.Max(0, field.Y + editor.Bounds.Height - viewport.Bounds.Height);
        if (Math.Abs(delta) > 0.5)
            body.Offset = new Vector(body.Offset.X, Math.Clamp(body.Offset.Y + delta,
                0, Math.Max(0, body.Extent.Height - body.Viewport.Height)));
    }

    private bool IsCurrent(Session? session) => session is not null && _sessions.Count > 0 && ReferenceEquals(_sessions[^1], session);

    private static bool CanFocus(IInputElement? target) => target is Control control && control.IsEffectivelyVisible &&
        control.IsEffectivelyEnabled && TopLevel.GetTopLevel(control) is not null;

    private void Close(Session s)
    {
        bool wasCurrent = IsCurrent(s);
        if (!_sessions.Remove(s)) return;
        host.Children.Remove(s.Layer);
        _open = Math.Max(0, _open - 1);
        host.IsVisible = host.Children.Count > 0;
        long revision = ++_focusRevision;
        var remaining = _sessions.LastOrDefault();
        if (remaining is not null) remaining.Layer.IsEnabled = true;
        if (!wasCurrent) return;
        var focusAtClose = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
        Dispatcher.UIThread.Post(() =>
        {
            if (revision != _focusRevision || !ReferenceEquals(_sessions.LastOrDefault(), remaining)) return;
            var current = TopLevel.GetTopLevel(host)?.FocusManager?.GetFocusedElement();
            if (current is not null && !ReferenceEquals(current, focusAtClose)) return;
            IInputElement? restore = s.PreviousFocus;
            if (remaining is not null)
            {
                if (restore is not Visual visual || !remaining.Layer.IsVisualAncestorOf(visual) || !CanFocus(restore))
                    restore = remaining.InitialFocus;
            }
            else if (!CanFocus(restore)) restore = fallbackFocus();
            if (CanFocus(restore))
            {
                if (restore is Control control) Controls.ListKeys.Focus(control);
                else restore?.Focus();
                if (announceRestoredRow is not null &&
                    remaining is null && restore is FileListControl list &&
                    TopLevel.GetTopLevel(host) is Window window)
                {
                    // Speak the current row after the modal's AX subtree retires.
                    // A second focus event for the same VoiceOver cursor does not
                    // speak the filename, even though the AX focus is correct.
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (revision == _focusRevision && !IsOpen && list.IsFocused &&
                            ReferenceEquals(window.FocusManager?.GetFocusedElement(), list))
                            announceRestoredRow(list);
                    }, DispatcherPriority.Background);
                }
            }
        }, DispatcherPriority.Input);
    }

    private static DialogCard Card(string title, Control body, Control? buttons, double maxWidth = 560,
        Control? pinnedReview = null) => new DialogCard(title, body, buttons, maxWidth, pinnedReview);

    private static WrapPanel ButtonRow(params Button[] buttons)
    {
        var row = DialogLayout.Actions(buttons);
        row.HorizontalAlignment = HorizontalAlignment.Right;
        foreach (var button in buttons) button.ContentTemplate ??= DialogLayout.WrappingText;
        return row;
    }

    public Task<PromptResult?> PromptAsync(PromptOptions o)
    {
        var tcs = new TaskCompletionSource<PromptResult?>();
        var box = new TextBox { Text = o.Text, AcceptsReturn = false };
        Avalonia.Automation.AutomationProperties.SetName(box, string.IsNullOrEmpty(o.Message) ? o.Title : o.Message);
        var error = new TextBlock { Classes = { "error" }, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        // Spoken as it appears, and part of the field's description while shown, as the panel filter's message is.
        Avalonia.Automation.AutomationProperties.SetLiveSetting(error, Avalonia.Automation.AutomationLiveSetting.Assertive);
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
            Avalonia.Automation.AutomationProperties.SetHelpText(box, error.IsVisible ? msg : null);
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
            if (e.Key != Key.Enter || !IsCurrent(session)) return;
            e.Handled = true;
            if (ok.IsEnabled)
            {
                Finish(new PromptResult(box.Text ?? string.Empty, check?.IsChecked == true));
                return;
            }
            // Enter on a rejected value keeps the dialog and says why, also before anything was typed.
            bool shown = error.IsVisible;
            held = true;
            Validate();
            if (shown) Controls.LiveRegions.Repeat(error);
        };
        ok.Click += (_, _) => Finish(new PromptResult(box.Text ?? string.Empty, check?.IsChecked == true));
        cancel.Click += (_, _) => Finish(null);
        Validate();
        session = Show(Card(o.Title, body, ButtonRow(cancel, ok)), box, top: true, () => Finish(null));
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrent(session) || !box.IsFocused) return;
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

    public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool danger = false, string cancelText = "Cancel") =>
        ConfirmCoreAsync(title, message, confirmText, danger, cancelText, CancellationToken.None);

    public Task<bool> ConfirmReviewAsync(string title, string message, IReadOnlyList<string> keyPoints,
        string confirmText = "OK", bool danger = false, string cancelText = "Cancel") =>
        ConfirmCoreAsync(title, message, confirmText, danger, cancelText, CancellationToken.None, keyPoints,
            "Key points", "Full details");

    /// <summary>Closes only this confirmation when its owning review is stopped.</summary>
    public Task<bool> ConfirmWithCancellationAsync(string title, string message, string confirmText, bool danger,
        CancellationToken cancellationToken, IReadOnlyList<string>? highlights = null,
        string summaryHeading = "Key choices before approval", string detailsHeading = "Full review and source mapping",
        bool collapseDetails = false, IReadOnlyList<string>? warnings = null) =>
        ConfirmCoreAsync(title, message, confirmText, danger, "Cancel", cancellationToken, highlights,
            summaryHeading, detailsHeading, collapseDetails, warnings);

    private async Task<bool> ConfirmCoreAsync(string title, string message, string confirmText, bool danger,
        string cancelText, CancellationToken cancellationToken, IReadOnlyList<string>? highlights = null,
        string summaryHeading = "Key choices before approval", string detailsHeading = "Full review and source mapping",
        bool collapseDetails = false, IReadOnlyList<string>? warnings = null)
    {
        if (cancellationToken.IsCancellationRequested) return false;
        message = OneLineBreak(message);
        var tcs = new TaskCompletionSource<bool>();
        var ok = new Button { Content = confirmText, IsDefault = !danger, Classes = { danger ? "danger" : "primary" } };
        var cancel = new Button { Content = cancelText, IsCancel = true, IsDefault = danger };
        var text = highlights is { Count: > 0 }
            ? HighlightedReviewBody(highlights, message, summaryHeading, detailsHeading, collapseDetails, warnings) : MessageBody(message);
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
        if (highlights is { Count: > 0 } || message.Length > 600)
            card.EnableResizing(host);
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && e.Source is not Button && IsCurrent(session))
            {
                e.Handled = true;
                Finish(!danger);
            }
        };
        session = Show(card, danger ? cancel : ok, top: false, () => Finish(false));
        using var registration = cancellationToken.Register(() => Dispatcher.UIThread.Post(() => Finish(false), DispatcherPriority.Input));
        return await tcs.Task;
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
        if (lines.Length == 1) return ReviewLine(message);
        var body = new StackPanel();
        bool paragraph = false;
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                paragraph = body.Children.Count > 0;
                continue;
            }
            var block = ReviewLine(line);
            block.Margin = new Thickness(0, paragraph ? 10 : 0, 0, 0);
            body.Children.Add(block);
            paragraph = false;
        }
        return body;
    }

    private static Control HighlightedReviewBody(IReadOnlyList<string> highlights, string message,
        string summaryHeading, string detailsHeading, bool collapseDetails, IReadOnlyList<string>? warnings)
    {
        var summary = new StackPanel { Spacing = 8 };
        summary.Children.Add(new TextBlock
        {
            Text = summaryHeading, FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        // Keep the complete wording selectable, but give the decision and its
        // consequences a visual hierarchy instead of another paragraph of text.
        for (int index = 0; index < highlights.Count; index++)
        {
            var line = ReviewLine(highlights[index]);
            if (index == 0)
            {
                line.Classes.Add("reviewPrimaryAction");
                summary.Children.Add(new Border { Classes = { "reviewActionCard" }, Child = line });
            }
            else
            {
                line.Classes.Add(highlights[index].StartsWith("Existing items:", StringComparison.Ordinal)
                    ? "reviewDecision" : "reviewFact");
                summary.Children.Add(line);
            }
        }
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new Border { Classes = { "reviewHighlights" }, Child = summary });
        if (warnings is { Count: > 0 })
        {
            var effects = new StackPanel { Spacing = 4 };
            effects.Children.Add(new TextBlock { Text = "Important effects", FontWeight = FontWeight.SemiBold });
            foreach (var warning in warnings)
                effects.Children.Add(ReviewLine("• " + warning));
            body.Children.Add(new Border { Classes = { "reviewWarnings" }, Child = effects });
        }
        var detail = ReviewDetailBody(message);
        if (collapseDetails)
        {
            detail.IsVisible = false;
            var label = string.IsNullOrWhiteSpace(detailsHeading)
                ? "details" : char.ToLowerInvariant(detailsHeading[0]) + detailsHeading[1..];
            var toggle = new ToggleButton { Content = "Show " + label, Name = "ReviewDetailsToggle" };
            Avalonia.Automation.AutomationProperties.SetHelpText(toggle,
                "Open the complete review, including the source-to-destination mapping and safety notes.");
            toggle.IsCheckedChanged += (_, _) =>
            {
                detail.IsVisible = toggle.IsChecked == true;
                toggle.Content = (toggle.IsChecked == true ? "Hide " : "Show ") + label;
                Avalonia.Automation.AutomationProperties.SetHelpText(toggle, toggle.IsChecked == true
                    ? "Hide the complete review; key choices and important effects remain visible."
                    : "Open the complete review, including the source-to-destination mapping and safety notes.");
            };
            body.Children.Add(toggle);
        }
        else body.Children.Add(new TextBlock
        {
            Text = detailsHeading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(detail);
        return body;
    }

    private static Control ReviewDetailBody(string message)
    {
        var body = new StackPanel { Spacing = 8 };
        // The original review is still present verbatim and in order. Paragraph
        // boundaries become visible groups; their heading remains selectable.
        foreach (var paragraph in OneLineBreak(message).Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var section = new StackPanel { Spacing = 4 };
            var lines = paragraph.Trim('\n').Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Length == 0) continue;
                var line = ReviewLine(lines[index]);
                if (index == 0 && lines[index].EndsWith(':') && lines[index].Length < 180)
                    line.Classes.Add("reviewDetailHeading");
                section.Children.Add(line);
            }
            if (section.Children.Count > 0)
                body.Children.Add(new Border { Classes = { "reviewDetailSection" }, Child = section });
        }
        return body;
    }

    internal static SelectableTextBlock ReviewLine(string text)
    {
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
        // Inlines retain one selectable line and its original accessibility
        // wording. Only display spans are styled; the review text is untouched.
        var weight = new byte[text.Length];
        int keyStart = 0;
        while (keyStart < text.Length)
        {
            int colon = text.IndexOf(':', keyStart);
            if (colon < 0) break;
            int segmentStart = text.LastIndexOf(" · ", colon, colon - keyStart + 1,
                StringComparison.Ordinal);
            int labelStart = segmentStart < 0 ? keyStart : segmentStart + 3;
            var label = text.AsSpan(labelStart, colon - labelStart);
            if (label.Length is >= 2 and <= 48 && char.IsLetter(label[0]) &&
                label.IndexOfAny('\\', '/', '.') < 0 &&
                !(char.IsDigit(label[^1]) && label.Contains(' ')))
                for (int index = labelStart; index <= colon; index++) weight[index] = 1;
            keyStart = colon + 1;
            int nextPair = text.IndexOf(" · ", keyStart, StringComparison.Ordinal);
            if (nextPair < 0) break;
            keyStart = nextPair + 3;
        }

        int valueStart = text.StartsWith("Source → target name:", StringComparison.Ordinal)
            ? text.IndexOf(':') + 1 : text.StartsWith("• ", StringComparison.Ordinal) ? 2 : -1;
        foreach (var pathLabel in new[] { "Destination:", "Source:", "Source path:", "Target path:",
                     "Issue path:", "File:", "Path:", "Target:", "Scope:" })
            if (text.StartsWith(pathLabel, StringComparison.Ordinal))
            {
                EmphasizePath(pathLabel.Length, text.Length);
                break;
            }
        if (valueStart >= 0)
        {
            int arrow = text.IndexOf(" → ", valueStart, StringComparison.Ordinal);
            if (arrow >= 0)
            {
                EmphasizePath(valueStart, arrow);
                EmphasizePath(arrow + 3, text.Length);
            }
        }
        // Undo actions quote complete, absolute paths rather than using a field
        // label for each one. Keep the quote punctuation plain and emphasize
        // the whole directory as well as its final name.
        for (int cursor = 0; cursor < text.Length;)
        {
            int open = text.IndexOf('"', cursor);
            if (open < 0) break;
            int close = text.IndexOf('"', open + 1);
            if (close < 0) break;
            var path = text.AsSpan(open + 1, close - open - 1);
            if (path.StartsWith('/') || path.StartsWith("\\\\", StringComparison.Ordinal) ||
                path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/')
                EmphasizePath(open + 1, close);
            cursor = close + 1;
        }
        if (weight.All(value => value == 0))
        {
            block.Text = text;
            return block;
        }
        block.Inlines = new InlineCollection();
        for (int start = 0; start < text.Length;)
        {
            int end = start + 1;
            while (end < text.Length && weight[end] == weight[start]) end++;
            var run = new Run(text[start..end])
            {
                FontWeight = weight[start] == 2 ? FontWeight.Bold :
                    weight[start] == 1 ? FontWeight.SemiBold : FontWeight.Normal
            };
            if (weight[start] is 2 or 3)
            {
                run.FontWeight = weight[start] == 2 ? FontWeight.Bold : FontWeight.SemiBold;
                run[!Run.ForegroundProperty] = new DynamicResourceExtension("FcTextLink");
            }
            block.Inlines.Add(run);
            start = end;
        }
        return block;

        void EmphasizePath(int start, int end)
        {
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (start == end) return;
            int slash = text.LastIndexOfAny(['\\', '/'], end - 1, end - start);
            for (int index = start; index < end; index++)
                weight[index] = (byte)(slash < start || index > slash ? 2 : 3);
        }
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
        var card = Card(title, MessageBody(message), ButtonRow(ok), 680);
        if (message.Length > 600) card.EnableResizing(host);
        session = Show(card, ok, top: false, Finish);
        return tcs.Task;
    }

    public Task<object?> ShowCustomAsync(string title, Control content, IReadOnlyList<DialogButton> buttons, Control? initialFocus = null,
        Func<bool>? canConfirm = null, DialogCloser? closer = null, bool resizable = false,
        Control? pinnedReview = null)
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
            btn.Click += (_, _) => { if (IsCurrent(session) && available?.Invoke() != false && b.Validate?.Invoke() != false) Finish(b.Result); };
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
        var card = Card(title, content, ButtonRow(btns), 760, pinnedReview);
        if (resizable) card.EnableResizing(host);
        // Enter confirms as soon as the dialog would: whether the default button may act is asked when Enter is pressed,
        // not only when the 200 ms re-check last enabled it, so Enter right after typing is not lost. A button or a
        // multi-line box that used Enter keeps it; a focused list item takes Enter for itself, and in a dialog Enter
        // there still confirms (unless the content acted on it already and so ended the dialog).
        card.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || tcs.Task.IsCompleted || !IsCurrent(session) || e.Source is not Visual source) return;
            bool inListItem = source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { } item && card.IsVisualAncestorOf(item);
            if (e.Handled && !inListItem || confirm is null || confirmAvailable?.Invoke() == false) return;
            e.Handled = true;
            if (confirm.Validate?.Invoke() != false) Finish(confirm.Result);
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        // Enter on a check box or an option confirms, as in Windows dialogs (Space toggles it): Avalonia would click it.
        card.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || tcs.Task.IsCompleted || !IsCurrent(session) || e.Source is not (CheckBox or RadioButton)) return;
            e.Handled = true;
            if (confirm is not null && confirmAvailable?.Invoke() != false && confirm.Validate?.Invoke() != false) Finish(confirm.Result);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        var cancel = buttons.FirstOrDefault(b => b.IsCancel);
        session = Show(card, initialFocus ?? btns.FirstOrDefault(b => b.IsDefault), top: false, () => Finish(cancel?.Result));
        closer?.Attach(Finish);
        return tcs.Task;
    }

    public Task<KeyboardReferenceChoice?> KeyboardReferenceAsync(IReadOnlyList<KeyboardHelpEntry> commands, string? selectedId = null, bool remapOnly = false)
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
            Classes = { "choices" },
            ItemTemplate = new FuncDataTemplate<KeyboardHelpEntry>((item, _) =>
            {
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
                var left = new StackPanel();
                left.Children.Add(new TextBlock { Text = item?.Title, TextTrimming = TextTrimming.CharacterEllipsis });
                left.Children.Add(new TextBlock { Text = item?.Category +
                    (item?.ContextLabel is null ? string.Empty : " · " + item.ContextLabel) + " · " + item?.Id +
                    (item?.Enabled == false ? " · unavailable" : string.Empty), Classes = { "muted", "small" }, TextTrimming = TextTrimming.CharacterEllipsis });
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
        var run = new Button { Content = "Run command", Classes = { "primary" }, IsDefault = !remapOnly, IsVisible = !remapOnly };
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
        body.Children.Add(new TextBlock { Text = remapOnly
            ? "Shortcuts reflect this Settings draft. Select a command and press Enter or F2 to change it; Esc returns to Settings."
            : "Shortcuts reflect your current settings. Select a command and press Enter to run it; F2 changes its shortcut; Esc closes help.", Classes = { "muted", "small" }, TextWrapping = TextWrapping.Wrap });
        Session? session = null;
        void Finish(KeyboardReferenceChoice? choice)
        {
            if (tcs.Task.IsCompleted) return;
            Close(session!);
            tcs.TrySetResult(choice);
        }
        void Accept()
        {
            if (remapOnly) { ChangeShortcut(); return; }
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
                (item.ContextLabel is null ? string.Empty : "Context: " + item.ContextLabel + ". ") +
                (item.DefaultGestures is null ? string.Empty : "Default: " + item.DefaultGestures + ". ") +
                (!item.Enabled ? "Unavailable: " + (item.UnavailableReason ?? "not available here") :
                    string.IsNullOrWhiteSpace(item.Description) ? item.Title : item.Description);
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
        var filter = new TextBox { PlaceholderText = "Type to filter…" };
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
        session = Show(Card(o.Title, body, null, 720), filter, top: true,
            () => Finish(new ChoiceResult(-1, false, deleted) { PinToggled = [.. pinToggled] }));
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

        // ListBoxItem's default native name uses the bound item's string value.
        // Keep the full choice and its explanation available to screen readers.
        public override string ToString() => (Pinned ? "Pinned, " : string.Empty) + Item.Title +
            (string.IsNullOrWhiteSpace(Item.Detail) ? string.Empty : ", " + Item.Detail);
    }
}
