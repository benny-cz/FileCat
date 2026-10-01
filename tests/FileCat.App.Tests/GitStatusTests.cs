using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class GitStatusTests
{
    [Fact]
    public void Snapshot_marks_direct_children_and_aggregates_folder_changes()
    {
        var tracked = "README.md\0src/main.cs\0docs/guide.md\0clean/sub/file.txt\0";
        var status = " M src/main.cs\0?? src/new file.cs\0A  docs/new.md\0UU conflict.txt\0?? loose file.txt\0";

        var snapshot = GitStatusSnapshot.Parse(tracked, status);

        Assert.Equal(GitStatusKind.Clean, snapshot.ForName("README.md"));
        Assert.Equal(GitStatusKind.Modified, snapshot.ForName("src"));
        Assert.Equal(GitStatusKind.Added, snapshot.ForName("docs"));
        Assert.Equal(GitStatusKind.Clean, snapshot.ForName("clean"));
        Assert.Equal(GitStatusKind.Conflict, snapshot.ForName("conflict.txt"));
        Assert.Equal(GitStatusKind.Untracked, snapshot.ForName("loose file.txt"));
        Assert.Equal(GitStatusKind.None, snapshot.ForName("ignored.tmp"));
    }

    [Fact]
    public void Snapshot_consumes_both_pathnames_of_a_rename()
    {
        var snapshot = GitStatusSnapshot.Parse("old.cs\0", "R  new.cs\0old.cs\0?? unknown.cs\0");

        Assert.Equal(GitStatusKind.Modified, snapshot.ForName("new.cs"));
        Assert.Equal(GitStatusKind.Modified, snapshot.ForName("old.cs"));
        Assert.Equal(GitStatusKind.Untracked, snapshot.ForName("unknown.cs"));
    }

    /// <summary>A downloaded folder controls its repository's configuration: Git is not run where it would run its programs.</summary>
    [Fact]
    public void Repositories_whose_configuration_names_programs_get_no_badges()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-git-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string work = Path.Combine(root, "repo"), sub = Directory.CreateDirectory(Path.Combine(work, "src")).FullName;
            string config = Path.Combine(Directory.CreateDirectory(Path.Combine(work, ".git")).FullName, "config");
            const string benign = "[core]\n\trepositoryformatversion = 0\n[remote \"origin\"]\n\turl = https://example.com/x.git\n";
            File.WriteAllText(config, benign);
            Assert.Equal(work, GitStatusReader.SafeRepository(sub));

            // Clean filters run while "git status" compares files; included files could name more.
            foreach (var section in new[] { "[filter \"x\"]\n\tclean = calc.exe\n", "[Filter.x]\n\tclean = calc.exe\n", "[include]\n\tpath = ../more\n", "[includeIf \"gitdir:~/\"]\n\tpath = more\n" })
            {
                File.WriteAllText(config, benign + section);
                Assert.Null(GitStatusReader.SafeRepository(sub));
            }

            // A linked work tree's ".git" file names the repository whose configuration counts.
            File.WriteAllText(config, benign + "[filter \"x\"]\n\tclean = calc.exe\n");
            string linked = Directory.CreateDirectory(Path.Combine(root, "linked")).FullName;
            File.WriteAllText(Path.Combine(linked, ".git"), "gitdir: ../repo/.git\n");
            Assert.Null(GitStatusReader.SafeRepository(linked));
            File.WriteAllText(config, benign);
            Assert.Equal(linked, GitStatusReader.SafeRepository(linked));

            if (OperatingSystem.IsWindows())
            {
                // Release plan I16: a ".git" file or "commondir" naming a network path is not looked at, not even to see
                // whether it exists (that alone connects to the server). 203.0.113.9 is a documentation address: an
                // attempt would take seconds to fail; refusing takes none.
                Assert.False(GitStatusReader.IsLocalPath(@"\\203.0.113.9\share\repo.git"));
                Assert.False(GitStatusReader.IsLocalPath(@"\\?\UNC\203.0.113.9\share\repo.git"));
                Assert.True(GitStatusReader.IsLocalPath(Path.Combine(root, "repo", ".git")));
                File.WriteAllText(Path.Combine(linked, ".git"), @"gitdir: \\203.0.113.9\share\repo.git" + "\n");
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Assert.Null(GitStatusReader.SafeRepository(linked));
                File.WriteAllText(Path.Combine(linked, ".git"), "gitdir: ../repo/.git\n");
                File.WriteAllText(Path.Combine(work, ".git", "commondir"), @"\\203.0.113.9\share\common" + "\n");
                Assert.Null(GitStatusReader.SafeRepository(linked));
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Took {clock.Elapsed}: a network path was tried.");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Release plan V24: a downloaded repository's own configuration sends Git to the files it names, and "git status"
    /// reads them while it compares — core.excludesFile and core.attributesFile for the rules, core.worktree for the
    /// work tree, objects/info/alternates for the objects. Naming a share there makes Windows connect to it while the
    /// folder is merely shown (measured: 21.2 s each against a documentation address, which never answers), so such a
    /// repository gets no badges, decided from the text of the setting. A share is a Windows notion: on Linux and
    /// macOS "//host/share" names an ordinary local directory and reaches no server, so those cases are asked there
    /// only on Windows.
    /// </summary>
    [Fact]
    public void Repositories_whose_configuration_sends_Git_off_this_computer_get_no_badges()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-git-tests", Guid.NewGuid().ToString("N"));
        try
        {
            string work = Path.Combine(root, "repo"), sub = Directory.CreateDirectory(Path.Combine(work, "src")).FullName;
            string gitDir = Directory.CreateDirectory(Path.Combine(work, ".git")).FullName;
            string config = Path.Combine(gitDir, "config");
            string alternates = Path.Combine(Directory.CreateDirectory(Path.Combine(gitDir, "objects", "info")).FullName, "alternates");
            const string benign = "[core]\n\trepositoryformatversion = 0\n\tbare = false\n";
            File.WriteAllText(config, benign);
            Assert.Equal(work, GitStatusReader.SafeRepository(sub));

            var clock = System.Diagnostics.Stopwatch.StartNew();
            if (OperatingSystem.IsWindows())
            {
                // Settings whose value Git opens, in the spellings Git accepts (names are case-insensitive, values may
                // be quoted, a comment may follow). 203.0.113.9 is a documentation address.
                foreach (var setting in new[]
                {
                    @"excludesFile = \\203.0.113.9\share\ignore",
                    @"excludesFile = \\\\203.0.113.9\\share\\ignore", // as "git config" writes that same path
                    @"excludesfile = //203.0.113.9/share/ignore",
                    "excludesFile = \"//203.0.113.9/share/ignore\" # mine",
                    @"attributesFile = \\203.0.113.9\share\attributes",
                    @"worktree = \\203.0.113.9\share\work",
                    @"hooksPath = \\203.0.113.9\share\hooks",
                })
                {
                    File.WriteAllText(config, benign + "\t" + setting + "\n");
                    Assert.Null(GitStatusReader.SafeRepository(sub));
                }

                // The objects of another repository, which Git reads as its own; one path per line.
                File.WriteAllText(config, benign);
                Assert.Equal(work, GitStatusReader.SafeRepository(sub));
                File.WriteAllText(alternates, "../../../other/.git/objects\n" + @"\\203.0.113.9\share\repo.git\objects" + "\n");
                Assert.Null(GitStatusReader.SafeRepository(sub));
            }

            // Everywhere: an alternates file that stays on this computer is no reason to refuse a repository.
            File.WriteAllText(config, benign);
            File.WriteAllText(alternates, "../../../other/.git/objects\n");
            Assert.Equal(work, GitStatusReader.SafeRepository(sub));

            // The same settings pointing at this computer are ordinary and keep their badges.
            File.WriteAllText(config, benign + "\texcludesFile = " + Path.Combine(root, "ignore") + "\n\tworktree = ../repo\n\tpager = less -R\n");
            Assert.Equal(work, GitStatusReader.SafeRepository(sub));
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"Took {clock.Elapsed}: a path off this computer was tried.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Release plan V24 with a packet capture on the named host as the oracle: FILECAT_V24_SHARE is a host in the lab
    /// that FileCat has no reason to contact. Listing a folder of repositories, one of which points Git at
    /// \\host\share, must leave that host alone — the capture, not this test, is what proves it. FILECAT_V24_RUN_GIT=1
    /// runs Git in that repository once instead, to show on the same capture what the configuration asks for.
    /// </summary>
    [Fact]
    public async Task A_repository_that_points_Git_at_a_share_is_never_run_in()
    {
        string? host = Environment.GetEnvironmentVariable("FILECAT_V24_SHARE");
        if (host is null) { Assert.Skip("Set FILECAT_V24_SHARE to a host under capture (and FILECAT_V24_RUN_GIT=1 to show the contact)."); return; }
        if (GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) is not { } git) { Assert.Skip("Git is not installed."); return; }
        bool runGit = Environment.GetEnvironmentVariable("FILECAT_V24_RUN_GIT") == "1";
        var log = TestContext.Current.TestOutputHelper;
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-v24", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            void Git(string folder, params string[] arguments)
            {
                var start = new System.Diagnostics.ProcessStartInfo(git) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com" }.Concat(arguments)) start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!;
                process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                log?.WriteLine($"git {string.Join(' ', arguments)} in {Path.GetFileName(folder)}: exit {process.ExitCode} {errors.Trim()}");
            }
            string Repository(string name)
            {
                string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
                Git(folder, "init", "-q");
                File.WriteAllText(Path.Combine(folder, "a.txt"), "one");
                Git(folder, "add", "a.txt");
                Git(folder, "commit", "-q", "-m", "first");
                return folder;
            }
            // The control proves Git runs here at all, so a missing badge below is the policy and not a broken harness.
            Repository("ordinary");
            string hostile = Repository("downloaded");
            // Forward slashes: Git reads them as the same UNC path and needs no escaping in a configuration file. The
            // share's name is fresh each run, so no earlier answer about it can stand in for a contact.
            string setting = $"//{host}/evidence-{Guid.NewGuid():N}/ignore";
            File.AppendAllText(Path.Combine(hostile, ".git", "config"), $"[core]\n\texcludesFile = {setting}\n");
            log?.WriteLine($"downloaded/.git/config names {setting}");

            if (runGit)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                Git(hostile, "-c", "core.fsmonitor=false", "status", "--porcelain=v1", "--untracked-files=normal");
                log?.WriteLine($"Git read the configuration in {clock.Elapsed.TotalSeconds:N1} s (what FileCat used to run).");
                return;
            }

            var clock2 = System.Diagnostics.Stopwatch.StartNew();
            var snapshot = await GitStatusReader.ReadAsync(root, ct);
            var inside = await GitStatusReader.ReadAsync(hostile, ct);
            log?.WriteLine($"listed in {clock2.Elapsed.TotalSeconds:N1} s; ordinary {snapshot?.ForName("ordinary")}, downloaded {snapshot?.ForName("downloaded")}, inside it {(inside is null ? "no snapshot" : "a snapshot")}");
            Assert.Equal(GitStatusKind.Clean, snapshot?.ForName("ordinary"));
            Assert.Equal(GitStatusKind.None, snapshot?.ForName("downloaded"));
            Assert.Null(inside);
            Assert.True(clock2.Elapsed < TimeSpan.FromSeconds(20), $"Took {clock2.Elapsed}: the share was tried.");
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Git_is_found_by_full_path_and_never_through_relative_PATH_entries()
    {
        string dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-git-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string git = Path.Join(dir, OperatingSystem.IsWindows() ? "git.exe" : "git");
            File.WriteAllText(git, string.Empty);
            char sep = Path.PathSeparator;
            Assert.Equal(git, GitStatusReader.FindGit($".{sep}relative{sep}\"{dir}\""));
            Assert.Null(GitStatusReader.FindGit($".{sep}relative")); // the current directory is never searched
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Folders_that_are_repositories_show_their_work_trees_state_where_they_are_listed()
    {
        if (GitStatusReader.FindGit(Environment.GetEnvironmentVariable("PATH")) is not { } git) { Assert.Skip("Git is not installed."); return; }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-git-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            void Git(string folder, params string[] arguments)
            {
                var start = new System.Diagnostics.ProcessStartInfo(git) { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var argument in new[] { "-c", "user.name=FileCat", "-c", "user.email=filecat@example.com", "-c", "core.autocrlf=false" }.Concat(arguments)) start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!;
                process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {errors}");
            }
            string Repository(string name, bool commit)
            {
                string folder = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
                Git(folder, "init", "-q");
                if (commit)
                {
                    File.WriteAllText(Path.Combine(folder, "a.txt"), "one");
                    Git(folder, "add", "a.txt");
                    Git(folder, "commit", "-q", "-m", "first");
                }
                return folder;
            }
            Repository("clean", commit: true);
            File.WriteAllText(Path.Combine(Repository("new", commit: false), "b.txt"), "untracked");
            File.WriteAllText(Path.Combine(Repository("edited", commit: true), "a.txt"), "two");
            Directory.CreateDirectory(Path.Combine(root, "plain"));

            // The folder that lists them is in no repository: each repository's folder says how its work tree is.
            var snapshot = await GitStatusReader.ReadAsync(root, ct);
            Assert.NotNull(snapshot);
            Assert.Equal(GitStatusKind.Clean, snapshot.ForName("clean"));
            Assert.Equal(GitStatusKind.Untracked, snapshot.ForName("new"));
            Assert.Equal(GitStatusKind.Modified, snapshot.ForName("edited"));
            Assert.Equal(GitStatusKind.None, snapshot.ForName("plain"));
        }
        finally
        {
            // Git marks its objects read-only.
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task Missing_git_executable_leaves_badges_unavailable_without_failing_the_list()
    {
        string absent = Path.Join(AppContext.BaseDirectory, "missing-git-" + Guid.NewGuid().ToString("N"));

        var snapshot = await GitStatusReader.ReadAsync(AppContext.BaseDirectory, CancellationToken.None, absent);

        Assert.Null(snapshot);
    }
}
