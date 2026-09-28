using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

public sealed record InspectionSection(string Title, IReadOnlyList<(string Name, string Value)> Fields);

/// <summary>What a static inspector found (plan §16.1): sections of named values plus warnings about malformed structure.</summary>
public sealed record InspectionReport(string Format, IReadOnlyList<InspectionSection> Sections, IReadOnlyList<string> Warnings)
{
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Format);
        foreach (var w in Warnings) sb.AppendLine("⚠ " + w);
        foreach (var section in Sections)
        {
            sb.AppendLine();
            sb.AppendLine(section.Title);
            int width = section.Fields.Count == 0 ? 0 : Math.Min(28, section.Fields.Max(f => f.Name.Length));
            foreach (var (name, value) in section.Fields) sb.AppendLine("  " + name.PadRight(width) + "  " + value);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Static inspectors: they read bounded structures from content and never load, execute, or decode active content.
/// Malformed input yields warnings, never exceptions other than cancellation and I/O failures.
/// </summary>
public static class Inspectors
{
    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct) =>
        PeInspector.Inspect(source, ct) ?? ImageInspector.Inspect(source, ct);
}

/// <summary>Bounded little- and big-endian reads over content; short reads return what exists.</summary>
internal sealed class ContentReader(IContentSource source)
{
    public long Length { get; } = source.Length;

    public byte[] Read(long offset, int count)
    {
        if (offset < 0 || count <= 0) return [];
        if (Length >= 0) count = (int)Math.Min(count, Math.Max(0, Length - offset));
        var buffer = new byte[count];
        int total = 0;
        while (total < count)
        {
            int n = source.Read(offset + total, buffer.AsSpan(total));
            if (n <= 0) break;
            total += n;
        }
        return total == count ? buffer : buffer[..total];
    }

    public string AsciiZ(long offset, int max = 256)
    {
        var bytes = Read(offset, max);
        int end = Array.IndexOf(bytes, (byte)0);
        var text = Encoding.ASCII.GetString(bytes, 0, end < 0 ? bytes.Length : end);
        // Only printable text is shown: names in hostile files are data, not markup or control sequences.
        return new string(text.Select(c => c is >= ' ' and < (char)127 ? c : '?').ToArray());
    }
}
