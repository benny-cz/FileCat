using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.App.ViewModels;

/// <summary>Integrated recovery (plan §17, P10): the deleted items of a disk image, browsed and recovered like any folder.</summary>
public sealed partial class MainViewModel
{
    private async Task FindDeletedAsync()
    {
        var tab = ActiveTab;
        if (tab?.Location is null) return;
        string? image = tab.Listing.TryGetFocused(out var focused) && focused.Kind == EntryKind.File
            ? tab.Listing.GetItemRef(tab.Listing.FocusedStoreIndex).FileSystemPath
            : null;
        if (image is null)
        {
            Notify("Focus a disk image (a raw .img, .dd, or .bin file, or a fixed .vhd) to find its deleted files. For a drive, make an image of it on another drive first: scanning the image leaves the drive untouched.", true);
            return;
        }
        int volumes;
        try
        {
            volumes = await Task.Run(() =>
            {
                using var source = new ImageFileSource(image);
                return PartitionTable.Find(source, []).Count;
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Notify($"\"{Path.GetFileName(image)}\" cannot be read as a disk image: {ex.Message}", true);
            return;
        }
        if (volumes == 0)
        {
            Notify($"\"{Path.GetFileName(image)}\" is not a disk image FileCat reads: it has no partition table and no NTFS, FAT, or exFAT file system.", true);
            return;
        }
        // One volume opens directly; a partitioned disk lists its volumes first.
        tab.Navigate(RecoveryProvider.ForImage(image, volumes == 1 ? 1 : null));
    }
}
