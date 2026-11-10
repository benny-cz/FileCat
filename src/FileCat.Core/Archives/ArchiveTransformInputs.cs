namespace FileCat.Core.Archives;

/// <summary>Reviewed regular-file facts; positive numeric IDs are grants, never filesystem spellings.</summary>
public sealed record ArchiveTransformInput
{
    public int Id { get; }
    public long Length { get; }
    public string Sha256 { get; }
    public ArchiveTimestamp ModifiedTimestamp { get; }
    public DateTimeOffset Modified => ModifiedTimestamp.ToDateTimeOffset();
    public int Mode { get; }
    /// <summary>
    /// The local clock's UTC offset at the modification instant, taken where FileCat runs. Formats that record local
    /// time (ZIP's DOS field) use it; the worker never consults time-zone data of its own.
    /// </summary>
    public int LocalOffsetMinutes { get; }
    public ArchiveTransformInput(int id, long length, string sha256, DateTimeOffset modified, int mode = 0x1a4)
        : this(id, length, sha256, (ArchiveTimestamp)modified, mode) { }
    public ArchiveTransformInput(int id, long length, string sha256, ArchiveTimestamp modified, int mode = 0x1a4, int? localOffsetMinutes = null)
    {
        ArchiveWritePolicy.ValidateDigest(sha256);
        int offset = localOffsetMinutes ?? ArchiveLocalClock.OffsetMinutes(modified);
        if (id <= 0 || length is < 0 or > ArchiveWritePolicy.MaxPayloadBytes || (mode & ~0x1ff) != 0 || !ArchiveLocalClock.Valid(offset) ||
            length == 0 && sha256 != "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")
            throw ArchiveManifestMember.Invalid();
        Id = id; Length = length; Sha256 = sha256; ModifiedTimestamp = modified; Mode = mode; LocalOffsetMinutes = offset;
    }
}

/// <summary>The parent's local clock, read only where FileCat runs; sandboxed workers receive the offsets explicitly.</summary>
internal static class ArchiveLocalClock
{
    internal const int MaxOffsetMinutes = 14 * 60;
    internal static bool Valid(int minutes) => minutes is >= -MaxOffsetMinutes and <= MaxOffsetMinutes;
    internal static int OffsetMinutes(ArchiveTimestamp instant)
    {
        // DateTimeOffset needs a day of room at either end of its range; such instants have no meaningful local zone.
        if (instant.Seconds < -62135596800L + 86400 || instant.Seconds > 253402300799L - 86400) return 0;
        int minutes = (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTimeOffset.FromUnixTimeSeconds(instant.Seconds)).TotalMinutes);
        return Valid(minutes) ? minutes : 0;
    }
}

/// <summary>One exact occurrence; recursive folder operations must already be expanded by trusted preparation.</summary>
public sealed record ArchiveTransformEdit
{
    public ArchiveChangeKind Kind { get; }
    public ArchiveMemberKey Key { get; }
    public string? NewName { get; }
    public int? InputId { get; }
    public ArchiveTransformEdit(ArchiveChangeKind kind, ArchiveMemberKey key, string? newName = null, int? inputId = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (kind is not (ArchiveChangeKind.Delete or ArchiveChangeKind.Rename or ArchiveChangeKind.Replace) ||
            kind != ArchiveChangeKind.Rename && newName is not null || kind != ArchiveChangeKind.Replace && inputId is not null ||
            kind == ArchiveChangeKind.Replace && (key.Type != ArchiveMemberType.File || inputId is not > 0)) throw ArchiveManifestMember.Invalid();
        if (kind == ArchiveChangeKind.Rename) ArchiveWritePolicy.ValidateName(newName!, key.Type);
        Kind = kind; Key = key; NewName = newName; InputId = inputId;
    }
}

/// <summary>One flattened file or explicit directory addition; no source path or implicit recursion.</summary>
public sealed record ArchiveTransformDirectoryMetadata
{
    public ArchiveTimestamp Modified { get; }
    public int Mode { get; }
    /// <summary>As <see cref="ArchiveTransformInput.LocalOffsetMinutes"/>.</summary>
    public int LocalOffsetMinutes { get; }
    public ArchiveTransformDirectoryMetadata(ArchiveTimestamp modified, int mode = 0x1ed, int? localOffsetMinutes = null)
    {
        int offset = localOffsetMinutes ?? ArchiveLocalClock.OffsetMinutes(modified);
        if ((mode & ~0x1ff) != 0 || !ArchiveLocalClock.Valid(offset)) throw ArchiveManifestMember.Invalid();
        Modified = modified; Mode = mode; LocalOffsetMinutes = offset;
    }
}

public sealed record ArchiveTransformAddition
{
    public string Name { get; }
    public ArchiveMemberType Type { get; }
    public int? InputId { get; }
    public ArchiveTransformDirectoryMetadata? DirectoryMetadata { get; }
    internal ArchiveCapturedDirectory? CapturedDirectory { get; }
    public ArchiveTransformAddition(string name, ArchiveMemberType type, int? inputId = null)
        : this(name, type, inputId, type == ArchiveMemberType.Directory ? new(new(0, 0)) : null, null) { }
    public ArchiveTransformAddition(string name, ArchiveTransformDirectoryMetadata directoryMetadata)
        : this(name, ArchiveMemberType.Directory, null, directoryMetadata, null) { }
    private ArchiveTransformAddition(string name, ArchiveMemberType type, int? inputId,
        ArchiveTransformDirectoryMetadata? directoryMetadata, ArchiveCapturedDirectory? capturedDirectory)
    {
        ArchiveWritePolicy.ValidateName(name, type);
        if (!Enum.IsDefined(type) || type == ArchiveMemberType.File && inputId is not > 0 ||
            type == ArchiveMemberType.Directory && inputId is not null || (type == ArchiveMemberType.Directory) != (directoryMetadata is not null)) throw ArchiveManifestMember.Invalid();
        Name = name; Type = type; InputId = inputId; DirectoryMetadata = directoryMetadata; CapturedDirectory = capturedDirectory;
    }
    internal static ArchiveTransformAddition FromCapturedDirectory(string name, ArchiveCapturedDirectory directory)
        => new(name, ArchiveMemberType.Directory, null, new(directory.Reviewed.Modified, directory.Reviewed.Mode), directory);
}
