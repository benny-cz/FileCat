namespace FileCat.Core.Analysis;

/// <summary>
/// One verified native directory and one session byte ceiling for data, sorting runs and indexes.
/// Each returned arena is independently owned by its caller; reader-retained arenas keep their directory
/// and both budget levels after this factory closes. There is no path-based reopening or recursive cleanup.
/// </summary>
internal sealed class AnalysisScratchWorkspace : IAnalysisScratchOwned
{
    private readonly object _gate = new();
    private readonly AnalysisSharedPrivateDirectory _directory;
    private readonly AnalysisScratchBudget _budget;
    private bool _disposed;
    internal AnalysisScratchWorkspace(AnalysisPrivateDirectory directory)
        : this(directory, AnalysisLimits.ScratchBytes, AnalysisScratchBudget.Shared) { }
    internal AnalysisScratchWorkspace(AnalysisPrivateDirectory directory, long sessionLimit, AnalysisScratchBudget profileBudget, AnalysisProfileQuota? quota = null,
        AnalysisResourceQuarantine? quarantine = null)
    {
        ArgumentNullException.ThrowIfNull(directory); ArgumentNullException.ThrowIfNull(profileBudget);
        if (sessionLimit is < 1 or > AnalysisLimits.ScratchBytes) throw new ArgumentOutOfRangeException(nameof(sessionLimit));
        var retained = quota?.Borrow();
        try { _budget = new(sessionLimit, profileBudget, quota, quarantine); _directory = new(directory, retained, quarantine); }
        catch { retained?.Dispose(); throw; }
    }
    internal long ReservedBytes => _budget.ReservedBytes;
    private AnalysisScratchWorkspace(AnalysisSharedPrivateDirectory directory, AnalysisScratchBudget budget)
    { _directory = directory; _budget = budget; }
    internal AnalysisScratchWorkspace Borrow()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _budget.CheckAdmission();
            var directory = _directory.Borrow();
            try { return new(directory, _budget); }
            catch { directory.Dispose(); throw; }
        }
    }
    internal AnalysisScratchArena CreateArena()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _budget.CheckAdmission();
            var directory = _directory.Borrow();
            try { return new(directory, _budget, AnalysisLimits.ScratchBytes); }
            catch (Exception error)
            {
                // A failed native close may have transferred this exact lease to quarantine.
                // Ordinary constructor refusal already closed it, or never accepted it.
                if (!AnalysisCloseUnconfirmedException.IsPresent(error)) directory.Dispose();
                throw;
            }
        }
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _directory.Dispose(); } }
}

/// <summary>Internal native directory lifetime sharing; the underlying no-follow capability remains authoritative.</summary>
internal sealed class AnalysisSharedPrivateDirectory : AnalysisPrivateDirectory
{
    private sealed class Shared(AnalysisPrivateDirectory directory, IDisposable? quota, AnalysisResourceQuarantine quarantine)
    {
        internal readonly AnalysisPrivateDirectory Directory = directory;
        internal readonly IDisposable? Quota = quota;
        internal readonly AnalysisResourceQuarantine Quarantine = quarantine;
        internal readonly object Lifetime = new(), Io = new();
        internal int References = 1;
    }
    private readonly Shared _shared;
    private readonly object _gate = new();
    private bool _disposed;
    internal AnalysisSharedPrivateDirectory(AnalysisPrivateDirectory directory, IDisposable? quota = null, AnalysisResourceQuarantine? quarantine = null) =>
        _shared = new(directory, quota, quarantine ?? AnalysisResourceQuarantine.Shared);
    private AnalysisSharedPrivateDirectory(Shared shared) => _shared = shared;
    internal AnalysisSharedPrivateDirectory Borrow()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_shared.Lifetime)
            {
                if (_shared.References >= AnalysisLimits.Components + 1) throw new IOException("The private scratch directory lease limit was reached.");
                var result = new AnalysisSharedPrivateDirectory(_shared); _shared.References++; return result;
            }
        }
    }
    private T Use<T>(Func<AnalysisPrivateDirectory, T> work)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_shared.Io) return work(_shared.Directory);
        }
    }
    internal override void Verify() => Use(directory => { directory.Verify(); return true; });
    internal override (UInt128 Capacity, UInt128 Available) Space() => Use(directory => directory.Space());
    internal override AnalysisPrivateScratchFile CreateScratch() => Use(directory => directory.CreateScratch());
    internal override AnalysisPrivateFile CreateStaging() => Use(directory => directory.CreateStaging());
    internal override AnalysisPrivateFile OpenRead(string name) => Use(directory => directory.OpenRead(name));
    internal override string[] Entries(CancellationToken cancellation) => Use(directory => directory.Entries(cancellation));
    public override void Dispose()
    {
        bool release;
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            lock (_shared.Lifetime) release = --_shared.References == 0;
        }
        if (release) lock (_shared.Io)
        {
            try { AnalysisIo.CloseOnWorker(_shared.Directory); }
            catch { _shared.Quarantine.Hold(_shared); throw; }
            if (_shared.Quota is { } quota)
                try { AnalysisIo.CloseOnWorker(quota); }
                catch { _shared.Quarantine.Hold(_shared); throw; }
        }
    }
}
