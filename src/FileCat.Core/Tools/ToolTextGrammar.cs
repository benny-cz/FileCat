using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.State;

namespace FileCat.Core.Tools;

/// <summary>
/// Non-executing Settings grammar. Double quotes group fields/arguments; doubled quotes represent a literal quote.
/// Backslashes stay literal. An explicit @json: string/array represents embedded line breaks losslessly.
/// </summary>
public static class ToolTextGrammar
{
    public const int MaximumTextLength = 1024 * 1024;
    private const string JsonPrefix = "@json:";
    private const string DefinitionsPrefix = "@tools:";
    private static readonly JsonSerializerOptions DefinitionJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 96,
    };

    // The concise legacy form cannot represent every valid saved structure (for example empty or
    // repeated submenu names, or a group's optional metadata). Use an explicit lossless form only
    // when the concise representation would change a value or ordering.
    internal static string PreserveDefinitions(IReadOnlyList<ToolDefinition> original, string concise,
        IReadOnlyList<ToolDefinition> parsed, string? error)
    {
        string json = JsonSerializer.Serialize(original, DefinitionJson);
        return error is null && json == JsonSerializer.Serialize(parsed, DefinitionJson)
            ? concise : DefinitionsPrefix + json;
    }

    internal static bool TryParseDefinitions(string text, out List<ToolDefinition> definitions)
    {
        definitions = [];
        string trimmed = text.Trim();
        if (!trimmed.StartsWith(DefinitionsPrefix, StringComparison.Ordinal)) return false;
        try
        {
            definitions = JsonSerializer.Deserialize<List<ToolDefinition>>(trimmed[DefinitionsPrefix.Length..], DefinitionJson)
                ?? throw new FormatException("The @tools: value must be an array of tool definitions.");
            Validate(definitions, 0);
            return true;
        }
        catch (JsonException ex)
        {
            throw new FormatException($"Invalid @tools: definitions at JSON line {(ex.LineNumber ?? 0) + 1}, column {(ex.BytePositionInLine ?? 0) + 1}.");
        }

        static void Validate(IEnumerable<ToolDefinition> tools, int depth)
        {
            if (depth > 31) throw new FormatException("Use at most 31 submenu levels.");
            foreach (var tool in tools)
            {
                if (tool is null || string.IsNullOrEmpty(tool.Name) || tool.Executable is null || tool.WorkingDirectory is null ||
                    tool.Arguments is null || tool.Arguments.Any(a => a is null))
                    throw new FormatException("Each tool needs a name, string program and working directory, and an array of string arguments.");
                if (tool.Children is { } children) Validate(children, depth + 1);
                else if (string.IsNullOrWhiteSpace(tool.Executable)) throw new FormatException($"The program is missing for '{tool.Name}'.");
            }
        }
    }

    public static string FormatArguments(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        return values.Any(HasLineBreak) ? JsonPrefix + JsonSerializer.Serialize(values) : string.Join(" ", values.Select(QuoteArgument));
    }

    public static bool TryParseArguments(string? text, out List<string> arguments, out string? error)
    {
        try { arguments = ParseArguments(text ?? ""); error = null; return true; }
        catch (FormatException ex) { arguments = []; error = ex.Message; return false; }
    }

    public static bool TryParseField(string? text, out string value, out string? error)
    {
        try { CheckLength(text ?? ""); value = Field(text ?? ""); error = null; return true; }
        catch (FormatException ex) { value = ""; error = ex.Message; return false; }
    }

    public static string QuoteField(string value, bool name = false)
    {
        if (HasLineBreak(value)) return JsonPrefix + JsonSerializer.Serialize(value);
        bool quote = value.Length == 0 || value != value.Trim() || value.Contains('"') || value.Contains('|') ||
            !name && value.Any(char.IsWhiteSpace) || name && (value.Contains('>') || value.StartsWith('#')) ||
            value.StartsWith(JsonPrefix, StringComparison.Ordinal) || value.StartsWith(DefinitionsPrefix, StringComparison.Ordinal);
        return quote ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"' : value;
    }

    public static string QuoteArgument(string value) => value.Length == 0 || value.Any(char.IsWhiteSpace) ||
        value.IndexOfAny(['|', '"']) >= 0 || value.StartsWith(JsonPrefix, StringComparison.Ordinal)
            ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"' : value;

    public static void CheckLength(string text)
    {
        if (text.Length > MaximumTextLength) throw new FormatException($"The field exceeds its {MaximumTextLength:N0}-character editing limit.");
    }

    internal static List<string> SplitFields(string text, char separator)
    {
        CheckLength(text);
        var fields = new List<string>();
        int start = 0, quoteStart = -1;
        bool quoted = false;
        bool json = text.AsSpan().TrimStart().StartsWith(JsonPrefix, StringComparison.Ordinal);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted && json && c == '\\') { i++; continue; }
            if (c == '"')
            {
                if (quoted && !json && i + 1 < text.Length && text[i + 1] == '"') { i++; continue; }
                quoted = !quoted;
                if (quoted) quoteStart = i;
            }
            else if (!quoted && c == separator)
            {
                fields.Add(text[start..i]);
                start = i + 1;
                json = text.AsSpan(start).TrimStart().StartsWith(JsonPrefix, StringComparison.Ordinal);
            }
        }
        if (quoted) throw new FormatException($"Column {quoteStart + 1}: unmatched double quote.");
        fields.Add(text[start..]);
        return fields;
    }

    internal static string Field(string text)
    {
        string value = text.Trim();
        if (value.StartsWith(JsonPrefix, StringComparison.Ordinal))
        {
            try { return JsonSerializer.Deserialize<string>(value[JsonPrefix.Length..]) ?? throw new FormatException("A field must be a string, not null."); }
            catch (JsonException) { throw new FormatException("Invalid @json: string field."); }
        }
        if (!value.Contains('"')) return value;
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            throw new FormatException("Quote the entire field; use doubled quotes for a literal quote.");
        var tokens = ParseArguments(value);
        if (tokens.Count != 1) throw new FormatException("Expected one quoted field.");
        return tokens[0];
    }

    internal static List<string> ParseArguments(string text)
    {
        CheckLength(text);
        string trimmed = text.Trim();
        if (trimmed.StartsWith(JsonPrefix, StringComparison.Ordinal))
        {
            try
            {
                var values = JsonSerializer.Deserialize<List<string>>(trimmed[JsonPrefix.Length..]);
                if (values is null || values.Any(v => v is null)) throw new FormatException("An @json: argument array must contain strings only.");
                return values;
            }
            catch (JsonException) { throw new FormatException("Invalid @json: argument array; supply a JSON array of strings."); }
        }
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, present = false;
        int quoteStart = -1;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { current.Append('"'); i++; continue; }
                quoted = !quoted;
                if (quoted) quoteStart = i;
                present = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (present) tokens.Add(current.ToString());
                current.Clear(); present = false;
            }
            else { current.Append(c); present = true; }
        }
        if (quoted) throw new FormatException($"Column {quoteStart + 1}: unmatched double quote.");
        if (present) tokens.Add(current.ToString());
        return tokens;
    }

    private static bool HasLineBreak(string text) => text.IndexOfAny(['\r', '\n']) >= 0;
}
