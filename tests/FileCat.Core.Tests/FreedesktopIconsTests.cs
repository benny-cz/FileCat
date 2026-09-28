using FileCat.Core.Platform;

namespace FileCat.Core.Tests;

/// <summary>Icons from the desktop's icon theme on Linux (Icon Theme and Shared MIME-info specifications).</summary>
public sealed class FreedesktopIconsTests
{
    [Fact]
    public void Types_folders_and_drives_find_their_theme_icons_and_draw()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) Assert.Skip("Freedesktop icon themes are a Linux desktop matter.");
        var icons = FreedesktopIcons.TryCreate();
        if (icons is null || !File.Exists("/usr/share/mime/globs2")) Assert.Skip("No icon themes or MIME database on this machine.");
        Assert.Equal("text/plain", icons.MimeType("notes.txt"));
        Assert.Equal("image/png", icons.MimeType("Photo.PNG"));
        Assert.Contains("x-compressed-tar", icons.MimeType("backup.tar.gz"));
        var names = icons.NamesForFile("notes.txt", executable: false);
        Assert.Equal("text-plain", names[0]);
        Assert.Contains("text-x-generic", names);
        Assert.Equal("user-home", icons.SpecialFolder(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));

        string? file = icons.Find(names, 16);
        if (file is null) Assert.Skip($"The icon theme {icons.ThemeName} has no text icon here.");
        Assert.True(file.EndsWith(".png", StringComparison.Ordinal) || file.EndsWith(".svg", StringComparison.Ordinal));
        Assert.NotNull(icons.Find(["folder"], 24));
        if (!FreedesktopIcons.TryRender(file, 16, out int w, out int h, out var bgra)) Assert.Skip("gdk-pixbuf is not installed.");
        Assert.Equal((16, 16), (w, h));
        Assert.Contains(bgra.Where((_, i) => i % 4 == 3), a => a > 0);
    }
}
