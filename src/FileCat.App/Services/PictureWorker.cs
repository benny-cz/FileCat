using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace FileCat.App.Services;

/// <summary>
/// The decoding side of the picture viewer (plan §16.1: "decoder worker; pixel/dimension/memory limits; bounded transfer
/// of decoded output"). FileCat started with <see cref="Argument"/> reads one picture's bytes from standard input and
/// writes it back decoded, turned the way its EXIF orientation says and scaled to fit a given size, or a reason. It never
/// opens a file or a window, so a damaged or hostile picture can crash or exhaust only this process, which on Windows
/// runs in the Shell helper's sandbox (<see cref="PictureDecoder"/>).
/// </summary>
internal static class PictureWorker
{
    public const string Argument = "--picture-worker";

    /// <summary>Pictures larger than this are not read at all.</summary>
    public const long MaxInputBytes = 256L * 1024 * 1024;

    /// <summary>Nor decoded beyond this many pixels (a 50-megapixel camera stays well inside).</summary>
    public const long MaxSourcePixels = 100_000_000;

    /// <summary>The longest side the decoded output may have.</summary>
    public const int MaxSide = 8192;

    internal static ReadOnlySpan<byte> SuccessMagic => "FCPX"u8;
    internal static ReadOnlySpan<byte> FailureMagic => "FCPE"u8;

    /// <summary>The worker's entry point: arguments <see cref="Argument"/> and the longest side wanted.</summary>
    public static int Run(string[] args)
    {
        using var output = Console.OpenStandardOutput();
        try
        {
            int maxSide = args.Length > 1 && int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out int wanted) ? Math.Clamp(wanted, 16, MaxSide) : 2048;
            using var input = Console.OpenStandardInput();
            var bytes = ReadAll(input);
            if (bytes is null) return Fail(output, $"it is larger than {MaxInputBytes / (1024 * 1024)} MiB, which is more than FileCat shows");
            Decode(bytes, maxSide, output);
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or OutOfMemoryException)
        {
            return Fail(output, ex.Message);
        }
    }

    private static byte[]? ReadAll(Stream input)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024 * 1024];
        int n;
        while ((n = input.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + n > MaxInputBytes) return null;
            buffer.Write(chunk, 0, n);
        }
        return buffer.ToArray();
    }

    private static int Fail(Stream output, string reason)
    {
        var text = Encoding.UTF8.GetBytes(reason.Length > 400 ? reason[..400] : reason);
        Span<byte> header = stackalloc byte[8];
        FailureMagic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], text.Length);
        output.Write(header);
        output.Write(text);
        output.Flush();
        return 2;
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> and writes: the magic, the output's width and height, the picture's own width and
    /// height (as shown), its frame count, flags (1: the data ended early), its format's name, then premultiplied BGRA
    /// rows without padding.
    /// </summary>
    internal static void Decode(byte[] bytes, int maxSide, Stream output)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes, writable: false), out var opened)
                          ?? throw new InvalidDataException(opened == SKCodecResult.Unimplemented ? "FileCat does not show this kind of picture" : "it is damaged or not a picture");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0) throw new InvalidDataException("it declares no size");
        if ((long)info.Width * info.Height > MaxSourcePixels)
            throw new InvalidDataException($"at {info.Width:N0} × {info.Height:N0} pixels it is larger than FileCat shows ({MaxSourcePixels / 1_000_000} megapixels)");
        var origin = codec.EncodedOrigin;
        bool turned = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int shownW = turned ? info.Height : info.Width, shownH = turned ? info.Width : info.Height;
        float scale = Math.Min(1f, (float)maxSide / Math.Max(shownW, shownH));
        // JPEG decodes at 1/2, 1/4, 1/8 directly; the rest is scaled when drawn.
        var decodedSize = codec.GetScaledDimensions(scale);
        var decodeInfo = new SKImageInfo(decodedSize.Width, decodedSize.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(decodeInfo);
        var result = codec.GetPixels(decodeInfo, decoded.GetPixels());
        bool incomplete = result is SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput;
        if (result != SKCodecResult.Success && !incomplete) throw new InvalidDataException($"it could not be decoded ({result})");

        int outW = Math.Clamp((int)Math.Round(shownW * scale), 1, maxSide), outH = Math.Clamp((int)Math.Round(shownH * scale), 1, maxSide);
        using var shown = new SKBitmap(new SKImageInfo(outW, outH, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(shown))
        {
            canvas.Clear(SKColors.Transparent);
            canvas.SetMatrix(Orientation(origin, outW, outH));
            using var image = SKImage.FromBitmap(decoded);
            canvas.DrawImage(image, new SKRect(0, 0, turned ? outH : outW, turned ? outW : outH), new SKSamplingOptions(SKCubicResampler.Mitchell));
        }

        var format = Encoding.ASCII.GetBytes(codec.EncodedFormat.ToString().ToUpperInvariant());
        Span<byte> header = stackalloc byte[32];
        SuccessMagic.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], outW);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], outH);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], shownW);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], shownH);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], Math.Max(1, codec.FrameCount));
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], incomplete ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], format.Length);
        output.Write(header);
        output.Write(format);
        var pixels = shown.GetPixelSpan();
        int rowBytes = shown.RowBytes, width = outW * 4;
        for (int y = 0; y < outH; y++) output.Write(pixels.Slice(y * rowBytes, width));
        output.Flush();
    }

    /// <summary>The drawing transform that shows a picture stored in <paramref name="origin"/>'s orientation upright.</summary>
    internal static SKMatrix Orientation(SKEncodedOrigin origin, int w, int h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
        _ => SKMatrix.Identity,
    };
}
