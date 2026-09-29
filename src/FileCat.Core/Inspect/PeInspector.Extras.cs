using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FileCat.Core.Inspect;

/// <summary>
/// The lesser-known parts of a PE file: the DOS header and stub, the Rich header (Microsoft's record of the tools that
/// built the file), the checksum and Authenticode hash computed from the whole file, section entropy and packer names,
/// bound imports, and the signature's certificates, program name, and time stamp. Everything is read, nothing is trusted:
/// a signature's hash can be compared with the file, but its certificates are neither verified nor looked up.
/// </summary>
public static partial class PeInspector
{
    /// <summary>Files up to this size are read whole for the checksum, the Authenticode hash, and section entropy.</summary>
    private const long MaxWholeFile = 128L << 20;

    // ---- DOS header -------------------------------------------------------------------------------------------------

    private static InspectionSection DosHeader(ContentReader r, byte[] dos, uint lfanew)
    {
        var fields = new List<(string, string)>
        {
            ("Magic", "MZ"),
            ("Bytes on the last page", $"{U16(dos, 2):N0}"),
            ("Pages", $"{U16(dos, 4):N0}"),
            ("Relocations", $"{U16(dos, 6):N0}"),
            ("Header size", $"{U16(dos, 8):N0} paragraphs"),
            ("Extra paragraphs", $"{U16(dos, 10):N0} minimum, {U16(dos, 12):N0} maximum"),
            ("Initial SS:SP", $"{U16(dos, 14):X4}:{U16(dos, 16):X4}"),
            ("Checksum", $"0x{U16(dos, 18):X4}"),
            ("Initial CS:IP", $"{U16(dos, 22):X4}:{U16(dos, 20):X4}"),
            ("Relocation table", $"0x{U16(dos, 24):X4}"),
            ("Overlay number", $"{U16(dos, 26)}"),
            ("OEM identifier and information", $"0x{U16(dos, 36):X4}, 0x{U16(dos, 38):X4}"),
            ("PE header offset", $"0x{lfanew:X8}"),
        };
        // The stub's message, the "$"-terminated text a DOS stub prints ("This program cannot be run in DOS mode.").
        var stub = r.Read(64, (int)Math.Min(lfanew - 64, 512));
        for (int i = 0; i < stub.Length; i++)
        {
            int start = i;
            while (i < stub.Length && stub[i] is >= 0x20 and < 0x7F && stub[i] != '$') i++;
            if (i < stub.Length && stub[i] == '$' && i - start >= 12)
            {
                fields.Add(("Stub message", Encoding.ASCII.GetString(stub, start, i - start).Trim()));
                break;
            }
        }
        return new InspectionSection("DOS header", fields);
    }

    // ---- Rich header ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Microsoft's linker writes, between the DOS stub and the PE header, which tools made the objects it linked: each
    /// entry a product (compiler, assembler, linker…) and build number with the count of objects. The entries are masked
    /// with a key that is also a checksum over the DOS header and the entries, so an edited header is noticed.
    /// </summary>
    private static InspectionSection? RichHeader(ContentReader r, uint lfanew, List<string> warnings, out string? builtWith)
    {
        builtWith = null;
        var d = r.Read(0, (int)Math.Min(lfanew, 8192));
        int rich = -1;
        for (int i = 0x80; i + 8 <= d.Length; i += 4)
            if (U32(d, i) == 0x68636952) // "Rich"
            {
                rich = i;
                break;
            }
        if (rich < 0) return null;
        uint key = U32(d, rich + 4);
        int dans = -1;
        for (int i = rich - 16; i >= 0x40; i -= 4)
            if ((U32(d, i) ^ key) == 0x536E6144) // "DanS", masked
            {
                dans = i;
                break;
            }
        if (dans < 0)
        {
            warnings.Add("The Rich header's end is there but not its start: it was damaged or edited.");
            return null;
        }
        var entries = new List<(uint Id, uint Count)>();
        for (int i = dans + 16; i + 8 <= rich; i += 8) entries.Add((U32(d, i) ^ key, U32(d, i + 4) ^ key));
        // The key is a checksum: the offset of the start, each byte of the DOS header before it (without e_lfanew)
        // rotated left by its offset, and each entry rotated left by its count.
        uint sum = (uint)dans;
        for (int i = 0; i < dans; i++)
            if (i is < 0x3C or >= 0x40) sum += uint.RotateLeft(d[i], i);
        foreach (var (id, count) in entries) sum += uint.RotateLeft(id, (int)(count & 31));
        bool padding = (U32(d, dans + 4) ^ key) == 0 && (U32(d, dans + 8) ^ key) == 0 && (U32(d, dans + 12) ^ key) == 0;
        var rows = new List<string[]>();
        (int Rank, string Toolset)? best = null;
        foreach (var (id, count) in entries)
        {
            ushort product = (ushort)(id >> 16), build = (ushort)id;
            var (tool, toolset, rank) = RichProduct(product, build);
            rows.Add([tool, toolset, $"{build}", $"{count:N0}", $"0x{product:X4}"]);
            // The linker's own entry says what built the file; without one, the newest tool.
            int weight = tool.StartsWith("Linker", StringComparison.Ordinal) ? rank + 100_000 : rank;
            if (toolset.Length > 0 && (best is null || weight > best.Value.Rank)) best = (weight, toolset);
        }
        builtWith = best?.Toolset;
        return new InspectionSection("Rich header (build tools)", [
            ("Key", $"0x{key:X8}"),
            ("Checksum", sum == key ? "matches: the header is as the linker wrote it" : $"does not match (0x{sum:X8}): the header was edited or forged"),
            ("Padding", padding ? "zero, as written" : "not zero: unusual, possibly edited"),
            ("Location", $"0x{dans:X} to 0x{rich + 8:X} ({entries.Count} entries)"),
        ]) { Table = new InspectionTable(["Tool", "Toolset", "Build", "Objects", "Product"], rows) };
    }

    /// <summary>What a Rich header product and build are: the tool, the Visual Studio release it came with, and its rank in time.</summary>
    private static (string Tool, string Toolset, int Rank) RichProduct(ushort product, ushort build)
    {
        string name = product < RichProducts.Length ? RichProducts[product] : "";
        if (product == 0) return ("Objects without a tool record", "", 0);
        if (name.Length == 0) return ($"product 0x{product:X4}", product > 0x010E ? "newer than FileCat knows" : "", product);
        string tool = name switch
        {
            "Import0" => "Imports (older import libraries)",
            "Resource" => "Resources",
            "VisualBasic60" => "Visual Basic 6",
            "ILAsm100" => "IL assembler",
            "PhoenixPrerelease" => "Phoenix compiler (prerelease)",
            _ when name.StartsWith("Linker", StringComparison.Ordinal) => "Linker",
            _ when name.StartsWith("Cvtres", StringComparison.Ordinal) => "Resource converter",
            _ when name.StartsWith("Cvtomf", StringComparison.Ordinal) => "OMF converter",
            _ when name.StartsWith("Cvtpgd", StringComparison.Ordinal) => "Profile database converter",
            _ when name.StartsWith("Masm", StringComparison.Ordinal) => "Assembler (MASM)",
            _ when name.StartsWith("Implib", StringComparison.Ordinal) => "Import library",
            _ when name.StartsWith("Export", StringComparison.Ordinal) => "Exports",
            _ when name.StartsWith("AliasObj", StringComparison.Ordinal) => "Alias object",
            _ when name.StartsWith("Utc", StringComparison.Ordinal) || name.StartsWith("Phx", StringComparison.Ordinal) => Compiler(name),
            _ => name,
        };
        string toolset = product switch
        {
            < 0x0019 => "Visual C++ 6.0 or earlier",
            < 0x0046 => "Visual Studio .NET 2002",
            < 0x005A => "Visual Studio .NET 2003 (prerelease)",
            < 0x006D => "Visual Studio .NET 2003",
            < 0x0083 => "Visual Studio 2005",
            < 0x0098 => "Visual Studio 2008",
            < 0x00B5 => "Visual Studio 2010",
            < 0x00C7 => "Visual Studio 2010 SP1",
            < 0x00D9 => "Visual Studio 2012",
            < 0x00EB => "Visual Studio 2013",
            < 0x00FD => "Visual Studio 2015 (preview)",
            // From 2015 on, the tools keep their product numbers and only the build tells the releases apart.
            _ => build switch
            {
                0 => "Visual Studio 2015 or later",
                < 25000 => "Visual Studio 2015",
                < 27500 => "Visual Studio 2017",
                < 30400 => "Visual Studio 2019",
                < 35500 => "Visual Studio 2022",
                _ => "Visual Studio 2026",
            },
        };
        // Import entries and resources carry no build of their own.
        if (name is "Import0" or "Resource") toolset = "";
        return (tool, toolset, product * 65536 + build);
    }

    private static string Compiler(string name)
    {
        string language = name.EndsWith("_CPP", StringComparison.Ordinal) ? "C++" : name.EndsWith("_MSIL", StringComparison.Ordinal) ? "MSIL" : name.EndsWith("_Basic", StringComparison.Ordinal) ? "Basic" : "C";
        string how = name.Contains("_LTCG_", StringComparison.Ordinal) ? " (whole-program optimization)"
            : name.Contains("_POGO_I_", StringComparison.Ordinal) ? " (profile instrumented)"
            : name.Contains("_POGO_O_", StringComparison.Ordinal) ? " (profile optimized)"
            : name.Contains("_CVTCIL_", StringComparison.Ordinal) ? " (CIL)"
            : name.Contains("_Std", StringComparison.Ordinal) ? " (standard edition)"
            : name.Contains("_Book", StringComparison.Ordinal) ? " (learning edition)" : "";
        return $"{language} compiler{how}";
    }

    /// <summary>The products a Rich header names, by number, as Microsoft's tools number them (0x0000–0x010E).</summary>
    private static readonly string[] RichProducts =
    [
        "", "Import0", "Linker510", "Cvtomf510", "Linker600", "Cvtomf600", "Cvtres500", "Utc11_Basic", "Utc11_C", "Utc12_Basic", "Utc12_C", "Utc12_CPP",
        "AliasObj60", "VisualBasic60", "Masm613", "Masm710", "Linker511", "Cvtomf511", "Masm614", "Linker512", "Cvtomf512", "Utc12_C_Std", "Utc12_CPP_Std",
        "Utc12_C_Book", "Utc12_CPP_Book", "Implib700", "Cvtomf700", "Utc13_Basic", "Utc13_C", "Utc13_CPP", "Linker610", "Cvtomf610", "Linker601", "Cvtomf601",
        "Utc12_1_Basic", "Utc12_1_C", "Utc12_1_CPP", "Linker620", "Cvtomf620", "AliasObj70", "Linker621", "Cvtomf621", "Masm615", "Utc13_LTCG_C",
        "Utc13_LTCG_CPP", "Masm620", "ILAsm100", "Utc12_2_Basic", "Utc12_2_C", "Utc12_2_CPP", "Utc12_2_C_Std", "Utc12_2_CPP_Std", "Utc12_2_C_Book",
        "Utc12_2_CPP_Book", "Implib622", "Cvtomf622", "Cvtres501", "Utc13_C_Std", "Utc13_CPP_Std", "Cvtpgd1300", "Linker622", "Linker700", "Export622",
        "Export700", "Masm700", "Utc13_POGO_I_C", "Utc13_POGO_I_CPP", "Utc13_POGO_O_C", "Utc13_POGO_O_CPP", "Cvtres700", "Cvtres710p", "Linker710p",
        "Cvtomf710p", "Export710p", "Implib710p", "Masm710p", "Utc1310p_C", "Utc1310p_CPP", "Utc1310p_C_Std", "Utc1310p_CPP_Std", "Utc1310p_LTCG_C",
        "Utc1310p_LTCG_CPP", "Utc1310p_POGO_I_C", "Utc1310p_POGO_I_CPP", "Utc1310p_POGO_O_C", "Utc1310p_POGO_O_CPP", "Linker624", "Cvtomf624", "Export624",
        "Implib624", "Linker710", "Cvtomf710", "Export710", "Implib710", "Cvtres710", "Utc1310_C", "Utc1310_CPP", "Utc1310_C_Std", "Utc1310_CPP_Std",
        "Utc1310_LTCG_C", "Utc1310_LTCG_CPP", "Utc1310_POGO_I_C", "Utc1310_POGO_I_CPP", "Utc1310_POGO_O_C", "Utc1310_POGO_O_CPP", "AliasObj710",
        "AliasObj710p", "Cvtpgd1310", "Cvtpgd1310p", "Utc1400_C", "Utc1400_CPP", "Utc1400_C_Std", "Utc1400_CPP_Std", "Utc1400_LTCG_C", "Utc1400_LTCG_CPP",
        "Utc1400_POGO_I_C", "Utc1400_POGO_I_CPP", "Utc1400_POGO_O_C", "Utc1400_POGO_O_CPP", "Cvtpgd1400", "Linker800", "Cvtomf800", "Export800", "Implib800",
        "Cvtres800", "Masm800", "AliasObj800", "PhoenixPrerelease", "Utc1400_CVTCIL_C", "Utc1400_CVTCIL_CPP", "Utc1400_LTCG_MSIL", "Utc1500_C", "Utc1500_CPP",
        "Utc1500_C_Std", "Utc1500_CPP_Std", "Utc1500_CVTCIL_C", "Utc1500_CVTCIL_CPP", "Utc1500_LTCG_C", "Utc1500_LTCG_CPP", "Utc1500_LTCG_MSIL",
        "Utc1500_POGO_I_C", "Utc1500_POGO_I_CPP", "Utc1500_POGO_O_C", "Utc1500_POGO_O_CPP", "Cvtpgd1500", "Linker900", "Export900", "Implib900", "Cvtres900",
        "Masm900", "AliasObj900", "Resource", "AliasObj1000", "Cvtpgd1600", "Cvtres1000", "Export1000", "Implib1000", "Linker1000", "Masm1000", "Phx1600_C",
        "Phx1600_CPP", "Phx1600_CVTCIL_C", "Phx1600_CVTCIL_CPP", "Phx1600_LTCG_C", "Phx1600_LTCG_CPP", "Phx1600_LTCG_MSIL", "Phx1600_POGO_I_C",
        "Phx1600_POGO_I_CPP", "Phx1600_POGO_O_C", "Phx1600_POGO_O_CPP", "Utc1600_C", "Utc1600_CPP", "Utc1600_CVTCIL_C", "Utc1600_CVTCIL_CPP",
        "Utc1600_LTCG_C", "Utc1600_LTCG_CPP", "Utc1600_LTCG_MSIL", "Utc1600_POGO_I_C", "Utc1600_POGO_I_CPP", "Utc1600_POGO_O_C", "Utc1600_POGO_O_CPP",
        "AliasObj1010", "Cvtpgd1610", "Cvtres1010", "Export1010", "Implib1010", "Linker1010", "Masm1010", "Utc1610_C", "Utc1610_CPP", "Utc1610_CVTCIL_C",
        "Utc1610_CVTCIL_CPP", "Utc1610_LTCG_C", "Utc1610_LTCG_CPP", "Utc1610_LTCG_MSIL", "Utc1610_POGO_I_C", "Utc1610_POGO_I_CPP", "Utc1610_POGO_O_C",
        "Utc1610_POGO_O_CPP", "AliasObj1100", "Cvtpgd1700", "Cvtres1100", "Export1100", "Implib1100", "Linker1100", "Masm1100", "Utc1700_C", "Utc1700_CPP",
        "Utc1700_CVTCIL_C", "Utc1700_CVTCIL_CPP", "Utc1700_LTCG_C", "Utc1700_LTCG_CPP", "Utc1700_LTCG_MSIL", "Utc1700_POGO_I_C", "Utc1700_POGO_I_CPP",
        "Utc1700_POGO_O_C", "Utc1700_POGO_O_CPP", "AliasObj1200", "Cvtpgd1800", "Cvtres1200", "Export1200", "Implib1200", "Linker1200", "Masm1200",
        "Utc1800_C", "Utc1800_CPP", "Utc1800_CVTCIL_C", "Utc1800_CVTCIL_CPP", "Utc1800_LTCG_C", "Utc1800_LTCG_CPP", "Utc1800_LTCG_MSIL",
        "Utc1800_POGO_I_C", "Utc1800_POGO_I_CPP", "Utc1800_POGO_O_C", "Utc1800_POGO_O_CPP", "AliasObj1210", "Cvtpgd1810", "Cvtres1210", "Export1210",
        "Implib1210", "Linker1210", "Masm1210", "Utc1810_C", "Utc1810_CPP", "Utc1810_CVTCIL_C", "Utc1810_CVTCIL_CPP", "Utc1810_LTCG_C", "Utc1810_LTCG_CPP",
        "Utc1810_LTCG_MSIL", "Utc1810_POGO_I_C", "Utc1810_POGO_I_CPP", "Utc1810_POGO_O_C", "Utc1810_POGO_O_CPP", "AliasObj1400", "Cvtpgd1900", "Cvtres1400",
        "Export1400", "Implib1400", "Linker1400", "Masm1400", "Utc1900_C", "Utc1900_CPP", "Utc1900_CVTCIL_C", "Utc1900_CVTCIL_CPP", "Utc1900_LTCG_C",
        "Utc1900_LTCG_CPP", "Utc1900_LTCG_MSIL", "Utc1900_POGO_I_C", "Utc1900_POGO_I_CPP", "Utc1900_POGO_O_C", "Utc1900_POGO_O_CPP",
    ];

    // ---- The whole file: checksum, Authenticode hash, entropy -------------------------------------------------------

    private sealed record WholeFile(uint Checksum, byte[]? Hash, double?[] Entropy);

    /// <summary>
    /// Reads the file (up to <see cref="MaxWholeFile"/>) for the PE checksum as the loader computes it, the
    /// Authenticode hash (the file without its checksum, certificate directory entry, and certificates, sections in file
    /// order, as the signature hashes it), and each section's entropy (near 8 bits per byte: compressed or encrypted).
    /// </summary>
    private static WholeFile? ReadWholeFile(ContentReader r, long checksumAt, long certEntryAt, uint headersSize, List<Section> sections,
        (uint Offset, uint Size) certificates, HashAlgorithmName? hashAlgorithm, CancellationToken ct)
    {
        long length = r.Length;
        if (length <= 0 || length > MaxWholeFile) return null;
        const int Chunk = 1 << 20;
        // The checksum: 32-bit words summed with the carries folded back, the checksum field left out, plus the length.
        ulong sum = 0;
        for (long at = 0; at < length; at += Chunk)
        {
            ct.ThrowIfCancellationRequested();
            var block = r.Read(at, (int)Math.Min(Chunk, length - at));
            if (block.Length == 0) return null;
            int words = (block.Length + 3) / 4;
            for (int i = 0; i < words; i++)
            {
                long offset = at + i * 4L;
                if (offset == (checksumAt & ~3L)) continue;
                uint word = i * 4 + 4 <= block.Length ? BitConverter.ToUInt32(block, i * 4) : Tail(block, i * 4);
                sum += word;
                if (sum >= 1UL << 32) sum = (sum & 0xFFFFFFFF) + (sum >> 32);
            }
        }
        uint folded = (uint)((sum & 0xFFFF) + (sum >> 16));
        folded = (folded + (folded >> 16)) & 0xFFFF;
        uint checksum = (uint)(folded + length);

        var entropy = new double?[sections.Count];
        byte[]? hash = null;
        using var hasher = hashAlgorithm is { } algorithm ? IncrementalHash.CreateHash(algorithm) : null;
        void Hash(long from, long to, long[]? histogram = null)
        {
            to = Math.Min(to, length);
            for (long at = from; at < to; at += Chunk)
            {
                ct.ThrowIfCancellationRequested();
                var block = r.Read(at, (int)Math.Min(Chunk, to - at));
                if (block.Length == 0) break;
                hasher?.AppendData(block);
                if (histogram is not null) foreach (byte b in block) histogram[b]++;
            }
        }
        Hash(0, checksumAt);
        Hash(checksumAt + 4, certEntryAt);
        Hash(certEntryAt + 8, headersSize);
        long hashed = headersSize;
        foreach (var (section, index) in sections.Select((s, i) => (s, i)).Where(x => x.s.RawSize > 0).OrderBy(x => x.s.RawPtr))
        {
            var histogram = new long[256];
            Hash(section.RawPtr, (long)section.RawPtr + section.RawSize, histogram);
            long total = histogram.Sum();
            if (total > 0) entropy[index] = -histogram.Where(c => c > 0).Sum(c => (double)c / total * Math.Log2((double)c / total));
            hashed += section.RawSize;
        }
        // Data after the sections, less the certificates at the end.
        long extra = length - (certificates.Size > 0 ? certificates.Size : 0) - hashed;
        if (extra > 0) Hash(hashed, hashed + extra);
        if (hasher is not null) hash = hasher.GetHashAndReset();
        return new WholeFile(checksum, hash, entropy);

        static uint Tail(byte[] block, int at)
        {
            uint word = 0;
            for (int k = 0; at + k < block.Length; k++) word |= (uint)block[at + k] << (8 * k);
            return word;
        }
    }

    /// <summary>Packers and protectors that name their sections recognizably.</summary>
    private static string? Packer(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            string? packer = name switch
            {
                "UPX0" or "UPX1" or "UPX2" or "UPX!" => "UPX",
                ".aspack" or ".adata" => "ASPack",
                ".MPRESS1" or ".MPRESS2" => "MPRESS",
                ".themida" or ".winlice" => "Themida or WinLicense",
                ".vmp0" or ".vmp1" or ".vmp2" => "VMProtect",
                ".enigma1" or ".enigma2" => "Enigma Protector",
                ".nsp0" or ".nsp1" or ".nsp2" => "NsPack",
                "PEC2" or "PECompact2" or "pec1" or "pec2" => "PECompact",
                ".petite" => "Petite",
                ".pelock" => "PELock",
                ".ndata" => "an NSIS installer",
                _ => null,
            };
            if (packer is not null) return packer;
        }
        return null;
    }

    // ---- Bound imports ----------------------------------------------------------------------------------------------

    private static InspectionSection? BoundImports(Image img, (uint Rva, uint Size) dir)
    {
        long at = img.Offset(dir.Rva);
        if (at < 0) return null;
        var d = img.R.Read(at, (int)Math.Min(dir.Size == 0 ? 4096 : dir.Size, 64 * 1024));
        var rows = new List<string[]>();
        for (int i = 0; i + 8 <= d.Length && rows.Count < 1000;)
        {
            uint stamp = U32(d, i);
            ushort name = U16(d, i + 4), forwarders = U16(d, i + 6);
            if (stamp == 0 && name == 0) break;
            rows.Add([name < d.Length ? Ascii(d.AsSpan(name)) : "(unreadable)", Stamp(stamp), $"{forwarders}"]);
            i += 8 + forwarders * 8;
        }
        return new InspectionSection($"Bound imports ({rows.Count})", []) { Table = new InspectionTable(["Library", "Time date stamp", "Forwarders"], rows) };
    }

    // ---- Signature --------------------------------------------------------------------------------------------------

    private sealed record Signature(List<(string, string)> Fields, List<string[]> Certificates, HashAlgorithmName? Algorithm, byte[]? SignedHash);

    /// <summary>
    /// The Authenticode signature: who signed (the certificate named by the signer), with which hash, the program name,
    /// the time stamp, the hash the signature covers, and every certificate included. Read, not verified.
    /// </summary>
    private static Signature? ReadSignature(ContentReader r, (uint Offset, uint Size) dir, List<string> warnings)
    {
        var table = r.Read(dir.Offset, (int)Math.Min(dir.Size, 4u << 20));
        if (table.Length < 8) return null;
        var fields = new List<(string, string)>();
        var certificates = new List<string[]>();
        HashAlgorithmName? algorithm = null;
        byte[]? signedHash = null;
        int signatures = 0;
        for (int at = 0; at + 8 <= table.Length && signatures < 8;)
        {
            int length = (int)Math.Min(U32(table, at), (uint)(table.Length - at));
            if (length < 8) break;
            if (U16(table, at + 6) == 2)
            {
                try
                {
                    Pkcs7(table.AsMemory(at + 8, length - 8), fields, certificates, ref algorithm, ref signedHash, ref signatures, nested: 0);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A damaged or hostile signature is said, never thrown.
                    warnings.Add("The signature could not be read: " + ex.Message);
                }
            }
            at += (length + 7) & ~7;
        }
        if (signatures > 1) fields.Add(("Signatures", $"{signatures} (the others nested in the first)"));
        return new Signature(fields, certificates, algorithm, signedHash);
    }

    private const string SignedDataOid = "1.2.840.113549.1.7.2";
    private const string NestedSignatureOid = "1.3.6.1.4.1.311.2.4.1";

    private static void Pkcs7(ReadOnlyMemory<byte> blob, List<(string, string)> fields, List<string[]> certificates, ref HashAlgorithmName? algorithm,
        ref byte[]? signedHash, ref int signatures, int nested)
    {
        var contentInfo = new AsnReader(blob, AsnEncodingRules.BER).ReadSequence();
        if (contentInfo.ReadObjectIdentifier() != SignedDataOid) return;
        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        signatures++;
        signedData.ReadInteger();
        signedData.ReadSetOf();
        // SpcIndirectDataContent: the hash of the file that was signed.
        var encapsulated = signedData.ReadSequence();
        encapsulated.ReadObjectIdentifier();
        byte[]? digest = null;
        string? digestOid = null;
        if (encapsulated.HasData)
        {
            var indirect = encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
            indirect.ReadEncodedValue();
            var digestInfo = indirect.ReadSequence();
            digestOid = digestInfo.ReadSequence().ReadObjectIdentifier();
            digest = digestInfo.ReadOctetString();
        }
        var loaded = new List<X509Certificate2>();
        try
        {
            if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
            {
                var set = signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
                while (set.HasData && loaded.Count < 32)
                {
                    var der = set.ReadEncodedValue();
                    if (Asn1Tag.Decode(der.Span, out _).HasSameClassAndValue(Asn1Tag.Sequence)) loaded.Add(X509CertificateLoader.LoadCertificate(der.Span));
                }
            }
            if (signedData.HasData && signedData.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1))) signedData.ReadEncodedValue();
            string prefix = nested == 0 ? "" : "Nested signature: ";
            var signerInfos = signedData.ReadSetOf();
            if (signerInfos.HasData)
            {
                var signer = signerInfos.ReadSequence();
                signer.ReadInteger();
                var sid = signer.ReadSequence();
                byte[] issuer = sid.ReadEncodedValue().ToArray();
                var serial = sid.ReadIntegerBytes().ToArray();
                string digestAlgorithm = HashName(signer.ReadSequence().ReadObjectIdentifier());
                var signingCertificate = loaded.FirstOrDefault(c => c.IssuerName.RawData.AsSpan().SequenceEqual(issuer) && TrimZeros(c.SerialNumberBytes.Span).SequenceEqual(TrimZeros(serial)));
                if (signingCertificate is not null)
                {
                    fields.Add((prefix + "Signed by", signingCertificate.GetNameInfo(X509NameType.SimpleName, false)));
                    fields.Add((prefix + "Issued by", signingCertificate.GetNameInfo(X509NameType.SimpleName, true)));
                    fields.Add((prefix + "Certificate valid", $"{signingCertificate.NotBefore.ToUniversalTime():yyyy-MM-dd} to {signingCertificate.NotAfter.ToUniversalTime():yyyy-MM-dd}"));
                }
                fields.Add((prefix + "Hash", digestAlgorithm));
                if (signer.HasData && signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    var attributes = signer.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
                    while (attributes.HasData)
                    {
                        var attribute = attributes.ReadSequence();
                        string oid = attribute.ReadObjectIdentifier();
                        var values = attribute.ReadSetOf();
                        if (oid == "1.3.6.1.4.1.311.2.1.12" && values.HasData && ProgramName(values.ReadSequence()) is { Length: > 0 } program)
                            fields.Add((prefix + "Program name", Printable(program)));
                    }
                }
                signer.ReadSequence();
                signer.ReadOctetString();
                if (signer.HasData && signer.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1)))
                {
                    var attributes = signer.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 1));
                    while (attributes.HasData)
                    {
                        var attribute = attributes.ReadSequence();
                        string oid = attribute.ReadObjectIdentifier();
                        var values = attribute.ReadSetOf();
                        switch (oid)
                        {
                            case "1.2.840.113549.1.9.6" when values.HasData: // a counter-signature: its signing time
                                if (CounterSignatureTime(values.ReadSequence()) is { } when) fields.Add((prefix + "Time stamp", $"{when:u}"));
                                break;
                            case "1.3.6.1.4.1.311.3.3.1" when values.HasData: // an RFC 3161 time stamp
                                if (TimeStampTime(values.ReadEncodedValue()) is { } time) fields.Add((prefix + "Time stamp", $"{time:u} (RFC 3161)"));
                                break;
                            case NestedSignatureOid when nested == 0:
                                while (values.HasData)
                                {
                                    var inner = values.ReadEncodedValue();
                                    HashAlgorithmName? ignored = null;
                                    byte[]? ignoredHash = null;
                                    Pkcs7(inner, fields, certificates, ref ignored, ref ignoredHash, ref signatures, nested + 1);
                                }
                                break;
                        }
                    }
                }
                if (nested == 0 && digestOid is not null)
                {
                    algorithm = digestOid switch
                    {
                        "1.3.14.3.2.26" => HashAlgorithmName.SHA1,
                        "2.16.840.1.101.3.4.2.1" => HashAlgorithmName.SHA256,
                        "2.16.840.1.101.3.4.2.2" => HashAlgorithmName.SHA384,
                        "2.16.840.1.101.3.4.2.3" => HashAlgorithmName.SHA512,
                        _ => null,
                    };
                    signedHash = digest;
                }
            }
            foreach (var certificate in loaded)
                certificates.Add([certificate.GetNameInfo(X509NameType.SimpleName, false), certificate.GetNameInfo(X509NameType.SimpleName, true),
                    $"{certificate.NotBefore.ToUniversalTime():yyyy-MM-dd}", $"{certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}", certificate.Thumbprint]);
        }
        finally
        {
            foreach (var certificate in loaded) certificate.Dispose();
        }
    }

    private static ReadOnlySpan<byte> TrimZeros(ReadOnlySpan<byte> serial)
    {
        int i = 0;
        while (i < serial.Length - 1 && serial[i] == 0) i++;
        return serial[i..];
    }

    private static string HashName(string oid) => oid switch
    {
        "1.3.14.3.2.26" => "SHA-1",
        "2.16.840.1.101.3.4.2.1" => "SHA-256",
        "2.16.840.1.101.3.4.2.2" => "SHA-384",
        "2.16.840.1.101.3.4.2.3" => "SHA-512",
        "1.2.840.113549.2.5" => "MD5",
        _ => oid,
    };

    /// <summary>SpcSpOpusInfo's program name: a BMP or IA5 string under a [0] tag.</summary>
    private static string? ProgramName(AsnReader opus)
    {
        if (!opus.HasData || !opus.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0))) return null;
        var name = opus.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
        var tag = name.PeekTag();
        if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0))) return name.ReadCharacterString(UniversalTagNumber.BMPString, new Asn1Tag(TagClass.ContextSpecific, 0));
        if (tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 1))) return name.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 1));
        return null;
    }

    /// <summary>A PKCS #9 counter-signature's signing time (a SignerInfo's authenticated attribute).</summary>
    private static DateTimeOffset? CounterSignatureTime(AsnReader signerInfo)
    {
        signerInfo.ReadInteger();
        signerInfo.ReadEncodedValue();
        signerInfo.ReadSequence();
        if (!signerInfo.HasData || !signerInfo.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0))) return null;
        var attributes = signerInfo.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
        while (attributes.HasData)
        {
            var attribute = attributes.ReadSequence();
            if (attribute.ReadObjectIdentifier() != "1.2.840.113549.1.9.5") continue;
            var values = attribute.ReadSetOf();
            return values.PeekTag().HasSameClassAndValue(new Asn1Tag(UniversalTagNumber.UtcTime)) ? values.ReadUtcTime() : values.ReadGeneralizedTime();
        }
        return null;
    }

    /// <summary>An RFC 3161 time-stamp token's time: SignedData → TSTInfo → genTime.</summary>
    private static DateTimeOffset? TimeStampTime(ReadOnlyMemory<byte> token)
    {
        var contentInfo = new AsnReader(token, AsnEncodingRules.BER).ReadSequence();
        if (contentInfo.ReadObjectIdentifier() != SignedDataOid) return null;
        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        signedData.ReadInteger();
        signedData.ReadSetOf();
        var encapsulated = signedData.ReadSequence();
        encapsulated.ReadObjectIdentifier();
        var info = new AsnReader(encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadOctetString(), AsnEncodingRules.DER).ReadSequence();
        info.ReadInteger();
        info.ReadObjectIdentifier();
        info.ReadSequence();
        info.ReadIntegerBytes();
        return info.ReadGeneralizedTime();
    }
}
