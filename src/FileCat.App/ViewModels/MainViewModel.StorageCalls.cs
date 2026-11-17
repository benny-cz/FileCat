using FileCat.App.Views;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    internal ReportWindow? ShowStorageCallEvidence(Job job, StorageJournalPrefix prefix, bool activeAtCapture)
    {
        if (!EditSessionsActive || !Services.Jobs.SupportsStorageHistory || job.Kind != JobKind.StorageTransform || prefix.JobId != job.Id)
        { Notify("Recorded storage calls are unavailable for this operation on this host.", true); return null; }
        return ShowStorageCallEvidence(job.Title, StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), prefix, activeAtCapture);
    }

    internal ReportWindow? ShowStorageCallEvidence(string title, string leaf, StorageJournalPrefix prefix, bool activeAtCapture)
    {
        if (!EditSessionsActive || !Services.Jobs.SupportsStorageHistory || !StoragePaths.IsChildName(leaf) ||
            !StorageJournalPolicy.TryJobId(leaf, out var id) || id != prefix.JobId)
        { Notify("Recorded storage calls are unavailable for this history on this host.", true); return null; }
        var accounting = StorageAccounting; // compose on the UI thread before a report producer starts
        string device = PathUtil.GetDeviceKey(Services.Paths.JournalDirectory);
        var report = ReportWindow.CreateStorageCalls(Formatters.SafeName(title), prefix, activeAtCapture, async (after, ct) =>
        {
            try
            {
                if (!EditSessionsActive || Services.Io.IsStopped) throw new IOException("This FileCat session has closed.");
                using var interactive = await accounting.EnterInteractiveAsync(ct);
                return await Services.Io.RunOwned(device, IoPriority.Interactive,
                    token => Services.Jobs.ReadStorageCalls(leaf, prefix, after, token), ct);
            }
            catch (ObjectDisposedException error) { throw new IOException("This FileCat session has closed.", error); }
            catch (OperationCanceledException error) when (!ct.IsCancellationRequested && Services.Io.IsStopped)
            { throw new IOException("This FileCat session has closed.", error); }
        });
        report.Show(); return report;
    }
}
