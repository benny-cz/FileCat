namespace FileCat.Core.Analysis;

/// <summary>
/// Shared producer-payload admission. Reserved working space prevents traversal queues or
/// standalone capture writes from starving the two actual producers. The partitions sum to8 MiB.
/// </summary>
internal static class AnalysisPendingPayload
{
    internal const long WorkingPerScan = AnalysisLimits.PendingBytes / 4;
    internal const long WitnessBytes = WorkingPerScan / 2;
    internal static readonly Pool Working = new(WorkingPerScan * AnalysisLimits.ActiveScans);
    internal static readonly Pool Frontier = new(AnalysisLimits.PendingBytes / 4);
    internal static readonly Pool Writes = new(AnalysisLimits.PendingBytes / 4);
    internal static long ReservedBytes => Working.ReservedBytes + Frontier.ReservedBytes + Writes.ReservedBytes;

    // Includes decoded display text and metadata strings, not just raw names/relative locators.
    internal static long EntryBytes(AnalysisEntry entry, bool ancestry = false)
    {
        long bytes = checked(1024L + entry.RawName.Length * 4L + entry.RelativeName.Length * 2L +
            entry.Name.Length * 2L + (entry.Identity?.Length ?? 0) * 2L + entry.Version.Length * 2L +
            entry.NameEncoding.Length * 2L + (entry.IssueCode?.Length ?? 0) * 2L);
        if (!ancestry) return bytes;
        int depth = 0;
        for (var witness = entry.Ancestry; witness is not null; witness = witness.Parent)
        {
            if (++depth > AnalysisLimits.Depth + 1) throw new IOException("The source read witness exceeds its depth limit.");
            bytes = checked(bytes + 256L + witness.RelativeName.Length * 2L + (witness.Identity?.Length ?? 0) * 2L + witness.Version.Length * 2L);
        }
        return bytes;
    }
    internal sealed class Pool(long limit)
    {
        private long _reserved;
        internal long Limit => limit;
        internal long ReservedBytes => Volatile.Read(ref _reserved);
        internal IDisposable Acquire(long bytes)
        {
            if (!TryAcquire(bytes, out var owner)) throw new IOException("The shared pending producer payload budget is in use. Wait before submitting more work.");
            return owner!;
        }
        internal bool TryAcquire(long bytes, out IDisposable? owner)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(bytes);
            while (true)
            {
                long observed = Volatile.Read(ref _reserved);
                if (bytes > limit - observed) { owner = null; return false; }
                if (Interlocked.CompareExchange(ref _reserved, observed + bytes, observed) == observed)
                { owner = new Owner(this, bytes); return true; }
            }
        }
        private sealed class Owner(Pool pool, long bytes) : IDisposable
        {
            private int _released;
            public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) Interlocked.Add(ref pool._reserved, -bytes); }
        }
    }
}
