using FileCat.Core.Threading;

namespace FileCat.App.Services;

internal interface IAnalysisSavedReferenceTarget
{
    void ForgetSavedReference(Guid id, string name, bool comparison);
}

/// <summary>In-process presentation invalidation for known private removals; never grants filesystem authority.</summary>
internal sealed class AnalysisSavedReferences(IUiDispatcher ui)
{
    private readonly object _gate = new();
    private readonly List<WeakReference<IAnalysisSavedReferenceTarget>> _targets = [];
    internal void Track(IAnalysisSavedReferenceTarget target)
    {
        lock (_gate)
        {
            _targets.RemoveAll(reference => !reference.TryGetTarget(out _));
            _targets.Add(new(target));
        }
    }
    internal void Untrack(IAnalysisSavedReferenceTarget target)
    {
        lock (_gate) _targets.RemoveAll(reference => !reference.TryGetTarget(out var current) || ReferenceEquals(current, target));
    }
    internal Task RemovedAsync(Guid id, string name, bool comparison)
    {
        void Update()
        {
            IAnalysisSavedReferenceTarget[] captured;
            lock (_gate) captured = _targets.Select(reference => reference.TryGetTarget(out var target) ? target : null).OfType<IAnalysisSavedReferenceTarget>().ToArray();
            foreach (var target in captured)
            {
                // A broken presentation observer cannot change a confirmed filesystem outcome
                // or prevent the other open observations from losing their obsolete saved marker.
                try { target.ForgetSavedReference(id, name, comparison); } catch { }
            }
        }
        if (ui.CheckAccess()) { Update(); return Task.CompletedTask; }
        // Normal view consumers resume on the UI thread and update synchronously. A late
        // background caller must not retain native-operation ownership behind a stopped UI.
        try { ui.Post(Update); } catch { }
        return Task.CompletedTask;
    }
}
