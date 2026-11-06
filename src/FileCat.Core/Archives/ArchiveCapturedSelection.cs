namespace FileCat.Core.Archives;

internal enum ArchiveSelectionKind { File, Directory, Link, Special, UnrepresentableName }
internal enum ArchiveSelectionExclusionReason { Link, SpecialFile, UnrepresentableName }
internal readonly record struct ArchiveSelectionEntry(string Leaf, ArchiveSelectionKind Kind, string Identity);
internal sealed record ArchiveSelectionExclusion(string MemberName, ArchiveSelectionExclusionReason Reason);

/// <summary>Immutable, task-local native path graph. Never constructed from a worker request.</summary>
internal sealed class ArchiveSelectionNode(ArchiveSelectionNode? parent, ArchiveSelectionEntry entry, string memberName)
{
    internal ArchiveSelectionNode? Parent { get; } = parent;
    internal ArchiveSelectionEntry Entry { get; } = entry;
    internal string MemberName { get; } = memberName;
    internal int Depth { get; } = (parent?.Depth ?? 0) + 1;
}

/// <summary>Exclusive parent-owned root authority; ownership transfers to Capture, including on failure.</summary>
internal interface IArchiveSelectionTree : IDisposable
{
    ArchiveSelectionEntry InspectRoot(string leaf, CancellationToken ct);
    void Enumerate(ArchiveSelectionNode directory, Action<ArchiveSelectionEntry> visit, CancellationToken ct);
    void CaptureFile(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode file, CancellationToken ct);
    void CaptureDirectory(ArchiveCapturedInputs inputs, int id, ArchiveSelectionNode directory, CancellationToken ct);
    void ReviewDestination(string path, string identity, IReadOnlySet<string> selectedDirectories, CancellationToken ct);
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
    private readonly List<(string Name, ArchiveMemberType Type, int Id)> members = [];
    private readonly List<ArchiveSelectionExclusion> samples = [];
    private readonly int[] exclusionCounts = new int[3];
    private int disposed;
    internal ArchiveCapturedInputs Inputs { get; } = new();
    internal IReadOnlyList<string> Roots { get; private set; } = [];
    internal IReadOnlyList<ArchiveSelectionExclusion> ExclusionSamples { get; private set; } = [];
    internal int ExcludedCount => exclusionCounts.Sum();
    internal int Excluded(ArchiveSelectionExclusionReason reason) => exclusionCounts[(int)reason];
    private ArchiveCapturedSelection(IArchiveSelectionTree source) => this.source = source;

    internal static ArchiveCapturedSelection Capture(IArchiveSelectionTree source, IReadOnlyList<string> selectedRoots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = new ArchiveCapturedSelection(source);
        try { result.Walk(selectedRoots, ct); return result; }
        catch { result.Dispose(); throw; }
    }

    private void Walk(IReadOnlyList<string> selectedRoots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(selectedRoots); ct.ThrowIfCancellationRequested();
        if (selectedRoots.Count is <= 0 or > ArchiveWritePolicy.MaxMembers) throw Invalid();
        // Freeze all roots before the first native call. Later focus/selection changes cannot widen this scope.
        Inputs.RetainSelection(0, checked(4096L + selectedRoots.Count * 64L));
        var roots = new string[selectedRoots.Count]; var unique = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < roots.Length; i++)
        {
            ct.ThrowIfCancellationRequested(); string leaf = selectedRoots[i]; RequireLeaf(leaf);
            Inputs.RetainSelection(leaf.Length, checked(48L + 2L * leaf.Length));
            if (!unique.Add(leaf)) throw Invalid(); roots[i] = leaf;
        }
        Roots = Array.AsReadOnly(roots);
        var pending = new Stack<ArchiveSelectionNode>(); var selectedDirectories = new HashSet<string>(StringComparer.Ordinal); int visited = 0, id = 0;
        foreach (string leaf in roots)
        {
            ct.ThrowIfCancellationRequested();
            if (!Representable(leaf)) { ReserveVisited(); Exclude(leaf, ArchiveSelectionExclusionReason.UnrepresentableName); continue; }
            ArchiveSelectionEntry entry;
            using (ArchiveCapturedInputLeases.Rent(ct)) entry = source.InspectRoot(leaf, ct);
            if (entry.Leaf != leaf) throw Changed();
            Enqueue(null, entry);
        }
        while (pending.TryPop(out var node))
        {
            ct.ThrowIfCancellationRequested(); int input = checked(++id);
            if (node.Entry.Kind == ArchiveSelectionKind.File)
            {
                source.CaptureFile(Inputs, input, node, ct);
                members.Add((node.MemberName, ArchiveMemberType.File, input));
            }
            else
            {
                source.CaptureDirectory(Inputs, input, node, ct);
                members.Add((node.MemberName, ArchiveMemberType.Directory, input));
                using (ArchiveCapturedInputLeases.Rent(ct)) source.Enumerate(node, child => Enqueue(node, child), ct);
            }
        }
        if (members.Count == 0)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "The selection contains no supported files or folders. No archive was created.");
        members.Sort((a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        ExclusionSamples = samples.AsReadOnly();
        Inputs.SetSelectionDestinationReview((path, identity, token) => source.ReviewDestination(path, identity, selectedDirectories, token));
        Inputs.Seal(ct);
        foreach (var directory in Inputs.Directories.Values) directory.Review(ct);

        void ReserveVisited() { ct.ThrowIfCancellationRequested(); if (++visited > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit(); }
        void Enqueue(ArchiveSelectionNode? parent, ArchiveSelectionEntry entry)
        {
            ReserveVisited();
            // Invalid byte names are reported without inventing a replacement filename to open later.
            if (entry.Kind == ArchiveSelectionKind.UnrepresentableName)
            { Exclude(parent?.MemberName ?? "", ArchiveSelectionExclusionReason.UnrepresentableName); return; }
            RequireLeaf(entry.Leaf);
            int length = checked((parent?.MemberName.Length + 1 ?? 0) + entry.Leaf.Length);
            if (length > ArchiveWritePolicy.MaxNameChars || (parent?.Depth ?? 0) >= MaximumDepth) throw ArchiveWriteBudget.Limit();
            Inputs.RetainSelection(length + (long)entry.Leaf.Length + entry.Identity.Length,
                checked(640L + 2L * (length + (long)entry.Leaf.Length + entry.Identity.Length)));
            string name = parent is null ? entry.Leaf : parent.MemberName + "/" + entry.Leaf;
            if (!Representable(name)) { Exclude(name, ArchiveSelectionExclusionReason.UnrepresentableName); return; }
            if (entry.Kind == ArchiveSelectionKind.Link) { Exclude(name, ArchiveSelectionExclusionReason.Link); return; }
            if (entry.Kind == ArchiveSelectionKind.Special) { Exclude(name, ArchiveSelectionExclusionReason.SpecialFile); return; }
            if (entry.Kind is not (ArchiveSelectionKind.File or ArchiveSelectionKind.Directory) || string.IsNullOrEmpty(entry.Identity)) throw Invalid();
            if (parent is null && entry.Kind == ArchiveSelectionKind.Directory) selectedDirectories.Add(entry.Identity);
            pending.Push(new(parent, entry, name));
        }
    }

    private void Exclude(string name, ArchiveSelectionExclusionReason reason)
    {
        exclusionCounts[(int)reason] = checked(exclusionCounts[(int)reason] + 1);
        if (samples.Count == MaximumExclusionSamples) return;
        Inputs.RetainSelection(name.Length, checked(128L + 2L * name.Length)); samples.Add(new(name, reason));
    }
    internal ArchiveTarTransformPlan CreatePlan(ArchiveWriteProfile profile, ArchiveAssessment? original,
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
