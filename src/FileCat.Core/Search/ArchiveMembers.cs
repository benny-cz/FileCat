using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Core.Search;

/// <summary>Lists the members of archives for Find's "inside archives" scope (plan §11): names only, never contents.</summary>
public interface IArchiveMembers
{
    /// <summary>Whether a file is an archive whose members can be listed.</summary>
    bool IsArchive(string fileName);

    /// <summary>
    /// The archive's members, folders included, each with the archive location that holds it. Throws an
    /// <see cref="IOException"/> or <see cref="InvalidDataException"/> when the archive cannot be read.
    /// </summary>
    IEnumerable<ItemRef> List(string archivePath, CancellationToken ct);
}

/// <summary>
/// The archive providers' members (ZIP, TAR, 7z, RAR, disc images, …): each folder of the archive is listed in turn;
/// archives inside the archive are listed as files and not opened (that would unpack them).
/// </summary>
public sealed class ProviderArchiveMembers(ProviderRegistry providers) : IArchiveMembers
{
    private IContainerDetector? Detector =>
        providers.TryGet(Schemes.FileSystem, out var provider) && provider is LocalFileSystemProvider local ? local.ContainerDetector : null;

    public bool IsArchive(string fileName) => Detector?.IsContainer(fileName) == true;

    public IEnumerable<ItemRef> List(string archivePath, CancellationToken ct)
    {
        if (Detector?.GetContainerLocation(archivePath) is not { } archive) yield break;
        var folders = new Stack<Location>();
        folders.Push(archive);
        while (folders.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = folders.Pop();
            var provider = providers.For(folder);
            var entries = new Collector();
            provider.EnumerateAsync(folder, entries, ct).GetAwaiter().GetResult();
            foreach (var entry in entries.Entries)
            {
                if (entry.Kind == EntryKind.Parent) continue;
                yield return provider.GetItemRef(folder, entry);
                if (entry.Kind == EntryKind.Directory && provider.GetChildLocation(folder, entry) is { } child && child.Scheme == folder.Scheme)
                    folders.Push(child);
            }
        }
    }

    private sealed class Collector : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];

        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Entries.Add(entry);
        }

        public void ReportIssue(string message)
        {
        }
    }
}
