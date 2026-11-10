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
        };

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
            if (!index.WriteRevisionAttempted) return "Refresh this archive before changing its members.";
            return index.WriteRevisionProblem;
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

    private static string HashListingRevision(FileStream witness, long length, CancellationToken ct)
    {
        if (length < 0 || length > ArchiveWritePolicy.MaxPhysicalBytes || witness.Length != length)
            throw new IOException("The archive length is outside the write profile or changed.");
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        byte[] bytes = System.Buffers.ArrayPool<byte>.Shared.Rent(ArchiveIoAuthority.MaximumReadBytes);
        try
        {
            long offset = 0;
            while (offset < length)
            {
                ct.ThrowIfCancellationRequested();
                int read = RandomAccess.Read(witness.SafeFileHandle, bytes.AsSpan(0, (int)Math.Min(bytes.Length, length - offset)), offset);
                if (read <= 0) throw new IOException("The archive changed while its listing revision was read.");
                hash.AppendData(bytes, 0, read); offset += read;
            }
            ct.ThrowIfCancellationRequested(); return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(bytes, clearArray: true); }
    }
}
