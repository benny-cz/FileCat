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

    /// <summary>
    /// None yet. The owner's route decision of 12 November 2026 includes native edit storage on the three routed runtimes. As
    /// the default store a stopped or failed F4 removes exactly what it made and a copy stays out of the session list until
    /// its record is published; it is listed once the edit, Core and platform suites have passed with it as the default on
    /// all three platforms and the full regression has run on the listing commit (ledger rows 4 and 8).
    /// </summary>
    internal static IReadOnlyList<Route> Qualified { get; } = [];

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
        if (nativeOpen is null || !listed || !IsQualified(routes, runtimeIdentifier, fileSystemOf(parent))) return new EditSessionStore(legacy, fs);
        if (!Holdable(nativeOpen, parent, native)) return new EditSessionStore(legacy, fs);
        FileCat.Core.Diagnostics.AppLog.Info("Edit sessions: native storage beside the legacy folder.");
        return new EditSessionStore(legacy, fs, nativeOpen, nativeCopyOpen, null, native);
    }

    // The native store pins its root's folder without following links, and creates the root there on first use. A state
    // folder reached through a link (an AppData moved with a junction, a symbolic link to a home folder, macOS's /tmp), or
    // one this process may not hold (an elevated FileCat on Windows), keeps the legacy store, which behaves there as it
    // always did, rather than refusing every new edit. So does a native root that exists but is not this user's private
    // one (made by another account or computer, as on an external drive or a shared --data folder): every edit, and the
    // review of interrupted work at startup, would be refused there. Logged without the path, either way.
    private static bool Holdable(Func<string, bool, CancellationToken, EditDirectoryLease> nativeOpen, string parent, string native)
    {
        try
        {
            using (var lease = nativeOpen(parent, false, CancellationToken.None))
                if (!string.Equals(lease.Path, parent, StringComparison.Ordinal))
                {
                    FileCat.Core.Diagnostics.AppLog.Warn("Edit sessions: the legacy folder is used: the native storage's folder opened at another path.");
                    return false;
                }
            if (!Directory.Exists(native)) return true;
            using var root = nativeOpen(native, true, CancellationToken.None);
            if (string.Equals(root.Path, native, StringComparison.Ordinal)) return true;
            FileCat.Core.Diagnostics.AppLog.Warn("Edit sessions: the legacy folder is used: the native storage opened at another path.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.ComponentModel.Win32Exception)
        {
            FileCat.Core.Diagnostics.AppLog.Warn("Edit sessions: the legacy folder is used: the native storage cannot be held without " +
                "following links, privately, or by this process (" + error.GetType().Name + ").");
        }
        return false;
    }
}
