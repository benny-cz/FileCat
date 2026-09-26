using FileCat.Core.Diagnostics;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>File operations (copy, move, delete, create, view, edit) routed to the operation engine.</summary>
public sealed partial class MainViewModel
{
    private partial Task<bool> ExecuteOperationCommandAsync(string id) => Task.FromResult(false);

    public bool CanCloseImmediately() => true;

    public Task<bool> ConfirmExitWithActiveWorkAsync() => Task.FromResult(true);

    /// <summary>
    /// Runs the command line in the configured shell in a visible terminal at the active location
    /// (plan §14.2). A leading <c>cd</c> changes the panel location instead.
    /// </summary>
    public void RunCommandLine()
    {
        var text = CommandLineText.Trim();
        if (text.Length == 0) return;
        Services.History.CommandLine.Remove(text);
        Services.History.CommandLine.Insert(0, text);
        if (Services.History.CommandLine.Count > 100) Services.History.CommandLine.RemoveRange(100, Services.History.CommandLine.Count - 100);
        var tab = ActiveTab;
        if (TryParseCd(text, out var arg))
        {
            CommandLineText = string.Empty;
            if (tab is null) return;
            if (arg.Length == 0)
            {
                Notify(tab.DisplayPath);
                return;
            }
            if (Services.Providers.TryParse(arg, tab.Location, out var loc) && loc is not null) tab.Navigate(loc);
            else Notify($"cd: \"{arg}\" is not a location FileCat can open.", true);
            View.FocusActivePanel();
            return;
        }
        if (tab?.Location is not { IsFileSystem: true } here)
        {
            Notify("Commands run in a file-system folder; this location is not one.", true);
            return;
        }
        try
        {
            Services.Shell.RunInTerminal(here.Path, text, Services.Settings.Terminal.Shell);
            AppLog.Info("Command line started in terminal (" + Services.Settings.Terminal.Shell + ")");
            CommandLineText = string.Empty;
        }
        catch (Exception ex)
        {
            Notify("Could not start the terminal: " + ex.Message, true);
        }
    }

    private static bool TryParseCd(string text, out string argument)
    {
        argument = string.Empty;
        var t = text.TrimStart();
        if (t.Equals("cd", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("cd..", StringComparison.OrdinalIgnoreCase) || t.StartsWith("cd\\", StringComparison.OrdinalIgnoreCase) || t.StartsWith("cd/", StringComparison.OrdinalIgnoreCase))
        {
            argument = t[2..].Trim();
            return true;
        }
        if (t.StartsWith("cd ", StringComparison.OrdinalIgnoreCase) || t.StartsWith("chdir ", StringComparison.OrdinalIgnoreCase))
        {
            argument = t[(t.IndexOf(' ') + 1)..].Trim();
            if (argument.StartsWith("/d ", StringComparison.OrdinalIgnoreCase)) argument = argument[3..].Trim();
            argument = argument.Trim('"');
            return true;
        }
        return false;
    }
}
