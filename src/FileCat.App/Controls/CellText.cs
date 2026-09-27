using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace FileCat.App.Controls;

/// <summary>
/// One laid-out, single-line cell. Simple-script text is a glyph run built straight from the font's character map
/// (no shaping, no text layout objects), which keeps scrolling through a huge listing from allocating a full
/// layout per cell; everything else is a trimmed <see cref="FormattedText"/>.
/// </summary>
internal sealed class CellText : IDisposable
{
    private GlyphRun? _run;
    private readonly FormattedText? _formatted;
    private readonly IBrush _brush;

    private CellText(GlyphRun? run, FormattedText? formatted, IBrush brush, double width, double height, double maxWidth)
    {
        _run = run;
        _formatted = formatted;
        _brush = brush;
        Width = width;
        Height = height;
        MaxWidth = maxWidth;
    }

    public double Width { get; }
    public double Height { get; }
    public double MaxWidth { get; }

    public void Draw(DrawingContext dc, Point origin)
    {
        if (_formatted is not null)
        {
            dc.DrawText(_formatted, origin);
            return;
        }
        if (_run is null) return;
        using (dc.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y))) dc.DrawGlyphRun(_brush, _run);
    }

    /// <summary>Releases the glyph run now instead of leaving it to the finalizer (which would promote it).</summary>
    public void Dispose() => Interlocked.Exchange(ref _run, null)?.Dispose();

    public static CellText Formatted(string s, IBrush brush, Typeface typeface, double emSize, double maxWidth)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, emSize, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        return new CellText(null, ft, brush, ft.Width, ft.Height, Math.Max(1, maxWidth));
    }

    internal static CellText FromRun(GlyphRun run, IBrush brush, double width, double height, double maxWidth) =>
        new(run, null, brush, width, height, maxWidth);
}

/// <summary>
/// Glyph lookup for one typeface and size. Only characters that never need shaping qualify: no surrogates, no
/// combining marks, no right-to-left or complex scripts, no controls, and a glyph present in the primary font.
/// Kerning is not applied (as in classic file managers); line metrics are taken from a formatted sample so fast
/// and fallback cells align.
/// </summary>
internal sealed class SimpleGlyphs
{
    private const int DirectRange = 0x0590;
    private readonly GlyphTypeface _face;
    private readonly double _emSize;
    private readonly double _scale;
    private readonly ushort[] _glyphs = new ushort[DirectRange];
    private readonly float[] _advances = new float[DirectRange];
    private readonly byte[] _state = new byte[DirectRange]; // 0 unknown, 1 usable, 2 unusable
    private readonly Dictionary<char, (ushort Glyph, float Advance)?> _other = [];
    private readonly ushort _ellipsisGlyph;
    private readonly double _ellipsisAdvance;

    private SimpleGlyphs(GlyphTypeface face, double emSize, double lineHeight, double baseline)
    {
        _face = face;
        _emSize = emSize;
        _scale = emSize / face.Metrics.DesignEmHeight;
        LineHeight = lineHeight;
        Baseline = baseline;
        if (TryGlyph('…', out var g, out var a))
        {
            _ellipsisGlyph = g;
            _ellipsisAdvance = a;
        }
    }

    public double LineHeight { get; }
    public double Baseline { get; }

    public static SimpleGlyphs? TryCreate(Typeface typeface, double emSize)
    {
        try
        {
            var face = typeface.GlyphTypeface;
            if (face.Metrics.DesignEmHeight <= 0) return null;
            var sample = new FormattedText("Ag", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, emSize, Brushes.Black);
            return new SimpleGlyphs(face, emSize, sample.Height, sample.Baseline);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A glyph-run cell, or null when the text needs shaping or font fallback.</summary>
    public CellText? TryLayout(string s, IBrush brush, double maxWidth)
    {
        if (s.Length == 0 || s.Length > 512) return null;
        var infos = new GlyphInfo[s.Length];
        double width = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (!TryGlyph(s[i], out var glyph, out var advance)) return null;
            infos[i] = new GlyphInfo(glyph, i, advance);
            width += advance;
        }
        maxWidth = Math.Max(1, maxWidth);
        ReadOnlyMemory<char> chars = s.AsMemory();
        if (width > maxWidth + 0.01)
        {
            // Character ellipsis, like the fallback's trimming: keep what fits in front of "…".
            if (_ellipsisGlyph == 0) return null;
            int keep = 0;
            double used = 0;
            while (keep < infos.Length && used + infos[keep].GlyphAdvance + _ellipsisAdvance <= maxWidth)
            {
                used += infos[keep].GlyphAdvance;
                keep++;
            }
            var trimmed = new GlyphInfo[keep + 1];
            Array.Copy(infos, trimmed, keep);
            trimmed[keep] = new GlyphInfo(_ellipsisGlyph, keep, _ellipsisAdvance);
            infos = trimmed;
            chars = (s[..keep] + "…").AsMemory();
            width = used + _ellipsisAdvance;
        }
        var run = new GlyphRun(_face, _emSize, chars, infos, new Point(0, Baseline));
        return CellText.FromRun(run, brush, width, LineHeight, maxWidth);
    }

    private bool TryGlyph(char c, out ushort glyph, out double advance)
    {
        if (c < DirectRange)
        {
            switch (_state[c])
            {
                case 1:
                    glyph = _glyphs[c];
                    advance = _advances[c];
                    return true;
                case 2:
                    glyph = 0;
                    advance = 0;
                    return false;
            }
            bool ok = Lookup(c, out glyph, out advance);
            _state[c] = ok ? (byte)1 : (byte)2;
            _glyphs[c] = glyph;
            _advances[c] = (float)advance;
            return ok;
        }
        if (!_other.TryGetValue(c, out var known))
        {
            known = Lookup(c, out var g, out var a) ? (g, (float)a) : null;
            _other[c] = known;
        }
        glyph = known?.Glyph ?? 0;
        advance = known?.Advance ?? 0;
        return known is not null;
    }

    private bool Lookup(char c, out ushort glyph, out double advance)
    {
        glyph = 0;
        advance = 0;
        if (!IsSimple(c)) return false;
        if (!_face.CharacterToGlyphMap.TryGetGlyph(c, out glyph) || glyph == 0) return false;
        if (!_face.TryGetHorizontalGlyphAdvance(glyph, out var units)) return false;
        advance = units * _scale;
        return true;
    }

    private static bool IsSimple(char c)
    {
        if (c < 0x20 || c is >= '\u007F' and < ' ') return false;
        if (char.IsSurrogate(c)) return false;
        switch (CharUnicodeInfo.GetUnicodeCategory(c))
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.SpacingCombiningMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.Format:
            case UnicodeCategory.Control:
                return false;
        }
        // Latin, Greek, Cyrillic, Armenian, and common punctuation/symbol blocks need no shaping.
        return c < 0x0590 || c is >= 'Ḁ' and <= '῿' || c is >= '‐' and <= '‧' || c is >= '‰' and <= '⁞'
            || c is >= '₠' and <= '⃏' || c is >= '℀' and <= '⇿';
    }
}
