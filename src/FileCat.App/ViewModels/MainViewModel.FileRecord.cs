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
        var records = Services.Platform.FileRecords;
        if (!records.IsSupported)
        {
            Notify("FileCat does not read file-system records on this system.", true);
            return;
        }
        new ReportWindow("File-system record", path, ct => Task.FromResult(records.Read(path, ct).ToText())).Show();
    }
}
