using System.Diagnostics;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitMetadataTreeTests
{
    [Theory]
    [InlineData("refs")]
    [InlineData("refs/heads")]
    [InlineData("objects/pack")]
    [InlineData("info")]
    [InlineData("logs/refs")]
    public void Automatic_badges_refuse_links_within_repository_metadata(string relative)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows metadata-link admission; Unix paths need separate qualification."); return; }
        using var fixture = new Fixture();
        string target = Directory.CreateDirectory(Path.Join(fixture.Root, "other-metadata")).FullName;
        fixture.Junction(Path.Join(fixture.GitDirectory, relative), target);

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Fact]
    public void An_oversized_metadata_tree_leaves_optional_badges_unavailable()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows metadata-tree admission budget."); return; }
        using var fixture = new Fixture();
        string many = Directory.CreateDirectory(Path.Join(fixture.GitDirectory, "many")).FullName;
        for (int i = 0; i < 10_001; i++) File.WriteAllText(Path.Join(many, i.ToString("D5")), string.Empty);

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Fact]
    public void Ordinary_metadata_files_and_directories_remain_available()
    {
        using var fixture = new Fixture();
        string heads = Directory.CreateDirectory(Path.Join(fixture.GitDirectory, "refs", "heads")).FullName;
        File.WriteAllText(Path.Join(heads, "main"), new string('0', 40) + "\n");
        Directory.CreateDirectory(Path.Join(fixture.GitDirectory, "objects", "pack"));
        File.WriteAllText(Path.Join(fixture.GitDirectory, "HEAD"), "ref: refs/heads/main\n");

        Assert.Equal(fixture.Repository, GitStatusReader.SafeRepository(fixture.Repository));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Join(Path.GetTempPath(), "filecat-git-metadata-tests", Guid.NewGuid().ToString("N"));
        internal string Repository => Path.Join(Root, "repo");
        internal string GitDirectory => Path.Join(Repository, ".git");
        private readonly List<string> links = [];

        internal Fixture()
        {
            Directory.CreateDirectory(GitDirectory);
            File.WriteAllText(Path.Join(GitDirectory, "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = false\n");
        }

        internal void Junction(string path, string target)
        {
            string link = Path.GetFullPath(path);
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, link);
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, Path.GetFullPath(target));
            Directory.CreateDirectory(Path.GetDirectoryName(link)!);
            var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cmd.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(process.ExitCode == 0, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
            links.Add(link);
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        }

        public void Dispose()
        {
            foreach (string link in links) Directory.Delete(link, recursive: false);
            Directory.Delete(Root, recursive: true);
        }
    }
}
