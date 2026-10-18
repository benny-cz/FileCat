using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FileCat.Core.Selection;

public sealed partial class Mask
{
    // Recovery stores the parsed tree, including nested saved filters, rather than consulting today's
    // SavedFilters resolver. Limits apply to the whole expanded tree, not separately to each branch.
    internal const int SnapshotTextLimit = 16 * 1024;
    internal const int SnapshotPartLimit = 256;
    internal const int SnapshotDepthLimit = 8;

    /// <summary>The resolved matching rules, for review; parentheses retain saved-filter grouping.</summary>
    public string ResolvedDescription => Describe(this);

    internal JsonObject? CaptureSnapshot()
    {
        int parts = 0, characters = 0;
        try { return Capture(this, 0); }
        catch (ArgumentException) { return null; }

        JsonObject Capture(Mask mask, int depth)
        {
            CheckDepth(depth);
            AddText(mask.Text);
            return new JsonObject { ["text"] = mask.Text, ["include"] = Terms(mask._include, depth), ["exclude"] = Terms(mask._exclude, depth) };
        }
        JsonArray Terms(MaskPart[] values, int depth)
        {
            var array = new JsonArray();
            foreach (var term in values)
            {
                if (++parts > SnapshotPartLimit) throw new ArgumentException("Too many mask terms.");
                if (term.Nested is { } nested)
                    array.Add(new JsonObject { ["kind"] = "mask", ["mask"] = Capture(nested, depth + 1) });
                else if (term.Expression is { } expression)
                {
                    string pattern = expression.ToString(); AddText(pattern);
                    array.Add(new JsonObject { ["kind"] = "regex", ["pattern"] = pattern, ["ignoreCase"] = expression.Options.HasFlag(RegexOptions.IgnoreCase) });
                }
                else
                {
                    AddText(term.Glob!);
                    array.Add(new JsonObject { ["kind"] = "glob", ["pattern"] = term.Glob, ["directoriesOnly"] = term.DirectoriesOnly });
                }
            }
            return array;
        }
        void AddText(string value)
        {
            if (value.Length > SnapshotTextLimit - characters) throw new ArgumentException("Mask text is too large.");
            characters += value.Length;
        }
    }

    internal static bool TryRestoreSnapshot(JsonNode? node, out Mask? mask)
    {
        int parts = 0, characters = 0;
        try { mask = Restore(node, 0); return true; }
        catch (ArgumentException) { mask = null; return false; }

        Mask Restore(JsonNode? value, int depth)
        {
            CheckDepth(depth);
            if (value is not JsonObject obj || obj.Count != 3) throw new ArgumentException("Invalid mask object.");
            string text = ReadText(obj["text"]);
            return new Mask(text, Terms(obj["include"], depth), Terms(obj["exclude"], depth));
        }
        MaskPart[] Terms(JsonNode? value, int depth)
        {
            if (value is not JsonArray array || array.Count > SnapshotPartLimit - parts) throw new ArgumentException("Invalid mask terms.");
            var result = new MaskPart[array.Count];
            for (int i = 0; i < array.Count; i++)
            {
                if (++parts > SnapshotPartLimit || array[i] is not JsonObject term) throw new ArgumentException("Invalid mask term.");
                string kind = term["kind"] is JsonValue kindValue && kindValue.TryGetValue<string>(out var kindText)
                    ? kindText : throw new ArgumentException("Invalid mask kind.");
                result[i] = kind switch
                {
                    "mask" when term.Count == 2 => new MaskPart(null, null, false) { Nested = Restore(term["mask"], depth + 1) },
                    "glob" when term.Count == 3 => new MaskPart(ReadText(term["pattern"]), null, ReadBool(term["directoriesOnly"])),
                    "regex" when term.Count == 3 => new MaskPart(null, new Regex(ReadText(term["pattern"]),
                        RegexOptions.CultureInvariant | (ReadBool(term["ignoreCase"]) ? RegexOptions.IgnoreCase : RegexOptions.None), RegexTimeout), false),
                    _ => throw new ArgumentException("Unsupported mask term."),
                };
            }
            return result;
        }
        string ReadText(JsonNode? value)
        {
            if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out var text) || text.Length > SnapshotTextLimit - characters)
                throw new ArgumentException("Invalid mask text.");
            characters += text.Length;
            return text;
        }
    }

    private static bool ReadBool(JsonNode? value) => value is JsonValue scalar && scalar.TryGetValue<bool>(out bool flag)
        ? flag : throw new ArgumentException("Invalid mask flag.");

    private static void CheckDepth(int depth)
    {
        if (depth > SnapshotDepthLimit) throw new ArgumentException("Saved filter nesting is too deep.");
    }

    private static string Describe(Mask mask)
    {
        string include = mask._include.Length == 0 ? "*" : string.Join("; ", mask._include.Select(DescribeTerm));
        return include + (mask._exclude.Length == 0 ? "" : " | exclude " + string.Join("; ", mask._exclude.Select(DescribeTerm)));
    }

    private static string DescribeTerm(MaskPart part) => part.Nested is { } nested ? "(" + Describe(nested) + ")"
        : part.Expression is { } expression ? "/" + expression + "/" + (expression.Options.HasFlag(RegexOptions.IgnoreCase) ? "i" : "")
        : "\"" + part.Glob + (part.DirectoriesOnly ? "/" : "") + "\"";
}
