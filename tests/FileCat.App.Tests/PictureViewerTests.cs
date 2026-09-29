using System.Buffers.Binary;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Resources;
using FileCat.Core.State;
using SkiaSharp;

namespace FileCat.App.Tests;

/// <summary>
/// F3 on a picture (plan §16.1, VIEW-004): the picture, decoded by FileCat's worker process (sandboxed on Windows),
/// upright, scaled to fit; refusals say why.
/// </summary>
public sealed class PictureViewerTests
{
    /// <summary>A picture with its left half red and its right half blue.</summary>
    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var red = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, width / 2f, height, red);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 95);
        return data.ToArray();
    }

    /// <summary>A JPEG that says it is stored turned (EXIF orientation 6: turn it clockwise to show it).</summary>
    private static byte[] WithOrientation(byte[] jpeg, ushort orientation)
    {
        byte[] app1 =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    private static DecodedPicture DecodeHere(byte[] bytes, int maxSide)
    {
        using var output = new MemoryStream();
        try { PictureWorker.Decode(bytes, maxSide, output); }
        catch (InvalidDataException ex)
        {
            output.SetLength(0);
            var text = System.Text.Encoding.UTF8.GetBytes(ex.Message);
            output.Write(PictureWorker.FailureMagic);
            output.Write(BitConverter.GetBytes(text.Length));
            output.Write(text);
        }
        output.Position = 0;
        return PictureDecoder.Read(output, maxSide);
    }

    /// <summary>
    /// A pixel of the worker's answer itself (the headless test renderer does not keep a bitmap's pixels between locks):
    /// premultiplied BGRA rows after the header and the format's name.
    /// </summary>
    private static SKColor PixelAt(byte[] bytes, int maxSide, int x, int y)
    {
        using var output = new MemoryStream();
        PictureWorker.Decode(bytes, maxSide, output);
        var answer = output.ToArray();
        int width = BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(4));
        int formatLength = BinaryPrimitives.ReadInt32LittleEndian(answer.AsSpan(28));
        int at = 32 + formatLength + (y * width + x) * 4;
        return new SKColor(answer[at + 2], answer[at + 1], answer[at], answer[at + 3]);
    }

    [AvaloniaFact]
    public void Pictures_are_decoded_upright_and_scaled_to_fit()
    {
        var png = Encode(400, 200, SKEncodedImageFormat.Png);
        var picture = DecodeHere(png, 100);
        Assert.Equal(("PNG", 400, 200, 1, false), (picture.Format, picture.Width, picture.Height, picture.Frames, picture.Incomplete));
        Assert.Equal((100, 50), (picture.Bitmap.PixelSize.Width, picture.Bitmap.PixelSize.Height));
        Assert.Equal(SKColors.Red, PixelAt(png, 100, 10, 25));
        Assert.Equal(SKColors.Blue, PixelAt(png, 100, 90, 25));

        // Stored 40 × 20, left red; EXIF orientation 6 turns it clockwise: shown 20 × 40, red on top.
        var jpeg = WithOrientation(Encode(40, 20, SKEncodedImageFormat.Jpeg), 6);
        var turned = DecodeHere(jpeg, 1000);
        Assert.Equal(("JPEG", 20, 40), (turned.Format, turned.Width, turned.Height));
        var top = PixelAt(jpeg, 1000, 10, 5);
        var bottom = PixelAt(jpeg, 1000, 10, 35);
        Assert.True(top.Red > 200 && top.Blue < 60, top.ToString());
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, bottom.ToString());
    }

    [AvaloniaFact]
    public void A_picture_with_its_own_color_profile_keeps_its_colors()
    {
        // Display P3 values are not sRGB values: shown without the conversion, the orange below would look different.
        using var p3 = SKColorSpace.CreateRgb(SKColorSpaceTransferFn.Srgb, SKColorSpaceXyz.DisplayP3);
        using var bitmap = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul, p3));
        // The stored values themselves (Erase would convert an sRGB color into P3 first).
        var raw = bitmap.GetPixelSpan();
        for (int i = 0; i < raw.Length; i += 4) { raw[i] = 200; raw[i + 1] = 100; raw[i + 2] = 50; raw[i + 3] = 255; }
        using var pixels = bitmap.PeekPixels();
        using var encoded = pixels.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, 6));
        var png = encoded!.ToArray();
        Assert.Contains("iCCP", System.Text.Encoding.ASCII.GetString(png), StringComparison.Ordinal); // the profile travels with the picture
        var shown = PixelAt(png, 100, 4, 4);
        int difference = Math.Abs(shown.Red - 200) + Math.Abs(shown.Green - 100) + Math.Abs(shown.Blue - 50);
        Assert.True(difference > 10, $"{shown} is the file's own values: the profile was ignored.");
    }

    [AvaloniaFact]
    public void What_cannot_be_shown_says_why()
    {
        var noise = new byte[5000];
        new Random(1).NextBytes(noise);
        Assert.Contains("does not show this kind of picture", Assert.Throws<InvalidDataException>(() => DecodeHere(noise, 100)).Message, StringComparison.Ordinal);

        // A PNG header claiming 20,000 × 20,000 pixels is refused before anything is decoded.
        var png = Encode(8, 8, SKEncodedImageFormat.Png);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(16), 20_000);
        BinaryPrimitives.WriteInt32BigEndian(png.AsSpan(20), 20_000);
        BinaryPrimitives.WriteUInt32BigEndian(png.AsSpan(29), FileCat.Core.Jobs.Crc32.HashToUInt32(png.AsSpan(12, 17)));
        Assert.Contains("larger than FileCat shows", Assert.Throws<InvalidDataException>(() => DecodeHere(png, 100)).Message, StringComparison.Ordinal);

        Assert.Equal("PNG", PictureDecoder.Recognize(Encode(4, 4, SKEncodedImageFormat.Png)));
        Assert.Equal("JPEG", PictureDecoder.Recognize(Encode(4, 4, SKEncodedImageFormat.Jpeg)));
        Assert.Null(PictureDecoder.Recognize(noise));
    }

    [AvaloniaFact]
    public async Task The_worker_process_decodes_and_refuses_on_its_own()
    {
        var ct = TestContext.Current.CancellationToken;
        var picture = await PictureDecoder.DecodeAsync(new MemoryContentSource("photo.png", Encode(300, 150, SKEncodedImageFormat.Png)), 120, ct);
        Assert.Equal((300, 150, 120, 60), (picture.Width, picture.Height, picture.Bitmap.PixelSize.Width, picture.Bitmap.PixelSize.Height));

        var noise = new byte[70_000];
        new Random(2).NextBytes(noise);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => PictureDecoder.DecodeAsync(new MemoryContentSource("x.png", noise), 120, ct));
        Assert.Contains("picture", refused.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task F3_on_a_picture_shows_the_picture_and_F4_its_bytes()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-app-tests", Guid.NewGuid().ToString("N"));
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
        var viewer = new ViewerWindow(services, new MemoryContentSource("photo.png", Encode(640, 400, SKEncodedImageFormat.Png)), "photo.png", hex: false);
        viewer.Show();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            for (int i = 0; i < 250 && viewer.PictureLoad is null; i++) await Task.Delay(20, ct);
            Assert.NotNull(viewer.PictureLoad);
            await viewer.PictureLoad;
            Assert.Equal((640, 400), (viewer.Picture!.Width, viewer.Picture.Height));

            // The wheel zooms around the pointer: the point of the picture under it stays under it (away from the edges,
            // where the picture is kept from leaving empty space).
            var view = viewer.GetVisualDescendants().OfType<FileCat.App.Controls.PictureView>().Single(v => v.IsEffectivelyVisible);
            await Task.Delay(50, ct);
            Assert.True(view.Fit);
            double fitted = view.Zoom;
            var at = new Avalonia.Point(view.Bounds.Width / 2 + 20, view.Bounds.Height / 2 - 10);
            var before = view.Offset;
            double pictureX = (at.X - before.X) / fitted;
            view.ZoomBy(3, at);
            Assert.False(view.Fit);
            Assert.Equal(fitted * Math.Pow(1.25, 3), view.Zoom, 3);
            Assert.Equal(pictureX, (at.X - view.Offset.X) / view.Zoom, 1);
            // Dragging moves the enlarged picture; it never leaves empty space at its edges.
            var start = Avalonia.VisualExtensions.TranslatePoint(view, at, viewer)!.Value;
            viewer.MouseDown(start, Avalonia.Input.MouseButton.Left);
            viewer.MouseMove(new Avalonia.Point(start.X + 40, start.Y + 30));
            viewer.MouseUp(new Avalonia.Point(start.X + 40, start.Y + 30), Avalonia.Input.MouseButton.Left);
            Assert.NotEqual(before, view.Offset);
            Assert.True(view.Offset.X <= 0 && view.Offset.Y <= 0);
            // 0 fits again.
            viewer.KeyPressQwerty(Avalonia.Input.PhysicalKey.Digit0, Avalonia.Input.RawInputModifiers.None);
            Assert.True(view.Fit);
            Assert.Equal(fitted, view.Zoom, 3);
            viewer.KeyPressQwerty(Avalonia.Input.PhysicalKey.F4, Avalonia.Input.RawInputModifiers.None);
            Assert.Null(viewer.GetVisualDescendants().OfType<FileCat.App.Controls.PictureView>().FirstOrDefault(v => v.IsEffectivelyVisible));
        }
        finally
        {
            viewer.Close();
            services.Dispose();
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
