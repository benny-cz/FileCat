using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FileCat.App.Services;

namespace FileCat.App.Controls;

/// <summary>
/// The viewer's picture mode: the picture fitted to the window (never enlarged), or zoomed and moved as picture viewers
/// do it: the mouse wheel zooms around the pointer, dragging moves the picture, a double click switches between fitted
/// and actual size; + and − zoom, 0 fits, 1 shows actual size, arrows move. Over a checkerboard where the picture is
/// transparent; or a line saying why there is no picture.
/// </summary>
public sealed class PictureView : Border
{
    /// <summary>The smallest and largest zoom, relative to the picture's own pixels.</summary>
    public const double MinZoom = 0.02, MaxZoom = 32;
    private const double Step = 1.25;

    private readonly Surface _surface;
    private readonly TextBlock _message = new()
    {
        Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, MaxWidth = 560,
    };
    private bool _fit = true;
    /// <summary>Device-independent pixels per pixel of the decoded bitmap, and where the bitmap's corner is drawn.</summary>
    private double _scale = 1;
    private Point _offset;
    private Point? _dragFrom;
    private Point _dragOffset;

    public PictureView()
    {
        Focusable = true;
        ClipToBounds = true;
        _surface = new Surface(this);
        Child = new Panel { Children = { _surface, _message } };
    }

    public DecodedPicture? Picture { get; private set; }

    /// <summary>The zoom or position changed (the viewer's status line says the zoom).</summary>
    public event Action? ZoomChanged;

    /// <summary>
    /// Fitted to the window (the default). Leaving it shows the actual size; a zoom of the wheel or the keys leaves it too.
    /// </summary>
    public bool Fit
    {
        get => _fit;
        set
        {
            if (value == _fit) return;
            if (value) FitNow();
            else ZoomTo(1, Center);
        }
    }

    /// <summary>How large the picture is drawn, relative to its own pixels (what the status line says).</summary>
    public double Zoom => Picture is { } p && p.Width > 0 ? _scale * Scaling * p.Bitmap.PixelSize.Width / p.Width : 1;

    private double Scaling => TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;

    private Point Center => new(Bounds.Width / 2, Bounds.Height / 2);

    public void Show(DecodedPicture picture)
    {
        var old = Picture;
        Picture = picture;
        _message.IsVisible = false;
        _surface.IsVisible = true;
        FitNow();
        old?.Bitmap.Dispose();
    }

    public void ShowMessage(string text)
    {
        _message.Text = text;
        _message.IsVisible = true;
        _surface.IsVisible = false;
    }

    /// <summary>The picture as large as fits, never larger than its actual size, centered.</summary>
    private void FitNow()
    {
        _fit = true;
        if (Picture is { } p && Bounds.Width > 0 && Bounds.Height > 0)
        {
            var pixels = p.Bitmap.PixelSize;
            _scale = Math.Min(Math.Min(Bounds.Width / pixels.Width, Bounds.Height / pixels.Height), ScaleFor(1));
        }
        Clamp();
        Changed();
    }

    /// <summary>The scale that shows the picture at <paramref name="zoom"/> of its own pixels.</summary>
    private double ScaleFor(double zoom) => Picture is { } p ? zoom * p.Width / (p.Bitmap.PixelSize.Width * Scaling) : 1;

    /// <summary>Zooms keeping the picture's point under <paramref name="at"/> where it is.</summary>
    private void ZoomTo(double zoom, Point at)
    {
        if (Picture is null) return;
        double scale = ScaleFor(Math.Clamp(zoom, MinZoom, MaxZoom));
        var under = new Point((at.X - _offset.X) / _scale, (at.Y - _offset.Y) / _scale);
        _scale = scale;
        _offset = new Point(at.X - under.X * scale, at.Y - under.Y * scale);
        _fit = false;
        Clamp();
        Changed();
    }

    /// <summary>A side smaller than the window is centered; a larger one never leaves empty space at its edges.</summary>
    private void Clamp()
    {
        if (Picture is not { } p) return;
        double width = p.Bitmap.PixelSize.Width * _scale, height = p.Bitmap.PixelSize.Height * _scale;
        double x = width <= Bounds.Width ? (Bounds.Width - width) / 2 : Math.Clamp(_offset.X, Bounds.Width - width, 0);
        double y = height <= Bounds.Height ? (Bounds.Height - height) / 2 : Math.Clamp(_offset.Y, Bounds.Height - height, 0);
        _offset = new Point(x, y);
    }

    private bool Movable => Picture is { } p && (p.Bitmap.PixelSize.Width * _scale > Bounds.Width + 0.5 || p.Bitmap.PixelSize.Height * _scale > Bounds.Height + 0.5);

    private void Changed()
    {
        _surface.Cursor = Movable ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
        _surface.InvalidateVisual();
        ZoomChanged?.Invoke();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        // A fitted picture follows the window's size; a zoomed one keeps its zoom and stays within bounds.
        if (_fit) FitNow();
        else
        {
            Clamp();
            _surface.InvalidateVisual();
        }
        return size;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Picture is null || e.Delta.Y == 0) return;
        ZoomTo(Zoom * Math.Pow(Step, e.Delta.Y), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        if (Picture is null || !point.Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            // Double click: actual size at the point clicked, or back to fitted.
            if (_fit || Math.Abs(Zoom - 1) > 0.001) ZoomTo(1, point.Position);
            else FitNow();
            e.Handled = true;
            return;
        }
        if (!Movable) return;
        _dragFrom = point.Position;
        _dragOffset = _offset;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragFrom is not { } from) return;
        var at = e.GetPosition(this);
        _offset = new Point(_dragOffset.X + at.X - from.X, _dragOffset.Y + at.Y - from.Y);
        Clamp();
        _surface.InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragFrom is null) return;
        _dragFrom = null;
        e.Pointer.Capture(null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Picture is null || (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta)) != 0) return;
        double step = Math.Max(Bounds.Width, Bounds.Height) / 10;
        switch (e.Key)
        {
            case Key.Add or Key.OemPlus:
                ZoomTo(Zoom * Step, Center);
                break;
            case Key.Subtract or Key.OemMinus:
                ZoomTo(Zoom / Step, Center);
                break;
            case Key.D0 or Key.NumPad0:
                FitNow();
                break;
            case Key.D1 or Key.NumPad1:
                ZoomTo(1, Center);
                break;
            case Key.Left or Key.Right or Key.Up or Key.Down when Movable:
                _offset = e.Key switch
                {
                    Key.Left => new Point(_offset.X + step, _offset.Y),
                    Key.Right => new Point(_offset.X - step, _offset.Y),
                    Key.Up => new Point(_offset.X, _offset.Y + step),
                    _ => new Point(_offset.X, _offset.Y - step),
                };
                Clamp();
                _surface.InvalidateVisual();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    /// <summary>Zooms by wheel notches around a point of the view (tests; the wheel does the same).</summary>
    internal void ZoomBy(double notches, Point at) => ZoomTo(Zoom * Math.Pow(Step, notches), at);

    /// <summary>Where the picture's top-left corner is drawn (tests).</summary>
    internal Point Offset => _offset;

    /// <summary>Draws the bitmap at the current scale and place: smoothly when shrunk, as crisp pixels when enlarged.</summary>
    private sealed class Surface(PictureView owner) : Control
    {
        private static readonly IBrush Checker = Checkerboard();

        public override void Render(DrawingContext context)
        {
            if (owner.Picture is not { } picture) return;
            var bitmap = picture.Bitmap;
            var destination = new Rect(owner._offset, new Size(bitmap.PixelSize.Width * owner._scale, bitmap.PixelSize.Height * owner._scale));
            context.FillRectangle(Checker, destination);
            bool enlarged = owner._scale * owner.Scaling >= 2;
            using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = enlarged ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
                context.DrawImage(bitmap, new Rect(bitmap.Size), destination);
        }
    }

    /// <summary>A light and a darker gray square, tiled: where the picture is transparent, it shows.</summary>
    private static IBrush Checkerboard()
    {
        var tile = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = tile.Lock())
        {
            var row = new byte[16 * 4];
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    byte v = (x < 8) ^ (y < 8) ? (byte)0xCC : (byte)0xF2;
                    row[x * 4] = row[x * 4 + 1] = row[x * 4 + 2] = v;
                    row[x * 4 + 3] = 0xFF;
                }
                System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
            }
        }
        return new ImageBrush(tile)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.None,
            DestinationRect = new RelativeRect(0, 0, 16, 16, RelativeUnit.Absolute),
        };
    }
}
