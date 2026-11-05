namespace FileCat.Core.Archives;

public enum ArchiveSidecarRole { AssessedSource = 1, ExpectedOutput = 2, VerifiedOutput = 3, OriginEvidence = 4 }

/// <summary>Immutable parent evidence for one reserved sidecar. A name or version never grants cleanup authority.</summary>
public sealed record ArchiveSidecarFacts
{
    public ArchiveSidecarRole Role { get; }
    public string Path { get; }
    public long MaximumLength { get; }
    public string? Identity { get; }
    public ArchiveObjectVersion? Version { get; }
    public int? OriginSources { get; }

    public ArchiveSidecarFacts(ArchiveSidecarRole role, string path, long maximumLength, string? identity = null,
        ArchiveObjectVersion? version = null, int? originSources = null)
    {
        if (!Enum.IsDefined(role) || maximumLength < 0 || maximumLength > ArchiveWritePolicy.MaxMetadataBytes)
            throw new ArgumentException("Invalid archive sidecar role or byte bound.");
        ArchiveTransactionFacts.ValidatePath(path);
        if (identity is not null) ArchiveTransactionFacts.ValidateText(identity, 4096);
        if (version is not null && (version.Identity != identity || version.Length > maximumLength))
            throw new ArgumentException("The sidecar version differs from its reserved object or byte bound.");
        if (role == ArchiveSidecarRole.OriginEvidence ? originSources is null or < 0 or > ArchiveWritePolicy.MaxMembers + 1 : originSources is not null)
            throw new ArgumentException("The sidecar source count differs from its role.");
        Role = role; Path = path; MaximumLength = maximumLength; Identity = identity; Version = version; OriginSources = originSources;
    }

    internal ArchiveSidecarFacts BindIdentity(string identity)
    {
        if (Identity is not null) throw new InvalidOperationException("The sidecar identity was already recorded.");
        return new(Role, Path, MaximumLength, identity, null, OriginSources);
    }
    internal ArchiveSidecarFacts BindVersion(ArchiveObjectVersion version)
    {
        if (Identity is null || Version is not null) throw new InvalidOperationException("The sidecar is not awaiting its sealed version.");
        return new(Role, Path, MaximumLength, Identity, version, OriginSources);
    }
    internal int AdvancesFrom(ArchiveSidecarFacts previous)
    {
        if (Role != previous.Role || Path != previous.Path || MaximumLength != previous.MaximumLength || OriginSources != previous.OriginSources ||
            previous.Identity is not null && Identity != previous.Identity || previous.Version is not null && Version != previous.Version)
            throw new ArgumentException("An archive sidecar reservation changed or lost evidence.");
        return (previous.Identity is null && Identity is not null ? 1 : 0) + (previous.Version is null && Version is not null ? 1 : 0);
    }
}
