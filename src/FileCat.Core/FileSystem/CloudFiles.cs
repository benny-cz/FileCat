namespace FileCat.Core.FileSystem;

/// <summary>Where a cloud storage provider keeps its files on this computer (on Windows, a Cloud Files sync root).</summary>
public sealed record CloudSyncRoot(string Provider, string Path);

/// <summary>What a cloud provider's file or folder is to this computer, as Explorer tells it.</summary>
public enum CloudState
{
    None,
    /// <summary>Only in the cloud (or asked to be): opening it downloads it.</summary>
    OnlineOnly,
    /// <summary>On this computer, kept until space is needed.</summary>
    Available,
    /// <summary>On this computer always: the user chose "Always keep on this device".</summary>
    AlwaysAvailable,
}

/// <summary>
/// Cloud files (OneDrive, Dropbox, iCloud Drive and every other provider built on Windows' Cloud Files API) told apart by
/// the attributes a listing already has: nothing is opened, so nothing is downloaded, and no provider's own Shell code
/// runs in FileCat.
/// </summary>
public static class CloudFiles
{
    // The attributes cloud providers set (winnt.h).
    public const FileAttributes Pinned = (FileAttributes)0x80000;
    public const FileAttributes Unpinned = (FileAttributes)0x100000;
    public const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>
    /// The state an item's attributes give it. Outside a sync root only what a provider alone sets says anything; inside
    /// one, an item with none of it is on this computer like any other.
    /// </summary>
    public static CloudState StateOf(FileAttributes attributes, bool inSyncRoot)
    {
        // Both at once is the Cloud Files API's "excluded" pin state: the provider does not sync it at all (Dropbox's own
        // .dropbox and .dropbox.cache, a folder's desktop.ini), so it is neither in the cloud nor kept from it.
        if ((attributes & (Pinned | Unpinned)) == (Pinned | Unpinned)) return CloudState.None;
        if ((attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline | Unpinned)) != 0) return CloudState.OnlineOnly;
        if ((attributes & Pinned) != 0) return CloudState.AlwaysAvailable;
        return inSyncRoot ? CloudState.Available : CloudState.None;
    }

    /// <summary>The sync root <paramref name="path"/> lies in (itself included), or null.</summary>
    public static CloudSyncRoot? RootOf(string path, IReadOnlyList<CloudSyncRoot> roots)
    {
        foreach (var root in roots)
            if (PathUtil.IsSameOrUnder(path, root.Path)) return root;
        return null;
    }
}
