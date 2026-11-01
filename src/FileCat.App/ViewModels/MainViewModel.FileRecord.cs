using FileCat.App.Views;

namespace FileCat.App.ViewModels;

/// <summary>An item's file-system record (D-56): what the file system itself keeps about it, in a report window.</summary>
public sealed partial class MainViewModel
{
    private void ShowFileRecord()
    {
        if (FocusedFileSystemPath() is not { } path)
        {
            Notify("File-system records belong to files and folders on disk.", true);
            return;
        }
        if (!ShowFileRecordForPath(path))
            Notify("FileCat does not read file-system records on this system.", true);
    }

    /// <summary>The selected panel item and context-menu paths share one exact-path report route.</summary>
    private bool ShowFileRecordForPath(string path)
    {
        var records = Services.Platform.FileRecords;
        if (!records.IsSupported) return false;
        new ReportWindow("File-system record", path, ct => Task.FromResult(records.Read(path, ct).ToText())).Show();
        return true;
    }
}
