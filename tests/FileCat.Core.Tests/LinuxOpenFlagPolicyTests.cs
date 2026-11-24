using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using FileCat.Core.Analysis;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// I656: Linux gives O_DIRECTORY and O_NOFOLLOW, and so O_TMPFILE, other values on ARM64 than on x64, and ARM64 reads
/// x64's as O_DIRECT and O_LARGEFILE. Every Linux open takes them from <see cref="LinuxOpenFlags"/>. These tests check the
/// sites' flags for both architectures on any host (x64's are the values rc.2 spelled), and keep the numbers from coming
/// back into the sources. <see cref="LinuxOpenFlagBehaviourTests"/> opens through the sites on the host's architecture.
/// </summary>
public sealed class LinuxOpenFlagPolicyTests
{
    [Fact]
    public void LinuxOpenFlags_gives_the_kernels_values_for_each_architecture()
    {
        // arch/x86 uses asm-generic/fcntl.h; arch/arm64/include/uapi/asm/fcntl.h redefines these two.
        Assert.Equal((0x10000, 0x20000), LinuxOpenFlags.ForArchitecture(Architecture.X64));
        Assert.Equal((0x4000, 0x8000), LinuxOpenFlags.ForArchitecture(Architecture.Arm64));
        Assert.Equal(0x80000, LinuxOpenFlags.CloseOnExec);
    }

    [Fact]
    public void On_x64_every_site_keeps_the_flags_rc2_spelled()
    {
        const Architecture x64 = Architecture.X64;
        Assert.Equal(2048 | 524288 | 131072, HtmlPage.LinuxResourceOpenFlags(x64));
        Assert.Equal(0x200000 | 0x80000 | 0x20000, UnixAnalysisBackend.LinuxRootOpenFlags(x64));
        Assert.Equal(0x80000 | 0x10000 | 0x20000, UnixAnalysisBackend.LinuxEntryOpenFlags(directory: true, x64));
        Assert.Equal(0x200000 | 0x80000 | 0x20000, UnixAnalysisBackend.LinuxEntryOpenFlags(directory: false, x64));
        Assert.Equal(0x10000 | 0x80000 | 0x20000, UnixAnalysisPrivateDirectory.LinuxDirectoryFlags(x64));
        Assert.Equal(0x80000 | 0x20000 | 0x800, UnixAnalysisPrivateDirectory.LinuxFileFlags(x64));
        Assert.Equal(0x410000 | 0x80000 | 2, UnixAnalysisPrivateDirectory.LinuxTemporaryFileFlags(x64));
    }

    [Fact]
    public void On_arm64_every_site_uses_its_own_values_and_none_of_x64s()
    {
        const Architecture arm64 = Architecture.Arm64;
        const int Directory = 0x4000, NoFollow = 0x8000, CloseOnExec = 0x80000;
        var flags = new (string Site, int Actual, int Expected)[]
        {
            ("page resource", HtmlPage.LinuxResourceOpenFlags(arm64), 0x800 | CloseOnExec | NoFollow),
            ("analysis root", UnixAnalysisBackend.LinuxRootOpenFlags(arm64), 0x200000 | CloseOnExec | NoFollow),
            ("analysis folder", UnixAnalysisBackend.LinuxEntryOpenFlags(directory: true, arm64), CloseOnExec | Directory | NoFollow),
            ("analysis entry", UnixAnalysisBackend.LinuxEntryOpenFlags(directory: false, arm64), 0x200000 | CloseOnExec | NoFollow),
            ("private folder", UnixAnalysisPrivateDirectory.LinuxDirectoryFlags(arm64), Directory | CloseOnExec | NoFollow),
            ("private file", UnixAnalysisPrivateDirectory.LinuxFileFlags(arm64), CloseOnExec | NoFollow | 0x800),
            ("unnamed file", UnixAnalysisPrivateDirectory.LinuxTemporaryFileFlags(arm64), 0x400000 | Directory | CloseOnExec | 2),
        };
        foreach (var (site, actual, expected) in flags)
        {
            Assert.True(expected == actual, $"{site}: 0x{actual:X} instead of 0x{expected:X}");
            // ARM64's O_DIRECT (a folder refuses it) and O_LARGEFILE (which openat2 refuses beside O_PATH).
            Assert.True((actual & 0x30000) == 0, $"{site} carries x64's value 0x{actual & 0x30000:X}");
        }
    }

    [Fact]
    public void No_FileCat_source_spells_a_Linux_open_flag_that_differs_by_architecture_as_a_number()
    {
        string? root = RepositoryRoot();
        if (root is null)
        {
            Assert.Skip("The FileCat sources are not beside this test build.");
            return;
        }
        var found = new List<string>();
        int scanned = 0;
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!Scanned(relative)) continue;
            scanned++;
            foreach (int line in NumericSpellings(File.ReadAllText(file)))
                found.Add($"{relative}:{line}");
        }
        Assert.True(scanned > 500, $"Only {scanned} source files were scanned.");
        Assert.True(found.Count == 0, "Take these flags from LinuxOpenFlags:\n" + string.Join('\n', found));
    }

    // The spellings rc.2 used (I656), each of which the scan must find.
    [Theory]
    [InlineData("            : 2048 | 524288 | 131072;")]
    [InlineData("        int noFollow = OperatingSystem.IsMacOS() ? 256 : 131072;")]
    [InlineData("        _root = Open(canonical, _mac ? 0x1000000 | 0x8000 | 0x100 | 0x4 : 0x200000 | 0x80000 | 0x20000);")]
    [InlineData("            var how = new OpenHow { Flags = directory ? 0x80000ul | 0x10000 | 0x20000 : 0x200000ul | 0x80000 | 0x20000,")]
    [InlineData("    private int DirectoryFlags => _mac ? 0x100000 | 0x1000000 | 0x100 | 4 : 0x10000 | 0x80000 | 0x20000;")]
    [InlineData("        private int ReadFlags => _mac ? 0x1000000 | 0x100 | 4 : 0x80000 | 0x20000 | 0x800;")]
    [InlineData("            owned = OpenOwned(Root, \".\", 0x410000 | 0x80000 | 2 | (unpublishable ? 0x80 : 0), 0x180); // 0600")]
    [InlineData("        int noFollow = RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.Arm ? 0x8000 : 0x20000;")]
    // Other spellings of the same mistake: ARM64's values, digit separators and suffixes, a LinuxOpenFlags neighbour.
    [InlineData("        int fd = Open(path, 0x200000 | 0x80000 | 0x8000);")]
    [InlineData("        int fd = Open(path, LinuxOpenFlags.Current.Directory | 0x2_0000u);")]
    [InlineData("        int fd = Open(path, 0x400000 | 0x10000 | LinuxOpenFlags.CloseOnExec | 2);")]
    [InlineData("    private const int LinuxNoFollow = 0x00020000;")]
    public void The_scan_finds_each_numeric_spelling(string line) => Assert.NotEmpty(NumericSpellings(line));

    // Darwin's flags, other kernels' flag tables, clone flags and LinuxOpenFlags' own users stay allowed.
    [Theory]
    [InlineData("    private const int MacImageOpenFlags = 0x4 | 0x100 | 0x20000 | 0x1000000 | 0x20000000;")]
    [InlineData("    private const int MacDirectoryFlags = 0x00100000 | 0x00008000 | 0x00000100 | 0x01000000; // O_DIRECTORY | O_EVTONLY")]
    [InlineData("                else flags = (flags & ~0x100) | 0x200000 | 0x8000; // O_SYMLINK, not O_NOFOLLOW")]
    [InlineData("            (0x8000, 't', \"no tail merging\"), (0x20000, 'T', \"top of a hierarchy\"), (0x80000, 'e', \"extents\"),")]
    [InlineData("        const uint ThreadFlags = 0x100 | 0x200 | 0x400 | 0x800 | 0x10000 | 0x40000 | 0x80000 | 0x100000 | 0x1000000;")]
    [InlineData("    private const int MaxDirectoryClusters = 65536;")]
    [InlineData("    private const int SearchNoFollow = 0x61100000; // O_SEARCH | O_CLOEXEC | O_NOFOLLOW_ANY")]
    [InlineData("        int fd = Open(path, 0x800 | 0x100 | 0x80000 | LinuxOpenFlags.Current.NoFollow);")]
    [InlineData("        return directory ? LinuxOpenFlags.CloseOnExec | folder | noFollow : 0x200000 | LinuxOpenFlags.CloseOnExec | noFollow;")]
    [InlineData("        // x64 spells O_NOFOLLOW 0x20000 | O_CLOEXEC 0x80000, which ARM64 reads as O_LARGEFILE.")]
    [InlineData("        string text = \"0x80000 | 0x20000\";")]
    public void The_scan_leaves_other_flags_alone(string line) => Assert.Empty(NumericSpellings(line));

    private static bool Scanned(string relative)
    {
        string[] parts = relative.Split('/');
        if (parts.Contains("bin") || parts.Contains("obj")) return false;
        // Third-party code and the Windows platform make no Linux opens; LinuxOpenFlags is where the values live.
        return !relative.StartsWith("src/ThirdParty/", StringComparison.Ordinal) &&
               !relative.StartsWith("src/FileCat.Platform.Windows/", StringComparison.Ordinal) &&
               relative != "src/FileCat.Core/FileSystem/LinuxOpenFlags.cs";
    }

    // O_DIRECTORY, O_NOFOLLOW and O_TMPFILE: x64's, then ARM64's.
    private static readonly HashSet<long> ArchitectureValues = [0x10000, 0x20000, 0x410000, 0x4000, 0x8000, 0x404000];

    // Every Linux open flag lies below this bit; Darwin's O_CLOEXEC and the clone flags reach it.
    private const long BeyondLinuxOpenFlags = 0x800000;

    private static readonly Regex Lexeme = new(
        @"(?<hex>0[xX][0-9A-Fa-f_]+)[uUlL]*" +
        @"|(?<real>[0-9][0-9_]*\.[0-9][0-9_]*(?:[eE][+-]?[0-9]+)?[fFdDmM]?)" +
        @"|(?<dec>[0-9][0-9_]*)[uUlL]*" +
        @"|(?<name>[A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*)" +
        @"|(?<op>\|\||\|=|=>|==|!=|<=|>=|&&|\S)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The lines where a Linux open flag that differs by architecture is written as a number: in a chain of flags joined with
    /// | that also holds Linux's O_CLOEXEC (0x80000) or a LinuxOpenFlags value, or in the value given to a name that says
    /// no-follow. A chain with a value from beyond Linux's open flags is something else, such as Darwin's flags.
    /// </summary>
    internal static SortedSet<int> NumericSpellings(string code)
    {
        string text = WithoutCommentsAndStrings(code);
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lineStarts.Add(i + 1);
        int Line(int index)
        {
            int found = lineStarts.BinarySearch(index);
            return (found >= 0 ? found : ~found - 1) + 1;
        }

        var tokens = new List<(string Kind, string Text, long? Value, int Line)>();
        foreach (Match match in Lexeme.Matches(text))
        {
            string kind = match.Groups["hex"].Success ? "number" : match.Groups["dec"].Success ? "number"
                : match.Groups["name"].Success ? "name" : match.Groups["real"].Success ? "real" : "op";
            long? value = null;
            if (match.Groups["hex"].Success && long.TryParse(match.Groups["hex"].Value[2..].Replace("_", ""), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out long hex)) value = hex;
            else if (match.Groups["dec"].Success && long.TryParse(match.Groups["dec"].Value.Replace("_", ""), NumberStyles.None,
                    CultureInfo.InvariantCulture, out long dec)) value = dec;
            string spelled = kind == "name" ? Regex.Replace(match.Value, @"\s+", "") : match.Value;
            tokens.Add((kind, spelled, value, Line(match.Index)));
        }

        var findings = new SortedSet<int>();
        var chain = new List<(string Kind, string Text, long? Value, int Line)>();
        bool afterBar = false;
        void Finish()
        {
            if (chain.Count > 1 && !chain.Any(t => t.Value >= BeyondLinuxOpenFlags) &&
                chain.Any(t => t.Value == 0x80000 || t.Kind == "name" && LinuxName(t.Text)))
                foreach (var token in chain)
                    if (token.Value is { } value && ArchitectureValues.Contains(value)) findings.Add(token.Line);
            chain.Clear();
        }
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Kind is "number" or "name")
            {
                if (!afterBar) Finish();
                chain.Add(token);
                afterBar = false;
                // A name that says no-follow, given a value: everything up to the end of that declaration.
                if (token.Kind == "name" && i + 1 < tokens.Count && tokens[i + 1].Text is "=" or "=>" && NoFollowName(token.Text))
                {
                    int depth = 0;
                    for (int j = i + 2; j < tokens.Count; j++)
                    {
                        string t = tokens[j].Text;
                        if (t is "(" or "[" or "{") depth++;
                        else if (t is ")" or "]" or "}") depth--;
                        if (depth < 0 || depth == 0 && t is ";" or ",") break;
                        if (tokens[j].Value is { } value && ArchitectureValues.Contains(value)) findings.Add(tokens[j].Line);
                    }
                }
            }
            else if (token.Text == "|") afterBar = true;
            else
            {
                Finish();
                afterBar = false;
            }
        }
        Finish();
        return findings;
    }

    private static bool LinuxName(string name) =>
        name.StartsWith("LinuxOpenFlags.", StringComparison.Ordinal) ||
        name.StartsWith("Linux", StringComparison.Ordinal) && name.EndsWith(".CloseOnExec", StringComparison.Ordinal);

    private static bool NoFollowName(string name)
    {
        string last = name[(name.LastIndexOf('.') + 1)..];
        return last.Contains("nofollow", StringComparison.OrdinalIgnoreCase) &&
               !last.StartsWith("mac", StringComparison.OrdinalIgnoreCase) && !last.StartsWith("darwin", StringComparison.OrdinalIgnoreCase);
    }

    // Blanks comments, strings and characters, keeping every line break so that line numbers hold.
    private static string WithoutCommentsAndStrings(string code)
    {
        var result = new StringBuilder(code.Length);
        int i = 0;
        void Skip(int end)
        {
            for (; i < end && i < code.Length; i++) result.Append(code[i] == '\n' ? '\n' : ' ');
        }
        while (i < code.Length)
        {
            char c = code[i];
            char next = i + 1 < code.Length ? code[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                int end = code.IndexOf('\n', i);
                Skip(end < 0 ? code.Length : end);
            }
            else if (c == '/' && next == '*')
            {
                int end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                Skip(end < 0 ? code.Length : end + 2);
            }
            else if (c == '"' && code.AsSpan(i).StartsWith("\"\"\""))
            {
                int end = code.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                Skip(end < 0 ? code.Length : end + 3);
            }
            else if (c == '"' || c == '\'')
            {
                bool verbatim = c == '"' && i > 0 && (code[i - 1] == '@' || i > 1 && code[i - 1] == '$' && code[i - 2] == '@');
                int j = i + 1;
                while (j < code.Length && code[j] != '\n')
                {
                    if (!verbatim && code[j] == '\\') { j += 2; continue; }
                    if (code[j] == c)
                    {
                        if (verbatim && j + 1 < code.Length && code[j + 1] == '"') { j += 2; continue; }
                        break;
                    }
                    j++;
                }
                Skip(Math.Min(j + 1, code.Length));
            }
            else
            {
                result.Append(c);
                i++;
            }
        }
        return result.ToString();
    }

    private static string? RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "FileCat.Core", "FileCat.Core.csproj"))) return directory.FullName;
        return null;
    }
}
