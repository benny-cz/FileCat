using Avalonia.Threading;
using FileCat.App.ViewModels;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;

namespace FileCat.App.Views;

public sealed partial class FindWindow
{
    private bool _storagePublishing, _storageRefreshQueued;
    internal bool StorageResultsSettled { get; private set; }
    private const string StorageUpdatingReason = "Stop the search or wait for results to finish updating before changing compression.";

    private CommandAvailability StorageAvailability()
    {
        // A newly requested sort/filter may precede its first Changed callback. These are cached scalar reads.
        UpdateStorageAvailability();
        if (_dialogs.IsOpen) return CommandAvailability.No("Close the current dialog before changing compression.");
        return StorageResultsSettled ? CommandAvailability.Yes : CommandAvailability.No(StorageUpdatingReason);
    }

    internal CommandAvailability StorageCommandAvailability => _scope is null ? StorageAvailability()
        : _vm.GetAvailability(Core.Commands.CommandIds.StorageAttributes, _scope);

    private void OnStorageListingChanged(object? sender, ListingChange change) => UpdateStorageAvailability();

    private void UpdateStorageAvailability()
    {
        var listing = _tab?.Listing;
        StorageResultsSettled = !_closed && !_storagePublishing && !_comparing && _duplicates is null &&
            _session is { Finished: true } && !_timer.IsEnabled && listing is { IsDisposed: false, State: ListingState.Complete, IsRefreshing: false } &&
            listing.AppliedViewSpecVersion == listing.ViewSpecVersion;
    }

    private void OnStorageJobFinished(Job job)
    {
        if (job.Kind != JobKind.StorageTransform) return;
        _services.Ui.Post(() =>
        {
            if (_closed || _tab is null || _storageRefreshQueued) return;
            _storageRefreshQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _storageRefreshQueued = false;
                if (_closed || _tab is null || _tab.Listing.IsDisposed) return;
                // Result references can span many originals. Refresh the existing tab once, without scanning
                // its rows on the UI thread or changing its query, location, navigation or marks.
                Refresh(force: true);
                UpdateStorageAvailability();
            }, DispatcherPriority.Background);
        });
    }
}
