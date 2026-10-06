using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitLazyFetchTests
{
    [Theory]
    [InlineData("[remote \"owned\"]\n\tpromisor = true\n")]
    [InlineData("[remote \"owned\"]\n\tpromisor = yes\n")]
    [InlineData("[remote \"owned\"]\n\tpromisor = ON\n")]
    [InlineData("[remote.owned]\n\tpromisor = 1\n")]
    [InlineData("[remote \"owned\"]\n\tpromisor\n")]
    [InlineData("[remote \"owned\"]\n\tpromisor = tr\"ue\"\n")]
    [InlineData("[remote \"owned\"]\n\tpartialCloneFilter = blob:none\n")]
    [InlineData("[remote \"owned\"]\n\tpartialCloneFilter\n")]
    [InlineData("[extensions]\n\tpartialClone = owned\n")]
    [InlineData("[extensions]\n\tpartialClone = \"ow\"ned\n")]
    [InlineData("[ExTeNsIoNs]\n\tPartialClone = owned\n")]
    public void Repositories_that_promise_missing_objects_are_not_automatically_read(string configuration)
    {
        using var fixture = new Fixture();
        string repository = fixture.MetadataRepository(configuration);

        Assert.Null(GitStatusReader.SafeRepository(repository));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("no")]
    [InlineData("0")]
    [InlineData("Off")]
    public void Explicitly_disabled_promisor_remotes_leave_ordinary_repositories_available(string value)
    {
        using var fixture = new Fixture();
        string repository = fixture.MetadataRepository("[remote \"owned\"]\n\tpromisor = " + value + "\n");

        Assert.Equal(repository, GitStatusReader.SafeRepository(repository));
    }

    [Fact]
    public async Task Automatic_badges_do_not_fetch_missing_partial_clone_objects()
    {
        if (GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) is not { } git)
        {
            Assert.Skip("Git is required for the owned partial-clone effect control.");
            return;
        }
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Join(fixture.Root, "source")).FullName;
        fixture.Git(git, source, "init", "-q");
        fixture.Git(git, source, "config", "uploadpack.allowFilter", "true");
        File.WriteAllText(Path.Join(source, "a.txt"), "one");
        fixture.Git(git, source, "add", "a.txt");
        fixture.Git(git, source, "commit", "-q", "-m", "owned fixture");
        string tree = fixture.Git(git, source, "rev-parse", "HEAD^{tree}").Trim();
        File.WriteAllText(Path.Join(source, "a.txt"), "two");
        var ordinary = await GitStatusReader.ReadAsync(source, TestContext.Current.CancellationToken, git);
        Assert.Equal(GitStatusKind.Modified, ordinary?.ForName("a.txt"));

        string partial = Path.Join(fixture.Root, "partial");
        fixture.Git(git, source, "clone", "--no-local", "--filter=tree:0", "--no-checkout", source, partial);
        File.Copy(Path.Join(source, ".git", "index"), Path.Join(partial, ".git", "index"));
        File.WriteAllText(Path.Join(partial, "a.txt"), "two");
        string pack = Path.Join(partial, ".git", "objects", "pack");
        foreach (string index in Directory.EnumerateFiles(pack, "*.idx"))
        {
            string contents = fixture.Git(git, partial, "verify-pack", "-v", index);
            Assert.DoesNotContain(contents.Split('\n'), line => line.StartsWith(tree + " ", StringComparison.Ordinal));
        }
        var before = PackHashes(pack);

        var automatic = await GitStatusReader.ReadAsync(partial, TestContext.Current.CancellationToken, git);

        // Refusing badges must leave the promised objects absent, not silently fetch them and then hide the badge.
        Assert.Equal(before, PackHashes(pack));
        Assert.Null(automatic);
    }

    private static KeyValuePair<string, string>[] PackHashes(string path) =>
        Directory.EnumerateFiles(path).Order(StringComparer.Ordinal).Select(file =>
            KeyValuePair.Create(Path.GetFileName(file), Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))))).ToArray();

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Join(Path.GetTempPath(), "filecat-git-lazy-tests");
        internal string Root { get; } = Directory.CreateDirectory(Path.Join(Parent, Guid.NewGuid().ToString("N"))).FullName;

        internal string MetadataRepository(string configuration)
        {
            string repository = Directory.CreateDirectory(Path.Join(Root, "repository")).FullName;
            string metadata = Directory.CreateDirectory(Path.Join(repository, ".git")).FullName;
            File.WriteAllText(Path.Join(metadata, "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = false\n" + configuration);
            return repository;
        }

        internal string Git(string executable, string directory, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string name in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(name);
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            start.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            foreach (string argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.autocrlf=false" }.Concat(arguments))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            string text = output.GetAwaiter().GetResult(), stderr = errors.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, stderr);
            return text;
        }

        public void Dispose()
        {
            Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Path.GetFullPath(Root));
            foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}

