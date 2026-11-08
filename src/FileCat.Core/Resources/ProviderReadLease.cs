using FileCat.Core.Diagnostics;

namespace FileCat.Core.Resources;

/// <summary>Runtime-only read ownership for a source and its containing archives. Admission performs no I/O.</summary>
public sealed class ProviderReadLease : IDisposable
{
    private List<IDisposable>? _leases;
    private ProviderReadLease(List<IDisposable> leases) => _leases = leases;

    public static ProviderReadLease? Capture(ProviderRegistry providers, IEnumerable<Location> sources)
    {
        var leases = new List<IDisposable>(); var retained = new HashSet<Location>();
        try
        {
            foreach (var parent in sources.Distinct())
                for (var location = parent; location is { IsFileSystem: false }; location = location.Container)
                {
                    var key = location.Scheme is Schemes.Archive or Schemes.Zip ? location.WithPath("") : location;
                    if (retained.Add(key) && providers.For(location).RetainRead(location) is { } lease) leases.Add(lease);
                }
            return leases.Count == 0 ? null : new(leases);
        }
        catch { new ProviderReadLease(leases).Dispose(); throw; }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _leases, null) is not { } leases) return;
        foreach (var lease in leases)
            try { lease.Dispose(); }
            catch (Exception error) { AppLog.Warn("Provider read cleanup failed: " + error.GetType().Name); }
    }
}
