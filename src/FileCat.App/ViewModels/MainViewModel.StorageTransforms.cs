using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>Selection cleanup authority is captured before preparation, independently of later refresh generations.</summary>
internal sealed record StorageSelectionOrigin(TabViewModel Tab, Location Location, long Navigation, long Marks)
{
    internal static StorageSelectionOrigin Capture(TabViewModel tab) => new(tab,
        tab.Location ?? throw new InvalidOperationException("The selected listing is unavailable."), tab.NavigationGeneration, tab.Listing.MarkRevision);

    internal bool SameNavigation => !Tab.Listing.IsDisposed && Tab.Location == Location && Tab.NavigationGeneration == Navigation;

    internal void UnmarkCompleted(Job job)
    {
        if (job.Kind != JobKind.StorageTransform || !SameNavigation || Tab.Listing.MarkRevision != Marks) return;
        var roots = job.StorageResult?.Roots;
        if (roots is null || roots.Value.IsDefaultOrEmpty) return;
        var complete = roots.Value.Where(root => root.Completeness == StorageCompleteness.Complete &&
            root.SourceIndex >= 0 && root.SourceIndex < job.Request.Sources.Count).Select(root => root.SourceIndex).Distinct();
        try { Tab.Listing.MarkItems(job.Request.Sources, complete, false); }
        catch (ObjectDisposedException) { }
    }
}

public sealed partial class MainViewModel
{
    private readonly Dictionary<Job, StorageSelectionOrigin> _storageOrigins = [];
    internal int StorageOriginCount => _storageOrigins.Count;

    internal static bool StorageIncludesFolder(Job job, Location location) =>
        job.Kind == JobKind.StorageTransform && job.Request.StorageTransform?.Scope == StorageScope.ExistingDescendants && location.IsFileSystem &&
        job.Request.Sources.Any(source => source.Kind == EntryKind.Directory && source.FileSystemPath is { } path && PathUtil.IsSameOrUnder(location.Path, path));

    /// <summary>Bound before allocation, using marked store indices instead of a full-list selection scan.</summary>
    internal static (ItemRef[] Items, bool[] Hidden) CaptureStorageItems(ListingModel listing)
    {
        const string limit = "Select at most 64 items. A selected folder can include its existing contents.";
        if (listing.MarkedCount > 64) throw new ArgumentException(limit);
        Span<int> indices = stackalloc int[65];
        int count;
        if (listing.HasMarks)
        {
            count = listing.CopyMarkedStoreIndices(-1, indices);
            if (count > 64) throw new ArgumentException(limit);
        }
        else if (listing.TryGetFocused(out var focused) && focused.Kind != EntryKind.Parent)
        { indices[0] = listing.FocusedStoreIndex; count = 1; }
        else throw new ArgumentException("Focus or mark files or folders to change NTFS compression.");
        if (count == 0) throw new ArgumentException("The captured selection is no longer available.");
        var items = new ItemRef[count]; var hidden = new bool[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = listing.GetItemRef(indices[i]);
            hidden[i] = listing.HasMarks && listing.GetVisibleIndex(indices[i]) < 0;
        }
        return (items, hidden);
    }

    private async Task ChangeStorageAttributesAsync()
    {
        // Dispatch also reaches this guard directly from scoped Find commands.
        if (GetAvailability(Core.Commands.CommandIds.StorageAttributes) is { Enabled: false } unavailable)
        { Notify(unavailable.Reason ?? "NTFS compression is unavailable here."); return; }
        if (ActiveTab is not { Location: not null } tab) return;
        var origin = StorageSelectionOrigin.Capture(tab);
        using var initial = new PreparationScope(tab, Services.Io);
        try
        {
            var captured = CaptureStorageItems(tab.Listing);
            if (await StorageRejectedSelectionDialog.ShowIfRejectedAsync(Dialogs, captured.Items, captured.Hidden)) return;
            using var review = Services.Jobs.CaptureStorageSelection(captured.Items);
            string device = Services.Providers.For(captured.Items[0].Parent).GetDeviceKey(captured.Items[0].Parent);
            using var form = new StorageCompressionDialog(review, Services.Io, device, captured.Hidden, initial.Stop.Token, initial.Detach);
            using var choice = await form.ShowAsync(Dialogs);
            if (choice is null)
            {
                if (form.RequestedOperation is { } blocker && _view.TopLevel is MainWindow main && main.RevealStorageBlocker(blocker)) return;
                if (!form.Ready && initial.Stop.IsCancellationRequested && origin.SameNavigation && !Services.Io.IsStopped)
                    Notify("The selected listing changed while checking. Nothing was changed.");
                return;
            }
            _ = Operations; // Subscribe to terminal events before the manager can schedule this job.
            Job? registered = null;
            try
            {
                Services.Jobs.SubmitStorage(choice.Roots, choice.Approval, job =>
                {
                    registered = job;
                    _storageOrigins.Add(job, origin);
                }, choice.Mode);
            }
            catch
            {
                if (registered is not null) _storageOrigins.Remove(registered);
                throw;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { Notify("Cannot prepare NTFS compression: " + error.Message, true); }
    }
}
