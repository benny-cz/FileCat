using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// Portable Executable (PE/COFF) inspector (plan §16.1): headers, architecture, security flags, sections, imports and
/// exports, the .NET header, version information, the manifest's execution level, the debug file name, and whether an
/// Authenticode signature is present. A static parser: the file is never loaded as a module, and a present signature is
/// reported as present, not as verified or trusted.
/// </summary>
public static class PeInspector
{
    private const int MaxSections = 96;
    private const int MaxImports = 2000;
    private const int MaxFunctionsPerImport = 10_000;
    private const int MaxImportEntries = 200_000; // across all libraries: a hostile table cannot make counting slow
    private const int MaxExportNames = 200;
    private const int MaxResource = 64 * 1024;

    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct)
    {
        var r = new ContentReader(source);
        var dos = r.Read(0, 64);
        if (dos.Length < 64 || dos[0] != 'M' || dos[1] != 'Z') return null;
        var warnings = new List<string>();
        var sections = new List<InspectionSection>();
        uint lfanew = U32(dos, 0x3C);
        if (lfanew < 64 || r.Length >= 0 && lfanew + 24 > r.Length || lfanew > 16 * 1024 * 1024)
            return new InspectionReport("DOS executable (MZ)", [], ["The PE header offset points outside the file: this is a DOS program or a damaged file."]);
        var nt = r.Read(lfanew, 24 + 240);
        if (nt.Length < 24 || nt[0] != 'P' || nt[1] != 'E' || nt[2] != 0 || nt[3] != 0)
            return new InspectionReport("DOS executable (MZ)", [], ["No PE signature: a DOS program, or another executable format such as NE or LE."]);

        ushort machine = U16(nt, 4), count = U16(nt, 6), optionalSize = U16(nt, 20), characteristics = U16(nt, 22);
        uint stamp = U32(nt, 8);
        var header = new List<(string, string)>
        {
            ("Architecture", Machine(machine)),
            ("Kind", (characteristics & 0x2000) != 0 ? "Dynamic-link library (DLL)" : (characteristics & 0x0002) != 0 ? "Executable" : "Object or damaged image"),
            ("Timestamp field", stamp == 0 ? "0"
                : stamp < 0x80000000 ? $"{DateTimeOffset.FromUnixTimeSeconds(stamp):u} (reproducible builds store a hash here instead)"
                : $"0x{stamp:X8} (not a plausible date: a reproducible-build hash)"),
        };
        if ((characteristics & 0x0020) != 0) header.Add(("Large address aware", "yes"));
        ushort magic = optionalSize >= 2 ? U16(nt, 24) : (ushort)0;
        bool pe64 = magic == 0x20B;
        if (magic is not (0x10B or 0x20B))
        {
            warnings.Add("The optional header is missing or of an unknown kind.");
            sections.Add(new InspectionSection("Header", header));
            return new InspectionReport("PE image", sections, warnings);
        }
        var opt = nt.AsSpan(24);
        header.Add(("Format", pe64 ? "PE32+ (64-bit)" : "PE32 (32-bit)"));
        header.Add(("Subsystem", Subsystem(U16(opt, 68))));
        ushort dll = U16(opt, 70);
        header.Add(("Entry point (RVA)", $"0x{U32(opt, 16):X8}"));
        header.Add(("Image base", pe64 ? $"0x{BinaryPrimitives.ReadUInt64LittleEndian(opt[24..]):X16}" : $"0x{U32(opt, 28):X8}"));
        header.Add(("Image size", $"{U32(opt, 56):N0} bytes"));
        header.Add(("OS version required", $"{U16(opt, 40)}.{U16(opt, 42)}"));
        sections.Add(new InspectionSection("Header", header));
        sections.Add(new InspectionSection("Security features", DllFlags(dll, pe64)));

        int dirCount = (int)Math.Min(16, U32(opt, pe64 ? 108 : 92));
        int dirBase = pe64 ? 112 : 96;
        var dirs = new (uint Rva, uint Size)[16];
        for (int i = 0; i < dirCount && dirBase + i * 8 + 8 <= opt.Length; i++) dirs[i] = (U32(opt, dirBase + i * 8), U32(opt, dirBase + i * 8 + 4));

        // Sections: also the map from virtual addresses to file offsets.
        var table = new List<(string Name, uint Va, uint VSize, uint RawSize, uint RawPtr, uint Flags)>();
        long sectionTable = lfanew + 24 + optionalSize;
        if (count > MaxSections) warnings.Add($"The header declares {count} sections; only the first {MaxSections} are read.");
        var raw = r.Read(sectionTable, Math.Min((int)count, MaxSections) * 40);
        for (int i = 0; i + 40 <= raw.Length; i += 40)
        {
            string name = Encoding.ASCII.GetString(raw, i, 8).TrimEnd('\0');
            table.Add((Printable(name), U32(raw, i + 12), U32(raw, i + 8), U32(raw, i + 16), U32(raw, i + 20), U32(raw, i + 36)));
        }
        if (table.Count < Math.Min((int)count, MaxSections)) warnings.Add("The section table is cut off by the end of the file.");
        sections.Add(new InspectionSection($"Sections ({table.Count})", table.Select(s => (s.Name.Length == 0 ? "(no name)" : s.Name,
            $"{SectionFlags(s.Flags),-9} virtual 0x{s.Va:X8} size {s.VSize:N0}, file {s.RawSize:N0} bytes")).ToList()));
        foreach (var s in table)
        {
            if (r.Length >= 0 && s.RawSize > 0 && (long)s.RawPtr + s.RawSize > r.Length) warnings.Add($"Section {s.Name} extends past the end of the file.");
            if ((s.Flags & 0x20000000) != 0 && (s.Flags & 0x80000000) != 0) warnings.Add($"Section {s.Name} is both writable and executable.");
        }
        long Offset(uint rva)
        {
            foreach (var s in table)
            {
                uint size = Math.Max(s.VSize, s.RawSize);
                if (rva >= s.Va && rva < s.Va + size) return s.RawPtr + (rva - s.Va);
            }
            return -1;
        }

        ct.ThrowIfCancellationRequested();
        if (dirs[14].Rva != 0 && Offset(dirs[14].Rva) is var cli and >= 0)
        {
            var cor = r.Read(cli, 72);
            if (cor.Length >= 20)
            {
                uint flags = U32(cor, 16);
                sections.Add(new InspectionSection(".NET", [
                    ("Runtime header", $"{U16(cor, 4)}.{U16(cor, 6)}"),
                    ("IL only", (flags & 1) != 0 ? "yes" : "no (contains native code)"),
                    ("32-bit", (flags & 2) != 0 ? "required" : (flags & 0x20000) != 0 ? "preferred" : "not required"),
                    ("Strong-name signed", (flags & 8) != 0 ? "flag set (not verified)" : "no"),
                ]));
            }
        }
        if (dirs[1].Rva != 0) sections.Add(Imports(r, Offset, dirs[1].Rva, pe64, warnings, ct));
        if (dirs[0].Rva != 0) sections.Add(Exports(r, Offset, dirs[0].Rva, warnings));
        if (dirs[2].Rva != 0) sections.AddRange(Resources(r, Offset, dirs[2].Rva, warnings, ct));
        if (dirs[6].Rva != 0 && Debug(r, Offset, dirs[6]) is { } pdb) sections.Add(new InspectionSection("Debug", [("Symbols file", pdb)]));
        // The security directory holds a file offset, not an address.
        if (dirs[4].Size > 0)
        {
            var cert = r.Read(dirs[4].Rva, 8);
            string kind = cert.Length == 8 ? U16(cert, 6) switch { 2 => "Authenticode (PKCS #7)", 1 => "X.509", _ => $"type {U16(cert, 6)}" } : "unreadable";
            sections.Add(new InspectionSection("Signature", [
                ("Status", "Present, not verified: FileCat does not check signatures or trust here"),
                ("Kind", kind),
                ("Size", $"{dirs[4].Size:N0} bytes"),
            ]));
        }
        else sections.Add(new InspectionSection("Signature", [("Status", "No embedded signature (a catalog signature may still apply)")]));
        return new InspectionReport($"{(pe64 ? "PE32+" : "PE32")} {((characteristics & 0x2000) != 0 ? "DLL" : "executable")} · {Machine(machine)}", sections, warnings);
    }

    private static InspectionSection Imports(ContentReader r, Func<uint, long> offset, uint rva, bool pe64, List<string> warnings, CancellationToken ct)
    {
        var fields = new List<(string, string)>();
        long at = offset(rva);
        if (at < 0)
        {
            warnings.Add("The import table points outside every section.");
            return new InspectionSection("Imports", fields);
        }
        int budget = MaxImportEntries;
        for (int i = 0; i < MaxImports; i++)
        {
            ct.ThrowIfCancellationRequested();
            var d = r.Read(at + i * 20, 20);
            if (d.Length < 20 || d.All(b => b == 0)) break;
            uint nameRva = U32(d, 12), thunkRva = U32(d, 0) != 0 ? U32(d, 0) : U32(d, 16);
            long nameAt = offset(nameRva);
            string name = nameAt >= 0 ? r.AsciiZ(nameAt) : "(unreadable name)";
            int functions = 0, width = pe64 ? 8 : 4;
            bool capped = false;
            long thunk = offset(thunkRva);
            while (thunk >= 0)
            {
                // The name table ends with a zero entry; it is read in blocks, within a per-library and a total limit.
                int take = Math.Min(512, Math.Min(MaxFunctionsPerImport - functions, budget));
                if (take <= 0)
                {
                    capped = true;
                    break;
                }
                var block = r.Read(thunk + (long)functions * width, take * width);
                int entries = block.Length / width, k = 0;
                while (k < entries && block.AsSpan(k * width, width).ContainsAnyExcept((byte)0)) k++;
                functions += k;
                budget -= k;
                if (k < take) break;
            }
            fields.Add((name, $"{functions:N0}{(capped ? "+" : "")} function{(functions == 1 && !capped ? "" : "s")}"));
            if (i == MaxImports - 1) warnings.Add($"More than {MaxImports} imported libraries; the rest are not listed.");
        }
        return new InspectionSection($"Imports ({fields.Count} libraries)", fields);
    }

    private static InspectionSection Exports(ContentReader r, Func<uint, long> offset, uint rva, List<string> warnings)
    {
        var fields = new List<(string, string)>();
        long at = offset(rva);
        var d = at >= 0 ? r.Read(at, 40) : [];
        if (d.Length < 40)
        {
            warnings.Add("The export table points outside every section.");
            return new InspectionSection("Exports", fields);
        }
        long nameAt = offset(U32(d, 12));
        uint functions = U32(d, 20), names = U32(d, 24);
        fields.Add(("Library name", nameAt >= 0 ? r.AsciiZ(nameAt) : "(unreadable)"));
        fields.Add(("Functions", $"{functions:N0} ({names:N0} with names)"));
        long table = offset(U32(d, 32));
        for (int i = 0; table >= 0 && i < Math.Min(names, MaxExportNames); i++)
        {
            var p = r.Read(table + i * 4L, 4);
            if (p.Length < 4) break;
            long n = offset(U32(p, 0));
            fields.Add(($"#{i + 1}", n >= 0 ? r.AsciiZ(n) : "(unreadable)"));
        }
        if (names > MaxExportNames) fields.Add(("…", $"{names - MaxExportNames:N0} more names"));
        return new InspectionSection("Exports", fields);
    }

    /// <summary>The version information (RT_VERSION) and the manifest's requested execution level (RT_MANIFEST).</summary>
    private static IEnumerable<InspectionSection> Resources(ContentReader r, Func<uint, long> offset, uint rva, List<string> warnings, CancellationToken ct)
    {
        long root = offset(rva);
        if (root < 0)
        {
            warnings.Add("The resource table points outside every section.");
            yield break;
        }
        if (FirstLeaf(r, offset, root, rva, 16) is { } version && ParseVersion(version) is { Count: > 0 } fields)
            yield return new InspectionSection("Version information", fields);
        ct.ThrowIfCancellationRequested();
        if (FirstLeaf(r, offset, root, rva, 24) is { } manifest)
        {
            string text = Encoding.UTF8.GetString(manifest);
            // Linear time whatever the text: a manifest in a hostile file is attacker-controlled.
            var level = System.Text.RegularExpressions.Regex.Match(text, "requestedExecutionLevel[^>]*level\\s*=\\s*[\"']([A-Za-z]+)",
                System.Text.RegularExpressions.RegexOptions.NonBacktracking);
            yield return new InspectionSection("Manifest", [
                ("Requested execution level", level.Success ? level.Groups[1].Value : "not stated (runs as the invoker)"),
                ("Size", $"{manifest.Length:N0} bytes"),
            ]);
        }
    }

    /// <summary>The data of the first resource of a type (type → name → language), bounded.</summary>
    private static byte[]? FirstLeaf(ContentReader r, Func<uint, long> offset, long root, uint rootRva, int type)
    {
        long typeDir = FindEntry(r, root, type, byId: true);
        if (typeDir < 0) return null;
        long nameDir = FirstChild(r, root + typeDir);
        if (nameDir < 0) return null;
        long langEntry = FirstChild(r, root + nameDir, leaf: true);
        if (langEntry < 0) return null;
        var data = r.Read(root + langEntry, 16);
        if (data.Length < 8) return null;
        uint size = U32(data, 4);
        long at = offset(U32(data, 0));
        return at < 0 || size == 0 || size > MaxResource ? null : r.Read(at, (int)size);
    }

    /// <summary>The offset (relative to the resource root) that an entry with this id points to, or -1.</summary>
    private static long FindEntry(ContentReader r, long dir, int id, bool byId)
    {
        var head = r.Read(dir, 16);
        if (head.Length < 16) return -1;
        int named = U16(head, 12), ids = U16(head, 14);
        var entries = r.Read(dir + 16, Math.Min(named + ids, 512) * 8);
        for (int i = 0; i + 8 <= entries.Length; i += 8)
        {
            uint name = U32(entries, i), target = U32(entries, i + 4);
            if (byId && (name & 0x80000000) == 0 && name == id) return target & 0x7FFFFFFF;
        }
        return -1;
    }

    private static long FirstChild(ContentReader r, long dir, bool leaf = false)
    {
        var head = r.Read(dir, 16);
        if (head.Length < 16 || U16(head, 12) + U16(head, 14) == 0) return -1;
        var entry = r.Read(dir + 16, 8);
        if (entry.Length < 8) return -1;
        uint target = U32(entry, 4);
        bool isDir = (target & 0x80000000) != 0;
        return leaf ? (isDir ? FirstChild(r, dir, false) : target) : isDir ? target & 0x7FFFFFFF : -1;
    }

    /// <summary>VS_VERSIONINFO: the fixed file version plus the StringFileInfo values.</summary>
    internal static List<(string, string)> ParseVersion(byte[] data)
    {
        var fields = new List<(string, string)>();
        int fixedAt = IndexOf(data, [0xBD, 0x04, 0xEF, 0xFE]);
        if (fixedAt >= 0 && fixedAt + 24 <= data.Length)
        {
            uint ms = U32(data, fixedAt + 8), ls = U32(data, fixedAt + 12);
            fields.Add(("File version (binary)", $"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}"));
        }
        foreach (var key in new[] { "CompanyName", "FileDescription", "FileVersion", "ProductName", "ProductVersion", "OriginalFilename", "InternalName", "LegalCopyright" })
        {
            byte[] pattern = Encoding.Unicode.GetBytes(key + "\0");
            int at = IndexOf(data, pattern);
            if (at < 0) continue;
            int value = at + pattern.Length;
            value += (4 - value % 4) % 4;
            var sb = new StringBuilder();
            for (int i = value; i + 1 < data.Length && sb.Length < 256; i += 2)
            {
                char c = (char)(data[i] | data[i + 1] << 8);
                if (c == '\0') break;
                sb.Append(char.IsControl(c) ? '?' : c);
            }
            if (sb.Length > 0) fields.Add((key, sb.ToString()));
        }
        return fields;
    }

    private static string? Debug(ContentReader r, Func<uint, long> offset, (uint Rva, uint Size) dir)
    {
        long at = offset(dir.Rva);
        if (at < 0) return null;
        for (int i = 0; i < Math.Min(dir.Size / 28, 16); i++)
        {
            var e = r.Read(at + i * 28L, 28);
            if (e.Length < 28 || U32(e, 12) != 2) continue; // IMAGE_DEBUG_TYPE_CODEVIEW
            var cv = r.Read(U32(e, 24), (int)Math.Min(U32(e, 16), 1024));
            if (cv.Length > 24 && cv[0] == 'R' && cv[1] == 'S' && cv[2] == 'D' && cv[3] == 'S')
            {
                int end = Array.IndexOf(cv, (byte)0, 24);
                return Printable(Encoding.UTF8.GetString(cv, 24, (end < 0 ? cv.Length : end) - 24));
            }
        }
        return null;
    }

    private static List<(string, string)> DllFlags(ushort f, bool pe64)
    {
        string Yes(bool on) => on ? "yes" : "no";
        var list = new List<(string, string)>
        {
            ("Address space randomization (ASLR)", Yes((f & 0x40) != 0)),
            ("Data execution prevention (NX)", Yes((f & 0x100) != 0)),
            ("Control flow guard", Yes((f & 0x4000) != 0)),
        };
        if (pe64) list.Add(("High-entropy ASLR", Yes((f & 0x20) != 0)));
        if ((f & 0x400) != 0) list.Add(("Exception handlers", "none (NO_SEH)"));
        if ((f & 0x1000) != 0) list.Add(("App container", "yes"));
        if ((f & 0x80) != 0) list.Add(("Integrity check", "the loader checks the signature"));
        return list;
    }

    private static string Machine(ushort m) => m switch
    {
        0x14C => "x86",
        0x8664 => "x64",
        0xAA64 => "ARM64",
        0xA641 => "ARM64EC",
        0x1C4 => "ARM (Thumb-2)",
        0x200 => "Itanium",
        0 => "any (unknown)",
        _ => $"0x{m:X4}",
    };

    private static string Subsystem(ushort s) => s switch
    {
        1 => "Native (driver or kernel)",
        2 => "Windows GUI",
        3 => "Windows console",
        9 => "Windows CE",
        10 => "EFI application",
        11 => "EFI boot driver",
        12 => "EFI runtime driver",
        14 => "Xbox",
        16 => "Windows boot application",
        _ => s.ToString(CultureInfo.InvariantCulture),
    };

    private static string SectionFlags(uint f) =>
        ((f & 0x40000000) != 0 ? "R" : "-") + ((f & 0x80000000) != 0 ? "W" : "-") + ((f & 0x20000000) != 0 ? "X" : "-") +
        ((f & 0x20) != 0 ? " code" : (f & 0x40) != 0 ? " data" : (f & 0x80) != 0 ? " bss" : "");

    private static string Printable(string s) => new(s.Select(c => char.IsControl(c) ? '?' : c).ToArray());

    private static int IndexOf(byte[] data, byte[] pattern) => data.AsSpan().IndexOf(pattern);

    private static ushort U16(ReadOnlySpan<byte> b, int at) => at + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b[at..]) : (ushort)0;

    private static uint U32(ReadOnlySpan<byte> b, int at) => at + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b[at..]) : 0;
}
