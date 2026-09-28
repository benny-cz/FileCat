using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using FileCat.App.Services;
using SkiaSharp;

namespace FileCat.App.Controls;

/// <summary>
/// What a theme draws behind the window's content (panels are translucent over it): Matrix rain, psychedelic light,
/// or riveted steampunk iron with stained glass behind the menu and key bars. Drawn with Skia on the render thread;
/// the UI thread only asks for a new frame.
/// </summary>
public sealed class ThemeBackdrop : Control
{
    public ThemeBackdrop() => IsHitTestVisible = false;

    // Static events: subscribed while shown, so a closed window is not kept alive by them.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeManager.ThemeChanged += InvalidateVisual;
        ThemeAnimation.Frame += OnFrame;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeManager.ThemeChanged -= InvalidateVisual;
        ThemeAnimation.Frame -= OnFrame;
    }

    /// <summary>Where the menu bar ends and the key bar begins: the stained glass fills the space above and below.</summary>
    public (double Top, double Bottom) GlassBands { get; set; }

    private void OnFrame()
    {
        if (ThemeManager.Current.Effect is ThemeEffect.Matrix or ThemeEffect.Psychedelic) InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var effect = ThemeManager.Current.Effect;
        if (effect == ThemeEffect.None || Bounds.Width < 1 || Bounds.Height < 1) return;
        context.Custom(new Backdrop(new Rect(Bounds.Size), effect, ThemeAnimation.Seconds, GlassBands));
    }

    private sealed class Backdrop(Rect bounds, ThemeEffect effect, double seconds, (double Top, double Bottom) glass) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) =>
            other is Backdrop b && b.Bounds == bounds && b.Effect == effect && b.Glass == glass && (effect == ThemeEffect.Steampunk || b.Seconds == seconds);
        public void Dispose() { }

        private ThemeEffect Effect => effect;
        private double Seconds => seconds;
        private (double Top, double Bottom) Glass => glass;

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var rect = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
            canvas.Save();
            canvas.ClipRect(rect);
            switch (effect)
            {
                case ThemeEffect.Matrix:
                    EffectArt.Matrix(canvas, rect, seconds);
                    break;
                case ThemeEffect.Psychedelic:
                    EffectArt.Psychedelic(canvas, rect, seconds);
                    break;
                case ThemeEffect.Steampunk:
                    EffectArt.Steampunk(canvas, rect, (float)glass.Top, (float)glass.Bottom);
                    break;
            }
            canvas.Restore();
        }
    }
}

/// <summary>
/// Glitches over the content (psychedelic theme): for a fraction of a second now and then, slices of what is on screen
/// jump sideways with split colors, blocks of noise flash, and scanlines roll. Nothing is drawn between glitches.
/// </summary>
public sealed class ThemeGlitchOverlay : Control
{
    private bool _wasGlitching;

    public ThemeGlitchOverlay() => IsHitTestVisible = false;

    private void OnFrame()
    {
        bool glitching = ThemeAnimation.Glitch is not null;
        if (glitching || _wasGlitching) InvalidateVisual();
        _wasGlitching = glitching;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ThemeAnimation.Frame += OnFrame;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ThemeAnimation.Frame -= OnFrame;
    }

    public override void Render(DrawingContext context)
    {
        if (ThemeAnimation.Glitch is not { } glitch || Bounds.Width < 1 || Bounds.Height < 1) return;
        context.Custom(new Glitch(new Rect(Bounds.Size), glitch.Seed, (ThemeAnimation.Seconds - glitch.Start) / glitch.Length));
    }

    private sealed class Glitch(Rect bounds, int seed, double progress) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
            using var lease = feature.Lease();
            EffectArt.Glitch(lease.SkCanvas, lease.SkSurface, new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height), seed, progress);
        }
    }
}

/// <summary>The drawings of the themes' effects.</summary>
internal static class EffectArt
{
    // Half-width katakana, digits, and a few signs, as in the film; fonts without katakana use the rest.
    private static readonly string[] Katakana = "ｦｧｨｩｪｫｬｭｮｯｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜﾝ0123456789:.=*+-<>|".Select(c => c.ToString()).ToArray();
    private static readonly string[] Plain = "0123456789ABCDEFZ:.=*+-<>|$#".Select(c => c.ToString()).ToArray();
    private static readonly Lazy<(SKTypeface Typeface, string[] Glyphs)> RainFont = new(() =>
    {
        var typeface = SKFontManager.Default.MatchCharacter('ｱ');
        if (typeface is not null) return (typeface, Katakana);
        return (SKTypeface.FromFamilyName("monospace") ?? SKTypeface.Default, Plain);
    });

    private static uint Hash(int a, int b = 0, int c = 0)
    {
        uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        return h ^ (h >> 15);
    }

    public static void Matrix(SKCanvas canvas, SKRect r, double t)
    {
        const float cell = 16f;
        canvas.DrawColor(SKColors.Black);
        var (typeface, glyphs) = RainFont.Value;
        using var font = new SKFont(typeface, cell * 0.92f);
        using var paint = new SKPaint { IsAntialias = true };
        int columns = (int)(r.Width / cell) + 1, rows = (int)(r.Height / cell) + 2;
        for (int c = 0; c < columns; c++)
        {
            uint h = Hash(c, 17);
            float speed = 4.5f + (h % 1000) / 1000f * 10f;
            int trail = 8 + (int)((h >> 10) % 24);
            int period = rows + trail + 4 + (int)((h >> 20) % (uint)(rows + 1));
            int head = (int)((t * speed + (h % 997)) % period);
            float x = r.Left + c * cell;
            float glyphRate = 0.6f + ((h >> 5) % 5) * 0.5f;
            for (int k = 0; k < trail; k++)
            {
                int row = head - k;
                if (row < 0 || row >= rows) continue;
                string glyph = glyphs[Hash(c, row, (int)(t * glyphRate) + k / 7) % (uint)glyphs.Length];
                double fade = Math.Pow(1 - k / (double)trail, 1.7);
                paint.Color = k == 0 ? new SKColor(0xE6, 0xFF, 0xEA) : new SKColor(0x2B, 0xE0, 0x5F, (byte)(40 + 215 * fade));
                canvas.DrawText(glyph, x, r.Top + (row + 1) * cell, font, paint);
            }
        }
    }

    private static readonly SKColor[] Neon = [new(0xF7, 0x25, 0x85), new(0x72, 0x09, 0xB7), new(0x4C, 0xC9, 0xF0), new(0x06, 0xD6, 0xA0), new(0xFF, 0xD1, 0x66), new(0xFF, 0x9F, 0x1C)];

    public static void Psychedelic(SKCanvas canvas, SKRect r, double t)
    {
        canvas.DrawColor(new SKColor(0x17, 0x0D, 0x29));
        using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Screen };
        float span = Math.Max(r.Width, r.Height);
        for (int i = 0; i < Neon.Length; i++)
        {
            double phase = i * 1.713;
            float cx = r.MidX + (float)(Math.Sin(t * (0.023 + i * 0.0041) + phase) * r.Width * 0.45);
            float cy = r.MidY + (float)(Math.Cos(t * (0.019 + i * 0.0053) + phase * 1.31) * r.Height * 0.42);
            float radius = span * (float)(0.26 + 0.1 * Math.Sin(t * 0.07 + i * 2.1));
            // Each blob's color turns slowly around the wheel, a little out of step with the others.
            Neon[i].ToHsl(out float hue, out float s, out float l);
            var color = SKColor.FromHsl((hue + (float)(t * 4 + i * 11)) % 360, s, l);
            using var shader = SKShader.CreateRadialGradient(new SKPoint(cx, cy), radius, [color.WithAlpha(215), color.WithAlpha(90), color.WithAlpha(0)], [0, 0.5f, 1], SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawCircle(cx, cy, radius, paint);
        }
        paint.Shader = null;
        // A slow ripple of light bands.
        paint.BlendMode = SKBlendMode.Overlay;
        for (float y = r.Top; y < r.Bottom; y += 6)
        {
            float wave = (float)Math.Sin(y * 0.021 + t * 0.9) * 0.5f + 0.5f;
            paint.Color = new SKColor(255, 255, 255, (byte)(wave * 16));
            canvas.DrawRect(r.Left, y, r.Width, 3, paint);
        }
    }

    public static void Glitch(SKCanvas canvas, SKSurface? surface, SKRect r, int seed, double progress)
    {
        var random = new Random(seed + (int)(progress * 6));
        var device = canvas.TotalMatrix.MapRect(r);
        using var paint = new SKPaint();
        // Slices of what is on screen jump sideways, with red and cyan pulled apart.
        if (surface is not null && device.Width >= 1 && device.Height >= 1)
        {
            var area = SKRectI.Round(device);
            using var snapshot = surface.Snapshot(area);
            if (snapshot is not null)
            {
                canvas.Save();
                canvas.ResetMatrix();
                int slices = 3 + random.Next(6);
                for (int i = 0; i < slices; i++)
                {
                    float top = (float)(random.NextDouble() * area.Height);
                    float height = 3 + (float)(random.NextDouble() * Math.Min(48, area.Height / 6.0));
                    float shift = (float)((random.NextDouble() - 0.5) * 70);
                    var source = new SKRect(0, top, area.Width, Math.Min(area.Height, top + height));
                    var target = new SKRect(area.Left + shift, area.Top + top, area.Left + shift + area.Width, area.Top + source.Bottom);
                    canvas.DrawImage(snapshot, source, target);
                    paint.BlendMode = SKBlendMode.Screen;
                    using (var red = SKColorFilter.CreateColorMatrix([1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0.55f, 0]))
                    {
                        paint.ColorFilter = red;
                        canvas.DrawImage(snapshot, source, target with { Left = target.Left - 4, Right = target.Right - 4 }, paint);
                    }
                    using (var cyan = SKColorFilter.CreateColorMatrix([0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0.55f, 0]))
                    {
                        paint.ColorFilter = cyan;
                        canvas.DrawImage(snapshot, source, target with { Left = target.Left + 4, Right = target.Right + 4 }, paint);
                    }
                    paint.ColorFilter = null;
                    paint.BlendMode = SKBlendMode.SrcOver;
                }
                canvas.Restore();
            }
        }
        // Blocks of noise and rolling scanlines.
        for (int i = 0; i < 5 + random.Next(10); i++)
        {
            var color = Neon[random.Next(Neon.Length)];
            paint.Color = color.WithAlpha((byte)(60 + random.Next(120)));
            float w = 8 + (float)random.NextDouble() * 140, h = 2 + (float)random.NextDouble() * 14;
            canvas.DrawRect((float)random.NextDouble() * r.Width, (float)random.NextDouble() * r.Height, w, h, paint);
        }
        paint.Color = new SKColor(255, 255, 255, 18);
        float roll = (float)(progress * 3 % 1) * 3;
        for (float y = r.Top + roll; y < r.Bottom; y += 3) canvas.DrawRect(r.Left, y, r.Width, 1, paint);
    }

    // ---- Steampunk: drawn once per size -------------------------------------------------------------------

    private static (SKSize Size, float Top, float Bottom, SKPicture Picture)? _steampunk;

    public static void Steampunk(SKCanvas canvas, SKRect r, float glassTop, float glassBottom)
    {
        var cached = _steampunk;
        if (cached is not { } c || c.Size != r.Size || c.Top != glassTop || c.Bottom != glassBottom)
        {
            using var recorder = new SKPictureRecorder();
            var target = recorder.BeginRecording(r);
            DrawSteampunk(target, r, glassTop, glassBottom);
            cached = (r.Size, glassTop, glassBottom, recorder.EndRecording());
            _steampunk?.Picture.Dispose();
            _steampunk = cached;
        }
        canvas.DrawPicture(cached.Value.Picture);
    }

    private static void DrawSteampunk(SKCanvas canvas, SKRect r, float glassTop, float glassBottom)
    {
        var random = new Random(1887);
        using var paint = new SKPaint { IsAntialias = true };
        // Dark iron, lighter at the top.
        using (var iron = SKShader.CreateLinearGradient(new SKPoint(0, r.Top), new SKPoint(0, r.Bottom), [new SKColor(0x3A, 0x2C, 0x20), new SKColor(0x16, 0x10, 0x0B)], SKShaderTileMode.Clamp))
        {
            paint.Shader = iron;
            canvas.DrawRect(r, paint);
            paint.Shader = null;
        }
        // Brushed metal grain.
        for (float y = r.Top; y < r.Bottom; y += 2)
        {
            paint.Color = random.Next(2) == 0 ? new SKColor(255, 230, 190, (byte)random.Next(4, 14)) : new SKColor(0, 0, 0, (byte)random.Next(6, 22));
            canvas.DrawRect(r.Left, y, r.Width, 1, paint);
        }
        // Rust blooms.
        for (int i = 0; i < 70; i++)
        {
            float x = (float)random.NextDouble() * r.Width, y = (float)random.NextDouble() * r.Height;
            float radius = 10 + (float)random.NextDouble() * 70;
            var rust = new[] { new SKColor(0x8B, 0x45, 0x13), new SKColor(0xA0, 0x52, 0x2D), new SKColor(0x6B, 0x3A, 0x1F), new SKColor(0xB7, 0x41, 0x0E) }[random.Next(4)];
            using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, radius * 0.35f);
            paint.MaskFilter = blur;
            paint.Color = rust.WithAlpha((byte)random.Next(25, 75));
            canvas.DrawOval(x, y, radius, radius * (0.5f + (float)random.NextDouble() * 0.6f), paint);
        }
        paint.MaskFilter = null;
        // Two brass gears in the corners.
        Gear(canvas, new SKPoint(r.Left + 60, r.Bottom - 40), 170, 18, new SKColor(0xC8, 0x96, 0x3E, 60));
        Gear(canvas, new SKPoint(r.Right - 90, r.Top + 120), 120, 14, new SKColor(0xB8, 0x73, 0x33, 50));
        // Stained glass behind the menu bar and the key bar.
        if (glassTop > 2) StainedGlass(canvas, new SKRect(r.Left, r.Top, r.Right, glassTop), random);
        if (glassBottom > 0 && glassBottom < r.Bottom - 2) StainedGlass(canvas, new SKRect(r.Left, glassBottom, r.Right, r.Bottom), random);
        // Rivets along the edges.
        for (float x = r.Left + 14; x < r.Right; x += 34)
        {
            Rivet(canvas, x, glassTop + 5);
            Rivet(canvas, x, glassBottom - 5);
        }
    }

    private static void Gear(SKCanvas canvas, SKPoint center, float radius, int teeth, SKColor color)
    {
        using var path = new SKPath();
        for (int i = 0; i < teeth * 2; i++)
        {
            double a0 = i * Math.PI / teeth, a1 = (i + 1) * Math.PI / teeth;
            float rr = i % 2 == 0 ? radius : radius * 0.86f;
            if (i == 0) path.MoveTo(center.X + rr * (float)Math.Cos(a0), center.Y + rr * (float)Math.Sin(a0));
            path.LineTo(center.X + rr * (float)Math.Cos(a0 + 0.08), center.Y + rr * (float)Math.Sin(a0 + 0.08));
            path.LineTo(center.X + rr * (float)Math.Cos(a1 - 0.08), center.Y + rr * (float)Math.Sin(a1 - 0.08));
        }
        path.Close();
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 5, Color = color };
        canvas.DrawPath(path, paint);
        canvas.DrawCircle(center, radius * 0.62f, paint);
        canvas.DrawCircle(center, radius * 0.16f, paint);
        for (int s = 0; s < 6; s++)
        {
            double a = s * Math.PI / 3;
            canvas.DrawLine(center.X + radius * 0.16f * (float)Math.Cos(a), center.Y + radius * 0.16f * (float)Math.Sin(a),
                center.X + radius * 0.62f * (float)Math.Cos(a), center.Y + radius * 0.62f * (float)Math.Sin(a), paint);
        }
    }

    private static readonly SKColor[] Jewels =
    [
        new(0x9B, 0x11, 0x1E), new(0x0F, 0x52, 0xBA), new(0x04, 0x63, 0x07), new(0xE0, 0x9A, 0x10), new(0x7A, 0x3F, 0xA8),
        new(0xB8, 0x56, 0x12), new(0x14, 0x7A, 0x80), new(0xC2, 0x2A, 0x5A),
    ];

    /// <summary>Panes of colored glass in irregular cells, lit from behind, held by dark lead.</summary>
    private static void StainedGlass(SKCanvas canvas, SKRect band, Random random)
    {
        float cellW = 46, rowsH = Math.Max(10, band.Height / Math.Max(1, (int)(band.Height / 22)));
        int cols = (int)(band.Width / cellW) + 2, rows = (int)Math.Ceiling(band.Height / rowsH) + 1;
        var points = new SKPoint[cols + 1, rows + 1];
        for (int i = 0; i <= cols; i++)
            for (int j = 0; j <= rows; j++)
            {
                float jx = i == 0 || i == cols ? 0 : (float)(random.NextDouble() - 0.5) * cellW * 0.6f;
                float jy = j == 0 || j == rows ? 0 : (float)(random.NextDouble() - 0.5) * rowsH * 0.5f;
                points[i, j] = new SKPoint(band.Left + i * cellW + jx, band.Top + j * rowsH + jy);
            }
        using var fill = new SKPaint { IsAntialias = true };
        using var lead = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2.8f, Color = new SKColor(0x10, 0x0C, 0x08, 240) };
        canvas.Save();
        canvas.ClipRect(band);
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                using var cell = new SKPath();
                cell.MoveTo(points[i, j]);
                cell.LineTo(points[i + 1, j]);
                cell.LineTo(points[i + 1, j + 1]);
                cell.LineTo(points[i, j + 1]);
                cell.Close();
                var jewel = Jewels[random.Next(Jewels.Length)];
                var deep = new SKColor((byte)(jewel.Red * 0.55), (byte)(jewel.Green * 0.55), (byte)(jewel.Blue * 0.55));
                var center = new SKPoint((points[i, j].X + points[i + 1, j + 1].X) / 2, (points[i, j].Y + points[i + 1, j + 1].Y) / 2);
                using var light = SKShader.CreateRadialGradient(center, cellW * 0.8f,
                    [new SKColor((byte)Math.Min(255, jewel.Red * 0.85 + 20), (byte)Math.Min(255, jewel.Green * 0.85 + 20), (byte)Math.Min(255, jewel.Blue * 0.85 + 20), 230), deep.WithAlpha(235)],
                    SKShaderTileMode.Clamp);
                fill.Shader = light;
                canvas.DrawPath(cell, fill);
                canvas.DrawPath(cell, lead);
            }
        canvas.Restore();
        using var edge = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = new SKColor(0xC8, 0x96, 0x3E, 200) };
        canvas.DrawLine(band.Left, band.Top == 0 ? band.Bottom - 1.5f : band.Top + 1.5f, band.Right, band.Top == 0 ? band.Bottom - 1.5f : band.Top + 1.5f, edge);
    }

    private static void Rivet(SKCanvas canvas, float x, float y)
    {
        using var shader = SKShader.CreateRadialGradient(new SKPoint(x - 1, y - 1), 3.5f, [new SKColor(0xF0, 0xD0, 0x90), new SKColor(0x6A, 0x4A, 0x22)], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = true, Shader = shader };
        canvas.DrawCircle(x, y, 3, paint);
    }
}
