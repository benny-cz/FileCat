using System.Text;
using FileCat.Core.Resources;

namespace FileCat.Core.Inspect;

/// <summary>Rows under column headings (a section table, an import list), aligned in the text.</summary>
public sealed record InspectionTable(IReadOnlyList<string> Columns, IReadOnlyList<string[]> Rows)
{
    /// <summary>What the rows leave out ("12,000 more functions are not listed"), said under them.</summary>
    public string? More { get; init; }
}

/// <summary>
/// Part of a report: named values, then optionally a table, preformatted lines (a manifest's text), and sections within
/// it (an import table's libraries).
/// </summary>
public sealed record InspectionSection(string Title, IReadOnlyList<(string Name, string Value)> Fields)
{
    public InspectionTable? Table { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = [];
    public IReadOnlyList<InspectionSection> Children { get; init; } = [];
}

/// <summary>What a static inspector found (plan §16.1): sections of named values plus warnings about malformed structure.</summary>
public sealed record InspectionReport(string Format, IReadOnlyList<InspectionSection> Sections, IReadOnlyList<string> Warnings)
{
    /// <summary>The report as text, the way Salamander's PE Viewer dumps a file: sections, aligned values, and tables.</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Format);
        foreach (var w in Warnings) sb.AppendLine("⚠ " + w);
        foreach (var section in Sections) Append(sb, section, "");
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, InspectionSection section, string indent)
    {
        sb.AppendLine();
        sb.Append(indent).AppendLine(section.Title);
        string inner = indent + "  ";
        int width = section.Fields.Count == 0 ? 0 : Math.Min(40, section.Fields.Max(f => f.Name.Length));
        foreach (var (name, value) in section.Fields)
            sb.Append(inner).Append(name.PadRight(width)).Append("  ").AppendLine(value);
        if (section.Table is { } table) AppendTable(sb, table, inner, section.Fields.Count > 0);
        if (section.Lines.Count > 0)
        {
            if (section.Fields.Count > 0 || section.Table is not null) sb.AppendLine();
            foreach (var line in section.Lines) sb.Append(inner).AppendLine(line);
        }
        foreach (var child in section.Children) Append(sb, child, inner);
    }

    private static void AppendTable(StringBuilder sb, InspectionTable table, string indent, bool gap)
    {
        if (gap) sb.AppendLine();
        int columns = table.Columns.Count;
        var widths = new int[columns];
        var right = new bool[columns];
        for (int c = 0; c < columns; c++)
        {
            widths[c] = table.Columns[c].Length;
            // Numbers line up on their last digit.
            right[c] = table.Rows.Count > 0;
            foreach (var row in table.Rows)
            {
                string cell = c < row.Length ? row[c] : "";
                widths[c] = Math.Min(64, Math.Max(widths[c], cell.Length));
                if (cell.Length > 0 && !IsNumber(cell)) right[c] = false;
            }
        }
        void Row(IReadOnlyList<string> cells)
        {
            sb.Append(indent);
            for (int c = 0; c < columns; c++)
            {
                string cell = c < cells.Count ? cells[c] : "";
                bool last = c == columns - 1;
                if (right[c]) sb.Append(cell.PadLeft(widths[c]));
                else sb.Append(last ? cell : cell.PadRight(widths[c]));
                if (!last) sb.Append("  ");
            }
            // Padding the last column leaves no spaces at the end of the line.
            while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
            sb.AppendLine();
        }
        Row(table.Columns);
        Row(widths.Select(w => new string('─', w)).ToArray());
        foreach (var row in table.Rows) Row(row);
        if (table.More is { } more) sb.Append(indent).AppendLine(more);
    }

    /// <summary>A count or size as the current culture writes it ("4,096", "4 096"), or a plain decimal number (not a version).</summary>
    private static bool IsNumber(string cell)
    {
        if (cell.Count(ch => ch == '.') > 1) return false;
        foreach (char ch in cell)
            if (!(char.IsAsciiDigit(ch) || ch is ',' or '.' or ' ' or '\u00A0' or '\u202F'))
                return false;
        return char.IsAsciiDigit(cell[0]);
    }
}

/// <summary>
/// Static inspectors: they read bounded structures from content and never load, execute, or decode active content.
/// Malformed input yields warnings, never exceptions other than cancellation and I/O failures.
/// </summary>
public static class Inspectors
{
    public static InspectionReport? Inspect(IContentSource source, CancellationToken ct) =>
        PeInspector.Inspect(source, ct) ?? ElfInspector.Inspect(source, ct) ?? MachOInspector.Inspect(source, ct) ?? ApkInspector.Inspect(source, ct) ??
        MediaInspector.Inspect(source, ct) ?? ImageInspector.Inspect(source, ct) ?? HtmlInspector.Inspect(source, ct);
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
