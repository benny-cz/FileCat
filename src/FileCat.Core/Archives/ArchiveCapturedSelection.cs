namespace FileCat.Core.Archives;

internal enum ArchiveSelectionKind { File, Directory, Link, Special, UnrepresentableName }
internal enum ArchiveSelectionExclusionReason { Link, SpecialFile, UnrepresentableName }
internal readonly record struct ArchiveSelectionEntry(string Leaf, ArchiveSelectionKind Kind, string Identity);
internal sealed record ArchiveSelectionExclusion(string MemberName, ArchiveSelectionExclusionReason Reason, int RootSelector = 0);
internal sealed record ArchiveSelectionRoot(int Selector, string Leaf, string MemberName, ArchiveSelectionKind? ExpectedKind = null);
internal sealed record ArchiveCapturedMember(string Name, ArchiveMemberType Type, int Id, int RootSelector);

/// <summary>Immutable, task-local native path graph. Never constructed from a worker request.</summary>
internal sealed class ArchiveSelectionNode(ArchiveSelectionNode? parent, ArchiveSelectionEntry entry, string memberName, int rootSelector = 0)
{
    internal ArchiveSelectionNode? Parent { get; } = parent;
    internal ArchiveSelectionEntry Entry { get; } = entry;
    internal string MemberName { get; } = memberName;
    internal int RootSelector { get; } = parent?.RootSelector ?? rootSelector;
    // Native traversal never uses the requested destination spelling.
    internal string NativeRelativePath { get; } = parent is null ? entry.Leaf : parent.NativeRelativePath + "/" + entry.Leaf;
    internal int Depth { get; } = (parent?.Depth ?? 0) + 1;
}

/// <summary>Exclusive parent-owned root authority; ownership transfers to Capture, including on failure.</summary>
internal interface IArchiveSelectionTree : IDisposable
{
    ArchiveSelectionEntry InspectRoot(string leaf, CancellationToken ct);
    ArchiveSelectionEntry InspectRoot(ArchiveSelectionRoot root, CancellationToken ct) => InspectRoot(root.Leaf, ct);
    void Enumerate(ArchiveSelectionNode directory, Action<ArchiveSelectionEntry> visit, CancellationToken ct);
    void CaptureFile(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode file, CancellationToken ct);
    void CaptureDirectory(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode directory, CancellationToken ct);
    void ReviewDestination(string path, string identity, IReadOnlySet<string> selectedDirectories, CancellationToken ct);
}

internal interface IArchiveSelectionRetainedCost
{
    long RetainedNameChars { get; }
    long RetainedHeapBytes { get; }
}

/// <summary>Optional native deletion authority for the exact captured graph. It never accepts a caller pathname.</summary>
internal interface IArchiveSelectionRemoval
{
    bool CanRemoveSources { get; }
    void RemoveFile(ArchiveSelectionNode node, ArchiveFileObservation expected, CancellationToken ct);
    void RemoveDirectory(ArchiveSelectionNode node, ArchiveDirectoryObservation expected, CancellationToken ct);
}

/// <summary>
/// A frozen local selection with bounded iterative traversal and explicit exclusions. Native adapters must
/// enumerate held directories without following links and bind each lazy reopen to the complete captured path.
/// This does not claim an atomic filesystem snapshot or confer source-deletion authority.
/// </summary>
internal sealed class ArchiveCapturedSelection : IDisposable
{
    internal const int MaximumDepth = 256, MaximumExclusionSamples = 128;
    private readonly IArchiveSelectionTree source;
    private readonly List<ArchiveCapturedMember> members = [];
    private readonly Dictionary<int, ArchiveSelectionNode> nodes = [];
    private readonly HashSet<ArchiveSelectionNode> excludedAncestors = [];
    private readonly List<ArchiveSelectionExclusion> samples = [];
    private readonly int[] exclusionCounts = new int[3];
    private int disposed;
    internal ArchiveCapturedInputs Inputs { get; } = new();
    internal IReadOnlyList<string> Roots { get; private set; } = [];
    internal IReadOnlyList<ArchiveCapturedMember> Members { get; private set; } = [];
    internal IReadOnlyList<ArchiveSelectionSource>? SourceSpecification => (source as IArchiveSelectionSpecification)?.Sources;
    internal IReadOnlyList<ArchiveSelectionExclusion> ExclusionSamples { get; private set; } = [];
    internal int ExcludedCount => exclusionCounts.Sum();
    internal bool CanRemoveSources => source is IArchiveSelectionRemoval { CanRemoveSources: true };
    internal int Excluded(ArchiveSelectionExclusionReason reason) => exclusionCounts[(int)reason];
    private ArchiveCapturedSelection(IArchiveSelectionTree source) => this.source = source;

    internal static ArchiveCapturedSelection Capture(IArchiveSelectionTree source, IReadOnlyList<string> selectedRoots, CancellationToken ct)
        => CaptureCore(source, () => LegacyRoots(selectedRoots), ct);
    internal static ArchiveCapturedSelection Capture(IArchiveSelectionTree source, IReadOnlyList<ArchiveSelectionRoot> selectedRoots, CancellationToken ct)
        => CaptureCore(source, () => selectedRoots, ct);
    private static ArchiveCapturedSelection CaptureCore(IArchiveSelectionTree source, Func<IReadOnlyList<ArchiveSelectionRoot>> roots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new ArchiveCapturedSelection(source);
        try { result.Walk(roots(), ct); return result; }
        catch { result.Dispose(); throw; }
    }

    private static IReadOnlyList<ArchiveSelectionRoot> LegacyRoots(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Count is <= 0 or > ArchiveWritePolicy.MaxMembers) throw Invalid();
        var result = new ArchiveSelectionRoot[roots.Count]; var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < result.Length; i++)
        {
            string leaf = roots[i]; RequireLeaf(leaf);
            if (!unique.Add(leaf)) throw Invalid();
            result[i] = new(i + 1, leaf, leaf);
        }
        return result;
    }
    private void Walk(IReadOnlyList<ArchiveSelectionRoot> selectedRoots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selectedRoots); ct.ThrowIfCancellationRequested();
        if (selectedRoots.Count is <= 0 or > ArchiveWritePolicy.MaxMembers) throw Invalid();
        if (source is IArchiveSelectionRetainedCost retained)
            Inputs.RetainSelection(retained.RetainedNameChars, retained.RetainedHeapBytes);
        // Freeze all roots before the first native call. Later focus/selection changes cannot widen this scope.
        Inputs.RetainSelection(0, checked(4096L + selectedRoots.Count * 64L));
        var roots = new ArchiveSelectionRoot[selectedRoots.Count]; var unique = new HashSet<int>();
        for (int i = 0; i < roots.Length; i++)
        {
            ct.ThrowIfCancellationRequested(); var root = selectedRoots[i] ?? throw Invalid(); RequireLeaf(root.Leaf);
            if (root.MemberName is null) throw Invalid();
            if (root.Selector <= 0 || !unique.Add(root.Selector) || root.ExpectedKind is not null && root.ExpectedKind is not (ArchiveSelectionKind.File or ArchiveSelectionKind.Directory)) throw Invalid();
            long chars = checked((long)root.Leaf.Length + root.MemberName.Length);
            Inputs.RetainSelection(chars, checked(192L + 2L * chars)); roots[i] = root;
        }
        Roots = Array.AsReadOnly(roots.Select(root => root.Leaf).ToArray());
        var pending = new Stack<ArchiveSelectionNode>(); var selectedDirectories = new HashSet<string>(StringComparer.Ordinal); int visited = 0, id = 0;
        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Representable(root.Leaf)) { ReserveVisited(); Exclude(root.MemberName, ArchiveSelectionExclusionReason.UnrepresentableName, root.Selector); continue; }
            ArchiveSelectionEntry entry;
            using (ArchiveCapturedInputLeases.Rent(ct)) entry = source.InspectRoot(root, ct);
            if (entry.Leaf != root.Leaf || root.ExpectedKind is { } expected &&
                (entry.Kind is ArchiveSelectionKind.File or ArchiveSelectionKind.Directory) && entry.Kind != expected) throw Changed();
            Enqueue(null, entry, root);
        }
        while (pending.TryPop(out var node))
        {
            ct.ThrowIfCancellationRequested(); int input = checked(++id);
            Inputs.RetainSelection(0, 128); nodes.Add(input, node);
            if (node.Entry.Kind == ArchiveSelectionKind.File)
            {
                source.CaptureFile(Inputs, input, node, ct);
                members.Add(new(node.MemberName, ArchiveMemberType.File, input, node.RootSelector));
            }
            else
            {
                source.CaptureDirectory(Inputs, input, node, ct);
                members.Add(new(node.MemberName, ArchiveMemberType.Directory, input, node.RootSelector));
                using (ArchiveCapturedInputLeases.Rent(ct)) source.Enumerate(node, child => Enqueue(node, child, null), ct);
            }
        }
        if (members.Count == 0)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The selection contains no supported files or folders. No archive was created.");
        members.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        Members = members.AsReadOnly();
        ExclusionSamples = samples.AsReadOnly();
        Inputs.SetSelectionDestinationReview((path, identity, token) => source.ReviewDestination(path, identity, selectedDirectories, token));
        Inputs.Seal(ct);
        foreach (var directory in Inputs.Directories.Values) directory.Review(ct);

        void ReserveVisited() { ct.ThrowIfCancellationRequested(); if (++visited > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit(); }
        void Enqueue(ArchiveSelectionNode? parent, ArchiveSelectionEntry entry, ArchiveSelectionRoot? selectedRoot)
        {
            ReserveVisited();
            int selector = parent?.RootSelector ?? selectedRoot!.Selector;
            // Invalid byte names are reported without inventing a replacement filename to open later.
            if (entry.Kind == ArchiveSelectionKind.UnrepresentableName)
            { Exclude(parent?.MemberName ?? selectedRoot!.MemberName, ArchiveSelectionExclusionReason.UnrepresentableName, selector, parent); return; }
            RequireLeaf(entry.Leaf);
            int length = parent is null ? selectedRoot!.MemberName.Length : checked(parent.MemberName.Length + 1 + entry.Leaf.Length);
            int nativeLength = checked((parent?.NativeRelativePath.Length + 1 ?? 0) + entry.Leaf.Length);
            if (length > ArchiveWritePolicy.MaxNameChars || nativeLength > ArchiveWritePolicy.MaxNameChars || (parent?.Depth ?? 0) >= MaximumDepth) throw ArchiveWriteBudget.Limit();
            long retainedChars = checked(length + (long)nativeLength + entry.Leaf.Length + entry.Identity.Length);
            Inputs.RetainSelection(retainedChars, checked(768L + 2L * retainedChars));
            string name = parent is null ? selectedRoot!.MemberName : parent.MemberName + "/" + entry.Leaf;
            if (!Representable(name)) { Exclude(name, ArchiveSelectionExclusionReason.UnrepresentableName, selector, parent); return; }
            if (entry.Kind == ArchiveSelectionKind.Link) { Exclude(name, ArchiveSelectionExclusionReason.Link, selector, parent); return; }
            if (entry.Kind == ArchiveSelectionKind.Special) { Exclude(name, ArchiveSelectionExclusionReason.SpecialFile, selector, parent); return; }
            if (entry.Kind is not (ArchiveSelectionKind.File or ArchiveSelectionKind.Directory) || string.IsNullOrEmpty(entry.Identity)) throw Invalid();
            if (parent is null && entry.Kind == ArchiveSelectionKind.Directory) selectedDirectories.Add(entry.Identity);
            pending.Push(new(parent, entry, name, selector));
        }
    }

    internal ArchiveSelectionNode CapturedNode(int id)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        return nodes.TryGetValue(id, out var node) ? node : throw Invalid();
    }
    internal bool HasExcludedDescendants(int id) => excludedAncestors.Contains(CapturedNode(id));
    internal void RemoveCaptured(int id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var node = CapturedNode(id);
        if (source is not IArchiveSelectionRemoval { CanRemoveSources: true } removal)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "Native removal is unavailable for these captured sources.");
        if (node.Entry.Kind == ArchiveSelectionKind.File) removal.RemoveFile(node, Inputs.Bindings[id].Reviewed, ct);
        else if (node.Entry.Kind == ArchiveSelectionKind.Directory) removal.RemoveDirectory(node, Inputs.Directories[id].Reviewed, ct);
        else throw Invalid();
    }

    private void Exclude(string name, ArchiveSelectionExclusionReason reason, int selector, ArchiveSelectionNode? parent = null)
    {
        exclusionCounts[(int)reason] = checked(exclusionCounts[(int)reason] + 1);
        for (var node = parent; node is not null; node = node.Parent)
        {
            if (excludedAncestors.Contains(node)) break; // The complete ancestor chain was already retained.
            Inputs.RetainSelection(0, 64); excludedAncestors.Add(node);
        }
        if (samples.Count == MaximumExclusionSamples) return;
        Inputs.RetainSelection(name.Length, checked(128L + 2L * name.Length)); samples.Add(new(name, reason, selector));
    }
    internal ArchiveContainerTransformPlan CreatePlan(ArchiveWriteProfile profile, ArchiveAssessment? original,
        IReadOnlyList<ArchiveTransformEdit> edits, bool acceptExclusions, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this); ct.ThrowIfCancellationRequested();
        if (ExcludedCount != 0 && !acceptExclusions)
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Review and accept the excluded source items before creating this archive.");
        var additions = new ArchiveTransformAddition[members.Count];
        for (int i = 0; i < additions.Length; i++)
        {
            ct.ThrowIfCancellationRequested(); var member = members[i];
            additions[i] = member.Type == ArchiveMemberType.Directory ? Inputs.DirectoryAddition(member.Name, member.Id) : new(member.Name, member.Type, member.Id);
        }
        return Inputs.CreatePlan(profile, original, edits, additions, ct);
    }
    private static bool Representable(string name)
    {
        try { ArchiveWritePolicy.ValidateName(name); return true; }
        catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.InvalidRequest) { return false; }
    }
    private static void RequireLeaf(string leaf)
    {
        if (string.IsNullOrEmpty(leaf) || leaf.Length > ArchiveWritePolicy.MaxNameChars || leaf is "." or ".." || leaf.Contains('/') || leaf.Contains('\0')) throw Invalid();
    }
    internal static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The selected source tree changed during capture or review.");
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The captured archive selection is invalid.");
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        try { Inputs.Dispose(); } finally { source.Dispose(); }
    }
}
