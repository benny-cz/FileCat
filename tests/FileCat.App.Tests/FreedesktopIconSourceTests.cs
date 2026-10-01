using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>Release issue I27: a symbolic icon is drawn in the theme's text color, its shape (alpha) unchanged.</summary>
public sealed class FreedesktopIconSourceTests
{
    [Fact]
    public void A_symbolic_icon_takes_the_text_color_and_keeps_its_shape()
    {
        byte[] bgra = [0x36, 0x34, 0x2e, 0xFF, 0x36, 0x34, 0x2e, 0x00, 0x10, 0x20, 0x30, 0x80];
        FreedesktopIconSource.Tint(bgra, "#F2E6CC");
        Assert.Equal(new byte[] { 0xCC, 0xE6, 0xF2, 0xFF, 0xCC, 0xE6, 0xF2, 0x00, 0xCC, 0xE6, 0xF2, 0x80 }, bgra);
    }
}
