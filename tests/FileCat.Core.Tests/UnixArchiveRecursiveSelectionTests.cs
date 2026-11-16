using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    [Fact]
    public async Task Recursive_scope_refuses_destination_inside_an_excluded_subtree_before_creating_artifacts()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string excluded = Directory.CreateDirectory(Path.Combine(root, "tree", "NUL")).FullName;
        using var selection = UnixArchiveSelectionTree.Capture(root, ["tree"], ct);
        Assert.Equal(1, selection.Excluded(ArchiveSelectionExclusionReason.UnrepresentableName)); Assert.Single(selection.Inputs.Directories);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar); var plan = selection.CreatePlan(profile, null, [], true, ct);
        using var destination = UnixArchiveDirectory.Open(excluded); using var journal = ReservationJournal(root, profile); int effects = 0;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => ArchiveJournaledContainerPreparation.PrepareAsync<UnixArchiveFile>(journal,
            Path.Combine(excluded, "result.tar"), plan, null, selection.Inputs.Bindings,
            (nonce, kind, maximum) => destination.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum),
            _ => { effects++; throw new InvalidOperationException(); }, new((_, _) => throw new InvalidOperationException()), new((_, _) => throw new InvalidOperationException()), ct));
        Assert.Equal(ArchiveWriteReason.InvalidRequest, failure.Reason); Assert.Equal(0, effects); Assert.Empty(Directory.GetFileSystemEntries(excluded));
    }
    [Theory]
    [InlineData(ArchiveWriteFormat.Tar)]
    [InlineData(ArchiveWriteFormat.TarGzip)]
    [InlineData(ArchiveWriteFormat.TarBzip2)]
    [InlineData(ArchiveWriteFormat.TarZstd)]
    public async Task Recursive_selection_publishes_exact_native_tree_and_preserves_empty_folders(ArchiveWriteFormat format)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
        Directory.CreateDirectory(Path.Combine(tree, "nested", "empty")); Directory.CreateDirectory(Path.Combine(root, "outside"));
        File.WriteAllBytes(Path.Combine(tree, "nested", "界.bin"), [7, 0, 255, 9]); File.WriteAllBytes(Path.Combine(root, "loose"), [2, 3]);
        File.CreateSymbolicLink(Path.Combine(tree, "link"), Path.Combine(root, "loose"));
        File.WriteAllText(Path.Combine(root, "outside", "secret"), "not selected");
        using var selection = UnixArchiveSelectionTree.Capture(root, ["tree", "loose"], ct);
        Assert.Equal(1, selection.Excluded(ArchiveSelectionExclusionReason.Link)); Assert.Equal(3, selection.Inputs.Directories.Count);
        Assert.Equal(2, selection.Inputs.Bindings.Count); var profile = new ArchiveWriteProfile(format);
        var plan = selection.CreatePlan(profile, null, [], true, ct);
        Assert.Equal(new[] { "loose", "tree", "tree/nested", "tree/nested/empty", "tree/nested/界.bin" }, plan.Additions.Select(a => a.Name));
        using var destination = UnixArchiveDirectory.Open(root); using var journal = ReservationJournal(root, profile);
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, Path.Combine(root, "result.tar"), plan, null, selection.Inputs.Bindings,
            (nonce, kind, maximum) => destination.ReserveArtifact(nonce, (UnixArchiveArtifactKind)kind, maximum), destination.CreateArtifact,
            new((nonce, _) => Child.Start(nonce, "archive-transform")), new((nonce, _) => Child.Start(nonce, "archive-verify")), ct);
        Assert.Equal(5, prepared.Facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.OriginEvidence).OriginSources);
        await ArchiveJournaledTarAssertions.PublishNew(prepared, journal, selection.Inputs.Bindings, destination.CreateArtifact, ct);
        string? oracle = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_RECURSIVE_ORACLES");
        if (oracle is not null)
        {
            File.Copy(Path.Combine(root, "result.tar"), Path.Combine(oracle, "unix-" + format + ".archive"));
            File.WriteAllText(Path.Combine(oracle, "unix-" + format + ".json"), JsonSerializer.Serialize(new { format = format.ToString(), names = plan.Additions.Select(a => a.Name), manifest = plan.Expected.Digest }));
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recursive_lazy_reopen_refuses_changed_file_or_replaced_ancestor(bool ancestor)
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        string child = Directory.CreateDirectory(Path.Combine(root, "tree", "child")).FullName; File.WriteAllBytes(Path.Combine(child, "file"), [1, 2, 3]);
        using var selection = UnixArchiveSelectionTree.Capture(root, ["tree"], ct); var binding = Assert.Single(selection.Inputs.Bindings).Value;
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
    [Fact]
    public void Recursive_native_enumeration_handles_multiple_batches_with_bounded_lazy_handles()
    {
        Native(); var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string root = Canonical(temp.Path);
        string tree = Directory.CreateDirectory(Path.Combine(root, "tree")).FullName;
        for (int i = 0; i < 1000; i++) File.WriteAllBytes(Path.Combine(tree, "界-" + i + "-" + new string('x', 100)), [(byte)i]);
        using var selection = UnixArchiveSelectionTree.Capture(root, ["tree"], ct); Assert.Equal(1000, selection.Inputs.Bindings.Count);
        foreach (var binding in selection.Inputs.Bindings.Values) { using var view = binding.File.OpenReadView(binding.Reviewed, ct); Assert.Equal(1, view.Length); }
        Assert.Single(selection.Inputs.Directories).Value.Review(ct);
    }
}
