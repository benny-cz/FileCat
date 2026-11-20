using System.Collections.ObjectModel;

namespace FileCat.Core.Archives;

/// <summary>
/// AW7 parent expectation for one standalone gzip, bzip2 or Zstandard stream: a new file from exactly one regular input, or
/// one replacement of the existing payload. Anything else (a second member, a folder, deletion or rename) is refused before
/// any object exists. The expected manifest has the single synthetic member and the source's wrapper features (or the
/// profile's for a new file). This object grants no IO or publication authority.
/// </summary>
public sealed class ArchiveStandaloneTransformPlan : ArchiveContainerTransformPlan
{
    public override ArchiveAssessment? Source { get; }
    public override IReadOnlyDictionary<int, ArchiveTransformInput> Inputs { get; }
    public override IReadOnlyList<ArchiveTransformEdit> Edits { get; }
    public override IReadOnlyList<ArchiveTransformAddition> Additions { get; }
    public override ArchiveWriteManifest Expected { get; }
    internal override ArchiveCapturedInputCost CaptureCost { get; }
    internal override ArchiveCapturedInputs? CaptureOwner { get; }
    // The owner's own (empty) map: preparation requires the very instance the captured selection holds.
    internal override IReadOnlyDictionary<int, ArchiveCapturedDirectory> Directories { get; }

    private ArchiveStandaloneTransformPlan(ArchiveAssessment? source, ArchiveTransformInput input, ArchiveTransformEdit[] edits,
        ArchiveTransformAddition[] additions, ArchiveWriteManifest expected, ArchiveCapturedInputCost captureCost, ArchiveCapturedInputs? owner)
    {
        Source = source; Inputs = new ReadOnlyDictionary<int, ArchiveTransformInput>(new Dictionary<int, ArchiveTransformInput> { [input.Id] = input });
        Edits = Array.AsReadOnly(edits); Additions = Array.AsReadOnly(additions); Expected = expected;
        CaptureCost = captureCost; CaptureOwner = owner; Directories = owner?.Directories ?? NoDirectories;
    }

    public static ArchiveStandaloneTransformPlan Create(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits,
        IReadOnlyList<ArchiveTransformAddition> additions, CancellationToken ct = default)
        => CreateCaptured(profile, source, inputs, edits, additions, default, ct);

    internal static ArchiveStandaloneTransformPlan CreateCaptured(ArchiveWriteProfile profile, ArchiveAssessment? source,
        IReadOnlyList<ArchiveTransformInput> inputs, IReadOnlyList<ArchiveTransformEdit> edits, IReadOnlyList<ArchiveTransformAddition> additions,
        ArchiveCapturedInputCost captureCost, CancellationToken ct, ArchiveCapturedInputs? owner = null)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(edits); ArgumentNullException.ThrowIfNull(additions); ct.ThrowIfCancellationRequested();
        if (!ArchiveStandalone.IsStandalone(profile.Format) || profile.Encryption.Mode != ArchiveEncryptionMode.None)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This compressed-file profile is not offered.");
        if (inputs.Count != 1 || inputs[0] is not { } input) throw ArchiveStandalone.OneFileOnly();
        byte[] features;
        if (source is not null)
        {
            features = AdmitSource(profile, source);
            // Exactly one replacement of the one payload; the reviewed source keeps its wrapper features.
            if (additions.Count != 0 || edits.Count != 1 || edits[0].Kind != ArchiveChangeKind.Replace || edits[0].InputId != input.Id ||
                edits[0].Key.RawPath != ArchiveStandalone.MemberName || edits[0].Key.Occurrence != 0 || edits[0].Key.Type != ArchiveMemberType.File ||
                edits[0].Key.CatalogRevision != source.CatalogRevision)
                throw ArchiveStandalone.OneFileOnly();
        }
        else
        {
            if (edits.Count != 0 || additions.Count != 1 || additions[0] is not { Name: ArchiveStandalone.MemberName, Type: ArchiveMemberType.File } addition ||
                addition.InputId != input.Id)
                throw ArchiveStandalone.OneFileOnly();
            features = ArchiveStandalone.Features(profile);
        }
        var budget = new ArchiveWriteBudget(); budget.RetainCapturedInputs(captureCost); budget.AddLogicalBytes(input.Length);
        var expected = ArchiveStandalone.Manifest(profile, input.Length, input.Sha256, features);
        return new(source, input, edits.ToArray(), additions.ToArray(), expected, captureCost, owner);
    }

    /// <summary>
    /// Admits a reviewed existing stream: one synthetic payload whose wrapper features this profile writes back unchanged.
    /// The target review calls this where TAR-based profiles prove preservation with a change-free plan, which a compressed
    /// file cannot have: its only update is one replacement.
    /// </summary>
    internal static byte[] AdmitSource(ArchiveWriteProfile profile, ArchiveAssessment source)
    {
        ArgumentNullException.ThrowIfNull(profile); ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(source.Manifest);
        if (!ArchiveStandalone.IsStandalone(profile.Format) || profile.Encryption.Mode != ArchiveEncryptionMode.None)
            throw new ArchiveWriteException(ArchiveWriteReason.UnsupportedFeature, "This compressed-file profile is not offered.");
        ArchiveAssessmentWire.Validate(new(profile, source.SourceLength, source.SourceSha256));
        if (source.Manifest.Profile.Format != profile.Format || source.Manifest.Members.Count != 1 ||
            source.Manifest.Members[0] is not { Name: ArchiveStandalone.MemberName, Type: ArchiveMemberType.File, Occurrence: 0 })
            throw new ArchiveWriteException(ArchiveWriteReason.InvalidArchive, "The compressed file's assessment does not describe one payload.");
        byte[] features = source.Manifest.Features.ToArray(); ArchiveStandalone.Validate(profile, features);
        return features;
    }

    /// <summary>
    /// A parent ceiling for the single stream, enforced before every stage write: the payload plus each encoder's
    /// worst-case expansion and framing (the TAR wrappers' bounds without the TAR records).
    /// </summary>
    internal static long OutputBound(ArchiveStandaloneTransformPlan plan)
    {
        long length = plan.Expected.Members[0].Length;
        long bound = plan.Expected.Profile.Format switch
        {
            ArchiveWriteFormat.Gzip => checked(length + (length + 7) / 8 + (64 << 10) + 18),
            ArchiveWriteFormat.Zstd => checked(length + (length + 7) / 8 + (64 << 10) + 22),
            _ => Math.Min(ArchiveWritePolicy.MaxPhysicalBytes, checked(4 * length + ((5 * length + 3) / 4 / 99_980 + 2) * (64 << 10) + 32)),
        };
        if (bound > ArchiveWritePolicy.MaxPhysicalBytes) throw ArchiveWriteBudget.Limit();
        return bound;
    }
}
