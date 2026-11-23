using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.Core.Tests;

public sealed class ProviderArchiveMembersTraversalTests
{
    [Fact]
    public void Archive_search_lists_a_linked_folder_without_entering_it()
    {
        var archive = new ControlledArchive();
        var providers = new ProviderRegistry();
        providers.Register(archive);
        providers.Register(new LocalFileSystemProvider { ContainerDetector = archive });

        var items = new ProviderArchiveMembers(providers)
            .List("fixture.zip", TestContext.Current.CancellationToken).ToArray();

        Assert.Equal(["linked", "ordinary", "child.txt"], items.Select(item => item.Name));
        Assert.Equal(["", "ordinary"], archive.ListedFolders);
        Assert.Equal(1, archive.ChildLocations);
        Assert.Equal(EntryFlags.Link, items[0].Flags & EntryFlags.Link);
    }

    private sealed class ControlledArchive : ResourceProvider, IContainerDetector
    {
        public List<string> ListedFolders { get; } = [];
        public int ChildLocations { get; private set; }
        public override string Scheme => Schemes.Zip;
        public override string GetDisplayPath(Location location) => location.ToString();
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public bool IsContainer(string fileName) => fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        public Location? GetContainerLocation(string filePath) =>
            new(Schemes.Zip, string.Empty, Location.FileSystem(Path.GetFullPath(filePath)));

        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ListedFolders.Add(location.Path);
            sink.AddBatch(location.Path switch
            {
                "" => [new EntryData("linked", EntryKind.Directory) { Flags = EntryFlags.Link },
                    new EntryData("ordinary", EntryKind.Directory)],
                "ordinary" => [new EntryData("child.txt", EntryKind.File)],
                _ => throw new InvalidOperationException("The linked archive folder must not be listed."),
            });
            return Task.CompletedTask;
        }

        public override Location? GetChildLocation(Location parent, in EntryData entry)
        {
            if (entry.Has(EntryFlags.Link)) throw new InvalidOperationException("The linked archive folder must not be opened.");
            ChildLocations++;
            return parent.WithPath(entry.Name);
        }

        public override IContentSource? OpenContent(ItemRef item) =>
            throw new InvalidOperationException("Archive member search is names only.");
    }
}
