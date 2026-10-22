using Avalonia.Media;
using Avalonia.Platform;
using FileCat.Platform.Windows.Shell;

namespace FileCat.App.Services;

/// <summary>Pictures from the Shell helper as Avalonia images (premultiplied BGRA, shown at <c>scale</c> pixels per DIP).</summary>
public static class ShellBitmaps
{
    public static IImage ToImage(ShellImage image, double scale = 1) =>
        ScaledBitmap.FromBgra(image.Width, image.Height, image.Bgra, AlphaFormat.Premul, Math.Max(1, scale));
}
