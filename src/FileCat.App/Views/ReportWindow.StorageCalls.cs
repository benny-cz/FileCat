using Avalonia.Controls;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.Core.Jobs;

namespace FileCat.App.Views;

public sealed partial class ReportWindow
{
    internal Task? StorageExporting { get; private set; }

    internal void ConfigureStorageCallExport(Func<CancellationToken, Task> save)
    {
        var button = new Button { Content = "Save all recorded calls…" };
        ToolTip.SetTip(button, "Save every record in this captured snapshot to a new file. Existing files are never replaced. Close this report to cancel.");
        button.Click += async (_, _) =>
        {
            if (_reading is not null || _closing.IsCancellationRequested || StorageExporting is { IsCompleted: false }) return;
            button.IsEnabled = false;
            try { StorageExporting = save(_closing.Token); await StorageExporting; }
            catch (OperationCanceledException) when (_closing.IsCancellationRequested) { }
            catch (Exception error) { if (!_closing.IsCancellationRequested) _status.Text = "Storage call export did not complete: " + Formatters.SafeName(error.Message); }
            finally { if (!_closing.IsCancellationRequested) button.IsEnabled = true; }
        };
        _actions.Children.Add(button);
    }

    internal static ReportWindow CreateStorageCalls(string subject, StorageJournalPrefix prefix, bool activeAtCapture,
        Func<long, CancellationToken, Task<StorageCallEvidencePage>> load)
    {
        long after = 0; int generation = 0; StorageCallEvidencePage? current = null;
        var first = new Button { Content = "First recorded call page", IsEnabled = false };
        var next = new Button { Content = "Next recorded call page", IsEnabled = false };
        ReportWindow? window = null;
        window = new ReportWindow("Recorded storage calls", subject, async ct =>
        {
            long requestedAfter = Interlocked.Read(ref after);
            int request = Interlocked.Increment(ref generation);
            Dispatcher.UIThread.Post(() =>
            {
                if (!ct.IsCancellationRequested && request == Volatile.Read(ref generation) && !window!._closing.IsCancellationRequested)
                { current = null; first.IsEnabled = next.IsEnabled = false; }
            });
            var page = await load(requestedAfter, ct).ConfigureAwait(false);
            if (page.Prefix != prefix || page.AfterCall != requestedAfter)
                throw new IOException("The reader returned a different durable storage prefix or call continuation.");
            ct.ThrowIfCancellationRequested();
            // The page becomes current before the read completes (I674): Navigate then sets the buttons from this page,
            // not from the previous one, which had left Next enabled on the last page until a posted update ran.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || window!._closing.IsCancellationRequested || request != Volatile.Read(ref generation)) return;
                current = page; first.IsEnabled = requestedAfter != 0; next.IsEnabled = page.More;
            });
            return StorageCallEvidenceText.Format(page, activeAtCapture);
        });
        window._refresh.IsVisible = false;
        window._copyAll.Content = "Copy this call page";
        window._save.Content = "Save this call page…";
        ToolTip.SetTip(window._copyAll, "Copy this displayed page of the captured call evidence.");
        ToolTip.SetTip(window._save, "Save this displayed call page; other pages remain available through navigation.");
        window._actions.Children.Add(first); window._actions.Children.Add(next);
        async Task Navigate(bool beginning)
        {
            if (window._reading is not null || !beginning && current?.More != true) return;
            after = beginning ? 0 : current!.NextAfterCall;
            first.IsEnabled = next.IsEnabled = false;
            try { await window.ReadAsync(); }
            finally
            {
                if (!window._closing.IsCancellationRequested)
                { first.IsEnabled = after != 0; next.IsEnabled = current?.More == true; }
            }
        }
        first.Click += async (_, _) => await Navigate(true); next.Click += async (_, _) => await Navigate(false);
        return window;
    }

    internal void ConfigureStorageCalls(Func<Task> open)
    {
        var button = new Button { Content = "View recorded calls…" };
        button.Click += async (_, _) =>
        {
            if (_reading is not null || _closing.IsCancellationRequested) return;
            button.IsEnabled = false;
            try { await open(); }
            finally { if (!_closing.IsCancellationRequested) button.IsEnabled = true; }
        };
        _actions.Children.Add(button);
    }
}
