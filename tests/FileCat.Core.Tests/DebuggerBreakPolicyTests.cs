using System.Text.RegularExpressions;

namespace FileCat.Core.Tests;

// Debugger.Break and Debugger.Launch are debugging aids. Without an attached debugger, a breakpoint left in shipped code stops
// the process or asks to attach a debugger, depending on the platform. SharpCompress's 7z reader carried two, both unreachable,
// until 23 November 2026; this keeps new ones out of FileCat's sources, bundled third-party code included.
public sealed class DebuggerBreakPolicyTests
{
    private static readonly Regex Call = new(@"\bDebugger\s*\.\s*(?:Break|Launch)\s*\(", RegexOptions.CultureInvariant);

    [Fact]
    public void No_FileCat_source_calls_Debugger_Break_or_Launch()
    {
        string? root = RepositoryRoot();
        if (root is null)
        {
            Assert.Skip("The FileCat sources are not beside this test build.");
            return;
        }
        var found = new List<string>();
        foreach (string folder in new[] { "src", "tests", "eng" })
        {
            string top = Path.Combine(root, folder);
            if (!Directory.Exists(top)) continue;
            foreach (string file in Directory.EnumerateFiles(top, "*.cs", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file);
                if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is var parts && (parts.Contains("bin") || parts.Contains("obj")))
                    continue;
                int line = 0;
                foreach (string text in File.ReadLines(file))
                {
                    line++;
                    if (Call.IsMatch(text)) found.Add($"{relative}:{line}");
                }
            }
        }
        Assert.Empty(found);
    }

    [Theory]
    // Split so that this file's own text does not match the scan above.
    [InlineData("                        Debugger." + "Break();")]
    [InlineData("        System.Diagnostics.Debugger." + "Launch();")]
    [InlineData("        if (failed) Debugger . " + "Break ( );")]
    public void The_policy_recognizes_each_call_form(string line) => Assert.Matches(Call, line);

    [Theory]
    [InlineData("        if (Debugger.IsAttached) return;")]
    [InlineData("        Debugger.Log(0, null, message);")]
    [InlineData("    // Upstream broke into a debugger here.")]
    public void The_policy_leaves_other_debugger_members_and_prose_alone(string line) => Assert.DoesNotMatch(Call, line);

    private static string? RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "FileCat.Core", "FileCat.Core.csproj"))) return directory.FullName;
        return null;
    }
}
