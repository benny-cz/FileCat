using FileCat.Core.Analysis;

namespace FileCat.App.Services;

/// <summary>One process-wide cap, including pending opens and owners still closing in old windows.</summary>
internal sealed class AnalysisSessionAdmission
{
    internal static AnalysisSessionAdmission Shared { get; } = new();
    private readonly object _gate = new();
    private int _reserved;
    private bool _failedClose;
    internal event Action? Changed;
    internal const string FailedCloseMessage = "An analysis could not close completely. Its place remains reserved until FileCat restarts.";
    internal bool HasFailedClose { get { lock (_gate) return _failedClose; } }
    internal bool HasCapacity { get { lock (_gate) return _reserved < AnalysisLimits.Sessions; } }
    internal string Refusal { get { lock (_gate) return "Eight analyses are open or still closing. " + (_failedClose
        ? "Some could not close completely. Preserve your work and restart FileCat if no analysis finishes closing."
        : "Wait for an analysis to finish closing before adding another."); } }
    internal Slot Acquire()
    {
        lock (_gate)
        {
            if (_reserved >= AnalysisLimits.Sessions)
                throw new InvalidOperationException(Refusal);
            _reserved++;
        }
        NotifyChanged(); return new(this);
    }
    private void Release() { lock (_gate) _reserved--; NotifyChanged(); }
    private void FailedClose() { lock (_gate) _failedClose = true; NotifyChanged(); }
    private void NotifyChanged()
    {
        // UI notifications cannot acquire authority over successful admission or actual closure.
        // One stale/closed subscriber must not strand a place or suppress other windows.
        foreach (Action observer in Changed?.GetInvocationList() ?? [])
            try { observer(); } catch (Exception) { }
    }
    internal sealed class Slot(AnalysisSessionAdmission owner)
    {
        private int _closing;
        internal async Task CloseAfterAsync(Task actualClose)
        {
            if (Interlocked.Exchange(ref _closing, 1) != 0) throw new InvalidOperationException("Analysis admission already transferred to closure.");
            // An unverified failed close keeps its reservation until process exit. Another window
            // must not reclaim it merely because a tab or its error notification disappeared.
            try { await actualClose.ConfigureAwait(false); }
            catch { owner.FailedClose(); throw; }
            owner.Release();
        }
    }
}
