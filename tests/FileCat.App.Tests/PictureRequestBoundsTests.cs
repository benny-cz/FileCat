using System.Buffers.Binary;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class PictureRequestBoundsTests(ITestOutputHelper output)
{
    [AvaloniaTheory]
    [InlineData(-1, 16, 12)]
    [InlineData(0, 16, 12)]
    [InlineData(1, 16, 12)]
    [InlineData(15, 16, 12)]
    [InlineData(16, 16, 12)]
    [InlineData(8192, 32, 24)]
    [InlineData(8193, 32, 24)]
    [InlineData(int.MaxValue, 32, 24)]
    public async Task Public_decode_keeps_worker_request_bounds(int requested, int width, int height)
    {
        var bytes = SolidBitmap();
        using var source = new MemoryContentSource("owned.bmp", bytes);
        output.WriteLine(JsonSerializer.Serialize(new { requested, InputBase64 = Convert.ToBase64String(bytes) }));
        DecodedPicture picture;
        try { picture = await PictureDecoder.DecodeAsync(source, requested, TestContext.Current.CancellationToken); }
        catch (Exception ex)
        {
            output.WriteLine(JsonSerializer.Serialize(new { requested, Failure = ex.GetType().Name, ex.Message }));
            throw;
        }
        using var bitmap = picture.Bitmap;
        output.WriteLine(JsonSerializer.Serialize(new
        {
            requested, SourceWidth = picture.Width, SourceHeight = picture.Height,
            ActualWidth = bitmap.PixelSize.Width, ActualHeight = bitmap.PixelSize.Height,
        }));
        Assert.Equal((32, 24, width, height), (picture.Width, picture.Height, bitmap.PixelSize.Width, bitmap.PixelSize.Height));
    }

    [AvaloniaTheory]
    [InlineData(8192, 8193, 1)]
    [InlineData(8192, 1, 8193)]
    [InlineData(8193, 8193, 1)]
    [InlineData(8193, 1, 8193)]
    [InlineData(int.MaxValue, 8193, 1)]
    [InlineData(int.MaxValue, 1, 8193)]
    public void Parent_refuses_output_over_the_absolute_side_limit_before_pixel_reads(int requested, int width, int height)
    {
        var header = new byte[32];
        "FCPX"u8.CopyTo(header);
        int[] fields = [width, height, width, height, 1, 0, 0];
        for (int i = 0; i < fields.Length; i++)
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4 + i * 4), fields[i]);
        using var stream = new PixelReadGuard(header);
        Exception? failure = Record.Exception(() => PictureDecoder.Read(stream, requested));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            requested, width, height, HeaderBase64 = Convert.ToBase64String(header),
            stream.PixelReadAttempts, Failure = failure?.GetType().Name, failure?.Message,
        }));
        Assert.IsType<InvalidDataException>(failure);
        Assert.Equal(0, stream.PixelReadAttempts);
    }

    private static byte[] SolidBitmap()
    {
        const int width = 32, height = 24, offset = 54;
        var bytes = new byte[offset + width * height * 4];
        bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(10), offset);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(22), -height);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        for (int i = offset; i < bytes.Length; i += 4)
        {
            bytes[i] = 21; bytes[i + 1] = 43; bytes[i + 2] = 65; bytes[i + 3] = 255;
        }
        return bytes;
    }

    private sealed class PixelReadGuard(byte[] header) : MemoryStream(header, writable: false)
    {
        public int PixelReadAttempts { get; private set; }
        public override int Read(Span<byte> buffer)
        {
            if (buffer.Length > 0 && Position >= Length)
            {
                PixelReadAttempts++;
                throw new InvalidOperationException("The parent attempted to read pixels from an oversized answer.");
            }
            return base.Read(buffer);
        }
    }
}
