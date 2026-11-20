namespace FileCat.Core.Archives;

internal sealed record ArchiveSelectionSource(string Path, string MemberName, ArchiveSelectionKind Kind);
internal interface IArchiveSelectionSpecification
{
    IReadOnlyList<ArchiveSelectionSource> Sources { get; }
}

/// <summary>Parent-owned source roots from several directories; selectors never reach a worker as paths.</summary>
internal sealed class ArchiveSelectionForest : IArchiveSelectionTree, IArchiveSelectionRetainedCost, IArchiveSelectionSpecification, IArchiveSelectionRemoval
{
    internal const int MaximumParents = ArchiveWritePolicy.MaxInputLeases;
    private readonly Dictionary<int, IArchiveSelectionTree> owners = [];
    private readonly Dictionary<int, ArchiveSelectionEntry> issued = [];
    private readonly List<IArchiveSelectionTree> trees = [];
    private bool disposed;
    public long RetainedNameChars { get; private set; }
    public long RetainedHeapBytes { get; private set; }
    public IReadOnlyList<ArchiveSelectionSource> Sources { get; private set; } = [];
    public bool CanRemoveSources => !disposed && trees.Count != 0 && trees.All(tree => tree is IArchiveSelectionRemoval { CanRemoveSources: true });

    internal static ArchiveCapturedSelection Capture(IReadOnlyList<ArchiveSelectionSource> requests,
        Func<string, IArchiveSelectionTree> open, CancellationToken ct, long retainedNameChars = 0, long retainedHeapBytes = 0,
        Action<string>? visiting = null)
    {
        ArgumentNullException.ThrowIfNull(open);
        var forest = new ArchiveSelectionForest();
        try
        {
            ArgumentNullException.ThrowIfNull(requests);
            if (requests.Count is <= 0 or > ArchiveWritePolicy.MaxMembers || retainedNameChars < 0 || retainedHeapBytes < 0)
                throw Invalid();
            forest.RetainedNameChars = retainedNameChars;
            forest.RetainedHeapBytes = checked(retainedHeapBytes + 4096L + requests.Count * 192L);
            forest.CheckBudget();
            var roots = new ArchiveSelectionRoot[requests.Count];
            // A Windows directory may be case-sensitive; string equality cannot infer native aliases.
            var comparer = StringComparer.Ordinal;
            var parentNames = new Dictionary<string, string>(comparer);
            var rootParents = new string[requests.Count];
            var frozen = new ArchiveSelectionSource[requests.Count];
            for (int i = 0; i < roots.Length; i++)
            {
                ct.ThrowIfCancellationRequested(); var request = requests[i] ?? throw Invalid();
                if (!Path.IsPathFullyQualified(request.Path) || request.Path.Length > ArchiveWritePolicy.MaxNameChars ||
                    request.Kind is not (ArchiveSelectionKind.File or ArchiveSelectionKind.Directory)) throw Invalid();
                string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Path));
                string? parent = Path.GetDirectoryName(path); string leaf = Path.GetFileName(path);
                if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf)) throw Invalid();
                ArchiveWritePolicy.ValidateName(request.MemberName);
                forest.RetainedNameChars = checked(forest.RetainedNameChars + path.Length);
                forest.RetainedHeapBytes = checked(forest.RetainedHeapBytes + 128L + 2L * path.Length);
                forest.CheckBudget();
                if (!parentNames.TryGetValue(parent, out var capturedParent))
                {
                    if (parentNames.Count == MaximumParents) throw new ArchiveWriteException(ArchiveWriteReason.BudgetExceeded,
                        "The source selection exceeds the retained native parent-directory limit.");
                    forest.RetainedNameChars = checked(forest.RetainedNameChars + 4L * parent.Length);
                    forest.RetainedHeapBytes = checked(forest.RetainedHeapBytes + 4096L + 8L * parent.Length);
                    forest.CheckBudget();
                    parentNames.Add(parent, capturedParent = parent);
                }
                roots[i] = new(i + 1, leaf, request.MemberName, request.Kind);
                rootParents[i] = capturedParent;
                frozen[i] = new(path, request.MemberName, request.Kind);
            }
            forest.Sources = Array.AsReadOnly(frozen);
            // Freeze and bound every root before acquiring the first native parent. Opening one parent
            // must not allow callbacks or selection changes to retarget another root.
            var parents = new Dictionary<string, IArchiveSelectionTree>(comparer);
            foreach (var parent in parentNames.Keys)
            {
                ct.ThrowIfCancellationRequested(); var tree = open(parent) ?? throw Invalid();
                forest.trees.Add(tree); parents.Add(parent, tree);
            }
            for (int i = 0; i < rootParents.Length; i++) forest.owners.Add(i + 1, parents[rootParents[i]]);
            // Capture takes exclusive ownership, including on failure.
            return Transfer(forest, roots, ct, visiting);
        }
        catch { forest.Dispose(); throw; }
    }

    private static ArchiveCapturedSelection Transfer(ArchiveSelectionForest forest, ArchiveSelectionRoot[] roots, CancellationToken ct,
        Action<string>? visiting)
        => ArchiveCapturedSelection.Capture(forest, roots, ct, visiting);

    public ArchiveSelectionEntry InspectRoot(string leaf, CancellationToken ct) => throw Invalid();
    public ArchiveSelectionEntry InspectRoot(ArchiveSelectionRoot root, CancellationToken ct)
    {
        Live(); ct.ThrowIfCancellationRequested();
        if (!owners.TryGetValue(root.Selector, out var owner) || issued.ContainsKey(root.Selector)) throw Invalid();
        var entry = owner.InspectRoot(root.Leaf, ct);
        if (entry.Leaf != root.Leaf) throw ArchiveCapturedSelection.Changed();
        issued.Add(root.Selector, entry); return entry;
    }
    private IArchiveSelectionTree Owner(ArchiveSelectionNode node)
    {
        Live(); var root = node;
        for (int depth = 0; root.Parent is not null; depth++)
        {
            if (depth >= ArchiveCapturedSelection.MaximumDepth) throw Invalid();
            root = root.Parent;
        }
        if (!owners.TryGetValue(root.RootSelector, out var owner) || !issued.TryGetValue(root.RootSelector, out var entry) || entry != root.Entry)
            throw Invalid();
        return owner;
    }
    public void Enumerate(ArchiveSelectionNode directory, Action<ArchiveSelectionEntry> visit, CancellationToken ct)
        => Owner(directory).Enumerate(directory, visit, ct);
    public void CaptureFile(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode file, CancellationToken ct)
        => Owner(file).CaptureFile(inputs, id, file, ct);
    public void CaptureDirectory(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode directory, CancellationToken ct)
        => Owner(directory).CaptureDirectory(inputs, id, directory, ct);
    public bool RemovesThroughAsideName => !disposed && trees.Count != 0 && trees.All(tree => tree is IArchiveSelectionRemoval { RemovesThroughAsideName: true });
    public void RemoveFile(ArchiveSelectionNode node, ArchiveFileObservation expected, CancellationToken ct, string? asideLeaf = null)
        => (Owner(node) as IArchiveSelectionRemoval ?? throw Invalid()).RemoveFile(node, expected, ct, asideLeaf);
    public void RemoveDirectory(ArchiveSelectionNode node, ArchiveDirectoryObservation expected, CancellationToken ct)
        => (Owner(node) as IArchiveSelectionRemoval ?? throw Invalid()).RemoveDirectory(node, expected, ct);
    public void ReviewDestination(string path, string identity, IReadOnlySet<string> selectedDirectories, CancellationToken ct)
    {
        Live(); foreach (var tree in trees) { ct.ThrowIfCancellationRequested(); tree.ReviewDestination(path, identity, selectedDirectories, ct); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        List<Exception>? failures = null;
        foreach (var tree in trees)
        {
            try { tree.Dispose(); }
            catch (Exception ex) { (failures ??= []).Add(ex); }
        }
        if (failures is not null) throw new AggregateException("Some native source roots could not be released.", failures);
    }
    private void Live() => ObjectDisposedException.ThrowIf(disposed, this);
    private void CheckBudget()
    {
        if (RetainedNameChars > ArchiveWritePolicy.MaxRetainedNameChars || RetainedHeapBytes > ArchiveWritePolicy.MaxRetainedHeapBytes)
            throw ArchiveWriteBudget.Limit();
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The native source-root selection is invalid.");
}
