using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using System.Runtime.CompilerServices;

namespace FileCat.App.Services;

/// <summary>Trusted application policy and bounded actual capture lifetime; inventories supply no authority.</summary>
internal sealed partial class AnalysisRollingHistoryController(AppServices services)
{
    private readonly SemaphoreSlim _writer = new(1, 1);
    private int _pending;
    private Exception? _unconfirmedClose;
    private readonly ConditionalWeakTable<AnalysisSession, Lazy<Task<AnalysisRollingCaptureOutcome?>>> _captures = new();
    internal Exception? UnconfirmedClose => Volatile.Read(ref _unconfirmedClose);
    internal AnalysisRollingHistoryPolicy Policy
    {
        get
        {
            var policy = services.Settings.AnalysisPreferences?.RollingHistory;
            try { policy?.Validate(); }
            catch (ArgumentException) { return AnalysisRollingHistoryPolicy.Default; }
            return policy ?? AnalysisRollingHistoryPolicy.Default;
        }
    }

    internal async Task<bool> ReviewAndApplyAsync(IDialogService dialogs, AnalysisRollingHistoryPolicy proposed,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(proposed); proposed.Validate();
        if (services.SettingsReadOnly) throw new InvalidOperationException("These settings are read-only.");
        var current = Policy;
        if (!await AnalysisRollingHistoryPolicyDialog.ConfirmAsync(dialogs, current, proposed, services.Paths.AnalysisHistoryDirectory)) return false;
        if (Interlocked.Increment(ref _pending) > AnalysisLimits.Sessions)
        { Interlocked.Decrement(ref _pending); throw new InvalidOperationException("History is busy. Wait for current work and review the policy again."); }
        bool entered = false;
        try
        {
            await _writer.WaitAsync(cancellation); entered = true;
            if (services.SettingsReadOnly || Policy != current) throw new InvalidOperationException("History policy changed during review. Review the current policy again.");
            if (proposed.Enabled && UnconfirmedClose is { } close) throw new AnalysisCloseUnconfirmedException(close);
            // This is the Settings draft commit. The Settings caller saves its whole reviewed
            // draft through the normal settings writer; cancellation never assigns this value.
            services.Settings.AnalysisPreferences = (services.Settings.AnalysisPreferences ?? new()) with { RollingHistory = proposed };
            return true;
        }
        finally { if (entered) _writer.Release(); Interlocked.Decrement(ref _pending); }
    }

    internal Task<AnalysisRollingCaptureOutcome?> CaptureCompletedAsync(AnalysisSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _captures.GetValue(session, key => new(() => CaptureOnceAsync(key), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private async Task<AnalysisRollingCaptureOutcome?> CaptureOnceAsync(AnalysisSession session)
    {
        try { await session.Completion.ConfigureAwait(false); }
        catch { return Refused("History was not captured because acquisition failed."); }
        var cancellation = CancellationToken.None;
        if (!Policy.Enabled || services.SettingsReadOnly) return null;
        if (!session.Completion.IsCompletedSuccessfully || session.Snapshot().Phase != AnalysisPhase.Finished || session.SourceCloseFailures != 0)
            return Refused("History was not captured because the live scan did not finish successfully with confirmed source cleanup.");
        if (Interlocked.Increment(ref _pending) > AnalysisLimits.Sessions)
        { Interlocked.Decrement(ref _pending); return Refused("History capture was deferred because all history work places are in use."); }
        bool entered = false; OwnedAnalysisStore? owner = null;
        AnalysisDatasetLease? dataset = null; AnalysisSnapshotLease? memory = null;
        AnalysisRollingCaptureOutcome? outcome = null; Exception? failure = null;
        try
        {
            await _writer.WaitAsync(cancellation).ConfigureAwait(false); entered = true;
            var policy = Policy;
            if (!policy.Enabled || services.SettingsReadOnly) return null;
            if (UnconfirmedClose is { } close) throw new AnalysisCloseUnconfirmedException(close);
            cancellation.ThrowIfCancellationRequested();
            dataset = await session.AcquireDatasetAsync(cancellation).ConfigureAwait(false);
            bool eligible;
            if (dataset is not null)
            {
                eligible = dataset.Descriptor.Origin == AnalysisOrigin.LiveSession && dataset.Descriptor.Phase == AnalysisPhase.Finished;
            }
            else
            {
                memory = session.AcquireSnapshot();
                eligible = memory.Snapshot.Origin == AnalysisOrigin.LiveSession && memory.Snapshot.Phase == AnalysisPhase.Finished;
            }
            if (!eligible) outcome = Refused("Only a completed live acquisition can enter automatic history.");
            else
            {
            owner = await AnalysisRollingHistoryAccess.OpenAsync(services, cancellation).ConfigureAwait(false);
            outcome = dataset is not null
                ? await owner.CaptureRollingAsync(policy, dataset, AnalysisSavedView.Default, ProtectedHistory(), cancellation).ConfigureAwait(false)
                : await owner.CaptureRollingAsync(policy, memory!.Snapshot, AnalysisSavedView.Default, ProtectedHistory(), cancellation).ConfigureAwait(false);
            }
        }
        catch (Exception error) { failure = error; outcome = (error as AnalysisRollingCaptureException)?.Outcome; }
        finally
        {
            try { if (owner is not null) await owner.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            try { if (dataset is not null) await dataset.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            try { memory?.Dispose(); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null && AnalysisCloseUnconfirmedException.IsPresent(failure)) Interlocked.CompareExchange(ref _unconfirmedClose, failure, null);
            if (entered) _writer.Release(); Interlocked.Decrement(ref _pending);
        }
        if (failure is null) return outcome;
        if (AnalysisCloseUnconfirmedException.IsPresent(failure))
        {
            Interlocked.CompareExchange(ref _unconfirmedClose, failure, null);
            return (outcome ?? Refused("History capture was not confirmed.")) with
            { State = AnalysisRollingCaptureState.Uncertain, Detail = "History cleanup could not be confirmed. Keep remaining files and inspect history before retrying." };
        }
        return outcome ?? Refused(failure is OperationCanceledException
            ? "History capture stopped. No successful capture is claimed; existing files were kept."
            : "History capture was not confirmed. Existing files were kept. " + AnalysisNames.Display(failure.Message, 400));
    }
    private static AnalysisRollingCaptureOutcome Refused(string message) => new(AnalysisRollingCaptureState.Deferred, null, null, false, [], message);
}
