using System.IO.Compression;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ZipNameBudgetTests
{
    [Fact]
    public void Names_past_the_budget_list_the_first_entries_with_a_warning()
    {
        // Review finding P6 of f134ba37: member names past the 64 Mi-character budget refused the whole archive, where
        // entries past the entry limit were only left unlisted.
        using var root = new TempDir();
        string path = System.IO.Path.Combine(root.Path, "names.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var name in new[] { "a/one.txt", "b/two.txt", "c/three.txt" })
                using (var writer = new StreamWriter(zip.CreateEntry(name).Open())) writer.Write(name);
        using var index = ZipIndex.Build(path, null, maxNameChars: 20);
        Assert.Contains("only the first entries are listed", index.Warning);
        Assert.Contains(index.Children[string.Empty], node => node.Name == "b");
        Assert.DoesNotContain(index.Children[string.Empty], node => node.Name == "c");
    }
}
