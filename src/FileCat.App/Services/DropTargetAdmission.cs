using Avalonia.Input;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>One offered panel destination. Hover and Drop use the same advisory capability check.</summary>
internal static class DropTargetAdmission
{
    internal sealed record Offer(TabViewModel Tab, Location Destination, DragDropEffects Effect);
    internal const string ChangedTargetMessage = "The drop target changed during the drag. Drop again onto the folder you want to use.";
    internal const string ExternalMoveRefusal = "Moving files by dragging from another app is not available here. Release Shift to copy them, or use F5 in FileCat to choose a destination.";
    internal const string StagedMoveRefusal = "These items were copied out of an archive, a server or a device to be dragged, so dragging cannot move them. Release Shift to copy them, or use F6 to move them.";

    /// <summary>Keep the first offered destination for one native drag, even if the panel navigates under the pointer.</summary>
    internal sealed class DragIntent
    {
        internal Offer? Original { get; private set; }
        internal bool TargetChanged { get; private set; }
        internal bool MoveRefused { get; private set; }

        internal (Offer? Accepted, string? Refusal) Hover(Offer? current, string? refusal)
        {
            if (refusal == ExternalMoveRefusal) MoveRefused = true;
            else if (current is not null) MoveRefused = false;
            if (TargetChanged) return (null, ChangedTargetMessage);
            if (Original is not null && current is not null && !SameTarget(Original, current))
            {
                TargetChanged = true;
                return (null, ChangedTargetMessage);
            }
            Original ??= current;
            return (current, refusal);
        }

        internal void Reset()
        {
            Original = null;
            TargetChanged = false;
            MoveRefused = false;
        }
    }

    /// <param name="ownDrag">The drag is FileCat's own, whose source deletes nothing on the drop's result.</param>
    internal static (Offer? Accepted, string? Refusal) Check(PanelViewModel panel, bool hasFilePayload, bool move,
        bool ownDrag = false, bool ownItems = false, Location? ownSource = null, IReadOnlyList<ItemRef>? sourceItems = null)
    {
        if (!hasFilePayload)
            return (null, "This drop contains no files or folders. Select items and drop them onto a supported folder.");
        // Drop returns a native performed effect before the asynchronous transfer review/job can complete.
        // Never report Move to a source that may delete its originals on that result. FileCat's own drag deletes nothing
        // on it, so its Shift-drag is a Move job, reviewed as F6's is (a Low of the review of d1757bca: it was refused
        // as another app's).
        if (move && !ownDrag) return (null, ExternalMoveRefusal);
        var tab = panel.ActiveTab;
        if (tab is null || tab.Listing.IsDisposed || tab.Location is not { } location)
            return (null, "This panel has no available destination. Open a local folder and try again.");
        if (ownSource?.Scheme == Schemes.Registry)
        {
            if (move) return (null, "Registry drag copies or exports only. Release Shift and try again.");
            if (!location.IsFileSystem && location.Scheme != Schemes.Registry)
                return (null, "Drop Registry items onto a local folder to export .reg, or onto another Registry key to copy.");
        }
        else if (ownSource?.Scheme == Schemes.Computer)
        {
            if (move) return (null, "A whole drive can be copied, not moved. Release Shift and try again.");
            if (!location.IsFileSystem || sourceItems is null || sourceItems.Count != 1 || sourceItems[0].Kind != EntryKind.Drive)
                return (null, "Select one drive and drop it onto a local folder for a reviewed copy of its contents.");
        }
        else if (location.Scheme == Schemes.Registry && ownSource is not null)
            return (null, "Drop Registry items here to review a Registry copy. Import .reg files through File → Import .reg.");
        if (location.Scheme == Schemes.Registry && !ownItems)
            return (null, "Use File → Import .reg to review files before changing the Registry, or choose a local folder with F5.");
        if (location.Scheme == Schemes.Registry && location.Path.Length == 0)
            return (null, "Open a Registry key before dropping Registry items here, or choose a local folder with F5.");
        if (!location.IsFileSystem && location.Scheme is not (Schemes.Ftp or Schemes.Sftp or Schemes.Registry))
            return (null, "This panel cannot receive a file drop. Use F5 to choose a local folder or supported server.");
        if (tab.Listing.State != ListingState.Complete || tab.Listing.IsPartial || tab.Listing.LastRefreshError is not null)
            return (null, "This folder is not available for a drop. Reconnect or refresh it, then try again; F5 also lets you choose another destination.");
        var provider = panel.Services.Providers.For(location);
        if (location.Scheme != Schemes.Registry &&
            (provider.GetCapabilities(location) & LocationCapabilities.TransferTarget) == 0)
            return (null, provider.ExplainUnavailable(location, LocationCapabilities.TransferTarget) + " Choose a supported folder with F5 instead.");
        return (new Offer(tab, location, move ? DragDropEffects.Move : DragDropEffects.Copy), null);
    }

    internal static bool SameTarget(Offer offered, Offer current) =>
        ReferenceEquals(offered.Tab, current.Tab) && offered.Destination == current.Destination;

    /// <summary>A transfer containing files must yield every advertised item as an absolute local path.</summary>
    internal static (IReadOnlyList<string>? Paths, string? Refusal) CompletePaths(int advertisedCount, IReadOnlyList<string?> paths)
    {
        if (advertisedCount == 0 || advertisedCount != paths.Count || paths.Any(path => path is null || !Path.IsPathFullyQualified(path)))
            return (null, $"No items were accepted from the {advertisedCount:N0} dropped: some files have no usable local path. Copy them to a local folder first, then use F5.");
        return (paths.Select(path => path!).ToArray(), null);
    }
}
