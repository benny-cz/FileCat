using FileCat.Core.Selection;
using FileCat.Core.State;

namespace FileCat.Core.Tools;

/// <summary>
/// Per-type associations (plan §14.2, FAR's file associations): masks map to View (F3), Edit (F4), and Open (Enter)
/// programs, preserving those intents. The first matching entry wins; launching follows the TV-17 tool rules.
/// </summary>
public static class Associations
{
    public const string View = "view";
    public const string Edit = "edit";
    public const string Open = "open";

    public static bool IsIntent(string? intent) => intent is View or Edit or Open;

    public static ToolDefinition? Find(IReadOnlyList<ToolDefinition> associations, string intent, string name)
        => Find(associations, intent, name, Mask.SavedFilters);

    public static ToolDefinition? Find(IReadOnlyList<ToolDefinition> associations, string intent, string name,
        Func<string, string?>? savedFilters)
    {
        foreach (var a in associations)
        {
            if (!string.Equals(a.Intent, intent, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(a.Mask)) continue;
            if (Mask.TryParse(a.Mask, savedFilters, out var mask, out _) && mask.IsMatch(name)) return a;
        }
        return null;
    }

    /// <summary>One association per line: mask | view/edit/open | program | arguments | options. Quote exclusion masks containing a pipe.</summary>
    public static List<ToolDefinition> Parse(string? text, out string? error)
        => Parse(text, Mask.SavedFilters, out error);

    public static List<ToolDefinition> Parse(string? text, Func<string, string?>? savedFilters, out string? error)
    {
        error = null;
        var list = new List<ToolDefinition>();
        int lineNumber = 0;
        try
        {
        ToolTextGrammar.CheckLength(text ?? "");
        if (ToolTextGrammar.TryParseDefinitions(text ?? "", out var structured))
        {
            foreach (var tool in structured)
                if (!IsIntent(tool.Intent?.ToLowerInvariant()) || string.IsNullOrEmpty(tool.Mask) || !Mask.TryParse(tool.Mask, savedFilters, out _, out _))
                    throw new FormatException($"Association '{tool.Name}' needs a valid mask and view, edit, or open intent.");
            return structured;
        }
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = ToolTextGrammar.SplitFields(line, '|');
            if (parts.Count is < 3 or > 5)
            {
                throw new FormatException("Expected mask | view, edit, or open | program | arguments | options (three to five columns).");
            }
            string maskText = ToolTextGrammar.Field(parts[0]);
            if (!Mask.TryParse(maskText, savedFilters, out _, out var maskError) || maskText.Length == 0)
            {
                throw new FormatException($"\"{maskText}\" is not a valid mask{(maskError is null ? "" : ": " + maskError)}.");
            }
            var intent = ToolTextGrammar.Field(parts[1]).ToLowerInvariant();
            if (!IsIntent(intent))
            {
                throw new FormatException($"\"{intent}\": use view (F3), edit (F4), or open (Enter).");
            }
            var executable = ToolTextGrammar.Field(parts[2]);
            if (executable.Length == 0)
            {
                throw new FormatException("The program is missing.");
            }
            var tool = new ToolDefinition
            {
                // Display name from either separator: settings written on Windows stay readable everywhere.
                Name = Path.GetFileNameWithoutExtension(executable[(executable.LastIndexOfAny(['\\', '/']) + 1)..]),
                Mask = maskText,
                Intent = intent,
                Executable = executable,
                Arguments = parts.Count > 3 ? ToolTextGrammar.ParseArguments(parts[3]) : ["{file}"],
            };
            if (parts.Count > 4)
            {
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string option in ToolTextGrammar.ParseArguments(parts[4]))
                {
                    int equals = option.IndexOf('=');
                    string key = equals < 0 ? option : option[..equals];
                    string value = equals < 0 ? "" : option[(equals + 1)..];
                    if (!seen.Add(key)) throw new FormatException($"Duplicate option '{key}'.");
                    switch (key.ToLowerInvariant())
                    {
                        case "name" when equals >= 0: tool.Name = value; break;
                        case "dir" when equals >= 0: tool.WorkingDirectory = value; break;
                        case "key" when equals >= 0: tool.Hotkey = value; break;
                        case "shell" when equals < 0: tool.ShellMode = true; break;
                        default: throw new FormatException($"Unknown association option '{option}' (use name=, dir=, key=, or shell).");
                    }
                }
            }
            list.Add(tool);
        }
        }
        catch (FormatException ex) { error = $"Line {Math.Max(1, lineNumber)}: {ex.Message}"; return []; }
        return list;
    }

    public static string Format(IEnumerable<ToolDefinition> associations) => Format(associations, Mask.SavedFilters);

    public static string Format(IEnumerable<ToolDefinition> associations, Func<string, string?>? savedFilters)
    {
        var definitions = associations.ToList();
        string concise = string.Join(Environment.NewLine, definitions.Select(a =>
        {
            var options = new List<string> { "name=" + a.Name };
            if (a.WorkingDirectory != "{dir}") options.Add("dir=" + a.WorkingDirectory);
            if (a.Hotkey is not null) options.Add("key=" + a.Hotkey);
            if (a.ShellMode) options.Add("shell");
            return $"{ToolTextGrammar.QuoteField(a.Mask ?? "", name: true)} | {a.Intent} | {ToolTextGrammar.QuoteField(a.Executable)} | {ToolTextGrammar.FormatArguments(a.Arguments)} | {ToolTextGrammar.FormatArguments(options)}";
        }));
        var parsed = Parse(concise, savedFilters, out var error);
        return ToolTextGrammar.PreserveDefinitions(definitions, concise, parsed, error);
    }
}
