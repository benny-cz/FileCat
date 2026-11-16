namespace FileCat.Core.Archives;

/// <summary>
/// AW7 (plan §1.2 R3): one standalone gzip, bzip2 or Zstandard stream around exactly one payload. Every standalone manifest
/// has one member with the synthetic <see cref="MemberName"/>; the name shown to the user comes from the outer file name and a
/// name stored inside a gzip stream is never used, so renaming the outer file never changes a plan.
/// </summary>
public static class ArchiveStandalone
{
    /// <summary>The single synthetic member of every standalone manifest.</summary>
    public const string MemberName = "content";

    public static bool IsStandalone(ArchiveWriteFormat format) =>
        format is ArchiveWriteFormat.Gzip or ArchiveWriteFormat.Bzip2 or ArchiveWriteFormat.Zstd;

    /// <summary>The suffix whose removal gives the displayed member name (".gz", ".bz2" or ".zst").</summary>
    public static string Suffix(ArchiveWriteFormat format) => format switch
    {
        ArchiveWriteFormat.Gzip => ".gz",
        ArchiveWriteFormat.Bzip2 => ".bz2",
        ArchiveWriteFormat.Zstd => ".zst",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Wrapper features: gzip [5, operating system, extra flags], bzip2 [6, block size 1-9], Zstandard [7, content checksum].
    /// The same admitted header rules as the TAR wrappers apply (metadata-neutral gzip, one stream or frame).
    /// </summary>
    public static byte[] Features(ArchiveWriteProfile profile, byte operatingSystem = 255, byte? extraFlags = null, byte bzip2BlockSize = 9,
        byte zstdChecksum = 1)
    {
        ArgumentNullException.ThrowIfNull(profile);
        byte[] features = profile.Format switch
        {
            ArchiveWriteFormat.Gzip => [5, operatingSystem, extraFlags ?? ArchiveTarFeatures.Flags(profile.CompressionLevel)],
            ArchiveWriteFormat.Bzip2 => [6, bzip2BlockSize],
            ArchiveWriteFormat.Zstd => [7, zstdChecksum],
            _ => throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Not a standalone compressed-file profile."),
        };
        Validate(profile, features); return features;
    }

    public static void Validate(ArchiveWriteProfile profile, ReadOnlySpan<byte> features)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool valid = profile.Format switch
        {
            ArchiveWriteFormat.Gzip => features.Length == 3 && features[0] == 5 && features[2] is 0 or 2 or 4,
            ArchiveWriteFormat.Bzip2 => features.Length == 2 && features[0] == 6 && features[1] is >= 1 and <= 9,
            ArchiveWriteFormat.Zstd => features.Length == 2 && features[0] == 7 && features[1] == 1,
            _ => false,
        };
        if (!valid) throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The compressed file's wrapper is outside its admitted profile.");
    }

    /// <summary>The complete manifest of one standalone stream: one synthetic member with its exact length and SHA-256.</summary>
    public static ArchiveWriteManifest Manifest(ArchiveWriteProfile profile, long length, string sha256, ReadOnlySpan<byte> features)
    {
        Validate(profile, features);
        return new(profile, [new ArchiveManifestMember(MemberName, ArchiveMemberType.File, 0, length, sha256, [])], features);
    }

    /// <summary>A refusal for any change other than one file into a new stream or one replacement of its content.</summary>
    public static ArchiveWriteException OneFileOnly() => new(ArchiveWriteReason.InvalidRequest,
        "A compressed file holds exactly one file. Choose a TAR-based archive (for example TAR + gzip) for several items.");

    /// <summary>Why folders, deletion and renaming are never offered inside a compressed file, with what to do instead.</summary>
    public const string MemberChangesUnavailable =
        "A compressed file holds exactly one file: it has no folders, and its contents cannot be renamed or deleted inside it. " +
        "Rename or delete the compressed file itself from its folder, or copy one file onto it with F5 to replace its contents.";
}
