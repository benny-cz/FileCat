using System.Globalization;
using System.Text;

namespace FileCat.Core.Records;

/// <summary>One access-control entry in words, as Windows' own Security tab would say it.</summary>
/// <param name="AppliesTo">For a folder: "This folder, subfolders and files" and the like; empty for a file.</param>
public sealed record AccessEntry(string Type, string Who, string Rights, string AppliesTo, bool Inherited);

/// <summary>What a security descriptor allows, in words (D-56), and what stands out in it.</summary>
/// <param name="Dacl">What the list as a whole says when that matters (none, empty, protected from inheritance), or null.</param>
public sealed record AccessSummary(IReadOnlyList<AccessEntry> Entries, string? Dacl, IReadOnlyList<RecordFinding> Findings);

/// <summary>
/// Windows security descriptors (SDDL) in words: each entry's rights named as the Security tab names them (in folder
/// terms for a folder), where it applies, whether it is inherited, and which entries let everyone, all users, or any
/// app change the item: the weak permissions privilege escalation looks for.
/// </summary>
public static class AccessText
{
    private const uint Synchronize = 0x0010_0000;
    private const uint GenericAll = 0x1000_0000, GenericExecute = 0x2000_0000, GenericWrite = 0x4000_0000, GenericRead = 0x8000_0000;
    private const uint WriteData = 0x2, AppendData = 0x4, DeleteChild = 0x40, Delete = 0x1_0000, WriteDac = 0x4_0000, WriteOwner = 0x8_0000;

    private static readonly Dictionary<string, uint> Codes = new(StringComparer.Ordinal)
    {
        ["GA"] = GenericAll, ["GR"] = GenericRead, ["GW"] = GenericWrite, ["GX"] = GenericExecute,
        ["FA"] = 0x001F_01FF, ["FR"] = 0x0012_0089, ["FW"] = 0x0012_0116, ["FX"] = 0x0012_00A0,
        ["SD"] = Delete, ["RC"] = 0x2_0000, ["WD"] = WriteDac, ["WO"] = WriteOwner,
        // Directory-service names that SDDL also uses for the low bits.
        ["CC"] = 0x1, ["DC"] = 0x2, ["LC"] = 0x4, ["SW"] = 0x8, ["RP"] = 0x10, ["WP"] = 0x20, ["DT"] = 0x40, ["LO"] = 0x80, ["CR"] = 0x100,
    };

    /// <summary>Who counts as "anyone": everyone, all users, guests, anonymous and interactive logons, domain users, and apps in any AppContainer.</summary>
    private static readonly HashSet<string> Broad = new(StringComparer.OrdinalIgnoreCase)
    {
        "WD", "AU", "BU", "BG", "AN", "IU", "DU", "AC",
        "S-1-1-0", "S-1-5-11", "S-1-5-32-545", "S-1-5-32-546", "S-1-5-7", "S-1-5-4", "S-1-15-2-1", "S-1-15-2-2",
    };

    private static readonly (uint Bit, string File, string Folder)[] Bits =
    [
        (0x1, "read data", "list"), (0x2, "write data", "add files"), (0x4, "append", "add subfolders"), (0x8, "read EAs", "read EAs"),
        (0x10, "write EAs", "write EAs"), (0x20, "execute", "traverse"), (0x40, "delete children", "delete what is in it"),
        (0x80, "read attributes", "read attributes"), (0x100, "write attributes", "write attributes"), (Delete, "delete", "delete"),
        (0x2_0000, "read permissions", "read permissions"), (WriteDac, "change permissions", "change permissions"),
        (WriteOwner, "take ownership", "take ownership"), (0x100_0000, "system security", "system security"),
    ];

    /// <summary>The SDDL one part per line (owner, group, DACL and SACL with their flags), each ACE on a line of its own.</summary>
    public static List<string> SddlLines(string sddl) => [.. Parts(sddl).Select(p => p.IsAce ? "  " + p.Text : p.Text)];

    /// <summary>
    /// The DACL's entries (and the integrity label, from the SACL) in words. <paramref name="account"/> names a SID string
    /// or alias ("BA"), or returns null.
    /// </summary>
    public static AccessSummary Describe(string sddl, bool folder, Func<string, string?> account)
    {
        var entries = new List<AccessEntry>();
        var findings = new List<RecordFinding>();
        string? dacl = null;
        char part = ' ';
        int inDacl = 0;
        bool hasDacl = false;
        foreach (var (text, isAce) in Parts(sddl))
        {
            if (!isAce)
            {
                part = text[0];
                if (part != 'D') continue;
                hasDacl = true;
                string flags = text[2..];
                if (flags.Contains("NO_ACCESS_CONTROL", StringComparison.Ordinal))
                {
                    dacl = "none: everyone has full access";
                    findings.Add(new(true, "It has no access list at all, so everyone has full access to it."));
                }
                else if (flags.Contains('P')) dacl = "protected: it inherits nothing from its folder";
                continue;
            }
            var f = text[1..^1].Split(';');
            if (f.Length < 6) continue;
            if (part == 'S' && f[0] == "ML")
            {
                entries.Add(Label(f));
                continue;
            }
            if (part != 'D') continue;
            inDacl++;
            string who = account(f[5]) ?? f[5];
            uint? mask = Mask(f[2]);
            string type = f[0] switch { "A" => "allow", "D" => "deny", "OA" => "allow (object)", "OD" => "deny (object)", "XA" => "allow if", "XD" => "deny if", _ => f[0] };
            entries.Add(new AccessEntry(type, who, mask is { } m ? Words(m, folder) : f[2], folder ? AppliesTo(f[1]) : "", f[1].Contains("ID", StringComparison.Ordinal)));
            if (f[0] == "A" && mask is { } allowed && !f[1].Contains("IO", StringComparison.Ordinal) && Broad.Contains(f[5]))
                Weak(findings, who, Specific(allowed), folder);
        }
        if (hasDacl && inDacl == 0 && dacl is null)
        {
            dacl = "empty: no one has access (its owner can still change the permissions)";
        }
        return new AccessSummary(entries, dacl, findings);
    }

    /// <summary>What anyone may do to the item that changes it: the weak permissions privilege escalation looks for.</summary>
    private static void Weak(List<RecordFinding> findings, string who, uint mask, bool folder)
    {
        var strong = new List<string>();
        var notable = new List<string>();
        if (folder)
        {
            if ((mask & WriteData) != 0) notable.Add("add files to it");
            if ((mask & AppendData) != 0) notable.Add("add subfolders");
            if ((mask & DeleteChild) != 0) strong.Add("delete anything in it");
        }
        else if ((mask & (WriteData | AppendData)) != 0) strong.Add("change its content");
        if ((mask & Delete) != 0) (folder ? notable : strong).Add("delete it");
        if ((mask & WriteDac) != 0) strong.Add("change its permissions");
        if ((mask & WriteOwner) != 0) strong.Add("take ownership of it");
        if (strong.Count > 0) findings.Add(new(true, $"{who} can {Join([.. strong, .. notable])}."));
        else if (notable.Count > 0) findings.Add(new(false, $"{who} can {Join(notable)}."));
    }

    private static string Join(List<string> parts) => parts.Count <= 1 ? string.Concat(parts) : string.Join(", ", parts[..^1]) + " and " + parts[^1];

    private static AccessEntry Label(string[] f)
    {
        string level = f[5] switch { "LW" => "low", "ME" => "medium", "MP" => "medium plus", "HI" => "high", "SI" => "system", "UT" => "untrusted", _ => f[5] };
        var policy = new List<string>();
        if (f[2].Contains("NW", StringComparison.Ordinal)) policy.Add("no write up");
        if (f[2].Contains("NR", StringComparison.Ordinal)) policy.Add("no read up");
        if (f[2].Contains("NX", StringComparison.Ordinal)) policy.Add("no execute up");
        return new AccessEntry("integrity", level, string.Join(", ", policy), "", f[1].Contains("ID", StringComparison.Ordinal));
    }

    /// <summary>An ACE's rights as a mask: hex, or SDDL's two-letter names run together ("FRFX"); null when unknown.</summary>
    internal static uint? Mask(string rights)
    {
        if (rights.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(rights.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint hex) ? hex : null;
        if (rights.Length == 0 || rights.Length % 2 != 0) return null;
        uint mask = 0;
        for (int i = 0; i < rights.Length; i += 2)
        {
            if (!Codes.TryGetValue(rights.Substring(i, 2), out uint bits)) return null;
            mask |= bits;
        }
        return mask;
    }

    /// <summary>Generic rights as the file system maps them to its own.</summary>
    private static uint Specific(uint mask)
    {
        uint m = mask & 0x0FFF_FFFF;
        if ((mask & GenericAll) != 0) m |= 0x001F_01FF;
        if ((mask & GenericRead) != 0) m |= 0x0012_0089;
        if ((mask & GenericWrite) != 0) m |= 0x0012_0116;
        if ((mask & GenericExecute) != 0) m |= 0x0012_00A0;
        return m;
    }

    /// <summary>The rights as the Security tab names them ("modify", "read and execute"), or bit by bit.</summary>
    internal static string Words(uint mask, bool folder)
    {
        uint m = Specific(mask) & ~Synchronize;
        if ((m & 0x000F_01FF) == 0x000F_01FF) return "full control";
        string? named = m switch
        {
            0x0003_01BF => "modify",
            0x0002_01BF => folder ? "read, write, and traverse" : "read, write, and execute",
            0x0002_00A9 => folder ? "list and traverse" : "read and execute",
            0x0002_0089 => folder ? "list" : "read",
            0x0000_0116 or 0x0002_0116 => folder ? "add files and subfolders" : "write",
            0x0002_00A0 => folder ? "traverse" : "execute",
            _ => null,
        };
        if (named is not null) return named;
        var words = Bits.Where(b => (m & b.Bit) != 0).Select(b => folder ? b.Folder : b.File).ToList();
        uint known = Bits.Aggregate(0u, (all, b) => all | b.Bit);
        if ((m & ~known) != 0) words.Add($"0x{m & ~known:X}");
        return words.Count > 0 ? string.Join(", ", words) : "nothing";
    }

    /// <summary>Where a folder's entry applies, in the Security tab's words.</summary>
    internal static string AppliesTo(string flags)
    {
        bool oi = flags.Contains("OI", StringComparison.Ordinal), ci = flags.Contains("CI", StringComparison.Ordinal);
        bool io = flags.Contains("IO", StringComparison.Ordinal), np = flags.Contains("NP", StringComparison.Ordinal);
        string where = (oi, ci, io) switch
        {
            (false, false, _) => "This folder only",
            (true, true, false) => "This folder, subfolders and files",
            (false, true, false) => "This folder and subfolders",
            (true, false, false) => "This folder and files",
            (true, true, true) => "Subfolders and files only",
            (false, true, true) => "Subfolders only",
            (true, false, true) => "Files only",
        };
        return np && (oi || ci) ? where + " (one level)" : where;
    }

    /// <summary>The SDDL's parts at the top level: "O:…", "G:…", "D:flags", "S:flags", and each "(ACE)".</summary>
    private static List<(string Text, bool IsAce)> Parts(string sddl)
    {
        var parts = new List<(string, bool)>();
        var part = new StringBuilder();
        int depth = 0;
        void Flush()
        {
            if (part.Length > 0) parts.Add((part.ToString(), false));
            part.Clear();
        }
        for (int i = 0; i < sddl.Length; i++)
        {
            char ch = sddl[i];
            if (depth == 0 && ch is 'O' or 'G' or 'D' or 'S' && i + 1 < sddl.Length && sddl[i + 1] == ':') Flush();
            if (ch == '(')
            {
                if (depth == 0) Flush();
                depth++;
            }
            part.Append(ch);
            if (ch == ')' && depth > 0 && --depth == 0)
            {
                parts.Add((part.ToString(), true));
                part.Clear();
            }
        }
        Flush();
        return parts;
    }
}
