using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>A file's streams and attributes (D-55): opened as a list, deleted, and saved as files.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The download marks: removing one lets the file open without the warning the system gives downloads.</summary>
    private static bool IsDownloadMark(string name) =>
        name.Equals("Zone.Identifier", StringComparison.OrdinalIgnoreCase) || name is "com.apple.quarantine" or "user.xdg.origin.url";

    private void OpenHiddenData()
    {
        if (ActiveTab is not { } tab || FocusedFileSystemPath() is not { } path)
        {
            Notify("Streams and attributes belong to files and folders on disk.", true);
            return;
        }
        var location = HiddenDataProvider.Of(path);
        if (!Services.Providers.IsRegistered(Schemes.HiddenData) || Services.Providers.For(location) is not HiddenDataProvider { Data.IsSupported: true })
        {
            Notify("This system keeps no streams or attributes beside files.", true);
            return;
        }
        tab.Navigate(location);
    }

    private (HiddenDataProvider Provider, string File) HiddenDataOf(TabViewModel tab) =>
        ((HiddenDataProvider)Services.Providers.For(tab.Location!), tab.Location!.Container!.Path);

    private static string HiddenNames(IReadOnlyList<ItemRef> items) =>
        items.Count == 1 ? $"“{items[0].Name}”" : $"{items.Count} streams and attributes";

    private async Task DeleteHiddenDataAsync(TabViewModel tab, IReadOnlyList<ItemRef> items)
    {
        var (provider, file) = HiddenDataOf(tab);
        string text = $"Delete {HiddenNames(items)} of {Path.GetFileName(file)}? This cannot be undone.";
        if (items.Any(i => IsDownloadMark(i.Name)))
            text += " Without its download mark the file opens without the warning the system gives downloaded files.";
        if (!await Dialogs.ConfirmAsync("Delete", text, "Delete", danger: true)) return;
        var failures = new List<string>();
        await Task.Run(() =>
        {
            foreach (var item in items)
            {
                try
                {
                    if (provider.Find(file, item.Name) is { } found) provider.Data.Delete(file, found);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failures.Add($"{item.Name}: {ex.Message}");
                }
            }
        });
        tab.Refresh();
        if (failures.Count > 0) Notify("Not deleted: " + string.Join("; ", failures), true);
        else Notify(items.Count == 1 ? $"Deleted “{items[0].Name}”." : $"Deleted {items.Count} streams and attributes.");
    }
}
