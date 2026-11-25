using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.Core.State;
using FileCat.Tests;

namespace FileCat.App.Tests;

/// <summary>
/// I645: FileCat's start-up sweeps of its temporary folder. An elevated or root FileCat runs none of them, and the
/// clipboard sweep never goes through a junction or symbolic link (the drag and leftover sweeps' cases are in the Core
/// tests, TemporarySweepLinkTests).
/// </summary>
public sealed class TemporarySweepTests : IDisposable
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    /// <summary>Every item counts as stale, a link made a moment ago included.</summary>
    private static readonly TimeSpan Immediately = TimeSpan.FromMinutes(-1);
    private readonly TemporaryLinkFixture _links = new();

    public void Dispose() => _links.Dispose();

    [AvaloniaFact]
    public async Task An_elevated_or_root_FileCat_runs_none_of_the_sweeps_and_an_ordinary_one_runs_all_three()
    {
        var paths = AppPaths.Resolve(overrideRoot: _links.Dir("state"));
        // Day-old leftovers of each sweep, as earlier ordinary sessions leave them.
        string drag = _links.Dir(paths.TempDirectory, "drag", "20260101-000000-0123abcd");
        _links.Stale(drag, "copy.txt");
        string clipboard = _links.Dir(paths.TempDirectory, "clipboard", "0123456789abcdef0123456789abcdef");
        _links.Stale(clipboard, "pasted.txt");
        string member = _links.File(paths.TempDirectory, "member-0123456789abcdef.tmp");
        File.SetLastWriteTimeUtc(member, DateTime.UtcNow.AddDays(-2));
        string[] leftovers = [drag, clipboard, member];

        using (var elevated = AppServices.CreateForPathsWithStartupAuthority(paths, elevatedOrRoot: true))
            await elevated.StartupSweep;
        Assert.All(leftovers, leftover => Assert.True(TemporaryLinkFixture.Exists(leftover), leftover));

        using (var ordinary = AppServices.CreateForPathsWithStartupAuthority(paths, elevatedOrRoot: false))
            await ordinary.StartupSweep;
        Assert.All(leftovers, leftover => Assert.False(TemporaryLinkFixture.Exists(leftover), leftover));

        // Without the seam the shell decides (high integrity on Windows, root elsewhere): this process sweeps unless it is
        // elevated or root itself.
        _links.Stale(_links.Dir(drag), "copy.txt");
        using var services = AppServices.CreateForPaths(paths);
        await services.StartupSweep;
        Assert.Equal(services.Shell.IsElevated, TemporaryLinkFixture.Exists(drag));
    }

    [Fact]
    public void Without_a_data_folder_the_page_view_of_Windows_and_macOS_starts_no_engine_and_says_why()
    {
        // An elevated or root viewer gives the page view no data folder: WebView2's would be the ordinary account's.
        Assert.SkipWhen(OperatingSystem.IsLinux(), "WebKitGTK keeps nothing on disk, so Linux needs no data folder.");

        Assert.Null(PageEngines.Create(new PlatformHandle(0, "HWND"), dataFolder: null, out string? unavailable));

        Assert.Equal(PageEngines.NoDataFolder, unavailable);
    }

    [Fact]
    public void Clipboard_sweep_removes_a_stale_link_in_place_of_a_staged_folder_itself_and_changes_nothing_it_leads_to()
    {
        string temp = _links.Dir("temp");
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string planted = _links.FolderLink(Path.Combine(_links.Dir(temp, "clipboard"), "planted"), outside);

        ClipboardVirtualStaging.Sweep(temp, Immediately);

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.False(TemporaryLinkFixture.Exists(planted), "The stale link itself is removed.");
    }

    [Fact]
    public void Clipboard_sweep_leaves_a_staging_folder_that_is_itself_a_link_unswept()
    {
        string temp = _links.Dir("temp");
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string clipboard = _links.FolderLink(Path.Combine(temp, "clipboard"), outside);

        ClipboardVirtualStaging.Sweep(temp, Day);

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.True(TemporaryLinkFixture.Exists(clipboard), "A staging folder that is a link is left as it is.");
    }

    [Fact]
    public void Clipboard_sweep_leaves_a_temporary_folder_that_is_itself_a_link_unswept()
    {
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string temp = _links.FolderLink(Path.Combine(_links.Root, "temp"), outside);

        ClipboardVirtualStaging.Sweep(temp, Day);

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.True(TemporaryLinkFixture.Exists(temp));
    }

    [Fact]
    public void Clipboard_sweep_removes_a_stale_pasted_tree_and_only_the_links_inside_it()
    {
        string temp = _links.Dir("temp");
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string staged = _links.Dir(temp, "clipboard", "fedcba9876543210fedcba9876543210");
        string deep = _links.Dir(staged, "folder", "inner folder");
        _links.File(deep, "pasted.txt");
        var inner = new List<string> { _links.FolderLink(Path.Combine(deep, "link"), outside) };
        if (!OperatingSystem.IsWindows()) inner.Add(_links.FileLink(Path.Combine(staged, "file link"), Path.Combine(outside, "keep.txt")));
        Directory.SetCreationTimeUtc(staged, DateTime.UtcNow.AddDays(-2));

        ClipboardVirtualStaging.Sweep(temp, Day);

        Assert.All(inner, link => Assert.False(TemporaryLinkFixture.Exists(link), link));
        Assert.False(TemporaryLinkFixture.Exists(staged), "The stale pasted tree goes.");
        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
    }
}
