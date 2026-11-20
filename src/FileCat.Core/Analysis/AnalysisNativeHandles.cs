namespace FileCat.Core.Analysis;

/// <summary>Process-wide analysis handle admission, including prepared roots and quarantined requests.</summary>
public static class AnalysisNativeHandles
{
    private static int s_count;
    internal static int Count => Volatile.Read(ref s_count);
    public static IDisposable Acquire()
    {
        while (true)
        {
            int count = Volatile.Read(ref s_count);
            if (count >= AnalysisLimits.NativeHandles) throw new IOException("The analysis native-handle budget is in use. Close or stop another analysis.");
            if (Interlocked.CompareExchange(ref s_count, count + 1, count) == count) return new Reservation();
        }
    }
    private sealed class Reservation : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) Interlocked.Decrement(ref s_count); }
    }
}
