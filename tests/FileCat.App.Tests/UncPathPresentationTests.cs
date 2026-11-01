using FileCat.App.ViewModels;
using FileCat.Core.Commands;

namespace FileCat.App.Tests;

public sealed class UncPathPresentationTests
{
    [Fact]
    public void Local_paths_are_not_claimed_as_unc_and_real_network_paths_remain()
    {
        Assert.Equal(OperatingSystem.IsWindows(), MainMenuModel.IsPlatformApplicable(CommandIds.CopyUncPaths));
        Assert.True(MainMenuModel.IsPlatformApplicable(CommandIds.CopyPaths));

        var actual = MainViewModel.ActualUncPaths(
            [@"C:\local\note.txt", @"Z:\mapped\review.txt", @"\\server\share\direct.txt", null],
            path => path.StartsWith("Z:", StringComparison.Ordinal) ? @"\\server\share\mapped\review.txt" : path);

        Assert.Equal([@"\\server\share\mapped\review.txt", @"\\server\share\direct.txt"], actual);
        Assert.Empty(MainViewModel.ActualUncPaths(["/Users/benny/local.txt"], path => path));
    }
}
