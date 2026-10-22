using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using FileCat.App.Services;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Rendering.Tests;

/// <summary>
/// Native icons are made at the display's pixels: 32 for a 16-unit row at 200%. Drawn the ways the place bar, the file
/// rows and Quick View draw them, every pixel lands where it belongs at every scaling. (At 200% on Ubuntu, each icon
/// showed only its top-left quarter, enlarged.)
/// </summary>
public sealed class ScaledIconRenderingTests
{
    public static TheoryData<double> Scalings => [1.0, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0];

    [AvaloniaTheory]
    [MemberData(nameof(Scalings))]
    public void A_native_icon_fills_the_place_bars_16_unit_image(double scaling)
    {
        int side = Frames.Pixels(16, scaling);
        var icon = ScaledBitmap.FromBgra(side, side, Pattern(side), AlphaFormat.Unpremul, side / 16.0);
        Assert.Equal(new Size(16, 16), icon.Size);
        var frame = Frames.Render(new Image { Source = icon, Width = 16, Height = 16 }, scaling, 16);
        AssertDrawn(Pattern(side), side, frame, side, 0);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Scalings))]
    public void A_native_icon_fills_a_file_rows_16_unit_rectangle(double scaling)
    {
        int side = Frames.Pixels(16, scaling);
        var icon = ScaledBitmap.FromBgra(side, side, Pattern(side), AlphaFormat.Premul, side / 16.0);
        var frame = Frames.Render(new DrawnIcon(icon), scaling, 16);
        AssertDrawn(Pattern(side), side, frame, side, 0);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Scalings))]
    public void A_shell_thumbnail_keeps_its_size_and_shows_all_its_pixels(double scaling)
    {
        int side = Frames.Pixels(16, scaling);
        var picture = ShellBitmaps.ToImage(new ShellImage(side, side, Pattern(side)), scaling);
        Assert.Equal(16, picture.Size.Width, 9);
        Assert.Equal(16, picture.Size.Height, 9);
        // As Quick View shows a picture: shrunk to fit, never enlarged, so here 16 units in the middle of 24.
        var image = new Image { Source = picture, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly };
        var frame = Frames.Render(new Panel { Children = { image } }, scaling, 24);
        AssertDrawn(Pattern(side), side, frame, Frames.Pixels(24, scaling), Frames.Pixels(4, scaling));
    }

    /// <summary>The file list's way: the icon drawn into a 16-unit rectangle.</summary>
    private sealed class DrawnIcon(IImage icon) : Control
    {
        public override void Render(DrawingContext context) => context.DrawImage(icon, new Rect(0, 0, 16, 16));
    }

    /// <summary>Four quadrants of opaque colors framed along the right and bottom: a pixel from the wrong place shows.</summary>
    private static byte[] Pattern(int side)
    {
        var bgra = new byte[side * side * 4];
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                (byte b, byte g, byte r) = x == side - 1 || y == side - 1 ? ((byte)255, (byte)0, (byte)255)
                    : (x < side / 2, y < side / 2) switch
                    {
                        (true, true) => ((byte)0, (byte)0, (byte)230),
                        (false, true) => ((byte)0, (byte)200, (byte)0),
                        (true, false) => ((byte)230, (byte)0, (byte)0),
                        _ => ((byte)0, (byte)210, (byte)250),
                    };
                int i = (y * side + x) * 4;
                (bgra[i], bgra[i + 1], bgra[i + 2], bgra[i + 3]) = (b, g, r, 255);
            }
        return bgra;
    }

    private static void AssertDrawn(byte[] icon, int side, byte[] frame, int frameSide, int offset)
    {
        int wrong = 0;
        string? first = null;
        for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int i = (y * side + x) * 4, j = ((y + offset) * frameSide + x + offset) * 4;
                if (Enumerable.Range(0, 4).All(c => Math.Abs(icon[i + c] - frame[j + c]) <= 2)) continue;
                wrong++;
                first ??= $"({x}, {y}) is #{frame[j + 2]:X2}{frame[j + 1]:X2}{frame[j]:X2} alpha {frame[j + 3]}, not #{icon[i + 2]:X2}{icon[i + 1]:X2}{icon[i]:X2}";
            }
        Assert.True(wrong == 0, $"{wrong} of {side * side} icon pixels are wrong; the first, {first}");
    }
}
