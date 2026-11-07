using System.Text;

namespace FileCat.Core.Archives;

// Wire values are explicit: journals and edit sessions must not depend on enum declaration order.
public enum ArchiveWriteFormat { Zip = 0, Tar = 1, TarGzip = 2, TarBzip2 = 3, TarZstd = 4, SevenZip = 5, Gzip = 6, Bzip2 = 7, Zstd = 8, Rar5 = 9 }
public enum ArchiveAdmissionState { Unknown, Assessing, Writable, ReadOnly, Unavailable }
public enum ArchiveMemberType { File = 0, Directory = 1 }
public enum ArchiveWriteReason
{
    InvalidRequest = 0, InvalidArchive = 1, UnsupportedFeature = 2, BudgetExceeded = 3, SourceChanged = 4,
    VerificationFailed = 5, WorkerUnavailable = 6, ProtocolViolation = 7, PublicationUnavailable = 8, NeedsReview = 9,
}

/// <summary>A controlled refusal, distinct from an unexpected decoder or programmer exception.</summary>
public sealed class ArchiveWriteException : IOException
{
    public ArchiveWriteReason Reason { get; }
    public ArchiveWriteException(ArchiveWriteReason reason, string message) : base(message) { Reason = reason; }
    public ArchiveWriteException(ArchiveWriteReason reason, string message, Exception innerException) : base(message, innerException) { Reason = reason; }
}

/// <summary>Versioned options. A filename never selects a writer or changes its preservation policy.</summary>
public sealed record ArchiveWriteProfile
{
    public ArchiveWriteFormat Format { get; }
    public int Version { get; }
    public int CompressionLevel { get; }
    public int DictionaryBytes { get; }
    public ArchiveSevenZipOptions? SevenZip { get; }
    public ArchiveRar5Options? Rar5 { get; }
    public ArchiveEncryptionOptions Encryption { get; }

    public ArchiveWriteProfile(ArchiveWriteFormat format, int version = 1, int compressionLevel = 0,
        int dictionaryBytes = 8 * 1024 * 1024, ArchiveSevenZipOptions? sevenZip = null,
        ArchiveRar5Options? rar5 = null, ArchiveEncryptionOptions? encryption = null)
    {
        encryption ??= ArchiveEncryptionOptions.None;
        if (!Enum.IsDefined(format) || version is not (1 or 2))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "This archive writing profile is not supported.");
        if (version == 2)
        {
            bool valid = format switch
            {
                ArchiveWriteFormat.SevenZip => sevenZip is not null && rar5 is null && compressionLevel is >= 0 and <= 3 &&
                    (sevenZip.Codec != ArchiveSevenZipCodec.Copy || compressionLevel == 0) &&
                    dictionaryBytes >= 1 << 20 && dictionaryBytes <= 16 << 20 && System.Numerics.BitOperations.IsPow2((uint)dictionaryBytes) &&
                    (sevenZip.Codec is ArchiveSevenZipCodec.Lzma or ArchiveSevenZipCodec.Lzma2 || dictionaryBytes == 8 << 20),
                ArchiveWriteFormat.Rar5 => rar5 is not null && sevenZip is null && compressionLevel is >= 0 and <= 5 && dictionaryBytes == 128 << 10,
                _ => false,
            };
            if (!valid) throw ArchiveEncryptionOptions.Invalid();
            Format = format; Version = version; CompressionLevel = compressionLevel; DictionaryBytes = dictionaryBytes;
            SevenZip = sevenZip; Rar5 = rar5; Encryption = encryption; return;
        }
        if (format == ArchiveWriteFormat.Rar5 || sevenZip is not null || rar5 is not null || encryption != ArchiveEncryptionOptions.None)
            throw ArchiveEncryptionOptions.Invalid();
        // Version 1 stays byte-for-byte compatible. Zero is its format-specific default, not a generic slider.
        bool levelValid = format switch
        {
            ArchiveWriteFormat.Zip or ArchiveWriteFormat.TarGzip or ArchiveWriteFormat.Gzip => compressionLevel is >= 0 and <= 3,
            ArchiveWriteFormat.TarZstd or ArchiveWriteFormat.Zstd => compressionLevel is >= 0 and <= 3,
            ArchiveWriteFormat.SevenZip => compressionLevel is >= 0 and <= 2,
            _ => compressionLevel == 0,
        };
        if (!levelValid || dictionaryBytes < 1 << 20 || dictionaryBytes > 16 << 20 ||
            format != ArchiveWriteFormat.SevenZip && dictionaryBytes != 8 << 20)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "These compression options are not supported for this format.");
        Format = format;
        Version = version;
        CompressionLevel = compressionLevel;
        DictionaryBytes = dictionaryBytes;
        Encryption = ArchiveEncryptionOptions.None;
    }

    public static ArchiveWriteProfile ForSevenZip(ArchiveSevenZipOptions? options = null, int compressionLevel = 0,
        int dictionaryBytes = 8 << 20, ArchiveEncryptionOptions? encryption = null) =>
        new(ArchiveWriteFormat.SevenZip, 2, compressionLevel, dictionaryBytes, options ?? new(), encryption: encryption);

    public static ArchiveWriteProfile ForRar5(ArchiveRar5Compression compression = ArchiveRar5Compression.Normal,
        ArchiveEncryptionOptions? encryption = null) => new(ArchiveWriteFormat.Rar5, 2, (int)compression, 128 << 10,
            rar5: new(), encryption: encryption);
}

/// <summary>One physical occurrence in one complete catalog. No case folding or path normalization is permitted.</summary>
public sealed record ArchiveMemberKey
{
    public string CatalogRevision { get; }
    public string RawPath { get; }
    public ArchiveMemberType Type { get; }
    public int Occurrence { get; }

    public ArchiveMemberKey(string catalogRevision, string rawPath, ArchiveMemberType type, int occurrence)
    {
        ArchiveWritePolicy.ValidateDigest(catalogRevision);
        if (!Enum.IsDefined(type) || occurrence < 0 || occurrence >= ArchiveWritePolicy.MaxMembers)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "The archive member identity is invalid.");
        ArchiveWritePolicy.ValidateName(rawPath, type);
        CatalogRevision = catalogRevision;
        RawPath = rawPath;
        Type = type;
        Occurrence = occurrence;
    }
}

/// <summary>Initial profile ceilings, independent of claims inside an archive (implementation plan §9).</summary>
public static class ArchiveWritePolicy
{
    public const int MaxMembers = 100_000;
    public const int MaxNameChars = 32_000;
    public const int MaxNameBytes = MaxNameChars * 4;
    public const int MaxRar5NameBytes = 64 << 10; // Current hardened RAR reader/writer policy, not a format maximum.
    public const long MaxRetainedNameChars = 64L << 20;
    public const int MaxEntryMetadataBytes = 1 << 20;
    public const long MaxMetadataBytes = 128L << 20;
    public const long MaxRetainedHeapBytes = 192L << 20;
    public const int MaxPaxPairsPerEntry = 1_024;
    public const int MaxPaxPairs = 1_000_000;
    public const long MaxPayloadBytes = 8L << 30;
    public const long MaxLogicalBytes = 64L << 30;
    public const long MaxPhysicalBytes = 80L << 30;
    public const int MaxControlFrameBytes = 1 << 20;
    public const int MaxDataFrameBytes = 64 << 10;
    public const int MaxInputLeases = 16;
    public const long MaxWorkerMemoryBytes = 1L << 30;
    public const int MaxZstdWindowBytes = 64 << 20;
    // Wire traffic includes bounded rereads/backpatches of sources and stage; it is not an output-file size.
    public const long MaxWorkerWireBytes = 8 * MaxPhysicalBytes + MaxMetadataBytes;
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    public static void ValidateDigest(string digest)
    {
        if (digest is null || digest.Length != 64 || digest.Any(c => c is not (>= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "An archive revision digest is invalid.");
    }

    public static void ValidateName(string name, ArchiveMemberType type = ArchiveMemberType.File)
    {
        if (name is null || name.Length == 0 || name.Length > MaxNameChars)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "An archive member name is empty or too long.");
        string path = type == ArchiveMemberType.Directory && name.EndsWith('/') ? name[..^1] : name;
        if (ArchivePaths.Problem(path) is { } problem)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, problem);
        foreach (var part in path.Split('/'))
        {
            if (part.EndsWith('.') || part.EndsWith(' ') || part.Any(char.IsControl))
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "An archive name contains a control character or ends with a dot or space.");
            var stem = part.AsSpan();
            int dot = stem.IndexOf('.');
            if (dot >= 0) stem = stem[..dot];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                stem.Length == 4 && (stem[..3].Equals("COM", StringComparison.OrdinalIgnoreCase) ||
                                    stem[..3].Equals("LPT", StringComparison.OrdinalIgnoreCase)) &&
                (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³'))
                throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "An archive name uses a reserved device name.");
        }
        try
        {
            if (Utf8.GetByteCount(name) > MaxNameBytes) throw new EncoderFallbackException();
        }
        catch (EncoderFallbackException)
        {
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "An archive name cannot be represented exactly as UTF-8.");
        }
    }
}

/// <summary>Per-pass counters. Reserve before allocating or consuming bytes; a refusal cannot reset a counter.</summary>
public sealed class ArchiveWriteBudget
{
    public long LogicalBytes { get; private set; }
    public long MetadataBytes { get; private set; }
    public long RetainedHeapBytes { get; private set; }
    public long NameChars { get; private set; }
    public int Members { get; private set; }
    public int PaxPairs { get; private set; }

    public void AddMember()
    {
        Reserve(Members, 1, ArchiveWritePolicy.MaxMembers);
        Members++;
    }

    public void AddLogicalBytes(long bytes)
    {
        Reserve(LogicalBytes, bytes, ArchiveWritePolicy.MaxLogicalBytes);
        LogicalBytes += bytes;
    }

    public void AddNameChars(long utf16Chars)
    {
        Reserve(NameChars, utf16Chars, ArchiveWritePolicy.MaxRetainedNameChars);
        NameChars += utf16Chars;
    }

    public void AddMetadata(long encodedBytes, long retainedHeapBytes, int pairs = 0)
    {
        if (encodedBytes > ArchiveWritePolicy.MaxEntryMetadataBytes || pairs > ArchiveWritePolicy.MaxPaxPairsPerEntry)
            throw Limit();
        Reserve(MetadataBytes, encodedBytes, ArchiveWritePolicy.MaxMetadataBytes);
        Reserve(RetainedHeapBytes, retainedHeapBytes, ArchiveWritePolicy.MaxRetainedHeapBytes);
        Reserve(PaxPairs, pairs, ArchiveWritePolicy.MaxPaxPairs);
        MetadataBytes += encodedBytes;
        RetainedHeapBytes += retainedHeapBytes;
        PaxPairs += pairs;
    }

    internal void RetainCapturedInputs(ArchiveCapturedInputCost cost)
    {
        // Per-entry limits were checked during capture. Transfer aggregate retained costs before allocating
        // the plan's own maps/manifests; source logical bytes/member counts are checked by plan admission itself.
        Reserve(MetadataBytes, cost.MetadataBytes, ArchiveWritePolicy.MaxMetadataBytes);
        Reserve(RetainedHeapBytes, cost.HeapBytes, ArchiveWritePolicy.MaxRetainedHeapBytes);
        Reserve(NameChars, cost.NameChars, ArchiveWritePolicy.MaxRetainedNameChars);
        MetadataBytes += cost.MetadataBytes; RetainedHeapBytes += cost.HeapBytes; NameChars += cost.NameChars;
    }

    internal static void Reserve(long used, long amount, long maximum)
    {
        if (used < 0 || amount < 0 || used > maximum || amount > maximum - used) throw Limit();
    }

    internal static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded,
        "This archive exceeds the current writing resource limits. It can still be browsed or extracted.");
}
