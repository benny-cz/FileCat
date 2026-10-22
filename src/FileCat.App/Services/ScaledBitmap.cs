using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace FileCat.App.Services;

/// <summary>
/// A picture with more pixels than the units it fills: a 32-pixel icon in a 16-unit row at 200% scaling. Avalonia 12.1
/// measures a bitmap in units (its pixels at its resolution) but draws the source rectangle it is given in pixels, so a
/// bitmap whose resolution made its 32 pixels 16 units showed only its top-left 16 pixels at any scaling above 100%. The
/// pixels stay at 96 DPI, where units and pixels agree; this reports the size in units and draws the matching pixels.
/// </summary>
internal sealed class ScaledBitmap : IImage, IDisposable
{
    private ScaledBitmap(Bitmap pixels, double pixelsPerUnit)
    {
        Pixels = pixels;
        PixelsPerUnit = pixelsPerUnit;
    }

    /// <summary>The pixels, at 96 DPI.</summary>
    internal Bitmap Pixels { get; }

    /// <summary>How many pixels make one unit: the display scaling the picture was made for.</summary>
    internal double PixelsPerUnit { get; }

    public Size Size => new(Pixels.PixelSize.Width / PixelsPerUnit, Pixels.PixelSize.Height / PixelsPerUnit);

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) =>
        context.DrawImage(Pixels, new Rect(sourceRect.Position * PixelsPerUnit, sourceRect.Size * PixelsPerUnit), destRect);

    public void Dispose() => Pixels.Dispose();

    /// <summary>Rows of BGRA pixels shown at <paramref name="pixelsPerUnit"/> pixels per unit.</summary>
    internal static IImage FromBgra(int width, int height, byte[] bgra, AlphaFormat alpha, double pixelsPerUnit)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, alpha);
        using (var locked = bitmap.Lock())
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(bgra, y * width * 4, locked.Address + y * locked.RowBytes, width * 4);
        }
        return Wrap(bitmap, pixelsPerUnit);
    }

    /// <summary>A 96-DPI bitmap shown at <paramref name="pixelsPerUnit"/> pixels per unit: itself when that is one.</summary>
    internal static IImage Wrap(WriteableBitmap bitmap, double pixelsPerUnit) =>
        Math.Abs(pixelsPerUnit - 1) < 1e-9 ? bitmap : new ScaledBitmap(bitmap, pixelsPerUnit);
}
