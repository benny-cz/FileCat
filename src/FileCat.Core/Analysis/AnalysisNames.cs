using System.Globalization;
using System.Text;

namespace FileCat.Core.Analysis;

public static class AnalysisNames
{
    // Inert observed names can be outside Win32's action-addressable subset. Native relative opens still
    // prohibit separators, streams and navigation components; passing this check grants no action authority.
    public static bool IsObservedChild(string name, bool windows, int rawBytes) =>
        name.Length > 0 && name is not ("." or "..") && !name.Contains('\0') && !name.Contains('/') &&
        (!windows || !name.Contains('\\') && !name.Contains(':')) && rawBytes is > 0 and <= AnalysisLimits.RawNameBytes;
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

    public static string Display(string value, int maximum = 4096, bool escapeTrailing = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var output = new StringBuilder();
        int trailing = value.Length;
        if (escapeTrailing) while (trailing > 0 && value[trailing - 1] is ' ' or '.') trailing--;
        int count = 0;
        for (int offset = 0; offset < value.Length;)
        {
            if (count++ >= maximum) { output.Append('…'); break; }
            if (!Rune.TryGetRuneAt(value, offset, out var rune))
            {
                output.Append("\\u").Append(((int)value[offset++]).ToString("X4", CultureInfo.InvariantCulture));
                continue;
            }
            var category = Rune.GetUnicodeCategory(rune);
            if (offset >= trailing || category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                output.Append("\\u").Append(rune.Value.ToString(rune.Value <= 65535 ? "X4" : "X8", CultureInfo.InvariantCulture));
            else output.Append(rune);
            offset += rune.Utf16SequenceLength;
        }
        return output.ToString();
    }

    public static string DisplayName(AnalysisNode node, int maximum = 4096)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        if (node.NameEncoding == "unix-bytes" && !node.RawName.IsDefaultOrEmpty && !AnalysisRawNames.HasLosslessText(node))
        {
            int take = Math.Min(node.RawName.Length, Math.Max(0, maximum / 2));
            return "[unix bytes] " + Convert.ToHexString(node.RawName.AsSpan(0, take)) + (take < node.RawName.Length ? "…" : "");
        }
        // Literal backslashes must remain distinguishable from the visible Unicode escapes above.
        return Display(node.Name.Replace("\\", "\\\\", StringComparison.Ordinal), maximum, escapeTrailing: true);
    }
    public static string DisplayName(AnalysisPathMetadata path, int maximum = 4096) => DisplayName(
        new AnalysisNode(path.Id, path.RootId, path.ParentId, path.Name, path.Kind)
            { NameEncoding = path.NameEncoding, RawName = path.RawName }, maximum);
    public static string? NameEvidence(AnalysisNode node)
    {
        if (AnalysisRawNames.HasLosslessText(node)) return null;
        var raw = node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name).AsSpan() : node.RawName.AsSpan();
        int take = Math.Min(raw.Length, 256);
        return "Raw name (" + node.NameEncoding + "): " + Convert.ToHexString(raw[..take]) + (take < raw.Length ? "…" : "");
    }
    public static string DisplayPath(string path, AnalysisNameRules rules)
    {
        if (rules == AnalysisNameRules.Provider) return Display(path, escapeTrailing: true);
        var output = new StringBuilder(); int start = 0;
        for (int i = 0; i < path.Length; i++)
            if (path[i] == '/' || rules == AnalysisNameRules.Windows && path[i] == '\\')
            { output.Append(Component(path[start..i])).Append(path[i]); start = i + 1; }
        return output.Append(Component(path[start..])).ToString();
        string Component(string name) => Display(rules == AnalysisNameRules.Unix ? name.Replace("\\", "\\\\", StringComparison.Ordinal) : name, escapeTrailing: true);
    }

    public static string CsvCell(string value)
    {
        // Protect spreadsheet interpretation, including leading whitespace before a formula marker.
        string trimmed = value.TrimStart();
        if (trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@' || value.StartsWith('\t') || value.StartsWith('\r')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
