using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Archives;

public sealed partial class ArchiveProvider
{
    private sealed record DirectoryRevision(string? ArchiveRevision, string? ArchiveMutationProblem) : IArchiveMemberRevision;
    private static readonly IReadOnlyDictionary<ArchiveKind, ArchiveWriteProfile> WriteProfiles =
        new Dictionary<ArchiveKind, ArchiveWriteProfile>
        {
            [ArchiveKind.Tar] = new(ArchiveWriteFormat.Tar), [ArchiveKind.TarGzip] = new(ArchiveWriteFormat.TarGzip),
            [ArchiveKind.TarBzip2] = new(ArchiveWriteFormat.TarBzip2), [ArchiveKind.TarZstd] = new(ArchiveWriteFormat.TarZstd),
            [ArchiveKind.Rar] = ArchiveWriteProfile.ForRar5(), [ArchiveKind.SevenZip] = ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Lzma2), compressionLevel: 3),
            // AW7: one payload per standalone stream. The profile is only a request; assessment admits the actual wrapper.
            [ArchiveKind.Gzip] = new(ArchiveWriteFormat.Gzip), [ArchiveKind.Bzip2] = new(ArchiveWriteFormat.Bzip2), [ArchiveKind.Zstd] = new(ArchiveWriteFormat.Zstd),
        };

    /// <summary>
    /// Whether <paramref name="location"/> is inside a standalone .gz/.bz2/.zst file FileCat could write (AW7): its one virtual
    /// member is replaced or edited only as a whole, and it has no folders or other members.
    /// </summary>
    public bool IsStandaloneTarget(Location location) =>
        TryGetWriteTarget(location, out _, out var profile) && ArchiveStandalone.IsStandalone(profile.Format);

    /// <summary>The name shown for a standalone file's one member: the outer name without its suffix (display only).</summary>
    public static string StandaloneMemberDisplayName(string archivePath, ArchiveWriteFormat format)
    {
        string name = Path.GetFileName(archivePath), suffix = ArchiveStandalone.Suffix(format);
        return name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? name[..^suffix.Length] : name + ".data";
    }

    // Trusted composition supplies a cheap capability probe. Actual bytes require a fresh complete native review.
    internal Func<ArchivePlan, ArchiveJobAvailability>? MutationAvailability { get; set; }

    internal bool TryGetWriteTarget(Location location, out string path, out ArchiveWriteProfile profile)
    {
        path = string.Empty; profile = null!;
        if (location.Scheme != Schemes.Archive || location.Container is not { IsFileSystem: true } container ||
            !Path.IsPathFullyQualified(container.Path)) return false;
        if (OperatingSystem.IsWindows() && (PathUtil.IsUncPath(container.Path) ||
            container.Path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))) return false;
        ArchiveKind? kind;
        if (location.Session is { Length: > 0 } forced)
            kind = Enum.TryParse<ArchiveKind>(forced, out var value) && Enum.IsDefined(value) ? value : null;
        else kind = ArchiveFormats.ByName(Path.GetFileName(container.Path));
        if (kind is null || !WriteProfiles.TryGetValue(kind.Value, out var selected)) return false;
        path = container.Path; profile = selected; return true;
    }

    /// <summary>Why this archive cannot be changed here, specifically (plan §11.6), without reading the archive.</summary>
    internal string ReadOnlyReason(Location location)
    {
        if (location.Scheme == Schemes.Archive && location.Container is { IsFileSystem: false })
            return "Archives inside other archives are read-only.";
        if (location.Container is { IsFileSystem: true } container && OperatingSystem.IsWindows() &&
            (PathUtil.IsUncPath(container.Path) || container.Path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase)))
            return "Archives on network shares are read-only: safe saving on shares is not qualified.";
        if (!TryGetWriteTarget(location, out _, out _)) return "FileCat cannot write this archive format.";
        if (IsStandaloneTarget(location) && !MutationState(location).Available)
            return "Replacing compressed contents is not available here yet: its worker and safe-save support are not qualified on this system.";
        var state = MutationState(location);
        if (state.Available) return "This archive can't be changed right now.";
        return ReferenceEquals(state, ArchiveJobAvailability.Unqualified) || state.Reason is null
            ? "Writing this archive format is not available here yet: its worker and safe-save support are not qualified on this system."
            : state.Reason;
    }

    private ArchiveJobAvailability MutationState(Location location) =>
        TryGetWriteTarget(location, out string path, out var profile)
            ? MutationAvailability?.Invoke(new(path, null, [], Profile: profile)) ?? ArchiveJobAvailability.Unqualified
            : ArchiveJobAvailability.Unqualified;

    private string? MemberMutationProblem(Location location)
    {
        foreach (var index in _cache.Values)
        {
            if (!string.Equals(index.ContainerPath, location.Container?.Path, StringComparison.Ordinal) || index.ContainerSession != location.Session) continue;
            if (index.Revision is not { } revision) return "Refresh this archive before changing its members.";
            return revision.Problem;
        }
        return null;
    }

    // Provisional UI hint only: no parsing or filesystem calls. Strict command-time review resolves stale/hidden data.
    internal bool? CachedDirectoryHint(Location location)
    {
        if (location.Path.Length == 0) return true;
        foreach (var index in _cache.Values)
        {
            if (!string.Equals(index.ContainerPath, location.Container?.Path, StringComparison.Ordinal) || index.ContainerSession != location.Session) continue;
            if (index.Children.ContainsKey(location.Path)) return true;
            return index.Warnings.Count == 0 ? false : null;
        }
        return null;
    }

}
