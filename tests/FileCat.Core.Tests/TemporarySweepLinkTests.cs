using FileCat.Core.Operations;
using FileCat.Core.State;
using FileCat.Tests;

namespace FileCat.Core.Tests;

/// <summary>
/// I645: the start-up sweeps of FileCat's temporary folder never go through a junction or symbolic link. What a link
/// leads to keeps its entries, attributes, modes and times; a stale link where a sweep removes things is removed itself;
/// a temporary folder, or a staging folder in it, that is itself a link is not swept. The clipboard sweep's cases are in
/// the App tests (TemporarySweepTests).
/// </summary>
public sealed class TemporarySweepLinkTests : IDisposable
{
    private static readonly TimeSpan Day = TimeSpan.FromDays(1);
    /// <summary>Every item counts as stale, a link made a moment ago included.</summary>
    private static readonly TimeSpan Immediately = TimeSpan.FromMinutes(-1);
    private readonly TemporaryLinkFixture _links = new();

    public void Dispose() => _links.Dispose();

    [Fact]
    public void Drag_sweep_removes_a_stale_link_in_place_of_a_staged_folder_itself_and_changes_nothing_it_leads_to()
    {
        string temp = _links.Dir("temp");
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string planted = _links.FolderLink(Path.Combine(_links.Dir(temp, "drag"), "planted"), outside);

        DragStaging.Sweep(temp, Immediately);

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.False(TemporaryLinkFixture.Exists(planted), "The stale link itself is removed.");
    }

    [Fact]
    public void Drag_sweep_leaves_a_staging_folder_that_is_itself_a_link_unswept()
    {
        string temp = _links.Dir("temp");
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string drag = _links.FolderLink(Path.Combine(temp, "drag"), outside);

        DragStaging.Sweep(temp, Day);

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.True(TemporaryLinkFixture.Exists(drag), "A staging folder that is a link is left as it is.");
    }

    [Fact]
    public void Drag_sweep_leaves_a_temporary_folder_that_is_itself_a_link_unswept()
    {
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string temp = _links.FolderLink(Path.Combine(_links.Root, "temp"), outside);

        DragStaging.Sweep(temp, Day);

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.True(TemporaryLinkFixture.Exists(temp));
    }

    [Fact]
    public void Drag_sweep_removes_a_stale_staged_folder_with_its_read_only_copy_and_only_the_link_inside_it()
    {
        string temp = _links.Dir("temp");
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        string staged = _links.Dir(temp, "drag", "20260101-000000-89abcdef");
        File.SetAttributes(_links.File(staged, "copy.txt"), FileAttributes.ReadOnly); // as Stage leaves its copies
        string inner = _links.FolderLink(Path.Combine(staged, "inner"), outside);
        Directory.SetCreationTimeUtc(staged, DateTime.UtcNow.AddDays(-2));

        DragStaging.Sweep(temp, Day);

        Assert.False(TemporaryLinkFixture.Exists(inner));
        Assert.False(TemporaryLinkFixture.Exists(staged), "The stale staged folder goes, its read-only copy included.");
        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
    }

    [Fact]
    public void Leftover_sweep_leaves_a_temporary_folder_that_is_itself_a_link_unswept()
    {
        var paths = AppPaths.Resolve(overrideRoot: _links.Dir("state"));
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        Directory.Delete(paths.TempDirectory);
        _links.FolderLink(paths.TempDirectory, outside);

        Assert.Equal(0, paths.SweepTemporaryLeftovers(Day));

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.True(TemporaryLinkFixture.Exists(paths.TempDirectory));
    }

    [Fact]
    public void Leftover_sweep_removes_a_stale_link_named_as_a_leftover_itself_and_changes_nothing_it_leads_to()
    {
        var paths = AppPaths.Resolve(overrideRoot: _links.Dir("state"));
        string outside = _links.Outside("outside");
        var before = TemporaryLinkFixture.Snapshot(outside);
        var links = new List<string> { _links.FolderLink(Path.Combine(paths.TempDirectory, "member-00000000000000aa.tmp"), outside) };
        if (!OperatingSystem.IsWindows())
        {
            links.Add(_links.FileLink(Path.Combine(paths.TempDirectory, "nested-00000000000000bb.zip"),
                Path.Combine(outside, "member-0123456789abcdef.tmp")));
            links.Add(_links.FileLink(Path.Combine(paths.TempDirectory, "member-00000000000000cc.tmp"), Path.Combine(outside, "keep.txt")));
        }

        paths.SweepTemporaryLeftovers(Immediately);

        Assert.Equal(before, TemporaryLinkFixture.Snapshot(outside));
        Assert.All(links, link => Assert.False(TemporaryLinkFixture.Exists(link), link));
    }
}
