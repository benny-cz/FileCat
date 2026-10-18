using System.Text;
using FileCat.Core.State;

namespace FileCat.Core.Tools;

/// <summary>
/// The Settings text form of the user commands (F9), one command per line and lossless:
/// <c>Group &gt; Name | program | arguments | options</c>. Groups become submenus (nested with more <c>&gt;</c>);
/// arguments are separated by spaces, with double quotes around an argument that contains spaces or <c>|</c>;
/// options are <c>key=HOTKEY</c>, <c>dir=FOLDER</c>, and <c>shell</c>. Lines starting with <c>#</c> are comments.
/// </summary>
public static class UserCommandsText
{
    public static string Format(IEnumerable<ToolDefinition> tools)
    {
        var definitions = tools.ToList();
        var sb = new StringBuilder();
        Format(definitions, string.Empty, sb);
        string concise = sb.ToString().TrimEnd();
        var parsed = Parse(concise, out var error);
        return ToolTextGrammar.PreserveDefinitions(definitions, concise, parsed, error);
    }

    private static void Format(IEnumerable<ToolDefinition> tools, string groups, StringBuilder sb)
    {
        foreach (var t in tools)
        {
            if (t.Children is { Count: > 0 } children)
            {
                Format(children, groups + QuoteName(t.Name) + " > ", sb);
                continue;
            }
            sb.Append(groups).Append(QuoteName(t.Name)).Append(" | ").Append(ToolTextGrammar.QuoteField(t.Executable)).Append(" | ")
                .Append(ToolTextGrammar.FormatArguments(t.Arguments));
            var options = new List<string>();
            if (t.Hotkey is not null) options.Add("key=" + t.Hotkey);
            if (t.WorkingDirectory != "{dir}") options.Add("dir=" + t.WorkingDirectory);
            if (t.ShellMode) options.Add("shell");
            if (options.Count > 0) sb.Append(" | ").Append(ToolTextGrammar.FormatArguments(options));
            sb.AppendLine();
        }
    }

    private static string QuoteName(string name) => ToolTextGrammar.QuoteField(name, name: true);

    private static string UnquoteName(string segment) => ToolTextGrammar.Field(segment);

    public static List<ToolDefinition> Parse(string? text, out string? error)
    {
        error = null;
        var root = new List<ToolDefinition>();
        int lineNumber = 0;
        try
        {
        ToolTextGrammar.CheckLength(text ?? "");
        if (ToolTextGrammar.TryParseDefinitions(text ?? "", out var structured)) return structured;
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var columns = SplitOutsideQuotes(line, '|');
            if (columns.Count is < 2 or > 4 || columns[1].Trim().Length == 0)
            {
                throw new FormatException("expected Name | program | arguments | options (two to four columns); unexpected columns are not ignored.");
            }
            var path = SplitOutsideQuotes(columns[0], '>').Select(UnquoteName).ToArray();
            if (path.Length > 32) throw new FormatException("Use at most 31 submenu levels.");
            if (path.Any(p => p.Length == 0))
            {
                throw new FormatException("A menu or command name is empty.");
            }
            var tool = new ToolDefinition
            {
                Name = path[^1],
                Executable = Unquote(columns[1].Trim()),
                Arguments = columns.Count > 2 ? Tokens(columns[2]) : ["{file}"],
            };
            if (string.IsNullOrWhiteSpace(tool.Executable)) throw new FormatException("The program is missing.");
            if (columns.Count > 3)
            {
                var seenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var option in Tokens(columns[3]))
                {
                    string optionKey = option.Split('=', 2)[0];
                    if (!seenOptions.Add(optionKey)) throw new FormatException($"Duplicate option '{optionKey}'.");
                    if (option.StartsWith("key=", StringComparison.OrdinalIgnoreCase)) tool.Hotkey = option[4..];
                    else if (option.StartsWith("dir=", StringComparison.OrdinalIgnoreCase)) tool.WorkingDirectory = option[4..];
                    else if (option.Equals("shell", StringComparison.OrdinalIgnoreCase)) tool.ShellMode = true;
                    else
                    {
                        throw new FormatException($"unknown option \"{option}\" (use key=, dir=, or shell).");
                    }
                }
            }
            var level = root;
            foreach (var group in path[..^1])
            {
                var menu = level.FirstOrDefault(t => t.Children is not null && t.Name == group);
                if (menu is null)
                {
                    menu = new ToolDefinition { Name = group, Children = [] };
                    level.Add(menu);
                }
                level = menu.Children!;
            }
            level.Add(tool);
        }
        }
        catch (FormatException ex) { error = $"Line {Math.Max(1, lineNumber)}: {ex.Message}"; return []; }
        return root;
    }

    private static List<string> SplitOutsideQuotes(string text, char separator) => ToolTextGrammar.SplitFields(text, separator);
    private static List<string> Tokens(string text) => ToolTextGrammar.ParseArguments(text);
    private static string Unquote(string text) => ToolTextGrammar.Field(text);
}
