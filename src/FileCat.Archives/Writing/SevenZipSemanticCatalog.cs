using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>Exact public projection of a fully verified source. Crypto bytes and physical reuse witnesses stay worker-local.</summary>
internal static class SevenZipSemanticCatalog
{
    internal static ArchiveSevenZipMetadata Metadata(SevenZipLogicalMember member, SevenZipPhysicalCatalog catalog)
    {
        var m = member.Physical;
        return new(m.Type, m.Attributes, m.CreatedFileTime, m.AccessedFileTime, m.ModifiedFileTime,
            m.Folder is { } index ? Graph(catalog.Streams.Folders[index], catalog.Streams, m.Crc32 is not null) : null);
    }
    internal static ArchiveSevenZipGraph Graph(SevenZipRawFolder folder, SevenZipRawStreams streams, bool memberCrcPresent)
        => new(folder.Coders.Select(PublicCoder).ToArray(), folder.Bindings.Select(b => new ArchiveSevenZipBinding(b.Input, b.Output)).ToArray(),
            folder.PackedInputs, streams.Packed.Skip(folder.FirstPack).Take(folder.PackedInputs.Count).Select(p => p.Crc32 is not null).ToArray(),
            folder.FinalOutput, folder.Crc32 is not null, memberCrcPresent);
    internal static ArchiveSevenZipCoder PublicCoder(SevenZipCoder coder)
    {
        ulong method = SevenZipStrictMetadata.Method(coder);
        if (method != 0x06f10701) return new(method, coder.Properties, coder.Inputs);
        // The complete decoder already checked the native AES extent. Copy only public strength/width facts.
        var p = coder.Properties.AsSpan(); if (p.IsEmpty) throw Invalid();
        int salt = 0, iv = 0;
        if ((p[0] & 0xc0) != 0)
        { if (p.Length < 2) throw Invalid(); salt = (p[0] >> 7) + (p[1] >> 4); iv = (p[0] >> 6 & 1) + (p[1] & 15); }
        if (p.Length != (salt == 0 && iv == 0 && (p[0] & 0xc0) == 0 ? 1 : 2 + salt + iv)) throw Invalid();
        return new(method, [(byte)(p[0] & 63), (byte)salt, (byte)iv], coder.Inputs);
    }
    internal static ArchiveSevenZipFeatures Features(SevenZipPhysicalCatalog catalog)
    {
        var s = catalog.Streams; var h = catalog.EncodedHeader;
        return new(catalog.MinorVersion, catalog.MainStreamsPresent, catalog.Files.PropertyMask, catalog.Files.DummyBytes,
            s.SubstreamsPresent, s.SubstreamCountsPresent, s.SubstreamSizesPresent, s.SubstreamCrcPresent,
            h is null ? null : Graph(h.Folders[0], h, h.MemberCrc32[0] is not null),
            h?.SubstreamsPresent == true, h?.SubstreamCountsPresent == true, h?.SubstreamSizesPresent == true, h?.SubstreamCrcPresent == true);
    }
    internal static ArchiveAssessment Assessment(SevenZipLogicalCatalog catalog, ArchiveWriteProfile profile, CancellationToken ct)
    {
        if (profile.Version != 2 || profile.Format != ArchiveWriteFormat.SevenZip) throw Invalid();
        var features = Features(catalog.Physical);
        var mode = features.HiddenNames ? ArchiveEncryptionMode.DataAndNames : catalog.Physical.Streams.Folders.Any(f => f.Encrypted) ? ArchiveEncryptionMode.Data : ArchiveEncryptionMode.None;
        if (profile.Encryption.Mode != mode) throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The observed 7z password mode differs from the requested profile.");
        var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, checked(catalog.RetainedHeapBytes + catalog.Members.Count * 256L));
        var members = new List<ArchiveManifestMember>(catalog.Members.Count);
        foreach (var member in catalog.Members)
        {
            ct.ThrowIfCancellationRequested(); var m = member.Physical;
            var folder = m.Folder is { } index ? catalog.Physical.Streams.Folders[index] : null;
            long graph = folder is null ? 0 : checked(folder.Coders.Count * 160L + folder.Bindings.Count * 48L + folder.PackedInputs.Count * 32L + folder.Coders.Sum(c => c.Properties.Length * 4L));
            budget.AddMetadata(0, checked(m.Name.Length * 4L + 512 + graph));
            byte[] metadata = Metadata(member, catalog.Physical).Encode();
            members.Add(new(m.Name, m.Type, m.Occurrence, m.Length, member.Sha256, metadata));
        }
        return new(new(profile, members, features.Encode()), catalog.Physical.Revision, catalog.Physical.Length, catalog.Physical.Revision);
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.UnsupportedFeature, "The complete 7z public metadata or explicit version-2 profile is unavailable.");
}
