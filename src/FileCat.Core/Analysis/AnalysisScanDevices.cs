namespace FileCat.Core.Analysis;

/// <summary>One full traversal per device. Only globally admitted scans can enter this bounded registry.</summary>
internal static class AnalysisScanDevices
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, Device> Devices = new(StringComparer.OrdinalIgnoreCase);
    private sealed class Device { public readonly SemaphoreSlim Turn = new(1, 1); public int References; }

    public static async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 1024) throw new ArgumentException("A bounded scan device key is required.", nameof(key));
        Device device;
        lock (Sync)
        {
            if (!Devices.TryGetValue(key, out device!))
            {
                if (Devices.Count >= AnalysisLimits.ActiveScans) throw new InvalidOperationException("The active scan device budget was reached.");
                Devices.Add(key, device = new());
            }
            device.References++;
        }
        try { await device.Turn.WaitAsync(cancellation).ConfigureAwait(false); return new Lease(key, device); }
        catch { Forget(key, device); throw; }
    }
    private static void Forget(string key, Device device)
    {
        lock (Sync)
        {
            if (--device.References != 0) return;
            Devices.Remove(key); device.Turn.Dispose();
        }
    }
    private sealed class Lease(string key, Device device) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            device.Turn.Release(); Forget(key, device);
        }
    }
}
