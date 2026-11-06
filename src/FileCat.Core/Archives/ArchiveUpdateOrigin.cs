namespace FileCat.Core.Archives;

/// <summary>Full ordered evidence binding before deriving the strongest declared zone for a native Windows update.</summary>
internal static class ArchiveUpdateOrigin
{
    internal static ArchiveOriginSnapshot ReviewWindows(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories = null)
        => Review(ownedEvidence, evidence, scope, original, captured, windows: true, ct, directories);

    internal static ArchiveOriginSnapshot ReviewUnmarked(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories = null)
        => Review(ownedEvidence, evidence, scope, original, captured, windows: false, ct, directories);

    private static ArchiveOriginSnapshot Review(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, bool windows, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories)
    {
        using var input = ownedEvidence;
        if (scope.Operation != ArchiveOriginOperation.Update) throw Invalid();
        using var reader = new ArchiveOriginEvidenceReader(input, evidence, scope, ct);
        var old = windows ? ArchiveWindowsOriginZone.Parse(original.Origin) : null;
        if (!windows && (original.Origin.State != ArchiveOriginState.Absent ||
            original.Origin.Format is not (ArchiveOriginFormat.MacQuarantine or ArchiveOriginFormat.LinuxOriginUrl))) throw Invalid();
        int? maximum = old?.DeclaredZone;
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
            else if (record.Origin.State != ArchiveOriginState.Absent || record.Origin.Format != original.Origin.Format) throw Invalid();
            count++;
        }
        if (!reader.IsComplete || reader.VerifiedInfo != evidence || count != scope.Sources || expected.MoveNext()) throw Invalid();
        if (!windows) return ArchiveOriginSnapshot.Absent(original.Origin.Format);
        return maximum is not { } value ? ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier) :
            old!.IsAbsent ? ArchiveWindowsOriginZone.ZoneOnly(value) : old.RaiseTo(value);
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview,
        "The complete update origin evidence does not match the retained original and captured inputs.");
}
