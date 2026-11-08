namespace FileCat.Core.Archives;

/// <summary>Translate frozen user intent against one complete catalog; never infer a format from a filename.</summary>
internal sealed class ArchiveJobPlanning
{
    private readonly ArchivePlan plan;
    private readonly Dictionary<string, int> selectors;
    internal IReadOnlyList<ArchiveSelectionSource> Sources { get; }
    internal long RetainedNameChars { get; }
    internal long RetainedHeapBytes { get; }

    internal ArchiveJobPlanning(ArchivePlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Profile is null || plan.Changes.Count > ArchiveWritePolicy.MaxMembers) throw Invalid();
        this.plan = plan;
        // Windows also supports case-sensitive directories. Only identical path spellings may be
        // coalesced before native capture proves which objects they identify.
        selectors = new(StringComparer.Ordinal);
        var sources = new List<ArchiveSelectionSource>();
        var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, 4096);
        budget.AddNameChars(plan.ArchivePath.Length);
        budget.AddMetadata(0, checked(4L * plan.ArchivePath.Length + 2L * (plan.NativeBaseline?.Origin.Bytes.Length ?? 0)));
        foreach (var change in plan.Changes)
        {
            ct.ThrowIfCancellationRequested();
            if (change is null || !Enum.IsDefined(change.Kind) || change.Ordinal is < 0 or >= ArchiveWritePolicy.MaxMembers ||
                change.Kind is not (ArchiveChangeKind.Delete or ArchiveChangeKind.Rename or ArchiveChangeKind.Replace) && change.Ordinal is not null ||
                (change.Kind == ArchiveChangeKind.Rename) != (change.NewMemberPath is not null)) throw Invalid();
            var type = change.Kind is ArchiveChangeKind.AddFolder or ArchiveChangeKind.CreateFolderEntry ? ArchiveMemberType.Directory : ArchiveMemberType.File;
            ArchiveWritePolicy.ValidateName(change.MemberPath, type);
            if (change.NewMemberPath is not null) ArchiveWritePolicy.ValidateName(change.NewMemberPath);
            long chars = checked((long)change.MemberPath.Length + (change.SourcePath?.Length ?? 0) + (change.NewMemberPath?.Length ?? 0));
            budget.AddNameChars(chars); budget.AddMetadata(0, checked(1024 + 4 * chars));
            bool needsSource = change.Kind is ArchiveChangeKind.AddFile or ArchiveChangeKind.AddFolder or ArchiveChangeKind.Replace;
            if (needsSource != (change.SourcePath is not null)) throw Invalid();
            if (!needsSource) continue;
            if (!Path.IsPathFullyQualified(change.SourcePath!)) throw Invalid();
            string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(change.SourcePath!));
            string leaf = Path.GetFileName(path);
            if (string.IsNullOrEmpty(leaf)) throw Invalid();
            var kind = change.Kind == ArchiveChangeKind.AddFolder ? ArchiveSelectionKind.Directory : ArchiveSelectionKind.File;
            if (selectors.TryGetValue(path, out int selector))
            {
                if (sources[selector - 1].Kind != kind) throw Invalid();
                continue;
            }
            budget.AddNameChars(path.Length + (long)leaf.Length);
            budget.AddMetadata(0, checked(256L + 2L * (path.Length + (long)leaf.Length)));
            selectors.Add(path, sources.Count + 1); sources.Add(new(path, leaf, kind));
        }
        Sources = sources.AsReadOnly(); RetainedNameChars = budget.NameChars; RetainedHeapBytes = budget.RetainedHeapBytes;
    }

    internal ArchiveWorkflowReview Review(ArchiveAssessment? source, ArchiveCapturedSelection? selection,
        ArchiveCapturedInputs captured, CancellationToken ct)
    {
        if (selection is not null && !ReferenceEquals(selection.Inputs, captured) ||
            (Sources.Count != 0) != (selection is not null) || (plan.Baseline is null) != (source is null)) throw Invalid();
        if (selection is not null)
        {
            if (selection.SourceSpecification is not { } specification || specification.Count != Sources.Count) throw Invalid();
            for (int i = 0; i < Sources.Count; i++) { ct.ThrowIfCancellationRequested(); if (Sources[i] != specification[i]) throw Invalid(); }
        }
        var members = source?.Manifest.Members ?? [];
        // Reserve every temporary index before allocating it. The catalog and captured owners stay live.
        var budget = new ArchiveWriteBudget(); budget.RetainCapturedInputs(captured.Cost);
        long sourceBytes = source?.Manifest.Features.Length ?? 0;
        long directoryIndexNames = 0;
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            sourceBytes = checked(sourceBytes + 512L + member.Metadata.Length + 2L * member.Name.Length);
            if (member.Type == ArchiveMemberType.Directory) directoryIndexNames = checked(directoryIndexNames + 2L * member.Name.Length);
        }
        budget.AddMetadata(0, sourceBytes);
        long operationBytes = checked(4096L + 512L * members.Count + 256L * plan.Changes.Count + directoryIndexNames +
            256L * (selection?.Members.Count ?? 0));
        budget.AddMetadata(0, operationBytes);
        var names = new Dictionary<string, NameGroup>(StringComparer.Ordinal);
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            occurrences.TryGetValue((member.Name, member.Type), out int ordinal);
            occurrences[(member.Name, member.Type)] = checked(ordinal + 1);
            string canonical = member.Type == ArchiveMemberType.Directory ? member.Name.TrimEnd('/') : member.Name;
            if (!names.TryGetValue(canonical, out var group)) names.Add(canonical, group = new());
            if (member.Type == ArchiveMemberType.Directory) group.Directory = true;
            else group.Files.Add(new(source!.CatalogRevision, member.Name, member.Type, ordinal));
        }
        var ordered = names.Keys.ToArray(); Array.Sort(ordered, StringComparer.Ordinal);
        var byRoot = new Dictionary<int, List<ArchiveCapturedMember>>();
        foreach (var member in selection?.Members ?? [])
        {
            ct.ThrowIfCancellationRequested();
            if (!byRoot.TryGetValue(member.RootSelector, out var list)) byRoot.Add(member.RootSelector, list = []);
            list.Add(member);
        }
        var edits = new List<ArchiveTransformEdit>(); var folders = new List<ArchiveFolderChange>();
        var editedKeys = new HashSet<ArchiveMemberKey>(); var folderScopes = new HashSet<string>(StringComparer.Ordinal);
        var incoming = new List<ArchiveTransformAddition>();
        foreach (var change in plan.Changes)
        {
            ct.ThrowIfCancellationRequested();
            if (change.Kind is ArchiveChangeKind.AddFile or ArchiveChangeKind.AddFolder)
            {
                int selector = Selector(change.SourcePath!);
                if (!byRoot.TryGetValue(selector, out var selected)) continue; // Excluded, explicitly reviewed below.
                string oldRoot = Sources[selector - 1].MemberName;
                foreach (var member in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    if (member.Name != oldRoot && !member.Name.StartsWith(oldRoot + "/", StringComparison.Ordinal)) throw Invalid();
                    string prefix = change.Kind == ArchiveChangeKind.AddFolder ? change.MemberPath.TrimEnd('/') : change.MemberPath;
                    int suffixLength = checked(member.Name.Length - oldRoot.Length);
                    int length = checked(prefix.Length + suffixLength);
                    if (length > ArchiveWritePolicy.MaxNameChars || incoming.Count >= ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
                    budget.AddNameChars(length); budget.AddMetadata(0, checked(1024L + 4L * length));
                    string output = suffixLength == 0 ? prefix : string.Concat(prefix, member.Name.AsSpan(oldRoot.Length));
                    incoming.Add(member.Type == ArchiveMemberType.Directory ? captured.DirectoryAddition(output, member.Id) : new(output, member.Type, member.Id));
                }
                continue;
            }
            if (change.Kind == ArchiveChangeKind.CreateFolderEntry)
            {
                if (incoming.Count >= ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
                budget.AddNameChars(change.MemberPath.Length);
                budget.AddMetadata(0, checked(1024L + 4L * change.MemberPath.Length));
                incoming.Add(new(change.MemberPath, new ArchiveTransformDirectoryMetadata(DateTimeOffset.UtcNow)));
                continue;
            }
            if (source is null) throw Changed();
            string name = change.MemberPath.TrimEnd('/');
            names.TryGetValue(name, out var candidates);
            IReadOnlyList<ArchiveMemberKey> files = name == change.MemberPath ? candidates?.Files ?? [] : [];
            bool folder = candidates?.Directory == true || HasDescendants(name);
            if (change.Ordinal is not null)
            {
                int ordinal = change.Ordinal.Value;
                if (ordinal >= files.Count || files[ordinal].Occurrence != ordinal) throw Changed();
                files = [files[ordinal]];
                folder = false;
            }
            else if (files.Count != 0 && folder) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest,
                "A file and a folder have this archive spelling. Select an exact member before changing it.");
            if (folder)
            {
                if (change.Kind == ArchiveChangeKind.Replace) throw Invalid();
                if (!folderScopes.Add(name)) throw Invalid();
                folders.Add(new(change.Kind, source.CatalogRevision, name, change.NewMemberPath));
                continue;
            }
            if (files.Count == 0 || files.Count != 1 && change.Kind != ArchiveChangeKind.Delete) throw Changed();
            int? inputId = null;
            if (change.Kind == ArchiveChangeKind.Replace)
            {
                if (!byRoot.TryGetValue(Selector(change.SourcePath!), out var selected)) continue;
                if (selected.Count != 1 || selected[0].Type != ArchiveMemberType.File) throw Invalid();
                inputId = selected[0].Id;
            }
            foreach (var key in files)
            {
                ct.ThrowIfCancellationRequested();
                if (edits.Count >= ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
                if (!editedKeys.Add(key)) throw Invalid();
                edits.Add(new(change.Kind, key, change.NewMemberPath, inputId));
            }
        }
        return ArchiveWorkflowReview.Prepare(plan.Profile!, source, captured, edits, folders, incoming,
            selection?.ExcludedCount ?? 0, ct, operationBytes);

        bool HasDescendants(string name)
        {
            string prefix = name + "/"; int index = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal);
            if (index < 0) index = ~index;
            return index < ordered.Length && ordered[index].StartsWith(prefix, StringComparison.Ordinal);
        }
    }
    private int Selector(string sourcePath) => selectors[Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath))];
    private sealed class NameGroup
    {
        internal readonly List<ArchiveMemberKey> Files = [];
        internal bool Directory;
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The queued archive change is invalid or ambiguous.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The queued archive member no longer matches its complete reviewed catalog.");
}
