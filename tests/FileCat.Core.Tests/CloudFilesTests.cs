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
    public void Attributes_tell_the_state_Explorer_shows()
    {
        const FileAttributes placeholder = FileAttributes.Archive | FileAttributes.ReparsePoint;
        Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf(placeholder | CloudFiles.RecallOnDataAccess, inSyncRoot: true));
        Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf(placeholder | CloudFiles.RecallOnOpen, inSyncRoot: true));
        Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf(FileAttributes.Directory | CloudFiles.Unpinned, inSyncRoot: true));
        Assert.Equal(CloudState.AlwaysAvailable, CloudFiles.StateOf(placeholder | CloudFiles.Pinned, inSyncRoot: true));
        Assert.Equal(CloudState.AlwaysAvailable, CloudFiles.StateOf(FileAttributes.Directory | CloudFiles.Pinned, inSyncRoot: true));
        Assert.Equal(CloudState.Available, CloudFiles.StateOf(placeholder, inSyncRoot: true));
        Assert.Equal(CloudState.Available, CloudFiles.StateOf(FileAttributes.Archive, inSyncRoot: true));

        // Pinned and unpinned at once is "excluded": the provider does not sync it (seen on Dropbox's own .dropbox,
        // .dropbox.cache and desktop.ini as 0x00180022 and 0x00180032), so it gets no mark at all.
        Assert.Equal(CloudState.None, CloudFiles.StateOf((FileAttributes)0x00180022, inSyncRoot: true));
        Assert.Equal(CloudState.None, CloudFiles.StateOf((FileAttributes)0x00180032, inSyncRoot: true));
        // The others seen there: an online-only file (0x00501620), a folder set to free up space (0x00100430), and a
        // file that is down and not pinned (0x00000420).
        Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf((FileAttributes)0x00501620, inSyncRoot: true));
        Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf((FileAttributes)0x00100430, inSyncRoot: true));
        Assert.Equal(CloudState.Available, CloudFiles.StateOf((FileAttributes)0x00000420, inSyncRoot: true));

        // Kept, but asked for and not yet down: still only in the cloud until it arrives.
        Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf(placeholder | CloudFiles.Pinned | CloudFiles.RecallOnDataAccess, inSyncRoot: true));

        // Outside a sync root only what a provider alone sets says anything: an ordinary file gets no mark.
        Assert.Equal(CloudState.None, CloudFiles.StateOf(FileAttributes.Archive, inSyncRoot: false));
        Assert.Equal(CloudState.None, CloudFiles.StateOf(FileAttributes.Directory | FileAttributes.ReparsePoint, inSyncRoot: false));
        Assert.Equal(CloudState.OnlineOnly, CloudFiles.StateOf(placeholder | CloudFiles.RecallOnDataAccess, inSyncRoot: false));
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
