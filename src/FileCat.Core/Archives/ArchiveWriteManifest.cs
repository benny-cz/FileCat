using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>One ordered physical member. Metadata is a versioned canonical format-specific encoding, never a path.</summary>
public sealed class ArchiveManifestMember
{
    private readonly byte[] _metadata;
    public string Name { get; }
    public ArchiveMemberType Type { get; }
    public int Occurrence { get; }
    public long Length { get; }
    public string Sha256 { get; }
    public ReadOnlySpan<byte> Metadata => _metadata;

    public ArchiveManifestMember(string name, ArchiveMemberType type, int occurrence, long length, string sha256,
        ReadOnlySpan<byte> metadata)
    {
        ArchiveWritePolicy.ValidateName(name, type);
        ArchiveWritePolicy.ValidateDigest(sha256);
        if (!Enum.IsDefined(type) || occurrence is < 0 or >= ArchiveWritePolicy.MaxMembers ||
            length is < 0 or > ArchiveWritePolicy.MaxPayloadBytes || type == ArchiveMemberType.Directory && length != 0 ||
            metadata.Length > ArchiveWritePolicy.MaxEntryMetadataBytes || length == 0 &&
            sha256 != "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")
            throw Invalid();
        Name = name; Type = type; Occurrence = occurrence; Length = length; Sha256 = sha256;
        _metadata = metadata.ToArray();
    }

    internal static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The archive manifest is invalid.");
}

/// <summary>
/// Frozen complete expected/observed manifest. All names, order, duplicate occurrences, content, semantic metadata,
/// profile options and admitted format features are hashed using versioned length-prefixed binary encodings.
/// This digest is not the physical source revision and cannot alone authorize publication.
/// </summary>
public sealed class ArchiveWriteManifest
{
    public const ushort Schema = 1;
    private readonly byte[] _features;
    public ArchiveWriteProfile Profile { get; }
    public IReadOnlyList<ArchiveManifestMember> Members { get; }
    public ReadOnlySpan<byte> Features => _features;
    public long LogicalBytes { get; }
    public string Digest { get; }
    public string ContentDigest { get; }
    public string MetadataDigest { get; }

    public ArchiveWriteManifest(ArchiveWriteProfile profile, IReadOnlyList<ArchiveManifestMember> members,
        ReadOnlySpan<byte> features)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count > ArchiveWritePolicy.MaxMembers || features.Length > ArchiveWritePolicy.MaxDataFrameBytes)
            throw ArchiveManifestMember.Invalid();
        var budget = new ArchiveWriteBudget();
        budget.AddMetadata(features.Length, features.Length);
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        var frozen = new ArchiveManifestMember[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            var member = members[i] ?? throw ArchiveManifestMember.Invalid();
            Reserve(budget, member.Name.Length, ArchiveWritePolicy.Utf8.GetByteCount(member.Name), member.Metadata.Length, member.Length);
            var key = (member.Name, member.Type);
            int occurrence = occurrences.GetValueOrDefault(key);
            if (member.Occurrence != occurrence) throw ArchiveManifestMember.Invalid();
            occurrences[key] = occurrence + 1;
            frozen[i] = member;
        }
        Profile = profile;
        Members = new ReadOnlyCollection<ArchiveManifestMember>(frozen);
        _features = features.ToArray();
        LogicalBytes = budget.LogicalBytes;
        using var digests = new ArchiveManifestDigestBuilder(Profile, frozen.Length, _features);
        foreach (var member in frozen) digests.Append(member);
        (Digest, ContentDigest, MetadataDigest) = digests.Complete();
    }

    internal static void Reserve(ArchiveWriteBudget budget, int nameChars, int nameBytes, int metadataBytes, long length)
    {
        budget.AddMember();
        budget.AddNameChars(nameChars);
        budget.AddLogicalBytes(length);
        // Include DTO/digest/string/dictionary/array overhead, not only the encoded metadata bytes.
        budget.AddMetadata(checked(nameBytes + metadataBytes + 64L), checked(2L * nameChars + metadataBytes + 512L));
    }

}
