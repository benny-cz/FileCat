using FileCat.Core.Resources;
using FileCat.Platform.Windows;

namespace FileCat.App.ViewModels;

/// <summary>A drive's change journal (D-56) as a list in the panel: what changed, when, and where.</summary>
public sealed partial class MainViewModel
{
    private async Task OpenChangeJournalAsync()
    {
        if (!OperatingSystem.IsWindows()) return;
        var tab = ActiveTab;
        var location = tab?.Location;
        string? folder = location switch
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
        var (journal, problem) = await Task.Run(() => UsnJournalProvider.Resolve(folder));
        if (ActiveTab != tab || tab.Location != location || Dialogs.IsOpen) return;
        if (journal is null)
        {
            Notify(problem!, true);
            return;
        }
        if (Services.Shell.IsElevated) tab.Navigate(journal);
        else await OpenAdministratorWindowAsync(journal);
    }

    private void OpenStartupChangeJournal(string root, string volume)
    {
        if (!OperatingSystem.IsWindows() || !Services.Shell.IsElevated || Workspace.ActivePanel is not { } panel) return;
        // The provider rechecks the selected mount before opening the captured volume device.
        panel.OpenTab(new Location(Schemes.Journal, root, session: volume));
    }
}
