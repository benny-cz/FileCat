using System.Security.Cryptography;

namespace FileCat.Core.Archives;

/// <summary>
/// The outer metadata a Unix existing-archive update promises to carry from the reviewed original to its replacement:
/// owner (which must already be the current user), group, permission bits, the access ACL, admitted extended attributes,
/// a small set of user-preservable file flags and, on macOS, the birth time. Content-changing modification/change times
/// and the Linux birth time are not promised: the replacement is a new inode whose content was written by this update.
/// Any original outside this profile refuses before a stage is created; nothing is converted or silently dropped.
/// </summary>
internal static class ArchiveUnixUpdateMetadata
{
    // Linux FS_IOC_GETFLAGS bits. Intrinsic bits describe the inode's on-disk layout and are not user metadata.
    internal const uint LinuxNoDump = 0x40, LinuxNoAtime = 0x80;
    internal const uint LinuxIntrinsic = 0x80000 /* EXTENT */ | 0x40000 /* HUGE_FILE */ | 0x10000000 /* INLINE_DATA */;
    // Darwin st_flags bits. UF_COMPRESSED is transparent HFS+/APFS storage compression, not user metadata.
    internal const uint MacNoDump = 0x1, MacHidden = 0x8000;
    internal const uint MacIntrinsic = 0x20 /* UF_COMPRESSED */;
    // Linux statx attribute bits that mirror a preservable flag; every other reported attribute refuses.
    private const ulong LinuxAttributeNoDump = 0x40;

    internal static uint Preservable(ArchiveUnixOuterMetadata.NativeFormat format) =>
        format == ArchiveUnixOuterMetadata.NativeFormat.Mac ? MacNoDump | MacHidden : LinuxNoDump | LinuxNoAtime;
    internal static uint Intrinsic(ArchiveUnixOuterMetadata.NativeFormat format) =>
        format == ArchiveUnixOuterMetadata.NativeFormat.Mac ? MacIntrinsic : LinuxIntrinsic;

    /// <summary>Refuse an original whose promised metadata this profile cannot reproduce exactly.</summary>
    internal static void RequireAdmissible(ArchiveUnixOuterMetadata original, uint currentUser)
    {
        ArgumentNullException.ThrowIfNull(original);
        var facts = original.Native;
        if (facts.Owner != currentUser)
            throw Unsupported("This archive belongs to another user. FileCat cannot keep its owner when saving, so it is read-only here.");
        if ((facts.Mode & 0xe00) != 0)
            throw Unsupported("This archive has setuid, setgid or sticky permission bits that FileCat does not reproduce when saving.");
        if (facts.LinkCount != 1)
            throw Unsupported("This archive has other hard links. Saving would detach them, so it is read-only here.");
        if ((facts.Flags & ~(Preservable(facts.Format) | Intrinsic(facts.Format))) != 0)
            throw Unsupported("This archive has file flags (such as immutable, append-only or compressed) that FileCat does not reproduce when saving.");
        if (facts.Format == ArchiveUnixOuterMetadata.NativeFormat.Linux && (facts.Attributes & facts.AttributeMask & ~LinuxAttributeNoDump) != 0)
            throw Unsupported("This archive has file attributes (such as encryption, verity or a mount root) that FileCat does not reproduce when saving.");
        original.RequireSupportedExtendedAttributes();
    }

    /// <summary>
    /// Compare a replacement candidate with the reviewed original on every promised field. The origin mark is compared
    /// with the separately reviewed required origin, which may add a mark carried from marked inputs but never removes one.
    /// </summary>
    internal static void RequirePromisedMatch(ArchiveUnixOuterMetadata original, ArchiveUnixOuterMetadata candidate, ArchiveOriginSnapshot requiredOrigin)
    {
        ArgumentNullException.ThrowIfNull(original); ArgumentNullException.ThrowIfNull(candidate); ArgumentNullException.ThrowIfNull(requiredOrigin);
        var o = original.Native; var c = candidate.Native; uint keep = Preservable(o.Format);
        string originName = o.Format == ArchiveUnixOuterMetadata.NativeFormat.Mac ? "com.apple.quarantine" : "user.xdg.origin.url";
        if (c.Format != o.Format || c.Owner != o.Owner || c.Group != o.Group || (c.Mode & 0xfff) != (o.Mode & 0xfff) ||
            (c.Flags & keep) != (o.Flags & keep) || (c.Flags & ~(keep | Intrinsic(c.Format))) != 0 ||
            c.Format == ArchiveUnixOuterMetadata.NativeFormat.Linux && (c.Attributes & c.AttributeMask & ~LinuxAttributeNoDump) != 0 ||
            c.Format == ArchiveUnixOuterMetadata.NativeFormat.Mac && c.Created != o.Created ||
            candidate.AclPresent != original.AclPresent || !candidate.AclBytes.SequenceEqual(original.AclBytes) ||
            candidate.Origin.Digest != requiredOrigin.Digest || original.ExtendedAttributes is null || candidate.ExtendedAttributes is null ||
            !candidate.ExtendedAttributes.MatchesExcept(original.ExtendedAttributes, originName))
            throw new ArchiveWriteException(ArchiveWriteReason.NeedsReview,
                "The replacement archive's owner, group, permissions, ACL, flags or marks differ from the reviewed original; nothing was published.");
    }

    /// <summary>Length-prefixed versioned digest of exactly the promised fields, for durable transaction facts.</summary>
    internal static string PromisedDigest(ArchiveUnixOuterMetadata metadata, ArchiveOriginSnapshot origin)
    {
        ArgumentNullException.ThrowIfNull(metadata); ArgumentNullException.ThrowIfNull(origin);
        var f = metadata.Native; uint keep = Preservable(f.Format);
        using var bytes = new MemoryStream(); using var writer = new BinaryWriter(bytes);
        writer.Write("FCUUPM1\0"u8); writer.Write((int)f.Format); writer.Write(f.Owner); writer.Write(f.Group); writer.Write(f.Mode & 0xfff);
        writer.Write(f.Flags & keep); writer.Write(f.Format == ArchiveUnixOuterMetadata.NativeFormat.Mac && f.Created.HasValue);
        if (f.Format == ArchiveUnixOuterMetadata.NativeFormat.Mac && f.Created is { } created) { writer.Write(created.Seconds); writer.Write(created.Nanoseconds); }
        writer.Write(metadata.AclPresent); writer.Write(metadata.AclBytes.Length); writer.Write(metadata.AclBytes);
        writer.Write(Convert.FromHexString(origin.Digest));
        string originName = f.Format == ArchiveUnixOuterMetadata.NativeFormat.Mac ? "com.apple.quarantine" : "user.xdg.origin.url";
        var others = metadata.ExtendedAttributes?.Attributes.Where(a => a.Name != originName).OrderBy(a => a.Name, StringComparer.Ordinal).ToArray()
            ?? throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The archive's extended attributes were not captured.");
        writer.Write(others.Length);
        foreach (var attribute in others)
        {
            byte[] name = ArchiveWritePolicy.Utf8.GetBytes(attribute.Name);
            writer.Write(name.Length); writer.Write(name); writer.Write(attribute.Value.Length); writer.Write(attribute.Value);
        }
        writer.Flush();
        return Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length))));
    }

    private static ArchiveWriteException Unsupported(string message) => new(ArchiveWriteReason.UnsupportedFeature, message);
}
