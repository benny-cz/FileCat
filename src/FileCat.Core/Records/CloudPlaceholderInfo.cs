using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Records;

/// <summary>
/// What the Cloud Files API says of a placeholder (CF_PLACEHOLDER_STANDARD_INFO, D-56): how much of its data is on this
/// device, validated by its provider, and changed here; its pin and in-sync states; its file ID and its sync root's; and
/// the identity its sync provider gave it (the provider's own name for it, at most 4 KiB).
/// </summary>
public sealed record CloudPlaceholderInfo(long OnDiskDataSize, long ValidatedDataSize, long ModifiedDataSize, long PropertiesSize,
    int PinState, int InSyncState, long FileId, long SyncRootFileId, byte[] Identity)
{
    /// <summary>The bytes before the identity, and the most an identity can have (CF_PLACEHOLDER_MAX_FILE_IDENTITY_LENGTH).</summary>
    public const int FixedSize = 60, MaxIdentity = 4096;

    /// <summary>
    /// The structure as the API returned it (<paramref name="info"/>: the bytes it said it wrote), or null when it is
    /// shorter than its fixed part. An identity longer than what came with it, or than any placeholder can have, is said
    /// in <paramref name="problems"/>.
    /// </summary>
    public static CloudPlaceholderInfo? Parse(ReadOnlySpan<byte> info, List<string> problems)
    {
        if (info.Length < FixedSize)
        {
            problems.Add($"The Cloud Files API returned {info.Length:N0} bytes, fewer than the {FixedSize} of a placeholder's standard information.");
            return null;
        }
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(info[56..]);
        int available = info.Length - FixedSize;
        byte[] identity = [];
        if (length > MaxIdentity)
            problems.Add($"Its identity is said to be {length:N0} bytes, more than the {MaxIdentity:N0} a placeholder can have; it is not shown.");
        else
        {
            if (length > available) problems.Add($"Its identity is said to be {length:N0} bytes, but only {available:N0} came with it; those are shown.");
            identity = info.Slice(FixedSize, (int)Math.Min(length, available)).ToArray();
        }
        return new CloudPlaceholderInfo(
            BinaryPrimitives.ReadInt64LittleEndian(info), BinaryPrimitives.ReadInt64LittleEndian(info[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(info[16..]), BinaryPrimitives.ReadInt64LittleEndian(info[24..]),
            BinaryPrimitives.ReadInt32LittleEndian(info[32..]), BinaryPrimitives.ReadInt32LittleEndian(info[36..]),
            BinaryPrimitives.ReadInt64LittleEndian(info[40..]), BinaryPrimitives.ReadInt64LittleEndian(info[48..]), identity);
    }

    /// <summary>CF_PIN_STATE in words.</summary>
    public static string PinStateText(int state) => state switch
    {
        0 => "unspecified (its provider decides)",
        1 => "pinned (always kept on this device)",
        2 => "unpinned (to be freed up: kept online only)",
        3 => "excluded (not synced)",
        4 => "inherited from its folder",
        _ => $"unknown ({state})",
    };

    /// <summary>CF_IN_SYNC_STATE in words.</summary>
    public static string InSyncText(int state) => state switch
    {
        0 => "no: it has changes its provider has not synced",
        1 => "yes",
        _ => $"unknown ({state})",
    };
}

/// <summary>
/// What the Cloud Files API says of a sync root (CF_SYNC_ROOT_STANDARD_INFO, D-56): the provider that keeps it, with its
/// version and status, and the policies it registered (how files are downloaded, how folders are filled in).
/// </summary>
public sealed record CloudSyncRootInfo(long FileId, ushort Hydration, ushort HydrationModifier, ushort Population, ushort PopulationModifier,
    uint InSyncPolicy, uint HardLinkPolicy, uint ProviderStatus, string ProviderName, string ProviderVersion, int IdentityLength)
{
    /// <summary>The bytes before the sync root's identity; the name and version are 256 UTF-16 characters each, NUL-terminated.</summary>
    public const int FixedSize = 1056;
    private const int NameAt = 28, VersionAt = 540, TextChars = 256;

    /// <summary>The structure as the API returned it, or null when it is shorter than its fixed part.</summary>
    public static CloudSyncRootInfo? Parse(ReadOnlySpan<byte> info, List<string> problems)
    {
        if (info.Length < FixedSize)
        {
            problems.Add($"The Cloud Files API returned {info.Length:N0} bytes of the sync root's information, fewer than the {FixedSize:N0} of its fixed part.");
            return null;
        }
        uint identity = BinaryPrimitives.ReadUInt32LittleEndian(info[1052..]);
        if (identity > info.Length - FixedSize)
            problems.Add($"The sync root's identity is said to be {identity:N0} bytes, but only {info.Length - FixedSize:N0} came with it.");
        return new CloudSyncRootInfo(BinaryPrimitives.ReadInt64LittleEndian(info),
            BinaryPrimitives.ReadUInt16LittleEndian(info[8..]), BinaryPrimitives.ReadUInt16LittleEndian(info[10..]),
            BinaryPrimitives.ReadUInt16LittleEndian(info[12..]), BinaryPrimitives.ReadUInt16LittleEndian(info[14..]),
            BinaryPrimitives.ReadUInt32LittleEndian(info[16..]), BinaryPrimitives.ReadUInt32LittleEndian(info[20..]),
            BinaryPrimitives.ReadUInt32LittleEndian(info[24..]), Text(info.Slice(NameAt, TextChars * 2)), Text(info.Slice(VersionAt, TextChars * 2)),
            (int)Math.Min(identity, int.MaxValue));
    }

    /// <summary>A fixed array of UTF-16 characters up to its first NUL (or all of it), control characters shown as '?'.</summary>
    private static string Text(ReadOnlySpan<byte> chars)
    {
        var text = new StringBuilder();
        for (int i = 0; i + 1 < chars.Length; i += 2)
        {
            char c = (char)BinaryPrimitives.ReadUInt16LittleEndian(chars[i..]);
            if (c == '\0') break;
            text.Append(char.IsControl(c) ? '?' : c);
        }
        return text.ToString();
    }

    /// <summary>CF_HYDRATION_POLICY: when a placeholder's data is downloaded, and what its provider allows.</summary>
    public string HydrationText
    {
        get
        {
            string primary = Hydration switch
            {
                0 => "partial (only the parts read)",
                1 => "progressive (the parts read first, then the rest)",
                2 => "full (all of it before a read completes)",
                3 => "always full (all of it, and never freed up)",
                _ => $"policy {Hydration}",
            };
            var modifiers = new List<string>();
            if ((HydrationModifier & 1) != 0) modifiers.Add("data validated");
            if ((HydrationModifier & 2) != 0) modifiers.Add("streaming allowed");
            if ((HydrationModifier & 4) != 0) modifiers.Add("freed up automatically");
            if ((HydrationModifier & 8) != 0) modifiers.Add("full restart allowed");
            if ((HydrationModifier & ~15) != 0) modifiers.Add($"modifier bits 0x{HydrationModifier & ~15:X}");
            return modifiers.Count > 0 ? primary + "; " + string.Join(", ", modifiers) : primary;
        }
    }

    /// <summary>CF_POPULATION_POLICY: whether listing a folder asks the provider for its contents.</summary>
    public string PopulationText => Population switch
    {
        0 => "partial (folders are filled in as they are opened)",
        2 => "full (a folder's whole listing comes from its provider when first opened)",
        3 => "always full (folders are always complete on this device)",
        _ => $"policy {Population}",
    };

    /// <summary>CF_SYNC_PROVIDER_STATUS in words.</summary>
    public string StatusText => ProviderStatus switch
    {
        0 => "not connected",
        0xFFFF_FFFE => "terminated",
        0xFFFF_FFFF => "in error",
        _ => string.Join(", ", new (uint Flag, string Text)[]
        {
            (0x1, "idle"), (0x2, "listing folders"), (0x4, "fetching metadata"), (0x8, "fetching content"),
            (0x10, "syncing changes"), (0x20, "syncing everything"), (0x40, "offline (connectivity lost)"),
        }.Where(s => (ProviderStatus & s.Flag) != 0).Select(s => s.Text).DefaultIfEmpty($"status 0x{ProviderStatus:X8}")),
    };
}
