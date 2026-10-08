using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitOptionalLaunchTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unavailable_git_leaves_repository_and_parent_badges_plain_and_recovers(bool parent, bool invalidExecutable)
    {
        using var fixture = new Fixture();
        var token = TestContext.Current.CancellationToken;
        string selected = parent ? fixture.Root : fixture.Repository;
        string child = parent ? "repo" : "tracked.txt";
        string unavailable = Path.Join(fixture.Root, OperatingSystem.IsWindows() ? "unavailable-git.exe" : "unavailable-git");
        if (invalidExecutable) File.WriteAllBytes(unavailable, "owned non-executable bytes\n"u8.ToArray());
        string[] before = fixture.InputHashes();
        Assert.Equal(GitStatusKind.Modified, (await GitStatusReader.ReadAsync(selected, token, fixture.Git))?.ForName(child));

        GitStatusSnapshot? refused = null;
        var error = await Record.ExceptionAsync(async () => { refused = await GitStatusReader.ReadAsync(selected, token, unavailable); });
        var recovered = await GitStatusReader.ReadAsync(selected, token, fixture.Git);
        string[] after = fixture.InputHashes();

        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
        {
            parent, invalidExecutable, NativeGit = fixture.Git, Selected = selected,
            ExceptionType = error?.GetType().FullName, SnapshotPresent = refused is not null,
            RecoveredBadge = recovered?.ForName(child).ToString(), Before = before, After = after,
        }));
        Assert.Equal(before, after);
        Assert.Equal(GitStatusKind.Modified, recovered?.ForName(child));
        Assert.Null(error);
        Assert.Null(refused);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_repository_and_parent_badges_keep_exact_native_state(bool parent)
    {
        using var fixture = new Fixture();
        var before = fixture.InputHashes();
        string nativeStatus = fixture.Run("status", "--porcelain=v1", "-z", "--untracked-files=normal");
        Assert.Equal(" M tracked.txt\0", nativeStatus);

        var snapshot = await GitStatusReader.ReadAsync(parent ? fixture.Root : fixture.Repository,
            TestContext.Current.CancellationToken, fixture.Git);

        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName(parent ? "repo" : "tracked.txt"));
        Assert.Equal(before, fixture.InputHashes());
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Join(Path.GetTempPath(), "filecat-git-optional-launch-tests");
        internal string Root { get; } = Directory.CreateDirectory(Path.Join(Parent, Guid.NewGuid().ToString("N"))).FullName;
        internal string Repository => Path.Join(Root, "repo");
        internal string Git { get; }

        internal Fixture()
        {
            Git = GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) ?? "";
            if (Git.Length == 0) { Directory.Delete(Root); Assert.Skip("Native Git is required for optional launch and recovery controls."); return; }
            try
            {
                Directory.CreateDirectory(Repository);
                Run("init", "-q");
                File.WriteAllText(Path.Join(Repository, "tracked.txt"), "owned original bytes\n");
                Run("add", "tracked.txt");
                Run("commit", "-q", "-m", "Owned launch control");
                File.WriteAllText(Path.Join(Repository, "tracked.txt"), "owned modified bytes\n");
                File.SetLastWriteTimeUtc(Path.Join(Repository, "tracked.txt"), DateTime.UtcNow.AddMinutes(1));
            }
            catch { Dispose(); throw; }
        }

        internal string[] InputHashes() => new[] { "tracked.txt", ".git/config", ".git/index" }
            .Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Join(Repository, p)))).ToLowerInvariant()).ToArray();

        internal string Run(params string[] arguments)
        {
            var start = new ProcessStartInfo(Git)
            {
                WorkingDirectory = Repository, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray()) start.Environment.Remove(key);
            foreach (var pair in new Dictionary<string, string>
            {
                ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_SYSTEM"] = "/dev/null", ["GIT_CONFIG_GLOBAL"] = "/dev/null",
                ["GIT_ATTR_NOSYSTEM"] = "1", ["GIT_OPTIONAL_LOCKS"] = "0", ["GIT_NO_LAZY_FETCH"] = "1",
            }) start.Environment[pair.Key] = pair.Value;
            foreach (string argument in new[] { "-c", "user.name=Owned control", "-c", "user.email=owned@example.invalid",
                "-c", "core.autocrlf=false", "-c", "core.fsmonitor=false", "-c", "core.attributesFile=/dev/null", "-c", "core.excludesFile=/dev/null" }.Concat(arguments)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(10_000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            string output = stdout.GetAwaiter().GetResult(), errors = stderr.GetAwaiter().GetResult();
            Assert.True(exited && process.ExitCode == 0, output + errors);
            return output;
        }

        public void Dispose()
        {
            Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Path.GetFullPath(Root));
            if (!Directory.Exists(Root)) return;
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
            Assert.False(Directory.Exists(Root));
        }
    }
}
