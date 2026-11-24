using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.App.Controls;
using FileCat.App.Services;

namespace FileCat.App.Tests;

/// <summary>
/// I647: automatic badges compare files with the user's own comparison settings (Git for Windows keeps
/// core.autocrlf=true in its system file) and still honour no setting that names a program or a path (I150). Each test
/// supplies its settings where FileCat's read finds them on every runner, in the global file of the test's own HOME;
/// a global value overrides whatever the machine's system file holds. The collection runs alone: it changes HOME.
/// </summary>
[Collection("Git environment")]
public sealed class GitComparisonSettingsTests
{
    /// <summary>A clean filter for the fixture's own commands: it stores every file as "pointer", as Git LFS stores a pointer.</summary>
    private const string FixtureFilter = "filter.fake.clean=cat >/dev/null; printf pointer";

    [Theory]
    [InlineData("true", true)]
    [InlineData("input", true)]
    [InlineData("false", false)]
    public async Task Automatic_badges_compare_line_endings_as_the_users_autocrlf_says(string autocrlf, bool clean)
    {
        var expected = clean ? GitStatusKind.Clean : GitStatusKind.Modified;
        using var fixture = new Fixture($"[core]\n\tautocrlf = {autocrlf}\n");
        string repository = fixture.Repository();
        string file = Path.Join(repository, "a.txt");
        // I647's reproduction: written with CRLF and committed through core.autocrlf=true, so its blob has LF.
        File.WriteAllText(file, "one\r\ntwo\r\n");
        fixture.Git(repository, "-c", "core.autocrlf=true", "add", "a.txt");
        fixture.Git(repository, "commit", "-q", "-m", "fixture");
        // Only its time changes, as after a case-only rename, a rewrite with the same bytes or a copy back.
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        string index = fixture.IndexHash(repository);
        Assert.Equal(" M a.txt\0", fixture.Status(repository, ordinary: false));
        Assert.Equal(expected == GitStatusKind.Clean ? "" : " M a.txt\0", fixture.Status(repository, ordinary: true));

        var snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);
        var parent = await GitStatusReader.ReadAsync(fixture.Root, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.Equal(expected, snapshot?.ForName("a.txt"));
        Assert.Equal(expected, parent?.ForName("repo"));
        // Nothing refreshed the index meanwhile: FileCat's own comparison decided.
        Assert.Equal(index, fixture.IndexHash(repository));
    }

    [Fact]
    public async Task A_real_change_still_reads_changed_with_the_users_autocrlf()
    {
        using var fixture = new Fixture("[core]\n\tautocrlf = true\n");
        string repository = fixture.Repository();
        string file = Path.Join(repository, "a.txt");
        File.WriteAllText(file, "one\r\ntwo\r\n");
        fixture.Git(repository, "-c", "core.autocrlf=true", "add", "a.txt");
        fixture.Git(repository, "commit", "-q", "-m", "fixture");
        // Different contents of the same size, so only a comparison of contents can tell.
        File.WriteAllText(file, "one\r\nTWO\r\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(" M a.txt\0", fixture.Status(repository, ordinary: true));

        var snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("a.txt"));
    }

    [Fact]
    public async Task Automatic_badges_follow_the_users_file_mode_setting()
    {
        using var fixture = new Fixture("[core]\n\tfilemode = false\n");
        string repository = fixture.Repository();
        // "git init" records its own guess; without it, the user's setting decides, as in a repository another tool made.
        fixture.Git(repository, "config", "--unset", "core.filemode");
        File.WriteAllText(Path.Join(repository, "run.sh"), "echo\n");
        fixture.Git(repository, "add", "run.sh");
        fixture.Git(repository, "update-index", "--chmod=+x", "run.sh");
        fixture.Git(repository, "commit", "-q", "-m", "fixture");
        // Tracked executable, not executable on disk (never on Windows).
        Assert.Equal(" M run.sh\0", fixture.Status(repository, ordinary: false));
        Assert.Equal("", fixture.Status(repository, ordinary: true));

        var snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.Equal(GitStatusKind.Clean, snapshot?.ForName("run.sh"));
    }

    [Fact]
    public async Task The_configuration_read_passes_on_only_the_comparison_settings_at_the_value_Git_uses()
    {
        using var fixture = new Fixture(string.Empty);
        string included = Path.Join(fixture.Home, "included.config").Replace('\\', '/');
        File.WriteAllText(fixture.GlobalConfig,
            "[core]\n\tautocrlf\n\teol =\n\tsymlinks = true\n\tignorecase = false\n\tfilemode = false\n\ttrustctime = false\n" +
            "\tcheckStat = minimal\n\tprecomposeUnicode = true\n\tlongpaths = true\n" +
            // Settings that name a program or a path, which nothing passes on.
            "\tfsmonitor = true\n\thooksPath = hooks\n\tattributesFile = attributes\n\texcludesFile = ignore\n\tpager = less\n" +
            "\teditor = vi\n\taskPass = askpass\n\tsshCommand = ssh\n" +
            "[filter \"outside\"]\n\tclean = cat\n\tsmudge = cat\n\trequired = true\n" +
            "[filter \"Process\"]\n\tprocess = filter-process\n" +
            "[filter \"smudge-only\"]\n\tsmudge = cat\n" +
            "[filter \"withdrawn\"]\n\tclean = cat\n" +
            "[diff \"outside\"]\n\ttextconv = cat\n[credential]\n\thelper = store\n[alias]\n\tst = status\n" +
            $"[include]\n\tpath = {included}\n");
        // Read where it is included, after everything above: its values are the ones Git uses.
        File.WriteAllText(Path.Join(fixture.Home, "included.config"), "[core]\n\ttrustctime = true\n[filter \"withdrawn\"]\n\tclean =\n");
        string repository = fixture.Repository();
        // The repository's own value is the one Git uses there.
        File.WriteAllText(Path.Join(repository, ".git", "config"), "[core]\n\trepositoryformatversion = 0\n\tbare = false\n\tignorecase = true\n");

        var configuration = await GitStatusReader.ReadConfigurationAsync(repository, fixture.GitPath, TestContext.Current.CancellationToken);

        Assert.NotNull(configuration);
        Assert.Equal(new[]
        {
            "-c", "core.autocrlf", // given without a value: true, passed the same way
            "-c", "core.eol=",
            "-c", "core.symlinks=true",
            "-c", "core.ignorecase=true",
            "-c", "core.filemode=false",
            "-c", "core.trustctime=true",
            "-c", "core.checkstat=minimal",
            "-c", "core.precomposeunicode=true",
            "-c", "core.longpaths=true",
        }, configuration.Settings);
        Assert.Contains("outside", configuration.Filters);
        Assert.Contains("Process", configuration.Filters);
        Assert.DoesNotContain("smudge-only", configuration.Filters);
        Assert.DoesNotContain("withdrawn", configuration.Filters);
        // Real Git accepts every value passed on.
        var snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);
        Assert.NotNull(snapshot);
    }

    [Fact]
    public async Task A_change_that_needs_a_filter_automatic_badges_never_run_reads_unknown_not_changed()
    {
        using var fixture = new Fixture(string.Empty);
        string marker = Path.Join(fixture.Root, "filter-ran");
        // As Git LFS's: defined outside the repository, selected by its attributes, required. Git runs it to compare.
        // (Quoted: an unquoted ";" starts a comment in a Git configuration file.)
        File.WriteAllText(fixture.GlobalConfig, "[core]\n\tautocrlf = false\n[filter \"fake\"]\n" +
            $"\tclean = \"cat >/dev/null; printf executed > '{marker.Replace('\\', '/')}'; printf pointer\"\n\trequired = true\n");
        string repository = fixture.Repository();
        File.WriteAllText(Path.Join(repository, ".gitattributes"), "*.bin filter=fake\n");
        string model = Path.Join(repository, "model.bin"), texture = Path.Join(repository, "assets", "texture.bin");
        string notes = Path.Join(repository, "notes.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(texture)!);
        File.WriteAllText(model, "REAL CONTENT 1234");
        File.WriteAllText(texture, "REAL CONTENT 5678");
        File.WriteAllText(notes, "plain");
        fixture.Git(repository, "-c", FixtureFilter, "add", "-A");
        fixture.Git(repository, "commit", "-q", "-m", "fixture");
        // Let Git record each file's size and time, older than the index, as after any ordinary Git command.
        foreach (string file in new[] { model, texture, notes }) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(-30));
        fixture.Git(repository, "-c", FixtureFilter, "update-index", "-q", "--refresh");
        Assert.Equal("", fixture.Status(repository, ordinary: false));
        // Only their times change.
        foreach (string file in new[] { model, texture }) File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(" M assets/texture.bin\0 M model.bin\0", fixture.Status(repository, ordinary: false));
        Assert.Equal("", fixture.Status(repository, ordinary: true));
        Assert.True(File.Exists(marker), "Ordinary Git did not run the fixture's filter.");
        File.Delete(marker);
        string index = fixture.IndexHash(repository);

        var snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);
        var parent = await GitStatusReader.ReadAsync(fixture.Root, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.False(File.Exists(marker), "An automatic badge read ran a filter.");
        Assert.Equal(GitStatusKind.Unknown, snapshot?.ForName("model.bin"));
        Assert.Equal(GitStatusKind.Unknown, snapshot?.ForName("assets"));
        Assert.Equal(GitStatusKind.Clean, snapshot?.ForName("notes.txt"));
        Assert.Equal(GitStatusKind.Unknown, parent?.ForName("repo"));
        Assert.Equal(index, fixture.IndexHash(repository));

        // A size other than the one Git recorded proves a change, for Git too; an unfiltered file is compared as before.
        File.WriteAllText(model, "REAL CONTENT 12345");
        File.WriteAllText(notes, "PLAIN");
        File.SetLastWriteTimeUtc(notes, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(" M model.bin\0 M notes.txt\0", fixture.Status(repository, ordinary: true));
        File.Delete(marker);

        snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);
        parent = await GitStatusReader.ReadAsync(fixture.Root, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.False(File.Exists(marker), "An automatic badge read ran a filter.");
        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("model.bin"));
        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("notes.txt"));
        Assert.Equal(GitStatusKind.Unknown, snapshot?.ForName("assets"));
        Assert.Equal(GitStatusKind.Modified, parent?.ForName("repo"));
    }

    [Fact]
    public async Task Filters_and_textconv_in_the_users_settings_still_never_run()
    {
        using var fixture = new Fixture(string.Empty);
        string filtered = Path.Join(fixture.Root, "filter-ran"), converted = Path.Join(fixture.Root, "textconv-ran");
        File.WriteAllText(fixture.GlobalConfig, "[core]\n\tautocrlf = true\n" +
            $"[filter \"outside\"]\n\tclean = \"printf executed > '{filtered.Replace('\\', '/')}'; cat\"\n" +
            $"[diff \"outside\"]\n\ttextconv = \"printf executed > '{converted.Replace('\\', '/')}'; cat\"\n");
        string repository = fixture.Repository();
        File.WriteAllText(Path.Join(repository, ".gitattributes"), "*.txt filter=outside diff=outside\n");
        File.WriteAllText(Path.Join(repository, "a.txt"), "one");
        fixture.Git(repository, "add", "-A");
        fixture.Git(repository, "commit", "-q", "-m", "fixture");
        File.WriteAllText(Path.Join(repository, "a.txt"), "two");
        File.SetLastWriteTimeUtc(Path.Join(repository, "a.txt"), DateTime.UtcNow.AddSeconds(5));
        // Ordinary Git runs both: the filter to compare, textconv to show the difference.
        fixture.Status(repository, ordinary: true);
        fixture.Ordinary(repository, "-c", "core.fsmonitor=false", "diff");
        Assert.True(File.Exists(filtered) && File.Exists(converted), "Ordinary Git did not run the fixture's programs.");
        File.Delete(filtered);
        File.Delete(converted);

        var snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);
        var parent = await GitStatusReader.ReadAsync(fixture.Root, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.False(File.Exists(filtered), "An automatic badge read ran a filter.");
        Assert.False(File.Exists(converted), "An automatic badge read ran textconv.");
        Assert.Equal(GitStatusKind.Unknown, snapshot?.ForName("a.txt"));
        Assert.Equal(GitStatusKind.Unknown, parent?.ForName("repo"));
    }

    [Fact]
    public async Task Trace_destinations_in_the_users_settings_receive_nothing_from_automatic_badges()
    {
        using var fixture = new Fixture(string.Empty);
        string trace = Path.Join(fixture.Root, "trace.json");
        File.WriteAllText(fixture.GlobalConfig, $"[core]\n\tautocrlf = true\n[trace2]\n\teventTarget = {trace.Replace('\\', '/')}\n");
        string repository = fixture.Repository();
        fixture.Ordinary(repository, "config", "--get", "core.autocrlf");
        Assert.True(File.Exists(trace), "Ordinary Git wrote no trace.");
        File.Delete(trace);

        var snapshot = await GitStatusReader.ReadAsync(repository, TestContext.Current.CancellationToken, fixture.GitPath);

        Assert.NotNull(snapshot);
        Assert.False(File.Exists(trace), "Automatic Git wrote to a trace destination of the user's settings.");
    }

    [Fact]
    public void An_undecided_change_reads_unknown_and_ranks_above_clean_only()
    {
        var undecided = new HashSet<string>(StringComparer.Ordinal) { "big.bin", "lfs/model.bin", "both/a.bin", "also/a.bin" };
        const string tracked = "big.bin\0lfs/model.bin\0lfs/clean.txt\0both/a.bin\0both/b.txt\0also/a.bin\0";
        const string status = " M big.bin\0 M lfs/model.bin\0 M both/a.bin\0 M both/b.txt\0 M also/a.bin\0?? also/new.txt\0";

        var snapshot = GitStatusSnapshot.Parse(tracked, status, StringComparer.Ordinal, undecided);

        Assert.Equal(GitStatusKind.Unknown, snapshot.ForName("big.bin"));
        Assert.Equal(GitStatusKind.Unknown, snapshot.ForName("lfs")); // not clean for it
        Assert.Equal(GitStatusKind.Modified, snapshot.ForName("both")); // a decided change is not hidden
        Assert.Equal(GitStatusKind.Untracked, snapshot.ForName("also"));
        Assert.Equal(GitStatusKind.Unknown, GitStatusSnapshot.Summary(" M big.bin\0", undecided));
        Assert.Equal(GitStatusKind.Modified, GitStatusSnapshot.Summary(" M big.bin\0 M both/b.txt\0", undecided));
        Assert.Equal(GitStatusKind.Modified, GitStatusSnapshot.Summary(" M big.bin\0"));
        Assert.Equal("Git state unknown", FileListControl.GitStatusLabel(GitStatusKind.Unknown));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<string, string?> original = Saved();
        internal string Root { get; } = Path.Join(Path.GetTempPath(), "filecat-git-comparison", Guid.NewGuid().ToString("N"));
        internal string Home => Path.Join(Root, "home");
        internal string GlobalConfig => Path.Join(Home, ".gitconfig");
        internal string GitPath { get; }

        internal Fixture(string globalConfig)
        {
            GitPath = GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) ?? string.Empty;
            if (GitPath.Length == 0) Assert.Skip("Git is not installed.");
            try
            {
                foreach (string key in original.Keys) Environment.SetEnvironmentVariable(key, null);
                // The fixture's own commands read no system or global settings. FileCat removes these variables and finds
                // the user's settings where Git does: in HOME, which is this test's.
                Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
                Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", "/dev/null");
                Directory.CreateDirectory(Path.Join(Home, ".config"));
                File.WriteAllText(GlobalConfig, globalConfig);
                Environment.SetEnvironmentVariable("HOME", Home);
                Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", Path.Join(Home, ".config"));
            }
            catch { Dispose(); throw; }
        }

        internal string Repository()
        {
            string path = Directory.CreateDirectory(Path.Join(Root, "repo")).FullName;
            Git(path, "init", "-q");
            return path;
        }

        /// <summary>Git as the fixture runs it: without system or global settings.</summary>
        internal string Git(string folder, params string[] arguments) =>
            Run(folder, ordinary: false, ["-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.fsmonitor=false", .. arguments]);

        /// <summary>Git as it ordinarily runs, with the system file and this test's global file; the index is not rewritten.</summary>
        internal string Ordinary(string folder, params string[] arguments) => Run(folder, ordinary: true, arguments);

        /// <summary>"git status" as automatic badges ran it until I647, or as Git ordinarily runs; neither rewrites the index.</summary>
        internal string Status(string folder, bool ordinary)
        {
            string[] status = ["status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all"];
            return ordinary
                ? Run(folder, ordinary: true, ["-c", "core.fsmonitor=false", .. status])
                : Run(folder, ordinary: false, ["-c", "core.fsmonitor=false", "-c", "core.attributesFile=/dev/null", "-c", "core.excludesFile=/dev/null", .. status]);
        }

        internal string IndexHash(string repository) =>
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Join(repository, ".git", "index"))));

        private string Run(string folder, bool ordinary, string[] arguments)
        {
            var start = new ProcessStartInfo(GitPath)
            {
                WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            };
            if (ordinary)
            {
                foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
                    start.Environment.Remove(key);
                start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            }
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(15_000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(exited && process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {errors.GetAwaiter().GetResult()}");
            return output.GetAwaiter().GetResult();
        }

        private static Dictionary<string, string?> Saved() => Environment.GetEnvironmentVariables().Cast<System.Collections.DictionaryEntry>()
            .Select(e => (string)e.Key)
            .Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) || key is "HOME" or "XDG_CONFIG_HOME")
            .Concat(["HOME", "XDG_CONFIG_HOME"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(key => key, Environment.GetEnvironmentVariable, StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            foreach (string key in Saved().Keys) Environment.SetEnvironmentVariable(key, null);
            foreach (var pair in original) Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            if (!Directory.Exists(Root)) return;
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
