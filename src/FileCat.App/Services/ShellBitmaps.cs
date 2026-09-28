using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using FileCat.Platform.Windows.Shell;

namespace FileCat.App.Services;

/// <summary>Pictures from the Shell helper as Avalonia bitmaps (premultiplied BGRA, shown at <c>scale</c> pixels per DIP).</summary>
public static class ShellBitmaps
{
    public static Bitmap ToBitmap(ShellImage image, double scale = 1)
    {
        var dpi = 96 * Math.Max(1, scale);
        var bmp = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(dpi, dpi), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var fb = bmp.Lock();
        for (int y = 0; y < image.Height; y++)
            Marshal.Copy(image.Bgra, y * image.Width * 4, fb.Address + y * fb.RowBytes, image.Width * 4);
        return bmp;
    }
}
