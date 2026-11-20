namespace FileCat.Core.FileSystem;

/// <summary>Where a cloud storage provider keeps its files on this computer (on Windows, a Cloud Files sync root).</summary>
public sealed record CloudSyncRoot(string Provider, string Path);

/// <summary>Neutral cached indicators; these do not claim complete residency.</summary>
public enum CloudState
{
    None,
    /// <summary>Provider-managed; attributes do not establish local residency.</summary>
    Managed,
    /// <summary>Content may need recall; some data may already be local.</summary>
    RecallPossible,
}

/// <summary>
/// Cached enumeration hints only. This presentation code opens nothing; provider activity elsewhere is not excluded.
/// </summary>
public static class CloudFiles
{
    // The attributes cloud providers set (winnt.h).
    public const FileAttributes Pinned = (FileAttributes)0x80000;
    public const FileAttributes Unpinned = (FileAttributes)0x100000;
    public const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    public const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>
    /// Cached enumeration hints only. Neither pin intent nor absence of recall proves complete local content.
    /// </summary>
    public static CloudState StateOf(FileAttributes attributes, bool inSyncRoot) => StateOf(attributes, inSyncRoot, 0);

    public static CloudState StateOf(FileAttributes attributes, bool inSyncRoot, uint reparseTag)
    {
        // Both at once is the Cloud Files API's "excluded" pin state: the provider does not sync it at all (Dropbox's own
        // .dropbox and .dropbox.cache, a folder's desktop.ini), so it is neither in the cloud nor kept from it.
        if ((attributes & (Pinned | Unpinned)) == (Pinned | Unpinned)) return CloudState.None;
        if ((attributes & (RecallOnDataAccess | RecallOnOpen | FileAttributes.Offline)) != 0) return CloudState.RecallPossible;
        bool knownPlaceholder = (attributes & FileAttributes.ReparsePoint) != 0 && ReparsePresentation.IsCloudTag(reparseTag);
        return inSyncRoot || knownPlaceholder || (attributes & (Pinned | Unpinned)) != 0
            ? CloudState.Managed : CloudState.None;
    }

    public static CloudObservation Observe(FileAttributes attributes, bool inSyncRoot, bool enumerationAttributes = true) =>
        Observe(attributes, inSyncRoot, 0, enumerationAttributes);

    public static CloudObservation Observe(FileAttributes attributes, bool inSyncRoot, uint reparseTag, bool enumerationAttributes = true)
    {
        // 0x40000 is RECALL_ON_OPEN in enumeration, but EA in queried attributes.
        var hints = enumerationAttributes ? attributes : attributes & ~RecallOnOpen;
        bool excluded = (attributes & (Pinned | Unpinned)) == (Pinned | Unpinned);
        string policy = excluded ? "excluded" : (attributes & Pinned) != 0 ? "keep locally"
            : (attributes & Unpinned) != 0 ? "allow removal" : "unspecified";
        bool recall = (hints & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0;
        return new(StateOf(hints, inSyncRoot, reparseTag), policy, recall ? "not fully present" : "not established", "not inspected");
    }

    /// <summary>The sync root <paramref name="path"/> lies in (itself included), or null.</summary>
    public static CloudSyncRoot? RootOf(string path, IReadOnlyList<CloudSyncRoot> roots)
    {
        foreach (var root in roots)
            if (PathUtil.IsSameOrUnder(path, root.Path)) return root;
        return null;
    }
}

public sealed record CloudObservation(CloudState Indicator, string PinPolicy, string LocalContent, string Synchronization)
{
    public string Description => $"Pin policy: {PinPolicy}; local content: {LocalContent}; synchronization: {Synchronization}";
}
