using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using FileCat.App.Controls;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    private void ConfigureAnalysisLayout()
    {
        // Keep command strings available to automation and use the theme's existing control templates.
        Styles.Add(new Style(s => s.Is<Button>())
        {
            Setters = { new Setter(MinHeightProperty, 32d), new Setter(ContentControl.ContentTemplateProperty, DialogLayout.WrappingText) }
        });
        Styles.Add(new Style(s => s.OfType<CheckBox>())
        {
            Setters = { new Setter(MinHeightProperty, 30d), new Setter(ContentControl.ContentTemplateProperty, DialogLayout.WrappingText) }
        });
        Styles.Add(new Style(s => s.OfType<TextBox>()) { Setters = { new Setter(MinHeightProperty, 32d) } });
        Styles.Add(new Style(s => s.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(MinHeightProperty, 32d),
                new Setter(ItemsControl.ItemTemplateProperty, WrappingChoice),
                new Setter(ComboBox.SelectionBoxItemTemplateProperty, WrappingChoice)
            }
        });
        Styles.Add(new Style(s => s.OfType<Expander>())
        {
            Setters = { new Setter(HeaderedContentControl.HeaderTemplateProperty, DialogLayout.WrappingListText) }
        });
        Styles.Add(new Style(s => s.OfType<TextBlock>().Class("dialogTitle"))
        {
            Setters = { new Setter(TextBlock.FontSizeProperty, ReadingFont(3)) }
        });
        Styles.Add(new Style(s => s.OfType<Border>().Class("dialogFooter"))
        {
            Setters = { new Setter(Border.PaddingProperty, new Thickness(12, 8)) }
        });
    }

    private static readonly IDataTemplate WrappingChoice = new FuncDataTemplate<object>(choice => choice is not Control, (choice, _) => new TextBlock
    {
        Text = choice?.ToString(),
        TextWrapping = TextWrapping.Wrap
    });

    private static Binding ReadingFont(double increase) => new(nameof(FontSize))
    {
        RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DiskSpaceWindow) },
        Converter = new FuncValueConverter<double, double>(size => size + increase)
    };

    private static ScrollViewer AnalysisControlsViewport(Control content, string name = "Analysis view controls")
    {
        var viewport = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(viewport, name);
        viewport.GotFocus += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            // The inner scroller reveals the command. The outer scroller must reveal the inner viewport too.
            if (viewport.IsEffectivelyVisible && viewport.IsKeyboardFocusWithin) viewport.BringIntoView();
        }, DispatcherPriority.Loaded);
        return viewport;
    }

    private double ControlsViewportHeight(double height, double summaryHeight) =>
        Math.Max(Math.Max(52, FontSize * 2 + 12), height / 2 - summaryHeight - 4);

    private sealed class LocationEditorRow : Grid
    {
        private readonly TextBox _path;
        private readonly Button _remove;
        private bool? _stacked;

        internal LocationEditorRow(TextBox path, Button remove)
        {
            _path = path;
            _remove = remove;
            ColumnSpacing = 8;
            RowSpacing = 4;
            ColumnDefinitions = new("*,Auto");
            RowDefinitions = new("Auto,0");
            Children.Add(path);
            Children.Add(remove);
            remove.HorizontalAlignment = HorizontalAlignment.Right;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            bool stacked = availableSize.Width < 12 * _path.FontSize;
            if (_stacked != stacked)
            {
                _stacked = stacked;
                ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Auto;
                RowDefinitions[1].Height = stacked ? GridLength.Auto : new GridLength(0);
                ColumnSpacing = stacked ? 0 : 8;
                RowSpacing = stacked ? 4 : 0;
                SetColumn(_remove, stacked ? 0 : 1);
                SetRow(_remove, stacked ? 1 : 0);
            }
            return base.MeasureOverride(availableSize);
        }
    }

    private sealed class RankedColumns : Grid
    {
        private readonly bool _header;
        private bool? _wide;

        internal RankedColumns(bool header)
        {
            _header = header;
            ColumnDefinitions = new("34,5*,4*,3*,2*,3*");
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            bool wide = availableSize.Width >= Math.Max(750, 51 * GetValue(TextElement.FontSizeProperty));
            if (_wide != wide)
            {
                _wide = wide;
                for (int i = 3; i < ColumnDefinitions.Count; i++)
                    ColumnDefinitions[i].Width = wide ? new GridLength(i == 4 ? 2 : 3, GridUnitType.Star) : new GridLength(0);
                ColumnSpacing = wide ? 6 : 4;
                foreach (var child in Children) child.IsVisible = wide || GetColumn(child) < 3;
                if (_header) ((TextBlock)Children[2]).Text = wide ? "Active metric" : "Value";
            }
            return base.MeasureOverride(availableSize);
        }
    }
}
