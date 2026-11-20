using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Native_source_removal_keeps_unstored_streams_already_present_during_capture(bool directory)
    {
        RequireWindows(); using var fixture = new Fixture(); var ct = TestContext.Current.CancellationToken;
        string path = fixture.PathOf("item");
        if (directory) Directory.CreateDirectory(path); else File.WriteAllBytes(path, [1, 2, 3]);
        File.WriteAllText(path + ":unarchived", "keep these bytes");
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["item"], ct);
        Assert.ThrowsAny<IOException>(() => selection.RemoveCaptured(Assert.Single(selection.Members).Id, ct));
        Assert.Equal("keep these bytes", File.ReadAllText(path + ":unarchived"));
        Assert.True(directory ? Directory.Exists(path) : File.Exists(path));
    }

    [Fact]
    public void Native_source_removal_removes_files_then_exact_empty_folders()
    {
        RequireWindows(); using var fixture = new Fixture(); var ct = TestContext.Current.CancellationToken;
        string tree = Directory.CreateDirectory(fixture.PathOf("tree/sub")).Parent!.FullName;
        File.WriteAllBytes(Path.Combine(tree, "sub/file"), [1, 2, 3]);
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct);
        foreach (var member in selection.Members.OrderByDescending(member => selection.CapturedNode(member.Id).Depth))
            selection.RemoveCaptured(member.Id, ct);
        Assert.False(Directory.Exists(tree));
    }

    [Theory]
    [InlineData("bytes")] [InlineData("identity")] [InlineData("origin")] [InlineData("stream")] [InlineData("readonly")]
    public void Native_source_removal_keeps_changed_or_unstored_file_data(string change)
    {
        RequireWindows(); using var fixture = new Fixture(); var ct = TestContext.Current.CancellationToken;
        string path = fixture.PathOf("file"); File.WriteAllBytes(path, [1, 2, 3]);
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["file"], ct);
        var member = Assert.Single(selection.Members); DateTime modified = File.GetLastWriteTimeUtc(path);
        try
        {
            switch (change)
            {
                case "bytes": File.WriteAllBytes(path, [9, 9, 9]); File.SetLastWriteTimeUtc(path, modified); break;
                case "identity": File.Move(path, path + ".old"); File.WriteAllBytes(path, [1, 2, 3]); File.SetLastWriteTimeUtc(path, modified); break;
                case "origin": File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); break;
                case "stream": File.WriteAllText(path + ":unarchived", "keep these bytes"); break;
                case "readonly": File.SetAttributes(path, FileAttributes.ReadOnly); break;
            }
            Assert.ThrowsAny<Exception>(() => selection.RemoveCaptured(member.Id, ct));
            Assert.True(File.Exists(path));
            if (change == "stream") Assert.Equal("keep these bytes", File.ReadAllText(path + ":unarchived"));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }

    [Theory]
    [InlineData("identity")] [InlineData("child")] [InlineData("origin")] [InlineData("stream")] [InlineData("attributes")]
    [InlineData("modified")] [InlineData("restored-modified")]
    public void Native_source_removal_keeps_changed_nonempty_or_unstored_folders(string change)
    {
        RequireWindows(); using var fixture = new Fixture(); var ct = TestContext.Current.CancellationToken;
        string path = Directory.CreateDirectory(fixture.PathOf("tree")).FullName;
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct);
        var member = Assert.Single(selection.Members);
        DateTime modified = Directory.GetLastWriteTimeUtc(path);
        switch (change)
        {
            case "identity": Directory.Move(path, path + ".old"); Directory.CreateDirectory(path); break;
            case "child": File.WriteAllText(Path.Combine(path, "new"), "keep"); break;
            case "origin": File.WriteAllText(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n"); break;
            case "stream": File.WriteAllText(path + ":unarchived", "keep these bytes"); break;
            case "attributes": File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); break;
            case "modified": Directory.SetLastWriteTimeUtc(path, DateTime.UnixEpoch); break;
            case "restored-modified": Thread.Sleep(20); File.WriteAllText(Path.Combine(path, "temporary"), "change"); File.Delete(Path.Combine(path, "temporary")); Directory.SetLastWriteTimeUtc(path, modified); break;
        }
        Assert.ThrowsAny<Exception>(() => selection.RemoveCaptured(member.Id, ct));
        Assert.True(Directory.Exists(path));
        if (change == "child") Assert.Equal("keep", File.ReadAllText(Path.Combine(path, "new")));
        if (change == "stream") Assert.Equal("keep these bytes", File.ReadAllText(path + ":unarchived"));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public void Native_source_removal_detects_unrelated_parent_changes_before_or_between_own_deletions(bool beforeFirst, bool restoreTime)
    {
        RequireWindows(); using var fixture = new Fixture(); var ct = TestContext.Current.CancellationToken;
        string tree = Directory.CreateDirectory(fixture.PathOf("tree")).FullName;
        File.WriteAllBytes(Path.Combine(tree, "first"), [1]); File.WriteAllBytes(Path.Combine(tree, "second"), [2]);
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct);
        var first = selection.Members.Single(member => member.Name == "tree/first");
        var second = selection.Members.Single(member => member.Name == "tree/second");
        if (!beforeFirst) selection.RemoveCaptured(first.Id, ct);
        DateTime current = Directory.GetLastWriteTimeUtc(tree);
        if (restoreTime)
        {
            Thread.Sleep(20); string added = Path.Combine(tree, "transient"); File.WriteAllText(added, "external"); File.Delete(added);
            Directory.SetLastWriteTimeUtc(tree, current);
        }
        else Directory.SetLastWriteTimeUtc(tree, DateTime.UnixEpoch);
        Assert.ThrowsAny<IOException>(() => selection.RemoveCaptured(second.Id, ct));
        Assert.True(File.Exists(Path.Combine(tree, "second"))); Assert.Equal(beforeFirst, File.Exists(Path.Combine(tree, "first")));
        Assert.ThrowsAny<IOException>(() => selection.RemoveCaptured(selection.Members.Single(member => member.Name == "tree").Id, ct));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Native_source_removal_rejects_a_replaced_ancestor(bool directory)
    {
        RequireWindows(); using var fixture = new Fixture(); var ct = TestContext.Current.CancellationToken;
        string parent = Directory.CreateDirectory(fixture.PathOf("tree")).FullName;
        string path = Path.Combine(parent, "item");
        if (directory) Directory.CreateDirectory(path); else File.WriteAllBytes(path, [1, 2, 3]);
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct);
        var member = selection.Members.Single(member => member.Name == "tree/item");
        Directory.Move(parent, parent + ".old"); Directory.CreateDirectory(parent);
        if (directory) Directory.CreateDirectory(path); else File.WriteAllBytes(path, [1, 2, 3]);
        Assert.ThrowsAny<Exception>(() => selection.RemoveCaptured(member.Id, ct));
        Assert.True(directory ? Directory.Exists(path) : File.Exists(path));
        string captured = Path.Combine(parent + ".old", "item"); Assert.True(directory ? Directory.Exists(captured) : File.Exists(captured));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Native_source_removal_cancellation_before_mutation_keeps_the_source(bool directory)
    {
        RequireWindows(); using var fixture = new Fixture(); var ct = TestContext.Current.CancellationToken;
        string path = fixture.PathOf("item");
        if (directory) Directory.CreateDirectory(path); else File.WriteAllBytes(path, [1, 2, 3]);
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["item"], ct);
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => selection.RemoveCaptured(Assert.Single(selection.Members).Id, cancellation.Token));
        Assert.True(directory ? Directory.Exists(path) : File.Exists(path));
    }
}
