using FileCat.Core.Analysis;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>A temporary panel listing of metadata read through an approved analysis folder binding.</summary>
internal sealed class AnalysisPanelFolderProvider : ResourceProvider
{
    internal const string WorkingSetRefusal = "Open a current folder to add its files to a working set. This panel shows read-only analysis metadata.";

    private readonly object _gate = new();
    private readonly Dictionary<Location, PublishedFolder> _folders = [];

    public override string Scheme => Schemes.AnalysisFolder;

    internal PreparedLocation Publish(AnalysisPanelFolder folder)
    {
        if (folder.Entries.Length > AnalysisPanelFolder.MaximumEntries)
            throw new NotSupportedException(AnalysisPanelFolder.LimitMessage);

        Location location = new(Scheme, Guid.NewGuid().ToString("N"));
        lock (_gate)
        {
            if (_folders.Count >= AnalysisLimits.Sessions)
                throw new InvalidOperationException("Close a read-only analysis folder in the workspace before opening another.");
            _folders.Add(location, new(folder));
        }
        return new(location, new ReadLease(this, location));
    }

    public override Task<PreparedLocation> PrepareNavigationAsync(Location location, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new PreparedLocation(location, Retain(location, out _)));
    }

    public override string GetDisplayPath(Location location) => Find(location)?.DisplayPath ?? "Analysis listing closed";

    public override string GetDisplayName(Location location) => Find(location)?.IsResultSet == true ? "Read-only analysis results" : "Read-only analysis folder";

    internal string GetBanner(Location location)
    {
        if (Find(location) is not { IsResultSet: true } result)
            return "Read-only folder metadata captured by Disk space. Reopen from the analysis to check the current contents.";
        string import = result.Taint.HasFlag(AnalysisTaint.ImportedUnverified) ? " Imported observations are unverified." : "";
        string sensitive = result.Taint.HasFlag(AnalysisTaint.PrivilegedDerived) ? " Contains privileged-derived metadata." : "";
        return "Read-only results from the selected analysis observations. The recorded locations stay offline." + import + sensitive;
    }

    private AnalysisPanelFolder? Find(Location location)
    {
        lock (_gate)
            return _folders.TryGetValue(location, out var folder) ? folder.Folder : null;
    }
    public override Location? GetParent(Location location) => null;
    public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
    public override string ExplainUnavailable(Location location, LocationCapabilities capability) =>
        "This read-only analysis listing contains observed folder metadata. Open a current folder separately to use file commands.";

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        using var lease = Retain(location, out var folder);
        for (int start = 0; start < folder.Entries.Length; start += 256)
        {
            ct.ThrowIfCancellationRequested();
            sink.AddBatch(folder.Entries.AsSpan(start, Math.Min(256, folder.Entries.Length - start)));
        }
        return Task.CompletedTask;
    }

    private ReadLease Retain(Location location, out AnalysisPanelFolder folder)
    {
        lock (_gate)
        {
            if (!_folders.TryGetValue(location, out var published))
                throw new DirectoryNotFoundException("The read-only analysis folder is no longer open.");
            published.Owners++;
            folder = published.Folder;
            return new(this, location);
        }
    }

    private void Release(Location location)
    {
        lock (_gate)
            if (--_folders[location].Owners == 0) _folders.Remove(location);
    }

    private sealed class PublishedFolder(AnalysisPanelFolder folder)
    {
        internal AnalysisPanelFolder Folder { get; } = folder;
        internal int Owners = 1;
    }

    private sealed class ReadLease(AnalysisPanelFolderProvider owner, Location location) : IDisposable
    {
        private AnalysisPanelFolderProvider? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release(location);
    }
}
