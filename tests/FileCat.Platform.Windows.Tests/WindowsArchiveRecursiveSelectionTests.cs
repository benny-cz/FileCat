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
        else File.WriteAllBytes(Path.Combine(child, "file"), [9, 9, 9]);
        Assert.ThrowsAny<IOException>(() => { using var view = binding.File.OpenReadView(binding.Reviewed, ct); _ = view.ReadByte(); });
        if (ancestor) Assert.ThrowsAny<IOException>(() => { foreach (var directory in selection.Inputs.Directories.Values) directory.Review(ct); });
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
