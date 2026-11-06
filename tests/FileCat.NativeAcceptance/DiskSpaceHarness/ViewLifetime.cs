using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private async Task ViewLifetimeProbeAsync(AnalysisSnapshot snapshot)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        AnalysisOrderedRows? lateOrder = null;
        using var held = new DiskSpaceViewModel(snapshot, (rows, field, descending, _) =>
        {
            entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(30));
            return lateOrder = AnalysisTableOrder.Apply(rows, field, descending, CancellationToken.None);
        });
        var old = new DiskSpaceWindow(); var next = new DiskSpaceWindow();
        using var replacement = new DiskSpaceViewModel(snapshot);
        Task? projection = null;
        try
        {
            old.AddSession(held); old.Show(); next.Show();
            // The main native analysis already owns one slot. These six plus the held view
            // exhaust the single process-wide cap even though they occupy separate windows.
            for (int i = 0; i < 6; i++) next.AddSession(new DiskSpaceViewModel(snapshot));
            projection = held.RebuildAsync(true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            old.ForceClose(); var closing = old.WaitForIndexedCloseAsync();
            if (closing.IsCompleted || held.CloseCompletion.IsCompleted || next.CanAddSession)
                throw new IOException("Closing the native window released its slot before the actual projection returned.");
            bool refused = false;
            try { next.AddSession(replacement); }
            catch (InvalidOperationException error) when (error.Message.StartsWith("Eight analyses", StringComparison.Ordinal)) { refused = true; }
            if (!refused) throw new IOException("A replacement window bypassed process-wide session admission.");
            release.Set(); await projection; await closing.WaitAsync(TimeSpan.FromSeconds(10));
            bool released = false;
            try { _ = lateOrder!.Rows; } catch (ObjectDisposedException) { released = true; }
            if (!released) throw new IOException("Late projection ordering was not released after actual return.");
            next.AddSession(replacement);
            if (next.SessionCount != 7 || next.CanAddSession) throw new IOException("The native replacement did not reuse exactly one released slot.");
        }
        finally
        {
            release.Set(); if (projection is not null) await projection;
            old.ForceClose(); next.ForceClose();
            await Task.WhenAll(old.WaitForIndexedCloseAsync(), next.WaitForIndexedCloseAsync());
        }
    }
}
