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
    {
        foreach (var a in associations)
        {
            if (!string.Equals(a.Intent, intent, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(a.Mask)) continue;
            if (Mask.TryParse(a.Mask, out var mask, out _) && mask.IsMatch(name)) return a;
        }
        return null;
    }

    /// <summary>One association per line: <c>mask | view/edit/open | program | arguments</c>.</summary>
    public static List<ToolDefinition> Parse(string? text, out string? error)
    {
        error = null;
        var list = new List<ToolDefinition>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length < 3)
            {
                error = $"\"{line}\": expected mask | view, edit, or open | program | arguments.";
                return list;
            }
            if (!Mask.TryParse(parts[0], out _, out var maskError) || parts[0].Length == 0)
            {
                error = $"\"{parts[0]}\" is not a valid mask{(maskError is null ? "" : ": " + maskError)}.";
                return list;
            }
            var intent = parts[1].ToLowerInvariant();
            if (!IsIntent(intent))
            {
                error = $"\"{parts[1]}\": use view (F3), edit (F4), or open (Enter).";
                return list;
            }
            if (parts[2].Length == 0)
            {
                error = $"\"{line}\": the program is missing.";
                return list;
            }
            list.Add(new ToolDefinition
            {
                Name = Path.GetFileNameWithoutExtension(parts[2].Trim('"')),
                Mask = parts[0],
                Intent = intent,
                Executable = parts[2].Trim('"'),
                Arguments = parts.Length > 3 && parts[3].Length > 0 ? parts[3].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() : ["{file}"],
            });
        }
        return list;
    }

    public static string Format(IEnumerable<ToolDefinition> associations) =>
        string.Join(Environment.NewLine, associations.Select(a => $"{a.Mask} | {a.Intent} | {a.Executable} | {string.Join(" ", a.Arguments)}"));
}
