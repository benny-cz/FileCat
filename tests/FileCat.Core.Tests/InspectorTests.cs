using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Inspect;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class InspectorTests
{
    private static InspectionReport? Inspect(byte[] bytes) => Inspectors.Inspect(new MemoryContentSource("x", bytes), TestContext.Current.CancellationToken);

    private static Dictionary<string, string> Fields(InspectionReport report, string section) =>
        report.Sections.First(s => s.Title.StartsWith(section, StringComparison.Ordinal)).Fields.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First().Value);

    [Fact]
    public void A_dotnet_assembly_reads_as_a_managed_PE_with_version_information()
    {
        var bytes = File.ReadAllBytes(typeof(PeInspector).Assembly.Location);
        var report = Inspect(bytes)!;
        Assert.StartsWith("PE32", report.Format);
        Assert.Contains("DLL", report.Format);
        Assert.Equal("yes", Fields(report, ".NET")["IL only"]);
        Assert.Equal("FileCat.Core.dll", Fields(report, "Version information")["OriginalFilename"]);
        Assert.Equal("yes", Fields(report, "Security features")["Data execution prevention (NX)"]);
        Assert.StartsWith("No embedded signature", Fields(report, "Signature")["Status"]);
        Assert.Contains("Sections", report.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_native_system_library_shows_architecture_imports_exports_and_signature_state()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows system libraries exist only on Windows.");
        var report = Inspect(File.ReadAllBytes(Path.Combine(Environment.SystemDirectory, "kernel32.dll")))!;
        // The system's own architecture: ARM64 Windows keeps ARM64 libraries in System32.
        Assert.Contains(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "ARM64" : "x64", report.Format);
        Assert.Contains(report.Sections, s => s.Title.StartsWith("Imports", StringComparison.Ordinal) && s.Fields.Count > 0);
        Assert.Equal("KERNEL32.dll", Fields(report, "Exports")["Library name"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Microsoft Corporation", Fields(report, "Version information")["CompanyName"]);
        Assert.Equal("yes", Fields(report, "Security features")["Address space randomization (ASLR)"]);
    }

    [Fact]
    public void Damaged_executables_and_images_give_warnings_never_exceptions()
    {
        var pe = File.ReadAllBytes(typeof(PeInspector).Assembly.Location)[..(64 * 1024)];
        var rng = new Random(11);
        foreach (var seed in new[] { pe, Png(), Jpeg(), Gif() })
        {
            for (int round = 0; round < 300; round++)
            {
                var copy = seed[..rng.Next(1, seed.Length + 1)];
                for (int flips = rng.Next(0, 20); flips > 0; flips--) copy[rng.Next(copy.Length)] = (byte)rng.Next(256);
                Inspect(copy); // must not throw
            }
        }
        var truncated = Inspect(pe[..80]);
        Assert.NotNull(truncated);
        Assert.NotEmpty(truncated!.Warnings);
        Assert.Null(Inspect("plain text"u8.ToArray()));
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var c = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(c, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(c, 4);
        data.CopyTo(c, 8);
        return c;
    }

    internal static byte[] Png()
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, 640);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), 480);
        ihdr[8] = 8;
        ihdr[9] = 6;
        var actl = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(actl, 12);
        var phys = new byte[9];
        BinaryPrimitives.WriteUInt32BigEndian(phys, 11811);
        BinaryPrimitives.WriteUInt32BigEndian(phys.AsSpan(4), 11811);
        phys[8] = 1;
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Chunk("IHDR", ihdr)).Concat(Chunk("acTL", actl))
            .Concat(Chunk("pHYs", phys)).Concat(Chunk("tEXt", "Title\0Sunset"u8.ToArray())).Concat(Chunk("IEND", [])).ToArray();
    }

    internal static byte[] Jpeg()
    {
        // EXIF (little-endian TIFF): orientation 6, camera maker "Acme".
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(BitConverter.GetBytes((ushort)42));
        tiff.AddRange(BitConverter.GetBytes(8u));
        tiff.AddRange(BitConverter.GetBytes((ushort)2));
        tiff.AddRange(BitConverter.GetBytes((ushort)0x0112)); tiff.AddRange(BitConverter.GetBytes((ushort)3)); tiff.AddRange(BitConverter.GetBytes(1u)); tiff.AddRange(BitConverter.GetBytes(6u));
        tiff.AddRange(BitConverter.GetBytes((ushort)0x010F)); tiff.AddRange(BitConverter.GetBytes((ushort)2)); tiff.AddRange(BitConverter.GetBytes(4u)); tiff.AddRange("Acme"u8.ToArray());
        tiff.AddRange(BitConverter.GetBytes(0u));
        var app1 = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var sof = new byte[] { 8, 0x01, 0xE0, 0x02, 0x80, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1 };
        return new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, (byte)((app1.Length + 2) >> 8), (byte)(app1.Length + 2) }.Concat(app1)
            .Concat(new byte[] { 0xFF, 0xC2, 0, (byte)(sof.Length + 2) }).Concat(sof).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    internal static byte[] Gif()
    {
        var frame = new byte[] { 0x2C, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 1, 0, 0 };
        var loop = new byte[] { 0x21, 0xFF, 11 }.Concat("NETSCAPE2.0"u8.ToArray()).Concat(new byte[] { 3, 1, 0, 0, 0 }).ToArray();
        return "GIF89a"u8.ToArray().Concat(new byte[] { 1, 0, 1, 0, 0, 0, 0 }).Concat(loop).Concat(frame).Concat(frame).Concat(new byte[] { 0x3B }).ToArray();
    }

    [Fact]
    public void A_TIFF_file_reads_its_dimensions_and_tags_and_ignores_offsets_past_the_end()
    {
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(BitConverter.GetBytes((ushort)42));
        tiff.AddRange(BitConverter.GetBytes(8u));
        tiff.AddRange(BitConverter.GetBytes((ushort)4));
        void Entry(ushort tag, ushort type, uint count, uint value)
        {
            tiff.AddRange(BitConverter.GetBytes(tag));
            tiff.AddRange(BitConverter.GetBytes(type));
            tiff.AddRange(BitConverter.GetBytes(count));
            tiff.AddRange(BitConverter.GetBytes(value));
        }
        Entry(0x0100, 3, 1, 1024);
        Entry(0x0101, 4, 1, 768);
        Entry(0x010F, 2, 16, 0xFFFFFFF0); // camera maker stored far past the end of the file
        Entry(0x0110, 2, 4, BitConverter.ToUInt32("X10\0"u8));
        tiff.AddRange(BitConverter.GetBytes(0u));

        var report = Inspect(tiff.ToArray())!;
        Assert.Equal($"{1024:N0} × 768 pixels", Fields(report, "Image")["Dimensions"]); // in the reader's number format
        Assert.Equal("X10", Fields(report, "Tags")["Camera model"]);
        Assert.DoesNotContain(report.Sections.SelectMany(s => s.Fields), f => f.Name == "Camera maker");
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void Image_headers_show_dimensions_depth_animation_exif_and_frames()
    {
        var png = Inspect(Png())!;
        var p = Fields(png, "Image");
        Assert.Equal(("640 × 480 pixels", "RGB with alpha", "12 frames (APNG)", "300 × 300 dpi"), (p["Dimensions"], p["Color"], p["Animation"], p["Resolution"]));
        Assert.Equal("Sunset", Fields(png, "Text")["Title"]);

        var jpeg = Inspect(Jpeg())!;
        Assert.Equal(("progressive", "3 (color)"), (Fields(jpeg, "Image")["Encoding"], Fields(jpeg, "Image")["Components"]));
        Assert.Equal(("rotated 90° clockwise", "Acme"), (Fields(jpeg, "EXIF")["Orientation"], Fields(jpeg, "EXIF")["Camera maker"]));

        var gif = Inspect(Gif())!;
        Assert.Equal(("89a", "2, loops"), (Fields(gif, "Image")["Version"], Fields(gif, "Image")["Frames"]));
    }
}
