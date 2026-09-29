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
        }
        finally
        {
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
    public async Task Missing_git_executable_leaves_badges_unavailable_without_failing_the_list()
    {
        string absent = Path.Join(AppContext.BaseDirectory, "missing-git-" + Guid.NewGuid().ToString("N"));

        var snapshot = await GitStatusReader.ReadAsync(AppContext.BaseDirectory, CancellationToken.None, absent);

        Assert.Null(snapshot);
    }
}
