namespace FileCat.Core.Archives;

// These are public intent/wire values, not a claim that a backend or platform has qualified them.
public enum ArchiveSevenZipCodec { Copy = 0, Lzma = 1, Lzma2 = 2, Bzip2 = 3, Deflate = 4, Zstandard = 5, Ppmd7 = 6 }
public enum ArchiveSevenZipFilter
{
    None = 0, Delta = 1, X86 = 2, Arm = 3, ArmThumb = 4, Arm64 = 5, Ppc = 6, Ia64 = 7,
    Sparc = 8, RiscV = 9, Swap2 = 10, Swap4 = 11, Bcj2 = 12,
}
public enum ArchiveSevenZipHeaderCompression { Raw = 0, Lzma2 = 1 }
public enum ArchiveRar5Compression { Store = 0, Fastest = 1, Fast = 2, Normal = 3, Good = 4, Best = 5 }
public enum ArchiveEncryptionMode { None = 0, Data = 1, DataAndNames = 2 }

/// <summary>Only public cipher intent. Passwords, salts, IVs, derived keys and password checks never belong here.</summary>
public sealed record ArchiveEncryptionOptions
{
    public static ArchiveEncryptionOptions None { get; } = new(ArchiveEncryptionMode.None, 0);
    public ArchiveEncryptionMode Mode { get; }
    public int CountPower { get; }
    public ArchiveEncryptionOptions(ArchiveEncryptionMode mode, int countPower = 19)
    {
        if (!Enum.IsDefined(mode) || (mode == ArchiveEncryptionMode.None ? countPower != 0 : countPower is < 19 or > 24))
            throw Invalid();
        Mode = mode; CountPower = countPower;
    }
    internal static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest,
        "These archive options are invalid, unsupported or have no effect in the selected profile.");
}

/// <summary>Bounded non-solid 7z encoder intent. Level and dictionary are the profile's existing common fields.</summary>
public sealed record ArchiveSevenZipOptions
{
    public ArchiveSevenZipCodec Codec { get; }
    public ArchiveSevenZipFilter Filter { get; }
    public int ModelOrder { get; }
    public int ModelMemoryBytes { get; }
    public int DeltaDistance { get; }
    public ArchiveSevenZipHeaderCompression HeaderCompression { get; }
    public ArchiveSevenZipOptions(ArchiveSevenZipCodec codec = ArchiveSevenZipCodec.Lzma2,
        ArchiveSevenZipFilter filter = ArchiveSevenZipFilter.None, int modelOrder = 0, int modelMemoryBytes = 0,
        int deltaDistance = 1, ArchiveSevenZipHeaderCompression headerCompression = ArchiveSevenZipHeaderCompression.Lzma2)
    {
        if (!Enum.IsDefined(codec) || !Enum.IsDefined(filter) || !Enum.IsDefined(headerCompression) ||
            deltaDistance is < 1 or > 256 || filter != ArchiveSevenZipFilter.Delta && deltaDistance != 1 ||
            (modelOrder == 0) != (modelMemoryBytes == 0) ||
            codec != ArchiveSevenZipCodec.Ppmd7 && (modelOrder != 0 || modelMemoryBytes != 0) ||
            modelOrder != 0 && (modelOrder is < 2 or > 64 || modelMemoryBytes < 1 << 11 || modelMemoryBytes > 32 << 20))
            throw ArchiveEncryptionOptions.Invalid();
        Codec = codec; Filter = filter; ModelOrder = modelOrder; ModelMemoryBytes = modelMemoryBytes;
        DeltaDistance = deltaDistance; HeaderCompression = headerCompression;
    }
}

/// <summary>Current independent RAR5 encoder: algorithm 0, non-solid, no write filter, 128 KiB dictionary.</summary>
public sealed record ArchiveRar5Options
{
    public int AlgorithmVersion { get; }
    public ArchiveRar5Options(int algorithmVersion = 0)
    {
        if (algorithmVersion != 0) throw ArchiveEncryptionOptions.Invalid();
        AlgorithmVersion = algorithmVersion;
    }
}
