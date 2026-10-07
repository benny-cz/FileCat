using System.Diagnostics;
using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitWorktreeTreeTests
{
    [Theory]
    [InlineData("direct")]
    [InlineData("nested")]
    [InlineData("configured")]
    [InlineData("linked")]
    public async Task Automatic_badges_refuse_junctions_below_the_actual_worktree(string location)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary; Unix mounts require separate qualification."); return; }
        using var fixture = new Fixture();
        string worktree = fixture.Worktree(location);
        string target = Directory.CreateDirectory(Path.Join(fixture.Root, "target")).FullName;
        string targetFile = Path.Join(target, "target-only.txt");
        File.WriteAllText(targetFile, "owned target-only bytes\n");
        string parent = location == "nested" ? Directory.CreateDirectory(Path.Join(worktree, "nested")).FullName : worktree;
        fixture.Junction(Path.Join(parent, "target-link"), target);
        string selected = location == "configured" ? fixture.Repository : worktree;

        var snapshot = await GitStatusReader.ReadAsync(selected, TestContext.Current.CancellationToken, fixture.GitExecutable);

        Assert.Equal("owned target-only bytes\n", File.ReadAllText(targetFile));
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { location, SnapshotPresent = snapshot is not null, TargetUnchanged = true }));
        Assert.Null(snapshot);
        Assert.Null(GitStatusReader.SafeRepository(selected));
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("configured")]
    [InlineData("linked")]
    public async Task Ordinary_nested_worktrees_keep_their_actual_badges(string location)
    {
        using var fixture = new Fixture();
        string worktree = fixture.Worktree(location);
        string ordinary = Directory.CreateDirectory(Path.Join(worktree, "ordinary")).FullName;
        string member = Path.Join(ordinary, "new.txt");
        File.WriteAllText(member, "owned ordinary bytes\n");
        string selected = location == "configured" ? fixture.Repository : worktree;

        var snapshot = await GitStatusReader.ReadAsync(selected, TestContext.Current.CancellationToken, fixture.GitExecutable);

        Assert.NotNull(snapshot);
        Assert.Equal(GitStatusKind.Untracked, snapshot.ForName("ordinary"));
        Assert.Equal("owned ordinary bytes\n", File.ReadAllText(member));
    }

    [Fact]
    public void Automatic_worktree_admission_is_bounded_before_git_starts()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows worktree traversal budget; Unix mounts require separate qualification."); return; }
        using var fixture = new Fixture();
        string directory = fixture.Repository;
        for (int depth = 0; depth < 66; depth++) directory = Directory.CreateDirectory(Path.Join(directory, "d")).FullName;
        File.WriteAllText(Path.Join(directory, "leaf.txt"), "owned leaf\n");

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Join(Path.GetTempPath(), "filecat-git-worktree-tree-tests");
        internal string Root { get; } = Directory.CreateDirectory(Path.Join(Parent, Guid.NewGuid().ToString("N"))).FullName;
        internal string Repository => Path.Join(Root, "repo");
        internal string GitExecutable { get; }
        private readonly List<string> links = [];

        internal Fixture()
        {
            GitExecutable = GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) ?? "";
            if (GitExecutable.Length == 0) { Assert.Skip("Git is required for actual automatic-status worktree controls."); return; }
            Directory.CreateDirectory(Repository);
            Git(Repository, "init", "--initial-branch=owned-control");
            File.WriteAllText(Path.Join(Repository, "tracked.txt"), "owned tracked bytes\n");
            Git(Repository, "add", "tracked.txt");
            Git(Repository, "commit", "-m", "Owned local control");
        }

        internal string Worktree(string location)
        {
            if (location == "linked")
            {
                string linked = Path.Join(Root, "linked");
                Git(Repository, "worktree", "add", "--detach", linked);
                return linked;
            }
            if (location == "configured")
            {
                string configured = Directory.CreateDirectory(Path.Join(Root, "configured")).FullName;
                File.Copy(Path.Join(Repository, "tracked.txt"), Path.Join(configured, "tracked.txt"));
                Git(Repository, "config", "core.worktree", configured);
                return configured;
            }
            return Repository;
        }

        internal void Junction(string link, string target)
        {
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, Path.GetFullPath(link));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, Path.GetFullPath(target));
            Run(Path.Join(Environment.SystemDirectory, "cmd.exe"), Root, ["/d", "/c", "mklink", "/J", link, target]);
            links.Add(link);
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        }

        private void Git(string directory, params string[] arguments) => Run(GitExecutable, directory,
            ["-c", "user.name=Owned control", "-c", "user.email=owned@example.invalid", "-c", "core.autocrlf=false", .. arguments]);

        private static void Run(string executable, string directory, string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            start.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
            start.Environment["GIT_NO_LAZY_FETCH"] = "1";
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(10000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(exited && process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }

        public void Dispose()
        {
            Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Path.GetFullPath(Root));
            foreach (string link in links) { Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0); Directory.Delete(link, recursive: false); }
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
