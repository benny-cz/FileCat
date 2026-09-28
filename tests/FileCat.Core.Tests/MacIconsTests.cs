using FileCat.Core.Platform;

namespace FileCat.Core.Tests;

/// <summary>Finder's icons through NSWorkspace, drawn by Core Graphics (runs on the macOS CI runner).</summary>
public sealed class MacIconsTests
{
    [Fact]
    public void Types_folders_and_the_home_folder_draw_as_Finder_shows_them()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("NSWorkspace exists only on macOS.");
        static void Visible(bool drawn, int w, int h, byte[] bgra, int size)
        {
            Assert.True(drawn);
            Assert.Equal((size, size), (w, h));
            Assert.Contains(bgra.Where((_, i) => i % 4 == 3), a => a > 0);
        }
        Visible(MacIcons.TryTypeIcon("txt", 32, out int w, out int h, out var bgra), w, h, bgra, 32);
        Visible(MacIcons.TryTypeIcon("public.folder", 16, out w, out h, out bgra), w, h, bgra, 16);
        Visible(MacIcons.TryPathIcon(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 24, out w, out h, out bgra), w, h, bgra, 24);
        Visible(MacIcons.TryPathIcon("/Applications", 32, out w, out h, out bgra), w, h, bgra, 32);
    }
}
