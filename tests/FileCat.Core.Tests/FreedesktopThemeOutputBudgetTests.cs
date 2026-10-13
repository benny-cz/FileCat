using FileCat.Core.Platform;

namespace FileCat.Core.Tests;

public sealed class FreedesktopThemeOutputBudgetTests
{
    [Fact]
    public void An_exited_theme_helper_cannot_publish_oversized_descendant_output()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The freedesktop theme helper is a Unix desktop command.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-theme-output-").FullName;
        string helper = Path.Combine(root, "gsettings");
        byte[] bulk = Enumerable.Repeat((byte)'x', 128 * 1024).ToArray();
        try
        {
            File.WriteAllBytes(helper + ".bulk", bulk);
            File.WriteAllText(helper,
                "#!/bin/sh\n(: > \"$0.started\"; /bin/cat \"$0.bulk\"; printf \"'Adwaita'\\n\") &\nexit 0\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Null(FreedesktopIcons.QueryGsettings(helper, "org.gnome.desktop.interface", "icon-theme", 2000));
            Assert.True(File.Exists(helper + ".started"), "The owned descendant did not start its output control.");
            Assert.Equal(bulk, File.ReadAllBytes(helper + ".bulk"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Assert.False(Directory.Exists(root));
        }
    }
}
