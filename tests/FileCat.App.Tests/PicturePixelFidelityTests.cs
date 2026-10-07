using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.App.Services;
using FileCat.Core.Jobs;
using SkiaSharp;

namespace FileCat.App.Tests;

/// <summary>Actual-size output keeps each decoded pixel; integer EXIF transforms do not resample it.</summary>
public sealed class PicturePixelFidelityTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    public void An_unscaled_patterned_BMP_keeps_every_pixel(int maxSide)
    {
        const int width = 32, height = 17;
        var pixels = Pattern(width, height, transparent: false);
        var bmp = Bitmap(width, height, pixels);
        Check(bmp, maxSide, width, height, "BMP", pixels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unscaled_lossless_PNG_keeps_opaque_and_premultiplied_pixels(bool transparent)
    {
        var pixels = Pattern(32, 17, transparent);
        Check(Png(32, 17, pixels, 1), 64, 32, 17, "PNG", pixels);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void Integer_EXIF_transforms_keep_each_decoded_JPEG_pixel(int orientation)
    {
        const int width = 32, height = 17;
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        Pattern(width, height, false).CopyTo(bitmap.GetPixelSpan());
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        var original = encoded.ToArray();
        byte[] app1 = [255, 225, 0, 34, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        byte[] jpeg = [.. original[..2], .. app1, .. original[2..]];
        using var codec = SKCodec.Create(new MemoryStream(jpeg));
        Assert.NotNull(codec);
        Assert.Equal((SKEncodedOrigin)orientation, codec.EncodedOrigin);
        // JPEG is lossy. Compare the integer transform against decoded, color-converted pixels before drawing.
        using var decoded = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(decoded.Info, decoded.GetPixels()));
        var pixels = decoded.GetPixelSpan().ToArray();
        bool turned = orientation >= 5;
        int w = turned ? height : width, h = turned ? width : height;
        var expected = new byte[w * h * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var (dx, dy) = orientation switch
                {
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (height - 1 - y, x),
                    7 => (height - 1 - y, width - 1 - x),
                    8 => (y, width - 1 - x),
                    _ => (x, y),
                };
                pixels.AsSpan((y * width + x) * 4, 4).CopyTo(expected.AsSpan((dy * w + dx) * 4, 4));
            }
        output.WriteLine(JsonSerializer.Serialize(new { DecodedJPEGBase64 = Convert.ToBase64String(pixels), orientation }));
        Check(jpeg, 64, w, h, "JPEG", expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_shrunken_checkerboard_still_blends_adjacent_pixels(bool png)
    {
        const int width = 32, height = 18;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = (byte)(((x + y) % 2) * 255);
                pixels[i + 3] = 255;
            }
        var input = png ? Png(width, height, pixels, 1) : Bitmap(width, height, pixels);
        var result = Answer(input, 16);
        Assert.Equal((16, 9, width, height, png ? "PNG" : "BMP", 1, 0),
            (result.Width, result.Height, result.SourceWidth, result.SourceHeight, result.Format, result.Frames, result.Flags));
        for (int y = 1; y < result.Height - 1; y++)
            for (int x = 1; x < result.Width - 1; x++)
            {
                int i = (y * result.Width + x) * 4;
                Assert.All(result.Pixels.AsSpan(i, 3).ToArray(), channel => Assert.InRange(channel, (byte)100, (byte)155));
                Assert.Equal(255, result.Pixels[i + 3]);
            }
        Observe(input, 16, result, expected: null);
    }

    private void Check(byte[] input, int maxSide, int w, int h, string format, byte[] expected)
    {
        var result = Answer(input, maxSide);
        Assert.Equal((w, h, w, h, format, 1, 0), (result.Width, result.Height, result.SourceWidth, result.SourceHeight, result.Format, result.Frames, result.Flags));
        Observe(input, maxSide, result, expected);
        Assert.Equal(expected, result.Pixels);
    }

    private void Observe(byte[] input, int maxSide, Decoded result, byte[]? expected) => output.WriteLine(JsonSerializer.Serialize(new
    {
        maxSide, result.Width, result.Height, result.SourceWidth, result.SourceHeight, result.Format, result.Frames, result.Flags,
        InputBase64 = Convert.ToBase64String(input), PixelsBase64 = Convert.ToBase64String(result.Pixels),
        ExpectedBase64 = expected is null ? null : Convert.ToBase64String(expected),
        InputSHA256 = Convert.ToHexString(SHA256.HashData(input)), PixelsSHA256 = Convert.ToHexString(SHA256.HashData(result.Pixels)),
        ChangedChannelBytes = expected is null ? (int?)null : expected.Where((v, i) => v != result.Pixels[i]).Count(),
    }));

    private sealed record Decoded(int Width, int Height, int SourceWidth, int SourceHeight, string Format, int Frames, int Flags, byte[] Pixels);

    private static Decoded Answer(byte[] input, int maxSide)
    {
        using var stream = new MemoryStream();
        PictureWorker.Decode(input, maxSide, stream);
        var raw = stream.ToArray();
        Assert.True(raw.AsSpan(0, 4).SequenceEqual("FCPX"u8));
        int w = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(4)), h = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(8));
        int sw = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(12)), sh = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(16));
        int frames = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(20)), flags = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(24));
        int n = BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(28));
        Assert.InRange(n, 1, 16);
        Assert.Equal(32 + n + w * h * 4, raw.Length);
        return new(w, h, sw, sh, System.Text.Encoding.ASCII.GetString(raw, 32, n), frames, flags, raw[(32 + n)..]);
    }

    private static byte[] Pattern(int width, int height, bool transparent)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (transparent)
                {
                    // Values exactly representable after straight-alpha PNG becomes premultiplied BGRA.
                    byte a = (byte)(((x + y) % 4) * 85);
                    pixels[i] = x % 2 == 0 ? a : (byte)0;
                    pixels[i + 1] = y % 2 == 0 ? a : (byte)0;
                    pixels[i + 2] = (x + y) % 2 == 0 ? a : (byte)0;
                    pixels[i + 3] = a;
                }
                else
                {
                    pixels[i] = (byte)((17 * x + 13 * y) % 256);
                    pixels[i + 1] = (byte)((7 * x + 3 * y) % 256);
                    pixels[i + 2] = (byte)((x ^ y) & 255);
                    pixels[i + 3] = 255;
                }
            }
        return pixels;
    }

    private static byte[] Bitmap(int width, int height, byte[] pixels)
    {
        int row = (width * 3 + 3) & ~3;
        var bmp = new byte[54 + row * height];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(34), row * height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                pixels.AsSpan((y * width + x) * 4, 3).CopyTo(bmp.AsSpan(54 + (height - 1 - y) * row + x * 3, 3));
        return bmp;
    }

    private static byte[] Png(int width, int height, byte[] pixels, int orientation)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = 6;
        Chunk("IHDR"u8, header);
        byte[] exif = [(byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        Chunk("eXIf"u8, exif);
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            for (int y = 0; y < height; y++)
            {
                z.WriteByte(0);
                for (int x = 0; x < width; x++)
                {
                    int i = (y * width + x) * 4;
                    byte a = pixels[i + 3];
                    for (int c = 2; c >= 0; c--) z.WriteByte(a == 0 ? (byte)0 : (byte)(pixels[i + c] * 255 / a));
                    z.WriteByte(a);
                }
            }
        Chunk("IDAT"u8, compressed.ToArray());
        Chunk("IEND"u8, []);
        return png.ToArray();

        void Chunk(ReadOnlySpan<byte> name, ReadOnlySpan<byte> body)
        {
            Span<byte> n = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(n, body.Length); png.Write(n); png.Write(name); png.Write(body);
            var crcInput = new byte[name.Length + body.Length]; name.CopyTo(crcInput); body.CopyTo(crcInput.AsSpan(name.Length));
            BinaryPrimitives.WriteUInt32BigEndian(n, Crc32.HashToUInt32(crcInput)); png.Write(n);
        }
    }
}
