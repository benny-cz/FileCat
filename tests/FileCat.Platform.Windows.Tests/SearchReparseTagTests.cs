using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Find and Flat view show a found link as a link (Attr L, Size &lt;LINK&gt;, the link overlay), not as another reparse
/// point. The .NET listing the search walks reports no reparse tags, so each found reparse point carries the tag read
/// from the item itself, never followed, and the results list keeps it while the item is unchanged. Ordinary items carry
/// no tag.
/// </summary>
public sealed class SearchReparseTagTests : IDisposable
{
    /// <summary>ERROR_PRIVILEGE_NOT_HELD (1314) as .NET reports it: neither Developer Mode nor the privilege.</summary>
    private const int PrivilegeNotHeld = unchecked((int)0x80070522);

    private readonly string _root = Directory.CreateTempSubdirectory("filecat-find-tags-").FullName;
    private readonly List<string> _links = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        // The links first, as links, so that deleting the folder never goes through one.
        foreach (string link in _links)
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            else if (File.Exists(link)) File.Delete(link);
        }
        Directory.Delete(_root, recursive: true);
    }

    /// <summary>An ordinary folder holding a file, and an ordinary file beside it.</summary>
    private string Ordinary()
    {
        string folder = Directory.CreateDirectory(Path.Join(_root, "folder")).FullName;
        File.WriteAllText(Path.Join(folder, "inside.txt"), "inside");
        File.WriteAllText(Path.Join(_root, "file.txt"), "untouched");
        return folder;
    }

    private static ResultSet Find(SearchQuery query)
    {
        var set = new ResultSet(Guid.NewGuid().ToString("N")[..12], "find", "reparse tag fixture");
        var session = new SearchSession(query, set);
        session.Run(Ct);
        Assert.True(set.IsComplete, string.Join("; ", session.Log.Select(e => e.Describe())));
        return set;
    }

    /// <summary>The found items by their path below the root (the engine may spell the root in the disk's case).</summary>
    private Dictionary<string, ItemRef> Found(ResultSet set) =>
        set.Snapshot().ToDictionary(r => Path.GetRelativePath(_root, r.Item.FileSystemPath!), r => r.Item, StringComparer.Ordinal);

    /// <summary>The rows the results list shows, revalidated through the platform's file operations, by name.</summary>
    private static async Task<(Dictionary<string, EntryData> Rows, Location Location)> List(ResultSet set)
    {
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var results = new ResultSetProvider(providers, new WindowsFileOperations());
        providers.Register(results);
        results.Adopt(set);
        var location = ResultSetProvider.LocationOf(set);
        var sink = new Sink();
        await results.EnumerateAsync(location, sink, Ct);
        return (sink.Rows, location);
    }

    private sealed class Sink : IEnumerationSink
    {
        public Dictionary<string, EntryData> Rows { get; } = new(StringComparer.Ordinal);
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Assert.True(Rows.TryAdd(entry.Name, entry), entry.Name);
        }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    [Fact]
    public async Task A_found_junction_lists_as_a_link_by_its_tag_while_ordinary_items_carry_none()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows reparse tags.");
        string folder = Ordinary();
        string junction = Path.Join(_root, "junction");
        Junction.Create(junction, folder);
        _links.Add(junction);

        var set = Find(new SearchQuery { Roots = [_root] });
        var found = Found(set);
        string inside = Path.Join("folder", "inside.txt");
        // The junction is listed as itself and never entered.
        Assert.Equal(["file.txt", "folder", inside, "junction"], found.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ReparseLinks.MountPointTag, found["junction"].NativeReparseTag);
        foreach (string ordinary in new[] { "file.txt", "folder", inside }) Assert.Equal(0u, found[ordinary].NativeReparseTag);

        var (rows, location) = await List(set);
        Assert.Equal(ReparseLinks.MountPointTag, rows["junction"].NativeReparseTag);
        Assert.True(EntryPresentation.IsLink(rows["junction"], location));
        Assert.True(rows["junction"].Has(EntryFlags.Link));
        foreach (string ordinary in new[] { "file.txt", "folder", "inside.txt" })
        {
            Assert.Equal(0u, rows[ordinary].NativeReparseTag);
            Assert.False(EntryPresentation.IsLink(rows[ordinary], location), ordinary);
        }
        Assert.Equal("inside", File.ReadAllText(Path.Join(folder, "inside.txt")));
    }

    /// <summary>
    /// Searching within results keeps an earlier result's tag while the item's metadata agrees, without reading it again
    /// (this one is deliberately not the junction's own, to tell the two apart). A tag that no longer agrees, or none,
    /// is read from the item.
    /// </summary>
    [Fact]
    public void Searching_within_results_keeps_an_agreeing_tag_and_reads_a_stale_or_missing_one()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows reparse tags.");
        string junction = Path.Join(_root, "junction");
        Junction.Create(junction, Ordinary());
        _links.Add(junction);
        var listed = Found(Find(new SearchQuery { Roots = [_root] }))["junction"];

        ItemRef Earlier(long modified) => new(listed.Parent, listed.Name, listed.Kind, listed.Size, modified)
            { NativeAttributes = listed.NativeAttributes, NativeReparseTag = ReparseLinks.SymbolicLinkTag };
        uint Refound(ItemRef earlier) =>
            Assert.Single(Find(new SearchQuery { Roots = [], WithinResults = [(earlier, "")] }).Snapshot()).Item.NativeReparseTag;

        Assert.Equal(ReparseLinks.SymbolicLinkTag, Refound(Earlier(listed.Modified)));
        Assert.Equal(ReparseLinks.MountPointTag, Refound(Earlier(listed.Modified - 1)));
        Assert.Equal(ReparseLinks.MountPointTag, Refound(ItemRef.ForFileSystemPath(junction, EntryKind.Directory)));
    }

    /// <summary>Symbolic links need Developer Mode or the privilege; without either the test is skipped.</summary>
    [Fact]
    public async Task Found_symbolic_links_list_as_links_in_Find_and_Flat_view()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows reparse tags.");
        string folder = Ordinary();
        string folderLink = Path.Join(_root, "folder-link"), fileLink = Path.Join(_root, "file-link.txt");
        try
        {
            Directory.CreateSymbolicLink(folderLink, folder);
            _links.Add(folderLink);
            File.CreateSymbolicLink(fileLink, Path.Join(_root, "file.txt"));
            _links.Add(fileLink);
        }
        catch (IOException ex) when (ex.HResult == PrivilegeNotHeld)
        {
            Assert.Skip("This token may not create symbolic links: " + ex.Message);
        }

        var set = Find(new SearchQuery { Roots = [_root] });
        var found = Found(set);
        Assert.DoesNotContain(Path.Join("folder-link", "inside.txt"), found.Keys);
        Assert.Equal(ReparseLinks.SymbolicLinkTag, found["folder-link"].NativeReparseTag);
        Assert.Equal(ReparseLinks.SymbolicLinkTag, found["file-link.txt"].NativeReparseTag);
        Assert.Equal(0u, found["folder"].NativeReparseTag);
        var (rows, location) = await List(set);
        Assert.Equal(ReparseLinks.SymbolicLinkTag, rows["folder-link"].NativeReparseTag);
        Assert.True(EntryPresentation.IsLink(rows["folder-link"], location));
        Assert.False(EntryPresentation.IsLink(rows["folder"], location));

        // Flat view's query: files only, the folder link neither listed nor entered.
        var flat = Find(new SearchQuery { Roots = [_root], IncludeDirectories = false });
        Assert.Equal(["file-link.txt", "file.txt", Path.Join("folder", "inside.txt")], Found(flat).Keys.Order(StringComparer.Ordinal));
        (rows, location) = await List(flat);
        Assert.Equal(ReparseLinks.SymbolicLinkTag, rows["file-link.txt"].NativeReparseTag);
        Assert.True(EntryPresentation.IsLink(rows["file-link.txt"], location));
        Assert.Equal(0u, rows["file.txt"].NativeReparseTag);
        Assert.False(EntryPresentation.IsLink(rows["file.txt"], location));
        Assert.Equal("untouched", File.ReadAllText(Path.Join(_root, "file.txt")));
    }
}
