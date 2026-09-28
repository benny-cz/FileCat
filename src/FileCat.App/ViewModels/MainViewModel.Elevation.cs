using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>
    /// Retries the items of a finished operation that failed for lack of permission through the per-plan
    /// administrator broker (ADR-14): one UAC approval, then the broker's own window shows exactly these steps.
    /// FileCat itself never runs elevated (AI-05).
    /// </summary>
    public async Task RetryElevatedAsync(Job job)
    {
        if (!OperatingSystem.IsWindows() || !ElevationPlanBuilder.OffersRetry(job)) return;
        if (ElevationBroker.Locate(Services.Paths.IsPortable, out var reason) is null)
        {
            await Dialogs.AlertAsync("Administrator retry is not available", reason!);
            return;
        }
        ElevationRetry retry;
        try { retry = await Task.Run(() => ElevationPlanBuilder.Build(job, Environment.ProcessId)); }
        catch (Exception ex) when (ElevationPlanBuilder.IsExplainable(ex))
        {
            await Dialogs.AlertAsync("Cannot retry as administrator", ex.Message);
            return;
        }
        job.MarkRetriedAsAdministrator();
        var elevated = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Elevated,
            Elevation = retry.Plan,
            Sources = retry.Sources,
            Destination = retry.Destination,
            Description = "As administrator: " + retry.Plan.Title,
        });
        if (ActiveTab is { } tab) Track(elevated, tab);
        Notify("Windows asks for administrator approval; then the FileCat administrator window shows the exact steps for you to approve.");
    }
}
