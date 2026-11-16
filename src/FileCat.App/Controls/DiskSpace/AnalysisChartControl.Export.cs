using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.App.Controls.DiskSpace;

public abstract partial class AnalysisChartControl
{
    internal AnalysisChartControl? CaptureExportView()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_view is not { } view) return null;
        AnalysisChartControl copy = this is SunburstControl ? new SunburstControl() : new TreemapControl();
        copy.SetView(view.Tree, view.Scope, null, [], view.Color); return copy;
    }
    // Run the existing layout on a detached control. Export never changes the live
    // chart's hit targets, pending pointer interaction, or source accounting.
    internal AnalysisChartScene CaptureExportScene(AnalysisReportHeader header, int width, int height, bool redact, AnalysisComparisonChartContext? comparison = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        AnalysisChartScene.ValidateSize(width, height);
        if (_view is not { } view) throw new InvalidOperationException("Choose a captured chart first.");
        if (view.Tree is AnalysisComparisonChart recorded)
        {
            if (comparison is null || recorded.Previous != comparison.PreviousSide || recorded.Identity != (comparison.PreviousSide ? comparison.Previous : comparison.Current) ||
                comparison.Previous.ObservedUtc < comparison.Previous.StartedUtc || comparison.Current.ObservedUtc < comparison.Current.StartedUtc ||
                !Enum.IsDefined(comparison.PreviousCoverage) || !Enum.IsDefined(comparison.CurrentCoverage) || !Enum.IsDefined(comparison.PreviousFreshness) || !Enum.IsDefined(comparison.CurrentFreshness) ||
                comparison.Taint == 0 || (comparison.Taint & ~AnalysisPrivacy.All) != 0 || (comparison.Taint & header.Taint) != header.Taint)
                throw new InvalidOperationException("The comparison chart captions must describe both exact captured inputs and the selected side.");
            header = header with { Taint = comparison.Taint, Coverage = comparison.PreviousCoverage == NamespaceCoverage.CompleteUnderPolicy && comparison.CurrentCoverage == NamespaceCoverage.CompleteUnderPolicy
                ? NamespaceCoverage.CompleteUnderPolicy : NamespaceCoverage.Partial };
        }
        else if (comparison is not null) throw new InvalidOperationException("Comparison captions require a captured comparison chart.");
        if (view.Tree.Metric != header.Metric || view.Tree.ObservedUtc != header.ObservedUtc)
            throw new InvalidOperationException("The chart and captured report describe different observations.");
        AnalysisChartControl layout = this is SunburstControl ? new SunburstControl() : new TreemapControl();
        layout.SetView(view.Tree, view.Scope, null, [], view.Color);
        int extra = comparison is null ? 0 : 72;
        layout.Arrange(new Rect(0, 0, width - 280, height - 240 - extra - (comparison is null ? 0 : 24)));
        layout._labelLineHeight = 16; layout.SunburstHoleRatio = .32;
        UInt128 total = view.Tree.ScopeValue(view.Scope);
        if (total != AnalysisGroupedChart.Area(header.Totals, header.Metric)) throw new InvalidOperationException("The chart and report describe different captured scopes.");
        double pixels = layout is SunburstControl ? Math.PI * Math.Min(layout.PlotBounds.Width, layout.PlotBounds.Height) : layout.PlotBounds.Width * layout.PlotBounds.Height;
        var rows = layout.DisplayRows(view.Scope, total, (layout is SunburstControl ? 3 : 24) / Math.Max(1, pixels), 2048);
        var parts = layout.Layout(rows.Where(row => row.Value > 0).ToImmutableArray(), layout.PlotBounds, total);
        var palette = ThemeManager.Current;
        string ColorText(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        var shapes = ImmutableArray.CreateBuilder<AnalysisChartShape>();
        var texts = ImmutableArray.CreateBuilder<AnalysisChartText>();
        var offset = new Vector(12, 142 + extra);
        string ink = ColorText(AnalysisChartPalette.On(AnalysisChartPalette.Panel(palette), Color.Parse(palette.Text)));
        void Text(string value, double x, double y, double available, double size = 13, string? color = null) =>
            texts.Add(new(AnalysisNames.Display(AnalysisChartScene.XmlText(AnalysisNames.Display(value, 512)), 512), new Rect(x, y, Math.Max(1, available), size + 6), color ?? ink, size));
        Text((comparison is null ? "FileCat disk space · " : "FileCat comparison · ") + ViewName, 20, 16, width - 240, 22);
        if (header.Coverage != NamespaceCoverage.CompleteUnderPolicy) Text("PARTIAL", width - 200, 16, 180, 22);
        string metric = header.Metric switch { AnalysisMetric.FileSize => "File size (bytes)", AnalysisMetric.UniqueFileSize => "Unique file size (bytes)", AnalysisMetric.OnDisk => "On disk — reported (bytes)", _ => "Files (paths)" };
        Text("Metric: " + metric + " · Known scope value: " + total.ToString(CultureInfo.InvariantCulture), 20, 50, width - 40);
        Text("Scope: " + (redact ? "Captured scope (names and locations omitted)" : header.ScopeLabel), 20, 74, width - 40);
        if (comparison is null) Text("Observed UTC: " + header.StartedUtc.ToUniversalTime().ToString("u") + " – " + header.ObservedUtc.ToUniversalTime().ToString("u"), 20, 98, width - 40);
        else
        {
            string Interval(SessionIdentity identity) => identity.StartedUtc.ToUniversalTime().ToString("u") + " – " + identity.ObservedUtc.ToUniversalTime().ToString("u");
            Text("Previous UTC: " + Interval(comparison.Previous), 20, 98, width - 40);
            Text("Current UTC: " + Interval(comparison.Current), 20, 122, width - 40);
            Text((comparison.PreviousSide ? "Previous" : "Current") + " recorded amounts · " + (comparison.Structural ? "Explicit structural comparison" : comparison.Compatibility.VerifiedIdentityContext ? "Compatible identity context" : "Path-based observations") +
                (comparison.Compatibility.ComparableTotals ? "" : " · LIMITED comparison"), 20, 146, width - 40);
            string Coverage(NamespaceCoverage value) => value == NamespaceCoverage.CompleteUnderPolicy ? "traversal complete under policy" : value == NamespaceCoverage.Unknown ? "unknown" : "partial";
            Text("Previous coverage: " + Coverage(comparison.PreviousCoverage) + " · Current coverage: " + Coverage(comparison.CurrentCoverage), 20, 170, width - 40);
        }
        bool partial = header.Coverage != NamespaceCoverage.CompleteUnderPolicy;
        Text(partial ? "PARTIAL OBSERVATIONS — not a complete inventory" : "Traversal complete under captured policy", 20, 121 + extra, width - 40);
        int ordinal = 0;
        foreach (var part in parts)
        {
            var item = layout.Item(part.Key); if (item is null) continue;
            var paint = AnalysisChartPalette.For(AnalysisChartColors.Category(view.Tree, item, view.Color).Key, palette);
            var bounds = part.Bounds.Translate(offset);
            string? path = null;
            if (layout is SunburstControl)
            {
                double radius = Math.Min(bounds.Width, bounds.Height) / 2, ring = (1 - layout.SunburstHoleRatio) / 4;
                double outer = radius * (layout.SunburstHoleRatio + (part.Depth + 1) * ring), inner = radius * (layout.SunburstHoleRatio + part.Depth * ring);
                double sweep = Math.Min(part.Sweep, 2 * Math.PI - .000001), end = part.Start + sweep;
                string Point(double angle, double r) => F(bounds.Center.X + Math.Cos(angle) * r) + " " + F(bounds.Center.Y + Math.Sin(angle) * r);
                string large = sweep > Math.PI ? "1" : "0";
                path = $"M {Point(part.Start, outer)} A {F(outer)} {F(outer)} 0 {large} 1 {Point(end, outer)} L {Point(end, inner)} A {F(inner)} {F(inner)} 0 {large} 0 {Point(part.Start, inner)} Z";
            }
            shapes.Add(new(bounds, path, ColorText(paint.Fill), ColorText(paint.Border), item.Synthetic || item.IdentityUnverified > 0));
            string label = redact ? "Item " + (++ordinal).ToString(CultureInfo.InvariantCulture) : AnalysisNames.Display(item.Name, 90);
            if (layout is TreemapControl && bounds.Width > 72 && bounds.Height >= 30)
            {
                Text(label, bounds.X + 7, bounds.Y + 5, bounds.Width - 14, color: ColorText(paint.Text));
                if (bounds.Height >= 48) Text(DiskSpaceViewModel.ValueText(item, view.Tree.Metric), bounds.X + 7, bounds.Y + 24, bounds.Width - 14, color: ColorText(paint.Text));
            }
        }
        if (parts.Length == 0) Text("No positive chart area. Zero, unknown and shared entries remain in the table.", 28, 172 + extra, width - 320);
        if (layout is SunburstControl)
        {
            var center = layout.PlotBounds.Center + offset;
            Text(redact ? "Captured scope" : AnalysisNames.Display(view.Tree.NameOf(view.Scope), 24), center.X - 68, center.Y - 18, 136);
            Text(total.ToString(CultureInfo.InvariantCulture), center.X - 68, center.Y + 4, 136);
        }
        Text("Colors: " + AnalysisChartColors.Title(view.Color), width - 250, 154 + extra, 230, 14);
        int legendRow = 0;
        foreach (var category in AnalysisChartColors.Legend(view.Color))
        {
            double y = 184 + extra + legendRow++ * 25;
            var paint = AnalysisChartPalette.For(category.Key, palette);
            shapes.Add(new(new Rect(width - 250, y, 14, 14), null, ColorText(paint.Fill), ColorText(paint.Border), false));
            Text(category.Label, width - 230, y - 2, 210, 12);
        }
        if (comparison is not null) Text("Growth colors describe observed changes, not signed area. Full captured hierarchy; table filters do not restrict this image.", 20, height - 110, width - 40, 12);
        Text("Areas show known captured measures; unknown values are not zero. Shared charges are not reclaimable space.", 20, height - 86, width - 40, 12);
        Text(comparison is null ? "Small items may be aggregated; this chart does not replace the complete table or inventory. Freshness: " + header.Freshness
            : "Small items may be aggregated. Previous freshness: " + comparison.PreviousFreshness + " · Current freshness: " + comparison.CurrentFreshness, 20, height - 64, width - 40, 12);
        Text((redact ? "Names, locations and source identifiers omitted." : "Sensitive displayed names and scope included.") +
            (header.Taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? " Includes privileged-derived observations." : "") +
            (header.Taint.HasFlag(AnalysisTaint.ImportedUnverified) ? " Imported, unverified observations." : ""), 20, height - 42, width - 40, 12);
        return new(width, height, ColorText(AnalysisChartPalette.Panel(palette)), shapes.ToImmutable(), texts.ToImmutable());
    }
    private static string F(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}

internal sealed record AnalysisChartShape(Rect Bounds, string? Path, string Fill, string Border, bool Dashed);
internal sealed record AnalysisChartText(string Value, Rect Bounds, string Color, double Size);

/// <summary>Only generated geometry, normalized colors and reviewed display text. No source keys or hidden payload.</summary>
internal sealed record AnalysisChartScene(int Width, int Height, string Background, ImmutableArray<AnalysisChartShape> Shapes, ImmutableArray<AnalysisChartText> Texts)
{
    internal static string XmlText(string value)
    {
        var result = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (XmlConvert.IsXmlChar(c)) result.Append(c);
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            { result.Append(c); result.Append(value[++i]); }
            else result.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }
    internal static void ValidateSize(int width, int height)
    {
        if (width < 960 || height < 720 || width > 16384 || height > 16384 || (long)width * height > 64_000_000)
            throw new ArgumentOutOfRangeException(nameof(width), "Choose dimensions from 960×720 up to 16,384 per side and 64 megapixels; memory admission may require a smaller image.");
    }
    internal void WriteSvg(Stream output, CancellationToken cancellation)
    {
        const string ns = "http://www.w3.org/2000/svg";
        using var xml = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false, true), CloseOutput = false, CheckCharacters = true });
        void Start(string name) => xml.WriteStartElement(name, ns);
        void Attr(string name, string value) => xml.WriteAttributeString(name, value);
        string N(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
        void Box(Rect r) { Attr("x", N(r.X)); Attr("y", N(r.Y)); Attr("width", N(r.Width)); Attr("height", N(r.Height)); }
        Start("svg"); Attr("width", N(Width)); Attr("height", N(Height)); Attr("viewBox", $"0 0 {Width} {Height}");
        Start("rect"); Box(new Rect(0, 0, Width, Height)); Attr("fill", Background); xml.WriteEndElement();
        foreach (var shape in Shapes)
        {
            cancellation.ThrowIfCancellationRequested(); Start(shape.Path is null ? "rect" : "path");
            if (shape.Path is null) Box(shape.Bounds); else Attr("d", shape.Path);
            Attr("fill", shape.Fill); Attr("stroke", shape.Border); Attr("stroke-width", "1");
            if (shape.Dashed) Attr("stroke-dasharray", "3 2"); xml.WriteEndElement();
        }
        for (int i = 0; i < Texts.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested(); var text = Texts[i]; string id = "t" + i.ToString(CultureInfo.InvariantCulture);
            Start("defs"); Start("clipPath"); Attr("id", id); Start("rect"); Box(text.Bounds); xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
            Start("text"); Attr("x", N(text.Bounds.X)); Attr("y", N(text.Bounds.Y + text.Size)); Attr("font-family", "sans-serif"); Attr("font-size", N(text.Size));
            Attr("fill", text.Color); Attr("clip-path", "url(#" + id + ")"); xml.WriteString(XmlText(text.Value)); xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.Flush();
    }
}

internal sealed class AnalysisChartCanvas(AnalysisChartScene scene) : Control
{
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new SolidColorBrush(Color.Parse(scene.Background)), new Rect(0, 0, scene.Width, scene.Height));
        foreach (var shape in scene.Shapes)
        {
            var fill = new SolidColorBrush(Color.Parse(shape.Fill));
            var pen = new Pen(new SolidColorBrush(Color.Parse(shape.Border)), 1, shape.Dashed ? new DashStyle([3, 2], 0) : null);
            if (shape.Path is null) context.DrawRectangle(fill, pen, shape.Bounds); else context.DrawGeometry(fill, pen, Geometry.Parse(shape.Path));
        }
        foreach (var text in scene.Texts)
        {
            var formatted = new FormattedText(text.Value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(FontFamily.Default), text.Size, new SolidColorBrush(Color.Parse(text.Color)))
            { MaxTextWidth = text.Bounds.Width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            using (context.PushClip(text.Bounds)) context.DrawText(formatted, text.Bounds.TopLeft);
        }
    }
}
