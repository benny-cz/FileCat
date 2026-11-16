namespace FileCat.Archives.Rar;

/// <summary>Where one part of a member's packed data lies.</summary>
internal readonly record struct RarSegment(int Volume, long Offset, long Length);

/// <summary>One member as the headers describe it, its packed data gathered across volumes.</summary>
internal sealed class RarMember
{
    public required string Name { get; init; }
    public MemberKind Kind { get; set; }
    public string? LinkTarget { get; set; }
    public long UnpackedSize { get; set; }      // -1 when the archive does not record it
    public long PackedSize { get; set; }
    public DateTime? ModifiedUtc { get; set; }
    public bool Encrypted { get; set; }
    public Rar5CryptoParameters? Encryption { get; set; }
    public string? EncryptionError { get; set; }
    public int Method { get; set; }              // 0 when stored
    public int Version { get; set; }             // decoder: 15, 20, 26, 29, 36 (RAR 1.5–4.x), 50, 70 (RAR 5/7)
    public long Dictionary { get; set; }
    public bool SolidContinuation { get; set; }  // decodes with the previous member's state
    public uint? Crc32 { get; set; }
    public byte[]? Blake2sp { get; set; }
    public List<RarSegment> Segments { get; } = [];
    public bool HasFirstPart { get; set; }
    public bool HasLastPart { get; set; }
    public bool HasGap { get; set; }            // a part in between is missing: listed once, never decoded
    public bool Complete => HasFirstPart && HasLastPart && !HasGap;
}
