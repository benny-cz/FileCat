using FileCat.Core.Platform;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.Core.Tests;

/// <summary>Icons from the desktop's icon theme on Linux (Icon Theme and Shared MIME-info specifications).</summary>
public sealed class FreedesktopIconsTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MakeFifo(string path, uint mode);

    [Fact]
    public void A_stalled_theme_setting_helper_does_not_hold_icon_detection()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The freedesktop theme helper is a Unix desktop command.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-theme-helper-" + Guid.NewGuid().ToString("N"))).FullName;
        string helper = Path.Combine(root, "gsettings");
        string pidFile = Path.Combine(root, "pid");
        try
        {
            File.WriteAllText(helper, "#!/bin/sh\nprintf '%s\\n' \"$$\" > '" + pidFile + "'\nexec /bin/sleep 5\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var clock = Stopwatch.StartNew();
            Assert.Null(FreedesktopIcons.QueryGsettings(helper, "org.gnome.desktop.interface", "icon-theme", 500));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), $"Theme detection waited {clock.Elapsed} for a stalled helper.");
            Assert.True(File.Exists(pidFile), "The owned helper did not reach its sleep control.");
            int pid = int.Parse(File.ReadAllText(pidFile));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_finished_theme_setting_helper_preserves_its_exact_result()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The freedesktop theme helper is a Unix desktop command.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-theme-helper-" + Guid.NewGuid().ToString("N"))).FullName;
        string helper = Path.Combine(root, "gsettings");
        try
        {
            File.WriteAllText(helper, "#!/bin/sh\nprintf \"'Adwaita'\\n\"\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Equal("Adwaita", FreedesktopIcons.QueryGsettings(helper, "org.gnome.desktop.interface", "icon-theme", 500));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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

    [Fact]
    public void A_theme_with_only_symbolic_icons_still_gives_types_their_icons()
    {
        // Release issue I27: adwaita-icon-theme 41 ships text-x-generic only as text-x-generic-symbolic; GTK falls back to
        // the symbolic variant, last, and so does FileCat.
        string root = Path.Combine(Path.GetTempPath(), "filecat-icons-" + Guid.NewGuid().ToString("N")[..8]);
        string dir = Directory.CreateDirectory(Path.Combine(root, "TestTheme", "scalable", "mimetypes")).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "TestTheme", "index.theme"),
                "[Icon Theme]\nName=TestTheme\nDirectories=scalable/mimetypes\n\n[scalable/mimetypes]\nSize=16\nMinSize=8\nMaxSize=512\nType=Scalable\n");
            const string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"><rect width=\"16\" height=\"16\" fill=\"#2e3436\"/></svg>";
            File.WriteAllText(Path.Combine(dir, "text-x-generic-symbolic.svg"), svg);
            File.WriteAllText(Path.Combine(dir, "folder-symbolic.svg"), svg);
            File.WriteAllText(Path.Combine(dir, "folder.svg"), svg);
            var icons = FreedesktopIcons.ForTests("TestTheme", root);
            string? text = icons.Find(["text-plain", "text-x-generic", "unknown"], 16);
            Assert.Equal(Path.Combine(dir, "text-x-generic-symbolic.svg"), Path.GetFullPath(text!));
            Assert.True(FreedesktopIcons.IsSymbolic(text!));
            // A full-color icon is still preferred to a symbolic one.
            string? folder = icons.Find(["folder"], 16);
            Assert.Equal(Path.Combine(dir, "folder.svg"), Path.GetFullPath(folder!));
            Assert.False(FreedesktopIcons.IsSymbolic(folder!));
            Assert.Null(icons.Find(["no-such-icon"], 16));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_theme_index_fifo_does_not_block_icon_lookup()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix theme FIFO control.");
        string root = Directory.CreateTempSubdirectory("filecat-icons-fifo-").FullName;
        try
        {
            string theme = Directory.CreateDirectory(Path.Combine(root, "TestTheme")).FullName;
            Assert.Equal(0, MakeFifo(Path.Combine(theme, "index.theme"), 384));
            var icons = FreedesktopIcons.ForTests("TestTheme", root);
            var clock = Stopwatch.StartNew();
            Assert.Null(icons.Find(["filecat-i459-nonexistent"], 16));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Theme lookup waited {clock.Elapsed}.");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }

    [Fact]
    public void Oversized_theme_line_does_not_materialize_it_or_hide_later_sections()
    {
        string root = Directory.CreateTempSubdirectory("filecat-icons-line-").FullName;
        string dir = Directory.CreateDirectory(Path.Combine(root, "TestTheme", "16x16", "mimetypes")).FullName;
        try
        {
            string icon = Path.Combine(dir, "filecat-i459-control.svg");
            File.WriteAllText(icon, "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"16\" height=\"16\"/>");
            string index = Path.Combine(root, "TestTheme", "index.theme");
            using (var writer = new StreamWriter(index))
            {
                writer.WriteLine(new string('X', 4 * 1024 * 1024));
                writer.WriteLine("[Icon Theme]");
                writer.WriteLine("Name=TestTheme");
                writer.WriteLine("Directories=16x16/mimetypes");
                writer.WriteLine("[16x16/mimetypes]");
                writer.WriteLine("Size=16");
                writer.WriteLine("Type=Fixed");
            }
            var icons = FreedesktopIcons.ForTests("TestTheme", root);
            long before = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(icon.Replace('\\', '/'), icons.Find(["filecat-i459-control"], 16)?.Replace('\\', '/'));
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(allocated < 16 * 1024 * 1024, $"Theme lookup allocated {allocated:N0} bytes.");
            Assert.True(File.Exists(index));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
