using FileCat.Core.Resources;
using FileCat.Platform.Windows;

namespace FileCat.App.ViewModels;

/// <summary>A drive's change journal (D-56) as a list in the panel: what changed, when, and where.</summary>
public sealed partial class MainViewModel
{
    private void OpenChangeJournal()
    {
        if (!OperatingSystem.IsWindows())
        {
            Notify("Change journals are NTFS's and ReFS's: this system keeps none to list.", true);
            return;
        }
        var tab = ActiveTab;
        string? folder = tab?.Location switch
        {
            { IsFileSystem: true } l => l.Path,
            { Scheme: Schemes.Journal } l => l.Path,
            _ => null,
        };
        if (tab is null || folder is null)
        {
            Notify("Open a folder on the drive whose change journal you want to see.", true);
            return;
        }
        if (UsnJournalProvider.WhyNot(folder) is { } why)
        {
            Notify(why, true);
            return;
        }
        tab.Navigate(UsnJournalProvider.ForPath(folder));
        if (!Environment.IsPrivilegedProcess) Notify("Reading a drive's change journal needs administrator rights: run FileCat as administrator.", true);
    }
}
