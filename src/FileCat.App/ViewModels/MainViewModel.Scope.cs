using Avalonia.Threading;

namespace FileCat.App.ViewModels;

/// <summary>
/// A tab outside the panels (a Find window's results) with the window that shows it: commands run there act on the
/// tab, open their dialogs in that window, and report there.
/// </summary>
public sealed record CommandScope(TabViewModel Tab, IDialogService Dialogs, IViewActions View);

public sealed partial class MainViewModel
{
    /// <summary>A scope while its command runs; work the command started that runs later sees none.</summary>
    private sealed class ScopeHolder(CommandScope scope)
    {
        public CommandScope Scope { get; } = scope;
        public bool Ended { get; set; }
    }

    // Only the scoped command's own flow on the UI thread sees the scope: a job thread or a callback it scheduled
    // carries the async-local value along, so the holder says whether the command is still running.
    private readonly AsyncLocal<ScopeHolder?> _scope = new();

    private CommandScope? Scope => _scope.Value is { Ended: false } holder && Dispatcher.UIThread.CheckAccess() ? holder.Scope : null;

    /// <summary>
    /// Runs a command for a tab outside the panels as if it were the active panel's (plan §11: a Find window acts on
    /// its results with the panels' own commands). Only this command's flow sees the scope, so the main window goes on
    /// working meanwhile; failures are reported in the scope's window.
    /// </summary>
    public async Task ExecuteInAsync(CommandScope scope, string id)
    {
        var holder = new ScopeHolder(scope);
        _scope.Value = holder;
        try
        {
            await ExecuteAsync(id);
        }
        catch (Exception ex)
        {
            Core.Diagnostics.AppLog.Error($"Command {id} failed", ex);
            Notify($"{Services.Commands.Get(id)?.Title ?? id} failed: {ex.Message}", true);
        }
        finally
        {
            holder.Ended = true;
        }
    }

    /// <summary>Whether a command applies to the scope's tab now, and why not.</summary>
    public CommandAvailability GetAvailability(string id, CommandScope scope)
    {
        var previous = _scope.Value;
        var holder = new ScopeHolder(scope);
        _scope.Value = holder;
        try
        {
            return GetAvailability(id);
        }
        finally
        {
            holder.Ended = true;
            _scope.Value = previous;
        }
    }
}
