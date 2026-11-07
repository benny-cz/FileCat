namespace FileCat.Core.Archives;

/// <summary>Complete currently admitted main/end/header-encryption observations, without secret/key material.</summary>
public sealed record ArchiveRar5Features
{
    public byte MainHeaderFlags { get; }
    public byte EndHeaderFlags { get; }
    public byte EncryptionHeaderFlags { get; }
    public bool HiddenNames { get; }
    public bool? HeaderPasswordCheckPresent { get; }
    public int? HeaderCountPower { get; }
    public ArchiveRar5Features(byte mainHeaderFlags = 0, byte endHeaderFlags = 0, byte encryptionHeaderFlags = 0,
        bool hiddenNames = false, bool? headerPasswordCheckPresent = null, int? headerCountPower = null)
    {
        if ((mainHeaderFlags & ~4) != 0 || (endHeaderFlags & ~4) != 0 || (encryptionHeaderFlags & ~4) != 0 ||
            hiddenNames != (headerPasswordCheckPresent is not null) || hiddenNames != (headerCountPower is not null) ||
            headerCountPower is < 0 or > 24 || !hiddenNames && encryptionHeaderFlags != 0) throw ArchiveRar5Metadata.Invalid();
        MainHeaderFlags = mainHeaderFlags; EndHeaderFlags = endHeaderFlags; EncryptionHeaderFlags = encryptionHeaderFlags;
        HiddenNames = hiddenNames; HeaderPasswordCheckPresent = headerPasswordCheckPresent; HeaderCountPower = headerCountPower;
    }
    public byte[] Encode() => [1, 0, MainHeaderFlags, EndHeaderFlags, EncryptionHeaderFlags, HiddenNames ? (byte)1 : (byte)0,
        HeaderPasswordCheckPresent == true ? (byte)1 : (byte)0, (byte)(HeaderCountPower ?? 0), 0, 0];
    public static ArchiveRar5Features Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 10 || bytes[0] != 1 || bytes[1] != 0 || bytes[5] > 1 || bytes[6] > 1 || bytes[8] != 0 || bytes[9] != 0 ||
            bytes[5] == 0 && (bytes[6] != 0 || bytes[7] != 0)) throw ArchiveRar5Metadata.Invalid();
        return new(bytes[2], bytes[3], bytes[4], bytes[5] != 0, bytes[5] != 0 ? bytes[6] != 0 : null, bytes[5] != 0 ? bytes[7] : null);
    }
}
