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

    /// <summary>Lists members and reports non-fatal provider warnings, including damage after a usable prefix.</summary>
    IEnumerable<ItemRef> List(string archivePath, Action<string> reportIssue, CancellationToken ct) => List(archivePath, ct);
}

/// <summary>Rechecks earlier archive results without entering folders, opening contents or discovering new members.</summary>
public interface IArchiveResultLookup
{
    /// <summary>
    /// Lists one parent once, retaining only requested identities (including duplicate ordinals). Reports partial
    /// enumeration warnings; a missing identity is gone only if the parent was read without such warnings.
    /// </summary>
    IReadOnlyDictionary<ItemRef, ItemRef> Revalidate(Location parent, IReadOnlySet<ItemRef> requested,
        Action<string> reportIssue, CancellationToken ct);
}

/// <summary>
/// The archive providers' members (ZIP, TAR, 7z, RAR, disc images, …): each folder of the archive is listed in turn;
/// archives inside the archive are listed as files and not opened (that would unpack them).
/// </summary>
public sealed class ProviderArchiveMembers(ProviderRegistry providers) : IArchiveMembers, IArchiveResultLookup
{
    private IContainerDetector? Detector =>
        providers.TryGet(Schemes.FileSystem, out var provider) && provider is LocalFileSystemProvider local ? local.ContainerDetector : null;

    public bool IsArchive(string fileName) => Detector?.IsContainer(fileName) == true;

    public IReadOnlyDictionary<ItemRef, ItemRef> Revalidate(Location parent, IReadOnlySet<ItemRef> requested,
        Action<string> reportIssue, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Find discovers members of local archives, but never opens another archive inside one to recheck a result.
        if (parent.Scheme is not (Schemes.Zip or Schemes.Archive) || parent.Container?.IsFileSystem != true)
            throw new NotSupportedException("This result is not a member of a local archive; it cannot be rechecked.");
        if (!providers.TryGet(parent.Scheme, out var provider) || provider is null)
            throw new NotSupportedException("The archive provider is unavailable; its members cannot be rechecked.");
        var sink = new LookupCollector(provider, parent, requested, reportIssue, ct);
        provider.EnumerateAsync(parent, sink, ct).GetAwaiter().GetResult();
        return sink.Members;
    }

    public IEnumerable<ItemRef> List(string archivePath, CancellationToken ct) => List(archivePath, _ => { }, ct);

    public IEnumerable<ItemRef> List(string archivePath, Action<string> reportIssue, CancellationToken ct)
    {
        if (Detector?.GetContainerLocation(archivePath) is not { } archive) yield break;
        var folders = new Stack<Location>();
        folders.Push(archive);
        while (folders.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var folder = folders.Pop();
            var provider = providers.For(folder);
            var entries = new Collector(reportIssue);
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

    private sealed class Collector(Action<string> reportIssue) : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];

        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries) Entries.Add(entry);
        }

        public void ReportIssue(string message) => reportIssue(message);
    }

    private sealed class LookupCollector(ResourceProvider provider, Location parent, IReadOnlySet<ItemRef> requested,
        Action<string> reportIssue, CancellationToken ct) : IEnumerationSink
    {
        public Dictionary<ItemRef, ItemRef> Members { get; } = [];

        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            foreach (var entry in entries)
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Kind == EntryKind.Parent) continue;
                var item = provider.GetItemRef(parent, entry);
                if (requested.Contains(item)) Members.TryAdd(item, item);
            }
        }

        public void ReportIssue(string message) => reportIssue(message);
    }
}
