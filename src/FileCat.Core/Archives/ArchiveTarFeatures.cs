using System.IO.Compression;

namespace FileCat.Core.Archives;

/// <summary>Canonical TAR dialect and admitted gzip/BZip2 wrapper metadata; no publication authority.</summary>
public static class ArchiveTarFeatures
{
    public static byte[] Create(ArchiveWriteProfile profile, byte dialect, byte operatingSystem = 255, byte? extraFlags = null, byte bzip2BlockSize = 9)
    {
        ArgumentNullException.ThrowIfNull(profile);
        byte[] features = profile.Format switch
        {
            ArchiveWriteFormat.Tar => [1, dialect],
            ArchiveWriteFormat.TarGzip => [2, dialect, operatingSystem, extraFlags ?? Flags(profile.CompressionLevel)],
            ArchiveWriteFormat.TarBzip2 => [3, dialect, bzip2BlockSize],
            _ => [],
        };
        Validate(profile, features); return features;
    }
    public static void Validate(ArchiveWriteProfile profile, ReadOnlySpan<byte> features)
    {
        ArgumentNullException.ThrowIfNull(profile);
        bool valid = profile.Format switch
        {
            ArchiveWriteFormat.Tar => features.Length == 2 && features[0] == 1,
            ArchiveWriteFormat.TarGzip => features.Length == 4 && features[0] == 2 && features[3] is 0 or 2 or 4 &&
                (profile.CompressionLevel == 0 || features[3] == Flags(profile.CompressionLevel)),
            ArchiveWriteFormat.TarBzip2 => features.Length == 3 && features[0] == 3 && features[2] is >= 1 and <= 9,
            _ => false,
        };
        if (!valid || features[1] is not (1 or 2))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The TAR dialect or compression wrapper cannot be preserved by this profile.");
    }
    public static byte Flags(int level) => level switch { 0 or 2 => 0, 1 => 4, 3 => 2, _ => throw new ArgumentOutOfRangeException(nameof(level)) };
    public static CompressionLevel Level(byte flags) => flags switch
    { 0 => CompressionLevel.Optimal, 4 => CompressionLevel.Fastest, 2 => CompressionLevel.SmallestSize, _ => throw new ArgumentOutOfRangeException(nameof(flags)) };
}
