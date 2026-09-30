namespace FileCat.Platform.Windows.Tests;

/// <summary>The Shell's stock icons FileCat draws for types without an icon of their own.</summary>
public sealed class WindowsIconsTests
{
    [Fact]
    public void Files_without_a_program_and_programs_have_stock_icons_that_draw()
    {
        // A name without an extension is Explorer's blank page, never another type's icon (".bin" is VLC's on many computers).
        foreach (int id in new[] { WindowsIcons.StockDocumentNoAssociation, WindowsIcons.StockApplication })
        {
            var location = WindowsIcons.StockLocation(id);
            Assert.NotNull(location);
            Assert.True(WindowsIcons.TryExtract(location.Value, 16, out int width, out int height, out var bgra), $"stock icon {id} did not draw");
            Assert.True(width > 0 && height > 0 && bgra.Length == width * height * 4);
        }
    }
}
