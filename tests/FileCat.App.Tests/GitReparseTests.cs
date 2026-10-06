using System.Diagnostics;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitReparseTests
{
    [Theory]
    [InlineData("worktree")]
    [InlineData("git-directory")]
    [InlineData("linked-git-directory")]
    [InlineData("common-directory")]
    [InlineData("objects-directory")]
    public void Automatic_repository_checks_refuse_links_before_reading_beneath_them(string location)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary; Unix mounted paths need separate qualification."); return; }
        using var fixture = new Fixture();
        string selected = fixture.Repository;
        switch (location)
        {
            case "worktree":
                selected = fixture.Junction("selected", fixture.Repository);
                break;
            case "git-directory":
                string moved = Path.Join(fixture.Root, "metadata");
                Directory.Move(fixture.GitDirectory, moved);
                fixture.Junction("repo/.git", moved);
                break;
            case "linked-git-directory":
                string linked = Directory.CreateDirectory(Path.Join(fixture.Root, "linked")).FullName;
                string alias = fixture.Junction("metadata-alias", fixture.GitDirectory);
                File.WriteAllText(Path.Join(linked, ".git"), "gitdir: " + Path.GetRelativePath(linked, alias));
                selected = linked;
                break;
            case "common-directory":
                string common = Directory.CreateDirectory(Path.Join(fixture.Root, "common")).FullName;
                File.WriteAllText(Path.Join(common, "config"), "[core]\n\trepositoryformatversion = 0\n");
                string commonAlias = fixture.Junction("common-alias", common);
                File.WriteAllText(Path.Join(fixture.GitDirectory, "commondir"), Path.GetRelativePath(fixture.GitDirectory, commonAlias));
                break;
            case "objects-directory":
                string objects = Directory.CreateDirectory(Path.Join(fixture.Root, "objects", "info")).Parent!.FullName;
                File.WriteAllText(Path.Join(objects, "info", "alternates"), string.Empty);
                fixture.Junction("repo/.git/objects", objects);
                break;
        }

        Assert.Null(GitStatusReader.SafeRepository(selected));
    }

    [Fact]
    public void A_local_spelling_does_not_make_a_path_beneath_a_junction_safe_to_probe()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary."); return; }
        using var fixture = new Fixture();
        string alias = fixture.Junction("metadata-alias", fixture.GitDirectory);

        Assert.False(GitStatusReader.IsLocalPath(Path.Join(alias, "config")));
        Assert.False(GitStatusReader.IsLocalPath(Path.Join(alias, "missing", "config")));
        Assert.True(GitStatusReader.IsLocalPath(Path.Join(fixture.GitDirectory, "missing", "config")));
    }

    [Fact]
    public void Ordinary_repository_and_linked_worktree_metadata_remain_available()
    {
        using var fixture = new Fixture();
        string linked = Directory.CreateDirectory(Path.Join(fixture.Root, "linked")).FullName;
        File.WriteAllText(Path.Join(linked, ".git"), "gitdir: ../repo/.git\n");

        Assert.Equal(fixture.Repository, GitStatusReader.SafeRepository(fixture.Repository));
        Assert.Equal(linked, GitStatusReader.SafeRepository(linked));
    }

    [Theory]
    [InlineData("../target-link")]
    [InlineData("../target-link/nested")]
    [InlineData("./../target-link")]
    public void Relative_configured_worktrees_refuse_junctions(string relativeWorktree)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary; Unix mounted paths need separate qualification."); return; }
        using var fixture = new Fixture();
        string target = Directory.CreateDirectory(Path.Join(fixture.Root, "target", "nested")).Parent!.FullName;
        fixture.Junction("repo/target-link", target);
        File.AppendAllText(Path.Join(fixture.GitDirectory, "config"), "\tworktree = " + relativeWorktree + "\n");

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../../ordinary-target")]
    public void Relative_configured_worktrees_resolve_against_the_metadata_directory(string relativeWorktree)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Join(fixture.Root, "ordinary-target"));
        File.AppendAllText(Path.Join(fixture.GitDirectory, "config"), "\tworktree = " + relativeWorktree + "\n");

        Assert.Equal(fixture.Repository, GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    public void Continued_configured_worktrees_refuse_junctions(string newline, bool quoted)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary; Unix mounted paths need separate qualification."); return; }
        using var fixture = new Fixture();
        string target = Directory.CreateDirectory(Path.Join(fixture.Root, "target")).FullName;
        fixture.Junction("repo/target-link", target);
        string quote = quoted ? "\"" : string.Empty;
        File.AppendAllText(Path.Join(fixture.GitDirectory, "config"), $"\tworktree = {quote}../tar\\{newline}get-link{quote}{newline}");

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    public void Continued_ordinary_worktrees_remain_available(string newline, bool quoted)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Join(fixture.Root, "ordinary-target"));
        string quote = quoted ? "\"" : string.Empty;
        File.AppendAllText(Path.Join(fixture.GitDirectory, "config"), $"\tworktree = {quote}../../ordinary-\\{newline}target{quote}{newline}");

        Assert.Equal(fixture.Repository, GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Fact]
    public void Linked_configured_worktrees_use_the_actual_git_directory()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary; Unix mounted paths need separate qualification."); return; }
        using var fixture = new Fixture();
        string target = Directory.CreateDirectory(Path.Join(fixture.Root, "target")).FullName;
        fixture.Junction("repo/target-link", target);
        string metadata = Directory.CreateDirectory(Path.Join(fixture.GitDirectory, "worktrees", "owned")).FullName;
        File.WriteAllText(Path.Join(metadata, "commondir"), "../..\n");
        string linked = Directory.CreateDirectory(Path.Join(fixture.Root, "linked")).FullName;
        File.WriteAllText(Path.Join(linked, ".git"), "gitdir: ../repo/.git/worktrees/owned\n");
        // The shared config is read by Git, but its relative worktree is based on the linked gitdir.
        File.AppendAllText(Path.Join(fixture.GitDirectory, "config"), "\tworktree = ../../../target-link\n");

        Assert.Null(GitStatusReader.SafeRepository(linked));
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Join(Path.GetTempPath(), "filecat-git-reparse-tests", Guid.NewGuid().ToString("N"));
        internal string Repository => Path.Join(Root, "repo");
        internal string GitDirectory => Path.Join(Repository, ".git");
        private readonly List<string> links = [];

        internal Fixture()
        {
            Directory.CreateDirectory(GitDirectory);
            File.WriteAllText(Path.Join(GitDirectory, "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = false\n");
        }

        internal string Junction(string relative, string target)
        {
            string link = Path.GetFullPath(Path.Join(Root, relative));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, link);
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, Path.GetFullPath(target));
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
            return link;
        }

        public void Dispose()
        {
            // Remove each owned junction itself before recursively deleting the ordinary fixture directories.
            foreach (string link in links) Directory.Delete(link, recursive: false);
            Directory.Delete(Root, recursive: true);
        }
    }
}
