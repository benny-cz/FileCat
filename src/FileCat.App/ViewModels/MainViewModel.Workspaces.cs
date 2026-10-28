using FileCat.App.Services;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.State;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileCat.App.ViewModels;

/// <summary>
/// Named workspaces (plan §19.1, adapting Total Commander's tab sets): panel layout, tabs, locked tabs,
/// targets, and locations; never running jobs or credentials.
/// </summary>
public sealed partial class MainViewModel
{
    private const int MaxSwitchTabs = 512;
    private WorkspaceState? _previousWorkspace;
    public bool CanRestorePreviousWorkspace => _previousWorkspace is not null;

    private void SetPreviousWorkspace(WorkspaceState? state)
    {
        _previousWorkspace = state;
        OnPropertyChanged(nameof(CanRestorePreviousWorkspace));
    }

    private static int TabCount(WorkspaceState state) => state.Panels.Sum(panel => panel.Tabs.Count);

    private int LiveUnrestorableTabCount() => Workspace.Panels.Sum(panel => panel.Tabs.Count(tab => tab.ToState().Location is null));

    internal static int DropUnrestorableTabs(WorkspaceState state)
    {
        int dropped = 0;
        foreach (var panel in state.Panels)
        {
            int oldActive = panel.ActiveTab;
            int activeBefore = panel.Tabs.Take(Math.Clamp(oldActive, 0, panel.Tabs.Count)).Count(tab => tab.Location is not null);
            int before = panel.Tabs.Count;
            panel.Tabs.RemoveAll(tab => tab.Location is null);
            dropped += before - panel.Tabs.Count;
            panel.ActiveTab = Math.Clamp(activeBefore, 0, Math.Max(0, panel.Tabs.Count - 1));
        }
        return dropped;
    }

    private string WorkspacePath(string name) =>
        Path.Combine(Services.Paths.WorkspacesDirectory, name + ".json");

    internal static string? WorkspaceNameProblem(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
        if (name != name.Trim() || name.EndsWith('.')) return "Remove leading/trailing spaces and trailing dots.";
        if (name.Length > 100) return "Use at most 100 characters.";
        if (name.Any(c => c < ' ' || "<>:\"/\\|?*".Contains(c) || Path.GetInvalidFileNameChars().Contains(c)))
            return "The name cannot contain file-path separators or special filename characters.";
        if (Formatters.DescribeHiddenNameCharacters(name) is not null)
            return "Remove invisible or direction-control characters from the name.";
        string stem = name.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9')
            return "Choose a name other than a reserved device name.";
        return null;
    }

    private string SuggestedWorkspaceName()
    {
        for (int i = 1; i < int.MaxValue; i++)
        {
            string name = i == 1 ? "Project" : $"Project {i}";
            if (!File.Exists(WorkspacePath(name))) return name;
        }
        throw new IOException("No unused workspace name is available.");
    }

    private string ExistingWorkspaceName(string submitted) =>
        NamedWorkspaces().FirstOrDefault(name =>
            string.Equals(name, submitted, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) ?? submitted;

    private sealed record WorkspaceTarget(byte[] Contents, string Digest);

    private static WorkspaceTarget? ReadWorkspaceTarget(string path)
    {
        if (!File.Exists(path)) return null;
        if (OperatingSystem.IsWindows() && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The saved workspace is a link, not a regular file.");
        using var stream = OperatingSystem.IsWindows()
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : new FileStream(UnixFiles.OpenRegularFileForRead(path), FileAccess.Read);
        if (stream.Length > 8 * 1024 * 1024) throw new IOException("The saved workspace exceeds the supported size limit.");
        byte[] contents = new byte[(int)stream.Length];
        stream.ReadExactly(contents);
        if (stream.ReadByte() != -1) throw new IOException("The saved workspace changed while being read.");
        return new WorkspaceTarget(contents, Convert.ToHexString(SHA256.HashData(contents)));
    }

    private static bool SameWorkspaceTarget(string path, WorkspaceTarget expected)
    {
        try { return ReadWorkspaceTarget(path)?.Digest == expected.Digest; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public IReadOnlyList<string> NamedWorkspaces()
    {
        try
        {
            return Directory.EnumerateFiles(Services.Paths.WorkspacesDirectory, "*.json")
                .Select(Path.GetFileNameWithoutExtension).Where(n => n is not null).Cast<string>().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    internal async Task SaveNamedWorkspaceAsync()
    {
        string draft;
        try { draft = SuggestedWorkspaceName(); }
        catch (IOException ex) { Notify("Could not suggest a workspace name: " + ex.Message, true); return; }
        while (true)
        {
            var r = await Dialogs.PromptAsync(new PromptOptions("Save workspace as", "Name for the current panels, tabs, and targets:")
            {
                Text = draft,
                Validate = WorkspaceNameProblem,
                ConfirmText = "Save as",
                Hint = "Workspaces never store running operations or credentials",
            });
            if (r is null) return;
            draft = r.Text;
            if (WorkspaceNameProblem(draft) is { } problem) { Notify(problem, true); continue; }
            string path = WorkspacePath(draft);
            try
            {
                var before = await Task.Run(() => ReadWorkspaceTarget(path));
                var state = Workspace.ToState(LastPlacement);
                state.Name = draft;
                if (before is not null)
                {
                    string existingName = ExistingWorkspaceName(draft);
                    path = WorkspacePath(existingName);
                    state.Name = existingName;
                    string savedSize;
                    try
                    {
                        var saved = JsonSerializer.Deserialize(before.Contents, StateJsonContext.Default.WorkspaceState);
                        savedSize = saved is null ? "Saved arrangement cannot be previewed." :
                            $"Saved: {saved.Panels.Count} panels, {saved.Panels.Sum(p => p.Tabs.Count)} tabs.";
                    }
                    catch (JsonException) { savedSize = "Saved arrangement cannot be previewed."; }
                    string currentSize = $"Current: {state.Panels.Count} panels, {state.Panels.Sum(p => p.Tabs.Count)} tabs.";
                    if (!await Dialogs.ConfirmAsync("Replace saved workspace",
                        $"Replace \"{existingName}\"?\n{savedSize}\n{currentSize}\nThe previous version is kept as a backup.",
                        "Replace saved workspace", danger: true)) return;
                    if (!await Task.Run(() => SameWorkspaceTarget(path, before)))
                    {
                        Notify("The saved workspace changed while you were reviewing it. Review it again before replacing.", true);
                        continue;
                    }
                    await Task.Run(() => JsonFileStore.Save(path, state, StateJsonContext.Default.WorkspaceState,
                        () => SameWorkspaceTarget(path, before)));
                }
                else await Task.Run(() => JsonFileStore.SaveNew(path, state, StateJsonContext.Default.WorkspaceState));
                Notify($"Workspace \"{state.Name}\" saved.");
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Notify("Could not save the workspace: " + ex.Message, true);
            }
        }
    }

    internal async Task OpenNamedWorkspaceAsync(string? name = null)
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
        if (status is StateLoadStatus.CorruptUsingDefaults or StateLoadStatus.Missing or StateLoadStatus.OversizedReadOnly or StateLoadStatus.UnavailableReadOnly or StateLoadStatus.NewerSchemaReadOnly ||
            state.Panels is null || state.Panels.Any(panel => panel.Tabs is null))
        {
            Notify($"Workspace \"{name}\" could not be read.", true);
            return;
        }
        int unavailable = LiveUnrestorableTabCount();
        var previous = Workspace.ToState(LastPlacement);
        if (TabCount(previous) > MaxSwitchTabs || TabCount(state) > MaxSwitchTabs)
        {
            Notify($"Workspace switching supports at most {MaxSwitchTabs} tabs per arrangement. Close some tabs before switching.", true);
            return;
        }
        _ = DropUnrestorableTabs(previous);
        if (unavailable > 0 && !await Dialogs.ConfirmAsync("Open workspace",
            $"{unavailable} temporary {(unavailable == 1 ? "view cannot" : "views cannot")} be restored after this switch. Open \"{name}\" and keep the supported panels and tabs for restoration?",
            "Open workspace", danger: true)) return;
        try { Workspace.LoadState(state); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try { Workspace.LoadState(previous); } catch (Exception) { }
            Notify("Could not open the workspace: " + ex.Message, true);
            return;
        }
        SetPreviousWorkspace(previous);
        AppLog.Info("Opened a named workspace");
        Notify($"Workspace \"{name}\" opened. Restore the previous panels, tabs, layout and targets with Restore previous workspace.",
            actionCommandId: CommandIds.RestorePreviousWorkspace);
        View.FocusActivePanel();
    }

    internal async Task RestorePreviousWorkspaceAsync()
    {
        if (_previousWorkspace is not { } previous) return;
        int unavailable = LiveUnrestorableTabCount();
        var current = Workspace.ToState(LastPlacement);
        if (TabCount(current) > MaxSwitchTabs)
        {
            Notify($"Restore supports at most {MaxSwitchTabs} tabs in the current arrangement. Close some tabs and try again.", true);
            return;
        }
        _ = DropUnrestorableTabs(current);
        if (unavailable > 0 && !await Dialogs.ConfirmAsync("Restore previous workspace",
            $"{unavailable} temporary {(unavailable == 1 ? "view cannot" : "views cannot")} be restored after switching back. Restore the previous workspace and keep the supported current arrangement for later?",
            "Restore previous workspace", danger: true)) return;
        try { Workspace.LoadState(previous); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            try { Workspace.LoadState(current); } catch (Exception) { }
            Notify("Could not restore the previous workspace: " + ex.Message, true);
            return;
        }
        SetPreviousWorkspace(current);
        Notify("Previous workspace restored. The workspace you left can be restored again.",
            actionCommandId: CommandIds.RestorePreviousWorkspace);
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
            case CommandIds.RestorePreviousWorkspace:
                await RestorePreviousWorkspaceAsync();
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
                await AnalyzeFolderAsync();
                return true;
        }
        return false;
    }

    /// <summary>
    /// View → Analyze folder (plan §10, META-002): computes a column's value for every item and sorts by it. Sorted by
    /// such a column already, that one is analyzed; otherwise the columns to choose from, each with what it shows.
    /// </summary>
    private async Task AnalyzeFolderAsync()
    {
        if (ActiveTab is not { Location: not null } tab) return;
        if (tab.Listing.Sort is { Field: Core.Listing.SortField.Metadata, MetadataId: { } current } && current != Controls.ColumnSpec.FolderSortKey)
        {
            await tab.AnalyzeAsync(current);
            return;
        }
        var fields = Services.Metadata.Fields.Where(f => OperatingSystem.IsWindows() ? f.Id is not ("permissions" or "owner" or "group") : f.Id != "version")
            .OrderBy(f => f.Title, StringComparer.CurrentCulture).ToList();
        if (fields.Count == 0) return;
        var items = fields.Select(f => new ChoiceItem(f.Title, AnalysisDescription(f.Id))).ToList();
        var r = await Dialogs.ChooseAsync(new ChoiceOptions("Analyze folder", items)
        {
            Hint = "Reads the value for every item in this folder, then sorts by it (Esc stops it) · Enter starts",
        });
        if (r.Index < 0 || r.Index >= fields.Count) return;
        tab.SortByMetadata(fields[r.Index].Id, analyzing: true);
        await tab.AnalyzeAsync(fields[r.Index].Id);
    }

    private static string AnalysisDescription(string fieldId) => fieldId switch
    {
        "version" => "Program and library versions (.exe, .dll, and similar)",
        "dimensions" => "Picture sizes in pixels",
        "linkTarget" => "Where links and junctions point",
        "zone" => "Where files came from (downloaded from the internet, say)",
        "permissions" => "Who may read, write, and run each item",
        "owner" => "The user each item belongs to",
        "group" => "The group each item belongs to",
        "hidden" => OperatingSystem.IsWindows() ? "Alternate data streams each file carries, counted" : "Extended attributes each item carries, counted",
        "verified" => "Whether files match the checksums and signatures beside them (large ones wait for File → Verify)",
        _ => "A column's value for every item",
    };

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
        if (!AdmitConfiguredExternalTool(tool)) return;
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
