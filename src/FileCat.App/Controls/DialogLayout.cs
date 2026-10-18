using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace FileCat.App.Controls;

/// <summary>
/// A modal's header and actions own their space first. Only its body scrolls. The parent gives this
/// card the actual client area after the overlay margins; no screen size or display-scale guess is used.
/// </summary>
internal sealed class DialogCard : Border
{
    private readonly Border _bodyBorder;
    private readonly Control _body;
    private readonly WidthConstrainedContent _content;
    private readonly Grid _layout;
    private double _lastAvailableWidth = double.NaN;
    private Size _requestedSize;
    private bool _resizable;

    internal DialogCard(string title, Control body, Control? buttons, double maxWidth)
    {
        _body = body;
        Classes.Add("card");
        MaxWidth = maxWidth;
        ClipToBounds = true;
        Styles.Add(new Style(s => s.OfType<CheckBox>())
        {
            Setters = { new Setter(ContentControl.ContentTemplateProperty, DialogLayout.WrappingText) },
        });
        Styles.Add(new Style(s => s.OfType<RadioButton>())
        {
            Setters = { new Setter(ContentControl.ContentTemplateProperty, DialogLayout.WrappingText) },
        });
        var titleLine = new Grid { ColumnDefinitions = new ColumnDefinitions("4,*"), ColumnSpacing = 10 };
        titleLine.Children.Add(new Border { Classes = { "dialogAccent" }, Height = 20, VerticalAlignment = VerticalAlignment.Center });
        var titleText = new TextBlock { Text = title, Classes = { "dialogTitle" }, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(titleText, 1);
        titleLine.Children.Add(titleText);

        _content = new WidthConstrainedContent { Child = body };
        var scroll = new ScrollViewer
        {
            Name = "DialogBodyScroll",
            Content = _content,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        AutomationProperties.SetName(scroll, title + " content");
        // Horizontal Auto normally measures with infinity, preventing wrapped forms from responding to
        // the viewport. Keep a finite measure constraint, but retain an honest horizontal extent for a
        // legacy/custom child that genuinely cannot shrink. Scrollbars themselves consume viewport space.
        scroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ViewportProperty)
            {
                if (scroll.Viewport.Width > 0) _content.ConstrainWidth(scroll.Viewport.Width);
                if (_resizable && scroll.Viewport.Height > 0) _body.MinHeight = scroll.Viewport.Height;
            }
        };
        _bodyBorder = new Border { Classes = { "dialogBody" }, Child = scroll };
        _layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        _layout.Children.Add(new Border { Classes = { "dialogHeader" }, Child = titleLine });
        Grid.SetRow(_bodyBorder, 1);
        _layout.Children.Add(_bodyBorder);
        if (buttons is not null)
        {
            var footer = new Border { Classes = { "dialogFooter" }, Child = buttons };
            Grid.SetRow(footer, 2);
            _layout.Children.Add(footer);
        }
        Child = _layout;
    }

    /// <summary>Optional pointer and keyboard size control for substantial search dialogs.</summary>
    internal void EnableResizing(Panel host)
    {
        _resizable = true;
        MaxWidth = double.PositiveInfinity;
        _requestedSize = new Size(700, 560);
        _layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var gripRow = new Grid { Height = 24 };
        var thumb = new Thumb { Name = "DialogResizeGrip", Width = 32, Height = 24,
            HorizontalAlignment = HorizontalAlignment.Right, Focusable = true, Background = Brushes.Transparent,
            Template = new FuncControlTemplate<Thumb>((_, _) => new Border { Background = Brushes.Transparent }) };
        AutomationProperties.SetName(thumb, "Resize dialog");
        AutomationProperties.SetHelpText(thumb, "Drag to resize, or use arrow keys. Hold Shift for larger steps.");
        ToolTip.SetTip(thumb, "Drag or use arrow keys to resize · Shift for larger steps");
        gripRow.Children.Add(new TextBlock { Text = "◢", FontSize = 18, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), IsHitTestVisible = false });
        gripRow.Children.Add(thumb);
        Grid.SetRow(gripRow, 3);
        _layout.Children.Add(gripRow);

        void ClampSize()
        {
            double maxWidth = Math.Max(1, host.Bounds.Width - 48);
            double maxHeight = Math.Max(1, host.Bounds.Height - 48);
            Width = Math.Clamp(_requestedSize.Width, Math.Min(400, maxWidth), maxWidth);
            Height = Math.Clamp(_requestedSize.Height, Math.Min(280, maxHeight), maxHeight);
        }
        void Resize(double dx, double dy)
        {
            if (!double.IsFinite(dx) || !double.IsFinite(dy)) return;
            _requestedSize = new Size(Math.Max(1, Width + dx), Math.Max(1, Height + dy));
            ClampSize();
        }
        thumb.DragDelta += (_, e) => Resize(e.Vector.X, e.Vector.Y);
        thumb.KeyDown += (_, e) =>
        {
            double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 48 : 16;
            (double x, double y) = e.Key switch
            {
                Key.Left => (-step, 0d), Key.Right => (step, 0d),
                Key.Up => (0d, -step), Key.Down => (0d, step), _ => (0d, 0d),
            };
            if (x == 0 && y == 0) return;
            Resize(x, y);
            e.Handled = true;
        };
        host.SizeChanged += OnHostSizeChanged;
        DetachedFromVisualTree += (_, _) => host.SizeChanged -= OnHostSizeChanged;
        void OnHostSizeChanged(object? _, SizeChangedEventArgs __) => ClampSize();
        ClampSize();
    }

    protected override Type StyleKeyOverride => typeof(Border);

    protected override Size MeasureOverride(Size availableSize)
    {
        if (double.IsFinite(availableSize.Width) && !availableSize.Width.Equals(_lastAvailableWidth))
        {
            _lastAvailableWidth = availableSize.Width;
            _content.ConstrainWidth(Math.Max(0, availableSize.Width - BorderThickness.Left - BorderThickness.Right
                - Padding.Left - Padding.Right - _bodyBorder.Padding.Left - _bodyBorder.Padding.Right));
        }
        var measured = base.MeasureOverride(availableSize);
        // A stable width avoids a form jumping sideways when an error, a tab or a preview changes.
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : measured.Width, measured.Height);
    }

    private sealed class WidthConstrainedContent : Decorator
    {
        private double _width = double.PositiveInfinity;
        private double _leftOverflow;
        private double _rightOverflow;

        internal void ConstrainWidth(double width)
        {
            if (!double.IsFinite(width) || width < 0 || Math.Abs(_width - width) < 0.1) return;
            _width = width;
            InvalidateMeasure();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child is null) return default;
            Child.Measure(new Size(Math.Min(_width, availableSize.Width), availableSize.Height));
            return new Size(Child.DesiredSize.Width + _leftOverflow + _rightOverflow, Child.DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child is null) return finalSize;
            double width = Math.Min(_width, finalSize.Width);
            // Keep responsive content at the real viewport width even when a fixed-width descendant
            // needs overflow space. Expanding the form itself would undo wrapping for every other field.
            Child.Arrange(new Rect(_leftOverflow, 0, width, finalSize.Height));
            double left = 0, right = width;
            CollectOverflow(Child, ref left, ref right);
            double nextLeft = Math.Max(0, -left), nextRight = Math.Max(0, right - width);
            if (Math.Abs(nextLeft - _leftOverflow) > 0.5 || Math.Abs(nextRight - _rightOverflow) > 0.5)
            {
                _leftOverflow = nextLeft;
                _rightOverflow = nextRight;
                InvalidateMeasure();
            }
            return finalSize;
        }

        private void CollectOverflow(Visual visual, ref double left, ref double right)
        {
            if (!visual.IsVisible) return;
            if (visual.TranslatePoint(default, this) is { } start &&
                visual.TranslatePoint(new Point(visual.Bounds.Width, 0), this) is { } end)
            {
                left = Math.Min(left, Math.Min(start.X, end.X) - _leftOverflow);
                right = Math.Max(right, Math.Max(start.X, end.X) - _leftOverflow);
            }
            // Only realized, unclipped layout participates. Text editors, lists and custom viewports
            // own their internal scrolling; their full content must not enlarge the enclosing modal.
            if (visual.ClipToBounds || visual is ScrollViewer) return;
            foreach (var child in visual.GetVisualChildren()) CollectOverflow(child, ref left, ref right);
        }
    }
}

/// <summary>Labels sit beside fields when there is room, and above them in a narrow dialog.</summary>
internal sealed class DialogForm : Grid
{
    private readonly (TextBlock? Label, Control Field)[] _rows;
    private bool? _wide;

    internal DialogForm(params (string Label, Control Control)[] rows)
    {
        _rows = rows.Select(row =>
        {
            TextBlock? label = row.Label.Length == 0 ? null : new TextBlock
            {
                Text = row.Label,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = row.Control is TextBox { AcceptsReturn: true } ? VerticalAlignment.Top : VerticalAlignment.Center,
            };
            if (label is not null)
            {
                Children.Add(label);
                if (string.IsNullOrEmpty(AutomationProperties.GetName(row.Control)))
                    AutomationProperties.SetName(row.Control, row.Label);
                AutomationProperties.SetLabeledBy(row.Control, label);
            }
            Children.Add(row.Control);
            return (label, row.Control);
        }).ToArray();
        SetLayout(wide: false);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        SetLayout(availableSize.Width >= 640);
        return base.MeasureOverride(availableSize);
    }

    private void SetLayout(bool wide)
    {
        if (_wide == wide) return;
        _wide = wide;
        ColumnDefinitions = new ColumnDefinitions(wide ? "200,*" : "*");
        RowDefinitions.Clear();
        int row = 0;
        foreach (var (label, field) in _rows)
        {
            RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            if (label is not null)
            {
                SetRow(label, row);
                label.Margin = wide ? new Thickness(0, 6, 12, 4) : new Thickness(0, 8, 0, 2);
                if (!wide)
                {
                    row++;
                    RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                }
            }
            SetRow(field, row++);
            SetColumn(field, wide && label is not null ? 1 : 0);
            SetColumnSpan(field, wide && label is null ? 2 : 1);
            field.Margin = new Thickness(0, 4);
        }
    }
}

internal static class DialogLayout
{
    // Keep the original string Content (and thus the accessible name and access key) intact.
    internal static readonly IDataTemplate WrappingText = new FuncDataTemplate<string>((text, _) =>
        new AccessText { Text = text, TextWrapping = TextWrapping.Wrap });

    internal static WrapPanel Actions(params Control[] controls)
    {
        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var control in controls) row.Children.Add(control);
        return row;
    }
}

/// <summary>Two related sections keep their reading order when a compact dialog stacks them.</summary>
internal sealed class DialogColumns : Grid
{
    private readonly Control _second;
    private bool? _wide;

    internal DialogColumns(Control first, Control second)
    {
        _second = second;
        Children.Add(first);
        Children.Add(second);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        bool wide = availableSize.Width >= 640;
        if (_wide != wide)
        {
            _wide = wide;
            ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "*");
            RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            ColumnSpacing = 24;
            RowSpacing = 12;
            SetColumn(_second, wide ? 1 : 0);
            SetRow(_second, wide ? 0 : 1);
        }
        return base.MeasureOverride(availableSize);
    }
}
