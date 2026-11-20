using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>One ordered output occurrence. Source indices refer to the complete reviewed catalog, never a path.</summary>
public sealed record ArchiveRar5PlannedMember(ArchiveManifestMember Expected, int? SourceIndex, int? InputId);

/// <summary>
/// Parent expectation for a non-solid RAR5 rewrite, derived without a codec or secret. Complete source assessment,
/// numeric capture facts and exact occurrence edits are required. This object grants no IO or publication authority.
/// </summary>
public sealed class ArchiveRar5TransformPlan : ArchiveContainerTransformPlan
{
    public override ArchiveAssessment? Source { get; }
    public override IReadOnlyDictionary<int, ArchiveTransformInput> Inputs { get; }
    public override IReadOnlyList<ArchiveTransformEdit> Edits { get; }
    public override IReadOnlyList<ArchiveTransformAddition> Additions { get; }
    public IReadOnlyList<ArchiveRar5PlannedMember> Members { get; }
    public override ArchiveWriteManifest Expected { get; }
    internal override ArchiveCapturedInputCost CaptureCost { get; }
    internal override ArchiveCapturedInputs? CaptureOwner { get; }
    internal override IReadOnlyDictionary<int, ArchiveCapturedDirectory> Directories { get; }
    private const string EmptySha256 = "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855";

    private ArchiveRar5TransformPlan(ArchiveAssessment? source, Dictionary<int, ArchiveTransformInput> inputs,
        ArchiveTransformEdit[] edits, ArchiveTransformAddition[] additions, List<ArchiveRar5PlannedMember> members, ArchiveWriteManifest expected,
        ArchiveCapturedInputCost captureCost, ArchiveCapturedInputs? captureOwner)
    {
        Source = source; Inputs = new ReadOnlyDictionary<int, ArchiveTransformInput>(inputs);
        Edits = Array.AsReadOnly(edits); Additions = Array.AsReadOnly(additions); Members = members.AsReadOnly(); Expected = expected;
        CaptureCost = captureCost; CaptureOwner = captureOwner; Directories = captureOwner?.Directories ?? NoDirectories;
    }

    public static ArchiveRar5TransformPlan Create(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, CancellationToken ct = default)
        => CreateObserved(profile, source, inputs, edits, additions, 0, ct);

    internal static ArchiveRar5TransformPlan CreateObserved(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, long retainedHeapBytes, CancellationToken ct)
        => CreateCore(profile, source, inputs, edits, additions, retainedHeapBytes, default, null, ct);

    internal static ArchiveRar5TransformPlan CreateCaptured(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, ArchiveCapturedInputCost captureCost, CancellationToken ct, ArchiveCapturedInputs? owner = null,
        IReadOnlyList<ArchiveCapturedDirectoryMerge>? directoryMerges = null, long retainedHeapBytes = 0)
        => CreateCore(profile, source, inputs, edits, additions, retainedHeapBytes, captureCost, owner, ct, directoryMerges);

    private static ArchiveRar5TransformPlan CreateCore(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, long retainedHeapBytes, ArchiveCapturedInputCost captureCost,
        ArchiveCapturedInputs? captureOwner, CancellationToken ct, IReadOnlyList<ArchiveCapturedDirectoryMerge>? directoryMerges = null)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(edits); ArgumentNullException.ThrowIfNull(additions); ct.ThrowIfCancellationRequested();
        if (source is not null) ArgumentNullException.ThrowIfNull(source.Manifest);
        if (profile.Version != 2 || profile.Format != ArchiveWriteFormat.Rar5 || source is not null && source.Manifest.Profile != profile) throw Unsupported();
        if (inputs.Count > ArchiveWritePolicy.MaxMembers || edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers)
            throw ArchiveWriteBudget.Limit();
        if (source is not null)
        {
            ArgumentNullException.ThrowIfNull(source.Manifest);
            ArchiveAssessmentWire.Validate(new(profile, source.SourceLength, source.SourceSha256));
            if (source.SourceSha256 != source.CatalogRevision) throw Changed();
        }
        else if (edits.Count != 0) throw Changed();
        var features = source is null ? new ArchiveRar5Features(hiddenNames: profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames,
            headerPasswordCheckPresent: profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames ? true : null,
            headerCountPower: profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames ? profile.Encryption.CountPower : null)
            : ArchiveRar5Features.Decode(source.Manifest.Features);
        if (features.HiddenNames != (profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames) ||
            features.HiddenNames && features.HeaderCountPower != profile.Encryption.CountPower) throw Unsupported();

        var budget = new ArchiveWriteBudget(); budget.AddMetadata(0, retainedHeapBytes); budget.RetainCapturedInputs(captureCost);
        budget.AddMetadata(0, checked(256L * inputs.Count + 128L * edits.Count + 96L * additions.Count));
        var captured = new Dictionary<int, ArchiveTransformInput>(inputs.Count); long capturedBytes = source?.Manifest.LogicalBytes ?? 0;
        foreach (var input in inputs)
        {
            ct.ThrowIfCancellationRequested();
            if (input is null || !captured.TryAdd(input.Id, input)) throw ArchiveManifestMember.Invalid();
            ArchiveWriteBudget.Reserve(capturedBytes, input.Length, ArchiveWritePolicy.MaxLogicalBytes); capturedBytes += input.Length;
        }
        var oldMembers = source?.Manifest.Members ?? []; var keys = new HashSet<ArchiveMemberKey>(); bool sourceEncrypted = false;
        foreach (var old in oldMembers)
        {
            ct.ThrowIfCancellationRequested();
            budget.AddNameChars(old.Name.Length); budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(old.Name) + old.Metadata.Length + 64L,
                checked(4L * old.Name.Length + 6L * old.Metadata.Length + 1024));
            var metadata = ValidateMetadata(old); sourceEncrypted |= metadata.EncryptionFlags is not null;
            keys.Add(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence));
        }
        if (source is not null)
        {
            var mode = features.HiddenNames ? ArchiveEncryptionMode.DataAndNames : sourceEncrypted ? ArchiveEncryptionMode.Data : ArchiveEncryptionMode.None;
            if (profile.Encryption.Mode != mode) throw Unsupported();
            try { Namespace(oldMembers.Select(member => (member, Changed: false)), ct); }
            catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.InvalidRequest) { throw Unsupported(); }
        }
        var changes = new Dictionary<ArchiveMemberKey, ArchiveTransformEdit>(); var frozenEdits = new ArchiveTransformEdit[edits.Count];
        var used = new HashSet<int>(); var usedDirectories = new HashSet<int>(); long requested = source?.Manifest.LogicalBytes ?? 0;
        for (int index = 0; index < edits.Count; index++)
        {
            ct.ThrowIfCancellationRequested(); var edit = edits[index] ?? throw ArchiveManifestMember.Invalid();
            if (!keys.Contains(edit.Key) || !changes.TryAdd(edit.Key, edit)) throw Changed();
            RequestName(edit.Key.RawPath); if (edit.NewName is not null) RequestName(edit.NewName);
            if (edit.InputId is { } id) Use(id); frozenEdits[index] = edit;
        }
        usedDirectories.UnionWith(ArchiveCapturedDirectoryMerge.Review(directoryMerges, captureOwner, source, keys, changes, budget, ct));
        var members = new List<ArchiveRar5PlannedMember>(); var namespaceRows = new List<(ArchiveManifestMember, string?)>();
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        for (int index = 0; index < oldMembers.Count; index++)
        {
            ct.ThrowIfCancellationRequested(); var old = oldMembers[index];
            changes.TryGetValue(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence), out var edit);
            if (edit?.Kind == ArchiveChangeKind.Delete) continue;
            string name = edit?.NewName ?? old.Name; var input = edit?.InputId is { } id ? captured[id] : null;
            byte[] metadata = input is null ? old.Metadata.ToArray() : Replacement(ArchiveRar5Metadata.Decode(old.Metadata), input.Length, profile).Encode();
            Add(name, old.Type, input?.Length ?? old.Length, input?.Sha256 ?? old.Sha256, metadata, input is null ? index : null,
                input?.Id, old.Name);
        }
        var frozenAdditions = new ArchiveTransformAddition[additions.Count];
        for (int index = 0; index < additions.Count; index++)
        {
            ct.ThrowIfCancellationRequested(); var addition = additions[index] ?? throw ArchiveManifestMember.Invalid(); RequestName(addition.Name);
            if (addition.CapturedDirectory is { } directory)
            {
                if (captureOwner is null || !ReferenceEquals(directory.Owner, captureOwner) ||
                    !captureOwner.Directories.TryGetValue(directory.Id, out var expectedDirectory) || !ReferenceEquals(directory, expectedDirectory) ||
                    addition.DirectoryMetadata != new ArchiveTransformDirectoryMetadata(directory.Reviewed.Modified, directory.Reviewed.Mode)) throw Changed();
                usedDirectories.Add(directory.Id);
            }
            var input = addition.InputId is { } id ? Use(id) : null;
            Add(addition.Name, addition.Type, input?.Length ?? 0, input?.Sha256 ?? EmptySha256,
                NewMetadata(addition.Type, input?.Length ?? 0, input?.Mode ?? addition.DirectoryMetadata!.Mode,
                    input?.ModifiedTimestamp ?? addition.DirectoryMetadata!.Modified, profile).Encode(), null, input?.Id, null);
            frozenAdditions[index] = addition;
        }
        if (used.Count != captured.Count) throw ArchiveManifestMember.Invalid();
        if (usedDirectories.Count != (captureOwner?.Directories.Count ?? 0)) throw ArchiveManifestMember.Invalid();
        ArchiveNamespaceReview.Validate(oldMembers, namespaceRows, budget, ct);
        if (profile.Encryption.Mode == ArchiveEncryptionMode.Data && !members.Any(member => ArchiveRar5Metadata.Decode(member.Expected.Metadata).EncryptionFlags is not null))
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "Payload password protection requires at least one encrypted file. Use hidden names for an archive containing only folders.");
        var expected = new ArchiveWriteManifest(profile, members.Select(member => member.Expected).ToArray(), features.Encode());
        return new(source, captured, frozenEdits, frozenAdditions, members, expected, captureCost, captureOwner);

        ArchiveTransformInput Use(int id)
        {
            if (!captured.TryGetValue(id, out var input)) throw ArchiveManifestMember.Invalid();
            ArchiveWriteBudget.Reserve(requested, input.Length, ArchiveWritePolicy.MaxLogicalBytes); requested += input.Length;
            used.Add(id); return input;
        }
        void RequestName(string name)
        {
            int bytes = ArchiveWritePolicy.Utf8.GetByteCount(name); if (bytes > ArchiveWritePolicy.MaxRar5NameBytes) throw ArchiveWriteBudget.Limit();
            budget.AddNameChars(name.Length); budget.AddMetadata(bytes, 2L * name.Length + 128);
        }
        void Add(string name, ArchiveMemberType type, long length, string digest, ReadOnlySpan<byte> metadata, int? sourceIndex, int? inputId, string? originalName)
        {
            budget.AddMember(); budget.AddNameChars(name.Length); budget.AddLogicalBytes(length);
            budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(name) + metadata.Length + 64L, checked(4L * name.Length + 4L * metadata.Length + 1024));
            var key = (name, type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
            var member = new ArchiveManifestMember(name, type, occurrence, length, digest, metadata); ValidateMetadata(member);
            members.Add(new(member, sourceIndex, inputId)); namespaceRows.Add((member, originalName));
        }
    }

    private static ArchiveRar5Metadata ValidateMetadata(ArchiveManifestMember member)
    {
        if (ArchiveWritePolicy.Utf8.GetByteCount(member.Name) > ArchiveWritePolicy.MaxRar5NameBytes) throw ArchiveWriteBudget.Limit();
        var metadata = ArchiveRar5Metadata.Decode(member.Metadata); if (metadata.Type != member.Type) throw Unsupported();
        if (((metadata.Compression >> 7) & 7) != 0)
        {
            long dictionary = 0x20000L << (int)((metadata.Compression >> 10) & 31);
            if ((metadata.Compression & 63) == 1) dictionary += dictionary / 32 * (long)((metadata.Compression >> 15) & 31);
            if (dictionary > 64L << 20) throw ArchiveWriteBudget.Limit();
        }
        if (metadata.EncryptionFlags is { } flags && (flags & 1) == 0 && !metadata.HasCrc32 && !metadata.HasBlake2sp)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This encrypted RAR member has no password check or content checksum to establish its password.");
        return metadata;
    }
    private static ulong Compression(ArchiveWriteProfile profile, long length) => length == 0 ? 0 : (ulong)profile.CompressionLevel << 7;
    private static ArchiveRar5Metadata Replacement(ArchiveRar5Metadata old, long length, ArchiveWriteProfile profile) => new(old.Type,
        old.HeaderFlags | 2, old.Attributes, old.Host, Compression(profile, length), old.BasicModifiedSeconds, old.HasCrc32,
        old.HasBlake2sp, old.Times, old.Owner, old.EncryptionFlags, old.EncryptionFlags is null ? null : profile.Encryption.CountPower);
    private static ArchiveRar5Metadata NewMetadata(ArchiveMemberType type, long length, int mode, ArchiveTimestamp modified, ArchiveWriteProfile profile)
    {
        ArchiveRar5Times times;
        if (modified.Seconds is >= 0 and <= uint.MaxValue) times = new(19, modified);
        else if (modified.Seconds >= ArchiveRar5Metadata.FileTimeEpochSeconds && modified.FitsTicks) times = new(2, modified);
        else throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "RAR cannot represent this modification time exactly with its supported Unix/FILETIME fields.");
        bool encrypted = type == ArchiveMemberType.File && profile.Encryption.Mode != ArchiveEncryptionMode.None;
        return new(type, type == ArchiveMemberType.File ? 3UL : 1UL, (uint)(mode | (type == ArchiveMemberType.File ? 0x8000 : 0x4000)), 1,
            type == ArchiveMemberType.File ? Compression(profile, length) : 0, hasCrc32: true, times: times,
            encryptionFlags: encrypted ? 3 : null, countPower: encrypted ? profile.Encryption.CountPower : null);
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
        {
            ct.ThrowIfCancellationRequested(); string prefix = file + "/"; int next = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal);
            if (next < 0) next = ~next; if (next < ordered.Length && ordered[next].StartsWith(prefix, StringComparison.Ordinal)) throw ArchiveManifestMember.Invalid();
        }
    }
    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.UnsupportedFeature, "The complete RAR5 source metadata or password profile cannot be preserved by this operation.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The exact reviewed RAR member occurrence is no longer available.");
}
