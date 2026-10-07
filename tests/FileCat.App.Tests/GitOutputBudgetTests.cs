using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitOutputBudgetTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(20, false)]
    [InlineData(20, true)]
    [InlineData(80000, false)]
    [InlineData(80000, true)]
    public async Task Automatic_badges_bound_real_git_diagnostics_and_recover(int lines, bool parent)
    {
        using var fixture = new Fixture();
        byte[] attributes = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("* a/b\n", lines)));
        File.WriteAllBytes(fixture.Attributes, attributes);
        var before = fixture.InputHashes();
        var control = await fixture.StatusControl();
        Assert.Equal(0, control.ExitCode);
        Assert.True(control.StdoutBytes < 100);
        Assert.Equal(lines, control.StderrLines);
        Assert.Equal(lines == 80000, control.StderrBytes > 4_000_000);
        Assert.Equal(fixture.Repository, GitStatusReader.SafeRepository(fixture.Repository));

        var snapshot = await GitStatusReader.ReadAsync(parent ? fixture.Root : fixture.Repository,
            TestContext.Current.CancellationToken, fixture.Git);
        var after = fixture.InputHashes();
        File.WriteAllText(fixture.Attributes, "* text\n");
        var recovered = await GitStatusReader.ReadAsync(parent ? fixture.Root : fixture.Repository,
            TestContext.Current.CancellationToken, fixture.Git);
        string child = parent ? "repo" : "tracked.txt";
        TestContext.Current.TestOutputHelper!.WriteLine(JsonSerializer.Serialize(new
        {
            lines, parent, NativeGit = fixture.Git, control.ExitCode, control.StdoutBytes, control.StderrBytes,
            control.StderrLines, control.StderrSHA256, AttributeBytes = attributes.Length,
            AttributeSHA256 = Convert.ToHexString(SHA256.HashData(attributes)).ToLowerInvariant(),
            Before = before, After = after, SnapshotPresent = snapshot is not null,
            Badge = snapshot?.ForName(child).ToString(), RecoveredBadge = recovered?.ForName(child).ToString(),
            OwnedLocalGitNoFiltersExternalNetworkPhysicalSourceOrNativeGUI = true,
        }));
        Assert.Equal(before, after);
        Assert.Equal(GitStatusKind.Modified, recovered?.ForName(child));
        if (lines == 80000) Assert.Null(snapshot);
        else
        {
            Assert.NotNull(snapshot);
            Assert.Equal(GitStatusKind.Modified, snapshot.ForName(child));
        }
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Join(Path.GetTempPath(), "filecat-git-output-budget-tests");
        internal string Root { get; } = Directory.CreateDirectory(Path.Join(Parent, Guid.NewGuid().ToString("N"))).FullName;
        internal string Repository => Path.Join(Root, "repo");
        internal string Attributes => Path.Join(Repository, ".gitattributes");
        internal string Git { get; }

        internal Fixture()
        {
            Git = GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) ?? "";
            if (Git.Length == 0) { Directory.Delete(Root); Assert.Skip("Git is required for native diagnostic-budget controls."); return; }
            try
            {
                Directory.CreateDirectory(Repository);
                Run("init", "-q");
                File.WriteAllText(Path.Join(Repository, "tracked.txt"), "owned original bytes\n");
                Run("add", "tracked.txt");
                Run("commit", "-q", "-m", "Owned diagnostic control");
                File.WriteAllText(Path.Join(Repository, "tracked.txt"), "owned modified bytes\n");
                File.SetLastWriteTimeUtc(Path.Join(Repository, "tracked.txt"), DateTime.UtcNow.AddMinutes(1));
            }
            catch { Dispose(); throw; }
        }

        internal string[] InputHashes() => new[] { "tracked.txt", ".gitattributes", ".git/config", ".git/index" }
            .Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Join(Repository, p)))).ToLowerInvariant()).ToArray();

        private ProcessStartInfo Start(params string[] arguments)
        {
            var start = new ProcessStartInfo(Git)
            {
                WorkingDirectory = Repository, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            foreach (var item in new Dictionary<string, string>
            {
                ["GIT_CONFIG_NOSYSTEM"] = "1", ["GIT_CONFIG_SYSTEM"] = "/dev/null", ["GIT_CONFIG_GLOBAL"] = "/dev/null",
                ["GIT_ATTR_NOSYSTEM"] = "1", ["GIT_OPTIONAL_LOCKS"] = "0", ["GIT_NO_LAZY_FETCH"] = "1",
            }) start.Environment[item.Key] = item.Value;
            foreach (string argument in new[] { "-c", "user.name=Owned control", "-c", "user.email=owned@example.invalid",
                "-c", "core.autocrlf=false", "-c", "core.fsmonitor=false", "-c", "status.relativePaths=true",
                "-c", "core.attributesFile=/dev/null", "-c", "core.excludesFile=/dev/null" }.Concat(arguments))
                start.ArgumentList.Add(argument);
            return start;
        }

        private void Run(params string[] arguments)
        {
            using var child = Process.Start(Start(arguments))!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            bool exited = child.WaitForExit(10_000);
            if (!exited) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
            Assert.True(exited && child.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
        }

        internal async Task<(int ExitCode, int StdoutBytes, long StderrBytes, int StderrLines, string StderrSHA256)> StatusControl()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            using var child = Process.Start(Start("status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all", "--", "."))!;
            var stdout = child.StandardOutput.ReadToEndAsync(timeout.Token);
            // The control counts/hashes diagnostics without retaining their text.
            var stderr = Count(child.StandardError.BaseStream, timeout.Token);
            try
            {
                await child.WaitForExitAsync(timeout.Token);
                var diagnostic = await stderr;
                return (child.ExitCode, Encoding.UTF8.GetByteCount(await stdout), diagnostic.Bytes, diagnostic.Lines, diagnostic.SHA256);
            }
            finally
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
                try { await stdout; await stderr; }
                catch (OperationCanceledException) { }
            }
        }

        private static async Task<(long Bytes, int Lines, string SHA256)> Count(Stream stream, CancellationToken token)
        {
            byte[] buffer = new byte[8192];
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long bytes = 0; int lines = 0, n;
            while ((n = await stream.ReadAsync(buffer, token)) > 0)
            {
                hash.AppendData(buffer, 0, n);
                bytes += n;
                for (int i = 0; i < n; i++) if (buffer[i] == (byte)'\n') lines++;
            }
            return (bytes, lines, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }

        public void Dispose()
        {
            Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Path.GetFullPath(Root));
            if (!Directory.Exists(Root)) return;
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
            Assert.False(Directory.Exists(Root));
        }
    }
}
