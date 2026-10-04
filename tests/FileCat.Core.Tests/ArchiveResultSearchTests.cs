using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class ArchiveResultSearchTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly List<(ZipProvider Provider, string File)> _archives = [];
    public void Dispose()
    {
        foreach (var (provider, file) in _archives) provider.Release(file);
        _dir.Dispose();
    }

    private (string File, ProviderArchiveMembers Members, List<(ItemRef Item, string Relative)> Results) Fixture()
    {
        string file = Path.Combine(_dir.Path, "members.zip");
        WriteZip(file, [("keep.txt", 10), ("dir/skip.bin", 7), ("dir/dup.txt", 1), ("dir/dup.txt", 12)]);
        var providers = new ProviderRegistry();
        var zip = new ZipProvider(Path.Combine(_dir.Path, "scratch"));
        _archives.Add((zip, file));
        providers.Register(zip);
        providers.Register(new LocalFileSystemProvider { ContainerDetector = zip });
        var members = new ProviderArchiveMembers(providers);
        var listed = members.List(file, TestContext.Current.CancellationToken).Select(i => (i, "kept relative provenance")).ToList();
        Assert.Equal(2, listed.Count(i => i.i.Name == "dup.txt"));
        Assert.Equal([0, 1], listed.Where(i => i.i.Name == "dup.txt").Select(i => i.i.Ordinal).Order());
        return (file, members, listed);
    }

    private static void WriteZip(string path, (string Name, int Size)[] entries)
    {
        using var archive = new ZipArchive(File.Create(path), ZipArchiveMode.Create);
        foreach (var e in entries)
        {
            using var stream = archive.CreateEntry(e.Name).Open();
            stream.Write(Enumerable.Repeat((byte)'x', e.Size).ToArray());
        }
    }

    private static (ResultSet Result, SearchSession Session) Run(SearchCriteria criteria, List<(ItemRef Item, string Relative)> within, IArchiveMembers? archives)
    {
        Assert.True(criteria.TryBuildQuery(DateTime.UtcNow, [], within, out var query, out var error, archives), error);
        var result = new ResultSet("narrow", "narrow", "owned original results");
        var session = new SearchSession(query!, result);
        session.Run(TestContext.Current.CancellationToken);
        Assert.True(session.Finished);
        Assert.True(result.IsComplete);
        return (result, session);
    }

    [Fact]
    public void Names_sizes_and_duplicate_ordinals_narrow_archive_results_without_discovering_more_members()
    {
        var (_, members, within) = Fixture();
        string local = Path.Combine(_dir.Path, "local.txt");
        File.WriteAllBytes(local, new byte[10]);
        within.Add((ItemRef.ForFileSystemPath(local, EntryKind.File), "local provenance"));
        var criteria = new SearchCriteria { Names = "*.txt", InsideArchives = false,
            Advanced = new AdvancedSearchCriteria { SizeAtLeast = 8, SizeAtLeastUnit = SizeUnit.Bytes, SizeAtMost = 20, SizeAtMostUnit = SizeUnit.Bytes } };
        var (result, session) = Run(criteria, within, members);
        Assert.Equal(["dup.txt#1", "keep.txt#0", "local.txt#0"], result.Snapshot().Select(r => r.Item.Name + "#" + r.Item.Ordinal).Order());
        Assert.All(result.Snapshot().Where(r => r.Item.FileSystemPath is null), r => Assert.Equal("kept relative provenance", r.Relative));
        Assert.All(session.Log, e =>
        {
            Assert.Equal(SearchLogKind.Warning, e.Kind);
            Assert.Contains("duplicate names", e.Detail, StringComparison.Ordinal);
        });
        foreach (var (item, _) in result.Snapshot().Where(r => r.Item.FileSystemPath is null))
        {
            Assert.Equal(within.Single(r => r.Item.Equals(item)).Item.Parent, item.Parent);
            using var source = _archives.Single().Provider.OpenContent(item);
            Assert.NotNull(source);
            var bytes = new byte[item.Size];
            Assert.Equal(bytes.Length, source.Read(0, bytes));
            Assert.All(bytes, b => Assert.Equal((byte)'x', b));
        }
        Assert.Equal(within.Count, within.Select(r => r.Item).Distinct().Count());
    }

    [Fact]
    public void Member_metadata_is_revalidated_and_a_removed_ordinal_is_reported()
    {
        var (file, members, within) = Fixture();
        var old = File.GetLastWriteTimeUtc(file);
        // Retain the old result references, but close the old index before replacing the owned fixture.
        _archives.Single(a => a.File == file).Provider.Release(file);
        WriteZip(file, [("keep.txt", 2), ("dir/skip.bin", 7), ("dir/dup.txt", 1)]);
        File.SetLastWriteTimeUtc(file, old.AddSeconds(4));
        var criteria = new SearchCriteria { Names = "*.txt", Advanced = new AdvancedSearchCriteria { SizeAtLeast = 8, SizeAtLeastUnit = SizeUnit.Bytes } };
        var (result, session) = Run(criteria, within, members);
        Assert.Empty(result.Snapshot());
        Assert.Contains(session.Log, e => e.Kind == SearchLogKind.Gone && e.Path.Contains("dup.txt", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Contains("dup.txt", StringComparison.Ordinal));
    }

    [Fact]
    public void Member_content_search_is_explicitly_unavailable_and_preserves_original_references()
    {
        var (_, members, within) = Fixture();
        var files = within.Where(r => !r.Item.IsContainer).ToList();
        var (result, session) = Run(new SearchCriteria { Text = "x", InsideArchives = true }, files, members);
        Assert.Empty(result.Snapshot());
        Assert.Equal(files.Count, session.Log.Count);
        Assert.All(session.Log, e =>
        {
            Assert.Equal(SearchLogKind.Inaccessible, e.Kind);
            Assert.Contains("contents are not searched", e.Detail, StringComparison.Ordinal);
        });
        Assert.Equal(4, files.Count);
    }

    [Fact]
    public void Missing_member_lookup_is_reported_instead_of_silently_returning_an_empty_success()
    {
        var (_, _, within) = Fixture();
        var (_, session) = Run(new SearchCriteria { Names = "*.txt" }, within, null);
        Assert.NotEmpty(session.Log);
        Assert.All(session.Log, e => Assert.Equal(SearchLogKind.Inaccessible, e.Kind));
    }

    [Fact]
    public void Newly_added_members_cannot_enter_the_original_subset_even_when_inside_archives_is_on()
    {
        var (file, members, within) = Fixture();
        var subset = within.Where(r => r.Item.Name == "keep.txt").ToList();
        var old = File.GetLastWriteTimeUtc(file);
        _archives.Single().Provider.Release(file);
        WriteZip(file, [("keep.txt", 10), ("new.txt", 40), ("dir/new.txt", 40)]);
        File.SetLastWriteTimeUtc(file, old.AddSeconds(4));
        var (result, session) = Run(new SearchCriteria { Names = "*.txt", InsideArchives = true }, subset, members);
        var match = Assert.Single(result.Snapshot());
        Assert.Equal(subset[0].Item, match.Item);
        Assert.Equal(subset[0].Relative, match.Relative);
        Assert.Empty(session.Log);
    }

    private ProviderArchiveMembers ControlledMembers(ControlledArchive provider)
    {
        var providers = new ProviderRegistry();
        providers.Register(provider);
        return new ProviderArchiveMembers(providers);
    }

    private Location Parent() => new(Schemes.Zip, "folder", Location.FileSystem(Path.Combine(_dir.Path, "controlled.zip")));

    [Fact]
    public void A_parent_is_listed_once_and_no_contents_subfolders_or_unrequested_matches_are_opened()
    {
        var parent = Parent();
        var provider = new ControlledArchive([
            new("one.txt", EntryKind.File, 10), new("two.txt", EntryKind.File, 20),
            new("unrequested.txt", EntryKind.File, 30), new("subfolder", EntryKind.Directory)]);
        List<(ItemRef Item, string Relative)> within = [
            (new(parent, "one.txt", EntryKind.File, 1), "one provenance"),
            (new(parent, "two.txt", EntryKind.File, 1), "two provenance")];
        var (result, session) = Run(new SearchCriteria { Names = "*.txt",
            Advanced = new AdvancedSearchCriteria { SizeAtLeast = 8, SizeAtLeastUnit = SizeUnit.Bytes } }, within, ControlledMembers(provider));
        Assert.Equal([10L, 20L], result.Snapshot().Select(r => r.Item.Size).Order());
        Assert.All(result.Snapshot(), r => Assert.Equal(within.Single(w => w.Item.Equals(r.Item)).Relative, r.Relative));
        Assert.Equal(1, provider.Listings);
        Assert.Equal(0, provider.ContentOpens);
        Assert.Equal(0, provider.ChildLocations);
        Assert.Empty(session.Log);
    }

    [Fact]
    public void A_partial_listing_reports_a_warning_and_cannot_call_an_unlisted_member_gone()
    {
        var parent = Parent();
        var provider = new ControlledArchive([new("present.txt", EntryKind.File, 10)]) { Warning = "Only the first entry was listed." };
        List<(ItemRef Item, string Relative)> within = [
            (new(parent, "present.txt", EntryKind.File), "present"), (new(parent, "unknown.txt", EntryKind.File), "unknown")];
        var (result, session) = Run(new SearchCriteria(), within, ControlledMembers(provider));
        Assert.Equal("present.txt", Assert.Single(result.Snapshot()).Item.Name);
        Assert.DoesNotContain(session.Log, e => e.Kind == SearchLogKind.Gone);
        Assert.Contains(session.Log, e => e.Kind == SearchLogKind.Warning && e.Detail == provider.Warning && e.Parent == parent);
        var unavailable = Assert.Single(session.Log, e => e.Kind == SearchLogKind.Inaccessible);
        Assert.Equal(parent, unavailable.Parent);
        Assert.Equal("unknown.txt", unavailable.Name);
        Assert.Equal(2, result.Issues.Count);
    }

    [Fact]
    public void Unsupported_and_nested_locations_are_reported_without_touching_the_provider()
    {
        var provider = new ControlledArchive([]);
        var nested = new Location(Schemes.Zip, "", Parent().WithPath("inner.zip"));
        List<(ItemRef Item, string Relative)> within = [
            (new(new Location(Schemes.Mtp, "unavailable-device"), "device.txt", EntryKind.File), "device"),
            (new(nested, "nested.txt", EntryKind.File), "nested")];
        var (result, session) = Run(new SearchCriteria(), within, ControlledMembers(provider));
        Assert.Empty(result.Snapshot());
        Assert.Equal(2, session.Log.Count);
        Assert.All(session.Log, e => Assert.Equal(SearchLogKind.Inaccessible, e.Kind));
        Assert.Equal(0, provider.Listings);
        Assert.Equal(0, provider.ContentOpens);
        Assert.Equal(0, provider.ChildLocations);
    }

    [Fact]
    public void Cancellation_during_revalidation_marks_the_result_incomplete_without_claiming_missing_members()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new ControlledArchive([]) { OnEnumeration = cancellation.Cancel };
        List<(ItemRef Item, string Relative)> within = [(new(Parent(), "unknown.txt", EntryKind.File), "unknown")];
        Assert.True(new SearchCriteria().TryBuildQuery(DateTime.UtcNow, [], within, out var query, out var error, ControlledMembers(provider)), error);
        var result = new ResultSet("cancel", "cancel", "owned cancellation control");
        var session = new SearchSession(query!, result);
        session.Run(cancellation.Token);
        Assert.True(session.Finished);
        Assert.False(result.IsComplete);
        Assert.Empty(result.Snapshot());
        Assert.Empty(session.Log);
        Assert.Equal(1, provider.Listings);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_missing_or_unreadable_archive_reports_its_scope(bool missing)
    {
        var (file, members, within) = Fixture();
        _archives.Single().Provider.Release(file);
        if (missing) File.Delete(file);
        else File.WriteAllText(file, "not an archive");
        var (result, session) = Run(new SearchCriteria(), within, members);
        Assert.Empty(result.Snapshot());
        Assert.NotEmpty(session.Log);
        Assert.All(session.Log, e =>
        {
            Assert.Equal(missing ? SearchLogKind.Gone : SearchLogKind.Inaccessible, e.Kind);
            Assert.Contains(file, e.Path, StringComparison.Ordinal);
            Assert.NotNull(e.Parent);
        });
        Assert.NotEmpty(result.Issues);
    }

    private sealed class ControlledArchive(EntryData[] entries) : ResourceProvider
    {
        public int Listings, ContentOpens, ChildLocations;
        public string? Warning { get; init; }
        public Action? OnEnumeration { get; init; }
        public override string Scheme => Schemes.Zip;
        public override string GetDisplayPath(Location location) => location.ToString();
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            Listings++;
            OnEnumeration?.Invoke();
            ct.ThrowIfCancellationRequested();
            if (Warning is not null) sink.ReportIssue(Warning);
            sink.AddBatch(entries);
            return Task.CompletedTask;
        }
        public override Location? GetChildLocation(Location parent, in EntryData entry)
        {
            ChildLocations++;
            throw new InvalidOperationException("The narrowing control must not enter a child.");
        }
        public override IContentSource? OpenContent(ItemRef item)
        {
            ContentOpens++;
            throw new InvalidOperationException("The narrowing control must not open content.");
        }
    }
}
