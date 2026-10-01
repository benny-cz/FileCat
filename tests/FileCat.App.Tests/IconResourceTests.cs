using FileCat.App.Services;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Shell;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan I16: an icon that a user's file names (desktop.ini, a shortcut) is read by the restricted helper only
/// from this computer, and a path elsewhere is not touched at all, not even for its time.
/// </summary>
public sealed class IconResourceTests
{
    [Fact]
    public void An_icon_named_on_a_network_path_is_refused_before_it_is_touched()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Icon resources are read on Windows.");
        static string? Policy(IconLocation location) => ShellPreviewPolicy.IconResourceRefusal(location, allowNetworkAndRemovable: false);
        // 203.0.113.9 is a documentation address: reading a time there takes seconds to fail; refusing takes none.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(NativeIconSource.ResourceTime(new IconLocation(@"\\203.0.113.9\share\folder.ico", 0), Policy));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Took {clock.Elapsed}: the network path was tried.");
        string local = Path.Combine(Environment.SystemDirectory, "imageres.dll");
        Assert.Equal(File.GetLastWriteTimeUtc(local).Ticks, NativeIconSource.ResourceTime(new IconLocation(local, 0), Policy));
    }
}
