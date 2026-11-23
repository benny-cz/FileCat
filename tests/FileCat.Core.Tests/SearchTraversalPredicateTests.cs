using FileCat.Core.Search;

namespace FileCat.Core.Tests;

/// <summary>
/// F-FIND: attribute predicates filter results and never change traversal. With either polarity of the Hidden predicate,
/// the results equal the unfiltered search's results filtered by that predicate. This includes a match inside a folder
/// that itself fails the predicate.
/// </summary>
public sealed class SearchTraversalPredicateTests
{
    // A leading dot hides an item on Unix; Windows needs the attribute as well.
    private static void Hide(string path)
    {
        if (OperatingSystem.IsWindows()) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }

    private static List<string> Run(SearchQuery query)
    {
        var set = new ResultSet("traversal", "traversal", "test");
        new SearchSession(query, set).Run(TestContext.Current.CancellationToken);
        Assert.True(set.IsComplete, string.Join("; ", set.Issues));
        return set.Snapshot().Select(s => Path.Join(s.Item.Parent.Path, s.Item.Name)).Order(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void Hidden_predicates_in_either_polarity_filter_results_without_changing_which_folders_are_searched()
    {
        using var temp = new TempDir();
        string hiddenFolder = Directory.CreateDirectory(Path.Join(temp.Path, ".hidden-folder")).FullName;
        string plainFolder = Directory.CreateDirectory(Path.Join(temp.Path, "plain-folder")).FullName;
        string hiddenInside = Path.Join(plainFolder, ".hidden-inside.txt");
        File.WriteAllText(Path.Join(hiddenFolder, "visible-inside.txt"), "v");
        File.WriteAllText(hiddenInside, "h");
        File.WriteAllText(Path.Join(temp.Path, "top.txt"), "t");
        Hide(hiddenFolder);
        Hide(hiddenInside);
        SearchQuery Query(FileAttributes set = 0, FileAttributes clear = 0) =>
            new() { Roots = [temp.Path], IncludeDirectories = false, AttributesSet = set, AttributesClear = clear };
        static bool IsHidden(string path) => (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
        static string Name(string path) => Path.GetFileName(path)!;

        var all = Run(Query());
        Assert.Equal([".hidden-inside.txt", "top.txt", "visible-inside.txt"], all.Select(Name).Order(StringComparer.Ordinal).ToArray());
        var notHidden = Run(Query(clear: FileAttributes.Hidden));
        Assert.Equal(all.Where(path => !IsHidden(path)).ToList(), notHidden);
        Assert.Contains("visible-inside.txt", notHidden.Select(Name)); // found inside a hidden folder
        var hidden = Run(Query(set: FileAttributes.Hidden));
        Assert.Equal(all.Where(IsHidden).ToList(), hidden);
        Assert.Equal([".hidden-inside.txt"], hidden.Select(Name).ToArray()); // found inside an ordinary folder
    }
}
