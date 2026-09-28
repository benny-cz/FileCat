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

    [Fact]
    public async Task Missing_git_executable_leaves_badges_unavailable_without_failing_the_list()
    {
        string absent = Path.Join(AppContext.BaseDirectory, "missing-git-" + Guid.NewGuid().ToString("N"));

        var snapshot = await GitStatusReader.ReadAsync(AppContext.BaseDirectory, CancellationToken.None, absent);

        Assert.Null(snapshot);
    }
}
