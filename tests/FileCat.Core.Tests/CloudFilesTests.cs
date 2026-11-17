using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// Cloud providers' files (OneDrive, Dropbox, iCloud Drive and every other Cloud Files provider) told apart by their
/// attributes alone, as measured in the owner's three sync roots: online-only files carry RECALL_ON_DATA_ACCESS, kept
/// ones PINNED, folders asked to free up space UNPINNED, and the rest are plain or placeholder files on this computer.
/// </summary>
public sealed class CloudFilesTests
{
    [Fact]
    public void Listing_hints_keep_pin_intent_and_residency_separate()
    {
        var pinned = CloudFiles.Observe(CloudFiles.Pinned, true);
        Assert.Equal(CloudState.Managed, pinned.Indicator);
        Assert.Equal("keep locally", pinned.PinPolicy);
        Assert.Equal("not established", pinned.LocalContent);
        Assert.Equal("not inspected", pinned.Synchronization);
        var unpinned = CloudFiles.Observe(CloudFiles.Unpinned, true);
        Assert.Equal(CloudState.Managed, unpinned.Indicator);
        Assert.Equal("allow removal", unpinned.PinPolicy);
        Assert.Equal("not established", unpinned.LocalContent);
        var partial = CloudFiles.Observe(CloudFiles.Pinned | CloudFiles.RecallOnDataAccess, true);
        Assert.Equal(CloudState.RecallPossible, partial.Indicator);
        Assert.Equal("keep locally", partial.PinPolicy);
        Assert.Equal("not fully present", partial.LocalContent);
        Assert.Equal(CloudState.Managed, CloudFiles.StateOf(FileAttributes.Archive, true));
        Assert.Equal(CloudState.None, CloudFiles.StateOf(FileAttributes.Archive, false));
        Assert.Equal(CloudState.None, CloudFiles.StateOf(CloudFiles.Pinned | CloudFiles.Unpinned, true));
        Assert.Equal("not established", CloudFiles.Observe(CloudFiles.RecallOnOpen, false, enumerationAttributes: false).LocalContent);
        Assert.Equal("not fully present", CloudFiles.Observe(CloudFiles.RecallOnOpen, false).LocalContent);
    }
    [Fact]
    public void A_folder_lies_in_the_sync_root_that_holds_it()
    {
        string home = OperatingSystem.IsWindows() ? @"C:\Users\someone" : "/home/someone";
        var roots = new[] { new CloudSyncRoot("OneDrive - Personal", Path.Combine(home, "OneDrive")), new CloudSyncRoot("Dropbox", Path.Combine(home, "Dropbox")) };
        Assert.Equal("OneDrive - Personal", CloudFiles.RootOf(Path.Combine(home, "OneDrive"), roots)?.Provider);
        Assert.Equal("Dropbox", CloudFiles.RootOf(Path.Combine(home, "Dropbox", "Photos", "2026"), roots)?.Provider);
        Assert.Null(CloudFiles.RootOf(home, roots));
        // A name that merely begins like a root is not in it.
        Assert.Null(CloudFiles.RootOf(Path.Combine(home, "OneDrive backup"), roots));
        Assert.Null(CloudFiles.RootOf(Path.Combine(home, "OneDrive"), []));
    }
}
