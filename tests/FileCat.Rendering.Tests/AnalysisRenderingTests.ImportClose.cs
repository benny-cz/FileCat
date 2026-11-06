using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task CaughtCloseFailureRemainsLatchedUntilAllAcceptedViewWorkReturns()
    {
        var work = new AnalysisViewWork(); var accepted = work.Enter(); var closing = work.CloseAsync();
        var first = new InvalidDataException("ordinary operation failure");
        var failure = new AnalysisPublicationUncertainException("saved.fcdsa", new AnalysisCloseUnconfirmedException(new IOException("injected close result")));
        work.RecordUnconfirmedClose(failure); Assert.False(closing.IsCompleted); accepted.Dispose();
        Assert.Same(failure, await Assert.ThrowsAsync<AnalysisPublicationUncertainException>(() => closing));
        Assert.True(AnalysisCloseUnconfirmedException.IsPresent(AnalysisViewWork.PreserveCloseFailure(first, failure)));
        Assert.Same(first, AnalysisViewWork.PreserveCloseFailure(first, new IOException("ordinary read failure")));
        var ordinary = new AnalysisViewWork(); ordinary.RecordUnconfirmedClose(first); await ordinary.CloseAsync();
        var failed = new AnalysisViewWork(); failed.RecordUnconfirmedClose(failure); Assert.True(failed.HasUnconfirmedClose);
        Assert.Throws<AnalysisCloseUnconfirmedException>(() => failed.Enter());
        await Assert.ThrowsAsync<AnalysisPublicationUncertainException>(() => failed.CloseAsync());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedPreparedImportKeepsAdmissionAfterWindowClosureAndReplacement(bool wrapped)
    {
        using var styles = new ProductStyles(); var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        var source = new TaskCompletionSource<AnalysisDatasetLease>(TaskCreationOptions.RunContinuationsAsynchronously);
        var opening = window.AddPreparedIndexedSessionAsync(_ => source.Task, AnalysisMetric.OnDisk, null, null, TestContext.Current.CancellationToken);
        var slots = new List<AnalysisSessionAdmission.Slot>();
        try
        {
            for (int n = 0; n < 7; n++) slots.Add(pool.Acquire()); Assert.False(pool.HasCapacity);
            window.ForceClose(); var closing = window.WaitForIndexedCloseAsync(); Assert.False(opening.IsCompleted); Assert.False(closing.IsCompleted);
            var failure = new AnalysisCloseUnconfirmedException(new IOException("injected unconfirmed import closure"));
            source.SetException(wrapped ? new IOException("operation wrapper", failure) : failure);
            await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => opening);
            var observed = await Assert.ThrowsAsync<IOException>(() => closing); Assert.True(AnalysisCloseUnconfirmedException.IsPresent(observed));
            Assert.True(pool.HasFailedClose); Assert.False(pool.HasCapacity); Assert.Throws<InvalidOperationException>(() => pool.Acquire());
            foreach (var slot in slots) await slot.CloseAfterAsync(Task.CompletedTask); slots.Clear();
            var replacement = new DiskSpaceWindow(null, null, pool);
            try
            {
                replacement.AddSession(new DiskSpaceViewModel(Fixture() with { Origin = AnalysisOrigin.ImportedInventory }));
                replacement.Show(); Frame(replacement);
                Assert.Contains(replacement.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text == AnalysisSessionAdmission.FailedCloseMessage);
                // One permanently failed slot plus this window's normal slot leave six places.
                for (int n = 0; n < 6; n++) slots.Add(pool.Acquire()); Assert.Throws<InvalidOperationException>(() => pool.Acquire());
            }
            finally { replacement.ForceClose(); await replacement.WaitForIndexedCloseAsync(); }
        }
        finally
        {
            source.TrySetException(new AnalysisCloseUnconfirmedException(new IOException("test cleanup"))); window.ForceClose();
            try { await opening; } catch (AnalysisCloseUnconfirmedException) { }
            try { await window.WaitForIndexedCloseAsync(); } catch (IOException) { }
            foreach (var slot in slots) await slot.CloseAfterAsync(Task.CompletedTask);
        }
    }

    [AvaloniaFact]
    public async Task RejectedPreparedInputAfterConfirmedCleanupReturnsAdmissionWithoutLatchingACloseFailure()
    {
        var pool = new AnalysisSessionAdmission(); var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => window.AddPreparedIndexedSessionAsync(
                _ => Task.FromException<AnalysisDatasetLease>(new InvalidDataException("injected invalid input")), AnalysisMetric.OnDisk, null, null, TestContext.Current.CancellationToken));
            Assert.False(pool.HasFailedClose);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        var slots = new List<AnalysisSessionAdmission.Slot>();
        try { for (int n = 0; n < AnalysisLimits.Sessions; n++) slots.Add(pool.Acquire()); }
        finally { foreach (var slot in slots) await slot.CloseAfterAsync(Task.CompletedTask); }
    }
}
