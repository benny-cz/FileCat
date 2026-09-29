using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.App.ViewModels;

/// <summary>
/// Named workspaces (plan §19.1, adapting Total Commander's tab sets): panel layout, tabs, locked tabs,
/// targets, and locations; never running jobs or credentials.
/// </summary>
public sealed partial class MainViewModel
{
    private string WorkspacePath(string name) =>
        Path.Combine(Services.Paths.WorkspacesDirectory, SanitizeName(name) + ".json");

    private static string SanitizeName(string name) =>
        new(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());

    public IReadOnlyList<string> NamedWorkspaces()
    {
        try
        {
            return Directory.EnumerateFiles(Services.Paths.WorkspacesDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension).Where(n => n is not null).Cast<string>().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (IOException) { return []; }
    }

    private async Task SaveNamedWorkspaceAsync()
    {
        var r = await Dialogs.PromptAsync(new PromptOptions("Save workspace", "Name for the current panels, tabs, and targets:")
        {
            Text = NamedWorkspaces().FirstOrDefault() ?? "Project",
            Validate = n => string.IsNullOrWhiteSpace(n) ? "Enter a name." : null,
            ConfirmText = "Save",
            Hint = "Workspaces never store running operations or credentials",
        });
        if (r is null) return;
        var state = Workspace.ToState(LastPlacement);
        state.Name = r.Text.Trim();
        try
        {
            JsonFileStore.Save(WorkspacePath(state.Name), state, StateJsonContext.Default.WorkspaceState);
            Notify($"Workspace \"{state.Name}\" saved.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify("Could not save the workspace: " + ex.Message, true);
        }
    }

    private async Task OpenNamedWorkspaceAsync(string? name = null)
    {
        var names = NamedWorkspaces();
        if (names.Count == 0)
        {
            Notify("No saved workspaces yet. Use Tools → Save workspace as… first.");
            return;
        }
        if (name is null)
        {
            var r = await Dialogs.ChooseAsync(new ChoiceOptions("Open workspace", names.Select(n => new ChoiceItem(n)).ToList())
            {
                Hint = "Enter replaces the current panels and tabs · Ctrl+Del deletes a saved workspace",
                AllowDelete = true,
            });
            foreach (var d in r.Deleted)
            {
                try { File.Delete(WorkspacePath(names[d])); }
                catch (IOException) { }
            }
            if (r.Index < 0) return;
            name = names[r.Index];
        }
        var state = JsonFileStore.Load(WorkspacePath(name), StateJsonContext.Default.WorkspaceState, WorkspaceState.CurrentSchema, () => new WorkspaceState(), out var status);
        if (status is StateLoadStatus.CorruptUsingDefaults or StateLoadStatus.Missing)
        {
            Notify($"Workspace \"{name}\" could not be read.", true);
            return;
        }
        foreach (var p in Workspace.Panels)
            foreach (var t in p.Tabs) Workspace.RememberClosed(t.ToState());
        Workspace.LoadState(state);
        AppLog.Info("Opened a named workspace");
        Notify($"Workspace \"{name}\" opened. The previous tabs can be reopened with Ctrl+Shift+T.");
        View.FocusActivePanel();
    }

    private async Task<bool> ExecuteWorkspaceCommandAsync(string id)
    {
        if (ExecuteLayoutCommand(id)) return true;
        switch (id)
        {
            case CommandIds.SaveWorkspace:
                await SaveNamedWorkspaceAsync();
                return true;
            case CommandIds.LoadWorkspace:
                await OpenNamedWorkspaceAsync();
                return true;
            case CommandIds.Settings:
                await Views.SettingsDialog.ShowAsync(this);
                return true;
            case CommandIds.DiagnosticsExport:
                await ExportDiagnosticsAsync();
                return true;
            case CommandIds.UserMenu:
                await ShowUserMenuAsync();
                return true;
            case CommandIds.AnalyzeFolder:
                if (ActiveTab is { } tab && tab.Listing.Sort is { Field: Core.Listing.SortField.Metadata, MetadataId: { } mid })
                    await tab.AnalyzeAsync(mid);
                else
                    Notify("Sort by a metadata column (Version, Dimensions, Origin, Link target) first; analysis then computes every value.");
                return true;
        }
        return false;
    }

    /// <summary>A previewable diagnostic bundle without secrets or file contents (plan §19.2).</summary>
    private async Task ExportDiagnosticsAsync()
    {
        var summary = DiagnosticsBundle.Describe(Services);
        if (!await Dialogs.ConfirmAsync("Export diagnostics", summary.Preview + "\n\nThe bundle is written to your Desktop. Nothing is sent anywhere.", "Export"))
            return;
        try
        {
            var path = await Task.Run(() => DiagnosticsBundle.Write(Services));
            Notify("Diagnostics written to " + path);
            Services.Shell.Reveal(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notify("Could not write diagnostics: " + ex.Message, true);
        }
    }

    /// <summary>User commands menu (F9): configured tools with structured arguments (plan §14.2).</summary>
    private async Task ShowUserMenuAsync(IReadOnlyList<ToolDefinition>? level = null)
    {
        var tools = level ?? Services.Settings.UserCommands;
        if (tools.Count == 0)
        {
            Notify("No user commands are configured yet. Add them in Settings → Tools.");
            return;
        }
        var items = tools.Select(t => new ChoiceItem(t.Children is { Count: > 0 } ? t.Name + " ▸" : t.Name,
            t.Children is { Count: > 0 } ? null : t.Executable + " " + string.Join(" ", t.Arguments), t.Hotkey)).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("User commands", items) { Hint = "Enter runs the command with the focused or marked items" });
        if (r.Index < 0) return;
        var tool = tools[r.Index];
        if (tool.Children is { Count: > 0 } children)
        {
            await ShowUserMenuAsync(children);
            return;
        }
        var tab = ActiveTab;
        var files = tab?.Listing.GetSelection().Select(s => s.FileSystemPath).Where(p => p is not null).Cast<string>().ToList() ?? [];
        var dir = tab?.Location is { IsFileSystem: true } l ? l.Path : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? prompt = null;
        if (tool.Arguments.Any(a => a.Contains("{prompt}")))
        {
            var p = await Dialogs.PromptAsync(new PromptOptions(tool.Name, "Value for {prompt}:"));
            if (p is null) return;
            prompt = p.Text;
        }
        try
        {
            var result = Core.Tools.ToolLauncher.Launch(tool, new Core.Tools.ToolContext(files, dir, ActiveTarget()?.Path, prompt), Services.Paths.TempDirectory);
            if (result.Warning is not null) Notify(result.Warning);
        }
        catch (Core.Tools.ToolLaunchException ex)
        {
            Notify(ex.Message, true);
        }
    }
}
