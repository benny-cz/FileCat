using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

public sealed record ArchiveSevenZipPlannedMember(ArchiveManifestMember Expected, int? SourceIndex, int? InputId);

/// <summary>Parent-derived exact expectation, independent of codecs, source paths and publication authority.</summary>
public sealed class ArchiveSevenZipTransformPlan : ArchiveContainerTransformPlan
{
    public override ArchiveAssessment? Source { get; }
    public override IReadOnlyDictionary<int, ArchiveTransformInput> Inputs { get; }
    public override IReadOnlyList<ArchiveTransformEdit> Edits { get; }
    public override IReadOnlyList<ArchiveTransformAddition> Additions { get; }
    public override ArchiveWriteManifest Expected { get; }
    public IReadOnlyList<ArchiveSevenZipPlannedMember> Members { get; }
    internal override ArchiveCapturedInputCost CaptureCost { get; }
    internal override ArchiveCapturedInputs? CaptureOwner { get; }
    internal override IReadOnlyDictionary<int, ArchiveCapturedDirectory> Directories { get; }
    internal long RetainedHeapBytes { get; }
    private const string EmptySha256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

    private ArchiveSevenZipTransformPlan(ArchiveAssessment? source, Dictionary<int, ArchiveTransformInput> inputs,
        ArchiveTransformEdit[] edits, ArchiveTransformAddition[] additions, List<ArchiveSevenZipPlannedMember> members,
        ArchiveWriteManifest expected, ArchiveCapturedInputCost captureCost, ArchiveCapturedInputs? captureOwner, long retainedHeapBytes)
    {
        Source = source; Inputs = new ReadOnlyDictionary<int, ArchiveTransformInput>(inputs); Edits = Array.AsReadOnly(edits);
        Additions = Array.AsReadOnly(additions); Members = members.AsReadOnly(); Expected = expected; CaptureCost = captureCost;
        CaptureOwner = captureOwner; Directories = captureOwner?.Directories ?? NoDirectories;
        RetainedHeapBytes = retainedHeapBytes;
    }
    public static ArchiveSevenZipTransformPlan Create(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, CancellationToken ct, long retainedHeapBytes = 0)
        => CreateCore(profile, source, inputs, edits, additions, retainedHeapBytes, default, null, ct);
    internal static ArchiveSevenZipTransformPlan CreateCaptured(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, ArchiveCapturedInputCost captureCost, CancellationToken ct, ArchiveCapturedInputs? owner = null)
        => CreateCore(profile, source, inputs, edits, additions, 0, captureCost, owner, ct);
    private static ArchiveSevenZipTransformPlan CreateCore(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, long retainedHeapBytes, ArchiveCapturedInputCost captureCost,
        ArchiveCapturedInputs? captureOwner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(additions); ct.ThrowIfCancellationRequested();
        if (profile.Version != 2 || profile.Format != ArchiveWriteFormat.SevenZip || source is not null && source.Manifest.Profile != profile) throw Unsupported();
        if (inputs.Count > ArchiveWritePolicy.MaxMembers || edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers) throw ArchiveWriteBudget.Limit();
        if (source is not null)
        {
            ArchiveAssessmentWire.Validate(new(profile, source.SourceLength, source.SourceSha256));
            if (source.CatalogRevision != source.SourceSha256) throw Changed();
        }
        else if (edits.Count != 0) throw Changed();
        var originalFeatures = source is null ? null : ArchiveSevenZipFeatures.Decode(source.Manifest.Features);
        if (source is not null) ArchiveSevenZipEncoding.ValidateFeatures(source.Manifest.Members, originalFeatures!);
        if (originalFeatures?.HeaderGraph is { } header) ArchiveSevenZipEncoding.ValidateHeader(header);
        var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, retainedHeapBytes); budget.RetainCapturedInputs(captureCost);
        budget.AddMetadata(0, checked(256L * inputs.Count + 128L * edits.Count + 96L * additions.Count));
        var captured = new Dictionary<int, ArchiveTransformInput>(inputs.Count); long capturedBytes = source?.Manifest.LogicalBytes ?? 0;
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested(); if (input is null || !captured.TryAdd(input.Id, input)) throw ArchiveManifestMember.Invalid();
            ArchiveWriteBudget.Reserve(capturedBytes, input.Length, ArchiveWritePolicy.MaxLogicalBytes); capturedBytes += input.Length;
        }
        var oldMembers = source?.Manifest.Members ?? []; var keys = new HashSet<ArchiveMemberKey>(); bool encrypted = false;
        foreach (var old in oldMembers)
        {
            ct.ThrowIfCancellationRequested(); budget.AddNameChars(old.Name.Length);
            budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(old.Name) + old.Metadata.Length + 64L, checked(4L * old.Name.Length + 6L * old.Metadata.Length + 1024));
            encrypted |= Metadata(old).Graph?.Encrypted == true; keys.Add(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence));
        }
        if (source is not null)
        {
            var mode = originalFeatures!.HiddenNames ? ArchiveEncryptionMode.DataAndNames : encrypted ? ArchiveEncryptionMode.Data : ArchiveEncryptionMode.None;
            if (profile.Encryption.Mode != mode) throw Unsupported();
            try { Namespace(oldMembers.Select(m => (m, false)), ct); }
            catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.InvalidRequest) { throw Unsupported(); }
        }
        var changes = new Dictionary<ArchiveMemberKey, ArchiveTransformEdit>(); var frozenEdits = new ArchiveTransformEdit[edits.Count];
        var used = new HashSet<int>(); var usedDirectories = new HashSet<int>(); long requested = source?.Manifest.LogicalBytes ?? 0;
        for (int i = 0; i < edits.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var edit = edits[i] ?? throw ArchiveManifestMember.Invalid();
            if (!keys.Contains(edit.Key) || !changes.TryAdd(edit.Key, edit)) throw Changed();
            if (edit.InputId is { } id) Use(id); frozenEdits[i] = edit;
        }
        var members = new List<ArchiveSevenZipPlannedMember>(); var namespaceRows = new List<(ArchiveManifestMember, bool)>();
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        for (int i = 0; i < oldMembers.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var old = oldMembers[i]; changes.TryGetValue(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence), out var edit);
            if (edit?.Kind == ArchiveChangeKind.Delete) continue;
            string name = edit?.NewName ?? old.Name; var input = edit?.InputId is { } id ? captured[id] : null;
            var metadata = Metadata(old);
            if (input is not null) metadata = new(metadata.Type, metadata.Attributes, metadata.CreatedFileTime, metadata.AccessedFileTime,
                metadata.ModifiedFileTime, ArchiveSevenZipEncoding.Payload(profile, input.Length));
            Add(name, old.Type, input?.Length ?? old.Length, input?.Sha256 ?? old.Sha256, metadata.Encode(), input is null ? i : null, input?.Id, name != old.Name);
        }
        var frozenAdditions = new ArchiveTransformAddition[additions.Count];
        for (int i = 0; i < additions.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var addition = additions[i] ?? throw ArchiveManifestMember.Invalid();
            if (addition.CapturedDirectory is { } directory)
            {
                if (captureOwner is null || !ReferenceEquals(directory.Owner, captureOwner) || !captureOwner.Directories.TryGetValue(directory.Id, out var expectedDirectory) ||
                    !ReferenceEquals(directory, expectedDirectory) || addition.DirectoryMetadata != new ArchiveTransformDirectoryMetadata(directory.Reviewed.Modified, directory.Reviewed.Mode)) throw Changed();
                usedDirectories.Add(directory.Id);
            }
            var input = addition.InputId is { } id ? Use(id) : null;
            var metadata = ArchiveSevenZipEncoding.Captured(addition.Type, input?.Length ?? 0, input?.Mode ?? addition.DirectoryMetadata!.Mode,
                input?.ModifiedTimestamp ?? addition.DirectoryMetadata!.Modified, profile);
            Add(addition.Name, addition.Type, input?.Length ?? 0, input?.Sha256 ?? EmptySha256, metadata.Encode(), null, input?.Id, true); frozenAdditions[i] = addition;
        }
        if (used.Count != captured.Count || usedDirectories.Count != (captureOwner?.Directories.Count ?? 0)) throw ArchiveManifestMember.Invalid();
        Namespace(namespaceRows, ct);
        var features = ArchiveSevenZipEncoding.Features(profile, members.Select(m => m.Expected).ToArray(), originalFeatures);
        ArchiveSevenZipEncoding.ValidateFeatures(members.Select(m => m.Expected).ToArray(), features);
        if (profile.Encryption.Mode == ArchiveEncryptionMode.Data && !members.Any(m => Metadata(m.Expected).Graph?.Encrypted == true))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "Payload password protection requires nonempty file data. Use hidden names for an archive containing only empty files or folders.");
        byte[] featureBytes = features.Encode(); budget.AddMetadata(0, checked(featureBytes.Length * 4L + members.Count * 16L));
        return new(source, captured, frozenEdits, frozenAdditions, members, new(profile, members.Select(m => m.Expected).ToArray(), featureBytes), captureCost, captureOwner, budget.RetainedHeapBytes);

        ArchiveTransformInput Use(int id)
        {
            if (!captured.TryGetValue(id, out var input)) throw ArchiveManifestMember.Invalid();
            ArchiveWriteBudget.Reserve(requested, input.Length, ArchiveWritePolicy.MaxLogicalBytes); requested += input.Length; used.Add(id); return input;
        }
        void Add(string name, ArchiveMemberType type, long length, string digest, byte[] metadata, int? sourceIndex, int? inputId, bool changed)
        {
            budget.AddMember(); budget.AddLogicalBytes(length); budget.AddNameChars(name.Length);
            budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(name) + metadata.Length + 64L, checked(name.Length * 4L + metadata.Length * 8L + 1024));
            var key = (name, type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
            var member = new ArchiveManifestMember(name, type, occurrence, length, digest, metadata); Metadata(member);
            members.Add(new(member, sourceIndex, inputId)); namespaceRows.Add((member, changed));
        }
    }
    private static ArchiveSevenZipMetadata Metadata(ArchiveManifestMember member)
    {
        var metadata = ArchiveSevenZipMetadata.Decode(member.Metadata);
        if (metadata.Type != member.Type || member.Length > 0 && metadata.Graph is null || metadata.Graph is { Encrypted: true } graph && !graph.FolderCrcPresent && !graph.MemberCrcPresent) throw Unsupported();
        return metadata;
    }
    private static void Namespace(IEnumerable<(ArchiveManifestMember Member, bool Changed)> members, CancellationToken ct)
    {
        var names = new Dictionary<string, (ArchiveMemberType Type, bool Changed)>(StringComparer.Ordinal); var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (member, changed) in members)
        {
            ct.ThrowIfCancellationRequested(); string name = member.Type == ArchiveMemberType.Directory ? member.Name.TrimEnd('/') : member.Name;
            if (names.TryGetValue(name, out var previous) && (changed || previous.Changed || previous.Type != member.Type)) throw ArchiveManifestMember.Invalid();
            names[name] = (member.Type, changed); if (member.Type == ArchiveMemberType.File) files.Add(name);
        }
        string[] ordered = names.Keys.ToArray(); Array.Sort(ordered, StringComparer.Ordinal);
        foreach (string file in files)
        { ct.ThrowIfCancellationRequested(); string prefix = file + "/"; int next = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal); if (next < 0) next = ~next; if (next < ordered.Length && ordered[next].StartsWith(prefix, StringComparison.Ordinal)) throw ArchiveManifestMember.Invalid(); }
    }
    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.UnsupportedFeature, "The complete 7z source metadata, encoded header or password mode cannot be preserved by this operation.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The exact reviewed 7z member occurrence is no longer available.");
}
