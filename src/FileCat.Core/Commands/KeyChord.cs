using System.Text;

namespace FileCat.Core.Commands;

[Flags]
public enum KeyMods
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

/// <summary>
/// A portable key chord. <see cref="Key"/> uses canonical names that match Avalonia's <c>Key</c> enum where
/// possible (F5, PageUp, Add, D1...). Parsing accepts friendly aliases ("PgUp", "Num+", "Del", "1").
/// </summary>
public readonly record struct KeyChord(string Key, KeyMods Mods)
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PgUp"] = "PageUp", ["PgDn"] = "PageDown", ["PageDn"] = "PageDown", ["Del"] = "Delete", ["Ins"] = "Insert",
        ["Esc"] = "Escape", ["Return"] = "Enter", ["Backspace"] = "Back", ["BkSp"] = "Back",
        ["Num+"] = "Add", ["Num-"] = "Subtract", ["Num*"] = "Multiply", ["Num/"] = "Divide",
        ["Gray+"] = "Add", ["Gray-"] = "Subtract", ["Gray*"] = "Multiply", ["Gray/"] = "Divide",
        ["\\"] = "Backslash", ["`"] = "Backtick", [","] = "Comma", ["Spacebar"] = "Space",
        ["0"] = "D0", ["1"] = "D1", ["2"] = "D2", ["3"] = "D3", ["4"] = "D4",
        ["5"] = "D5", ["6"] = "D6", ["7"] = "D7", ["8"] = "D8", ["9"] = "D9",
    };

    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.Ordinal)
    {
        ["PageUp"] = "PgUp", ["PageDown"] = "PgDn", ["Delete"] = "Del", ["Insert"] = "Ins", ["Escape"] = "Esc",
        ["Back"] = "Backspace", ["Add"] = "Num+", ["Subtract"] = "Num-", ["Multiply"] = "Num*", ["Divide"] = "Num/",
        ["Backslash"] = "\\", ["Backtick"] = "`", ["Comma"] = ",",
        ["D0"] = "0", ["D1"] = "1", ["D2"] = "2", ["D3"] = "3", ["D4"] = "4",
        ["D5"] = "5", ["D6"] = "6", ["D7"] = "7", ["D8"] = "8", ["D9"] = "9",
    };

    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        // "Num+" and "Ctrl+Num+" end in '+' which is also the separator.
        var parts = new List<string>();
        int start = 0;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] == '+' && i > start && i < t.Length - 1)
            {
                parts.Add(t[start..i]);
                start = i + 1;
            }
        }
        parts.Add(t[start..]);
        var mods = KeyMods.None;
        for (int i = 0; i < parts.Count - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= KeyMods.Ctrl; break;
                case "shift": mods |= KeyMods.Shift; break;
                case "alt": mods |= KeyMods.Alt; break;
                case "meta": case "cmd": case "win": case "super": mods |= KeyMods.Meta; break;
                default: return false;
            }
        }
        var key = parts[^1].Trim();
        if (key.Length == 0) return false;
        if (Aliases.TryGetValue(key, out var canonical)) key = canonical;
        else if (key.Length == 1 && char.IsAsciiLetter(key[0])) key = key.ToUpperInvariant();
        chord = new KeyChord(key, mods);
        return true;
    }

    public static KeyChord Parse(string text) =>
        TryParse(text, out var c) ? c : throw new FormatException($"Invalid key chord '{text}'.");

    public string ToDisplayString()
    {
        var sb = new StringBuilder();
        if ((Mods & KeyMods.Ctrl) != 0) sb.Append("Ctrl+");
        if ((Mods & KeyMods.Alt) != 0) sb.Append("Alt+");
        if ((Mods & KeyMods.Shift) != 0) sb.Append("Shift+");
        if ((Mods & KeyMods.Meta) != 0) sb.Append(OperatingSystem.IsMacOS() ? "Cmd+" : "Meta+");
        sb.Append(DisplayNames.TryGetValue(Key, out var d) ? d : Key);
        return sb.ToString();
    }

    public override string ToString() => ToDisplayString();

    public bool IsFunctionKey => Key.Length is 2 or 3 && Key[0] == 'F' && int.TryParse(Key.AsSpan(1), out var n) && n is >= 1 and <= 24;
}
