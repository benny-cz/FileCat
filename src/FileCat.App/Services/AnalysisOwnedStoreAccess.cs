using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.App.Services;

internal static class AnalysisOwnedStoreAccess
{
    internal static Task<OwnedAnalysisStore> OpenAsync(AppServices services, CancellationToken cancellation)
    {
        string path = services.Paths.AnalysisSnapshotsDirectory;
        string parent = Path.GetDirectoryName(path) ?? throw new IOException("The private profile parent is unavailable.");
        string name = Path.GetFileName(path);
        return OwnedAnalysisStore.OpenAsync(services.Io, PathUtil.GetDeviceKey(path),
            () => OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(parent, name) : new UnixAnalysisPrivateDirectory(parent, name), cancellation);
    }
    internal static async Task<OwnedAnalysisInventoryDetails> InspectAsync(AppServices services, Guid inventory, CancellationToken cancellation)
    {
        await using var owned = await OpenAsync(services, cancellation);
        return await owned.InspectAsync(inventory, cancellation);
    }
    internal static async Task<AnalysisOwnedMaintenanceCapture> PrepareAsync(AppServices services, Guid inventory,
        AnalysisViewWork work, CancellationToken cancellation)
    {
        var actual = work.Enter(); OwnedAnalysisStore? store = null;
        try
        {
            store = await OpenAsync(services, cancellation); var review = await store.PrepareDeleteAsync(inventory, cancellation);
            return new(store, review, actual, work.RecordUnconfirmedClose,
                () => services.AnalysisSavedReferences.RemovedAsync(review.Entry.InventoryId, review.Entry.Name, comparison: false));
        }
        catch (Exception error)
        {
            work.RecordUnconfirmedClose(error);
            try { if (store is not null) await store.DisposeAsync(); }
            catch (Exception close) { work.RecordUnconfirmedClose(close); throw new AggregateException(error, close); }
            finally { actual.Dispose(); }
            throw;
        }
    }
    internal static async Task<AnalysisOwnedComparisonMaintenanceCapture> PrepareComparisonAsync(AppServices services, string name,
        AnalysisViewWork work, CancellationToken cancellation)
    {
        var actual = work.Enter(); OwnedAnalysisStore? store = null;
        try
        {
            store = await OpenAsync(services, cancellation); var review = await store.PrepareComparisonDeleteAsync(name, cancellation);
            return new(store, review, actual, work.RecordUnconfirmedClose,
                () => services.AnalysisSavedReferences.RemovedAsync(review.ComparisonId, review.Name, comparison: true));
        }
        catch (Exception error)
        {
            work.RecordUnconfirmedClose(error);
            try { if (store is not null) await store.DisposeAsync(); }
            catch (Exception close) { work.RecordUnconfirmedClose(close); throw new AggregateException(error, close); }
            finally { actual.Dispose(); }
            throw;
        }
    }
}

internal sealed class AnalysisOwnedMaintenanceCapture(OwnedAnalysisStore store, OwnedAnalysisDeleteReview review,
    IDisposable actual, Action<Exception> recordCloseFailure, Func<Task> removed) : IAsyncDisposable
{
    internal OwnedAnalysisDeleteReview Review => review;
    internal async Task<OwnedAnalysisDeletionOutcome> ChangeAsync(bool restore, CancellationToken cancellation)
    {
        try
        {
            var outcome = restore ? await store.RestoreNamedAsync(review, cancellation) : await store.DeleteAsync(review, cancellation);
            if (!restore && outcome.State == OwnedAnalysisDeletionState.Deleted) await removed();
            return outcome;
        }
        catch (OwnedAnalysisDeletionException error)
        {
            if (!restore && (error.Outcome.PayloadAbsentConfirmed || error.Outcome.State == OwnedAnalysisDeletionState.Uncertain)) await removed();
            recordCloseFailure(error); throw;
        }
        catch (Exception error) { recordCloseFailure(error); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { await store.DisposeAsync(); }
        catch (Exception error) { recordCloseFailure(error); throw; }
        finally { actual.Dispose(); }
    }
}

internal sealed class AnalysisOwnedComparisonMaintenanceCapture(OwnedAnalysisStore store, OwnedAnalysisComparisonDeleteReview review,
    IDisposable actual, Action<Exception> recordCloseFailure, Func<Task> removed) : IAsyncDisposable
{
    internal OwnedAnalysisComparisonDeleteReview Review => review;
    internal async Task<OwnedAnalysisComparisonDeletionOutcome> DeleteAsync(CancellationToken cancellation)
    {
        try
        {
            var outcome = await store.DeleteComparisonAsync(review, cancellation);
            if (outcome.State == OwnedAnalysisDeletionState.Deleted) await removed();
            return outcome;
        }
        catch (OwnedAnalysisComparisonDeletionException error)
        {
            if (error.Outcome.RemovalConfirmed || error.Outcome.State == OwnedAnalysisDeletionState.Uncertain) await removed();
            recordCloseFailure(error); throw;
        }
        catch (Exception error) { recordCloseFailure(error); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        try { await store.DisposeAsync(); }
        catch (Exception error) { recordCloseFailure(error); throw; }
        finally { actual.Dispose(); }
    }
}
