using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.App.Services;
using FileCat.Core.Content;

namespace FileCat.App.Tests;

/// <summary>
/// I657: porcelain "git status" names paths from the work tree's top, whatever status.relativePaths says, while
/// "git ls-files" and the listing name them from the folder. Each folder's badges must come from its own records, below
/// the top as at it. Each test builds one repository and reads it in several folders. The collection runs alone: one
/// test changes the console's code page for a while.
/// </summary>
[Collection("Git environment")]
public sealed class GitSubfolderBadgeTests
{
    [Fact]
    public async Task A_subfolder_shows_its_own_changes()
    {
        using var fixture = new Fixture();
        Assert.Equal(" M sub/a.txt\0 M sub/deep/d.txt\0?? sub/b.txt\0?? sub/newdir/\0", fixture.Status("sub"));

        var snapshot = await fixture.Badges("sub");

        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("a.txt"));
        Assert.Equal(GitStatusKind.Clean, snapshot?.ForName("c.txt"));
        Assert.Equal(GitStatusKind.Untracked, snapshot?.ForName("b.txt"));
        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("deep"));
        Assert.Equal(GitStatusKind.Untracked, snapshot?.ForName("newdir"));
        // Named like the folder itself: rc.2 charged every change here to it.
        Assert.Equal(GitStatusKind.Clean, snapshot?.ForName("sub"));
        Assert.Equal(GitStatusKind.None, snapshot?.ForName("other"));
    }

    [Fact]
    public async Task The_top_and_a_sibling_folder_show_their_own_states()
    {
        using var fixture = new Fixture();

        var top = await fixture.Badges("");
        var other = await fixture.Badges("other");

        Assert.Equal(GitStatusKind.Clean, top?.ForName("top.txt"));
        Assert.Equal(GitStatusKind.Modified, top?.ForName("sub"));
        Assert.Equal(GitStatusKind.Modified, top?.ForName("other"));
        Assert.Equal(GitStatusKind.Modified, top?.ForName("sp ace"));
        Assert.Equal(GitStatusKind.Modified, other?.ForName("o.txt"));
        Assert.Equal(GitStatusKind.Clean, other?.ForName("p.txt"));
        Assert.Equal(GitStatusKind.None, other?.ForName("other"));
    }

    [Fact]
    public async Task A_folder_with_spaces_and_non_ASCII_names_shows_its_own_changes()
    {
        using var fixture = new Fixture();
        // FileCat makes the legacy code pages available once it decodes text. Windows .NET then reads a child's
        // output in the console's code page, 437 here, or in the ANSI code page where there is no console, as for
        // FileCat itself. Git's paths must still read as UTF-8.
        TextDecoding.EnsureCodePages();
        GitStatusSnapshot? snapshot;
        using (new LegacyConsoleCodePage()) snapshot = await fixture.Badges("sp ace/ü");

        Assert.Equal(GitStatusKind.Modified, snapshot?.ForName("x.txt"));
        Assert.Equal(GitStatusKind.Untracked, snapshot?.ForName("nový.txt"));
        Assert.Equal(GitStatusKind.Clean, snapshot?.ForName("čistý.txt"));
    }

    [Fact]
    public async Task A_folder_inside_an_untracked_folder_shows_its_files_untracked()
    {
        using var fixture = new Fixture();
        // Git reports the whole folder as one untracked record, named like the folder.
        Assert.Equal("?? sub/newdir/inner/\0", fixture.Status("sub/newdir/inner"));

        var newdir = await fixture.Badges("sub/newdir");
        var inner = await fixture.Badges("sub/newdir/inner");

        Assert.Equal(GitStatusKind.Untracked, newdir?.ForName("inner"));
        Assert.Equal(GitStatusKind.Untracked, newdir?.ForName("n.txt"));
        Assert.Equal(GitStatusKind.None, newdir?.ForName("ignored.log"));
        Assert.Equal(GitStatusKind.Untracked, inner?.ForName("m.txt"));
    }

    /// <summary>On Windows with a console, its output code page set to 437 for a while, then restored.</summary>
    private sealed class LegacyConsoleCodePage : IDisposable
    {
        [DllImport("kernel32.dll")]
        private static extern uint GetConsoleOutputCP();

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleOutputCP(uint codePage);

        private readonly uint _previous;

        internal LegacyConsoleCodePage()
        {
            if (OperatingSystem.IsWindows() && (_previous = GetConsoleOutputCP()) != 0) SetConsoleOutputCP(437);
        }

        public void Dispose()
        {
            if (_previous != 0) SetConsoleOutputCP(_previous);
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Join(Path.GetTempPath(), "filecat-git-subfolders", Guid.NewGuid().ToString("N"));
        internal string Repository => Path.Join(Root, "repo");
        private string GitPath { get; }

        internal Fixture()
        {
            GitPath = GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) ?? string.Empty;
            if (GitPath.Length == 0) Assert.Skip("Git is not installed.");
            try
            {
                Directory.CreateDirectory(Repository);
                Git("init", "-q");
                Write(".gitignore", "*.log\n");
                Write("top.txt", "one");
                Write("sub/a.txt", "one");
                Write("sub/c.txt", "one");
                Write("sub/deep/d.txt", "one");
                Write("sub/sub/s.txt", "one");
                Write("other/o.txt", "one");
                Write("other/p.txt", "one");
                Write("sp ace/ü/x.txt", "one");
                Write("sp ace/ü/čistý.txt", "one");
                Git("add", "-A");
                Git("commit", "-q", "-m", "fixture");
                // Changed contents of another size, so no file time matters; new files and an untracked folder.
                Write("sub/a.txt", "two!");
                Write("sub/deep/d.txt", "two!");
                Write("other/o.txt", "two!");
                Write("sp ace/ü/x.txt", "two!");
                Write("sub/b.txt", "new");
                Write("sp ace/ü/nový.txt", "new");
                Write("sub/newdir/n.txt", "new");
                Write("sub/newdir/ignored.log", "ignored");
                Write("sub/newdir/inner/m.txt", "new");
            }
            catch { Dispose(); throw; }
        }

        private void Write(string path, string text)
        {
            string full = Path.Join(Repository, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        /// <summary>A folder's snapshot, read again only while absent (a busy runner can pass the 8 s deadline, I405).</summary>
        internal async Task<GitStatusSnapshot?> Badges(string folder)
        {
            string path = Path.GetFullPath(Path.Join(Repository, folder));
            GitStatusSnapshot? snapshot = null;
            for (int attempt = 1; attempt <= 3 && snapshot is null; attempt++)
            {
                if (attempt > 1) await Task.Delay(100, TestContext.Current.CancellationToken);
                snapshot = await GitStatusReader.ReadAsync(path, TestContext.Current.CancellationToken, GitPath);
                TestContext.Current.TestOutputHelper?.WriteLine($"{folder}, attempt {attempt}: {(snapshot is null ? "no snapshot" : "a snapshot")}");
            }
            return snapshot;
        }

        /// <summary>Porcelain status as automatic badges run it, in a folder of the repository.</summary>
        internal string Status(string folder) => Git(Path.Join(Repository, folder),
            ["-c", "core.fsmonitor=false", "-c", "status.relativePaths=true", "status", "--porcelain=v1", "-z", "--untracked-files=normal", "--ignore-submodules=all", "--", "."]);

        private string Git(params string[] arguments) => Git(Repository, arguments);

        private string Git(string folder, string[] arguments)
        {
            var start = new ProcessStartInfo(GitPath)
            {
                WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new System.Text.UTF8Encoding(false),
            };
            // The fixture's own runs read no system or global settings, so every runner builds the same repository.
            foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
                start.Environment.Remove(key);
            start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            start.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
            start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            foreach (string argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.autocrlf=false" }.Concat(arguments))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(15_000);
            if (!exited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            Assert.True(exited && process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {errors.GetAwaiter().GetResult()}");
            return output.GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            if (!Directory.Exists(Root)) return;
            foreach (string path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
    }
}
