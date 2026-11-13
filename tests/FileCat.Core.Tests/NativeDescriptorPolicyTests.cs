using System.Text.RegularExpressions;

namespace FileCat.Core.Tests;

// dup2 and dup3 onto a chosen number first close whatever that number holds, silently. In FileCat's processes, and in this
// shared, parallel test host, another thread may hold it: on 13 November 2026 MacArchiveProcessTests' dup2 onto 200 replaced
// a 7z read-back test's archive with a 17-byte canary in mid-read, and that test failed (codecId 6 with a compressed header).
// Duplicate with fcntl F_DUPFD / F_DUPFD_CLOEXEC and a minimum, or with dup: both take the lowest free number.
// posix_spawn_file_actions_adddup2 acts in the child only and stays allowed.
public sealed class NativeDescriptorPolicyTests
{
    private static readonly Regex Declaration = new(@"EntryPoint\s*=\s*""dup[23]""|\b(?:extern|partial)\s+int\s+dup[23]\s*\(",
        RegexOptions.CultureInvariant);

    [Fact]
    public void No_FileCat_source_declares_dup2_or_dup3()
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
                    if (Declaration.IsMatch(text)) found.Add($"{relative}:{line}");
                }
            }
        }
        Assert.Empty(found);
    }

    [Theory]
    [InlineData("    [LibraryImport(\"libc\", EntryPoint = \"dup2\", SetLastError = true)]")]
    [InlineData("    [DllImport(\"libc\", EntryPoint=\"dup3\")]")]
    // Split so that this file's own text does not match the scan above.
    [InlineData("    private static extern int " + "dup2(int fd, int target);")]
    [InlineData("    private static partial int " + "dup3(int fd, int target, int flags);")]
    public void The_policy_recognizes_each_declaration_form(string line) => Assert.Matches(Declaration, line);

    [Theory]
    [InlineData("    [LibraryImport(\"libc\", EntryPoint = \"posix_spawn_file_actions_adddup2\")]")]
    [InlineData("    [LibraryImport(\"libc\", EntryPoint = \"dup\", SetLastError = true)]")]
    [InlineData("    // Never dup2 onto a fixed number.")]
    public void The_policy_leaves_spawn_actions_dup_and_prose_alone(string line) => Assert.DoesNotMatch(Declaration, line);

    private static string? RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src", "FileCat.Core", "FileCat.Core.csproj"))) return directory.FullName;
        return null;
    }
}
