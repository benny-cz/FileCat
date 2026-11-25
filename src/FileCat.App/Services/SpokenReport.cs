using System.Globalization;
using FileCat.Core.Inspect;

namespace FileCat.App.Services;

/// <summary>
/// What a report window's spoken-line readout says for each line of <see cref="InspectionReport.ToText"/>: a section's
/// title as a heading, a warning as a warning, a field as "name: value", a table as its column names and row count, and
/// each row as its cells under their column names. The text itself (shown, found, copied and saved) is unchanged.
/// </summary>
internal static class SpokenReport
{
    /// <summary>
    /// One entry per line of <paramref name="text"/>, which must be <paramref name="report"/>'s own text. Null entries are
    /// read as shown: blank lines, and every line of a part whose value holds a line break. Null as a whole when the report's
    /// structure does not account for every line of the text, so a mismatch never attaches words to the wrong line.
    /// </summary>
    public static string?[]? Lines(InspectionReport report, string text)
    {
        var spoken = new List<string?>();
        void Add(string said, params string[] parts)
        {
            int breaks = 0;
            foreach (string part in parts) breaks += part.AsSpan().Count('\n');
            spoken.Add(breaks == 0 ? said : null);
            for (int i = 0; i < breaks; i++) spoken.Add(null);
        }

        Add(report.Format, report.Format);
        foreach (string warning in report.Warnings) Add("Warning: " + warning, warning);
        foreach (var section in report.Sections) Section(section, depth: 0);
        return spoken.Count == text.AsSpan().Count('\n') ? spoken.ToArray() : null;

        void Section(InspectionSection section, int depth)
        {
            spoken.Add(null); // the blank line before every title
            Add((depth == 0 ? "Section: " : "Subsection: ") + section.Title, section.Title);
            foreach (var (name, value) in section.Fields) Add($"{name}: {value}", name, value);
            if (section.Table is { } table)
            {
                if (section.Fields.Count > 0) spoken.Add(null);
                var columns = table.Columns;
                string rows = table.Rows.Count switch { 0 => "no rows", 1 => "1 row", var n => n.ToString("N0", CultureInfo.CurrentCulture) + " rows" };
                Add($"Table with {rows}. Columns: {string.Join(", ", columns)}.", [.. columns]);
                spoken.Add("Table rule.");
                for (int r = 0; r < table.Rows.Count; r++)
                {
                    string[] row = table.Rows[r];
                    var cells = new List<string>();
                    var parts = new List<string>();
                    for (int c = 0; c < columns.Count; c++)
                    {
                        // As the text lays it out: cells past the last column are not shown.
                        string cell = c < row.Length ? row[c] : "";
                        parts.Add(cell);
                        if (cell.Length > 0) cells.Add($"{columns[c]}: {cell}");
                    }
                    Add($"Row {r + 1:N0} of {table.Rows.Count:N0}. {(cells.Count == 0 ? "(empty)" : string.Join("; ", cells))}", [.. parts]);
                }
                if (table.More is { } more) Add(more, more);
            }
            if (section.Lines.Count > 0)
            {
                if (section.Fields.Count > 0 || section.Table is not null) spoken.Add(null);
                foreach (string line in section.Lines) Add(line, line);
            }
            foreach (var child in section.Children) Section(child, depth + 1);
        }
    }
}
