using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.State;

namespace FileCat.Core.Tests;

public sealed class WorkingSetTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string StateFile => Path.Combine(_dir.Path, "state", "working-sets.json");

    private (ResultSetProvider Provider, WorkingSets Sets) Open()
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        var provider = new ResultSetProvider(providers, new PortableFileOperations());
        providers.Register(provider);
        return (provider, new WorkingSets(StateFile, provider));
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }

    private static async Task<List<EntryData>> List(ResultSetProvider provider, Location location)
    {
        var list = new List<EntryData>();
        await provider.EnumerateAsync(location, new Sink(list), TestContext.Current.CancellationToken);
        return list;
    }

    [Fact]
    public async Task A_working_set_keeps_references_across_sessions_and_never_touches_the_items()
    {
        var a = _dir.File(Path.Combine("one", "a.txt"), "alpha");
        var b = _dir.File(Path.Combine("two", "b.txt"), "beta");
        var (provider, sets) = Open();
        var release = sets.Create("Release notes");
        Assert.Equal(2, sets.Add(release, [ItemRef.ForFileSystemPath(a, EntryKind.File), ItemRef.ForFileSystemPath(b, EntryKind.File)]));
        Assert.Equal(0, sets.Add(release, [ItemRef.ForFileSystemPath(a, EntryKind.File)])); // already a member
        Assert.Equal("A working set with this name already exists.", sets.ValidateName("release NOTES"));
        Assert.Equal("Working set", sets.SuggestName());
        sets.Dispose(); // saves

        (provider, sets) = Open();
        var loaded = Assert.Single(sets.All);
        Assert.Equal("Release notes", loaded.Title);
        var location = ResultSetProvider.LocationOf(loaded);
        Assert.True(ResultSetProvider.IsWorkingSet(location));
        Assert.Equal(ResultSetProvider.WorkingSetList, provider.GetParent(location));
        var entries = await List(provider, location);
        Assert.Equal(["a.txt", "b.txt"], entries.Select(e => e.Name).Order());
        // Each member shows where it is; copying it keeps no relative folder.
        var tag = Assert.IsType<ResultTag>(entries.First(e => e.Name == "a.txt").Tag);
        Assert.Equal(Path.GetDirectoryName(a), tag.Folder);
        Assert.Equal(string.Empty, provider.GetItemRef(location, entries[0]).RelativeFolder);

        // The list of sets is a location of its own.
        var list = await List(provider, ResultSetProvider.WorkingSetList);
        var row = Assert.Single(list);
        Assert.Equal(("Release notes", "2 items"), (row.Name, ((WorkingSetTag)row.Tag!).DetailsText));
        Assert.Equal(location, provider.GetChildLocation(ResultSetProvider.WorkingSetList, row));

        // Removing members and deleting the set forget references only.
        Assert.Equal(1, provider.Remove(location, [provider.GetItemRef(location, entries.First(e => e.Name == "a.txt"))]));
        Assert.Equal(1, sets.Delete([loaded]));
        sets.Dispose();
        Assert.True(File.Exists(a) && File.Exists(b));
        (_, sets) = Open();
        Assert.Empty(sets.All);
        sets.Dispose();
    }

    [Fact]
    public async Task Vanished_and_renamed_members_are_shown_as_unavailable_or_followed()
    {
        var a = _dir.File("report.txt", "r");
        var (provider, sets) = Open();
        var set = sets.Create("Reports");
        sets.Add(set, [ItemRef.ForFileSystemPath(a, EntryKind.File)]);
        var location = ResultSetProvider.LocationOf(set);
        File.Move(a, Path.Combine(_dir.Path, "report-final.txt"));
        var entry = Assert.Single(await List(provider, location));
        Assert.True(entry.Flags.HasFlag(EntryFlags.Unavailable));

        var old = provider.GetItemRef(location, entry);
        Assert.True(set.Replace(old, new ItemRef(old.Parent, "report-final.txt", EntryKind.File)));
        entry = Assert.Single(await List(provider, location));
        Assert.Equal("report-final.txt", entry.Name);
        Assert.False(entry.Flags.HasFlag(EntryFlags.Unavailable));
        sets.Dispose();
    }

    [Fact]
    public void A_damaged_file_keeps_what_it_can_and_a_newer_one_is_never_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        var parent = Location.FileSystem(_dir.Path).Serialize();
        File.WriteAllText(StateFile, $$"""
            {
              "SchemaVersion": 1,
              "Sets": [
                { "Id": "a1", "Name": "Dup", "Items": [ { "Parent": {{parent}}, "Name": "x.txt", "Kind": "File" }, { "Name": "no-parent.txt", "Kind": "File" } ] },
                { "Id": "a2", "Name": "dup\u0007", "Items": [] },
                { "Id": "a1", "Name": "Same id again", "Items": [] },
                { "Id": "", "Name": "No id", "Items": [] }
              ]
            }
            """);
        var (_, sets) = Open();
        Assert.Equal(["Dup", "dup (2)"], sets.All.Select(s => s.Title));
        Assert.Equal(1, sets.All[0].Count);
        sets.Dispose();

        File.WriteAllText(StateFile, """{ "SchemaVersion": 99, "Sets": [ { "Id": "z", "Name": "From the future", "Items": [] } ] }""");
        (_, sets) = Open();
        Assert.True(sets.IsReadOnly);
        sets.Create("Local only");
        sets.Dispose();
        Assert.Contains("From the future", File.ReadAllText(StateFile));
        Assert.DoesNotContain("Local only", File.ReadAllText(StateFile));

        File.WriteAllText(StateFile, "{ not json");
        (_, sets) = Open();
        Assert.Equal(StateLoadStatus.CorruptUsingDefaults, sets.Status);
        Assert.Empty(sets.All);
        sets.Dispose();
        Assert.NotEmpty(Directory.GetFiles(Path.GetDirectoryName(StateFile)!, "working-sets.json.corrupt-*"));
    }

    [Fact]
    public void Names_are_validated_and_sets_stop_at_their_limit()
    {
        var (_, sets) = Open();
        Assert.Equal("The name cannot be empty.", sets.ValidateName("   "));
        Assert.NotNull(sets.ValidateName(new string('n', WorkingSets.MaxNameLength + 1)));
        Assert.NotNull(sets.ValidateName("tab\there"));
        Assert.Throws<ArgumentException>(() => sets.Create(""));
        var set = sets.Create("Big");
        var parent = Location.FileSystem(_dir.Path);
        int added = sets.Add(set, Enumerable.Range(0, WorkingSets.MaxMembers + 5).Select(i => new ItemRef(parent, $"f{i}", EntryKind.File)));
        Assert.Equal(WorkingSets.MaxMembers, added);
        // Sets are never members of sets.
        Assert.Equal(0, sets.Add(sets.Create("Other"), [new ItemRef(ResultSetProvider.WorkingSetList, "Big", EntryKind.Directory)]));
        sets.Dispose();
    }
}
