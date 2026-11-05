using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace FileCat.Core.Archives;

/// <summary>
/// Frozen parent expectation, computed without a writer or archive payload parser. The source is a complete reviewed
/// assessment; new input hashes/metadata come from retained native leases or snapshots. Construction does not acquire
/// those leases, grant IO, launch a worker or authorize publication. Numeric source ID zero is reserved for the archive.
/// </summary>
public sealed class ArchiveTarTransformPlan
{
    public ArchiveAssessment? Source { get; }
    public IReadOnlyDictionary<int, ArchiveTransformInput> Inputs { get; }
    public IReadOnlyList<ArchiveTransformEdit> Edits { get; }
    public IReadOnlyList<ArchiveTransformAddition> Additions { get; }
    public ArchiveWriteManifest Expected { get; }

    private ArchiveTarTransformPlan(ArchiveAssessment? source, Dictionary<int, ArchiveTransformInput> inputs,
        ArchiveTransformEdit[] edits, ArchiveTransformAddition[] additions, ArchiveWriteManifest expected)
    {
        Source = source; Inputs = new ReadOnlyDictionary<int, ArchiveTransformInput>(inputs);
        Edits = Array.AsReadOnly(edits); Additions = Array.AsReadOnly(additions); Expected = expected;
    }

    public static ArchiveTarTransformPlan Create(ArchiveAssessment? source, IReadOnlyList<ArchiveTransformInput> inputs,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions, CancellationToken ct)
        => Create(source?.Manifest?.Profile ?? new(ArchiveWriteFormat.Tar), source, inputs, edits, additions, ct);

    public static ArchiveTarTransformPlan Create(ArchiveWriteProfile profile, ArchiveAssessment? source, IReadOnlyList<ArchiveTransformInput> inputs,
        IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(inputs); ArgumentNullException.ThrowIfNull(edits); ArgumentNullException.ThrowIfNull(additions);
        ct.ThrowIfCancellationRequested();
        if (inputs.Count > ArchiveWritePolicy.MaxMembers || edits.Count > ArchiveWritePolicy.MaxMembers || additions.Count > ArchiveWritePolicy.MaxMembers)
            throw ArchiveWriteBudget.Limit();
        if (source is not null) ArgumentNullException.ThrowIfNull(source.Manifest);
        if (profile.Format is not (ArchiveWriteFormat.Tar or ArchiveWriteFormat.TarGzip) || source is not null && profile != source.Manifest.Profile) throw Unsupported();
        bool ustar = false;
        if (source is not null)
        {
            ArchiveAssessmentWire.Validate(new(profile, source.SourceLength, source.SourceSha256));
            if (source.CatalogRevision != source.SourceSha256) throw Changed();
            ArchiveTarFeatures.Validate(profile, source.Manifest.Features); ustar = source.Manifest.Features[1] == 1;
        }
        else if (edits.Count != 0) throw Changed();

        // Account retained source, request arrays/maps and the derived output together. This is deliberately
        // conservative even when immutable strings/metadata are shared; global operation leases remain a later gate.
        var budget = new ArchiveWriteBudget();
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
        foreach (var old in source?.Manifest.Members ?? [])
        {
            ct.ThrowIfCancellationRequested();
            ReserveSource(old); ValidateMetadata(old, ustar);
            keys.Add(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence));
        }
        // An edit cannot turn an ambiguous source into an admitted update by deleting the troublesome row.
        // Assessment is evidence, not permission to skip whole-source admission during trusted preparation.
        if (source is not null)
        {
            try { ValidateNamespace(source.Manifest.Members.Select(m => (m, Changed: false)), ct); }
            catch (ArchiveWriteException ex) when (ex.Reason == ArchiveWriteReason.InvalidRequest) { throw Unsupported(); }
        }
        var changes = new Dictionary<ArchiveMemberKey, ArchiveTransformEdit>();
        var frozenEdits = new ArchiveTransformEdit[edits.Count];
        var usedInputs = new HashSet<int>();
        long requestedBytes = source?.Manifest.LogicalBytes ?? 0;
        for (int i = 0; i < edits.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var edit = edits[i] ?? throw ArchiveManifestMember.Invalid();
            if (!keys.Contains(edit.Key) || !changes.TryAdd(edit.Key, edit)) throw Changed();
            RequestName(edit.Key.RawPath); if (edit.NewName is not null) RequestName(edit.NewName);
            if (edit.InputId is { } id) Use(id);
            frozenEdits[i] = edit;
        }
        var planned = new List<(ArchiveManifestMember Member, bool Changed)>();
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        foreach (var old in source?.Manifest.Members ?? [])
        {
            ct.ThrowIfCancellationRequested();
            changes.TryGetValue(new(source!.CatalogRevision, old.Name, old.Type, old.Occurrence), out var edit);
            if (edit?.Kind == ArchiveChangeKind.Delete) continue;
            string name = edit?.NewName ?? old.Name;
            var input = edit?.InputId is { } id ? captured[id] : null;
            Add(name, old.Type, input?.Length ?? old.Length, input?.Sha256 ?? old.Sha256, old.Metadata, name != old.Name);
        }
        var frozenAdditions = new ArchiveTransformAddition[additions.Count];
        for (int i = 0; i < additions.Count; i++)
        {
            ct.ThrowIfCancellationRequested(); var addition = additions[i] ?? throw ArchiveManifestMember.Invalid();
            RequestName(addition.Name);
            var input = addition.InputId is { } id ? Use(id) : null;
            var modified = input?.ModifiedTimestamp ?? new ArchiveTimestamp(0, 0);
            if (ustar) modified = ArchiveTimestamp.FromUnixSeconds(decimal.Truncate(modified.UnixSeconds));
            Add(addition.Name, addition.Type, input?.Length ?? 0,
                input?.Sha256 ?? "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855",
                AddedMetadata(input?.Mode ?? 0x1ed, modified), changed: true);
            frozenAdditions[i] = addition;
        }
        if (usedInputs.Count != captured.Count) throw ArchiveManifestMember.Invalid(); // no unrelated read grants
        ValidateNamespace(planned, ct);
        // A headerless empty TAR has no PAX extension and is observed as USTAR, including a PAX remove-all update.
        var expected = new ArchiveWriteManifest(profile, planned.Select(p => p.Member).ToArray(),
            ArchiveTarFeatures.Create(profile, ustar || planned.Count == 0 ? (byte)1 : (byte)2,
                source is not null && profile.Format == ArchiveWriteFormat.TarGzip ? source.Manifest.Features[2] : (byte)255,
                source is not null && profile.Format == ArchiveWriteFormat.TarGzip ? source.Manifest.Features[3] : null));
        return new(source, captured, frozenEdits, frozenAdditions, expected);

        ArchiveTransformInput Use(int id)
        {
            if (!captured.TryGetValue(id, out var input)) throw ArchiveManifestMember.Invalid();
            // Reusing one captured object for several replacements/additions still consumes its payload each time.
            ArchiveWriteBudget.Reserve(requestedBytes, input.Length, ArchiveWritePolicy.MaxLogicalBytes); requestedBytes += input.Length;
            usedInputs.Add(id); return input;
        }
        void RequestName(string name)
        { budget.AddNameChars(name.Length); budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(name), 2L * name.Length + 128); }
        void ReserveSource(ArchiveManifestMember member)
        {
            budget.AddNameChars(member.Name.Length);
            budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(member.Name) + member.Metadata.Length + 64L,
                2L * member.Name.Length + member.Metadata.Length + 640L);
        }
        void Add(string name, ArchiveMemberType type, long length, string digest, ReadOnlySpan<byte> metadata, bool changed)
        {
            // Reserve before copying a complete member representation. Metadata is already bounded by the source
            // manifest; newly synthesized metadata has a fixed 32/36-byte representation.
            budget.AddMember(); budget.AddNameChars(name.Length); budget.AddLogicalBytes(length);
            budget.AddMetadata(ArchiveWritePolicy.Utf8.GetByteCount(name) + metadata.Length + 64L, 2L * name.Length + metadata.Length + 640L);
            var key = (name, type); int occurrence = occurrences.GetValueOrDefault(key); occurrences[key] = occurrence + 1;
            var member = new ArchiveManifestMember(name, type, occurrence, length, digest, metadata);
            ValidateMetadata(member, ustar);
            planned.Add((member, changed));
        }
    }

    private static byte[] AddedMetadata(int mode, ArchiveTimestamp modified)
    {
        byte[] result = new byte[modified.FitsTicks ? 32 : 36]; BinaryPrimitives.WriteUInt16LittleEndian(result, modified.FitsTicks ? (ushort)1 : (ushort)2);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(2), mode);
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(22), modified.FitsTicks ? modified.ToDateTimeOffset().UtcTicks : modified.Seconds);
        if (!modified.FitsTicks) BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(30), modified.Nanoseconds);
        return result;
    }

    private static void ValidateMetadata(ArchiveManifestMember member, bool ustar)
    {
        var data = member.Metadata;
        if (data.Length < 32) throw Unsupported(); ushort schema = BinaryPrimitives.ReadUInt16LittleEndian(data);
        if (schema is not (1 or 2)) throw Unsupported();
        var reader = new MetadataReader(data[2..], schema); int mode = reader.Integer(), uid = reader.Integer(), gid = reader.Integer();
        if ((mode & ~0xfff) != 0 || uid < 0 || gid < 0) throw Unsupported();
        int user = reader.TextLength(), group = reader.TextLength(); ArchiveTimestamp modified = reader.Time();
        bool accessed = reader.OptionalTime(), changed = reader.OptionalTime();
        if (!reader.AtEnd) throw Unsupported();
        if (ustar && (accessed || changed || uid > 0x1fffff || gid > 0x1fffff || user > 32 || group > 32 ||
            modified.Seconds < 0 || modified.Nanoseconds != 0 || modified.Seconds > 0x1ffffffffL || member.Length > 0x1ffffffffL || !FitsUstarName(member.Name)))
            throw Unsupported();

    }

    private ref struct MetadataReader(ReadOnlySpan<byte> data, ushort schema)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;
        public bool AtEnd => _offset == _data.Length;
        public int Integer()
        { if (_offset > _data.Length - 4) throw Unsupported(); int value = BinaryPrimitives.ReadInt32LittleEndian(_data[_offset..]); _offset += 4; return value; }
        public int TextLength()
        {
            int length = Integer(); if (length < 0 || _offset > _data.Length - length) throw Unsupported();
            var text = _data.Slice(_offset, length); _offset += length;
            if (text.Contains((byte)0)) throw Unsupported();
            try { ArchiveWritePolicy.Utf8.GetCharCount(text); } catch (DecoderFallbackException) { throw Unsupported(); }
            return length;
        }
        public ArchiveTimestamp Time()
        {
            if (_offset > _data.Length - 8) throw Unsupported(); long value = BinaryPrimitives.ReadInt64LittleEndian(_data[_offset..]); _offset += 8;
            if (schema == 2) return new(value, Integer());
            if (value < DateTimeOffset.MinValue.UtcTicks || value > DateTimeOffset.MaxValue.UtcTicks) throw Unsupported();
            return new DateTimeOffset(value, TimeSpan.Zero);
        }
        public bool OptionalTime()
        {
            if (_offset >= _data.Length || _data[_offset] > 1) throw Unsupported(); bool present = _data[_offset++] == 1;
            if (present) Time(); return present;
        }
    }

    private static bool FitsUstarName(string name)
    {
        if (ArchiveWritePolicy.Utf8.GetByteCount(name) <= 100) return true;
        for (int slash = name.LastIndexOf('/'); slash > 0; slash = name.LastIndexOf('/', slash - 1))
            if (slash < name.Length - 1 && ArchiveWritePolicy.Utf8.GetByteCount(name.AsSpan(0, slash)) <= 155 &&
                ArchiveWritePolicy.Utf8.GetByteCount(name.AsSpan(slash + 1)) <= 100) return true;
        return false;
    }

    private static void ValidateNamespace(IEnumerable<(ArchiveManifestMember Member, bool Changed)> planned, CancellationToken ct)
    {
        var names = new Dictionary<string, (ArchiveMemberType Type, bool Changed)>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (member, changed) in planned)
        {
            ct.ThrowIfCancellationRequested();
            string name = member.Type == ArchiveMemberType.Directory ? member.Name.TrimEnd('/') : member.Name;
            if (names.TryGetValue(name, out var previous) && (changed || previous.Changed || previous.Type != member.Type)) throw ArchiveManifestMember.Invalid();
            names[name] = (member.Type, changed); if (member.Type == ArchiveMemberType.File) files.Add(name);
        }
        // Find the first possible descendant using ordinal sorted names. Walking and hashing every prefix of a
        // deeply nested 32K name would cause quadratic parent work even though the retained data is bounded.
        string[] ordered = names.Keys.ToArray(); Array.Sort(ordered, StringComparer.Ordinal);
        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            string prefix = file + "/"; int next = Array.BinarySearch(ordered, prefix, StringComparer.Ordinal);
            if (next < 0) next = ~next;
            if (next < ordered.Length && ordered[next].StartsWith(prefix, StringComparison.Ordinal)) throw ArchiveManifestMember.Invalid();
        }
    }

    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.UnsupportedFeature, "The TAR profile, metadata or requested changes cannot be represented by this writer.");
    private static ArchiveWriteException Changed() => new(ArchiveWriteReason.SourceChanged, "The exact reviewed TAR member occurrence is no longer available.");
}
