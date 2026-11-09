namespace FileCat.Core.Archives;

/// <summary>Derives creation provenance only after every sealed record matches the frozen native capture.</summary>
internal static class ArchiveCreationOrigin
{
    internal static ArchiveOriginSnapshot Review(Stream ownedEvidence, ArchiveOriginEvidenceInfo evidence,
        ArchiveOriginEvidenceScope scope, IEnumerable<ArchiveFileObservation> captured, ArchiveOriginFormat format, CancellationToken ct,
        IEnumerable<ArchiveDirectoryObservation>? directories = null)
    {
        using var input = ownedEvidence;
        if (scope.Operation != ArchiveOriginOperation.Create || !Enum.IsDefined(format)) throw Invalid();
        using var reader = new ArchiveOriginEvidenceReader(ownedEvidence, evidence, scope, ct);
        using var expected = ArchiveCapturedOriginSources.Enumerate(captured, directories).GetEnumerator();
        bool marked = false; int maximum = 0, count = 0;
        ArchiveOriginSnapshot? unixMark = null;
        while (reader.ReadNext() is { } record)
        {
            ct.ThrowIfCancellationRequested();
            if (!expected.MoveNext() || record.Kind != expected.Current.Kind || record.Source != expected.Current.Version ||
                record.Origin.Digest != expected.Current.Origin.Digest || record.Origin.Format != format || record.Origin.State == ArchiveOriginState.Unavailable)
                throw Invalid();
            count++;
            if (format == ArchiveOriginFormat.WindowsZoneIdentifier)
            {
                var parsed = ArchiveWindowsOriginZone.Parse(record.Origin);
                if (parsed.DeclaredZone is { } zone) { marked = true; maximum = Math.Max(maximum, zone); }
            }
            else if (record.Origin.State == ArchiveOriginState.Present)
            {
                // Unix origin bytes are opaque. A single exact mark can be retained, including when other
                // inputs have no mark; distinct marks have no defined ordering or truthful merged value.
                if (unixMark is not null && unixMark.Digest != record.Origin.Digest)
                    throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable,
                        "The selected files have different origin marks that cannot be combined into one archive mark.");
                unixMark = record.Origin;
            }
        }
        if (!reader.IsComplete || reader.VerifiedInfo != evidence || count != scope.Sources || expected.MoveNext()) throw Invalid();
        return format == ArchiveOriginFormat.WindowsZoneIdentifier
            ? marked ? ArchiveWindowsOriginZone.ZoneOnly(maximum) : ArchiveOriginSnapshot.Absent(format)
            : unixMark ?? ArchiveOriginSnapshot.Absent(format);
    }

    internal static void RequireSupported(ArchiveOriginSnapshot origin)
    {
        if (origin.State == ArchiveOriginState.Absent) return;
        if (origin.Format == ArchiveOriginFormat.WindowsZoneIdentifier && origin.State == ArchiveOriginState.Present &&
            ArchiveWindowsOriginZone.Parse(origin).DeclaredZone is { } zone && ArchiveWindowsOriginZone.ZoneOnly(zone).Digest == origin.Digest) return;
        if (origin.Format is ArchiveOriginFormat.MacQuarantine or ArchiveOriginFormat.LinuxOriginUrl &&
            origin.State == ArchiveOriginState.Present) return;
        throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The new archive origin policy cannot represent this mark.");
    }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.NeedsReview, "The complete origin evidence does not match the captured archive inputs.");
}
