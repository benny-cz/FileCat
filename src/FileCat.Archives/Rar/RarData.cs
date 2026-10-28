namespace FileCat.Archives.Rar;

/// <summary>
/// Hard limits of FileCat's RAR reader. RAR data comes from untrusted archives: every size, count and memory request
/// read from one is checked against these before it is used.
/// </summary>
internal static class RarLimits
{
    /// <summary>Largest decompression window FileCat allocates; archives that need more are refused, not attempted.</summary>
    public const long MaxWindowBytes = 1L << 30;

    /// <summary>Smallest window; RAR's own minimum.</summary>
    public const int MinWindowBytes = 1 << 18;

    /// <summary>Most bytes one decoded symbol may add to the window (a RAR 5 match is at most 0x1101 + 3).</summary>
    public const int MaxSymbolOutput = 1 << 13;

    /// <summary>Largest filtered block (RAR 5 encoders never exceed 4 MiB; RAR 3 VM blocks are far smaller).</summary>
    public const int MaxFilterBlockBytes = 4 << 20;

    /// <summary>Filters waiting for their block at once.</summary>
    public const int MaxPendingFilters = 8192;

    /// <summary>Channels of a delta or audio filter.</summary>
    public const int MaxFilterChannels = 1024;

    /// <summary>A RAR 3 PPMd model's memory (the format stores up to 256 MiB).</summary>
    public const int MaxPpmdMemoryBytes = 256 << 20;

    /// <summary>A RAR 5 header, as RAR itself bounds it.</summary>
    public const int MaxHeaderBytes = 2 << 20;

    /// <summary>A member name in bytes.</summary>
    public const int MaxNameBytes = 64 * 1024;

    /// <summary>Members FileCat lists from one archive.</summary>
    public const int MaxMembers = 1_000_000;
}

internal static class RarData
{
    private const string DamageKey = "FileCat.Rar.Damage";
    private const string ChecksumKey = "FileCat.Rar.Checksum";

    public static InvalidDataException Truncated() => Mark(new("The archive's compressed data ends early."), DamageKey);

    public static InvalidDataException Corrupt(string what) => Mark(new("The archive's compressed data is damaged: " + what + "."), DamageKey);

    public static InvalidDataException Unsupported(string what) => Mark(new("FileCat cannot decompress this RAR data: " + what + "."), DamageKey);

    public static InvalidDataException ChecksumMismatch(string message) => Mark(new(message), ChecksumKey);

    /// <summary>True for damaged or unsupported content (not an encrypted list or a checksum mismatch).</summary>
    public static bool IsDamage(Exception ex) => ex.Data.Contains(DamageKey);

    public static bool IsChecksum(Exception ex) => ex.Data.Contains(ChecksumKey);

    private static InvalidDataException Mark(InvalidDataException ex, string key)
    {
        ex.Data[key] = true;
        return ex;
    }
}
