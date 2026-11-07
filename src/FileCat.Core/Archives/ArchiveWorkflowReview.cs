namespace FileCat.Core.Archives;

/// <summary>Explicit recursive scope, including an implicit archive folder without its own physical directory row.</summary>
internal sealed record ArchiveFolderChange
{
    internal ArchiveChangeKind Kind { get; }
    internal string CatalogRevision { get; }
    internal string Path { get; }
    internal string? NewPath { get; }
    internal ArchiveFolderChange(ArchiveChangeKind kind, string catalogRevision, string path, string? newPath = null)
    {
        ArchiveWritePolicy.ValidateDigest(catalogRevision); ArchiveWritePolicy.ValidateName(path, ArchiveMemberType.Directory);
        if (kind is not (ArchiveChangeKind.Delete or ArchiveChangeKind.Rename) || kind == ArchiveChangeKind.Delete && newPath is not null)
            throw Invalid();
        if (kind == ArchiveChangeKind.Rename) ArchiveWritePolicy.ValidateName(newPath!, ArchiveMemberType.Directory);
        Kind = kind; CatalogRevision = catalogRevision; Path = path.TrimEnd('/'); NewPath = newPath?.TrimEnd('/');
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The recursive archive operation is invalid.");
}

/// <summary>
/// Common parent-side workflow review for all container planners. No archive parsing, worker launch or publication.
/// The caller owns the sealed captures and obtains the affirmative replacement/exclusion choices separately.
/// </summary>
internal sealed class ArchiveWorkflowReview
{
    private readonly ArchiveCapturedInputs captured;
    private readonly ArchiveWriteProfile profile;
    private readonly ArchiveAssessment? source;
    private readonly ArchiveTransformEdit[] edits;
    private readonly ArchiveTransformEdit[] replacements;
    private readonly ArchiveTransformAddition[] additions;
    private readonly ArchiveCapturedDirectoryMerge[] merges;
    private readonly long retainedHeapBytes;
    internal int ReplacementNames { get; }
    internal int ReplacedOccurrences { get; }
    internal int MergedDirectories => merges.Length;
    internal int ExcludedCount { get; }
    internal int InputTimePrecisionChanges { get; }
    internal IReadOnlyList<string> ReplacementSamples { get; }

    private ArchiveWorkflowReview(ArchiveCapturedInputs captured, ArchiveWriteProfile profile, ArchiveAssessment? source,
        ArchiveTransformEdit[] edits, ArchiveTransformEdit[] replacements, ArchiveTransformAddition[] additions,
        ArchiveCapturedDirectoryMerge[] merges, int conflicts, int occurrences, int excluded, int precisionChanges, string[] samples, long retainedHeapBytes)
    {
        this.captured = captured; this.profile = profile; this.source = source; this.edits = edits; this.replacements = replacements;
        this.additions = additions; this.merges = merges; ReplacementNames = conflicts; ReplacedOccurrences = occurrences;
        ExcludedCount = excluded; InputTimePrecisionChanges = precisionChanges;
        ReplacementSamples = Array.AsReadOnly(samples); this.retainedHeapBytes = retainedHeapBytes;
    }

    internal static ArchiveWorkflowReview Prepare(ArchiveWriteProfile profile, ArchiveAssessment? source, ArchiveCapturedInputs captured,
        IReadOnlyList<ArchiveTransformEdit> memberEdits, IReadOnlyList<ArchiveFolderChange> folderEdits,
        IReadOnlyList<ArchiveTransformAddition> incoming, int excludedCount, CancellationToken ct, long operationHeapBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(memberEdits); ArgumentNullException.ThrowIfNull(folderEdits); ArgumentNullException.ThrowIfNull(incoming);
        ct.ThrowIfCancellationRequested();
        if (operationHeapBytes < 0 || excludedCount < 0 || excludedCount > ArchiveWritePolicy.MaxMembers || memberEdits.Count > ArchiveWritePolicy.MaxMembers ||
            folderEdits.Count > ArchiveWritePolicy.MaxMembers || incoming.Count > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
        if (source is not null && (source.Manifest.Profile != profile || source.CatalogRevision != source.SourceSha256)) throw Changed();
        if (source is null && (memberEdits.Count != 0 || folderEdits.Count != 0)) throw Changed();
        var members = source?.Manifest.Members ?? []; var cost = captured.Cost;
        var budget = new ArchiveWriteBudget(); budget.RetainCapturedInputs(cost);
        budget.AddMetadata(0, operationHeapBytes);
        budget.AddMetadata(0, checked(4096L + 512L * members.Count + 256L * memberEdits.Count + 256L * folderEdits.Count + 768L * incoming.Count));
        // The source manifest stays live while this review holds its maps and captured inputs. Charge that
        // retained source before allocating the maps; the later format planner separately accounts for it again.
        long sourceHeapBytes = source?.Manifest.Features.Length ?? 0;
        budget.AddMetadata(0, sourceHeapBytes);
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested(); long retained = checked(member.Metadata.Length + 2L * member.Name.Length + 512);
            budget.AddMetadata(0, retained); sourceHeapBytes = checked(sourceHeapBytes + retained);
        }
        // Freeze requests before consulting the catalog. These objects are immutable; their collections need not be.
        var requestedMembers = memberEdits.ToArray(); var requestedFolders = folderEdits.ToArray(); var requestedIncoming = incoming.ToArray();
        var keys = new Dictionary<ArchiveMemberKey, ArchiveManifestMember>();
        var ordered = new (string Name, ArchiveMemberKey Key)[members.Count];
        for (int i = 0; i < members.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var member = members[i];
            var key = new ArchiveMemberKey(source!.CatalogRevision, member.Name, member.Type, member.Occurrence);
            if (!keys.TryAdd(key, member)) throw Changed(); ordered[i] = (member.Name, key);
        }
        Array.Sort(ordered, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        var changes = new Dictionary<ArchiveMemberKey, ArchiveTransformEdit>();
        foreach (var edit in requestedMembers)
        {
            ct.ThrowIfCancellationRequested(); if (edit is null || !keys.ContainsKey(edit.Key)) throw Changed(); AddEdit(edit);
        }
        foreach (var folder in requestedFolders)
        {
            ct.ThrowIfCancellationRequested(); if (folder is null || folder.CatalogRevision != source?.CatalogRevision) throw Changed();
            budget.AddMetadata(0, checked(2L * (folder.Path.Length + 1)));
            int matched = 0; string prefix = folder.Path + "/";
            for (int i = Lower(folder.Path); i < ordered.Length; i++)
            {
                ct.ThrowIfCancellationRequested(); var (name, key) = ordered[i];
                if (name != folder.Path && !name.StartsWith(prefix, StringComparison.Ordinal))
                { if (StringComparer.Ordinal.Compare(name, prefix) >= 0) break; continue; }
                if (name == folder.Path && key.Type != ArchiveMemberType.Directory) throw Invalid("A file cannot be selected as a recursive archive folder.");
                string? destination = null;
                if (folder.Kind == ArchiveChangeKind.Rename)
                {
                    int length = checked(folder.NewPath!.Length + name.Length - folder.Path.Length);
                    if (length > ArchiveWritePolicy.MaxNameChars) throw ArchiveWriteBudget.Limit();
                    budget.AddNameChars(length); budget.AddMetadata(0, checked(256L + length * 4L));
                    destination = folder.NewPath + name[folder.Path.Length..];
                }
                AddEdit(new(folder.Kind, key, destination), folder.Kind == ArchiveChangeKind.Rename); matched++;
            }
            if (matched == 0) throw Changed();
        }
        var active = new Dictionary<string, List<ArchiveMemberKey>>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested(); var key = new ArchiveMemberKey(source!.CatalogRevision, member.Name, member.Type, member.Occurrence);
            changes.TryGetValue(key, out var edit); if (edit?.Kind == ArchiveChangeKind.Delete) continue;
            string name = edit?.NewName ?? member.Name;
            if (member.Type == ArchiveMemberType.Directory) { budget.AddMetadata(0, name.Length * 2L); name = name.TrimEnd('/'); }
            if (!active.TryGetValue(name, out var rows)) active[name] = rows = []; rows.Add(key);
        }
        var additions = new List<ArchiveTransformAddition>(); var replacements = new List<ArchiveTransformEdit>();
        var merges = new List<ArchiveCapturedDirectoryMerge>(); var names = new HashSet<string>(StringComparer.Ordinal); var samples = new List<string>();
        int conflicts = 0, replaced = 0;
        foreach (var addition in requestedIncoming)
        {
            ct.ThrowIfCancellationRequested(); if (addition is null) throw Invalid("An archive addition is missing.");
            if (addition.Type == ArchiveMemberType.File && !captured.Bindings.ContainsKey(addition.InputId!.Value))
                throw Invalid("An archive addition must use a sealed captured input.");
            if (addition.CapturedDirectory is { } owned && !ReferenceEquals(owned.Owner, captured))
                throw Invalid("The incoming directory belongs to a different captured selection.");
            if (addition.Type == ArchiveMemberType.Directory) budget.AddMetadata(0, addition.Name.Length * 2L);
            string name = addition.Type == ArchiveMemberType.Directory ? addition.Name.TrimEnd('/') : addition.Name;
            if (!names.Add(name)) throw Invalid("Two incoming archive members have the same name.");
            if (!active.TryGetValue(name, out var existing)) { additions.Add(addition); continue; }
            if (existing.Any(key => key.Type != addition.Type)) throw Invalid("A file and a folder use the same archive name. Choose a different name.");
            if (existing.Any(changes.ContainsKey)) throw Invalid("A member cannot be edited and replaced or merged in the same reviewed operation.");
            if (addition.Type == ArchiveMemberType.Directory)
            {
                if (addition.CapturedDirectory is { } directory) merges.Add(new(directory.Id, existing[0]));
                continue; // Keep every existing directory row and its optional metadata verbatim.
            }
            conflicts++; replaced = checked(replaced + existing.Count);
            if (samples.Count < ArchiveCapturedSelection.MaximumExclusionSamples) samples.Add(name);
            for (int i = 0; i < existing.Count; i++)
                replacements.Add(i == 0 ? new(ArchiveChangeKind.Replace, existing[i], inputId: addition.InputId) : new(ArchiveChangeKind.Delete, existing[i]));
        }
        if (changes.Count + replacements.Count > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
        int precisionChanges = profile.Format == ArchiveWriteFormat.SevenZip ? additions.Count(a =>
            (a.Type == ArchiveMemberType.File ? captured.Bindings[a.InputId!.Value].Reviewed.Input(a.InputId.Value).ModifiedTimestamp : a.DirectoryMetadata!.Modified).Nanoseconds % 100 != 0) : 0;
        return new(captured, profile, source, changes.Values.ToArray(), replacements.ToArray(), additions.ToArray(), merges.ToArray(), conflicts,
            replaced, excludedCount, precisionChanges, samples.ToArray(), budget.RetainedHeapBytes - cost.HeapBytes - sourceHeapBytes);

        void AddEdit(ArchiveTransformEdit edit, bool reserved = false)
        {
            if (changes.Count >= ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
            if (!changes.TryAdd(edit.Key, edit)) throw Invalid("Overlapping archive selections target the same physical member more than once.");
            if (!reserved) budget.AddMetadata(0, checked(256L + (edit.NewName?.Length ?? 0) * 2L));
        }
        int Lower(string name)
        {
            int first = 0, end = ordered.Length;
            while (first < end) { int mid = first + (end - first) / 2; if (StringComparer.Ordinal.Compare(ordered[mid].Name, name) < 0) first = mid + 1; else end = mid; }
            return first;
        }
    }

    internal ArchiveContainerTransformPlan CreatePlan(bool replaceExisting, bool acceptExclusions, CancellationToken ct, bool acceptTimestampPrecision = false)
    {
        ct.ThrowIfCancellationRequested();
        if (ReplacementNames != 0 && !replaceExisting) throw Invalid("Review and accept the listed archive replacements before continuing.");
        if (ExcludedCount != 0 && !acceptExclusions) throw Invalid("Review and accept excluded source items before continuing.");
        if (InputTimePrecisionChanges != 0 && !acceptTimestampPrecision)
            throw Invalid("7z stores added timestamps to 100 ns precision. Review that format conversion before continuing.");
        var combined = new ArchiveTransformEdit[edits.Length + replacements.Length]; edits.CopyTo(combined, 0); replacements.CopyTo(combined, edits.Length);
        return captured.CreatePlan(profile, source, combined, additions, ct, merges, retainedHeapBytes);
    }
    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidRequest, message);
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "An exact member or recursive folder scope no longer belongs to the reviewed archive catalog.");
}
