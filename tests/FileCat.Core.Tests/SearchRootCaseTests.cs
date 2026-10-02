using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>
/// V13 (append): a search root typed in another letter case than the disk's ("c:\projects" for C:\Projects) names the
/// same folders on Windows. Items found through it are the same items as through the disk's own spelling, so appending
/// one search to another lists each file once.
/// </summary>
public sealed class SearchRootCaseTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private static void Search(ResultSet set, string root) =>
        new SearchSession(new SearchQuery { Roots = [root], Names = Selection.Mask.Parse("*.txt"), IncludeDirectories = false }, set).Run(TestContext.Current.CancellationToken);

    [Fact]
    public void A_path_is_spelled_as_the_disk_spells_it()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Letter case names the same folder on Windows only.");
        string app = Directory.CreateDirectory(Path.Combine(_dir.Path, "Projects", "App")).FullName;
        File.WriteAllText(Path.Combine(app, "ReadMe.txt"), "r");
        // (The temporary folder itself may be written otherwise than the disk has it: "C:\WINDOWS\Temp" for C:\Windows\Temp.)
        string lower = Path.Combine(_dir.Path, "projects", "APP", "readme.TXT");
        string spelled = FileSystem.PathUtil.WithDiskCase(lower);
        Assert.EndsWith(Path.Combine("Projects", "App", "ReadMe.txt"), spelled, StringComparison.Ordinal);
        Assert.Equal(lower, spelled, ignoreCase: true);
        // Past the first name that is not there, the rest stays as written.
        Assert.EndsWith(Path.Combine("Projects", "App", "missing", "MoRe"),
            FileSystem.PathUtil.WithDiskCase(Path.Combine(_dir.Path, "projects", "app", "missing", "MoRe")), StringComparison.Ordinal);
        // The drive letter in upper case.
        string drive = Path.GetPathRoot(app)!;
        Assert.StartsWith(drive.ToUpperInvariant(), FileSystem.PathUtil.WithDiskCase(char.ToLowerInvariant(app[0]) + app[1..]), StringComparison.Ordinal);
    }

    [Fact]
    public void Appending_a_search_from_a_root_typed_in_another_case_lists_each_file_once()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Letter case names the same folder on Windows; elsewhere it names another.");
        string projects = Directory.CreateDirectory(Path.Combine(_dir.Path, "Projects", "App")).FullName;
        File.WriteAllText(Path.Combine(projects, "readme.txt"), "r");
        File.WriteAllText(Path.Combine(projects, "notes.txt"), "n");

        var set = new ResultSet("t", "t", "t");
        Search(set, Path.Combine(_dir.Path, "Projects"));
        Assert.Equal(2, set.Snapshot().Count);
        // Appended: the same folder, the root typed in lower case.
        var more = new ResultSet("m", "m", "m");
        Search(more, Path.Combine(_dir.Path, "projects", "app"));
        set.AddRange(more.Snapshot());
        Assert.Equal(2, set.Snapshot().Count);
        Assert.All(set.Snapshot(), i => Assert.Contains(Path.Combine("Projects", "App"), i.Item.Parent.Path, StringComparison.Ordinal));
    }
}
