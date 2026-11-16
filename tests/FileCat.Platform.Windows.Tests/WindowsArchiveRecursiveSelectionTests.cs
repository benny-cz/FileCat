using FileCat.Core.Archives;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveStorageTests
{
    [Fact]
    public void Recursive_capture_excludes_a_native_junction_without_reading_its_target()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string tree = Directory.CreateDirectory(fixture.PathOf("tree")).FullName;
        string outside = Directory.CreateDirectory(fixture.PathOf("outside")).FullName;
        File.WriteAllBytes(Path.Combine(tree, "file"), [1]); File.WriteAllBytes(Path.Combine(outside, "secret"), [9]);
        string link = Path.Combine(tree, "junction"); Junction.Create(link, outside);
        try
        {
            using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct);
            Assert.Equal(1, selection.Excluded(ArchiveSelectionExclusionReason.Link)); Assert.Single(selection.Inputs.Bindings);
            var plan = selection.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], true, ct);
            Assert.Equal(new[] { "tree", "tree/file" }, plan.Additions.Select(a => a.Name));
        }
        finally { Directory.Delete(link); }
    }
    [Fact]
    public void Recursive_scope_checks_native_destination_ancestor_identities()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string below = Directory.CreateDirectory(Path.Combine(fixture.Root, "tree", "child")).FullName;
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct);
        using var destination = WindowsArchiveDirectory.Open(below);
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => selection.Inputs.ReviewSelectionDestination(below, destination.Identity.ToString(), ct)).Reason);
        using var outside = WindowsArchiveDirectory.Open(fixture.Root);
        selection.Inputs.ReviewSelectionDestination(fixture.Root, outside.Identity.ToString(), ct);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_lazy_reopen_refuses_changed_file_or_replaced_ancestor(bool ancestor)
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string child = Directory.CreateDirectory(Path.Combine(fixture.Root, "tree", "child")).FullName; File.WriteAllBytes(Path.Combine(child, "file"), [1, 2, 3]);
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct); var binding = Assert.Single(selection.Inputs.Bindings).Value;
        using (var view = binding.File.OpenReadView(binding.Reviewed, ct)) Assert.Equal(1, view.ReadByte());
        if (ancestor)
        { Directory.Move(child, child + "-retired"); Directory.CreateDirectory(child); File.WriteAllBytes(Path.Combine(child, "file"), [1, 2, 3]); }
        else
        {
            // The reopen judges identity and metadata, not content: each writer checks the bytes' SHA-256 as it reads them. A
            // rewrite of the same length inside one tick of the file system's clock can keep every timestamp (hosted Windows
            // ARM64 failed this case so, run 43785533121); an edit made later carries a later time, as this one now does.
            string changed = Path.Combine(child, "file"); var before = File.GetLastWriteTimeUtc(changed);
            File.WriteAllBytes(changed, [9, 9, 9]); File.SetLastWriteTimeUtc(changed, before.AddSeconds(2));
        }
        Assert.ThrowsAny<IOException>(() => { using var view = binding.File.OpenReadView(binding.Reviewed, ct); _ = view.ReadByte(); });
        if (ancestor) Assert.ThrowsAny<IOException>(() => { foreach (var directory in selection.Inputs.Directories.Values) directory.Review(ct); });
    }
    // Each access pins only the folders below the held selection root (AT18 on Windows): inputs three folders deep are
    // captured and reopened with their exact content, and once they are closed no folder stays pinned.
    [Fact]
    public void Nested_inputs_reopen_through_folders_pinned_below_the_root_and_release_them()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string deep = Directory.CreateDirectory(Path.Combine(fixture.Root, "tree", "a", "b")).FullName;
        for (int i = 0; i < 50; i++) File.WriteAllBytes(Path.Combine(i % 2 == 0 ? deep : Path.GetDirectoryName(deep)!, $"file{i:00}"), [(byte)i, 7]);
        using (var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct))
        {
            Assert.Equal(50, selection.Inputs.Bindings.Count);
            foreach (var binding in selection.Inputs.Bindings.Values)
            {
                using var view = binding.File.OpenReadView(binding.Reviewed, ct);
                Assert.Equal(2, view.Length); view.ReadByte(); Assert.Equal(7, view.ReadByte());
            }
            foreach (var directory in selection.Inputs.Directories.Values) directory.Review(ct);
        }
        Directory.Move(Path.Combine(fixture.Root, "tree", "a"), Path.Combine(fixture.Root, "tree", "a-moved"));
        Directory.Move(Path.Combine(fixture.Root, "tree"), Path.Combine(fixture.Root, "tree-moved"));
    }

    [Fact]
    public void Recursive_native_enumeration_handles_multiple_batches_with_bounded_lazy_handles()
    {
        RequireWindows(); var ct = TestContext.Current.CancellationToken; using var fixture = new Fixture();
        string tree = Directory.CreateDirectory(fixture.PathOf("tree")).FullName;
        for (int i = 0; i < 1000; i++) File.WriteAllBytes(Path.Combine(tree, "界-" + i + "-" + new string('x', 100)), [(byte)i]);
        using var selection = WindowsArchiveSelectionTree.Capture(fixture.Root, ["tree"], ct); Assert.Equal(1000, selection.Inputs.Bindings.Count);
        foreach (var binding in selection.Inputs.Bindings.Values) { using var view = binding.File.OpenReadView(binding.Reviewed, ct); Assert.Equal(1, view.Length); }
        Assert.Single(selection.Inputs.Directories).Value.Review(ct);
    }
}
