using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using FileCat.Core.Inspect;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>
/// The executable inspectors checked against independent readers: the runtime's own PE reader (every platform),
/// Windows' version and signature APIs, readelf on Linux, and otool and codesign on macOS. What FileCat shows about a
/// real file must be what those tools read from it.
/// </summary>
public sealed class InspectorCrossCheckTests
{
    private static InspectionReport Inspect(string path)
    {
        using var source = new FileContentSource(path);
        return Inspectors.Inspect(source, TestContext.Current.CancellationToken)!;
    }

    private static InspectionReport Inspect(byte[] bytes) => Inspectors.Inspect(new MemoryContentSource("x", bytes), TestContext.Current.CancellationToken)!;

    private static InspectionSection Section(InspectionReport report, string title) => report.Sections.First(s => s.Title.StartsWith(title, StringComparison.Ordinal));

    private static string Field(InspectionReport report, string section, string name) => Section(report, section).Fields.First(f => f.Name == name).Value;

    /// <summary>The number a field starts with: "0x00001000 (4,096)" is 0x1000.</summary>
    private static ulong Number(string value)
    {
        var hex = Regex.Match(value, "^0x([0-9A-F]+)");
        if (hex.Success) return ulong.Parse(hex.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ulong.Parse(new string([.. value.TakeWhile(c => char.IsAsciiDigit(c) || c is ',' or ' ' or ' ' or ' ').Where(char.IsAsciiDigit)]), CultureInfo.InvariantCulture);
    }

    /// <summary>PE files on every platform: this assembly's own dependencies are PE files wherever the tests run.</summary>
    public static TheoryData<string> PeFiles()
    {
        var files = new TheoryData<string>
        {
            typeof(PeInspector).Assembly.Location,
            typeof(object).Assembly.Location, // ReadyToRun: native code beside the IL
            typeof(System.Text.Json.JsonSerializer).Assembly.Location,
        };
        if (OperatingSystem.IsWindows())
            foreach (var name in new[] { "kernel32.dll", "ntdll.dll", "notepad.exe", "user32.dll", Path.Combine("drivers", "ntfs.sys") })
                if (File.Exists(Path.Combine(Environment.SystemDirectory, name))) files.Add(Path.Combine(Environment.SystemDirectory, name));
        return files;
    }

    [Theory]
    [MemberData(nameof(PeFiles))]
    public void PE_headers_directories_and_sections_are_what_the_runtime_reads(string path)
    {
        var report = Inspect(path);
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var coff = pe.PEHeaders.CoffHeader;
        var optional = pe.PEHeaders.PEHeader!;
        Assert.Equal((ulong)coff.Machine, Number(Field(report, "File header", "Machine")));
        Assert.Equal((ulong)coff.NumberOfSections, Number(Field(report, "File header", "Number of sections")));
        Assert.Equal((uint)coff.TimeDateStamp, (uint)Number(Field(report, "File header", "Time date stamp")));
        Assert.Equal((ulong)coff.Characteristics, Number(Field(report, "File header", "Characteristics")));
        Assert.Equal((ulong)coff.SizeOfOptionalHeader, Number(Field(report, "File header", "Size of optional header")));
        Assert.Equal((ulong)optional.Magic, Number(Field(report, "Optional header", "Magic")));
        Assert.Equal($"{optional.MajorLinkerVersion}.{optional.MinorLinkerVersion}", Field(report, "Optional header", "Linker version"));
        Assert.Equal((ulong)optional.SizeOfCode, Number(Field(report, "Optional header", "Size of code")));
        Assert.Equal((ulong)optional.SizeOfInitializedData, Number(Field(report, "Optional header", "Size of initialized data")));
        Assert.Equal((ulong)optional.AddressOfEntryPoint, Number(Field(report, "Optional header", "Address of entry point")));
        Assert.Equal(optional.ImageBase, Number(Field(report, "Optional header", "Image base")));
        Assert.Equal((ulong)optional.SectionAlignment, Number(Field(report, "Optional header", "Section alignment")));
        Assert.Equal((ulong)optional.FileAlignment, Number(Field(report, "Optional header", "File alignment")));
        Assert.Equal((ulong)optional.SizeOfImage, Number(Field(report, "Optional header", "Size of image")));
        Assert.Equal((ulong)optional.SizeOfHeaders, Number(Field(report, "Optional header", "Size of headers")));
        Assert.Equal((ulong)optional.CheckSum, optional.CheckSum == 0 ? 0 : Number(Field(report, "Optional header", "Checksum")));
        Assert.Equal((ulong)optional.Subsystem, Number(Field(report, "Optional header", "Subsystem")));
        Assert.Equal((ulong)optional.DllCharacteristics, Number(Field(report, "Optional header", "DLL characteristics")));
        Assert.Equal(optional.SizeOfStackReserve, Number(Field(report, "Optional header", "Size of stack reserve")));
        Assert.Equal(optional.SizeOfHeapReserve, Number(Field(report, "Optional header", "Size of heap reserve")));

        // The data directories, in order, and the section table.
        var directories = new[]
        {
            optional.ExportTableDirectory, optional.ImportTableDirectory, optional.ResourceTableDirectory, optional.ExceptionTableDirectory,
            optional.CertificateTableDirectory, optional.BaseRelocationTableDirectory, optional.DebugTableDirectory, optional.CopyrightTableDirectory,
            optional.GlobalPointerTableDirectory, optional.ThreadLocalStorageTableDirectory, optional.LoadConfigTableDirectory,
            optional.BoundImportTableDirectory, optional.ImportAddressTableDirectory, optional.DelayImportTableDirectory, optional.CorHeaderTableDirectory,
        };
        var rows = Section(report, "Data directories").Table!.Rows;
        for (int i = 0; i < directories.Length; i++)
        {
            Assert.Equal((ulong)directories[i].RelativeVirtualAddress, Number(rows[i][1]));
            Assert.Equal((ulong)directories[i].Size, rows[i][2].Length == 0 ? 0 : Number(rows[i][2]));
        }
        var sections = Section(report, "Sections").Table!.Rows;
        Assert.Equal(pe.PEHeaders.SectionHeaders.Length, sections.Count);
        for (int i = 0; i < sections.Count; i++)
        {
            var header = pe.PEHeaders.SectionHeaders[i];
            Assert.Equal(header.Name.Length == 0 ? "(no name)" : header.Name, sections[i][0]);
            Assert.Equal((ulong)header.VirtualAddress, Number(sections[i][1]));
            Assert.Equal((ulong)header.VirtualSize, Number(sections[i][2]));
            Assert.Equal((ulong)header.PointerToRawData, Number(sections[i][3]));
            Assert.Equal((ulong)header.SizeOfRawData, Number(sections[i][4]));
            Assert.Equal((ulong)header.SectionCharacteristics, Number(sections[i][7]));
        }
        // A file this size is read whole: its checksum is compared, and it matches unless the header leaves it unset.
        if (optional.CheckSum != 0 && new FileInfo(path).Length < 128 << 20)
            Assert.EndsWith("(matches the file)", Field(report, "Optional header", "Checksum"));
        Assert.Empty(report.Warnings.Where(w => w.Contains("could not be read", StringComparison.Ordinal) || w.Contains("damaged", StringComparison.Ordinal)));

        // .NET assemblies: the metadata as the runtime reads it.
        if (pe.HasMetadata)
        {
            var md = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
            Assert.Equal(md.MetadataVersion, Field(report, ".NET", "Metadata version"));
            var name = md.GetString(md.GetAssemblyDefinition().Name);
            Assert.StartsWith($"{name}, Version={md.GetAssemblyDefinition().Version}", Field(report, ".NET", "Assembly"));
            // System.Private.CoreLib references nothing; the others list theirs.
            var references = Section(report, ".NET").Children.FirstOrDefault(c => c.Title.StartsWith("Referenced assemblies", StringComparison.Ordinal))?.Table!.Rows.Select(r => r[0]) ?? [];
            Assert.Equal(md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).Take(1000), references);
            Assert.Equal(md.ManifestResources.Count, Section(report, ".NET").Children.FirstOrDefault(c => c.Title.StartsWith("Resources", StringComparison.Ordinal))?.Table!.Rows.Count ?? 0);
        }
    }

    [Fact]
    public void Windows_version_information_and_signatures_are_what_Windows_reads()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' version and signature APIs exist only on Windows.");
        foreach (var name in new[] { "kernel32.dll", "notepad.exe", "user32.dll" })
        {
            string path = Path.Combine(Environment.SystemDirectory, name);
            var report = Inspect(path);
            // The file's own version resource, as Windows reads it when asked for the neutral one: by default Windows
            // (and FileVersionInfo) answers from the language's .mui file beside it, which can be of another build.
            var neutral = NeutralVersion(path);
            Assert.Equal(neutral.FileVersion, Field(report, "Version information", "File version"));
            foreach (var key in new[] { "CompanyName", "FileDescription", "OriginalFilename", "ProductVersion", "FileVersion" })
                Assert.Equal(neutral.Strings[key], Field(report, "Version information", key));
            // An embedded signature: the certificate that signed it, as Windows extracts it; catalog-signed files have none.
#pragma warning disable SYSLIB0057 // the test wants Windows' own reading of the file's signature
            System.Security.Cryptography.X509Certificates.X509Certificate? signer = null;
            try { signer = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path); }
            catch (System.Security.Cryptography.CryptographicException) { }
#pragma warning restore SYSLIB0057
            if (signer is null) Assert.StartsWith("No embedded signature", Field(report, "Signature", "Status"));
            else
            {
                using var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(signer);
                Assert.Equal(certificate.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.SimpleName, false), Field(report, "Signature", "Signed by"));
                Assert.Equal("yes: the file is as it was signed", Field(report, "Signature", "Signed hash matches the file"));
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("version.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileVersionInfoSizeExW(uint flags, string file, out uint handle);

    [System.Runtime.InteropServices.DllImport("version.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetFileVersionInfoExW(uint flags, string file, uint handle, uint length, byte[] data);

    [System.Runtime.InteropServices.DllImport("version.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool VerQueryValueW(byte[] block, string subBlock, out IntPtr buffer, out uint length);

    /// <summary>Windows' reading of a file's own (neutral) version resource: FILE_VER_GET_NEUTRAL.</summary>
    private static (string FileVersion, Dictionary<string, string> Strings) NeutralVersion(string path)
    {
        const uint Neutral = 0x2;
        uint size = GetFileVersionInfoSizeExW(Neutral, path, out _);
        var data = new byte[size];
        Assert.True(GetFileVersionInfoExW(Neutral, path, 0, size, data));
        var pinned = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            Assert.True(VerQueryValueW(data, "\\", out var info, out _));
            uint ms = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(info, 8), ls = (uint)System.Runtime.InteropServices.Marshal.ReadInt32(info, 12);
            Assert.True(VerQueryValueW(data, "\\VarFileInfo\\Translation", out var translation, out _));
            int language = (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(translation), codePage = (ushort)System.Runtime.InteropServices.Marshal.ReadInt16(translation, 2);
            var strings = new Dictionary<string, string>();
            foreach (var key in new[] { "CompanyName", "FileDescription", "OriginalFilename", "ProductVersion", "FileVersion" })
                if (VerQueryValueW(data, $"\\StringFileInfo\\{language:X4}{codePage:X4}\\{key}", out var text, out _))
                    strings[key] = System.Runtime.InteropServices.Marshal.PtrToStringUni(text)!.Trim();
            return ($"{ms >> 16}.{ms & 0xFFFF}.{ls >> 16}.{ls & 0xFFFF}", strings);
        }
        finally
        {
            pinned.Free();
        }
    }

    [Fact]
    public void Edits_after_linking_and_signing_are_noticed()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Needs a signed Windows system library.");
        var original = File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "kernel32.dll"));
        var report = Inspect(original);
        Assert.StartsWith("matches", Field(report, "Rich header", "Checksum"));
        Assert.StartsWith("Visual Studio", Field(report, "Header", "Built with"));

        // One byte of code changed: the signature's hash and the checksum no longer match the file.
        var patched = (byte[])original.Clone();
        using (var pe = new PEReader(new MemoryStream(original)))
        {
            var text = pe.PEHeaders.SectionHeaders.First(s => s.Name == ".text");
            patched[text.PointerToRawData + 100] ^= 0xFF;
        }
        var edited = Inspect(patched);
        Assert.Equal("no: the file changed after it was signed", Field(edited, "Signature", "Signed hash matches the file"));
        Assert.Contains("changed after linking", Field(edited, "Optional header", "Checksum"));

        // An edited Rich header: its checksum says so.
        var rich = (byte[])original.Clone();
        int at = original.AsSpan(0, 1024).IndexOf("Rich"u8);
        rich[at - 8] ^= 0x01;
        Assert.StartsWith("does not match", Field(Inspect(rich), "Rich header", "Checksum"));
    }

    [Fact]
    public void Damaged_signatures_Rich_headers_and_metadata_give_warnings_never_exceptions()
    {
        var rng = new Random(29);
        var seeds = new List<byte[]> { File.ReadAllBytes(typeof(PeInspector).Assembly.Location) };
        if (OperatingSystem.IsWindows()) seeds.Add(File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "kernel32.dll")));
        foreach (var seed in seeds)
        {
            // The regions the newer readers parse: the DOS stub and Rich header, the signature at the end, and the middle
            // (metadata, resources, debug directory).
            foreach (var (start, length) in new[] { (64, 400), (seed.Length - 20_000, 20_000), (seed.Length / 3, seed.Length / 3) })
            {
                for (int round = 0; round < 60; round++)
                {
                    var copy = (byte[])seed.Clone();
                    for (int flips = rng.Next(1, 40); flips > 0; flips--) copy[Math.Clamp(start + rng.Next(length), 0, copy.Length - 1)] = (byte)rng.Next(256);
                    Inspect(copy).ToText(); // must not throw
                }
            }
        }
    }

    // ---- Linux: readelf ------------------------------------------------------------------------------------------------

    private static string? Run(string tool, params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(tool, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) return null;
            return output.Result + error.Result;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [Fact]
    public void ELF_reports_agree_with_readelf()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("readelf and ELF system files are on Linux.");
        foreach (var path in new[] { "/bin/ls", "/usr/bin/env" }.Where(File.Exists))
        {
            string? readelf = Run("readelf", "-W", "--file-header", "--program-headers", "--section-headers", "--dynamic", "--notes", "--dyn-syms", path);
            if (readelf is null) Assert.Skip("readelf is not installed.");
            var report = Inspect(path);
            // Program headers: as many, of the same types.
            int programHeaders = int.Parse(Regex.Match(readelf, @"Number of program headers:\s+(\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
            Assert.Equal(programHeaders, Section(report, "Program headers").Table!.Rows.Count);
            // Sections: the same names in the same order.
            var names = Regex.Matches(readelf, @"^\s*\[\s*(\d+)\]\s+(\S+)", RegexOptions.Multiline).Select(m => m.Groups[2].Value).Where(n => n != "NULL").ToList();
            Assert.Equal(names, Section(report, "Sections").Table!.Rows.Select(r => r[1]));
            // Needed libraries, and the build ID.
            var needed = Regex.Matches(readelf, @"\(NEEDED\)\s+Shared library: \[([^\]]+)\]").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(needed, Section(report, "Dynamic linking").Fields.Where(f => f.Name == "Needs").Select(f => f.Value));
            var buildId = Regex.Match(readelf, @"Build ID: ([0-9a-f]+)");
            if (buildId.Success) Assert.Equal(buildId.Groups[1].Value, Field(report, "Header", "Build ID"));
            // Imports: every undefined global or weak dynamic symbol with a name.
            var undefined = Regex.Matches(readelf, @"^\s*\d+:\s+[0-9a-f]+\s+\d+\s+\w+\s+(GLOBAL|WEAK)\s+\w+\s+UND\s+(\S+)", RegexOptions.Multiline)
                .Select(m => m.Groups[2].Value.Split('@')[0]).ToList();
            Assert.Equal(undefined, Section(report, "Imported symbols").Table!.Rows.Select(r => r[0]));
        }
    }

    // ---- macOS: otool and codesign -------------------------------------------------------------------------------------

    [Fact]
    public void Mach_O_reports_agree_with_otool_and_codesign()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("otool, codesign, and Mach-O system files are on macOS.");
        const string path = "/bin/ls";
        string arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64e" : "x86_64";
        string? otool = Run("otool", "-arch", arch, "-L", path);
        string? codesign = Run("codesign", "-dv", path);
        if (otool is null || codesign is null) Assert.Skip("otool or codesign is not available.");
        var report = Inspect(path);
        // The report's first architecture's libraries, and otool's for the same architecture where the file has only one.
        var libraries = otool.Split('\n').Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0).Select(l => l[..l.IndexOf(" (", StringComparison.Ordinal)]).ToList();
        var firstArchitecture = report.Sections.First(s => s.Title.Contains("Libraries", StringComparison.Ordinal));
        string prefix = firstArchitecture.Title[..(firstArchitecture.Title.IndexOf("Libraries", StringComparison.Ordinal))];
        var reported = firstArchitecture.Table!.Rows.Select(r => r[3]).ToList();
        Assert.All(reported, library => Assert.Contains(library, libraries));
        // The signing identifier.
        var identifier = Regex.Match(codesign, @"Identifier=(\S+)").Groups[1].Value;
        Assert.Equal(identifier, report.Sections.First(s => s.Title == prefix + "Signature").Fields.First(f => f.Name == "Identifier").Value);
    }
}
