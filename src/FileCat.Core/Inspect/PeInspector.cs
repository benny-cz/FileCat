using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// Portable Executable (PE/COFF) inspector (plan §16.1), as deep as Salamander's PE Viewer: a summary first (architecture,
/// security features, signature presence, version, the .NET assembly), then the file and optional headers, data
/// directories, sections, imports with hints (delay-loaded ones too), exports by ordinal, the resource tree, the manifest's
/// text, the debug directory, load configuration, thread-local storage, and relocations. A static parser: the file is
/// never loaded as a module, every table is read within limits, and a present signature is reported as present, not as
/// verified or trusted.
/// </summary>
public static partial class PeInspector
{
    private const int MaxSections = 96;
    private const int MaxImports = 2000;
    private const int MaxFunctionsPerImport = 10_000;
    private const int MaxImportEntries = 200_000; // counted across all libraries: a hostile table cannot make counting slow
    private const int MaxListed = 20_000; // imported and exported names shown, each
    private const int MaxResources = 5_000;
    private const int MaxResource = 64 * 1024;
    private const int MaxMetadata = 64 << 20;
    private const int MaxMetadataRows = 1_000;

    private static readonly string[] DirectoryNames =
    [
        "Export table", "Import table", "Resource table", "Exception table", "Certificate table (file offset)", "Base relocation table",
        "Debug", "Architecture", "Global pointer", "Thread-local storage (TLS)", "Load configuration", "Bound import", "Import address table (IAT)",
        "Delay-load import", ".NET runtime header", "Reserved",
    ];

    private readonly record struct Section(string Name, uint Va, uint VSize, uint RawSize, uint RawPtr, uint Flags);

    /// <summary>The image being read: its sections map addresses to file offsets.</summary>
    private sealed class Image(ContentReader reader, bool pe64, ulong imageBase, uint headersSize, List<Section> sections)
    {
        public ContentReader R { get; } = reader;
        public bool Pe64 { get; } = pe64;
        public ulong ImageBase { get; } = imageBase;
        public List<Section> Sections { get; } = sections;

        /// <summary>The file offset of an address, or -1 when no section holds it in the file.</summary>
        public long Offset(uint rva)
        {
            foreach (var s in Sections)
            {
                uint size = Math.Max(s.VSize, s.RawSize);
                if (rva >= s.Va && rva - s.Va < size) return rva - s.Va < s.RawSize ? s.RawPtr + (long)(rva - s.Va) : -1;
            }
            // Before the first section, addresses are the headers' own offsets.
            return rva < headersSize ? rva : -1;
        }

        public string? SectionOf(uint rva) =>
            Sections.FirstOrDefault(s => rva >= s.Va && rva - s.Va < Math.Max(s.VSize, s.RawSize)) is { Name: not null } s && (s.VSize | s.RawSize) != 0 ? s.Name : null;

        public string Text(uint rva, int max = 256) => Offset(rva) is var at and >= 0 ? R.AsciiZ(at, max) : "(unreadable)";

        /// <summary>A virtual address (as TLS and load configuration store them) as an address relative to the image.</summary>
        public uint? Rva(ulong va) => va >= ImageBase && va - ImageBase <= uint.MaxValue ? (uint)(va - ImageBase) : null;

        public byte[] Read(uint rva, int count) => Offset(rva) is var at and >= 0 ? R.Read(at, count) : [];
    }

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
        var fileHeader = new InspectionSection("File header", [
            ("Machine", $"0x{machine:X4} ({Machine(machine)})"),
            ("Number of sections", $"{count:N0}"),
            ("Time date stamp", Stamp(stamp)),
            ("Pointer to symbol table", $"0x{U32(nt, 12):X8}"),
            ("Number of symbols", $"{U32(nt, 16):N0}"),
            ("Size of optional header", $"0x{optionalSize:X4} ({optionalSize:N0})"),
            ("Characteristics", Flags(characteristics, 4, FileFlags)),
        ]);
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
            sections.Add(fileHeader);
            return new InspectionReport("PE image", sections, warnings);
        }
        var opt = nt.AsSpan(24).ToArray();
        ulong imageBase = pe64 ? U64(opt, 24) : U32(opt, 28);
        ushort subsystem = U16(opt, 68), dll = U16(opt, 70);
        header.Add(("Format", pe64 ? "PE32+ (64-bit)" : "PE32 (32-bit)"));
        header.Add(("Subsystem", Subsystem(subsystem)));
        header.Add(("Entry point (RVA)", $"0x{U32(opt, 16):X8}"));
        header.Add(("Image base", pe64 ? $"0x{imageBase:X16}" : $"0x{imageBase:X8}"));
        header.Add(("Image size", $"{U32(opt, 56):N0} bytes"));
        header.Add(("OS version required", $"{U16(opt, 40)}.{U16(opt, 42)}"));

        int dirCount = (int)Math.Min(16, U32(opt, pe64 ? 108 : 92));
        int dirBase = pe64 ? 112 : 96;
        var dirs = new (uint Rva, uint Size)[16];
        for (int i = 0; i < dirCount && dirBase + i * 8 + 8 <= opt.Length; i++) dirs[i] = (U32(opt, dirBase + i * 8), U32(opt, dirBase + i * 8 + 4));

        // Sections: also the map from addresses to file offsets.
        var table = new List<Section>();
        long sectionTable = lfanew + 24 + optionalSize;
        if (count > MaxSections) warnings.Add($"The header declares {count} sections; only the first {MaxSections} are read.");
        var raw = r.Read(sectionTable, Math.Min((int)count, MaxSections) * 40);
        for (int i = 0; i + 40 <= raw.Length; i += 40)
        {
            string name = Encoding.ASCII.GetString(raw, i, 8).TrimEnd('\0');
            table.Add(new Section(Printable(name), U32(raw, i + 12), U32(raw, i + 8), U32(raw, i + 16), U32(raw, i + 20), U32(raw, i + 36)));
        }
        if (table.Count < Math.Min((int)count, MaxSections)) warnings.Add("The section table is cut off by the end of the file.");
        foreach (var s in table)
        {
            if (r.Length >= 0 && s.RawSize > 0 && (long)s.RawPtr + s.RawSize > r.Length) warnings.Add($"Section {s.Name} extends past the end of the file.");
            if ((s.Flags & 0x20000000) != 0 && (s.Flags & 0x80000000) != 0) warnings.Add($"Section {s.Name} is both writable and executable.");
        }
        var img = new Image(r, pe64, imageBase, U32(opt, 60), table);
        ct.ThrowIfCancellationRequested();
        var rich = RichHeader(r, lfanew, warnings, out string? builtWith);
        if (builtWith is not null) header.Add(("Built with", builtWith + " (from the Rich header)"));
        if (Packer(table.Select(s => s.Name)) is { } packer) header.Add(("Packed or protected", packer + " (from the section names)"));
        var signature = dirs[4].Size > 0 ? ReadSignature(r, dirs[4], warnings) : null;
        var whole = ReadWholeFile(r, lfanew + 24 + 64, lfanew + 24 + (pe64 ? 144 : 128), U32(opt, 60), table, dirs[4],
            signature?.Algorithm ?? System.Security.Cryptography.HashAlgorithmName.SHA256, ct);
        // Where the program starts: a packer often starts outside the code, in a section not marked executable.
        uint entry = U32(opt, 16);
        if (entry != 0)
        {
            var at = table.FirstOrDefault(s => entry >= s.Va && entry - s.Va < Math.Max(s.VSize, s.RawSize));
            if (at.Name is null && table.Count > 0) warnings.Add("The entry point is outside every section.");
            else if (at.Name is not null && (at.Flags & 0x20000000) == 0) warnings.Add($"The entry point is in section {at.Name}, which is not marked executable (packers do this).");
        }
        for (int i = 0; i < table.Count && whole is not null; i++)
            if (whole.Entropy[i] is >= 7.2 && (table[i].Flags & 0x20000000) != 0)
                warnings.Add($"Section {table[i].Name} is executable and nearly random (entropy {whole.Entropy[i]:0.00}): compressed or encrypted code, as packers leave it.");

        // Data appended after the image (an installer's payload, a self-extracting archive's data).
        long imageEnd = table.Count == 0 ? 0 : table.Max(s => (long)s.RawPtr + s.RawSize);
        long certEnd = dirs[4].Size > 0 ? (long)dirs[4].Rva + dirs[4].Size : 0;
        if (r.Length > 0 && imageEnd > 0 && r.Length > Math.Max(imageEnd, certEnd) && certEnd <= imageEnd)
            header.Add(("Appended data (overlay)", $"{r.Length - imageEnd:N0} bytes at 0x{imageEnd:X}"));

        ct.ThrowIfCancellationRequested();
        var debug = dirs[6].Rva != 0 ? Debug(img, dirs[6], warnings) : null;
        var loadConfig = dirs[10].Rva != 0 ? LoadConfiguration(img, dirs[10], machine) : null;
        sections.Add(new InspectionSection("Header", header));
        sections.Add(new InspectionSection("Security features", SecurityFeatures(dll, pe64, machine, debug, loadConfig)));
        // The security directory holds a file offset, not an address.
        if (dirs[4].Size > 0)
        {
            var cert = r.Read(dirs[4].Rva, 8);
            string kind = cert.Length == 8 ? U16(cert, 6) switch { 2 => "Authenticode (PKCS #7)", 1 => "X.509", _ => $"type {U16(cert, 6)}" } : "unreadable";
            var fields = new List<(string, string)>
            {
                ("Status", "Present, not verified: FileCat does not check signatures or trust here"),
                ("Kind", kind),
                ("Size", $"{dirs[4].Size:N0} bytes"),
            };
            if (signature is not null) fields.AddRange(signature.Fields);
            // The hash the signature covers, compared with the file's own: an edit after signing shows, trust does not.
            if (signature?.SignedHash is { } signed)
                fields.Add(("Signed hash matches the file", whole?.Hash is not { } hash ? "not computed (the file is over 128 MB)"
                    : hash.AsSpan().SequenceEqual(signed) ? "yes: the file is as it was signed" : "no: the file changed after it was signed"));
            sections.Add(new InspectionSection("Signature", fields)
            {
                Children = signature is { Certificates.Count: > 0 }
                    ? [new InspectionSection($"Certificates ({signature.Certificates.Count})", []) { Table = new InspectionTable(["Subject", "Issuer", "Valid from", "Valid to", "Thumbprint (SHA-1)"], signature.Certificates) }]
                    : [],
            });
        }
        else sections.Add(new InspectionSection("Signature", [("Status", "No embedded signature (a catalog signature may still apply)")]));

        var resources = dirs[2].Rva != 0 ? ResourceLeaves(img, dirs[2].Rva, warnings, ct) : [];
        if (resources.FirstOrDefault(l => l.TypeId == 16) is { } versionLeaf && ReadLeaf(img, versionLeaf) is { } version && VersionInformation(version) is { } versionSection)
            sections.Add(versionSection);
        if (dirs[14].Rva != 0 && img.Offset(dirs[14].Rva) is var cli and >= 0 && r.Read(cli, 72) is { Length: >= 20 } cor)
            sections.Add(DotNet(img, cor, warnings, ct));
        if (resources.FirstOrDefault(l => l.TypeId == 24) is { } manifestLeaf && ReadLeaf(img, manifestLeaf) is { } manifest)
            sections.Add(Manifest(manifest));

        if (rich is not null) sections.Add(rich);
        sections.Add(DosHeader(r, dos, lfanew));
        sections.Add(fileHeader);
        sections.Add(OptionalHeader(opt, pe64, whole?.Checksum));
        sections.Add(new InspectionSection("Data directories", []) with
        {
            Table = new InspectionTable(["Directory", "Address", "Size", "In section"],
                [.. dirs.Select((d, i) => new[] { DirectoryNames[i], $"0x{d.Rva:X8}", d.Size == 0 ? "" : $"{d.Size:N0}", i == 4 || d.Rva == 0 ? "" : img.SectionOf(d.Rva) ?? "(none)" })]),
        });
        sections.Add(new InspectionSection($"Sections ({table.Count})", []) with
        {
            Table = new InspectionTable(["Name", "Address", "Virtual size", "File offset", "File size", "Access", "Entropy", "Characteristics"],
                [.. table.Select((s, i) => new[] { s.Name.Length == 0 ? "(no name)" : s.Name, $"0x{s.Va:X8}", $"{s.VSize:N0}", $"0x{s.RawPtr:X8}", $"{s.RawSize:N0}", SectionAccess(s.Flags),
                    whole?.Entropy[i] is double e ? e.ToString("0.00", CultureInfo.InvariantCulture) : "", SectionFlags(s.Flags) })]),
        });
        int listed = MaxListed;
        if (dirs[1].Rva != 0) sections.Add(Imports(img, dirs[1].Rva, warnings, ref listed, ct));
        if (dirs[13].Rva != 0) sections.Add(DelayImports(img, dirs[13].Rva, warnings, ref listed, ct));
        if (dirs[11].Rva != 0 && BoundImports(img, dirs[11]) is { } bound) sections.Add(bound);
        if (dirs[0].Rva != 0) sections.Add(Exports(img, dirs[0], warnings, ct));
        if (resources.Count > 0) sections.Add(Resources(img, resources));
        if (debug is not null) sections.Add(debug);
        if (loadConfig is not null) sections.Add(loadConfig);
        if (dirs[9].Rva != 0 && Tls(img, dirs[9].Rva) is { } tls) sections.Add(tls);
        if (dirs[5].Rva != 0 || dirs[3].Size != 0) sections.Add(RelocationsAndExceptions(img, dirs[5], dirs[3], machine, ct));
        return new InspectionReport($"{(pe64 ? "PE32+" : "PE32")} {((characteristics & 0x2000) != 0 ? "DLL" : "executable")} · {Machine(machine)}", sections, warnings);
    }

    // ---- Headers ------------------------------------------------------------------------------------------------------

    private static InspectionSection OptionalHeader(byte[] o, bool pe64, uint? computed)
    {
        uint stored = U32(o, 64);
        string checksum = (stored, computed) switch
        {
            (0, null) => "0 (not set)",
            (0, uint c) => $"0 (not set; the file's would be 0x{c:X8})",
            (_, null) => $"0x{stored:X8}",
            (_, uint c) when c == stored => $"0x{stored:X8} (matches the file)",
            (_, uint c) => $"0x{stored:X8} (the file's is 0x{c:X8}: changed after linking)",
        };
        string Size(ulong v) => $"0x{v:X8} ({v:N0})";
        string Word(int at) => pe64 ? Size(U64(o, at)) : Size(U32(o, at));
        var fields = new List<(string, string)>
        {
            ("Magic", $"0x{U16(o, 0):X4} ({(pe64 ? "PE32+" : "PE32")})"),
            ("Linker version", $"{U8(o, 2)}.{U8(o, 3)}"),
            ("Size of code", Size(U32(o, 4))),
            ("Size of initialized data", Size(U32(o, 8))),
            ("Size of uninitialized data", Size(U32(o, 12))),
            ("Address of entry point", $"0x{U32(o, 16):X8}"),
            ("Base of code", $"0x{U32(o, 20):X8}"),
        };
        if (!pe64) fields.Add(("Base of data", $"0x{U32(o, 24):X8}"));
        fields.AddRange([
            ("Image base", pe64 ? $"0x{U64(o, 24):X16}" : $"0x{U32(o, 28):X8}"),
            ("Section alignment", Size(U32(o, 32))),
            ("File alignment", Size(U32(o, 36))),
            ("Operating system version", $"{U16(o, 40)}.{U16(o, 42)}"),
            ("Image version", $"{U16(o, 44)}.{U16(o, 46)}"),
            ("Subsystem version", $"{U16(o, 48)}.{U16(o, 50)}"),
            ("Win32 version value", $"0x{U32(o, 52):X8}"),
            ("Size of image", Size(U32(o, 56))),
            ("Size of headers", Size(U32(o, 60))),
            ("Checksum", checksum),
            ("Subsystem", $"{U16(o, 68)} ({Subsystem(U16(o, 68))})"),
            ("DLL characteristics", Flags(U16(o, 70), 4, DllFlagNames)),
            ("Size of stack reserve", Word(72)),
            ("Size of stack commit", Word(pe64 ? 80 : 76)),
            ("Size of heap reserve", Word(pe64 ? 88 : 80)),
            ("Size of heap commit", Word(pe64 ? 96 : 84)),
            ("Loader flags", $"0x{U32(o, pe64 ? 104 : 88):X8}"),
            ("Number of data directories", $"{U32(o, pe64 ? 108 : 92):N0}"),
        ]);
        return new InspectionSection("Optional header", fields);
    }

    private static readonly (uint Flag, string Name)[] FileFlags =
    [
        (0x0001, "relocations stripped"), (0x0002, "executable image"), (0x0004, "line numbers stripped"), (0x0008, "local symbols stripped"),
        (0x0010, "aggressive working-set trim"), (0x0020, "large address aware"), (0x0080, "bytes reversed (low)"), (0x0100, "32-bit machine"),
        (0x0200, "debug information stripped"), (0x0400, "run from swap when removable"), (0x0800, "run from swap when on the network"),
        (0x1000, "system file"), (0x2000, "DLL"), (0x4000, "uniprocessor only"), (0x8000, "bytes reversed (high)"),
    ];

    private static readonly (uint Flag, string Name)[] DllFlagNames =
    [
        (0x0020, "high-entropy ASLR"), (0x0040, "dynamic base (ASLR)"), (0x0080, "force integrity"), (0x0100, "NX compatible"),
        (0x0200, "no isolation"), (0x0400, "no SEH"), (0x0800, "no bind"), (0x1000, "app container"), (0x2000, "WDM driver"),
        (0x4000, "control flow guard"), (0x8000, "terminal server aware"),
    ];

    /// <summary>"0x0022 (executable image, large address aware)": the value and the names of its flags.</summary>
    private static string Flags(uint value, int digits, IEnumerable<(uint Flag, string Name)> names)
    {
        var set = names.Where(n => (value & n.Flag) != 0).Select(n => n.Name).ToList();
        uint known = names.Aggregate(0u, (a, n) => a | n.Flag);
        if ((value & ~known) != 0) set.Add($"0x{value & ~known:X} unknown");
        return $"0x{value.ToString("X" + digits, CultureInfo.InvariantCulture)}" + (set.Count > 0 ? $" ({string.Join(", ", set)})" : "");
    }

    private static string Stamp(uint stamp) => stamp == 0 ? "0"
        : stamp == 0xFFFFFFFF ? "0xFFFFFFFF (bound)"
        : stamp < 0x80000000 ? $"0x{stamp:X8} ({DateTimeOffset.FromUnixTimeSeconds(stamp):u})"
        : $"0x{stamp:X8} (not a date: a reproducible-build hash)";

    private static List<(string, string)> SecurityFeatures(ushort f, bool pe64, ushort machine, InspectionSection? debug, InspectionSection? loadConfig)
    {
        string Yes(bool on) => on ? "yes" : "no";
        var list = new List<(string, string)>
        {
            ("Address space randomization (ASLR)", Yes((f & 0x40) != 0)),
            ("Data execution prevention (NX)", Yes((f & 0x100) != 0)),
            ("Control flow guard", Yes((f & 0x4000) != 0)),
        };
        if (pe64) list.Add(("High-entropy ASLR", Yes((f & 0x20) != 0)));
        if (machine is 0x8664 or 0xA641 or 0xAA64 && debug?.Fields.FirstOrDefault(x => x.Name == "Shadow stack (CET)") is { Name: not null } cet) list.Add(cet);
        if (machine == 0x14C)
        {
            string? handlers = loadConfig?.Fields.FirstOrDefault(x => x.Name == "Safe exception handlers").Value;
            list.Add(("Safe exception handlers (SafeSEH)", (f & 0x400) != 0 ? "not needed (no exception handlers)" : handlers is not null ? "yes (" + handlers + ")" : "no"));
        }
        if ((f & 0x400) != 0 && machine != 0x14C) list.Add(("Exception handlers", "none (NO_SEH)"));
        if ((f & 0x1000) != 0) list.Add(("App container", "yes"));
        if ((f & 0x80) != 0) list.Add(("Integrity check", "the loader checks the signature"));
        return list;
    }

    // ---- Imports and exports ----------------------------------------------------------------------------------------

    private static InspectionSection Imports(Image img, uint rva, List<string> warnings, ref int listed, CancellationToken ct)
    {
        var fields = new List<(string, string)>();
        var libraries = new List<InspectionSection>();
        long at = img.Offset(rva);
        if (at < 0)
        {
            warnings.Add("The import table points outside every section.");
            return new InspectionSection("Imports", fields);
        }
        int budget = MaxImportEntries;
        for (int i = 0; i < MaxImports; i++)
        {
            ct.ThrowIfCancellationRequested();
            var d = img.R.Read(at + i * 20, 20);
            if (d.Length < 20 || d.All(b => b == 0)) break;
            uint names = U32(d, 0), stamp = U32(d, 4), forwarder = U32(d, 8), iat = U32(d, 16);
            string name = img.Text(U32(d, 12));
            var (functions, rows, capped) = Thunks(img, names != 0 ? names : iat, ref budget, ref listed);
            fields.Add((name, $"{functions:N0}{(capped ? "+" : "")} function{(functions == 1 && !capped ? "" : "s")}"));
            libraries.Add(new InspectionSection(name, [
                ("Import name table (INT)", $"0x{names:X8}"),
                ("Import address table (IAT)", $"0x{iat:X8}"),
                ("Time date stamp", stamp == 0 ? "0 (not bound)" : Stamp(stamp)),
                ("Forwarder chain", forwarder == 0xFFFFFFFF ? "none (-1)" : $"0x{forwarder:X8}"),
            ]) with { Table = new InspectionTable(["Hint", "Function"], rows) { More = rows.Count < functions ? $"{functions - rows.Count:N0} more functions are not listed" : null } });
            if (i == MaxImports - 1) warnings.Add($"More than {MaxImports} imported libraries; the rest are not listed.");
        }
        return new InspectionSection($"Imports ({fields.Count} libraries)", fields) { Children = libraries };
    }

    /// <summary>
    /// An import name table: hints and names, or ordinals. It ends with a zero entry and is read in blocks, within a
    /// per-library and a total limit; names are listed while <paramref name="listed"/> lasts.
    /// </summary>
    private static (int Count, List<string[]> Rows, bool Capped) Thunks(Image img, uint rva, ref int budget, ref int listed)
    {
        var rows = new List<string[]>();
        int functions = 0, width = img.Pe64 ? 8 : 4;
        long thunk = img.Offset(rva);
        while (thunk >= 0)
        {
            int take = Math.Min(512, Math.Min(MaxFunctionsPerImport - functions, budget));
            if (take <= 0) return (functions, rows, true);
            var block = img.R.Read(thunk + (long)functions * width, take * width);
            int entries = block.Length / width, k = 0;
            for (; k < entries; k++)
            {
                ulong value = img.Pe64 ? U64(block, k * width) : U32(block, k * width);
                if (value == 0) break;
                if (listed > 0)
                {
                    listed--;
                    bool ordinal = img.Pe64 ? (value & 0x8000000000000000) != 0 : (value & 0x80000000) != 0;
                    if (ordinal) rows.Add(["", $"ordinal {value & 0xFFFF}"]);
                    else
                    {
                        var byName = img.Read((uint)(value & 0x7FFFFFFF), 2 + 256);
                        rows.Add(byName.Length >= 3 ? [$"{U16(byName, 0)}", Ascii(byName.AsSpan(2))] : ["", "(unreadable)"]);
                    }
                }
            }
            functions += k;
            budget -= k;
            if (k < take) break;
        }
        return (functions, rows, false);
    }

    private static InspectionSection DelayImports(Image img, uint rva, List<string> warnings, ref int listed, CancellationToken ct)
    {
        var fields = new List<(string, string)>();
        var libraries = new List<InspectionSection>();
        long at = img.Offset(rva);
        if (at < 0)
        {
            warnings.Add("The delay-load import table points outside every section.");
            return new InspectionSection("Delay-load imports", fields);
        }
        int budget = MaxImportEntries;
        for (int i = 0; i < MaxImports; i++)
        {
            ct.ThrowIfCancellationRequested();
            var d = img.R.Read(at + i * 32, 32);
            if (d.Length < 32 || d.All(b => b == 0)) break;
            // Old descriptors (attribute bit 0 clear) hold virtual addresses instead of relative ones.
            bool relative = (U32(d, 0) & 1) != 0;
            uint Address(int field)
            {
                uint value = U32(d, field);
                return relative || value == 0 ? value : img.Rva(value) ?? 0;
            }
            string name = img.Text(Address(4));
            var (functions, rows, capped) = Thunks(img, Address(16), ref budget, ref listed);
            fields.Add((name, $"{functions:N0}{(capped ? "+" : "")} function{(functions == 1 && !capped ? "" : "s")}"));
            libraries.Add(new InspectionSection(name, [
                ("Import name table (INT)", $"0x{Address(16):X8}"),
                ("Import address table (IAT)", $"0x{Address(12):X8}"),
                ("Module handle", $"0x{Address(8):X8}"),
                ("Time date stamp", U32(d, 28) == 0 ? "0 (not bound)" : Stamp(U32(d, 28))),
            ]) with { Table = new InspectionTable(["Hint", "Function"], rows) { More = rows.Count < functions ? $"{functions - rows.Count:N0} more functions are not listed" : null } });
        }
        return new InspectionSection($"Delay-load imports ({fields.Count} libraries)", fields) { Children = libraries };
    }

    private static InspectionSection Exports(Image img, (uint Rva, uint Size) dir, List<string> warnings, CancellationToken ct)
    {
        var fields = new List<(string, string)>();
        var d = img.Read(dir.Rva, 40);
        if (d.Length < 40)
        {
            warnings.Add("The export table points outside every section.");
            return new InspectionSection("Exports", fields);
        }
        uint functions = U32(d, 20), names = U32(d, 24), ordinalBase = U32(d, 16);
        fields.Add(("Library name", img.Text(U32(d, 12))));
        fields.Add(("Functions", $"{functions:N0} ({names:N0} with names)"));
        fields.Add(("Ordinal base", $"{ordinalBase:N0}"));
        fields.Add(("Time date stamp", Stamp(U32(d, 4))));
        fields.Add(("Version", $"{U16(d, 8)}.{U16(d, 10)}"));
        // Names by ordinal: the name table and the ordinal table run side by side.
        int nameCount = (int)Math.Min(names, 100_000);
        var nameTable = img.Read(U32(d, 32), nameCount * 4);
        var ordinalTable = img.Read(U32(d, 36), nameCount * 2);
        var nameOf = new Dictionary<int, uint>();
        for (int i = 0; i * 4 + 4 <= nameTable.Length && i * 2 + 2 <= ordinalTable.Length; i++) nameOf.TryAdd(U16(ordinalTable, i * 2), U32(nameTable, i * 4));
        int listedCount = (int)Math.Min(functions, MaxListed);
        var addresses = img.Read(U32(d, 28), listedCount * 4);
        var rows = new List<string[]>();
        bool forwarders = false;
        for (int i = 0; i * 4 + 4 <= addresses.Length; i++)
        {
            if (i % 1024 == 0) ct.ThrowIfCancellationRequested();
            uint address = U32(addresses, i * 4);
            if (address == 0) continue; // an unused ordinal
            // An address inside the export directory is a forwarder: "OTHER.Function".
            bool forwarded = address >= dir.Rva && address - dir.Rva < dir.Size;
            forwarders |= forwarded;
            rows.Add([$"{ordinalBase + (uint)i}", $"0x{address:X8}", nameOf.TryGetValue(i, out var n) ? img.Text(n) : "(no name)", forwarded ? img.Text(address) : ""]);
        }
        var columns = forwarders ? new[] { "Ordinal", "Address", "Name", "Forwarded to" } : ["Ordinal", "Address", "Name"];
        if (!forwarders) rows = [.. rows.Select(row => row[..3])];
        return new InspectionSection("Exports", fields)
        {
            Table = new InspectionTable(columns, rows) { More = functions > MaxListed ? $"{functions - MaxListed:N0} more functions are not listed" : null },
        };
    }

    // ---- Resources ----------------------------------------------------------------------------------------------------

    private sealed record ResourceLeaf(int TypeId, string Type, string Name, string Language, uint DataRva, uint Size, uint CodePage);

    /// <summary>Every resource: type → name → language → data, read within limits and never around a loop.</summary>
    private static List<ResourceLeaf> ResourceLeaves(Image img, uint rva, List<string> warnings, CancellationToken ct)
    {
        var leaves = new List<ResourceLeaf>();
        long root = img.Offset(rva);
        if (root < 0)
        {
            warnings.Add("The resource table points outside every section.");
            return leaves;
        }
        var seen = new HashSet<long>();
        List<(uint Name, uint Target)> Entries(long dir)
        {
            if (!seen.Add(dir)) return [];
            var head = img.R.Read(root + dir, 16);
            if (head.Length < 16) return [];
            int n = Math.Min(U16(head, 12) + U16(head, 14), 4096);
            var raw = img.R.Read(root + dir + 16, n * 8);
            var list = new List<(uint, uint)>();
            for (int i = 0; i + 8 <= raw.Length; i += 8) list.Add((U32(raw, i), U32(raw, i + 4)));
            return list;
        }
        string Name(uint name, Func<uint, string> byId)
        {
            if ((name & 0x80000000) == 0) return byId(name);
            // A counted UTF-16 string at an offset from the resource root.
            var at = root + (name & 0x7FFFFFFF);
            var length = img.R.Read(at, 2);
            if (length.Length < 2) return "(unreadable)";
            var chars = img.R.Read(at + 2, Math.Min((int)U16(length, 0), 128) * 2);
            return "\"" + Printable(Encoding.Unicode.GetString(chars)) + "\"";
        }
        foreach (var (typeName, typeTarget) in Entries(0))
        {
            if ((typeTarget & 0x80000000) == 0) continue;
            int typeId = (typeName & 0x80000000) == 0 ? (int)typeName : -1;
            string type = Name(typeName, ResourceType);
            foreach (var (name, nameTarget) in Entries(typeTarget & 0x7FFFFFFF))
            {
                ct.ThrowIfCancellationRequested();
                string itemName = Name(name, id => $"#{id}");
                var languages = (nameTarget & 0x80000000) != 0 ? Entries(nameTarget & 0x7FFFFFFF) : [(0u, nameTarget)];
                foreach (var (language, target) in languages)
                {
                    if ((target & 0x80000000) != 0) continue;
                    var data = img.R.Read(root + target, 16);
                    if (data.Length < 16) continue;
                    leaves.Add(new ResourceLeaf(typeId, type, itemName, Name(language, Language), U32(data, 0), U32(data, 4), U32(data, 8)));
                    if (leaves.Count >= MaxResources)
                    {
                        warnings.Add($"More than {MaxResources:N0} resources; the rest are not listed.");
                        return leaves;
                    }
                }
            }
        }
        return leaves;
    }

    private static byte[]? ReadLeaf(Image img, ResourceLeaf leaf) =>
        leaf.Size == 0 || leaf.Size > MaxResource || img.Offset(leaf.DataRva) < 0 ? null : img.Read(leaf.DataRva, (int)leaf.Size);

    private static InspectionSection Resources(Image img, List<ResourceLeaf> leaves)
    {
        var fields = leaves.GroupBy(l => l.Type).Select(g => (g.Key, $"{g.Count():N0} ({g.Sum(l => (long)l.Size):N0} bytes)")).ToList();
        return new InspectionSection($"Resources ({leaves.Count:N0})", fields)
        {
            Table = new InspectionTable(["Type", "Name", "Language", "Size", "File offset"],
                [.. leaves.Select(l => new[] { l.Type, l.Name, l.Language, $"{l.Size:N0}", img.Offset(l.DataRva) is var at and >= 0 ? $"0x{at:X8}" : "(outside the file)" })]),
        };
    }

    private static string ResourceType(uint id) => id switch
    {
        1 => "Cursor", 2 => "Bitmap", 3 => "Icon", 4 => "Menu", 5 => "Dialog", 6 => "String table", 7 => "Font directory", 8 => "Font",
        9 => "Accelerators", 10 => "Raw data", 11 => "Message table", 12 => "Cursor group", 14 => "Icon group", 16 => "Version",
        17 => "Dialog include", 19 => "Plug and play", 20 => "VxD", 21 => "Animated cursor", 22 => "Animated icon", 23 => "HTML", 24 => "Manifest",
        _ => $"#{id}",
    };

    /// <summary>"0409 (en-US)": a Windows language identifier and, where the system knows it, its culture.</summary>
    private static string Language(uint id) => id == 0 ? "neutral" : CultureName(id) is { } name ? $"{id:X4} ({name})" : $"{id:X4}";

    private static string? CultureName(uint id)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo((int)id);
            return culture.Name.Length > 0 ? culture.Name : null;
        }
        catch (Exception ex) when (ex is CultureNotFoundException or ArgumentException) { return null; }
    }

    // ---- Version information and manifest -----------------------------------------------------------------------------

    /// <summary>A VS_VERSIONINFO block: its key, value, and where its children are.</summary>
    private readonly record struct Block(int Start, int End, int Type, string Key, int ValueStart, int ValueLength, int ChildrenStart);

    private static Block? ReadBlock(byte[] d, int at, int end)
    {
        if (at + 6 > end) return null;
        int length = U16(d, at), valueLength = U16(d, at + 2), type = U16(d, at + 4);
        if (length < 6) return null;
        int blockEnd = Math.Min(end, at + length);
        var key = new StringBuilder();
        int k = at + 6;
        for (; k + 1 < blockEnd; k += 2)
        {
            char c = (char)(d[k] | d[k + 1] << 8);
            if (c == '\0') break;
            if (key.Length < 64) key.Append(char.IsControl(c) ? '?' : c);
        }
        int valueStart = Align4(k + 2);
        // Text values count characters, binary ones bytes.
        int valueBytes = type == 1 ? valueLength * 2 : valueLength;
        return new Block(at, blockEnd, type, key.ToString(), valueStart, Math.Max(0, Math.Min(valueBytes, blockEnd - valueStart)), Align4(valueStart + valueBytes));
    }

    private static IEnumerable<Block> Children(byte[] d, Block parent)
    {
        int at = parent.ChildrenStart;
        for (int i = 0; i < 1000 && at < parent.End; i++)
        {
            if (ReadBlock(d, at, parent.End) is not { } child) yield break;
            yield return child;
            at = Align4(child.End);
        }
    }

    private static int Align4(int at) => (at + 3) & ~3;

    private static string BlockText(byte[] d, Block b)
    {
        var sb = new StringBuilder();
        for (int i = b.ValueStart; i + 1 < b.End && sb.Length < 1024; i += 2)
        {
            char c = (char)(d[i] | d[i + 1] << 8);
            if (c == '\0') break;
            sb.Append(char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>VS_VERSIONINFO: the fixed file information and every string table, as Salamander's PE Viewer shows them.</summary>
    private static InspectionSection? VersionInformation(byte[] data)
    {
        if (ReadBlock(data, 0, data.Length) is not { } root) return null;
        var fields = new List<(string, string)>();
        var children = new List<InspectionSection>();
        if (root.ValueLength >= 52 && U32(data, root.ValueStart) == 0xFEEF04BD)
        {
            var f = data.AsSpan(root.ValueStart, 52).ToArray();
            string Version(int at) => $"{U32(f, at) >> 16}.{U32(f, at) & 0xFFFF}.{U32(f, at + 4) >> 16}.{U32(f, at + 4) & 0xFFFF}";
            uint flags = U32(f, 28) & U32(f, 24), os = U32(f, 32), type = U32(f, 36), subtype = U32(f, 40);
            fields.Add(("File version", Version(8)));
            fields.Add(("Product version", Version(16)));
            fields.Add(("File flags", Flags(flags, 8, [(1, "debug"), (2, "prerelease"), (4, "patched"), (8, "private build"), (0x10, "information inferred"), (0x20, "special build")])));
            fields.Add(("File OS", os switch
            {
                0x40004 => "Windows NT (32-bit Windows)", 0x4 => "32-bit Windows", 0x40000 => "Windows NT", 0x10004 => "MS-DOS, 32-bit Windows",
                0x10001 => "MS-DOS, 16-bit Windows", 0x1 => "16-bit Windows", 0x10000 => "MS-DOS", 0 => "unknown", _ => $"0x{os:X8}",
            }));
            fields.Add(("File type", type switch
            {
                1 => "Application", 2 => "Dynamic-link library", 5 => "Virtual device", 7 => "Static library",
                3 => "Driver" + subtype switch { 1 => " (printer)", 2 => " (keyboard)", 3 => " (language)", 4 => " (display)", 5 => " (mouse)", 6 => " (network)", 7 => " (system)", 8 => " (installable)", 9 => " (sound)", 10 => " (communications)", 12 => " (versioned printer)", _ => "" },
                4 => "Font" + subtype switch { 1 => " (raster)", 2 => " (vector)", 3 => " (TrueType)", _ => "" },
                0 => "unknown", _ => $"{type}",
            }));
            ulong date = (ulong)U32(f, 44) << 32 | U32(f, 48);
            if (date != 0) fields.Add(("File date", date < (ulong)DateTime.MaxValue.ToFileTimeUtc() ? $"{DateTime.FromFileTimeUtc((long)date):u}" : $"0x{date:X16}"));
        }
        foreach (var info in Children(data, root))
        {
            if (info.Key == "StringFileInfo")
            {
                foreach (var strings in Children(data, info))
                {
                    var values = Children(data, strings).Select(s => (Printable(s.Key), BlockText(data, s))).Where(v => v.Item1.Length > 0).ToList();
                    string language = StringTableLanguage(strings.Key);
                    // The first table's strings are the file's own; other languages follow as their own sections.
                    if (fields.All(x => x.Item1 != "Language of the strings"))
                    {
                        fields.Add(("Language of the strings", language));
                        fields.AddRange(values);
                    }
                    else children.Add(new InspectionSection("Strings, " + language, values));
                }
            }
            else if (info.Key == "VarFileInfo")
            {
                foreach (var v in Children(data, info).Where(v => v.Key == "Translation"))
                {
                    var pairs = new List<string>();
                    for (int i = v.ValueStart; i + 4 <= v.ValueStart + v.ValueLength && pairs.Count < 32; i += 4) pairs.Add(StringTableLanguage($"{U16(data, i):X4}{U16(data, i + 2):X4}"));
                    if (pairs.Count > 0) fields.Add(("Translations", string.Join("; ", pairs)));
                }
            }
        }
        return fields.Count > 0 ? new InspectionSection("Version information", fields) { Children = children } : null;
    }

    /// <summary>"040904B0": a language and a code page, as a string table's key names them.</summary>
    private static string StringTableLanguage(string key)
    {
        if (key.Length != 8 || !uint.TryParse(key, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) return key;
        uint codePage = value & 0xFFFF, language = value >> 16;
        string page = codePage switch { 1200 => "Unicode", 0 => "no code page", 65001 => "UTF-8", _ => $"code page {codePage}" };
        return $"{key} ({(language == 0 ? "neutral" : CultureName(language) ?? $"language {language:X4}")}, {page})";
    }

    private static InspectionSection Manifest(byte[] manifest)
    {
        string text = Encoding.UTF8.GetString(manifest).TrimStart('\uFEFF');
        // Linear time whatever the text: a manifest in a hostile file is attacker-controlled.
        var level = System.Text.RegularExpressions.Regex.Match(text, "requestedExecutionLevel[^>]*level\\s*=\\s*[\"']([A-Za-z]+)",
            System.Text.RegularExpressions.RegexOptions.NonBacktracking);
        var lines = (Indented(text) ?? text).Split('\n').Take(2000).Select(l => Printable(l.TrimEnd('\r', ' ', '\t'))).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return new InspectionSection("Manifest", [
            ("Requested execution level", level.Success ? level.Groups[1].Value : "not stated (runs as the invoker)"),
            ("Size", $"{manifest.Length:N0} bytes"),
        ]) { Lines = lines };
    }

    /// <summary>
    /// XML laid out one element per line (manifests are often written on one long line), its names and prefixes as the
    /// file writes them; null when it does not parse. Hostile XML is safe here: document type definitions are refused,
    /// so nothing is fetched or expanded.
    /// </summary>
    private static string? Indented(string xml)
    {
        try
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxResource * 2, IgnoreWhitespace = true };
            using var reader = System.Xml.XmlReader.Create(new StringReader(xml), settings);
            var text = new StringBuilder();
            using (var writer = System.Xml.XmlWriter.Create(text, new System.Xml.XmlWriterSettings { Indent = true, IndentChars = "  ", NewLineChars = "\n", ConformanceLevel = System.Xml.ConformanceLevel.Document }))
                writer.WriteNode(reader, defattr: false);
            return text.ToString();
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException or ArgumentException) { return null; }
    }

    // ---- .NET ---------------------------------------------------------------------------------------------------------

    private static InspectionSection DotNet(Image img, byte[] cor, List<string> warnings, CancellationToken ct)
    {
        uint flags = U32(cor, 16), entry = U32(cor, 20);
        var fields = new List<(string, string)>
        {
            ("Runtime header", $"{U16(cor, 4)}.{U16(cor, 6)}"),
            ("IL only", (flags & 1) != 0 ? "yes" : "no (contains native code)"),
            ("32-bit", (flags & 2) != 0 ? "required" : (flags & 0x20000) != 0 ? "preferred" : "not required"),
            ("Strong-name signed", (flags & 8) != 0 ? "flag set (not verified)" : "no"),
        };
        if (cor.Length >= 72 && U32(cor, 68) != 0) fields.Add(("Precompiled (ReadyToRun)", "yes: native code for this runtime is included"));
        var children = new List<InspectionSection>();
        uint metadataRva = U32(cor, 8), metadataSize = U32(cor, 12);
        if (metadataSize > 0 && metadataSize <= MaxMetadata && img.Read(metadataRva, (int)metadataSize) is { Length: > 0 } metadata)
            Metadata(metadata, (flags & 0x10) != 0 ? null : entry, fields, children, warnings, ct);
        if ((flags & 0x10) != 0 && entry != 0) fields.Add(("Entry point", $"native, at 0x{entry:X8}"));
        return new InspectionSection(".NET", fields) { Children = children };
    }

    /// <summary>
    /// The assembly's metadata (read with System.Reflection.Metadata, nothing loaded): its identity, target framework,
    /// assembly attributes, referenced assemblies, resources, and the native libraries its methods call.
    /// </summary>
    private static void Metadata(byte[] metadata, uint? entryToken, List<(string, string)> fields, List<InspectionSection> children, List<string> warnings, CancellationToken ct)
    {
        try
        {
            using var provider = MetadataReaderProvider.FromMetadataImage(ImmutableArray.Create(metadata));
            var md = provider.GetMetadataReader();
            fields.Add(("Metadata version", Printable(md.MetadataVersion)));
            var attributes = new List<(string, string)>();
            if (md.IsAssembly)
            {
                var assembly = md.GetAssemblyDefinition();
                fields.Add(("Assembly", Identity(md.GetString(assembly.Name), assembly.Version, md.GetString(assembly.Culture), KeyToken(md.GetBlobBytes(assembly.PublicKey)))));
                foreach (var handle in md.GetCustomAttributes(EntityHandle.AssemblyDefinition).Take(MaxMetadataRows))
                {
                    // One attribute that does not decode leaves the others.
                    try
                    {
                        if (StringAttribute(md, md.GetCustomAttribute(handle)) is { } attribute) attributes.Add(attribute);
                    }
                    catch (BadImageFormatException) { }
                }
                if (attributes.FirstOrDefault(a => a.Item1 == "TargetFramework") is { Item2: not null } framework) fields.Add(("Target framework", framework.Item2));
            }
            var module = md.GetModuleDefinition();
            fields.Add(("Module", Printable(md.GetString(module.Name))));
            fields.Add(("Module version ID (MVID)", md.GetGuid(module.Mvid).ToString()));
            if (entryToken is uint token && token >> 24 == 0x06 && (token & 0xFFFFFF) is var row and > 0 && row <= md.MethodDefinitions.Count)
            {
                var main = md.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle((int)row));
                var type = md.GetTypeDefinition(main.GetDeclaringType());
                string ns = md.GetString(type.Namespace);
                fields.Add(("Entry point", Printable((ns.Length > 0 ? ns + "." : "") + md.GetString(type.Name) + "." + md.GetString(main.Name))));
            }
            fields.Add(("Types", $"{md.TypeDefinitions.Count:N0} ({md.MethodDefinitions.Count:N0} methods, {md.FieldDefinitions.Count:N0} fields)"));
            if (attributes.Count > 0) children.Add(new InspectionSection("Assembly attributes", attributes));

            ct.ThrowIfCancellationRequested();
            var references = md.AssemblyReferences.Take(MaxMetadataRows).Select(h =>
            {
                var reference = md.GetAssemblyReference(h);
                var key = md.GetBlobBytes(reference.PublicKeyOrToken);
                string token = (reference.Flags & AssemblyFlags.PublicKey) != 0 ? KeyToken(key) ?? "null" : key.Length == 8 ? Convert.ToHexStringLower(key) : "null";
                string culture = md.GetString(reference.Culture);
                return new[] { Printable(md.GetString(reference.Name)), reference.Version.ToString(), culture.Length == 0 ? "neutral" : Printable(culture), token };
            }).ToList();
            if (references.Count > 0)
                children.Add(new InspectionSection($"Referenced assemblies ({md.AssemblyReferences.Count:N0})", []) { Table = new InspectionTable(["Name", "Version", "Culture", "Public key token"], references) });

            var resources = md.ManifestResources.Take(MaxMetadataRows).Select(h =>
            {
                var resource = md.GetManifestResource(h);
                // An embedded resource has no implementation (a nil handle, whatever kind it says).
                string where = resource.Implementation.IsNil ? "embedded" : resource.Implementation.Kind switch
                {
                    HandleKind.AssemblyFile => "file " + Printable(md.GetString(md.GetAssemblyFile((AssemblyFileHandle)resource.Implementation).Name)),
                    HandleKind.AssemblyReference => "assembly " + Printable(md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)resource.Implementation).Name)),
                    _ => "embedded",
                };
                return new[] { Printable(md.GetString(resource.Name)), (resource.Attributes & ManifestResourceAttributes.VisibilityMask) == ManifestResourceAttributes.Public ? "public" : "private", where };
            }).ToList();
            if (resources.Count > 0)
                children.Add(new InspectionSection($"Resources ({md.ManifestResources.Count:N0})", []) { Table = new InspectionTable(["Name", "Visibility", "Where"], resources) });

            // Platform invoke: which native libraries the methods call, and how many functions of each.
            var calls = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int seen = 0;
            foreach (var handle in md.MethodDefinitions)
            {
                if (++seen % 4096 == 0) ct.ThrowIfCancellationRequested();
                var method = md.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.PinvokeImpl) == 0) continue;
                var import = method.GetImport();
                if (import.Module.IsNil) continue;
                string library = Printable(md.GetString(md.GetModuleReference(import.Module).Name));
                calls[library] = calls.GetValueOrDefault(library) + 1;
            }
            if (calls.Count > 0)
                children.Add(new InspectionSection($"Native libraries called ({calls.Count:N0})", [.. calls.OrderBy(c => c.Key, StringComparer.OrdinalIgnoreCase).Take(MaxMetadataRows).Select(c => (c.Key, $"{c.Value:N0} function{(c.Value == 1 ? "" : "s")}"))]));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Hostile or damaged metadata: said, never thrown.
            warnings.Add("The .NET metadata is damaged: " + ex.Message);
        }
    }

    private static string Identity(string name, Version version, string culture, string? token) =>
        $"{Printable(name)}, Version={version}, Culture={(culture.Length == 0 ? "neutral" : Printable(culture))}, PublicKeyToken={token ?? "null"}";

    /// <summary>A public key's token: the last eight bytes of its SHA-1 hash, reversed (an identifier, not a check).</summary>
    private static string? KeyToken(byte[] key)
    {
        if (key.Length == 0) return null;
        var hash = System.Security.Cryptography.SHA1.HashData(key);
        return Convert.ToHexStringLower([.. hash.AsSpan(hash.Length - 8).ToArray().Reverse()]);
    }

    /// <summary>An assembly attribute whose constructor takes one string ("AssemblyCompany", "Contoso"), or null.</summary>
    private static (string, string)? StringAttribute(MetadataReader md, CustomAttribute attribute)
    {
        StringHandle name;
        BlobHandle signature;
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference) return null;
                name = md.GetTypeReference((TypeReferenceHandle)member.Parent).Name;
                signature = member.Signature;
                break;
            case HandleKind.MethodDefinition:
                var method = md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                name = md.GetTypeDefinition(method.GetDeclaringType()).Name;
                signature = method.Signature;
                break;
            default:
                return null;
        }
        var sig = md.GetBlobReader(signature);
        var header = sig.ReadSignatureHeader();
        if (header.Kind != SignatureKind.Method) return null;
        if (header.IsGeneric) sig.ReadCompressedInteger();
        if (sig.ReadCompressedInteger() != 1 || sig.ReadSignatureTypeCode() != SignatureTypeCode.Void || sig.ReadSignatureTypeCode() != SignatureTypeCode.String) return null;
        var value = md.GetBlobReader(attribute.Value);
        if (value.Length < 2 || value.ReadUInt16() != 1) return null;
        string? text = value.ReadSerializedString();
        string type = md.GetString(name);
        if (type.EndsWith("Attribute", StringComparison.Ordinal)) type = type[..^"Attribute".Length];
        return text is null ? null : (Printable(type), Printable(text.Length > 512 ? text[..512] + "…" : text));
    }

    // ---- Debug, load configuration, TLS, relocations ------------------------------------------------------------------

    private static InspectionSection? Debug(Image img, (uint Rva, uint Size) dir, List<string> warnings)
    {
        long at = img.Offset(dir.Rva);
        if (at < 0)
        {
            warnings.Add("The debug directory points outside every section.");
            return null;
        }
        var fields = new List<(string, string)>();
        var rows = new List<string[]>();
        InspectionSection? contributions = null;
        for (int i = 0; i < Math.Min(dir.Size / 28, 32); i++)
        {
            var e = img.R.Read(at + i * 28L, 28);
            if (e.Length < 28) break;
            uint type = U32(e, 12), size = U32(e, 16), pointer = U32(e, 24);
            rows.Add([DebugType(type), Stamp(U32(e, 4)), $"{U16(e, 8)}.{U16(e, 10)}", $"{size:N0}", $"0x{U32(e, 20):X8}", $"0x{pointer:X8}"]);
            if (type == 2 && img.R.Read(pointer, (int)Math.Min(size, 1024)) is { Length: > 24 } cv && cv[0] == 'R' && cv[1] == 'S' && cv[2] == 'D' && cv[3] == 'S')
            {
                int end = Array.IndexOf(cv, (byte)0, 24);
                fields.Add(("Symbols file", Printable(Encoding.UTF8.GetString(cv, 24, (end < 0 ? cv.Length : end) - 24))));
                fields.Add(("Symbols file ID", $"{new Guid(cv.AsSpan(4, 16)):B}, age {U32(cv, 20)}"));
            }
            else if (type == 16)
            {
                // The hash that replaces the timestamps of a reproducible build (its length, then the bytes).
                var repro = img.R.Read(pointer, (int)Math.Min(size, 256));
                fields.Add(("Reproducible build", repro.Length > 4 && U32(repro, 0) is var n and > 0 && n <= repro.Length - 4 ? "yes, hash " + Convert.ToHexStringLower(repro.AsSpan(4, (int)n)) : "yes"));
            }
            else if (type == 17) fields.Add(("Embedded portable PDB", $"{size:N0} bytes"));
            else if (type == 12 && img.R.Read(pointer, 20) is { Length: 20 } vc)
                // Object counts by compiler feature: how much of the code was built with /GS, /sdl, and guard checks.
                fields.Add(("Compiler features (objects)", $"C/C++ {U32(vc, 4):N0}, /GS {U32(vc, 8):N0}, /sdl {U32(vc, 12):N0}, guard {U32(vc, 16):N0}, before VC++ 11 {U32(vc, 0):N0}"));
            else if (type == 13 && Pogo(img.R.Read(pointer, (int)Math.Min(size, 256 * 1024))) is { } pogo) contributions = pogo;
            else if (type == 20 && img.R.Read(pointer, 4) is { Length: 4 } ex)
            {
                fields.Add(("Shadow stack (CET)", (U32(ex, 0) & 1) != 0 ? "yes" : "no"));
                fields.Add(("Extended DLL characteristics", Flags(U32(ex, 0), 8, [(1, "CET compatible"), (2, "CET strict"), (4, "CET relaxed context IP validation"),
                    (8, "CET dynamic APIs in process only"), (0x40, "forward CFI compatible"), (0x80, "hot patch compatible")])));
            }
        }
        if (!fields.Any(f => f.Item1 == "Shadow stack (CET)")) fields.Add(("Shadow stack (CET)", "no"));
        return new InspectionSection("Debug", fields)
        {
            Table = new InspectionTable(["Type", "Time date stamp", "Version", "Size", "Address", "File offset"], rows),
            Children = contributions is null ? [] : [contributions],
        };
    }

    /// <summary>
    /// The POGO debug entry: the linker's list of what went into each section (".text$mn", ".rdata$zzzdbg"…), with
    /// addresses and sizes: a map of the image that few tools show.
    /// </summary>
    private static InspectionSection? Pogo(byte[] d)
    {
        if (d.Length < 8) return null;
        uint signature = U32(d, 0);
        // The kind is four letters, most significant first: LTCG, PGU, PGI, PGO, SPGO.
        string kind = new string([.. BitConverter.GetBytes(signature).Reverse().Where(b => b != 0).Select(b => b is >= 0x20 and < 0x7F ? (char)b : '?')]);
        var rows = new List<string[]>();
        int at = 4;
        while (at + 9 <= d.Length && rows.Count < 2000)
        {
            uint rva = U32(d, at), size = U32(d, at + 4);
            int end = Array.IndexOf(d, (byte)0, at + 8);
            if (end < 0) break;
            rows.Add([Printable(Encoding.ASCII.GetString(d, at + 8, end - at - 8)), $"0x{rva:X8}", $"{size:N0}"]);
            at = (end + 1 + 3) & ~3;
        }
        return rows.Count == 0 ? null : new InspectionSection($"Linker's section contributions (POGO, {kind})", []) { Table = new InspectionTable(["Contribution", "Address", "Size"], rows) };
    }

    private static string DebugType(uint type) => type switch
    {
        1 => "COFF", 2 => "CodeView", 3 => "FPO", 4 => "Misc", 5 => "Exception", 6 => "Fixup", 7 => "OMAP to source", 8 => "OMAP from source",
        9 => "Borland", 11 => "CLSID", 12 => "VC feature", 13 => "POGO (profile-guided)", 14 => "ILTCG", 15 => "MPX", 16 => "Reproducible",
        17 => "Embedded portable PDB", 19 => "PDB checksum", 20 => "Extended DLL characteristics", _ => $"{type}",
    };

    private static InspectionSection? LoadConfiguration(Image img, (uint Rva, uint Size) dir, ushort machine)
    {
        var d = img.Read(dir.Rva, 320);
        if (d.Length < 8) return null;
        int size = (int)Math.Min(U32(d, 0), (uint)d.Length);
        bool pe64 = img.Pe64;
        int W = pe64 ? 8 : 4;
        ulong Word(int at) => at + W <= size ? pe64 ? U64(d, at) : U32(d, at) : 0;
        bool Has(int at, int width) => at + width <= size;
        var fields = new List<(string, string)>
        {
            ("Size", $"{U32(d, 0):N0} bytes"),
            ("Time date stamp", Stamp(U32(d, 4))),
        };
        // Field offsets differ between the 32-bit and 64-bit layouts.
        int cookie = pe64 ? 88 : 60, seTable = pe64 ? 96 : 64, seCount = pe64 ? 104 : 68, cfCheck = pe64 ? 112 : 72, cfTable = pe64 ? 128 : 80,
            cfCount = pe64 ? 136 : 84, guardFlags = pe64 ? 144 : 88, ehTable = pe64 ? 264 : 164, ehCount = pe64 ? 272 : 168, chpe = pe64 ? 200 : 124;
        if (Has(cookie, W)) fields.Add(("Security cookie", Word(cookie) == 0 ? "none" : $"0x{Word(cookie):X}"));
        if (machine == 0x14C && Has(seCount, W) && Word(seTable) != 0) fields.Add(("Safe exception handlers", $"{Word(seCount):N0} handlers"));
        if (Has(cfCheck, W) && Word(cfCheck) != 0) fields.Add(("Guard check function pointer", $"0x{Word(cfCheck):X}"));
        if (Has(cfCount, W) && Word(cfTable) != 0) fields.Add(("Guard function table", $"{Word(cfCount):N0} functions"));
        if (Has(guardFlags, 4))
            fields.Add(("Guard flags", Flags(U32(d, guardFlags) & 0x0FFFFFFF, 8, [
                (0x100, "CF instrumented"), (0x200, "CF write instrumented"), (0x400, "CF function table"), (0x800, "security cookie unused"),
                (0x1000, "protected delay-load IAT"), (0x2000, "delay-load IAT in its own section"), (0x4000, "export suppression info"),
                (0x8000, "export suppression"), (0x10000, "long jump table"), (0x20000, "return flow instrumented"), (0x40000, "return flow"),
                (0x80000, "return flow strict"), (0x100000, "retpoline"), (0x400000, "EH continuation table"), (0x800000, "XFG"),
                (0x1000000, "CastGuard"), (0x2000000, "memcpy guard")])));
        if (Has(ehCount, W) && Word(ehTable) != 0) fields.Add(("EH continuation targets", $"{Word(ehCount):N0}"));
        if (Has(chpe, W) && Word(chpe) != 0) fields.Add(("Hybrid (CHPE) metadata", $"0x{Word(chpe):X}"));
        return new InspectionSection("Load configuration", fields);
    }

    private static InspectionSection? Tls(Image img, uint rva)
    {
        int w = img.Pe64 ? 8 : 4;
        var d = img.Read(rva, w * 4 + 8);
        if (d.Length < w * 4 + 8) return null;
        ulong Word(int i) => img.Pe64 ? U64(d, i * w) : U32(d, i * w);
        int callbacks = 0;
        if (Word(3) != 0 && img.Rva(Word(3)) is uint list)
        {
            var array = img.Read(list, 64 * w);
            while (callbacks < 64 && (callbacks + 1) * w <= array.Length && (img.Pe64 ? U64(array, callbacks * w) : U32(array, callbacks * w)) != 0) callbacks++;
        }
        return new InspectionSection("Thread-local storage (TLS)", [
            ("Raw data", $"0x{Word(0):X} to 0x{Word(1):X} ({(Word(1) >= Word(0) ? Word(1) - Word(0) : 0):N0} bytes)"),
            ("Index address", $"0x{Word(2):X}"),
            ("Callbacks", callbacks == 0 ? "none" : $"{callbacks}{(callbacks == 64 ? "+" : "")} (they run before the entry point)"),
            ("Size of zero fill", $"{U32(d, w * 4):N0} bytes"),
        ]);
    }

    private static InspectionSection RelocationsAndExceptions(Image img, (uint Rva, uint Size) relocations, (uint Rva, uint Size) exceptions, ushort machine, CancellationToken ct)
    {
        var fields = new List<(string, string)>();
        if (relocations.Rva != 0 && img.Offset(relocations.Rva) is var at and >= 0)
        {
            long blocks = 0, entries = 0, done = 0;
            while (done + 8 <= relocations.Size && blocks < 100_000)
            {
                if (blocks % 4096 == 0) ct.ThrowIfCancellationRequested();
                var head = img.R.Read(at + done, 8);
                if (head.Length < 8 || U32(head, 4) < 8) break;
                blocks++;
                entries += (U32(head, 4) - 8) / 2;
                done += U32(head, 4);
            }
            fields.Add(("Base relocations", $"{entries:N0} in {blocks:N0} pages"));
        }
        // Unwind information: a function table entry per function (12 bytes on x64, 8 on ARM64).
        int entry = machine switch { 0x8664 => 12, 0xAA64 or 0xA641 or 0xA64E => 8, _ => 0 };
        if (exceptions.Size != 0 && entry != 0) fields.Add(("Functions with unwind information", $"{exceptions.Size / entry:N0}"));
        return new InspectionSection("Relocations and unwinding", fields);
    }

    // ---- Names --------------------------------------------------------------------------------------------------------

    private static string Machine(ushort m) => m switch
    {
        0x14C => "x86",
        0x8664 => "x64",
        0xAA64 => "ARM64",
        0xA641 => "ARM64EC",
        0xA64E => "ARM64X",
        0x1C0 => "ARM",
        0x1C2 => "ARM (Thumb)",
        0x1C4 => "ARM (Thumb-2)",
        0x200 => "Itanium",
        0x5032 => "RISC-V 32",
        0x5064 => "RISC-V 64",
        0x6232 => "LoongArch 32",
        0x6264 => "LoongArch 64",
        0xEBC => "EFI byte code",
        0 => "any (unknown)",
        _ => $"0x{m:X4}",
    };

    private static string Subsystem(ushort s) => s switch
    {
        1 => "Native (driver or kernel)",
        2 => "Windows GUI",
        3 => "Windows console",
        5 => "OS/2 console",
        7 => "POSIX console",
        9 => "Windows CE",
        10 => "EFI application",
        11 => "EFI boot driver",
        12 => "EFI runtime driver",
        13 => "EFI ROM",
        14 => "Xbox",
        16 => "Windows boot application",
        _ => s.ToString(CultureInfo.InvariantCulture),
    };

    private static string SectionAccess(uint f) =>
        ((f & 0x40000000) != 0 ? "R" : "-") + ((f & 0x80000000) != 0 ? "W" : "-") + ((f & 0x20000000) != 0 ? "X" : "-") +
        ((f & 0x20) != 0 ? " code" : (f & 0x40) != 0 ? " data" : (f & 0x80) != 0 ? " bss" : "");

    private static string SectionFlags(uint f)
    {
        var names = new List<string>();
        if ((f & 0x02000000) != 0) names.Add("discardable");
        if ((f & 0x10000000) != 0) names.Add("shared");
        if ((f & 0x04000000) != 0) names.Add("not cached");
        if ((f & 0x08000000) != 0) names.Add("not paged");
        if ((f & 0x00000200) != 0) names.Add("comments");
        return $"0x{f:X8}" + (names.Count > 0 ? " " + string.Join(", ", names) : "");
    }

    private static string Ascii(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        return Printable(Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]));
    }

    private static string Printable(string s) => new(s.Select(c => char.IsControl(c) ? '?' : c).ToArray());

    /// <summary>A byte, or 0 past the end: an optional header may be shorter than its fields (a damaged size, a cut file).</summary>
    private static byte U8(ReadOnlySpan<byte> b, int at) => at >= 0 && at < b.Length ? b[at] : (byte)0;

    private static ushort U16(ReadOnlySpan<byte> b, int at) => at >= 0 && at + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b[at..]) : (ushort)0;

    private static uint U32(ReadOnlySpan<byte> b, int at) => at >= 0 && at + 4 <= b.Length ? BinaryPrimitives.ReadUInt32LittleEndian(b[at..]) : 0;

    private static ulong U64(ReadOnlySpan<byte> b, int at) => at >= 0 && at + 8 <= b.Length ? BinaryPrimitives.ReadUInt64LittleEndian(b[at..]) : 0;
}
