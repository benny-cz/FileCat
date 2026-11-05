using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private sealed partial class SessionView
    {
        private void ConfigureTableSelection()
        {
            const KeyModifiers toggle = KeyModifiers.Control | KeyModifiers.Meta;
            const KeyModifiers allowed = toggle | KeyModifiers.Shift;
            bool modifiedPress = false;
            void EndModifiedPress()
            { if (modifiedPress) { modifiedPress = false; Model.Interaction(false); } }
            _list.AddHandler(PointerReleasedEvent, (_, e) => { if (modifiedPress) { e.Pointer.Capture(null); EndModifiedPress(); } }, RoutingStrategies.Bubble, handledEventsToo: true);
            _list.PointerCaptureLost += (_, _) => EndModifiedPress();
            _list.DetachedFromVisualTree += (_, _) => EndModifiedPress();
            AutomationProperties.SetHelpText(_list, "Click focuses a row. Ctrl or Command-click toggles a real row mark. Shift-click or Shift with arrows, Home, End or Page keys marks the captured table range. Accounting groups require explicit Mark current members; the review basket stays separate.");
            _list.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (_disposed || _window._dialogs.IsOpen || !e.GetCurrentPoint(_list).Properties.IsLeftButtonPressed || (e.KeyModifiers & ~allowed) != 0 || e.Source is not Visual source) return;
                var container = source as ListBoxItem ?? source.GetVisualAncestors().OfType<ListBoxItem>().FirstOrDefault();
                if (container?.Content is not AnalysisTreeItem item || !Model.Rows.Any(row => ReferenceEquals(row, item))) return;
                if (e.KeyModifiers != KeyModifiers.None && !modifiedPress)
                { modifiedPress = true; Model.Interaction(true); e.Pointer.Capture(_list); }
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    e.Handled = true;
                    if (Model.FocusTableRange(item.Key, (e.KeyModifiers & toggle) != 0)) container.Focus();
                }
                else if ((e.KeyModifiers & toggle) != 0)
                {
                    e.Handled = true; Model.Focus(item.Key, toggleMark: true); container.Focus();
                }
                else Model.SetTableAnchor(item.Key);
            }, RoutingStrategies.Tunnel);
            _list.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (_disposed || _window._dialogs.IsOpen || (e.KeyModifiers & KeyModifiers.Shift) == 0 || (e.KeyModifiers & ~allowed) != 0 || Model.Rows.IsEmpty) return;
                if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown)) return;
                e.Handled = true;
                var rows = Model.Rows; int current = -1;
                for (int i = 0; i < rows.Length; i++) if (rows[i].Key == Model.Selection.Focus) { current = i; break; }
                var container = _list.ContainerFromIndex(Math.Max(0, current));
                double height = container?.Bounds.Height ?? 0;
                int page = height > 0 && double.IsFinite(height) && double.IsFinite(_list.Bounds.Height)
                    ? (int)Math.Clamp(_list.Bounds.Height / height, 1, rows.Length) : 1;
                int next = e.Key switch
                {
                    Key.Up => current < 0 ? rows.Length - 1 : Math.Max(0, current - 1), Key.Down => current < 0 ? 0 : Math.Min(rows.Length - 1, current + 1),
                    Key.Home => 0, Key.End => rows.Length - 1,
                    Key.PageUp => current < 0 ? rows.Length - 1 : Math.Max(0, current - page), _ => current < 0 ? 0 : Math.Min(rows.Length - 1, current + page)
                };
                if (!Model.FocusTableRange(rows[next].Key, (e.KeyModifiers & toggle) != 0)) return;
                if (_list.SelectedItem is { } selected) _list.ScrollIntoView(selected);
                _list.Focus();
            }, RoutingStrategies.Tunnel);
        }
    }
}
