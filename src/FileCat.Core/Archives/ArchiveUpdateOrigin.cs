namespace FileCat.Core.Archives;

/// <summary>Full ordered evidence binding before deriving the strongest declared zone for a native Windows update.</summary>
internal static class ArchiveUpdateOrigin
{
    internal static ArchiveOriginSnapshot ReviewWindows(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, ArchiveFileObservation original, IEnumerable<ArchiveFileObservation> captured, CancellationToken ct)
    {
        using var input = ownedEvidence;
        if (scope.Operation != ArchiveOriginOperation.Update) throw Invalid();
        using var reader = new ArchiveOriginEvidenceReader(input, evidence, scope, ct);
        var old = ArchiveWindowsOriginZone.Parse(original.Origin);
        int? maximum = old.DeclaredZone;
        var first = reader.ReadNext();
        if (first is null || first.Kind != ArchiveOriginSourceKind.OriginalArchive || first.Source != original.Version ||
            first.Origin.Digest != original.Origin.Digest) throw Invalid();
        int count = 1;
        using var expected = captured.GetEnumerator();
        while (reader.ReadNext() is { } record)
        {
            ct.ThrowIfCancellationRequested();
            if (!expected.MoveNext() || record.Kind != ArchiveOriginSourceKind.File || record.Source != expected.Current.Version ||
                record.Origin.Digest != expected.Current.Origin.Digest) throw Invalid();
            var parsed = ArchiveWindowsOriginZone.Parse(record.Origin);
            if (parsed.DeclaredZone is { } zone) maximum = Math.Max(maximum ?? zone, zone);
            count++;
        }
        if (!reader.IsComplete || reader.VerifiedInfo != evidence || count != scope.Sources || expected.MoveNext()) throw Invalid();
        return maximum is not { } value ? ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier) :
            old.IsAbsent ? ArchiveWindowsOriginZone.ZoneOnly(value) : old.RaiseTo(value);
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview,
        "The complete update origin evidence does not match the retained original and captured inputs.");
}
