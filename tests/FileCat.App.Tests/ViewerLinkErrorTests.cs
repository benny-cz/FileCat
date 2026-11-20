using FileCat.App.ViewModels;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class ViewerLinkErrorTests
{
    [Fact]
    public void Missing_link_target_is_distinguished_from_a_missing_ordinary_file()
    {
        var parent = Location.FileSystem(Path.GetTempPath());
        var link = new ItemRef(parent, "alias.txt", EntryKind.File) { Flags = EntryFlags.Link };
        var file = new ItemRef(parent, "ordinary.txt", EntryKind.File);
        var missing = new FileNotFoundException();

        Assert.Equal("The link, its target, or its folder no longer exists. Check the link target in Properties.",
            MainViewModel.DescribeViewError(link, missing));
        Assert.Equal("The item no longer exists or its folder was removed.",
            MainViewModel.DescribeViewError(file, missing));
        Assert.Equal("canceled.", MainViewModel.DescribeViewError(link, new OperationCanceledException()));
    }
}
