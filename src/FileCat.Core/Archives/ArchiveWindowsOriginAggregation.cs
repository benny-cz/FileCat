namespace FileCat.Core.Archives;

/// <summary>Derived declaration plus complete scoped evidence checksum, not a native publication permission.</summary>
public sealed record ArchiveWindowsOriginAggregate(ArchiveOriginEvidenceScope Scope, ArchiveOriginSnapshot RequiredOrigin,
    ArchiveOriginEvidenceInfo Evidence);

/// <summary>
/// Parent-only streaming aggregation. Keeps at most the original mark, records every input (including all raw URLs)
/// in bounded evidence, and never short-circuits after a high zone. Creation uses a zone-only result; updates retain
/// the original outer mark's bytes and only increase its declared zone. Unsupported/unavailable input poisons the job.
/// Native identity, source consumption/version checks, aggregate job budgets and post-publication rereads are separate.
/// </summary>
public sealed class ArchiveWindowsOriginAggregation : IDisposable
{
    private readonly ArchiveOriginEvidenceWriter writer;
    private readonly State state = new();
    private bool failed, completed, disposed;
    public ArchiveOriginEvidenceScope Scope => writer.Scope;

    public ArchiveWindowsOriginAggregation(Stream ownedEvidenceOutput, ArchiveOriginEvidenceScope scope, CancellationToken cancellationToken = default)
    { writer = new(ownedEvidenceOutput, scope, cancellationToken); }

    public void Add(ArchiveOriginEvidenceRecord record)
    {
        EnsureLive();
        try
        {
            writer.Append(record); // Retain diagnostic raw evidence even when its interpretation must refuse below.
            state.Add(record);
        }
        catch { failed = true; throw; }
    }

    public ArchiveWindowsOriginAggregate Complete()
    {
        EnsureLive();
        try
        {
            var origin = state.Result(); var info = writer.Complete(); completed = true;
            return new(Scope, origin, info);
        }
        catch { failed = true; throw; }
    }

    /// <summary>Re-derives the result from a fresh bounded evidence stream; returns only after full checksum/EOF.</summary>
    public static ArchiveWindowsOriginAggregate Review(Stream ownedEvidenceInput, ArchiveOriginEvidenceInfo expected,
        ArchiveOriginEvidenceScope scope, CancellationToken cancellationToken = default)
    {
        using var reader = new ArchiveOriginEvidenceReader(ownedEvidenceInput, expected, scope, cancellationToken);
        var observed = new State();
        while (reader.ReadNext() is { } record) observed.Add(record);
        if (!reader.IsComplete || reader.VerifiedInfo != expected) throw ArchiveOriginEvidenceWriter.Invalid();
        return new(scope, observed.Result(), expected);
    }

    private sealed class State
    {
        private ArchiveWindowsOriginZone? original;
        private int maximum;
        private bool marked;
        public void Add(ArchiveOriginEvidenceRecord record)
        {
            var parsed = ArchiveWindowsOriginZone.Parse(record.Origin); // Every source must be readable and supported.
            if (record.Kind == ArchiveOriginSourceKind.OriginalArchive) original = parsed;
            if (parsed.DeclaredZone is { } zone) { marked = true; maximum = Math.Max(maximum, zone); }
        }
        public ArchiveOriginSnapshot Result() => !marked ? ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier) :
            original is { IsAbsent: false } ? original.RaiseTo(maximum) : ArchiveWindowsOriginZone.ZoneOnly(maximum);
    }

    private void EnsureLive()
    { ObjectDisposedException.ThrowIf(disposed, this); if (failed || completed) throw ArchiveOriginEvidenceWriter.Invalid(); }
    public void Dispose() { if (disposed) return; disposed = true; writer.Dispose(); }
}
