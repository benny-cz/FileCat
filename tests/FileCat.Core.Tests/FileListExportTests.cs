using FileCat.Core.FileSystem;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class FileListExportTests
{
    [Fact]
    public async Task Recursive_selection_formats_metadata_without_reading_contents()
    {
        string root = Directory.CreateTempSubdirectory("filecat-list-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "alpha.txt"), "A");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "nested.bin"), "B");
            File.WriteAllText(Path.Combine(root, "ignored.md"), "C");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var place = Location.FileSystem(root);
            var selected = new[]
            {
                new ItemRef(place, "alpha.txt", EntryKind.File),
                new ItemRef(place, "sub", EntryKind.Directory),
            };
            var rule = new FileListRule("My remove list", "{Filename}.{Ext} | {RelativePath} | {Size}",
                Recursive: true, IncludeFolders: false);
            string result = await FileListExport.GenerateAsync(providers, place, selected, rule, CancellationToken.None);
            Assert.Contains("alpha.txt | alpha.txt | 1", result);
            Assert.Contains("nested.bin | sub/nested.bin | 1", result);
            Assert.DoesNotContain("ignored.md", result);
            Assert.DoesNotContain("sub. |", result);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task All_items_includes_folders_without_recursing_and_selected_missing_refuses()
    {
        string root = Directory.CreateTempSubdirectory("filecat-list-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "folder"));
            File.WriteAllText(Path.Combine(root, "file.txt"), "text");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var place = Location.FileSystem(root);
            string all = await FileListExport.GenerateAsync(providers, place, null,
                FileListRule.BuiltIns[0], CancellationToken.None);
            Assert.Contains("folder", all);
            Assert.Contains("file.txt", all);
            Assert.DoesNotContain("folder/", all);
            await Assert.ThrowsAsync<IOException>(() => FileListExport.GenerateAsync(providers, place,
                [new ItemRef(place, "gone.txt", EntryKind.File)], FileListRule.BuiltIns[0], CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Fields_validate_and_escape_control_characters()
    {
        Assert.Contains("Unknown field", FileListExport.Validate(new FileListRule("bad", "{Unknown}")));
        Assert.NotNull(FileListExport.Validate(new FileListRule("empty", "{Name}", IncludeFiles: false, IncludeFolders: false)));
        var entry = new EntryData("a\nb.txt", EntryKind.File, 4);
        Assert.Equal("{a\\nb.txt} a\\nb txt 4", FileListExport.Format("{{{Name}}} {Filename} {Ext} {Size}", entry, "/x", "a\nb.txt"));
        var folder = new EntryData("folder.with.dots", EntryKind.Directory, 0);
        Assert.Equal("folder.with.dots||", FileListExport.Format("{Filename}|{Ext}|{Size}", folder, "/x", "folder.with.dots"));
    }
}
