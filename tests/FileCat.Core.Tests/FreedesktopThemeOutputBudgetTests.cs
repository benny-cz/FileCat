using FileCat.Core.Platform;
using System.Diagnostics;

namespace FileCat.Core.Tests;

public sealed class FreedesktopThemeOutputBudgetTests
{
    [Fact]
    public void An_exited_helper_cannot_wait_indefinitely_for_a_descendant_holding_stdout()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The freedesktop theme helper is a Unix desktop command.");
        string root = Directory.CreateTempSubdirectory("filecat-owned-theme-open-pipe-").FullName;
        string helper = Path.Combine(root, "gsettings");
        string pidFile = helper + ".pid";
        try
        {
            File.WriteAllText(helper,
                "#!/bin/sh\n/bin/sleep 5 &\nprintf '%s\\n' \"$!\" > \"$0.pid\"\nprintf \"'Adwaita'\\n\"\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var clock = Stopwatch.StartNew();
            Assert.Null(FreedesktopIcons.QueryGsettings(helper, "org.gnome.desktop.interface", "icon-theme", 2000));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4),
                $"Theme detection waited {clock.Elapsed} for a descendant-held output pipe.");
            Assert.True(File.Exists(pidFile), "The owned descendant did not start.");
        }
        finally
        {
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile), out int pid))
            {
                try
                {
                    using var child = Process.GetProcessById(pid);
                    if (!child.HasExited) { child.Kill(); child.WaitForExit(2000); }
                }
                catch (ArgumentException) { }
            }
            Directory.Delete(root, recursive: true);
        }
    }

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
