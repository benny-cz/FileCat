using System.Diagnostics;
using FileCat.App.Services;

namespace FileCat.App.Tests;

// These tests change only this test process's environment, never user/system configuration. Keeping the
// collection nonparallel prevents any other test from inheriting a fixture's Git environment.
[CollectionDefinition("Git environment", DisableParallelization = true)]
public sealed class GitEnvironmentCollection;

[Collection("Git environment")]
public sealed class GitEnvironmentTests
{
    [Theory]
    [InlineData("global")]
    [InlineData("system")]
    [InlineData("environment")]
    public async Task Automatic_badges_do_not_run_filters_from_outside_the_repository(string configuration)
    {
        using var fixture = new Fixture();
        string marker = Path.Join(fixture.Root, "filter-ran");
        string command = $"printf executed > '{marker.Replace('\\', '/')}'; cat";
        File.WriteAllText(Path.Join(fixture.Repository, ".gitattributes"), "*.txt filter=outside\n");
        string config = Path.Join(fixture.Root, "outside.config");
        File.WriteAllText(config, $"[filter \"outside\"]\n\tclean = {command}\n\trequired = true\n");
        switch (configuration)
        {
            case "global": Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config); break;
            case "system":
                Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "0");
                Environment.SetEnvironmentVariable("GIT_CONFIG_SYSTEM", config);
                break;
            case "environment":
                Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "2");
                Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "filter.outside.clean");
                Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", command);
                Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_1", "filter.outside.required");
                Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_1", "true");
                break;
        }
        // The direct Git control proves that this exact fixture can execute the selected command.
        fixture.Git(fixture.Repository, "status", "--porcelain=v1", "-z");
        Assert.Equal("executed", File.ReadAllText(marker));
        File.Delete(marker);
        var inherited = Fixture.GitEnvironment();

        var snapshot = await GitStatusReader.ReadAsync(fixture.Repository, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.NotNull(snapshot);
        Assert.Equal(GitStatusKind.Modified, snapshot.ForName("a.txt"));
        Assert.False(File.Exists(marker), "An automatic badge read executed the input-selected filter.");
        Assert.Equal(inherited.OrderBy(p => p.Key), Fixture.GitEnvironment().OrderBy(p => p.Key));
    }

    [Fact]
    public async Task Inherited_repository_selection_cannot_redirect_automatic_badges()
    {
        using var fixture = new Fixture();
        string other = fixture.CreateRepository("other");
        File.WriteAllText(Path.Join(other, "foreign.txt"), "foreign");
        Environment.SetEnvironmentVariable("GIT_DIR", Path.Join(other, ".git"));
        Environment.SetEnvironmentVariable("GIT_WORK_TREE", other);
        Assert.Contains("foreign.txt", fixture.Git(fixture.Repository, "status", "--porcelain=v1"));

        var snapshot = await GitStatusReader.ReadAsync(fixture.Repository, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.NotNull(snapshot);
        Assert.Equal(GitStatusKind.Modified, snapshot.ForName("a.txt"));
        Assert.Equal(GitStatusKind.None, snapshot.ForName("foreign.txt"));
    }

    [Fact]
    public async Task Inherited_tracing_cannot_create_files_during_automatic_badges()
    {
        using var fixture = new Fixture();
        string trace = Path.Join(fixture.Root, "trace.json");
        Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", trace);
        fixture.Git(fixture.Repository, "status", "--porcelain=v1");
        Assert.True(new FileInfo(trace).Length > 0);
        File.Delete(trace);

        var snapshot = await GitStatusReader.ReadAsync(fixture.Repository, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("a.txt"));
        Assert.False(File.Exists(trace), "Automatic Git wrote an inherited trace destination.");
    }

    [Fact]
    public async Task Automatic_badges_use_repository_ignore_rules_without_user_wide_rules()
    {
        using var fixture = new Fixture();
        string ignore = Path.Join(fixture.Root, "global-ignore");
        File.WriteAllText(ignore, "global.tmp\n");
        string config = Path.Join(fixture.Root, "global.config");
        File.WriteAllText(config, $"[core]\n\texcludesFile = {ignore.Replace('\\', '/')}\n");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config);
        File.WriteAllText(Path.Join(fixture.Repository, ".gitignore"), "local.tmp\n");
        File.WriteAllText(Path.Join(fixture.Repository, "global.tmp"), "global");
        File.WriteAllText(Path.Join(fixture.Repository, "local.tmp"), "local");
        Assert.DoesNotContain("global.tmp", fixture.Git(fixture.Repository, "status", "--porcelain=v1"));

        var snapshot = await GitStatusReader.ReadAsync(fixture.Repository, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.Equal(GitStatusKind.Untracked, snapshot?.ForName("global.tmp"));
        Assert.Equal(GitStatusKind.None, snapshot?.ForName("local.tmp"));
        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("a.txt"));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<string, string?> original = GitEnvironment();
        internal string Root { get; } = Path.Join(Path.GetTempPath(), "filecat-git-environment", Guid.NewGuid().ToString("N"));
        internal string GitPath { get; }
        internal string Repository { get; }
        internal Fixture()
        {
            GitPath = GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) ?? string.Empty;
            if (GitPath.Length == 0) Assert.Skip("Git is not installed.");
            try
            {
                ClearGitEnvironment();
                Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
                Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
                Repository = CreateRepository("repo");
                File.WriteAllText(Path.Join(Repository, "a.txt"), "two");
                File.SetLastWriteTimeUtc(Path.Join(Repository, "a.txt"), DateTime.UtcNow.AddSeconds(3));
            }
            catch { Dispose(); throw; }
        }
        internal string CreateRepository(string name)
        {
            string path = Directory.CreateDirectory(Path.Join(Root, name)).FullName;
            Git(path, "init", "-q");
            File.WriteAllText(Path.Join(path, "a.txt"), "one");
            Git(path, "add", "a.txt");
            Git(path, "commit", "-q", "-m", "fixture");
            return path;
        }
        internal string Git(string folder, params string[] arguments)
        {
            var start = new ProcessStartInfo(GitPath) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.autocrlf=false", "-c", "core.fsmonitor=false" }.Concat(arguments)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(15_000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(exited && process.ExitCode == 0, errors.GetAwaiter().GetResult());
            return output.GetAwaiter().GetResult();
        }
        internal static Dictionary<string, string?> GitEnvironment() => Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Where(e => ((string)e.Key).StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(e => (string)e.Key, e => e.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
        private static void ClearGitEnvironment()
        {
            foreach (string key in GitEnvironment().Keys) Environment.SetEnvironmentVariable(key, null);
        }
        public void Dispose()
        {
            ClearGitEnvironment();
            foreach (var pair in original) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            if (!Directory.Exists(Root)) return;
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
