using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class FractionalScrollTests
{
    [Fact]
    public void Row_accumulator_preserves_fractional_distance_and_direction()
    {
        var wheel = new WheelRowAccumulator();
        Assert.Equal(3, Enumerable.Range(0, 10).Sum(_ => wheel.Consume(0.1)));
        Assert.Equal(-3, Enumerable.Range(0, 10).Sum(_ => wheel.Consume(-0.1)));
        Assert.Equal(0, wheel.Consume(0.1));
        Assert.Equal(0, wheel.Consume(-0.1));
        Assert.Equal(3, wheel.Consume(1));
        wheel.Reset();
        Assert.Equal(0, wheel.Consume(0.2));
        wheel.Reset();
        Assert.Equal(0, wheel.Consume(0.2));
        Assert.Equal(0, wheel.Consume(double.NaN));
        Assert.Equal(0, wheel.Consume(0.2));
    }

    [AvaloniaFact]
    public async Task Text_viewer_precision_wheel_and_one_row_pages_move_by_whole_rows()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 100).Select(i => $"line {i:D3}\n")));
        using var reader = new PagedReader(new MemoryContentSource("text", bytes));
        var view = new TextViewer { Wrap = false };
        view.SetReader(reader, Encoding.UTF8, 0);
        var window = new Window { Width = 420, Height = 200, Content = view };
        window.Show();
        try
        {
            await Wait(() => view.SourceGlyphBounds(0) is not null);
            var point = new Point(40, 40);
            for (int i = 0; i < 10; i++) window.MouseWheel(point, new Vector(0, -0.1));
            long small = view.TopOffset;
            Assert.Equal(3 * 9, small);
            view.ScrollToOffset(0);
            window.MouseWheel(point, new Vector(0, -1));
            Assert.Equal(small, view.TopOffset);
            for (int i = 0; i < 10; i++) window.MouseWheel(point, new Vector(0, 0.1));
            Assert.Equal(0, view.TopOffset);

            window.Height = 20;
            window.CaptureRenderedFrame()?.Dispose();
            view.Focus();
            window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
            Assert.Equal(9, view.TopOffset);
            window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
            Assert.Equal(0, view.TopOffset);
        }
        finally { view.SetReader(null, Encoding.UTF8, 0); window.Close(); }
    }

    [AvaloniaFact]
    public void Hex_viewer_precision_wheel_and_one_row_PageUp_keep_moving()
    {
        using var reader = new PagedReader(new MemoryContentSource("hex", new byte[16 * 100]));
        var view = new HexView();
        view.SetReader(reader);
        var window = new Window { Width = 420, Height = 200, Content = view };
        window.Show();
        try
        {
            window.UpdateLayout();
            var bar = view.GetVisualDescendants().OfType<ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
            var point = new Point(40, 40);
            for (int i = 0; i < 10; i++) window.MouseWheel(point, new Vector(0, -0.1));
            Assert.Equal(3, bar.Value);
            bar.Value = 0;
            window.MouseWheel(point, new Vector(0, -1));
            Assert.Equal(3, bar.Value);
            window.Height = 20;
            window.CaptureRenderedFrame()?.Dispose();
            view.GoTo(16 * 5);
            view.Focus();
            window.KeyPress(Key.PageUp, RawInputModifiers.None, PhysicalKey.PageUp, null);
            Assert.Equal(16 * 4, view.CursorOffset);
        }
        finally { view.SetReader(null); window.Close(); }
    }

    [AvaloniaFact]
    public void Hex_comparison_precision_wheel_and_one_row_paging_move()
    {
        using var left = new PagedReader(new MemoryContentSource("left", new byte[16 * 100]));
        using var right = new PagedReader(new MemoryContentSource("right", new byte[16 * 100]));
        var view = new HexCompareView();
        var window = new Window { Width = 640, Height = 200, Content = view };
        window.Show();
        try
        {
            window.UpdateLayout();
            view.SetContent(left, right, null, []);
            var point = new Point(40, 40);
            for (int i = 0; i < 10; i++) window.MouseWheel(point, new Vector(0, -0.1));
            Assert.Equal(3, view.TopRow);
            view.SetContent(left, right, null, []);
            window.MouseWheel(point, new Vector(0, -1));
            Assert.Equal(3, view.TopRow);
            for (int i = 0; i < 10; i++) window.MouseWheel(point, new Vector(0, 0.1));
            Assert.Equal(0, view.TopRow);
            window.Height = 20;
            window.CaptureRenderedFrame()?.Dispose();
            view.Focus();
            window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.PageDown, null);
            Assert.Equal(1, view.TopRow);
        }
        finally { view.SetContent(null, null, null, []); window.Close(); }
    }

    private static async Task Wait(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
