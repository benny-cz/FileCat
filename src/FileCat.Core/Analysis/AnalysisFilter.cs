using System.Globalization;
using System.Text;

namespace FileCat.Core.Analysis;

public enum AnalysisTruth { False, True, Unknown }
public sealed record AnalysisFilterItem(AnalysisNode Node, AnalysisObject? Object, string RelativePath,
    AnalysisMeasurement Allocation, Int128? Delta = null, string? Change = null, bool Stale = false, bool Unreadable = false);

/// <summary>Bounded declarative query parser. It has no filesystem, reflection or executable expression access.</summary>
public sealed class AnalysisFilter
{
    private readonly Func<AnalysisFilterItem, AnalysisTruth> _evaluate;
    private AnalysisFilter(Func<AnalysisFilterItem, AnalysisTruth> evaluate) => _evaluate = evaluate;
    public AnalysisTruth Evaluate(AnalysisFilterItem item) => _evaluate(item);
    public static AnalysisFilter Parse(string text, TimeZoneInfo zone, bool caseSensitive = false, bool comparison = false) =>
        new(new Parser(text, zone, caseSensitive, comparison).Parse());

    public static AnalysisTruth Not(AnalysisTruth v) => v switch { AnalysisTruth.True => AnalysisTruth.False, AnalysisTruth.False => AnalysisTruth.True, _ => AnalysisTruth.Unknown };
    public static AnalysisTruth And(AnalysisTruth a, AnalysisTruth b) => a == AnalysisTruth.False || b == AnalysisTruth.False ? AnalysisTruth.False : a == AnalysisTruth.True && b == AnalysisTruth.True ? AnalysisTruth.True : AnalysisTruth.Unknown;
    public static AnalysisTruth Or(AnalysisTruth a, AnalysisTruth b) => a == AnalysisTruth.True || b == AnalysisTruth.True ? AnalysisTruth.True : a == AnalysisTruth.False && b == AnalysisTruth.False ? AnalysisTruth.False : AnalysisTruth.Unknown;

    public static UInt128 ParseBytes(string text)
    {
        int split = 0;
        while (split < text.Length && (char.IsAsciiDigit(text[split]) || text[split] == '.')) split++;
        string number = text[..split], suffix = text[split..].ToUpperInvariant();
        ulong multiplier = suffix switch
        {
            "" or "B" => 1, "KB" => 1000, "MB" => 1_000_000, "GB" => 1_000_000_000, "TB" => 1_000_000_000_000,
            "KIB" => 1024, "MIB" => 1_048_576, "GIB" => 1_073_741_824, "TIB" => 1_099_511_627_776,
            _ => throw new FormatException("Unknown byte suffix.")
        };
        var parts = number.Split('.');
        if (parts.Length > 2 || parts[0].Length == 0 || parts.Length == 2 && parts[1].Length is < 1 or > 3 ||
            parts.Any(p => p.Any(c => !char.IsAsciiDigit(c)))) throw new FormatException("Invalid byte threshold.");
        UInt128 whole = UInt128.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
        UInt128 result = checked(whole * multiplier);
        if (parts.Length == 2)
        {
            uint divisor = parts[1].Length switch { 1 => 10u, 2 => 100u, _ => 1000u };
            UInt128 fraction = checked(UInt128.Parse(parts[1], CultureInfo.InvariantCulture) * multiplier);
            if (fraction % divisor != 0) throw new FormatException("Threshold must resolve to whole bytes.");
            result = checked(result + fraction / divisor);
        }
        return result;
    }

    private sealed class Parser
    {
        private readonly List<Token> _tokens = [];
        private readonly TimeZoneInfo _zone;
        private readonly StringComparison _case;
        private readonly bool _comparison;
        private int _at, _terms;
        private sealed record Token(string Value, bool Quoted = false);
        public Parser(string text, TimeZoneInfo zone, bool sensitive, bool comparison)
        {
            if (Encoding.UTF8.GetByteCount(text) > AnalysisLimits.QueryBytes) throw new FormatException("Filter exceeds 8 KiB.");
            _zone = zone; _case = sensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase; _comparison = comparison;
            for (int i = 0; i < text.Length;)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '"')
                {
                    var b = new StringBuilder(); i++;
                    bool closed = false;
                    while (i < text.Length)
                    {
                        c = text[i++];
                        if (c == '"') { closed = true; break; }
                        if (c == '\\')
                        {
                            if (i >= text.Length || text[i] is not ('"' or '\\')) throw new FormatException("Only quote and backslash can be escaped.");
                            c = text[i++];
                        }
                        b.Append(c);
                    }
                    if (!closed) throw new FormatException("Unclosed quoted text.");
                    _tokens.Add(new(b.ToString(), true)); continue;
                }
                if ("():=<>!".Contains(c))
                {
                    string op = c.ToString(); i++;
                    if (i < text.Length && text[i] == '=' && c is '<' or '>' or '!') { op += '='; i++; }
                    _tokens.Add(new(op)); continue;
                }
                int start = i++;
                while (i < text.Length && !char.IsWhiteSpace(text[i]) && !"():=<>!\"".Contains(text[i])) i++;
                _tokens.Add(new(text[start..i]));
            }
        }
        private bool Is(string t) => _at < _tokens.Count && !_tokens[_at].Quoted && _tokens[_at].Value == t;
        private Token Take() => _at < _tokens.Count ? _tokens[_at++] : throw new FormatException("Incomplete filter.");
        private bool Consume(string t) { if (!Is(t)) return false; _at++; return true; }
        public Func<AnalysisFilterItem, AnalysisTruth> Parse()
        {
            if (_tokens.Count == 0) return _ => AnalysisTruth.True;
            var expression = Expression(0);
            if (_at != _tokens.Count) throw new FormatException("Unexpected filter token.");
            return expression;
        }
        private Func<AnalysisFilterItem, AnalysisTruth> Expression(int depth)
        {
            var left = Conjunction(depth);
            while (Consume("OR")) { var a = left; var b = Conjunction(depth); left = i => Or(a(i), b(i)); }
            return left;
        }
        private Func<AnalysisFilterItem, AnalysisTruth> Conjunction(int depth)
        {
            var left = Unary(depth);
            while (_at < _tokens.Count && !Is(")") && !Is("OR"))
            {
                Consume("AND"); var a = left; var b = Unary(depth); left = i => And(a(i), b(i));
            }
            return left;
        }
        private Func<AnalysisFilterItem, AnalysisTruth> Unary(int depth)
        {
            if (depth > AnalysisLimits.QueryDepth) throw new FormatException("Filter grouping is too deep.");
            bool not = Consume("NOT");
            Func<AnalysisFilterItem, AnalysisTruth> p;
            if (Consume("(")) { p = Expression(depth + 1); if (!Consume(")")) throw new FormatException("Unclosed filter group."); }
            else
            {
                if (++_terms > AnalysisLimits.QueryTerms) throw new FormatException("Too many filter terms.");
                var t = Take();
                if (!t.Quoted && (t.Value is "AND" or "OR" or "NOT" or ")" || "=:<>!".Contains(t.Value))) throw new FormatException("Expected a filter term.");
                if (_at < _tokens.Count && !_tokens[_at].Quoted && _tokens[_at].Value is ":" or "=" or "!=" or "<" or "<=" or ">" or ">=")
                {
                    string op = Take().Value; var value = Take();
                    if (!value.Quoted && value.Value is "(" or ")" or "AND" or "OR" or "NOT" or ":" or "=" or "<" or ">" or "!") throw new FormatException("Expected a field value.");
                    // Offset timestamps contain colons. Consume only contiguous timestamp-like fragments.
                    string v = value.Value;
                    if (t.Value is "modified" or "modified-before" && !value.Quoted)
                        while (Consume(":")) v += ":" + Take().Value;
                    p = Field(t.Value, op, v);
                }
                else p = i => Truth(i.Node.Name.Contains(t.Value, _case));
            }
            return not ? i => Not(p(i)) : p;
        }
        private Func<AnalysisFilterItem, AnalysisTruth> Field(string field, string op, string value)
        {
            if (field is "size" or "allocated" or "delta")
            {
                if (op == ":") throw new FormatException("Byte fields require a comparison operator.");
                bool negative = value.StartsWith('-');
                if (negative && field != "delta") throw new FormatException("A byte length cannot be negative.");
                UInt128 bytes = ParseBytes(negative ? value[1..] : value.StartsWith('+') && field == "delta" ? value[1..] : value);
                if (field == "delta")
                {
                    if (!_comparison || bytes > (UInt128)Int128.MaxValue) throw new FormatException("Delta requires a comparison and a signed bounded value.");
                    Int128 limit = negative ? -(Int128)bytes : (Int128)bytes;
                    return i => i.Delta.HasValue ? Truth(Compare(i.Delta.Value.CompareTo(limit), op)) : AnalysisTruth.Unknown;
                }
                return i =>
                {
                    var m = field == "allocated" ? i.Allocation : i.Object?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                    return m.IsKnown ? Truth(Compare(m.Value!.Value.CompareTo(bytes), op)) : AnalysisTruth.Unknown;
                };
            }
            if (field is "modified" or "modified-before")
            {
                if (field == "modified-before") { if (op is not (":" or "=")) throw new FormatException("modified-before uses colon or equals."); op = "<"; }
                if (op == ":") throw new FormatException("Dates require a comparison operator.");
                bool dateOnly = DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date);
                DateTimeOffset instant;
                if (dateOnly)
                {
                    var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                    if (op is ">" or "<=") local = local.AddDays(1);
                    if (_zone.IsAmbiguousTime(local) || _zone.IsInvalidTime(local)) throw new FormatException("Date boundary needs an explicit offset.");
                    instant = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, _zone));
                }
                else
                {
                    int t = value.IndexOf('T');
                    if (t < 0 || !(value.EndsWith('Z') || value[(t + 1)..].Contains('+') || value[(t + 1)..].Contains('-')) ||
                        !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant)) throw new FormatException("Use an ISO date or timestamp with an explicit offset.");
                }
                string comparisonOp = dateOnly ? op switch { ">" => ">=", "<=" => "<", _ => op } : op;
                DateTimeOffset? end = null;
                if (dateOnly && op is "=" or "!=")
                {
                    var localEnd = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                    if (_zone.IsAmbiguousTime(localEnd) || _zone.IsInvalidTime(localEnd)) throw new FormatException("Date boundary needs an explicit offset.");
                    end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, _zone));
                }
                return i => i.Object?.ModifiedUtc is not { } m ? AnalysisTruth.Unknown : end.HasValue
                    ? Truth(op == "=" ? m >= instant && m < end : !(m >= instant && m < end))
                    : Truth(Compare(m.CompareTo(instant), comparisonOp));
            }
            if (field is "sparse" or "compressed" or "cloud")
            {
                if (op is not (":" or "=") || !bool.TryParse(value, out bool wanted)) throw new FormatException("Boolean fields use :true or :false.");
                return i => Truth((field switch { "sparse" => i.Node.Sparse, "compressed" => i.Node.Compressed, _ => i.Node.Cloud }) == wanted);
            }
            if (field == "change")
            {
                if (!_comparison || value is not ("added" or "removed" or "growing" or "shrinking" or "moved" or "attribution" or "unknown")) throw new FormatException("Invalid comparison change.");
                EnumOperator(op); return i => i.Change is null ? AnalysisTruth.Unknown : Truth(i.Change == value);
            }
            if (field == "state")
            {
                EnumOperator(op);
                if (value is not ("known" or "unknown" or "pending" or "failed" or "excluded" or "stale" or "unreadable")) throw new FormatException("Unknown measurement state.");
                return i => Truth(value switch
                {
                    "stale" => i.Stale, "unreadable" => i.Unreadable,
                    "known" => i.Object?.LogicalLength.IsKnown == true,
                    "unknown" => i.Object is null || i.Object.LogicalLength.State is MeasurementState.Unsupported or MeasurementState.Changed,
                    "pending" => i.Object?.LogicalLength.State == MeasurementState.Pending,
                    "failed" => i.Object?.LogicalLength.State == MeasurementState.Failed,
                    _ => i.Object?.LogicalLength.State == MeasurementState.Excluded
                });
            }
            if (field is not ("name" or "path" or "ext" or "type" or "root")) throw new FormatException("Unknown filter field.");
            if (op is not (":" or "=" or "!=")) throw new FormatException("Text fields use colon, equals or not-equals.");
            if (field == "ext") value = value.StartsWith('.') ? value[1..] : value;
            return i =>
            {
                string candidate = field switch
                {
                    "name" => i.Node.Name, "path" => i.RelativePath, "root" => i.Node.RootId,
                    "ext" => Path.GetExtension(i.Node.Name).TrimStart('.'), _ => AnalysisClassification.Family(i.Node.Name)
                };
                if (field == "type" && !AnalysisClassification.Families.Contains(value, StringComparer.OrdinalIgnoreCase)) candidate = Path.GetExtension(i.Node.Name).TrimStart('.');
                return Truth(op == ":" && field is not ("type" or "root") ? candidate.Contains(value, _case) : op == "!=" ? !candidate.Equals(value, _case) : candidate.Equals(value, _case));
            };
        }
        private static void EnumOperator(string op) { if (op is not (":" or "=")) throw new FormatException("Enum fields use colon or equals."); }
        private static bool Compare(int order, string op) => op switch
        {
            "=" => order == 0, "!=" => order != 0, "<" => order < 0, "<=" => order <= 0, ">" => order > 0, ">=" => order >= 0,
            _ => throw new FormatException("Unsupported comparison operator.")
        };
    }
    private static AnalysisTruth Truth(bool value) => value ? AnalysisTruth.True : AnalysisTruth.False;
}

public static class AnalysisClassification
{
    public static readonly string[] Families = ["text", "code", "image", "audio", "video", "archive", "executable", "document", "other", "unknown"];
    private static readonly Dictionary<string, string> Extensions = Build();
    public static string Family(string name) => Extensions.GetValueOrDefault(Path.GetExtension(name).TrimStart('.'), "other");
    private static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string family, string extensions) { foreach (string e in extensions.Split(' ')) d.Add(e, family); }
        Add("text", "txt md log csv tsv rst"); Add("code", "cs fs vb cpp c h hpp py js ts tsx jsx java go rs rb sh ps1 json xml yaml yml toml css html sql");
        Add("image", "png jpg jpeg gif webp bmp tiff tif heic avif svg ico"); Add("audio", "mp3 wav flac ogg m4a aac opus");
        Add("video", "mp4 mkv mov avi webm m4v"); Add("archive", "zip 7z rar tar gz bz2 xz zst iso dmg");
        Add("executable", "exe dll msi msix appx deb rpm pkg appimage"); Add("document", "pdf doc docx xls xlsx ppt pptx odt ods odp rtf epub");
        return d;
    }
}
