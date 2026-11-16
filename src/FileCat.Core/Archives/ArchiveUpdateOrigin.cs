namespace FileCat.Core.Archives;

/// <summary>Full ordered evidence binding before deriving representable update provenance.</summary>
internal static class ArchiveUpdateOrigin
{
    internal static ArchiveOriginSnapshot ReviewWindows(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories = null)
        => Review(ownedEvidence, evidence, scope, original, captured, windows: true, exactUnix: false, ct, directories);

    internal static ArchiveOriginSnapshot ReviewUnmarked(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories = null)
        => Review(ownedEvidence, evidence, scope, original, captured, windows: false, exactUnix: false, ct, directories);

    /// <summary>One exact opaque Unix mark may survive an update; differing marks have no supported union.</summary>
    internal static ArchiveOriginSnapshot ReviewUnixExact(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories = null)
        => Review(ownedEvidence, evidence, scope, original, captured, windows: false, exactUnix: true, ct, directories);

    private static ArchiveOriginSnapshot Review(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, bool windows, bool exactUnix, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories)
    {
        using var input = ownedEvidence;
        if (scope.Operation != ArchiveOriginOperation.Update) throw Invalid();
        using var reader = new ArchiveOriginEvidenceReader(input, evidence, scope, ct);
        var old = windows ? ArchiveWindowsOriginZone.Parse(original.Origin) : null;
        if (!windows && (original.Origin.State == ArchiveOriginState.Unavailable ||
            !exactUnix && original.Origin.State != ArchiveOriginState.Absent ||
            original.Origin.Format is not (ArchiveOriginFormat.MacQuarantine or ArchiveOriginFormat.LinuxOriginUrl))) throw Invalid();
        int? maximum = old?.DeclaredZone;
        ArchiveOriginSnapshot? unixMark = !windows && original.Origin.State == ArchiveOriginState.Present ? original.Origin : null;
        bool conflictingUnixMarks = false;
        var first = reader.ReadNext();
        if (first is null || first.Kind != ArchiveOriginSourceKind.OriginalArchive || first.Source != original.Version ||
            first.Origin.Digest != original.Origin.Digest) throw Invalid();
        int count = 1;
        using var expected = ArchiveCapturedOriginSources.Enumerate(captured, directories).GetEnumerator();
        while (reader.ReadNext() is { } record)
        {
            ct.ThrowIfCancellationRequested();
            if (!expected.MoveNext() || record.Kind != expected.Current.Kind || record.Source != expected.Current.Version ||
                record.Origin.Digest != expected.Current.Origin.Digest) throw Invalid();
            if (windows)
            {
                var parsed = ArchiveWindowsOriginZone.Parse(record.Origin);
                if (parsed.DeclaredZone is { } zone) maximum = Math.Max(maximum ?? zone, zone);
            }
            else
            {
                if (record.Origin.Format != original.Origin.Format || record.Origin.State == ArchiveOriginState.Unavailable ||
                    !exactUnix && record.Origin.State != ArchiveOriginState.Absent) throw Invalid();
                if (record.Origin.State == ArchiveOriginState.Present)
                {
                    if (unixMark is not null && unixMark.Digest != record.Origin.Digest) conflictingUnixMarks = true;
                    else unixMark = record.Origin;
                }
            }
            count++;
        }
        if (!reader.IsComplete || reader.VerifiedInfo != evidence || count != scope.Sources || expected.MoveNext()) throw Invalid();
        if (!windows)
        {
            if (conflictingUnixMarks) throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                "The existing archive and selected files have different Unix origin marks that cannot be combined.");
            return unixMark ?? ArchiveOriginSnapshot.Absent(original.Origin.Format);
        }
        return maximum is not { } value ? ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier) :
            old!.IsAbsent ? ArchiveWindowsOriginZone.ZoneOnly(value) : old.RaiseTo(value);
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview,
        "The complete update origin evidence does not match the retained original and captured inputs.");
}
