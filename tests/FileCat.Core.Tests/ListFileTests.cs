using System.Text;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>List files (plan §19.1, Total Commander's LOADLIST; --list): paths, one per line, opened as a result set.</summary>
public sealed class ListFileTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-8 with BOM")]
    [InlineData("utf-16")]
    public void A_list_opens_what_it_names_relative_to_itself_and_leaves_network_paths_untouched(string encoding)
    {
        string data = _dir.Dir("data");
        File.WriteAllText(Path.Combine(data, "a.txt"), "a");
        Directory.CreateDirectory(Path.Combine(data, "sub"));
        File.WriteAllText(Path.Combine(data, "sub", "b ü.txt"), "b");
        string lists = _dir.Dir("lists");
        File.WriteAllText(Path.Combine(lists, "near.txt"), "n");
        string[] lines =
        [
            Path.Combine(data, "a.txt"),
            "",
            Path.Combine(data, "sub") + Path.DirectorySeparatorChar,
            Path.Combine(data, "sub", "b ü.txt"),
            "near.txt", // relative: from the list's own folder
            Path.Combine(data, "gone.txt"),
            // Network paths are left out without asking the server anything (the names cannot resolve either way).
            @"\\filecat-test-server.invalid\share\x.txt",
            "//filecat-test-server.invalid/share/y.txt",
            @"\\?\UNC\filecat-test-server.invalid\share\z.txt",
            Path.Combine(data, "a.txt"), // twice: one member
        ];
        string list = Path.Combine(lists, "list.lst");
        string text = string.Join("\r\n", lines) + "\r\n";
        File.WriteAllText(list, text, encoding switch
        {
            "utf-8" => new UTF8Encoding(false),
            "utf-8 with BOM" => new UTF8Encoding(true),
            _ => Encoding.Unicode,
        });

        var set = new ResultSet("list-test", "List: list.lst", "test");
        var outcome = ListFile.Load(list, set, TestContext.Current.CancellationToken);

        Assert.Equal((4, 1, 3, false), (outcome.Added, outcome.Missing, outcome.Network, outcome.Truncated));
        var members = set.Snapshot().Select(m => m.Item).ToList();
        Assert.Equal(["a.txt", "b ü.txt", "near.txt", "sub"], members.Select(m => m.Name).Order(StringComparer.Ordinal));
        Assert.Equal(EntryKind.Directory, members.Single(m => m.Name == "sub").Kind);
        Assert.Equal(Path.Combine(lists, "near.txt"), members.Single(m => m.Name == "near.txt").FileSystemPath);
    }

    [Fact]
    public void A_missing_or_oversized_list_is_refused()
    {
        var set = new ResultSet("list-test", "List", "test");
        Assert.Throws<FileNotFoundException>(() => ListFile.Load(Path.Combine(_dir.Path, "none.lst"), set, TestContext.Current.CancellationToken));
        string big = Path.Combine(_dir.Path, "big.lst");
        using (var file = File.Create(big)) file.SetLength(ListFile.MaxBytes + 1);
        Assert.Throws<IOException>(() => ListFile.Load(big, set, TestContext.Current.CancellationToken));
        Assert.Equal(0, set.Count);
    }
}
