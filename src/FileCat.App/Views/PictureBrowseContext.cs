using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Views;

/// <summary>
/// The panel order in which a picture viewer was opened. It keeps no listing or image cache alive: a closed tab or a
/// panel that leaves the folder ends browsing, and each Space press reads only the current visible order.
/// </summary>
internal sealed class PictureBrowseContext(TabViewModel tab, ItemRef opened)
{
    private static readonly HashSet<string> PictureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "jpe", "jfif", "gif", "webp", "bmp", "dib", "ico", "cur",
    };

    private readonly WeakReference<TabViewModel> _tab = new(tab);
    private readonly Location _folder = opened.Parent;
    private ItemRef _cursor = opened;

    public static bool IsPictureName(string name) => PictureExtensions.Contains(NameParts.GetExtension(name));

    /// <summary>Advance past an attempted item, including a damaged one, so another press can continue.</summary>
    public ItemRef? Step(int direction, out string reason)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (!_tab.TryGetTarget(out var tab) || !Equals(tab.Location, _folder) || tab.Listing.IsDisposed)
        {
            reason = "The source panel left this folder. Open a picture from the folder again to browse it.";
            return null;
        }
        var listing = tab.Listing;
        int currentStore = listing.FindStoreIndex(_cursor.Name);
        if (currentStore < 0 || !listing.GetItemRef(currentStore).Equals(_cursor))
        {
            // Archive members may share a name. The ordinary lookup finds its first occurrence;
            // only that exceptional case needs an exact-ordinal scan of the visible rows.
            currentStore = -1;
            for (int row = 0; row < listing.VisibleCount; row++)
            {
                int candidate = listing.GetStoreIndex(row);
                if (!string.Equals(listing.GetVisible(row).Name, _cursor.Name, StringComparison.Ordinal) ||
                    !listing.GetItemRef(candidate).Equals(_cursor)) continue;
                currentStore = candidate;
                break;
            }
        }
        if (currentStore < 0)
        {
            reason = "The current picture is no longer in the folder view. Open it from the panel again to browse.";
            return null;
        }
        int start = listing.GetVisibleIndex(currentStore);
        if (start < 0)
        {
            reason = "The current picture is hidden by the panel filter. Clear the filter or open a visible picture.";
            return null;
        }
        int count = listing.VisibleCount;
        for (int index = start + direction; index >= 0 && index < count; index += direction)
        {
            var entry = listing.GetVisible(index);
            if (entry.Kind != EntryKind.File || !IsPictureName(entry.Name)) continue;
            var next = listing.GetItemRef(listing.GetStoreIndex(index));
            if (next.Equals(_cursor)) continue;
            _cursor = next;
            reason = string.Empty;
            return next;
        }
        reason = direction > 0 ? "Last supported picture in this folder." : "First supported picture in this folder.";
        return null;
    }
}
