using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class ListingModelTests : IDisposable
{
    private readonly TestDispatcher _ui = new();
    private readonly DeviceIoScheduler _io = new();
    private readonly ProviderRegistry _providers = new();
    private readonly TempDir _dir = new();

    public ListingModelTests()
    {
        _providers.Register(new LocalFileSystemProvider());
        _providers.Register(new ComputerProvider());
    }

    public void Dispose()
    {
        _io.Dispose();
        _ui.Dispose();
        _dir.Dispose();
    }

    private async Task<ListingModel> LoadAsync(string path, string? focus = null)
    {
        var model = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => model.Load(Location.FileSystem(path), focus));
        await _ui.WaitUntilAsync(() => model.State == ListingState.Complete);
        return model;
    }

    private Task<string[]> VisibleNames(ListingModel m) =>
        _ui.InvokeAsync(() => Enumerable.Range(0, m.VisibleCount).Select(i => m.GetVisible(i).Name).ToArray());

    [Fact]
    public async Task Loads_sorted_with_parent_row_and_directories_first()
    {
        _dir.File("b.txt");
        _dir.File("A.txt");
        _dir.Dir("zdir");
        _dir.Dir("adir");
        var m = await LoadAsync(_dir.Path);
        Assert.Equal(["..", "adir", "zdir", "A.txt", "b.txt"], await VisibleNames(m));
        Assert.Equal(4, await _ui.InvokeAsync(() => m.TotalCount));
    }

    [Fact]
    public async Task Missing_directory_fails_with_plain_error_not_empty_listing()
    {
        var m = await _ui.InvokeAsync(() => new ListingModel(_providers, _io, _ui));
        await _ui.InvokeAsync(() => m.Load(Location.FileSystem(Path.Combine(_dir.Path, "missing"))));
        await _ui.WaitUntilAsync(() => m.State is ListingState.Failed or ListingState.Complete);
        Assert.Equal(ListingState.Failed, await _ui.InvokeAsync(() => m.State));
        Assert.NotNull(await _ui.InvokeAsync(() => m.Error));
    }

    [Fact]
    public async Task Focus_name_is_applied_when_it_arrives()
    {
        for (int i = 0; i < 50; i++) _dir.File($"f{i:000}.txt");
        var m = await LoadAsync(_dir.Path, focus: "f042.txt");
        Assert.Equal("f042.txt", await _ui.InvokeAsync(() => m.TryGetFocused(out var e) ? e.Name : null));
    }

    [Fact]
    public async Task Filter_keeps_marks_and_reports_hidden_marked_items()
    {
        _dir.File("a.txt");
        _dir.File("b.log");
        _dir.File("c.txt");
        var m = await LoadAsync(_dir.Path);
        await _ui.InvokeAsync(() => m.MarkAll(true));
        Assert.Equal(3, await _ui.InvokeAsync(() => m.MarkedCount));
        await _ui.InvokeAsync(() => m.Filter = Mask.Parse("*.txt"));
        await _ui.WaitUntilAsync(() => m.VisibleCount == 3);
        var stats = await _ui.InvokeAsync(() => m.GetMarkStats());
        Assert.Equal(3, stats.Count);
        Assert.Equal(1, stats.HiddenByFilter);
        var visibleOnly = await _ui.InvokeAsync(() => m.GetSelection(includeHiddenMarks: false));
        Assert.Equal(2, visibleOnly.Count);
        await _ui.InvokeAsync(() => m.UnmarkHidden());
        Assert.Equal(2, await _ui.InvokeAsync(() => m.MarkedCount));
    }

    [Fact]
    public async Task Refresh_transfers_marks_by_identity_and_never_substitutes()
    {
        _dir.File("a.txt");
        var b = _dir.File("b.txt");
        _dir.File("c.txt");
        var m = await LoadAsync(_dir.Path);
        await _ui.InvokeAsync(() => m.MarkNames(["b.txt", "c.txt"], true));
        File.Delete(b);
        _dir.File("aa.txt");
        await _ui.InvokeAsync(() => m.Refresh());
        await _ui.WaitUntilAsync(() => !m.IsRefreshing && m.TotalCount == 3);
        var marked = await _ui.InvokeAsync(() => m.GetSelection().Select(r => r.Name).ToArray());
        Assert.Equal(["c.txt"], marked);
    }

    [Fact]
    public async Task Selection_falls_back_to_focus_and_excludes_parent_row()
    {
        _dir.File("only.txt");
        var m = await LoadAsync(_dir.Path);
        await _ui.InvokeAsync(() => m.SetFocus(0));
        Assert.Empty(await _ui.InvokeAsync(() => m.GetSelection()));
        await _ui.InvokeAsync(() => m.SetFocus(1));
        var sel = await _ui.InvokeAsync(() => m.GetSelection());
        Assert.Single(sel);
        Assert.Equal("only.txt", sel[0].Name);
        Assert.Equal(Path.Combine(_dir.Path, "only.txt"), sel[0].FileSystemPath);
    }

    [Fact]
    public async Task Mask_selection_and_invert_and_restore()
    {
        _dir.File("a.txt");
        _dir.File("b.txt");
        _dir.File("c.log");
        _dir.Dir("sub.txt");
        var m = await LoadAsync(_dir.Path);
        int n = await _ui.InvokeAsync(() => m.MarkByMask(Mask.Parse("*.txt"), true, includeDirectories: false));
        Assert.Equal(2, n);
        await _ui.InvokeAsync(() => m.InvertMarks(includeDirectories: false));
        Assert.Equal(["c.log"], await _ui.InvokeAsync(() => m.GetSelection().Select(s => s.Name).ToArray()));
        await _ui.InvokeAsync(() => { m.LastOperationNames = ["a.txt", "b.txt"]; m.UnmarkEverything(); m.RestoreSelection(); });
        Assert.Equal(2, await _ui.InvokeAsync(() => m.MarkedCount));
    }

    [Fact]
    public async Task Hidden_items_can_be_hidden()
    {
        _dir.File("visible.txt");
        var h = _dir.File("hidden.txt");
        File.SetAttributes(h, FileAttributes.Hidden);
        var m = await LoadAsync(_dir.Path);
        if (!OperatingSystem.IsWindows()) return;
        Assert.Contains("hidden.txt", await VisibleNames(m));
        await _ui.InvokeAsync(() => m.ShowHidden = false);
        await _ui.WaitUntilAsync(() => m.VisibleCount == 2);
        Assert.DoesNotContain("hidden.txt", await VisibleNames(m));
    }

    [Fact]
    public async Task Large_listing_streams_and_sorts()
    {
        for (int i = 0; i < 3000; i++) File.WriteAllText(Path.Combine(_dir.Path, $"n{3000 - i}.dat"), "");
        var m = await LoadAsync(_dir.Path);
        var names = await VisibleNames(m);
        Assert.Equal(3001, names.Length);
        Assert.Equal("n1.dat", names[1]);
        Assert.Equal("n3000.dat", names[^1]);
        await _ui.InvokeAsync(() => m.Sort = new SortSpec(SortField.Name, Descending: true));
        await _ui.WaitUntilAsync(() => m.GetVisible(1).Name == "n3000.dat");
    }
}
