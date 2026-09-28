using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>
/// Image inspector (plan §16.1): format, dimensions, depth, animation, color profile, resolution, and EXIF orientation,
/// camera, and date, read from bounded headers without decoding pixels.
/// </summary>
public static class ImageInspector
{
    private const int HeadBytes = 256 * 1024;

    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct)
    {
        var r = new ContentReader(source);
        var b = r.Read(0, HeadBytes);
        string? format = Format(b);
        if (format is null) return null;
        var warnings = new List<string>();
        var image = new List<(string, string)> { ("Format", format) };
        List<(string, string)>? tags = null;
        (int Width, int Height)? tiffSize = null;
        if (format == "TIFF") tags = Exif(b, 0, b.Length, out tiffSize);
        if ((ImageHeader.Parse(b, ct) ?? tiffSize) is { } size)
        {
            image.Add(("Dimensions", $"{size.Width:N0} × {size.Height:N0} pixels"));
            image.Add(("Pixels", $"{(double)size.Width * size.Height / 1_000_000:0.##} megapixels"));
        }
        else warnings.Add("The dimensions could not be read from the header.");
        var sections = new List<InspectionSection> { new("Image", image) };
        if (tags is { Count: > 0 }) sections.Add(new InspectionSection("Tags", tags));
        switch (format)
        {
            case "PNG": Png(b, image, sections, ct); break;
            case "GIF": Gif(b, image, ct); break;
            case "JPEG": Jpeg(b, image, sections, warnings, ct); break;
            case "BMP":
                if (b.Length >= 32) image.Add(("Bits per pixel", U16L(b, 28).ToString()));
                break;
            case "WebP": WebP(b, image); break;
        }
        return new InspectionReport($"{format} image", sections, warnings);
    }

    private static string? Format(byte[] b)
    {
        if (b.Length >= 8 && b.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "PNG";
        if (b.Length >= 6 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F') return "GIF";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "JPEG";
        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M') return "BMP";
        if (b.Length >= 12 && b.AsSpan(0, 4).SequenceEqual("RIFF"u8) && b.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return "WebP";
        if (b.Length >= 6 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0) return "ICO";
        if (b.Length >= 4 && (b[0] == 'I' && b[1] == 'I' && b[2] == 42 && b[3] == 0 || b[0] == 'M' && b[1] == 'M' && b[2] == 0 && b[3] == 42)) return "TIFF";
        return null;
    }

    private static void Png(byte[] b, List<(string, string)> image, List<InspectionSection> sections, CancellationToken ct)
    {
        if (b.Length >= 29)
        {
            image.Add(("Bit depth", b[24].ToString()));
            image.Add(("Color", b[25] switch { 0 => "grayscale", 2 => "RGB", 3 => "palette", 4 => "grayscale with alpha", 6 => "RGB with alpha", _ => $"type {b[25]}" }));
            image.Add(("Interlaced", b[28] == 1 ? "yes (Adam7)" : "no"));
        }
        var text = new List<(string, string)>();
        int at = 8;
        for (int chunks = 0; at + 8 <= b.Length && chunks < 1000; chunks++)
        {
            ct.ThrowIfCancellationRequested();
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at)), int.MaxValue);
            string type = Encoding.ASCII.GetString(b, at + 4, 4);
            int data = at + 8;
            switch (type)
            {
                case "acTL" when data + 8 <= b.Length:
                    image.Add(("Animation", $"{BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(data)):N0} frames (APNG)"));
                    break;
                case "iCCP" when data < b.Length:
                    image.Add(("Color profile", "embedded ICC profile \"" + Z(b, data, 80) + "\""));
                    break;
                case "sRGB":
                    image.Add(("Color space", "sRGB"));
                    break;
                case "pHYs" when data + 9 <= b.Length && b[data + 8] == 1:
                    image.Add(("Resolution", $"{BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(data)) * 0.0254:0} × {BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(data + 4)) * 0.0254:0} dpi"));
                    break;
                case "tEXt" when data < b.Length && text.Count < 10:
                    string key = Z(b, data, 80);
                    int valueAt = data + key.Length + 1;
                    int valueLength = Math.Min(length - key.Length - 1, 200);
                    if (valueLength > 0 && valueAt + valueLength <= b.Length) text.Add((key, Printable(Encoding.Latin1.GetString(b, valueAt, valueLength))));
                    break;
                case "IEND":
                    at = b.Length;
                    continue;
            }
            if (length > b.Length) break;
            at = data + length + 4;
        }
        if (text.Count > 0) sections.Add(new InspectionSection("Text", text));
    }

    private static void Gif(byte[] b, List<(string, string)> image, CancellationToken ct)
    {
        image.Add(("Version", Encoding.ASCII.GetString(b, 3, 3)));
        if (b.Length < 13) return;
        int at = 13;
        if ((b[10] & 0x80) != 0) at += 3 << ((b[10] & 7) + 1);
        int frames = 0;
        bool loops = false, cutOff = false;
        while (at < b.Length && frames < 100_000)
        {
            ct.ThrowIfCancellationRequested();
            byte block = b[at];
            if (block == 0x3B) break;
            if (block == 0x21 && at + 2 < b.Length)
            {
                if (b[at + 1] == 0xFF && at + 14 <= b.Length && Encoding.ASCII.GetString(b, at + 3, 11) is "NETSCAPE2.0" or "ANIMEXTS1.0") loops = true;
                at = SkipSubBlocks(b, at + 2);
            }
            else if (block == 0x2C && at + 10 <= b.Length)
            {
                frames++;
                int next = at + 10;
                if ((b[at + 9] & 0x80) != 0) next += 3 << ((b[at + 9] & 7) + 1);
                at = SkipSubBlocks(b, next + 1);
            }
            else break;
            if (at < 0)
            {
                cutOff = true;
                break;
            }
        }
        image.Add(("Frames", frames == 1 && !cutOff ? "1" : $"{frames:N0}{(cutOff ? "+ (counted in the first 256 KiB)" : "")}{(loops ? ", loops" : "")}"));
    }

    private static int SkipSubBlocks(byte[] b, int at)
    {
        while (at < b.Length)
        {
            int size = b[at];
            if (size == 0) return at + 1;
            at += size + 1;
        }
        return -1;
    }

    private static void Jpeg(byte[] b, List<(string, string)> image, List<InspectionSection> sections, List<string> warnings, CancellationToken ct)
    {
        int at = 2;
        while (at + 4 <= b.Length)
        {
            ct.ThrowIfCancellationRequested();
            if (b[at] != 0xFF)
            {
                at++;
                continue;
            }
            byte marker = b[at + 1];
            if (marker is 0xD8 or 0x01 or 0xFF || marker is >= 0xD0 and <= 0xD7)
            {
                at += marker == 0xFF ? 1 : 2;
                continue;
            }
            if (marker is 0xD9 or 0xDA) break;
            int length = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at + 2));
            int data = at + 4;
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC) && data + 6 <= b.Length)
            {
                image.Add(("Encoding", marker switch { 0xC0 => "baseline", 0xC1 => "extended sequential", 0xC2 => "progressive", 0xC3 => "lossless", _ => $"SOF{marker - 0xC0}" }));
                image.Add(("Bits per sample", b[data].ToString()));
                image.Add(("Components", b[data + 5] switch { 1 => "1 (grayscale)", 3 => "3 (color)", 4 => "4 (CMYK)", var n => n.ToString() }));
            }
            else if (marker == 0xE1 && data + 6 <= b.Length && b.AsSpan(data, 6).SequenceEqual("Exif\0\0"u8))
            {
                if (Exif(b, data + 6, Math.Min(b.Length, data + length - 2), out _) is { Count: > 0 } exif) sections.Add(new InspectionSection("EXIF", exif));
                else warnings.Add("The EXIF block could not be read.");
            }
            else if (marker == 0xE2 && data + 12 <= b.Length && b.AsSpan(data, 12).SequenceEqual("ICC_PROFILE\0"u8))
            {
                image.Add(("Color profile", "embedded ICC profile"));
            }
            at += 2 + length;
        }
    }

    /// <summary>IFD0 of a TIFF/EXIF block: orientation, camera, and date; for a TIFF file also its dimensions.</summary>
    internal static List<(string, string)> Exif(byte[] b, int tiff, int end, out (int Width, int Height)? size)
    {
        var fields = new List<(string, string)>();
        size = null;
        int width = 0, height = 0;
        if (tiff + 8 > end) return fields;
        bool little = b[tiff] == 'I';
        ushort U16(int at) => at + 2 <= end ? little ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(at)) : (ushort)0;
        uint U32(int at) => at + 4 <= end ? little ? BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(at)) : 0;
        long ifd = tiff + U32(tiff + 4);
        if (ifd + 2 > end) return fields;
        int count = Math.Min((int)U16((int)ifd), 256);
        for (int i = 0; i < count; i++)
        {
            int e = (int)ifd + 2 + i * 12;
            if (e + 12 > end) break;
            ushort tag = U16(e), type = U16(e + 2);
            uint n = U32(e + 4);
            string Ascii()
            {
                long at = n <= 4 ? e + 8 : (long)tiff + U32(e + 8);
                int len = (int)Math.Min(n, 128);
                return type == 2 && at >= 0 && at + len <= end ? Printable(Encoding.ASCII.GetString(b, (int)at, len).TrimEnd('\0', ' ')) : "";
            }
            int Number() => type == 3 ? U16(e + 8) : (int)Math.Min(U32(e + 8), int.MaxValue);
            switch (tag)
            {
                case 0x0100: width = Number(); break;
                case 0x0101: height = Number(); break;
                case 0x0112:
                    fields.Add(("Orientation", U16(e + 8) switch
                    {
                        1 => "normal",
                        2 => "mirrored horizontally",
                        3 => "rotated 180°",
                        4 => "mirrored vertically",
                        5 => "mirrored horizontally, rotated 270° clockwise",
                        6 => "rotated 90° clockwise",
                        7 => "mirrored horizontally, rotated 90° clockwise",
                        8 => "rotated 270° clockwise",
                        var o => $"value {o}",
                    }));
                    break;
                case 0x010F: fields.Add(("Camera maker", Ascii())); break;
                case 0x0110: fields.Add(("Camera model", Ascii())); break;
                case 0x0132: fields.Add(("Date", Ascii())); break;
                case 0x0131: fields.Add(("Software", Ascii())); break;
            }
        }
        if (width > 0 && height > 0) size = (width, height);
        return fields.Where(f => f.Item2.Length > 0).ToList();
    }

    private static void WebP(byte[] b, List<(string, string)> image)
    {
        if (b.Length < 16) return;
        string chunk = Encoding.ASCII.GetString(b, 12, 4);
        image.Add(("Encoding", chunk switch { "VP8 " => "lossy", "VP8L" => "lossless", "VP8X" => "extended", _ => chunk }));
        if (chunk == "VP8X" && b.Length >= 21)
        {
            byte flags = b[20];
            if ((flags & 0x02) != 0) image.Add(("Animation", "yes"));
            if ((flags & 0x10) != 0) image.Add(("Alpha", "yes"));
            if ((flags & 0x20) != 0) image.Add(("Color profile", "embedded ICC profile"));
            if ((flags & 0x08) != 0) image.Add(("EXIF", "present"));
        }
    }

    private static ushort U16L(byte[] b, int at) => at + 2 <= b.Length ? BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at)) : (ushort)0;

    private static string Z(byte[] b, int at, int max)
    {
        int end = Array.IndexOf(b, (byte)0, at, Math.Min(max, b.Length - at));
        return Printable(Encoding.Latin1.GetString(b, at, (end < 0 ? Math.Min(max, b.Length - at) : end - at)));
    }

    private static string Printable(string s) => new(s.Select(c => char.IsControl(c) ? '?' : c).ToArray());
}
