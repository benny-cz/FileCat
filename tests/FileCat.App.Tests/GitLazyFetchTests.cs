using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
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
        await fixture.Git(git, source, "init", "-q");
        await fixture.Git(git, source, "config", "uploadpack.allowFilter", "true");
        File.WriteAllText(Path.Join(source, "a.txt"), "one");
        await fixture.Git(git, source, "add", "a.txt");
        await fixture.Git(git, source, "commit", "-q", "-m", "owned fixture");
        string tree = (await fixture.Git(git, source, "rev-parse", "HEAD^{tree}")).Trim();
        File.WriteAllText(Path.Join(source, "a.txt"), "two");
        var ordinary = await GitStatusReader.ReadAsync(source, TestContext.Current.CancellationToken, git);
        Assert.Equal(GitStatusKind.Modified, ordinary?.ForName("a.txt"));

        string partial = Path.Join(fixture.Root, "partial");
        await fixture.Git(git, source, "clone", "--no-local", "--filter=tree:0", "--no-checkout", source, partial);
        File.Copy(Path.Join(source, ".git", "index"), Path.Join(partial, ".git", "index"));
        File.WriteAllText(Path.Join(partial, "a.txt"), "two");
        string pack = Path.Join(partial, ".git", "objects", "pack");
        foreach (string index in Directory.EnumerateFiles(pack, "*.idx"))
        {
            string contents = await fixture.Git(git, partial, "verify-pack", "-v", index);
            Assert.DoesNotContain(contents.Split('\n'), line => line.StartsWith(tree + " ", StringComparison.Ordinal));
        }
        var before = PackHashes(pack);

        var automatic = await GitStatusReader.ReadAsync(partial, TestContext.Current.CancellationToken, git);

        // Refusing badges must leave the promised objects absent, not silently fetch them and then hide the badge.
        Assert.Equal(before, PackHashes(pack));
        Assert.Null(automatic);
    }

    [Fact]
    public async Task Fixture_setup_accepts_success_after_the_old_ten_second_cutoff()
    {
        string output = await RunFixtureCommand(OwnedShell(
            "Start-Sleep -Seconds 11; [Console]::Out.Write('owned success')",
            "sleep 11; printf 'owned success'"), TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        Assert.Equal("owned success", output);
    }

    [Fact]
    public async Task Fixture_setup_preserves_a_real_failure_and_both_output_streams()
    {
        var error = await Record.ExceptionAsync(() => RunFixtureCommand(OwnedShell(
            "[Console]::Out.Write('owned stdout'); [Console]::Error.Write('owned stderr'); exit 7",
            "printf 'owned stdout'; printf 'owned stderr' >&2; exit 7"), TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken));
        var failure = Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(error);
        Assert.Contains("\"ExitCode\":7", failure.Message);
        Assert.Contains("\"DeadlineExpired\":false", failure.Message);
        Assert.Contains("owned stdout", failure.Message);
        Assert.Contains("owned stderr", failure.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fixture_setup_stops_an_owned_process_on_deadline_or_cancellation(bool cancelled)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var start = OwnedShell("[Console]::Out.Write($PID); Start-Sleep -Seconds 60", "printf '%s' $$; sleep 60");
        if (cancelled) cancellation.CancelAfter(TimeSpan.FromSeconds(3));
        var error = await Record.ExceptionAsync(() => RunFixtureCommand(start,
            TimeSpan.FromSeconds(cancelled ? 60 : 3), cancellation.Token));
        var failure = Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(error);
        using var diagnostic = JsonDocument.Parse(failure.Message);
        var result = diagnostic.RootElement;
        Assert.Equal(!cancelled, result.GetProperty("DeadlineExpired").GetBoolean());
        Assert.Equal(cancelled, result.GetProperty("Cancelled").GetBoolean());
        Assert.False(result.GetProperty("ExitedNormally").GetBoolean());
        int pid = result.GetProperty("PID").GetInt32();
        // Query the OS after the helper returns: cleanup must finish before the fixture can be deleted.
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    private static ProcessStartInfo OwnedShell(string windows, string unix)
    {
        var start = new ProcessStartInfo(OperatingSystem.IsWindows()
            ? Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : "/bin/sh");
        foreach (string argument in OperatingSystem.IsWindows()
                     ? new[] { "-NoProfile", "-NonInteractive", "-Command", windows }
                     : new[] { "-c", unix }) start.ArgumentList.Add(argument);
        return start;
    }

    private static async Task<string> RunFixtureCommand(ProcessStartInfo start, TimeSpan budget, CancellationToken cancellation)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(budget);
        var clock = Stopwatch.StartNew();
        using var process = Process.Start(start)!;
        int pid = process.Id;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        bool exited = false;
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            exited = true;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        string output = await stdout, errors = await stderr;
        string diagnostic = JsonSerializer.Serialize(new
        {
            start.FileName, Arguments = start.ArgumentList.ToArray(), PID = pid, BudgetSeconds = budget.TotalSeconds,
            ElapsedSeconds = clock.Elapsed.TotalSeconds, ExitedNormally = exited, process.ExitCode,
            DeadlineExpired = !exited && !cancellation.IsCancellationRequested, Cancelled = !exited && cancellation.IsCancellationRequested,
            Stdout = output, Stderr = errors,
        });
        TestContext.Current.TestOutputHelper!.WriteLine(diagnostic);
        Assert.True(exited && process.ExitCode == 0, diagnostic);
        return output;
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

        internal Task<string> Git(string executable, string directory, params string[] arguments)
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
            // Creating the owned repository is setup, separate from FileCat's eight-second badge budget.
            return RunFixtureCommand(start, TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
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

