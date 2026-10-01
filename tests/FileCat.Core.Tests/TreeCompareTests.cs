using FileCat.Core.Compare;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class TreeCompareTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ProviderRegistry _providers = new();

    public TreeCompareTests() => _providers.Register(new LocalFileSystemProvider());

    public void Dispose() => _dir.Dispose();

    private string File(string relative, string content, DateTime? time = null)
    {
        string path = _dir.File(relative, content);
        System.IO.File.SetLastWriteTimeUtc(path, time ?? new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        return path;
    }

    private TreeCompareResult Compare(CompareCriteria criteria) => TreeCompare.Compare(_providers,
        Location.FileSystem(Path.Combine(_dir.Path, "L")), Location.FileSystem(Path.Combine(_dir.Path, "R")), criteria, TimeSpan.FromSeconds(2),
        caseInsensitiveNames: OperatingSystem.IsWindows(), TestContext.Current.CancellationToken);

    [Fact]
    public void Every_kind_of_difference_is_found_recursively_and_nothing_undecided_counts_as_same()
    {
        File("L/same.txt", "same");
        File("R/same.txt", "same");
        File("L/size.txt", "short");
        File("R/size.txt", "much longer");
        File("L/left.txt", "only here");
        File("R/right.txt", "only there");
        File("L/sub/newer.txt", "v2", new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        File("R/sub/newer.txt", "v1", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File("L/sub/content.txt", "AAAA");
        File("R/sub/content.txt", "BBBB");
        File("L/sub/deep/tolerance.txt", "t", new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        File("R/sub/deep/tolerance.txt", "t", new DateTime(2025, 1, 1, 12, 0, 1, DateTimeKind.Utc));
        File("L/folderonly/inside.txt", "x");
        File("L/kind", "a file");
        _dir.Dir("R/kind");

        var byName = Compare(CompareCriteria.Size | CompareCriteria.Time).Entries.ToDictionary(e => e.RelativePath);
        Assert.Equal(TreeDiffKind.Same, byName["same.txt"].Kind);
        Assert.Equal(TreeDiffKind.Different, byName["size.txt"].Kind);
        Assert.Equal("Sizes 5 and 11 bytes", byName["size.txt"].Detail);
        Assert.Equal(TreeDiffKind.LeftOnly, byName["left.txt"].Kind);
        Assert.Equal(TreeDiffKind.RightOnly, byName["right.txt"].Kind);
        Assert.Equal(TreeDiffKind.LeftNewer, byName["sub/newer.txt"].Kind);
        Assert.Equal(TreeDiffKind.Same, byName["sub/content.txt"].Kind); // same size and time: only a content check tells
        Assert.Equal(TreeDiffKind.Same, byName["sub/deep/tolerance.txt"].Kind);
        Assert.Equal(TreeDiffKind.LeftOnly, byName["folderonly"].Kind);
        Assert.True(byName["folderonly"].IsFolder);
        Assert.False(byName.ContainsKey("folderonly/inside.txt")); // a one-sided folder is reported once, with what it holds
        Assert.Equal("Holds 1 file, 1 byte", byName["folderonly"].Detail);
        Assert.Equal((1L, 0L, 1L), (byName["folderonly"].Contents!.Files, byName["folderonly"].Contents!.Folders, byName["folderonly"].Contents!.Bytes));
        Assert.Equal(TreeDiffKind.TypeMismatch, byName["kind"].Kind);

        var withContent = Compare(CompareCriteria.Size | CompareCriteria.Time | CompareCriteria.Content).Entries.ToDictionary(e => e.RelativePath);
        Assert.Equal(TreeDiffKind.Different, withContent["sub/content.txt"].Kind);
        Assert.Equal("Same size, different content", withContent["sub/content.txt"].Detail);
        Assert.Equal(TreeDiffKind.Same, withContent["same.txt"].Kind);
    }

    [Fact]
    public void What_a_folder_holds_reads_the_same_until_something_in_it_changes()
    {
        File("F/a.txt", "aaa");
        File("F/sub/b.txt", "bb");
        string folder = Path.Combine(_dir.Path, "F");
        var ct = TestContext.Current.CancellationToken;
        var first = FolderContents.Read(folder, ct)!;
        Assert.Equal(first, FolderContents.Read(folder, ct));
        Assert.Equal("2 files and 1 folder, 5 bytes", first.Describe());
        System.IO.File.Move(Path.Combine(folder, "sub", "b.txt"), Path.Combine(folder, "sub", "c.txt")); // same size and time, another name
        var renamed = FolderContents.Read(folder, ct)!;
        Assert.NotEqual(first, renamed);
        _dir.Dir("F/sub/empty");
        Assert.NotEqual(renamed, FolderContents.Read(folder, ct));
        Assert.Null(FolderContents.Read(folder, ct, limit: 3)); // it holds four items: a reading that stops vouches for nothing
        Assert.Null(FolderContents.Read(Path.Combine(folder, "missing"), ct));
    }

    [Fact]
    public void A_missing_root_is_unknown_not_equal()
    {
        _dir.Dir("L");
        var result = Compare(CompareCriteria.Size);
        var entry = Assert.Single(result.Entries);
        Assert.Equal(TreeDiffKind.Unknown, entry.Kind);
        Assert.StartsWith("The folder could not be read", entry.Detail);
    }
}
