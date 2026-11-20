using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class NativeSearchPredicateTests
{
    private sealed class Members : IArchiveMembers, IArchiveResultLookup
    {
        public int Listings, Lookups;
        public Location Parent = new(Schemes.Zip, "", Location.FileSystem(Path.Combine(Path.GetTempPath(), "native-scope.zip")));
        public bool IsArchive(string name) => name.EndsWith(".zip", StringComparison.Ordinal);
        public IEnumerable<ItemRef> List(string archivePath, CancellationToken ct)
        { Listings++; return [new(Parent, "member.txt", EntryKind.File, 8), new(Parent, "folder", EntryKind.Directory)]; }
        public IReadOnlyDictionary<ItemRef, ItemRef> Revalidate(Location parent, IReadOnlySet<ItemRef> wanted, Action<string> issue, CancellationToken ct)
        { Lookups++; return wanted.ToDictionary(i => i, i => i); }
    }

    [Fact]
    public void Admission_checks_both_polarities_platform_scope_and_unknown_saved_bits_without_IO()
    {
        foreach (var bit in new[] { FileAttributes.Compressed, FileAttributes.Encrypted, FileAttributes.Archive, FileAttributes.ReparsePoint, FileAttributes.SparseFile, FileAttributes.Offline })
        {
            Assert.Single(NativeSearchPredicates.Issues(bit, windows: false));
            Assert.Empty(NativeSearchPredicates.Issues(bit, windows: true));
            Assert.Single(NativeSearchPredicates.Issues(bit, windows: true, nativeScope: false));
        }
        Assert.Empty(NativeSearchPredicates.Issues(FileAttributes.Directory, windows: false, nativeScope: false));
        var unknown = Assert.Single(NativeSearchPredicates.Issues((FileAttributes)0x40000000, windows: true));
        Assert.Contains("0x40000000", unknown.Name);
        foreach (bool clear in new[] { false, true })
        {
            var query = new SearchQuery { Roots = [], AttributesSet = clear ? 0 : (FileAttributes)0x40000000, AttributesClear = clear ? (FileAttributes)0x40000000 : 0 };
            Assert.False(SearchSession.TryValidate(query, out var reason)); Assert.Contains("Remove this criterion", reason);
            var set = new ResultSet("invalid", "invalid", "test"); new SearchSession(query, set).Run(TestContext.Current.CancellationToken);
            Assert.False(set.IsComplete); Assert.Contains(set.Issues, issue => issue.Contains("Query admission", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Discovered_archive_members_are_unexamined_for_set_and_clear_native_masks(bool clear)
    {
        using var dir = new TempDir(); dir.File("outer.zip"); dir.File("plain.txt");
        var members = new Members(); var set = new ResultSet("native", "native", "test");
        var query = new SearchQuery { Roots = [dir.Path], Archives = members,
            AttributesSet = clear ? 0 : FileAttributes.Hidden, AttributesClear = clear ? FileAttributes.Hidden : 0 };
        var session = new SearchSession(query, set); session.Run(TestContext.Current.CancellationToken);
        Assert.Equal(2, session.NativePredicateUnexamined); Assert.False(set.IsComplete);
        Assert.DoesNotContain(set.Snapshot(), r => !r.Item.Parent.IsFileSystem);
        Assert.Contains(set.Issues, issue => issue.Contains("2 archive/provider candidates unexamined", StringComparison.Ordinal));
        Assert.Equal(1, members.Listings);
    }

    [Fact]
    public void Refined_mixed_scope_keeps_local_matches_and_counts_members_without_reopening_the_archive()
    {
        using var dir = new TempDir(); string file = dir.File("plain.txt"); var members = new Members();
        var archived = new ItemRef(members.Parent, "member.txt", EntryKind.File);
        var local = ItemRef.ForFileSystemPath(file, EntryKind.File);
        var query = new SearchQuery { Roots = [], WithinResults = [(local, "original local"), (archived, "original archive")],
            ResultArchives = members, AttributesClear = FileAttributes.Hidden };
        Assert.True(SearchSession.TryValidate(query, out _));
        var set = new ResultSet("native", "native", "test"); var session = new SearchSession(query, set); session.Run(TestContext.Current.CancellationToken);
        Assert.Equal(local, Assert.Single(set.Snapshot()).Item); Assert.Equal(1, session.NativePredicateUnexamined);
        Assert.Equal(0, members.Listings); Assert.Equal(0, members.Lookups); Assert.False(set.IsComplete);
        var archiveOnly = new SearchQuery { Roots = [], WithinResults = [(archived, "original archive")], AttributesClear = FileAttributes.Hidden };
        Assert.False(SearchSession.TryValidate(archiveOnly, out var reason)); Assert.Contains("contains no local filesystem subjects", reason);
        var directoryOnly = new SearchQuery { Roots = [], WithinResults = [(archived, "original archive")], ResultArchives = members, AttributesClear = FileAttributes.Directory };
        Assert.True(SearchSession.TryValidate(directoryOnly, out _));
        var scoped = new ResultSet("directory", "directory", "test"); new SearchSession(directoryOnly, scoped).Run(TestContext.Current.CancellationToken);
        Assert.Equal(archived, Assert.Single(scoped.Snapshot()).Item); Assert.True(scoped.IsComplete);
    }

    [Fact]
    public void Refinement_keeps_a_captured_reparse_hint_until_the_entry_changes()
    {
        using var directory = new TempDir();
        string target = directory.File("target.txt", "target");
        string path = Path.Join(directory.Path, "link.txt");
        try { File.CreateSymbolicLink(path, target); }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is (IOException or UnauthorizedAccessException))
        { Assert.Skip("The Windows test token cannot create a symbolic link."); }

        var info = new FileInfo(path);
        Assert.True(info.Attributes.HasFlag(FileAttributes.ReparsePoint));
        var captured = new ItemRef(Location.FileSystem(directory.Path), info.Name, EntryKind.File, info.Length, info.LastWriteTimeUtc.Ticks)
        {
            NativeAttributes = (uint)info.Attributes,
            NativeReparseTag = ReparseLinks.SymbolicLinkTag,
        };
        var query = new SearchQuery { Roots = [], WithinResults = [(captured, "original folder")] };

        var first = Refine();
        Assert.Equal("original folder", first.Relative);
        Assert.Equal(captured, first.Item);
        Assert.Equal(ReparseLinks.SymbolicLinkTag, first.Item.NativeReparseTag);

        File.Delete(path);
        File.WriteAllBytes(path, new byte[checked((int)captured.Size)]);
        File.SetLastWriteTimeUtc(path, new DateTime(captured.Modified, DateTimeKind.Utc));
        Assert.Equal(0u, Refine().Item.NativeReparseTag);
        Assert.Equal("target", File.ReadAllText(target));

        (ItemRef Item, string Relative) Refine()
        {
            var results = new ResultSet("refined", "Refined", "test");
            new SearchSession(query, results).Run(TestContext.Current.CancellationToken);
            Assert.True(results.IsComplete);
            return Assert.Single(results.Snapshot());
        }
    }
}
