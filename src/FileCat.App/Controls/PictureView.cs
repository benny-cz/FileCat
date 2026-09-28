using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FileCat.App.Services;

namespace FileCat.App.Controls;

/// <summary>
/// The viewer's picture mode: the picture fitted to the window (never enlarged) or at its actual size with scrolling, over
/// a checkerboard where it is transparent; or a line saying why there is no picture.
/// </summary>
public sealed class PictureView : Border
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
    private readonly Border _frame;
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _message = new()
    {
        Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, MaxWidth = 560,
    };
    private bool _fit = true;

    public PictureView()
    {
        Focusable = true;
        _frame = new Border { Child = _image, Background = Checkerboard(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _scroll = new ScrollViewer { Content = _frame };
        Child = new Panel { Children = { _scroll, _message } };
        Apply();
    }

    public DecodedPicture? Picture { get; private set; }

    /// <summary>Fitted to the window (the default) or at actual size.</summary>
    public bool Fit
    {
        get => _fit;
        set
        {
            _fit = value;
            Apply();
        }
    }

    /// <summary>How large the picture is drawn, relative to its own pixels (what the status line says).</summary>
    public double Zoom
    {
        get
        {
            if (Picture is null) return 1;
            double shown = _image.Bounds.Width > 0 ? _image.Bounds.Width : Picture.Bitmap.PixelSize.Width;
            return shown / Picture.Width;
        }
    }

    public void Show(DecodedPicture picture)
    {
        var old = Picture;
        Picture = picture;
        _image.Source = picture.Bitmap;
        _message.IsVisible = false;
        _scroll.IsVisible = true;
        Apply();
        old?.Bitmap.Dispose();
    }

    public void ShowMessage(string text)
    {
        _message.Text = text;
        _message.IsVisible = true;
        _scroll.IsVisible = false;
    }

    private void Apply()
    {
        // Fitted: the frame takes the viewport's size and the picture shrinks into it; actual size: the decoded pixels
        // (at most 4096 on the longest side) at one pixel per pixel, scrolled.
        _scroll.HorizontalScrollBarVisibility = _fit ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _scroll.VerticalScrollBarVisibility = _fit ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
        _image.Stretch = _fit ? Stretch.Uniform : Stretch.None;
        if (Picture is { } p && !_fit)
        {
            // One screen pixel per decoded pixel, whatever the display scale.
            double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            _image.Width = p.Bitmap.PixelSize.Width / scaling;
            _image.Height = p.Bitmap.PixelSize.Height / scaling;
        }
        else
        {
            _image.Width = double.NaN;
            _image.Height = double.NaN;
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
