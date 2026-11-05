namespace FileCat.Core.Archives;

/// <summary>Reviewed regular-file facts; positive numeric IDs are grants, never filesystem spellings.</summary>
public sealed record ArchiveTransformInput
{
    public int Id { get; }
    public long Length { get; }
    public string Sha256 { get; }
    public DateTimeOffset Modified { get; }
    public int Mode { get; }
    public ArchiveTransformInput(int id, long length, string sha256, DateTimeOffset modified, int mode = 0x1a4)
    {
        ArchiveWritePolicy.ValidateDigest(sha256);
        if (id <= 0 || length is < 0 or > ArchiveWritePolicy.MaxPayloadBytes || (mode & ~0x1ff) != 0 ||
            length == 0 && sha256 != "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")
            throw ArchiveManifestMember.Invalid();
        Id = id; Length = length; Sha256 = sha256; Modified = modified.ToUniversalTime(); Mode = mode;
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
public sealed record ArchiveTransformAddition
{
    public string Name { get; }
    public ArchiveMemberType Type { get; }
    public int? InputId { get; }
    public ArchiveTransformAddition(string name, ArchiveMemberType type, int? inputId = null)
    {
        ArchiveWritePolicy.ValidateName(name, type);
        if (!Enum.IsDefined(type) || type == ArchiveMemberType.File && inputId is not > 0 ||
            type == ArchiveMemberType.Directory && inputId is not null) throw ArchiveManifestMember.Invalid();
        Name = name; Type = type; InputId = inputId;
    }
}
