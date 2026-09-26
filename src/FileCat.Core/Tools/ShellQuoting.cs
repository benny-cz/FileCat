using System.Text;

namespace FileCat.Core.Tools;

/// <summary>
/// Quotes a name for insertion into a command line of a specific shell (plan §14.2, TV-17). Quoting is
/// shell-specific because Windows has no argument vector: every program parses its own command line.
/// </summary>
public static class ShellQuoting
{
    public static string Quote(string text, string shell) => shell.ToLowerInvariant() switch
    {
        "powershell" or "pwsh" => QuotePowerShell(text),
        "posix" or "bash" or "sh" or "zsh" => QuotePosix(text),
        _ => OperatingSystem.IsWindows() ? QuoteCmd(text) : QuotePosix(text),
    };

    /// <summary>
    /// cmd.exe: double quotes protect spaces and <c>&amp; | &lt; &gt; ^ ( ) , ;</c>; a percent sign is escaped as
    /// <c>^%</c> outside the quotes so <c>%NAME%</c> inside a file name is never expanded.
    /// Windows file names cannot contain double quotes.
    /// </summary>
    public static string QuoteCmd(string text)
    {
        if (text.Length > 0 && text.IndexOfAny([' ', '&', '|', '<', '>', '^', '(', ')', ',', ';', '=', '%', '!', '\t', '\'', '`']) < 0)
            return text;
        var sb = new StringBuilder();
        var parts = text.Split('%');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sb.Append("^%");
            if (parts[i].Length > 0) sb.Append('"').Append(parts[i]).Append('"');
        }
        return sb.Length == 0 ? "\"\"" : sb.ToString();
    }

    /// <summary>
    /// PowerShell: single-quoted literal. Both ASCII and the typographic single quotes PowerShell also
    /// treats as quote characters (U+2018–U+201B) are doubled.
    /// </summary>
    public static string QuotePowerShell(string text)
    {
        var sb = new StringBuilder("'");
        foreach (var c in text)
        {
            sb.Append(c);
            if (c is '\'' or '‘' or '’' or '‚' or '‛') sb.Append(c);
        }
        return sb.Append('\'').ToString();
    }

    /// <summary>POSIX shells: single-quoted literal, embedded single quote as <c>'\''</c>.</summary>
    public static string QuotePosix(string text)
    {
        if (text.Length > 0 && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '/' or '+' or ':' or '@') && text[0] != '-')
            return text;
        return "'" + text.Replace("'", "'\\''") + "'";
    }
}
