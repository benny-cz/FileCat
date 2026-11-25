using FileCat.Core.Diagnostics;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>Items asked for while the list is drawn: a provider's refusal never ends a frame (I649).</summary>
public sealed partial class TabViewModel
{
    /// <summary>The visit (<see cref="NavigationGeneration"/>) whose listing was last read again for a row it could not describe.</summary>
    private long _undescribedRowRefresh = -1;

    /// <summary>
    /// What a provider throws for a row it listed but can no longer describe: the streams and attributes provider's
    /// "Supplementary row category is unavailable; refresh required." among them.
    /// </summary>
    internal static bool IsUndescribedRow(Exception error) => error is InvalidOperationException or NotSupportedException or
        ArgumentException or IOException or UnauthorizedAccessException;

    /// <summary>
    /// The item a listed row stands for, asked while the list is drawn; null when the provider can no longer describe the
    /// row. The row is then drawn without what needed the item, and the listing is read again after the frame.
    /// </summary>
    internal ItemRef? ItemForDrawing(ResourceProvider provider, Location location, in EntryData entry)
    {
        try { return provider.GetItemRef(location, entry); }
        catch (Exception error) when (IsUndescribedRow(error))
        {
            RefreshForUndescribedRow(error);
            return null;
        }
    }

    /// <summary>
    /// Reads the listing again, after the frame that met a row its provider could not describe: once a visit, so a provider
    /// that keeps refusing gets plain rows and no endless rereads. Refresh (Ctrl+R) still reads it again.
    /// </summary>
    private void RefreshForUndescribedRow(Exception error)
    {
        if (_disposed || _undescribedRowRefresh == NavigationGeneration) return;
        long visit = _undescribedRowRefresh = NavigationGeneration;
        // The type only: provider messages can name paths, which the log keeps out.
        AppLog.Warn($"A listed row could not be described while it was drawn ({error.GetType().Name}); the listing is read again.");
        Services.Ui.Post(() =>
        {
            if (_disposed || NavigationGeneration != visit || Listing.State != ListingState.Complete || Listing.IsRefreshing) return;
            Listing.Refresh();
        });
    }
}
