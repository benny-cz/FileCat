using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

[assembly: AvaloniaTestApplication(typeof(FileCat.Rendering.Tests.RenderingTestAppBuilder))]
// One application and dispatcher for all tests, one test at a time (as in the App tests).
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace FileCat.Rendering.Tests;

public static class RenderingTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<RenderingTestApplication>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class RenderingTestApplication : Application;

/// <summary>Draws a control as a display at some scaling would, and reads back its pixels.</summary>
internal static class Frames
{
    /// <summary>
    /// The control laid out in a square of <paramref name="units"/> device-independent units and drawn at
    /// <paramref name="scaling"/> pixels per unit, as BGRA rows of straight (not premultiplied) color.
    /// </summary>
    internal static byte[] Render(Control control, double scaling, double units)
    {
        control.Measure(new Size(units, units));
        control.Arrange(new Rect(0, 0, units, units));
        int side = Pixels(units, scaling);
        using var target = new RenderTargetBitmap(new PixelSize(side, side), new Vector(96 * scaling, 96 * scaling));
        target.Render(control);
        var bgra = new byte[side * side * 4];
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try { target.CopyPixels(new PixelRect(0, 0, side, side), handle.AddrOfPinnedObject(), bgra.Length, side * 4); }
        finally { handle.Free(); }
        if (target.Format == PixelFormat.Rgba8888)
            for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
        if (target.AlphaFormat == AlphaFormat.Premul)
            for (int i = 0; i < bgra.Length; i += 4)
                if (bgra[i + 3] is > 0 and < 255 and var a)
                    for (int c = 0; c < 3; c++) bgra[i + c] = (byte)Math.Min(255, bgra[i + c] * 255 / a);
        return bgra;
    }

    internal static int Pixels(double units, double scaling) => (int)Math.Round(units * scaling);
}
