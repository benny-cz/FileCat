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

    /// <summary>Keep the first offered destination for one native drag, even if the panel navigates under the pointer.</summary>
    internal sealed class DragIntent
    {
        internal Offer? Original { get; private set; }
        internal bool TargetChanged { get; private set; }

        internal (Offer? Accepted, string? Refusal) Hover(Offer? current, string? refusal)
        {
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
        }
    }

    internal static (Offer? Accepted, string? Refusal) Check(PanelViewModel panel, bool hasFilePayload, bool move)
    {
        if (!hasFilePayload)
            return (null, "This drop contains no files or folders. Select files and drop them onto a local folder.");
        var tab = panel.ActiveTab;
        if (tab is null || tab.Listing.IsDisposed || tab.Location is not { } location)
            return (null, "This panel has no available destination. Open a local folder and try again.");
        if (!location.IsFileSystem)
            return (null, "This panel cannot receive dropped files. Open a local folder here, or use F5 to choose a supported destination.");
        if (tab.Listing.State != ListingState.Complete || tab.Listing.IsPartial || tab.Listing.LastRefreshError is not null)
            return (null, "This folder is not available for a drop. Reconnect or refresh it, then try again; F5 also lets you choose another destination.");
        var provider = panel.Services.Providers.For(location);
        if ((provider.GetCapabilities(location) & LocationCapabilities.TransferTarget) == 0)
            return (null, provider.ExplainUnavailable(location, LocationCapabilities.TransferTarget) + " Choose a local folder with F5 instead.");
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
