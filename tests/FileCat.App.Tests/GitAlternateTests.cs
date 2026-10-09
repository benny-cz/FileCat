using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.App.Services;

namespace FileCat.App.Tests;

// The shared-object effect control exercises optional badge admission, whose wall-clock and
// shared-worker budgets must not compete with unrelated tests in this App test process.
[CollectionDefinition("Git alternate effect", DisableParallelization = true)]
public sealed class GitAlternateEffectCollection;

[Collection("Git alternate effect")]
public sealed class GitAlternateTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("../../../target-link/objects")]
    [InlineData("\"../../../target-link/objects\"")]
    [InlineData("\"\\056\\056/\\056\\056/\\056\\056/target-link/objects\"")]
    public void Relative_alternate_object_stores_refuse_junctions(string alternate)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary; Unix mounts require separate qualification."); return; }
        using var fixture = new Fixture();
        string target = Directory.CreateDirectory(Path.Join(fixture.Root, "target", "objects")).Parent!.FullName;
        fixture.Junction("target-link", target);
        File.WriteAllText(fixture.Alternates, alternate + "\n");

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Theory]
    [InlineData("absolute")]
    [InlineData("relative")]
    [InlineData("chained")]
    public void Ordinary_local_alternate_object_stores_remain_available(string spelling)
    {
        using var fixture = new Fixture();
        string ordinary = Directory.CreateDirectory(Path.Join(fixture.Root, "ordinary", "objects", "info")).Parent!.FullName;
        string value = spelling == "absolute" ? ordinary : "../../../ordinary/objects";
        File.WriteAllText(fixture.Alternates, value + "\n");
        if (spelling == "chained")
        {
            Directory.CreateDirectory(Path.Join(fixture.Root, "second", "objects"));
            File.WriteAllText(Path.Join(ordinary, "info", "alternates"), "../../second/objects\n");
        }

        Assert.Equal(fixture.Repository, fixture.AdmitOrdinary(fixture.Repository, output));
    }

    [Theory]
    [InlineData("\"../../../ordinary/objects\"")]
    [InlineData("\"\\056\\056/\\056\\056/\\056\\056/ordinary/objects\"")]
    public void Quoted_alternate_paths_are_left_plain_when_their_C_escapes_are_not_qualified(string value)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Join(fixture.Root, "ordinary", "objects"));
        File.WriteAllText(fixture.Alternates, value + "\n");

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Theory]
    [InlineData("chained")]
    [InlineData("pack-directory")]
    public void Ordinary_alternates_cannot_hide_a_later_junction(string location)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows junction boundary; Unix mounts require separate qualification."); return; }
        using var fixture = new Fixture();
        string ordinary = Directory.CreateDirectory(Path.Join(fixture.Root, "ordinary", "objects", "info")).Parent!.FullName;
        string target = Directory.CreateDirectory(Path.Join(fixture.Root, "target", "objects", "pack")).Parent!.Parent!.FullName;
        File.WriteAllText(fixture.Alternates, "../../../ordinary/objects\n");
        if (location == "chained")
        {
            fixture.Junction("target-link", target);
            File.WriteAllText(Path.Join(ordinary, "info", "alternates"), "../../target-link/objects\n");
        }
        else fixture.Junction("ordinary/objects/pack", Path.Join(target, "objects", "pack"));

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Fact]
    public void Repeated_ordinary_alternate_directories_do_not_loop()
    {
        using var fixture = new Fixture();
        string ordinary = Directory.CreateDirectory(Path.Join(fixture.Root, "ordinary", "objects", "info")).Parent!.FullName;
        File.WriteAllText(fixture.Alternates, "../../../ordinary/objects\n../../../ordinary/objects\n");
        File.WriteAllText(Path.Join(ordinary, "info", "alternates"), "../../repo/.git/objects\n");

        Assert.Equal(fixture.Repository, fixture.AdmitOrdinary(fixture.Repository, output));
    }

    [Fact]
    public void Automatic_badges_refuse_an_oversized_alternate_graph()
    {
        using var fixture = new Fixture();
        for (int i = 0; i < 33; i++)
        {
            string objects = Directory.CreateDirectory(Path.Join(fixture.Root, "ordinary-" + i, "objects")).FullName;
            File.AppendAllText(fixture.Alternates, objects + "\n");
        }

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Theory]
    [InlineData("https://example.invalid/objects")]
    [InlineData("unqualified-relative-fetch-location")]
    public void Automatic_badges_do_not_admit_HTTP_alternate_locations(string value)
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Join(Path.GetDirectoryName(fixture.Alternates)!, "http-alternates"), value + "\n");

        Assert.Null(GitStatusReader.SafeRepository(fixture.Repository));
    }

    [Fact]
    public async Task Owned_shared_objects_keep_badges_but_a_junction_to_them_is_refused()
    {
        if (GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) is not { } git)
        {
            Assert.Skip("Git is required for the owned shared-object effect control.");
            return;
        }
        using var fixture = new Fixture();
        string source = Directory.CreateDirectory(Path.Join(fixture.Root, "source")).FullName;
        fixture.Git(git, source, "init", "-q");
        File.WriteAllText(Path.Join(source, "a.txt"), "one");
        fixture.Git(git, source, "add", "a.txt");
        fixture.Git(git, source, "commit", "-q", "-m", "owned fixture");
        string borrowed = Path.Join(fixture.Root, "borrowed");
        fixture.Git(git, source, "clone", "--shared", source, borrowed);
        File.WriteAllText(Path.Join(borrowed, "a.txt"), "two");
        File.SetLastWriteTimeUtc(Path.Join(borrowed, "a.txt"), DateTime.UtcNow.AddSeconds(3));
        string objects = Path.Join(borrowed, ".git", "objects");
        string alternates = Path.Join(objects, "info", "alternates");
        Assert.All(Directory.EnumerateFiles(objects, "*", SearchOption.AllDirectories), p => Assert.Equal(alternates, p));
        File.WriteAllText(alternates, "../../../source/.git/objects\n");
        Assert.Equal("one", fixture.Git(git, borrowed, "cat-file", "-p", "HEAD:a.txt"));

        var before = fixture.InputHashes(source, borrowed, alternates);
        var clock = Stopwatch.StartNew();
        string? admitted = fixture.AdmitOrdinary(borrowed, output);
        long admissionMilliseconds = clock.ElapsedMilliseconds;
        string nativeStatus = fixture.Git(git, borrowed, "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all", "--", ".");
        output.WriteLine(JsonSerializer.Serialize(new { Phase = "ordinary-preconditions", admitted, admissionMilliseconds,
            nativeStatus, Before = before, NonparallelAppCollection = true, ProductLimitsUnchanged = true }));
        Assert.Equal(borrowed, admitted);
        Assert.Equal(" M a.txt\0", nativeStatus);
        clock.Restart();
        var ordinary = await GitStatusReader.ReadAsync(borrowed, TestContext.Current.CancellationToken, git);
        var after = fixture.InputHashes(source, borrowed, alternates);
        output.WriteLine(JsonSerializer.Serialize(new { Phase = "ordinary-result", ElapsedMilliseconds = clock.ElapsedMilliseconds,
            SnapshotPresent = ordinary is not null, Badge = ordinary?.ForName("a.txt").ToString(), Before = before, After = after }));
        Assert.Equal(before, after);
        Assert.Equal(GitStatusKind.Modified, ordinary?.ForName("a.txt"));
        if (!OperatingSystem.IsWindows()) return; // The ordinary effect control runs everywhere; junction scope is Windows.

        fixture.Junction("source-link", source);
        File.WriteAllText(alternates, "../../../source-link/.git/objects\n");
        Assert.Equal("one", fixture.Git(git, borrowed, "cat-file", "-p", "HEAD:a.txt"));
        var junctionBefore = fixture.InputHashes(source, borrowed, alternates);
        string? junctionAdmitted = GitStatusReader.SafeRepository(borrowed);
        var automatic = await GitStatusReader.ReadAsync(borrowed, TestContext.Current.CancellationToken, git);
        var junctionAfter = fixture.InputHashes(source, borrowed, alternates);
        output.WriteLine(JsonSerializer.Serialize(new { Phase = "junction-result", junctionAdmitted,
            SnapshotPresent = automatic is not null, Before = junctionBefore, After = junctionAfter,
            NativeBorrowedBlob = fixture.Git(git, borrowed, "cat-file", "-p", "HEAD:a.txt"), OwnedLocalJunction = true }));
        Assert.Equal(junctionBefore, junctionAfter);
        Assert.Equal(before.Take(3), junctionAfter.Take(3));
        Assert.Null(junctionAdmitted);
        Assert.Null(automatic);
    }

    private sealed class Fixture : IDisposable
    {
        private static readonly string Parent = Path.Join(Path.GetTempPath(), "filecat-git-alternate-tests");
        internal string Root { get; } = Directory.CreateDirectory(Path.Join(Parent, Guid.NewGuid().ToString("N"))).FullName;
        internal string Repository => Path.Join(Root, "repo");
        internal string Alternates => Path.Join(Repository, ".git", "objects", "info", "alternates");
        private readonly List<string> links = [];

        internal Fixture()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Alternates)!);
            File.WriteAllText(Path.Join(Repository, ".git", "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = false\n");
        }

        // Optional badge admission can return null when its unchanged wall-clock budget expires.
        // Positive path/graph controls get at most three fresh admissions; refusal checks never retry.
        internal string? AdmitOrdinary(string repository, ITestOutputHelper output)
        {
            var attempts = new List<object>();
            string? admitted = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var clock = Stopwatch.StartNew();
                admitted = GitStatusReader.SafeRepository(repository);
                attempts.Add(new { Attempt = attempt, ActualAdmittedRoot = admitted, ElapsedMilliseconds = clock.ElapsedMilliseconds });
                if (admitted is not null) break;
                if (attempt < 3) Thread.Sleep(50);
            }
            output.WriteLine(JsonSerializer.Serialize(new { Phase = "bounded-positive-admission", ExpectedRepository = repository,
                ActualAttempts = attempts, MaximumAttempts = 3, ProductLimitsUnchanged = true, RefusalChecksNeverRetry = true,
                NullReasonNotInferredFromElapsedTime = true }));
            return admitted;
        }

        internal void Junction(string relative, string target)
        {
            string link = Path.GetFullPath(Path.Join(Root, relative));
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, link);
            Assert.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, Path.GetFullPath(target));
            var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cmd.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            };
            foreach (string arg in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(10000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(exited && process.ExitCode == 0, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            links.Add(link);
            Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        }

        internal string[] InputHashes(string source, string borrowed, string alternates) =>
            new[] { Path.Join(source, "a.txt"), Path.Join(borrowed, "a.txt"), Path.Join(borrowed, ".git", "index"), alternates }
                .Select(p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()).ToArray();

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
            start.Environment["GIT_NO_LAZY_FETCH"] = "1";
            foreach (string arg in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.autocrlf=false" }.Concat(arguments))
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(10000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(exited && process.ExitCode == 0, errors.GetAwaiter().GetResult());
            return output.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            Assert.StartsWith(Path.GetFullPath(Parent) + Path.DirectorySeparatorChar, Path.GetFullPath(Root));
            foreach (string link in links)
            {
                Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
                Directory.Delete(link, recursive: false);
            }
            foreach (string p in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(p, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
