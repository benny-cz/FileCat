using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Exact supported semantic projection of an already completely verified catalog, never admission authority.</summary>
internal static class Rar5SemanticCatalog
{
    internal static ArchiveRar5Metadata Metadata(Rar5LogicalMember member) => Metadata(member.Physical, (int)member.Filters);
    private static ArchiveRar5Metadata Metadata(Rar5PhysicalMember member, int filters) => new(member.Type, member.HeaderFlags,
        member.Attributes, member.Host, member.Compression, member.BasicModified is { } basic ? checked((uint)basic.Seconds) : null,
        member.Crc32 is not null, member.Blake2sp is not null,
        member.Times is { } times ? new(times.Flags, times.Modified, times.Created, times.Accessed) : null,
        member.Owner is { } owner ? new(owner.Flags, owner.UserName, owner.GroupName, owner.UserId, owner.GroupId) : null,
        member.Encryption is { } crypto ? (crypto.Check.Length != 0 ? 1 : 0) | (crypto.UseMac ? 2 : 0) : null,
        member.Encryption?.CountPower, filters);

    internal static ArchiveRar5Features Features(Rar5PhysicalCatalog catalog) => new(checked((byte)catalog.MainHeaderFlags),
        checked((byte)catalog.EndHeaderFlags), checked((byte)catalog.EncryptionHeaderFlags), catalog.HiddenNames,
        catalog.HeaderPasswordCheckPresent, catalog.HeaderKdfPower);

    internal static ArchiveAssessment Assessment(Rar5LogicalCatalog catalog, ArchiveWriteProfile profile, CancellationToken ct)
    {
        if (profile.Version != 2 || profile.Format != ArchiveWriteFormat.Rar5)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This RAR catalog needs the explicit RAR5 writing profile.");
        var budget = new ArchiveWriteBudget();
        budget.AddMetadata(0, checked(catalog.Physical.RetainedHeapBytes + catalog.Members.Count * 256L));
        var members = new List<ArchiveManifestMember>(catalog.Members.Count);
        foreach (var member in catalog.Members)
        {
            ct.ThrowIfCancellationRequested(); var physical = member.Physical;
            long native = (physical.Owner?.UserName?.Length ?? 0L) + (physical.Owner?.GroupName?.Length ?? 0L);
            // Include source observations, immutable-owner copy, encoding scratch/result and manifest-owned copy.
            budget.AddMetadata(0, checked(5 * native + physical.Name.Length * 4L + 1536));
            byte[] metadata = Metadata(member).Encode();
            members.Add(new(physical.Name, physical.Type, physical.Occurrence, physical.Length, member.Sha256, metadata));
        }
        return new(new(profile, members, Features(catalog.Physical).Encode()), catalog.Physical.Revision,
            catalog.Physical.Length, catalog.Physical.Revision);
    }
}
