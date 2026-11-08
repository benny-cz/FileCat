using FileCat.Core.Diagnostics;

namespace FileCat.Core.Jobs;

public sealed partial class JobManager
{
    private readonly Dictionary<Job, List<IDisposable>> _readLeases = [];

    private List<IDisposable> CaptureReadLeases(JobRequest request)
    {
        var leases = new List<IDisposable>();
        if (request.Kind is not (JobKind.Copy or JobKind.Extract)) return leases;
        try
        {
            foreach (var location in request.Sources.Select(item => item.Parent).Distinct())
                if (_providers.For(location).RetainRead(location) is { } lease) leases.Add(lease);
            return leases;
        }
        catch { DisposeReadLeases(leases); throw; }
    }

    private void ReleaseReadLeases(Job job)
    {
        List<IDisposable>? leases;
        lock (_lock) _readLeases.Remove(job, out leases);
        if (leases is not null) DisposeReadLeases(leases);
    }

    private static void DisposeReadLeases(List<IDisposable> leases)
    {
        foreach (var lease in leases)
            try { lease.Dispose(); }
            catch (Exception error) { AppLog.Warn("Archive read session cleanup failed: " + error.GetType().Name); }
    }
}
