using System.Globalization;
using System.Text;

namespace FileCat.Core.Analysis;

public static class AnalysisNames
{
    // A source name is a single component. Host-specific path separators must not change imported classification.
    public static string Extension(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot < 0 || dot == name.Length - 1 ? "" : name[(dot + 1)..];
    }
    // This validates data for a future binding. Passing this check does not create live authority.
    public static bool IsOrdinaryChild(string name, bool windows, int? rawBytes = null)
    {
        if (name.Length == 0 || name is "." or ".." || name.Contains('\0') || name.Contains('/') ||
            (rawBytes ?? Encoding.UTF8.GetByteCount(name)) is < 1 or > AnalysisLimits.RawNameBytes) return false;
        if (!windows) return true;
        if (name.Contains('\\') || name.Contains(':') || name.EndsWith('.') || name.EndsWith(' ') ||
            name.IndexOfAny(['<', '>', '"', '|', '?', '*']) >= 0 || name.Any(c => c < 32)) return false;
        string stem = name.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase)) return false;
        if (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            (stem[3] is >= '1' and <= '9' or '¹' or '²' or '³')) return false;
        return true;
    }

    public static string Display(string value, int maximum = 4096)
    {
        var output = new StringBuilder();
        int i = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (i++ >= maximum) { output.Append('…'); break; }
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                output.Append("\\u").Append(rune.Value.ToString(rune.Value <= 65535 ? "X4" : "X8", CultureInfo.InvariantCulture));
            else output.Append(rune);
        }
        return output.ToString();
    }

    public static string CsvCell(string value)
    {
        // Protect spreadsheet interpretation, including leading whitespace before a formula marker.
        string trimmed = value.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
