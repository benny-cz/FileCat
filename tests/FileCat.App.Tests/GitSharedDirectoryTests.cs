using System.Diagnostics;
using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitSharedDirectoryTests
{
    [Theory]
    [InlineData(2100, false)]
    [InlineData(2100, true)]
    [InlineData(3000, false)]
    [InlineData(3000, true)]
    public async Task Oversized_shared_directory_files_are_refused_before_automatic_git(int dotSegments, bool trailingNewline)
    {
        using var fixture = new Fixture();
        fixture.CommonSpelling(dotSegments, trailingNewline);
        fixture.AddFilter();
        Assert.True(new FileInfo(fixture.CommonFile).Length > 4096);

        var snapshot = await GitStatusReader.ReadAsync(fixture.Linked, TestContext.Current.CancellationToken, fixture.GitExecutable);

        Assert.Equal("owned modified bytes\n", File.ReadAllText(Path.Join(fixture.Linked, "tracked.txt")));
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new { dotSegments, trailingNewline, SnapshotPresent = snapshot is not null, CommonBytes = new FileInfo(fixture.CommonFile).Length, OwnedContentUnchanged = true }));
        Assert.Null(snapshot);
        Assert.Null(GitStatusReader.SafeRepository(fixture.Linked));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2000)]
    [InlineData(2045)]
    public async Task Ordinary_shared_directory_spellings_within_the_budget_keep_badges(int dotSegments)
    {
        using var fixture = new Fixture();
        fixture.CommonSpelling(dotSegments, trailingNewline: true);
        Assert.True(new FileInfo(fixture.CommonFile).Length <= 4096);

        var snapshot = await GitStatusReader.ReadAsync(fixture.Linked, TestContext.Current.CancellationToken, fixture.GitExecutable);

        Assert.NotNull(snapshot);
        Assert.Equal(GitStatusKind.Modified, snapshot.ForName("tracked.txt"));
        Assert.Equal("owned modified bytes\n", File.ReadAllText(Path.Join(fixture.Linked, "tracked.txt")));
    }

    [Fact]
    public async Task A_small_shared_directory_still_checks_the_shared_filter_configuration()
    {
        using var fixture = new Fixture();
        fixture.AddFilter();

        Assert.Null(await GitStatusReader.ReadAsync(fixture.Linked, TestContext.Current.CancellationToken, fixture.GitExecutable));
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Join(Path.GetTempPath(), "filecat-git-shared-directory-tests");
        internal string Root { get; } = Directory.CreateDirectory(Path.Join(Parent, Guid.NewGuid().ToString("N"))).FullName;
        internal string Source => Path.Join(Root, "source");
        internal string Linked => Path.Join(Root, "linked");
        internal string CommonFile => Path.Join(Source, ".git", "worktrees", "linked", "commondir");
        internal string GitExecutable { get; }

        internal Fixture()
        {
            GitExecutable = GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) ?? "";
            if (GitExecutable.Length == 0) { Directory.Delete(Root); Assert.Skip("Git is required for actual shared-metadata worktree controls."); return; }
            Directory.CreateDirectory(Source);
            Git("init", "--initial-branch=owned-control");
            File.WriteAllText(Path.Join(Source, ".gitattributes"), "tracked.txt filter=owned\n");
            File.WriteAllText(Path.Join(Source, "tracked.txt"), "owned original bytes\n");
            Git("add", ".gitattributes", "tracked.txt");
            Git("commit", "-m", "Owned local control");
            Git("worktree", "add", "--detach", Linked);
            File.WriteAllText(Path.Join(Linked, "tracked.txt"), "owned modified bytes\n");
            Assert.Equal(new FileInfo(Path.Join(Source, "tracked.txt")).Length, new FileInfo(Path.Join(Linked, "tracked.txt")).Length);
        }

        internal void CommonSpelling(int dotSegments, bool trailingNewline) => File.WriteAllText(CommonFile,
            string.Concat(Enumerable.Repeat("./", dotSegments)) + "../.." + (trailingNewline ? "\n" : ""));

        internal void AddFilter() => Git("config", "filter.owned.clean", "cat"); // An inert byte-preserving control.

        private void Git(params string[] arguments)
        {
            var start = new ProcessStartInfo(GitExecutable) { WorkingDirectory = Source, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            start.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
            start.Environment["GIT_NO_LAZY_FETCH"] = "1";
            foreach (string argument in new[] { "-c", "user.name=Owned control", "-c", "user.email=owned@example.invalid", "-c", "core.autocrlf=false" }.Concat(arguments)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();var stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(10000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(exited && process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }

        public void Dispose()
        {
            Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Path.GetFullPath(Root));
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
