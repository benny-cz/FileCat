namespace FileCat.Core.Analysis;

/// <summary>Process-wide admission for accepted explorer builds and requests, through actual completion.</summary>
internal static class AnalysisExplorerAdmission
{
    private static int s_active;
    internal static int Active => Volatile.Read(ref s_active);
    internal static IDisposable Acquire()
    {
        while (true)
        {
            int active = Volatile.Read(ref s_active);
            if (active >= AnalysisLimits.IoRequests)
                throw new InvalidOperationException("Eight explorer requests are still completing across open analyses. Wait before starting more explorer work.");
            if (Interlocked.CompareExchange(ref s_active, active + 1, active) == active) return new Owner();
        }
    }
    private sealed class Owner : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) Interlocked.Decrement(ref s_active); }
    }
}
