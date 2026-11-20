using System.Diagnostics;
using FileCat.Core.Diagnostics;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.App.Services;

/// <summary>Keeps the file retry's exchange available until the separate administrator window answers.</summary>
internal sealed record AdministratorFileRetryReview(Task<bool> Declined)
{
    internal static AdministratorFileRetryReview Start(AdministratorTask task, string root,
        Func<ProcessStartInfo, CancellationToken, Task> launch, CancellationToken stopping)
    {
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        ElevationExchange? exchange = null;
        try
        {
            exchange = task.Stage(root);
            var start = AdministratorWindow.CreateStartInfo(task: (exchange.VolumePlanPath, exchange.Hash));
            return new(WaitForDeclineAsync(exchange, launch(start, lifetime.Token), lifetime));
        }
        catch
        {
            exchange?.Dispose();
            lifetime.Dispose();
            throw;
        }
    }

    internal static void Report((string Path, string Hash) request, bool approved)
    {
        try
        {
            using var exchange = BrokerExchange.Open(request.Path);
            if (!string.Equals(ElevationPlanCodec.Hash(exchange.ReadPlan()), request.Hash, StringComparison.Ordinal)) return;
            exchange.WriteResult(new ElevationResult
            {
                Nonce = request.Hash,
                Consented = approved,
                Refused = approved ? null : ElevationMessages.Declined,
            });
        }
        catch (Exception error) when (error is InvalidDataException || ElevationPlanBuilder.IsExplainable(error))
        {
            AppLog.Warn("Could not return the administrator file review result.", error);
        }
    }

    private static async Task<bool> WaitForDeclineAsync(ElevationExchange exchange, Task exited, CancellationTokenSource lifetime)
    {
        using (exchange)
        using (lifetime)
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    await Task.WhenAny(exited, Task.Delay(200, lifetime.Token)).ConfigureAwait(false);
                    if (lifetime.IsCancellationRequested) break;
                    // A failed exit watch is not evidence that the administrator window closed.
                    if (exited.IsFaulted || exited.IsCanceled)
                    {
                        AppLog.Warn("Could not watch the administrator FileCat process.", exited.Exception);
                        exited = Task.Delay(Timeout.Infinite, lifetime.Token);
                    }
                    // If exit raced the report, read after observing the exit notification.
                    bool closed = exited.IsCompletedSuccessfully;
                    if (TryReadResult(exchange) is { Finished: false, Stopped: false, Steps.Count: 0 } result && result.Nonce == exchange.Hash)
                    {
                        if (!result.Consented && result.Refused == ElevationMessages.Declined) return true;
                        if (result.Consented && result.Refused is null) return false;
                    }
                    if (closed) return false;
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) when (ElevationPlanBuilder.IsExplainable(error))
            {
                AppLog.Warn("Could not monitor the administrator file review.", error);
            }
            finally { await lifetime.CancelAsync().ConfigureAwait(false); }
        }
        return false;
    }

    private static ElevationResult? TryReadResult(ElevationExchange exchange)
    {
        try { return exchange.ReadResult(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
    }
}
