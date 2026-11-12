using FileCat.Core.Archives;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Edit;

/// <summary>
/// Plan §16.1 for edit storage (remaining-work ledger rows 4 and 8): the runtimes and filesystems on which ordinary startup
/// composes the native edit store, each with the evidence that qualified it. Where one is listed, edits are kept in private,
/// identity-bound storage in a root of their own beside the legacy folder: new edits are created there, existing ones stay in
/// the legacy folder and commit through their own route until a reviewed adoption copies them, and an adopted original goes
/// only through its exact-object retirement. Everywhere else the legacy store stays, and its native-only actions say why they
/// are unavailable. A runtime is added only after the connected three-platform qualification closed for it on the release
/// candidate, as a reviewed release change that its test pins (as for <c>ArchiveWriteRoutes</c>).
/// </summary>
internal static class NativeEditStorage
{
    internal sealed record Route(string RuntimeIdentifier, string FileSystem, string EvidenceId);

    /// <summary>The owner's route decision of 12 November 2026, listed after the final independent review closed.</summary>
    internal static IReadOnlyList<Route> Qualified { get; } =
    [
        new("win-x64", "NTFS", ArchiveRouteEvidence.Windows),
        new("linux-x64", "ext4", ArchiveRouteEvidence.Linux),
        new("osx-arm64", "apfs", ArchiveRouteEvidence.MacOS),
    ];

    /// <summary>Whether native edit storage is qualified for this runtime on this filesystem (null: not established).</summary>
    internal static bool IsQualified(IReadOnlyList<Route> routes, string runtimeIdentifier, string? fileSystem)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentException.ThrowIfNullOrEmpty(runtimeIdentifier);
        return fileSystem is not null && routes.Any(route => string.Equals(route.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal) &&
            string.Equals(route.FileSystem, fileSystem, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The edit store for this runtime: native, with <paramref name="nativeRoot"/> as its own root, when the runtime and the
    /// filesystem holding that root are qualified and the platform opens private directory leases; otherwise the legacy store.
    /// The legacy folder stays the store's root either way, so its sessions keep loading and committing.
    /// </summary>
    internal static EditSessionStore Compose(IReadOnlyList<Route> routes, string runtimeIdentifier, string legacyRoot, string nativeRoot,
        IFileSystemOperations fs, Func<string, string?> fileSystemOf,
        Func<string, bool, CancellationToken, EditDirectoryLease>? nativeOpen,
        Func<string, CancellationToken, EditSaveCopyDestination>? nativeCopyOpen)
    {
        ArgumentNullException.ThrowIfNull(fileSystemOf);
        string legacy = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacyRoot));
        string native = Path.TrimEndingDirectorySeparator(Path.GetFullPath(nativeRoot));
        if (string.Equals(legacy, native, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ArgumentException("Native edit storage needs a root of its own, beside the legacy edit folder.", nameof(nativeRoot));
        // Nothing is probed unless this runtime is listed at all, so an unqualified startup does what it always did. The
        // native root may not exist yet (the store creates it privately on first use): its parent's filesystem will hold it.
        bool listed = routes.Any(route => string.Equals(route.RuntimeIdentifier, runtimeIdentifier, StringComparison.Ordinal));
        string parent = Path.GetDirectoryName(native)!;
        return nativeOpen is not null && listed && IsQualified(routes, runtimeIdentifier, fileSystemOf(parent)) && Pinnable(nativeOpen, parent)
            ? new EditSessionStore(legacy, fs, nativeOpen, nativeCopyOpen, null, native)
            : new EditSessionStore(legacy, fs);
    }

    // The native store pins its root's folder without following links, and creates the root there on first use. A state
    // folder reached through a link (an AppData moved with a junction, a symbolic link to a home folder, macOS's /tmp), or
    // one this process may not hold (an elevated FileCat on Windows), keeps the legacy store, which behaves there as it
    // always did, rather than refusing every new edit. Logged without the path, so support can tell which store is used.
    private static bool Pinnable(Func<string, bool, CancellationToken, EditDirectoryLease> nativeOpen, string parent)
    {
        try
        {
            using var lease = nativeOpen(parent, false, CancellationToken.None);
            if (string.Equals(lease.Path, parent, StringComparison.Ordinal)) return true;
            FileCat.Core.Diagnostics.AppLog.Warn("Native edit storage is not used: its folder opened at another path.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            FileCat.Core.Diagnostics.AppLog.Warn("Native edit storage is not used: its folder cannot be held without following links, " +
                "or not by this process (" + error.GetType().Name + ").");
        }
        return false;
    }
}
