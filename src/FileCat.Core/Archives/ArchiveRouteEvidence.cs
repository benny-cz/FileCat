namespace FileCat.Core.Archives;

/// <summary>
/// The evidence IDs of the listed archive-writing routes (plan §16.1). The release manifest carries, under each ID, the
/// evidence of that runtime's qualification on the release candidate (IDs below it, such as <c>AWQ-win-x64-NTFS/full</c>):
/// its full regression and its package route lanes. The manifest refuses a route whose evidence it does not carry. A
/// capability qualified separately on the same runtime has its own ID below the route's, so that refusal binds for it
/// instead of collapsing into the route's: <see cref="Edit"/> for native edit storage.
/// </summary>
internal static class ArchiveRouteEvidence
{
    internal const string Windows = "AWQ-win-x64-NTFS";
    internal const string Linux = "AWQ-linux-x64-ext4";
    internal const string MacOS = "AWQ-osx-arm64-apfs";

    /// <summary>The native edit storage qualification of one route: its own ID, below that route's.</summary>
    internal static string Edit(string route) => route + "/edit";
}
