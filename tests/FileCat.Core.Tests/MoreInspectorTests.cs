using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FileCat.Core.Inspect;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>P8 inspectors: ELF, Mach-O and Java class, APK/AAB, audio/video containers, and HTML.</summary>
public sealed class MoreInspectorTests
{
    private static InspectionReport? Inspect(byte[] bytes) => Inspectors.Inspect(new MemoryContentSource("x", bytes), TestContext.Current.CancellationToken);

    private static Dictionary<string, string> Fields(InspectionReport report, string section) =>
        report.Sections.First(s => s.Title.Contains(section, StringComparison.Ordinal)).Fields.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First().Value);

    private sealed class Writer
    {
        private readonly MemoryStream _s = new();
        public int Position => (int)_s.Position;
        public Writer At(int offset) { while (_s.Length < offset) _s.WriteByte(0); _s.Position = offset; return this; }
        public Writer U8(int v) { _s.WriteByte((byte)v); return this; }
        public Writer U16(int v, bool big = false) { Span<byte> b = stackalloc byte[2]; if (big) BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); else BinaryPrimitives.WriteUInt16LittleEndian(b, (ushort)v); _s.Write(b); return this; }
        public Writer U32(uint v, bool big = false) { Span<byte> b = stackalloc byte[4]; if (big) BinaryPrimitives.WriteUInt32BigEndian(b, v); else BinaryPrimitives.WriteUInt32LittleEndian(b, v); _s.Write(b); return this; }
        public Writer U64(ulong v, bool big = false) { Span<byte> b = stackalloc byte[8]; if (big) BinaryPrimitives.WriteUInt64BigEndian(b, v); else BinaryPrimitives.WriteUInt64LittleEndian(b, v); _s.Write(b); return this; }
        public Writer Bytes(ReadOnlySpan<byte> b) { _s.Write(b); return this; }
        public Writer Ascii(string s) => Bytes(Encoding.ASCII.GetBytes(s));
        public byte[] ToArray() => _s.ToArray();
    }

    // ---- ELF ---------------------------------------------------------------------------------------------------

    internal static byte[] Elf()
    {
        const ulong Base = 0x400000;
        var w = new Writer();
        w.Bytes([0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 3]).At(16);
        w.U16(3).U16(62).U32(1).U64(Base + 0x1000).U64(0x40).U64(0x340).U32(0).U16(64).U16(56).U16(5).U16(64).U16(3).U16(2);
        void Ph(uint type, uint flags, ulong offset, ulong size) => w.U32(type).U32(flags).U64(offset).U64(Base + offset).U64(Base + offset).U64(size).U64(size).U64(8);
        w.At(0x40);
        Ph(3, 4, 0x200, 28);          // PT_INTERP
        Ph(1, 5, 0, 0x400);           // PT_LOAD over the whole file
        Ph(2, 6, 0x280, 0x60);        // PT_DYNAMIC
        Ph(0x6474E551, 6, 0, 0);      // PT_GNU_STACK: read/write, not executable
        Ph(0x6474E552, 4, 0x280, 0x60); // PT_GNU_RELRO
        w.At(0x200).Ascii("/lib64/ld-linux-x86-64.so.2\0");
        w.At(0x240).Ascii("\0libc.so.6\0libm.so.6\0");
        w.At(0x280).U64(1).U64(1).U64(1).U64(11).U64(5).U64(Base + 0x240).U64(10).U64(0x20).U64(0x6FFFFFFB).U64(1).U64(0).U64(0);
        w.At(0x300).Ascii("\0.text\0.shstrtab\0");
        w.At(0x340).Bytes(new byte[64]);
        w.U32(1).U32(1).U64(6).U64(Base + 0x1000).U64(0x1000).U64(0x100).Bytes(new byte[24]); // .text
        w.U32(7).U32(3).U64(0).U64(0).U64(0x300).U64(0x11).Bytes(new byte[24]);                // .shstrtab
        w.At(0x400);
        return w.ToArray();
    }

    [Fact]
    public void An_ELF_library_shows_its_interpreter_libraries_and_hardening()
    {
        var report = Inspect(Elf())!;
        Assert.Equal("ELF 64-bit shared object · x86-64", report.Format);
        Assert.Equal("/lib64/ld-linux-x86-64.so.2", Fields(report, "Header")["Interpreter"]);
        var libs = report.Sections.First(s => s.Title.StartsWith("Dynamic", StringComparison.Ordinal)).Fields;
        Assert.Equal(["libc.so.6", "libm.so.6"], libs.Where(f => f.Name == "Needs").Select(f => f.Value));
        var security = Fields(report, "Security");
        Assert.Equal(("yes", "yes", "full (with BIND_NOW)"), (security["Position independent (PIE)"], security["Non-executable stack"], security["RELRO"]));
        Assert.Contains(report.Sections.First(s => s.Title.StartsWith("Sections", StringComparison.Ordinal)).Table!.Rows, row => row[1] == ".text" && row[2] == "PROGBITS" && row[6] == "AX");
        // readelf's tables: program headers and the dynamic section, decoded.
        Assert.Equal(["INTERP", "LOAD", "DYNAMIC", "GNU_STACK", "GNU_RELRO"], report.Sections.First(s => s.Title.StartsWith("Program headers", StringComparison.Ordinal)).Table!.Rows.Select(r => r[0]));
        var dynamic = report.Sections.First(s => s.Title.StartsWith("Dynamic section", StringComparison.Ordinal)).Table!.Rows;
        Assert.Contains(dynamic, row => row[0] == "NEEDED" && row[1] == "libm.so.6");
        Assert.Contains(dynamic, row => row[0] == "FLAGS_1" && row[1] == "0x1 (NOW)");
        Assert.Contains("ELF header", report.ToText(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A shared object with a dynamic symbol table: two imports with their glibc versions, one export, a build ID note,
    /// and an x86 property note for CET.
    /// </summary>
    internal static byte[] ElfWithSymbols()
    {
        var w = new Writer();
        w.Bytes([0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0]).At(16);
        w.U16(3).U16(62).U32(1).U64(0x1000).U64(0x40).U64(0x800).U32(0).U16(64).U16(56).U16(3).U16(64).U16(7).U16(6);
        void Ph(uint type, uint flags, ulong offset, ulong size) => w.U32(type).U32(flags).U64(offset).U64(offset).U64(offset).U64(size).U64(size).U64(8);
        w.At(0x40);
        Ph(1, 5, 0, 0x1000);    // PT_LOAD over the file
        Ph(2, 6, 0x400, 0x40);  // PT_DYNAMIC
        Ph(4, 4, 0x500, 56);    // PT_NOTE
        // .dynsym: none, puts and __stack_chk_fail (undefined), my_export (defined).
        w.At(0x100).Bytes(new byte[24]);
        w.U32(1).U8(0x12).U8(0).U16(0).U64(0).U64(0);
        w.U32(6).U8(0x12).U8(0).U16(0).U64(0).U64(0);
        w.U32(23).U8(0x12).U8(0).U16(1).U64(0x1234).U64(42);
        w.At(0x200).Ascii("\0puts\0__stack_chk_fail\0my_export\0libc.so.6\0GLIBC_2.2.5\0GLIBC_2.4\0");
        // .gnu.version: puts GLIBC_2.2.5 (2), __stack_chk_fail GLIBC_2.4 (3), my_export global (1).
        w.At(0x280).U16(0).U16(2).U16(3).U16(1);
        // .gnu.version_r: libc.so.6 needs GLIBC_2.2.5 and GLIBC_2.4.
        w.At(0x2A0).U16(1).U16(2).U32(33).U32(16).U32(0);
        w.U32(0).U16(0).U16(2).U32(43).U32(16);
        w.U32(0).U16(0).U16(3).U32(55).U32(0);
        w.At(0x400).U64(1).U64(33).U64(5).U64(0x200).U64(10).U64(65).U64(0).U64(0);
        // Notes: a build ID, and the x86 feature property with IBT and SHSTK.
        w.At(0x500).U32(4).U32(8).U32(3).Ascii("GNU\0").Bytes([0xDE, 0xAD, 0xBE, 0xEF, 1, 2, 3, 4]);
        w.U32(4).U32(16).U32(5).Ascii("GNU\0").U32(0xC0000002).U32(4).U32(3).U32(0);
        w.At(0x700).Ascii("\0.dynsym\0.dynstr\0.gnu.version\0.gnu.version_r\0.note\0.shstrtab\0");
        w.At(0x800).Bytes(new byte[64]);
        void Sh(uint name, uint type, ulong offset, ulong size, uint link, uint info, ulong align, ulong entsize) =>
            w.U32(name).U32(type).U64(2).U64(offset).U64(offset).U64(size).U32(link).U32(info).U64(align).U64(entsize);
        Sh(1, 11, 0x100, 96, 2, 1, 8, 24);
        Sh(9, 3, 0x200, 65, 0, 0, 1, 0);
        Sh(17, 0x6FFFFFFF, 0x280, 8, 1, 0, 2, 2);
        Sh(30, 0x6FFFFFFE, 0x2A0, 48, 2, 1, 8, 0);
        Sh(45, 7, 0x500, 56, 0, 0, 4, 0);
        Sh(51, 3, 0x700, 61, 0, 0, 1, 0);
        return w.ToArray();
    }

    [Fact]
    public void ELF_symbols_come_with_their_versions_and_notes_with_build_ID_and_CET()
    {
        var report = Inspect(ElfWithSymbols())!;
        Assert.Equal("deadbeef01020304", Fields(report, "Header")["Build ID"]);
        var imports = report.Sections.First(s => s.Title.StartsWith("Imported symbols", StringComparison.Ordinal)).Table!.Rows;
        Assert.Equal([["puts", "GLIBC_2.2.5", "function", "global"], ["__stack_chk_fail", "GLIBC_2.4", "function", "global"]], imports);
        var exports = report.Sections.First(s => s.Title.StartsWith("Exported symbols", StringComparison.Ordinal)).Table!.Rows;
        Assert.Equal([["my_export", "", "function", "0x1234", "42"]], exports);
        // The newest glibc the imports need: the oldest system the file runs on.
        Assert.Equal("GLIBC_2.4", Fields(report, "Dynamic linking")["Newest glibc version needed"]);
        var security = Fields(report, "Security");
        Assert.Equal(("yes (__stack_chk_fail is used)", "yes", "yes"), (security["Stack protector"], security["Indirect branch tracking (CET IBT)"], security["Shadow stack (CET SHSTK)"]));
        Assert.Equal("IBT, SHSTK", Fields(report, "Notes")["x86 features"]);
    }

    // ---- Mach-O and Java class ---------------------------------------------------------------------------------------

    internal static byte[] MachO(uint cpu = 0x0100000C)
    {
        var cmds = new Writer();
        var dylib = "/usr/lib/libSystem.B.dylib\0\0\0\0\0\0";
        cmds.U32(0xC).U32((uint)(24 + dylib.Length)).U32(24).U32(2).U32(0x10000).U32(0x10000).Ascii(dylib);
        cmds.U32(0x32).U32(24).U32(1).U32(0x000E0000).U32(0x000F0200).U32(0);
        cmds.U32(0x1B).U32(24).Bytes(Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());
        cmds.U32(0x1D).U32(16).U32(0x8000).U32(0x100);
        cmds.U32(0x80000028).U32(24).U64(0x4000).U64(0);
        var c = cmds.ToArray();
        return new Writer().U32(0xFEEDFACF).U32(cpu).U32(0).U32(2).U32(5).U32((uint)c.Length).U32(0x200085).U32(0).Bytes(c).ToArray();
    }

    [Fact]
    public void Mach_O_thin_and_universal_binaries_and_Java_classes_are_told_apart()
    {
        var thin = Inspect(MachO())!;
        Assert.Equal("Mach-O 64-bit · ARM64", thin.Format);
        var header = Fields(thin, "Header");
        Assert.Equal(("Executable", "yes"), (header["Type"], header["Position independent (PIE)"]));
        Assert.Equal(("macOS", "14.0", "15.2"), (Fields(thin, "Build")["Platform"], Fields(thin, "Build")["Minimum OS"], Fields(thin, "Build")["SDK"]));
        Assert.Equal(["load", "1.0", "1.0", "/usr/lib/libSystem.B.dylib"], thin.Sections.First(s => s.Title.StartsWith("Libraries", StringComparison.Ordinal)).Table!.Rows.Single());
        Assert.StartsWith("Present, not verified", Fields(thin, "Signature")["Status"]);

        var x64 = MachO(0x01000007);
        var arm = MachO();
        var fat = new Writer().U32(0xCAFEBABE, big: true).U32(2, big: true)
            .U32(0x01000007, big: true).U32(3, big: true).U32(4096, big: true).U32((uint)x64.Length, big: true).U32(12, big: true)
            .U32(0x0100000C, big: true).U32(0, big: true).U32(8192, big: true).U32((uint)arm.Length, big: true).U32(14, big: true)
            .At(4096).Bytes(x64).At(8192).Bytes(arm).ToArray();
        var universal = Inspect(fat)!;
        Assert.Equal("Mach-O universal binary · x86-64, ARM64", universal.Format);
        Assert.Contains(universal.Sections, s => s.Title.StartsWith("x86-64: Header", StringComparison.Ordinal));

        var java = Inspect(new Writer().U32(0xCAFEBABE, big: true).U16(0, big: true).U16(61, big: true).Bytes(new byte[40]).ToArray())!;
        Assert.Equal("Java class file · Java 17", java.Format);
    }

    /// <summary>
    /// A signed Mach-O executable: a __TEXT segment with its section, a symbol table with an import and an export, and a
    /// code signature whose code directory names the identifier and team and asks for the hardened runtime, with
    /// entitlements.
    /// </summary>
    internal static byte[] MachOSigned()
    {
        const int SymbolsAt = 0x1000, StringsAt = 0x1020, SignatureAt = 0x1040;
        var cmds = new Writer();
        cmds.U32(0x19).U32(152).Ascii("__TEXT").Bytes(new byte[10]).U64(0x100000000).U64(0x4000).U64(0).U64(0x2000).U32(5).U32(5).U32(1).U32(0);
        cmds.Ascii("__text").Bytes(new byte[10]).Ascii("__TEXT").Bytes(new byte[10]).U64(0x100000400).U64(0x100).U32(0x400).U32(4).U32(0).U32(0).U32(0x80000400).U32(0).U32(0).U32(0);
        var dylib = "/usr/lib/libSystem.B.dylib\0\0\0\0\0\0";
        cmds.U32(0xC).U32((uint)(24 + dylib.Length)).U32(24).U32(2).U32(0x05180000).U32(0x10000).Ascii(dylib);
        cmds.U32(0x2).U32(24).U32(SymbolsAt).U32(2).U32(StringsAt).U32(13);
        // The signature: a SuperBlob (big-endian) holding a code directory and entitlements.
        var cd = new Writer();
        string identifier = "com.example.tool\0", team = "ABCDE12345\0";
        int identOffset = 88, teamOffset = identOffset + identifier.Length, hashOffset = teamOffset + team.Length;
        cd.U32(0xFADE0C02, big: true).U32((uint)(hashOffset + 32), big: true).U32(0x20400, big: true).U32(0x10000, big: true)
          .U32((uint)hashOffset, big: true).U32((uint)identOffset, big: true).U32(0, big: true).U32(1, big: true).U32(0x2000, big: true)
          .U8(32).U8(2).U8(0).U8(12).U32(0, big: true).U32(0, big: true).U32((uint)teamOffset, big: true).U32(0, big: true)
          .U64(0, big: true).U64(0, big: true).U64(0, big: true).U64(0, big: true)
          .Ascii(identifier).Ascii(team).Bytes(new byte[32]);
        var cdBytes = cd.ToArray();
        var xml = "<plist><dict><key>com.apple.security.app-sandbox</key><true/></dict></plist>";
        var entitlements = new Writer().U32(0xFADE7171, big: true).U32((uint)(8 + xml.Length), big: true).Ascii(xml).ToArray();
        int total = 28 + cdBytes.Length + entitlements.Length;
        var blob = new Writer().U32(0xFADE0CC0, big: true).U32((uint)total, big: true).U32(2, big: true)
            .U32(0, big: true).U32(28, big: true).U32(5, big: true).U32((uint)(28 + cdBytes.Length), big: true).Bytes(cdBytes).Bytes(entitlements).ToArray();
        cmds.U32(0x1D).U32(16).U32(SignatureAt).U32((uint)blob.Length);
        cmds.U32(0x80000028).U32(24).U64(0x400).U64(0);
        var c = cmds.ToArray();
        var w = new Writer().U32(0xFEEDFACF).U32(0x0100000C).U32(0).U32(2).U32(5).U32((uint)c.Length).U32(0x200085).U32(0).Bytes(c);
        // Symbols: _puts (undefined, from the first library) and _main (defined in section 1).
        w.At(SymbolsAt).U32(1).U8(0x01).U8(0).U16(0x0100).U64(0);
        w.U32(7).U8(0x0F).U8(1).U16(0).U64(0x100000400);
        w.At(StringsAt).Ascii("\0_puts\0_main\0");
        w.At(SignatureAt).Bytes(blob);
        return w.ToArray();
    }

    [Fact]
    public void A_signed_Mach_O_shows_its_signature_symbols_segments_and_libraries()
    {
        var report = Inspect(MachOSigned())!;
        var signature = Fields(report, "Signature");
        Assert.Equal(("com.example.tool", "ABCDE12345"), (signature["Identifier"], signature["Team ID"]));
        Assert.Contains("hardened runtime", signature["Code signing flags"]);
        Assert.Equal("yes", Fields(report, "Security")["Hardened runtime"]);
        Assert.Contains(report.Sections.First(s => s.Title == "Signature").Children.Single(c => c.Title == "Entitlements").Lines, l => l.Contains("app-sandbox", StringComparison.Ordinal));
        Assert.Equal([["_puts", "/usr/lib/libSystem.B.dylib"]], report.Sections.First(s => s.Title.StartsWith("Imported symbols", StringComparison.Ordinal)).Table!.Rows);
        Assert.Equal([["_main", "0x100000400"]], report.Sections.First(s => s.Title.StartsWith("Exported symbols", StringComparison.Ordinal)).Table!.Rows);
        Assert.Contains(report.Sections.First(s => s.Title.StartsWith("Sections", StringComparison.Ordinal)).Table!.Rows, row => row[0] == "__TEXT" && row[1] == "__text" && row[6] == "regular");
        Assert.Equal(["load", "1304.0", "1.0", "/usr/lib/libSystem.B.dylib"], report.Sections.First(s => s.Title.StartsWith("Libraries", StringComparison.Ordinal)).Table!.Rows.Single());
        Assert.Contains(report.Sections.First(s => s.Title.StartsWith("Load commands", StringComparison.Ordinal)).Table!.Rows, row => row[1] == "LC_CODE_SIGNATURE");
    }

    // ---- APK and AAB -------------------------------------------------------------------------------------------------

    /// <summary>A minimal binary-XML (AXML) manifest, as aapt writes it.</summary>
    internal static byte[] BinaryManifest()
    {
        string[] strings = ["", "package", "versionCode", "versionName", "manifest", "com.example.app", "1.2.3", "uses-sdk", "minSdkVersion", "targetSdkVersion",
                            "uses-permission", "name", "android.permission.INTERNET", "application", "debuggable", "activity", "exported"];
        var pool = new Writer();
        var data = new Writer();
        var offsets = new List<uint>();
        foreach (var s in strings)
        {
            offsets.Add((uint)data.Position);
            data.U16(s.Length).Bytes(Encoding.Unicode.GetBytes(s)).U16(0);
        }
        while (data.Position % 4 != 0) data.U8(0);
        var d = data.ToArray();
        int poolSize = 28 + 4 * strings.Length + d.Length;
        pool.U16(1).U16(28).U32((uint)poolSize).U32((uint)strings.Length).U32(0).U32(0).U32((uint)(28 + 4 * strings.Length)).U32(0);
        foreach (var o in offsets) pool.U32(o);
        pool.Bytes(d);
        var body = new Writer().Bytes(pool.ToArray());
        const uint None = 0xFFFFFFFF;
        void Start(int name, params (int Name, int Type, uint Data, uint Raw)[] attrs)
        {
            body.U16(0x0102).U16(16).U32((uint)(36 + 20 * attrs.Length)).U32(1).U32(None);
            body.U32(None).U32((uint)name).U16(20).U16(20).U16(attrs.Length).U16(0).U16(0).U16(0);
            foreach (var (n, t, v, raw) in attrs) body.U32(None).U32((uint)n).U32(raw).U16(8).U8(0).U8(t).U32(v);
        }
        void End(int name) => body.U16(0x0103).U16(16).U32(24).U32(1).U32(None).U32(None).U32((uint)name);
        Start(4, (1, 0x03, 5, 5), (2, 0x10, 12, None), (3, 0x03, 6, 6));
        Start(7, (8, 0x10, 24, None), (9, 0x10, 34, None));
        End(7);
        Start(10, (11, 0x03, 12, 12));
        End(10);
        Start(13, (14, 0x12, 0xFFFFFFFF, None));
        Start(15, (16, 0x12, 1, None));
        End(15);
        End(13);
        End(4);
        var b = body.ToArray();
        return new Writer().U16(3).U16(8).U32((uint)(8 + b.Length)).Bytes(b).ToArray();
    }

    private static byte[] Zip(params (string Name, byte[] Data)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in entries)
                using (var s = zip.CreateEntry(name).Open()) s.Write(data);
        return ms.ToArray();
    }

    /// <summary>Protocol-buffer fields: length-delimited (wire type 2) only, as aapt2's XmlNode uses them.</summary>
    private static byte[] Proto(int field, params byte[][] parts)
    {
        var payload = parts.SelectMany(p => p).ToArray();
        var w = new MemoryStream();
        void Varint(ulong v) { while (v >= 0x80) { w.WriteByte((byte)(v | 0x80)); v >>= 7; } w.WriteByte((byte)v); }
        Varint((ulong)(field << 3 | 2));
        Varint((ulong)payload.Length);
        w.Write(payload);
        return w.ToArray();
    }

    private static byte[] ProtoString(int field, string s) => Proto(field, Encoding.UTF8.GetBytes(s));

    private static byte[] ProtoAttribute(string name, string value) => Proto(4, ProtoString(2, name), ProtoString(3, value));

    private static byte[] ProtoElement(string name, byte[][] attributes, params byte[][] children) =>
        Proto(1, [ProtoString(3, name), .. attributes, .. children.Select(c => Proto(5, c))]);

    [Fact]
    public void Android_packages_show_package_versions_permissions_and_signature_presence()
    {
        var apk = Zip(("AndroidManifest.xml", BinaryManifest()), ("classes.dex", [1]), ("classes2.dex", [2]), ("lib/arm64-v8a/libx.so", [3]),
            ("lib/x86_64/libx.so", [4]), ("META-INF/CERT.RSA", [5]));
        var report = Inspect(apk)!;
        Assert.Equal("Android package (APK)", report.Format);
        var package = Fields(report, "Package");
        Assert.Equal(("com.example.app", "1.2.3 (code 12)", "24 (Android 7)", "34 (Android 14)", "yes: a debug build"),
            (package["Package"], package["Version"], package["Minimum SDK"], package["Target SDK"], package["Debuggable"]));
        Assert.Equal("android.permission.INTERNET", Assert.Single(report.Sections.First(s => s.Title.StartsWith("Permissions", StringComparison.Ordinal)).Fields).Name);
        Assert.Equal("1 (1 exported)", Fields(report, "Components")["activity"]);
        Assert.Equal(("2", "arm64-v8a, x86_64"), (Fields(report, "Contents")["Dex files"], Fields(report, "Contents")["Native code"]));
        Assert.Equal("JAR (v1)", Fields(report, "Signature")["Schemes"]);

        var manifest = ProtoElement("manifest", [ProtoAttribute("package", "com.example.bundle"), ProtoAttribute("versionCode", "7"), ProtoAttribute("versionName", "2.0")],
            ProtoElement("uses-sdk", [ProtoAttribute("minSdkVersion", "26")]),
            ProtoElement("uses-permission", [ProtoAttribute("name", "android.permission.CAMERA")]));
        var aab = Zip(("BundleConfig.pb", [0]), ("base/manifest/AndroidManifest.xml", manifest), ("base/dex/classes.dex", [1]),
            ("camera/manifest/AndroidManifest.xml", ProtoElement("manifest", [])));
        var bundle = Inspect(aab)!;
        Assert.Equal("Android App Bundle (AAB)", bundle.Format);
        Assert.Equal(("com.example.bundle", "2.0 (code 7)", "26 (Android 8)"), (Fields(bundle, "Package")["Package"], Fields(bundle, "Package")["Version"], Fields(bundle, "Package")["Minimum SDK"]));
        Assert.Equal("base, camera", Fields(bundle, "Contents")["Modules"]);
        Assert.Null(Inspect(Zip(("readme.txt", [1]))));
    }

    // ---- Audio and video ---------------------------------------------------------------------------------------------

    internal static byte[] Wav() => new Writer().Ascii("RIFF").U32(36 + 176_400).Ascii("WAVE").Ascii("fmt ").U32(16).U16(1).U16(2).U32(44_100).U32(176_400).U16(4).U16(16)
        .Ascii("data").U32(176_400).Bytes(new byte[176_400]).ToArray();

    internal static byte[] Flac()
    {
        var info = new Writer().U16(4096, big: true).U16(4096, big: true).Bytes([0, 0, 0, 0, 0, 0]);
        // 48 kHz, 2 channels, 24-bit, 144,000 samples (3 s).
        ulong packed = (48_000UL << 44) | (1UL << 41) | (23UL << 36) | 144_000UL;
        info.U64(packed, big: true).Bytes(new byte[16]);
        var comment = new Writer().U32(6).Ascii("FileCa").U32(2).U32(11).Ascii("TITLE=Sound").U32(12).Ascii("ARTIST=Nobod");
        var c = comment.ToArray();
        return new Writer().Ascii("fLaC").U8(0).Bytes([0, 0, 34]).Bytes(info.ToArray()).U8(0x84).Bytes([(byte)(c.Length >> 16), (byte)(c.Length >> 8), (byte)c.Length]).Bytes(c).ToArray();
    }

    internal static byte[] Mp3()
    {
        byte[] Frame(string id, string text) => new Writer().Ascii(id).U32((uint)(text.Length + 1), big: true).U16(0).U8(0).Ascii(text).ToArray();
        var frames = Frame("TIT2", "Tune").Concat(Frame("TPE1", "Band")).ToArray();
        var tag = new Writer().Ascii("ID3").U8(3).U8(0).U8(0).Bytes([0, 0, (byte)(frames.Length >> 7), (byte)(frames.Length & 0x7F)]).Bytes(frames).ToArray();
        var audio = new byte[128_000 / 8 * 2]; // two seconds at 128 kbit/s
        audio[0] = 0xFF; audio[1] = 0xFB; audio[2] = 0x90; audio[3] = 0x64;
        return tag.Concat(audio).ToArray();
    }

    private static byte[] Box(string type, params byte[][] content)
    {
        var payload = content.SelectMany(c => c).ToArray();
        return new Writer().U32((uint)(8 + payload.Length), big: true).Ascii(type).Bytes(payload).ToArray();
    }

    internal static byte[] Mp4()
    {
        var mvhd = Box("mvhd", new Writer().U32(0).U32(0).U32(0).U32(1000, big: true).U32(5000, big: true).Bytes(new byte[80]).ToArray());
        byte[] Stsd(string codec, byte[] extra) => Box("stsd", new Writer().U32(0).U32(1, big: true).U32((uint)(16 + extra.Length), big: true).Ascii(codec).Bytes(extra).ToArray());
        var tkhd = Box("tkhd", new Writer().Bytes(new byte[76]).U32(1280 << 16, big: true).U32(720 << 16, big: true).ToArray());
        var video = Box("trak", tkhd, Box("mdia", Box("hdlr", new Writer().U32(0).U32(0).Ascii("vide").Bytes(new byte[12]).ToArray()),
            Box("minf", Box("stbl", Stsd("avc1", new byte[70])))));
        var audioEntry = new Writer().Bytes(new byte[16]).U16(2, big: true).U16(16, big: true).U32(0).U32(48_000u << 16, big: true).ToArray();
        var audio = Box("trak", Box("mdia", Box("hdlr", new Writer().U32(0).U32(0).Ascii("soun").Bytes(new byte[12]).ToArray()),
            Box("minf", Box("stbl", Stsd("mp4a", audioEntry)))));
        return Box("ftyp", new Writer().Ascii("isom").U32(0x200).Ascii("isomiso2avc1mp41").ToArray()).Concat(Box("mdat", new byte[100]))
            .Concat(Box("moov", mvhd, video, audio)).ToArray();
    }

    private static byte[] Ebml(uint id, params byte[][] content)
    {
        var payload = content.SelectMany(c => c).ToArray();
        var w = new Writer();
        if (id > 0xFFFFFF) w.U32(id, big: true); else if (id > 0xFFFF) w.Bytes([(byte)(id >> 16), (byte)(id >> 8), (byte)id]); else if (id > 0xFF) w.U16((int)id, big: true); else w.U8((int)id);
        w.U8(0x01).U16(0).U8(0).U32((uint)payload.Length, big: true); // an 8-byte size (marker 0x01, then 7 bytes)
        return w.Bytes(payload).ToArray();
    }

    internal static byte[] Webm()
    {
        var header = Ebml(0x1A45DFA3, Ebml(0x4282, "webm"u8.ToArray()));
        var info = Ebml(0x1549A966, Ebml(0x2AD7B1, [0x0F, 0x42, 0x40]), Ebml(0x4489, BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(BitConverter.SingleToInt32Bits(3000f)))),
            Ebml(0x4D80, "FileCatMux"u8.ToArray()));
        var track = Ebml(0xAE, Ebml(0x83, [1]), Ebml(0x86, "V_VP9"u8.ToArray()), Ebml(0xE0, Ebml(0xB0, [0x02, 0x80]), Ebml(0xBA, [0x01, 0x68])));
        var segment = Ebml(0x18538067, info, Ebml(0x1654AE6B, track), Ebml(0x1F43B675, new byte[16]));
        return header.Concat(segment).ToArray();
    }

    [Fact]
    public void Audio_and_video_containers_show_duration_tracks_and_tags()
    {
        var wav = Inspect(Wav())!;
        Assert.Equal(("WAV audio", "0:01.0", "PCM"), (wav.Format, Fields(wav, "General")["Duration"], Fields(wav, "General")["Encoding"]));

        var flac = Inspect(Flac())!;
        Assert.Equal(("0:03.0", $"2 channels, {48_000:N0} Hz, 24-bit"), (Fields(flac, "General")["Duration"], Fields(flac, "General")["Audio"]));
        Assert.Equal("Sound", Fields(flac, "Tags")["Title"]);

        var mp3 = Inspect(Mp3())!;
        Assert.Equal("MP3 audio", mp3.Format);
        Assert.StartsWith("MPEG 1 layer 3, 128 kbit/s", Fields(mp3, "General")["Audio"]);
        Assert.Equal(("Tune", "Band"), (Fields(mp3, "Tags")["Title"], Fields(mp3, "Tags")["Artist"]));

        var mp4 = Inspect(Mp4())!;
        Assert.Equal("0:05.0", Fields(mp4, "General")["Duration"]);
        var tracks = mp4.Sections.First(s => s.Title.StartsWith("Tracks", StringComparison.Ordinal)).Fields;
        Assert.Equal(("Video", "H.264 (avc1), 1280 × 720"), tracks[0]);
        Assert.Equal("Audio", tracks[1].Name);
        Assert.StartsWith("AAC (mp4a), 2 channels", tracks[1].Value);

        var webm = Inspect(Webm())!;
        Assert.Equal(("WebM video", "0:03.0", "FileCatMux"), (webm.Format, Fields(webm, "General")["Duration"], Fields(webm, "General")["Muxing application"]));
        Assert.Equal(("Video", "V_VP9, 640 × 360"), webm.Sections.First(s => s.Title.StartsWith("Tracks", StringComparison.Ordinal)).Fields[0]);
    }

    // ---- HTML --------------------------------------------------------------------------------------------------------

    internal static byte[] Html() => Encoding.UTF8.GetBytes("""
        <!DOCTYPE html>
        <html lang="cs">
        <head><meta charset="utf-8"><title>Kočka &amp; spol.</title>
        <meta name="description" content="A test page">
        <link rel="stylesheet" href="https://fonts.example.com/css">
        <script src="https://cdn.example.com/app.js"></script><script>var x = 1;</script></head>
        <body><img src="//images.example.org/a.png"><a href="https://elsewhere.example.net/">link</a><form action="/x"></form>
        <iframe src="https://frames.example.com/"></iframe></body></html>
        """);

    [Fact]
    public void An_HTML_page_is_described_without_running_it()
    {
        var html = Inspect(Html())!;
        var page = Fields(html, "Page");
        Assert.Equal(("Kočka & spol.", "cs", "utf-8", "A test page"), (page["Title"], page["Language"], page["Character set"], page["Description"]));
        var contents = Fields(html, "Contents");
        Assert.Equal(("1 external, 1 inline", "1", "1", "1"), (contents["Scripts"], contents["Stylesheets"], contents["Forms"], contents["Frames"]));
        // Links load nothing; resources do.
        Assert.Equal(["cdn.example.com", "fonts.example.com", "frames.example.com", "images.example.org"],
            html.Sections.First(s => s.Title.StartsWith("Loads from", StringComparison.Ordinal)).Fields.Select(f => f.Name));
        Assert.Null(Inspect("just text, no markup"u8.ToArray()));
    }

    [Fact]
    public void Damaged_files_of_every_new_format_give_warnings_never_exceptions()
    {
        var rng = new Random(23);
        var apk = Zip(("AndroidManifest.xml", BinaryManifest()));
        foreach (var seed in new[] { Elf(), ElfWithSymbols(), MachO(), MachOSigned(), Wav()[..4096], Flac(), Mp3()[..2048], Mp4(), Webm(), Html(), apk, BinaryManifest() })
        {
            for (int round = 0; round < 250; round++)
            {
                var copy = seed[..rng.Next(1, seed.Length + 1)];
                for (int flips = rng.Next(0, 24); flips > 0; flips--) copy[rng.Next(copy.Length)] = (byte)rng.Next(256);
                Inspect(copy); // must not throw
            }
        }
        var manifest = new ApkInspector.Manifest();
        var damaged = BinaryManifest();
        for (int round = 0; round < 500; round++)
        {
            var copy = (byte[])damaged.Clone();
            for (int flips = rng.Next(1, 16); flips > 0; flips--) copy[rng.Next(copy.Length)] = (byte)rng.Next(256);
            try { ApkInspector.BinaryXml.Parse(copy, manifest, TestContext.Current.CancellationToken); }
            catch (InvalidDataException) { }
            catch (ArgumentException) { }
            catch (IndexOutOfRangeException) { }
            catch (OverflowException) { }
        }
    }
}
