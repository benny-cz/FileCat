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
        var sb = new StringBuilder();
        Format(tools, string.Empty, sb);
        return sb.ToString().TrimEnd();
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
            sb.Append(groups).Append(QuoteName(t.Name)).Append(" | ").Append(Quote(t.Executable)).Append(" | ")
                .Append(string.Join(" ", t.Arguments.Select(Quote)));
            var options = new List<string>();
            if (!string.IsNullOrEmpty(t.Hotkey)) options.Add("key=" + t.Hotkey);
            if (!string.IsNullOrEmpty(t.WorkingDirectory) && t.WorkingDirectory != "{dir}") options.Add(Quote("dir=" + t.WorkingDirectory));
            if (t.ShellMode) options.Add("shell");
            if (options.Count > 0) sb.Append(" | ").Append(string.Join(" ", options));
            sb.AppendLine();
        }
    }

    private static string Quote(string token) =>
        token.Length == 0 || token.AsSpan().IndexOfAny(' ', '|', '"') >= 0 ? "\"" + token.Replace("\"", "\"\"") + "\"" : token;

    /// <summary>Names may hold spaces; they are quoted only when they hold a separator or surrounding spaces.</summary>
    private static string QuoteName(string name) =>
        name.AsSpan().IndexOfAny('|', '>', '"') >= 0 || name != name.Trim() ? "\"" + name.Replace("\"", "\"\"") + "\"" : name;

    private static string UnquoteName(string segment)
    {
        var t = segment.Trim();
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1].Replace("\"\"", "\"") : t;
    }

    public static List<ToolDefinition> Parse(string? text, out string? error)
    {
        error = null;
        var root = new List<ToolDefinition>();
        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var columns = SplitOutsideQuotes(line, '|');
            if (columns.Count < 2 || columns[1].Trim().Length == 0)
            {
                error = $"\"{line}\": expected Name | program | arguments.";
                return root;
            }
            var path = SplitOutsideQuotes(columns[0], '>').Select(UnquoteName).ToArray();
            if (path.Any(p => p.Length == 0))
            {
                error = $"\"{line}\": a menu or command name is empty.";
                return root;
            }
            var tool = new ToolDefinition
            {
                Name = path[^1],
                Executable = Unquote(columns[1].Trim()),
                Arguments = columns.Count > 2 ? Tokens(columns[2]) : ["{file}"],
            };
            if (columns.Count > 3)
            {
                foreach (var option in Tokens(columns[3]))
                {
                    if (option.StartsWith("key=", StringComparison.OrdinalIgnoreCase)) tool.Hotkey = option[4..];
                    else if (option.StartsWith("dir=", StringComparison.OrdinalIgnoreCase)) tool.WorkingDirectory = option[4..];
                    else if (option.Equals("shell", StringComparison.OrdinalIgnoreCase)) tool.ShellMode = true;
                    else
                    {
                        error = $"\"{line}\": unknown option \"{option}\" (use key=, dir=, or shell).";
                        return root;
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
        return root;
    }

    /// <summary>Splits on <paramref name="separator"/> outside double quotes (quotes are kept for the next pass).</summary>
    private static List<string> SplitOutsideQuotes(string line, char separator)
    {
        var columns = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (char c in line)
        {
            if (c == '"') quoted = !quoted;
            if (c == separator && !quoted)
            {
                columns.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        columns.Add(current.ToString());
        return columns;
    }

    /// <summary>Space-separated tokens; double quotes group (and <c>""</c> inside quotes is one quote).</summary>
    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool quoted = false, any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }
                quoted = !quoted;
                any = true;
                continue;
            }
            if (c == ' ' && !quoted)
            {
                if (current.Length > 0 || any) tokens.Add(current.ToString());
                current.Clear();
                any = false;
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0 || any) tokens.Add(current.ToString());
        return tokens;
    }

    private static string Unquote(string token)
    {
        var tokens = Tokens(token);
        return tokens.Count == 1 ? tokens[0] : token.Trim('"');
    }
}
