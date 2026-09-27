using FileCat.Core.Diagnostics;

namespace FileCat.App.ViewModels;

public sealed partial class MainViewModel
{
    private int _commandHistoryIndex = -1;

    /// <summary>
    /// Runs the command line in the configured shell in a visible terminal at the active location
    /// (plan §14.2). A leading <c>cd</c> changes the panel location instead.
    /// </summary>
    public void RunCommandLine()
    {
        var text = CommandLineText.Trim();
        if (text.Length == 0) return;
        _commandHistoryIndex = -1;
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

    /// <summary>Alt+F8 (TC/FAR): pick an earlier command; Enter puts it on the command line, Shift+Enter runs it.</summary>
    private async Task ShowCommandHistoryAsync()
    {
        var history = Services.History.CommandLine;
        if (history.Count == 0)
        {
            Notify("No commands yet. The command line (Ctrl+E) remembers what you run.");
            return;
        }
        var items = history.Select(c => new ChoiceItem(c)).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Command history", items)
        {
            Hint = "Type to filter · Enter puts it on the command line · Shift+Enter runs it · Del removes",
            AllowDelete = true,
        });
        string? chosen = r.Index >= 0 ? items[r.Index].Title : null;
        foreach (var d in r.Deleted.OrderByDescending(i => i))
        {
            if (d < history.Count) history.RemoveAt(d);
        }
        if (chosen is null) return;
        ShowCommandLine = true;
        CommandLineText = chosen;
        if (r.Alternate) RunCommandLine();
        else View.FocusCommandLine();
    }

    /// <summary>Up/Down in the command line walk its history.</summary>
    public void CommandLineHistory(int delta)
    {
        var h = Services.History.CommandLine;
        if (h.Count == 0) return;
        _commandHistoryIndex = Math.Clamp(_commandHistoryIndex + delta, -1, h.Count - 1);
        CommandLineText = _commandHistoryIndex < 0 ? string.Empty : h[_commandHistoryIndex];
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
