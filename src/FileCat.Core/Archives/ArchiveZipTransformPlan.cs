using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>
/// Parent expectation for the worker ZIP profile (plan AW2 task 5): the reviewed source members in order with deletions,
/// renames and payload replacements applied, then the additions. A replacement keeps the member's recorded metadata; a new
/// member's metadata comes from its captured observation (<see cref="ArchiveZipMetadata.ForCaptured"/>). Directory names
/// always end in "/", as ZIP marks folders. Construction grants no IO, worker or publication authority.
/// </summary>
public sealed class ArchiveZipTransformPlan : ArchiveContainerTransformPlan
{
    public override ArchiveAssessment? Source { get; }
    public override IReadOnlyDictionary<int, ArchiveTransformInput> Inputs { get; }
    public override IReadOnlyList<ArchiveTransformEdit> Edits { get; }
    public override IReadOnlyList<ArchiveTransformAddition> Additions { get; }
    public override ArchiveWriteManifest Expected { get; }
    internal override ArchiveCapturedInputCost CaptureCost { get; }
    internal override ArchiveCapturedInputs? CaptureOwner { get; }
    internal override IReadOnlyDictionary<int, ArchiveCapturedDirectory> Directories { get; }
    /// <summary>The parent's ceiling for the staged file, enforced before every stage write.</summary>
    internal long OutputLengthBound { get; }

    private ArchiveZipTransformPlan(ArchiveAssessment? source, Dictionary<int, ArchiveTransformInput> inputs, ArchiveTransformEdit[] edits,
        ArchiveTransformAddition[] additions, ArchiveWriteManifest expected, ArchiveCapturedInputCost captureCost, ArchiveCapturedInputs? owner, long bound)
    {
        Source = source; Inputs = new ReadOnlyDictionary<int, ArchiveTransformInput>(inputs);
        Edits = Array.AsReadOnly(edits); Additions = Array.AsReadOnly(additions); Expected = expected;
        CaptureCost = captureCost; CaptureOwner = owner; Directories = owner?.Directories ?? NoDirectories; OutputLengthBound = bound;
    }

    public static ArchiveZipTransformPlan Create(ArchiveWriteProfile profile, ArchiveAssessment? source, IReadOnlyList<ArchiveTransformInput> inputs,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions, CancellationToken ct = default)
        => CreateCaptured(profile, source, inputs, edits, additions, default, ct);

    internal static ArchiveZipTransformPlan CreateCaptured(ArchiveWriteProfile profile, ArchiveAssessment? source, IReadOnlyList<ArchiveTransformInput> inputs,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions, ArchiveCapturedInputCost captureCost, CancellationToken ct,
        ArchiveCapturedInputs? captureOwner = null, IReadOnlyList<ArchiveCapturedDirectoryMerge>? directoryMerges = null, long retainedHeapBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(edits); ArgumentNullException.ThrowIfNull(additions); ct.ThrowIfCancellationRequested();
        if (inputs.Count > ArchiveWritePolicy.MaxMembers || edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers)
            throw ArchiveWriteBudget.Limit();
        _ = ArchiveZipFeatures.Level(profile);
        if (profile.Encryption.Mode != ArchiveEncryptionMode.None) throw Unsupported();
        if (source is not null)
        {
            ArgumentNullException.ThrowIfNull(source.Manifest);
            if (profile != source.Manifest.Profile) throw Unsupported();
            ArchiveAssessmentWire.Validate(new(profile, source.SourceLength, source.SourceSha256));
            if (source.CatalogRevision != source.SourceSha256) throw Changed();
            ArchiveZipFeatures.Validate(source.Manifest.Features);
        }
        else if (edits.Count != 0) throw Changed();

        var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, retainedHeapBytes);
        budget.RetainCapturedInputs(captureCost);
        budget.AddMetadata(0, 256L * inputs.Count + 128L * edits.Count + 96L * additions.Count);
        var captured = new Dictionary<int, ArchiveTransformInput>(inputs.Count);
        long capturedBytes = source?.Manifest.LogicalBytes ?? 0;
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            if (input is null || !captured.TryAdd(input.Id, input)) throw ArchiveManifestMember.Invalid();
            ArchiveWriteBudget.Reserve(capturedBytes, input.Length, ArchiveWritePolicy.MaxLogicalBytes); capturedBytes += input.Length;
        }
        var keys = new HashSet<ArchiveMemberKey>();
        bool retainsPayload = false;
        foreach (var old in source?.Manifest.Members ?? [])
        {
            ct.ThrowIfCancellationRequested();
            budget.AddNameChars(old.Name.Length);
            budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(old.Name) + old.Metadata.Length + 64L, 2L * old.Name.Length + old.Metadata.Length + 640L);
            ValidateMetadata(old);
            keys.Add(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence));
        }
        if (source is not null)
        {
            try { ValidateNamespace(source.Manifest.Members, ct); }
            catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.InvalidRequest) { throw Unsupported(); }
        }
        var changes = new Dictionary<ArchiveMemberKey, ArchiveTransformEdit>();
        var frozenEdits = new ArchiveTransformEdit[edits.Count];
        var usedInputs = new HashSet<int>(); var usedDirectories = new HashSet<int>();
        long requestedBytes = source?.Manifest.LogicalBytes ?? 0;
        for (int i = 0; i < edits.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var edit = edits[i] ?? throw ArchiveManifestMember.Invalid();
            if (!keys.Contains(edit.Key) || !changes.TryAdd(edit.Key, edit)) throw Changed();
            RequestName(edit.Key.RawPath); if (edit.NewName is not null) RequestName(edit.NewName);
            if (edit.InputId is { } id) Use(id);
            frozenEdits[i] = edit;
        }
        usedDirectories.UnionWith(ArchiveCapturedDirectoryMerge.Review(directoryMerges, captureOwner, source, keys, changes, budget, ct));
        var planned = new List<(ArchiveManifestMember Member, string? OriginalName)>();
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        long bound = checked(22L + 56 + 20);
        long newPayloadBound = 0;
        foreach (var old in source?.Manifest.Members ?? [])
        {
            ct.ThrowIfCancellationRequested();
            changes.TryGetValue(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence), out var edit);
            if (edit?.Kind == ArchiveChangeKind.Delete) continue;
            string name = edit?.NewName is { } renamed ? Name(renamed, old.Type) : old.Name;
            var input = edit?.InputId is { } id ? captured[id] : null;
            if (input is null && old.Type == ArchiveMemberType.File) retainsPayload = true;
            if (input is not null) newPayloadBound = checked(newPayloadBound + DeflateBound(input.Length));
            Add(name, old.Type, input?.Length ?? old.Length, input?.Sha256 ?? old.Sha256, old.Metadata, old.Name);
        }
        var frozenAdditions = new ArchiveTransformAddition[additions.Count];
        for (int i = 0; i < additions.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var addition = additions[i] ?? throw ArchiveManifestMember.Invalid();
            RequestName(addition.Name);
            if (addition.CapturedDirectory is { } directory)
            {
                if (captureOwner is null || !ReferenceEquals(directory.Owner, captureOwner) ||
                    !captureOwner.Directories.TryGetValue(directory.Id, out var expectedDirectory) || !ReferenceEquals(directory, expectedDirectory) ||
                    addition.DirectoryMetadata != new ArchiveTransformDirectoryMetadata(directory.Reviewed.Modified, directory.Reviewed.Mode)) throw Changed();
                usedDirectories.Add(directory.Id);
            }
            var input = addition.InputId is { } id ? Use(id) : null;
            var metadata = input is not null
                ? ArchiveZipMetadata.ForCaptured(ArchiveMemberType.File, input.Mode, input.ModifiedTimestamp, input.LocalOffsetMinutes)
                : ArchiveZipMetadata.ForCaptured(ArchiveMemberType.Directory, addition.DirectoryMetadata!.Mode, addition.DirectoryMetadata.Modified,
                    addition.DirectoryMetadata.LocalOffsetMinutes);
            if (input is not null) newPayloadBound = checked(newPayloadBound + DeflateBound(input.Length));
            Add(Name(addition.Name, addition.Type), addition.Type, input?.Length ?? 0,
                input?.Sha256 ?? "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", metadata.Encode(), originalName: null);
            frozenAdditions[i] = addition;
        }
        if (usedInputs.Count != captured.Count) throw ArchiveManifestMember.Invalid(); // no unrelated read grants
        if (usedDirectories.Count != (captureOwner?.Directories.Count ?? 0)) throw ArchiveManifestMember.Invalid();
        ArchiveNamespaceReview.Validate(source?.Manifest.Members ?? [], planned, budget, ct);
        byte[] features = source is not null ? source.Manifest.Features.ToArray() : ArchiveZipFeatures.Create([]);
        bound = checked(bound + features.Length + newPayloadBound + (retainsPayload ? source!.SourceLength : 0));
        if (bound > ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit();
        var expected = new ArchiveWriteManifest(profile, planned.Select(p => p.Member).ToArray(), features);
        return new(source, captured, frozenEdits, frozenAdditions, expected, captureCost, captureOwner, bound);

        ArchiveTransformInput Use(int id)
        {
            if (!captured.TryGetValue(id, out var input)) throw ArchiveManifestMember.Invalid();
            // Reusing one captured object for several replacements/additions still consumes its payload each time.
            ArchiveWriteBudget.Reserve(requestedBytes, input.Length, ArchiveWritePolicy.MaxLogicalBytes); requestedBytes += input.Length;
            usedInputs.Add(id); return input;
        }
        void RequestName(string name)
        { budget.AddNameChars(name.Length); budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(name), 2L * name.Length + 128); }
        void Add(string name, ArchiveMemberType type, long length, string digest, ReadOnlySpan<byte> metadata, string? originalName)
        {
            budget.AddMember(); budget.AddNameChars(name.Length); budget.AddLogicalBytes(length);
            int nameBytes = ArchiveWritePolicy.Utf8.GetByteCount(name);
            budget.AddMetadata(nameBytes + metadata.Length + 64L, 2L * name.Length + metadata.Length + 640L);
            var key = (name, type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
            var member = new ArchiveManifestMember(name, type, occurrence, length, digest, metadata);
            var decoded = ValidateMetadata(member);
            // Local and central headers (each name, the largest admitted extra fields), the comment and a data descriptor.
            bound = checked(bound + 30 + 46 + 2L * nameBytes + 96 + 81 + ArchiveWritePolicy.Utf8.GetByteCount(decoded.Comment) + 24);
            planned.Add((member, originalName));
        }
    }

    /// <summary>ZIP marks a folder by its final "/"; the worker applies exactly the same rule.</summary>
    public static string Name(string name, ArchiveMemberType type) =>
        type == ArchiveMemberType.Directory && !name.EndsWith('/') ? name + "/" : name;

    /// <summary>A generous per-payload Deflate ceiling (stored blocks cost five bytes per 64 KiB).</summary>
    internal static long DeflateBound(long length) => checked(length + (length + 7) / 8 + 1024);

    internal static long OutputBound(ArchiveZipTransformPlan plan, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return plan.OutputLengthBound; }

    private static ArchiveZipMetadata ValidateMetadata(ArchiveManifestMember member)
    {
        ArchiveZipMetadata metadata;
        try { metadata = ArchiveZipMetadata.Decode(member.Metadata); }
        catch (ArchiveWriteException) { throw Unsupported(); }
        if ((member.Type == ArchiveMemberType.Directory) != member.Name.EndsWith('/') ||
            !ArchiveZipMetadata.AttributesFit(metadata.ExternalAttributes, member.Type)) throw Unsupported();
        // ZIP stores a name's length in 16 bits; longer names (allowed by the shared name rules for TAR) are refused here.
        if (ArchiveWritePolicy.Utf8.GetByteCount(member.Name) > ushort.MaxValue) throw NameTooLong();
        return metadata;
    }

    private static void ValidateNamespace(IReadOnlyList<ArchiveManifestMember> members, CancellationToken ct)
    {
        var names = new Dictionary<string, ArchiveMemberType>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            string name = member.Type == ArchiveMemberType.Directory ? member.Name.TrimEnd('/') : member.Name;
            if (names.TryGetValue(name, out var previous) && previous != member.Type) throw ArchiveManifestMember.Invalid();
            names[name] = member.Type; if (member.Type == ArchiveMemberType.File) files.Add(name);
        }
        // A file cannot also be a folder of other members. Sorted ordinal names find the first possible descendant
        // without walking every prefix of a long name.
        string[] ordered = names.Keys.ToArray(); Array.Sort(ordered, StringComparer.Ordinal);
        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            string prefix = file + "/"; int next = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal);
            if (next < 0) next = ~next;
            if (next < ordered.Length && ordered[next].StartsWith(prefix, StringComparison.Ordinal)) throw ArchiveManifestMember.Invalid();
        }
    }

    private static ArchiveWriteException Unsupported() =>
        new(ArchiveWriteReason.UnsupportedFeature, "The ZIP profile, metadata or requested changes cannot be represented by this writer.");
    private static ArchiveWriteException Changed() =>
        new(ArchiveWriteReason.SourceChanged, "The exact reviewed ZIP member occurrence is no longer available.");
    private static ArchiveWriteException NameTooLong() =>
        new(ArchiveWriteReason.InvalidRequest, "A ZIP archive stores names of at most 65,535 bytes. Choose a shorter name or another format.");
}
