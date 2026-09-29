using FileCat.Core.Commands;

namespace FileCat.App.ViewModels;

/// <summary>
/// The main menu's commands (plan §4.4): the window builds its menus from this, and the command search shows where in
/// the menus each command is.
/// </summary>
public static class MainMenuModel
{
    /// <summary>A menu's own submenu (the Registry's commands, dimmed everywhere else, stay out of the File menu's way).</summary>
    public sealed record Submenu(string Header, string[] Items);

    public static readonly (string Header, object[] Items)[] Layout =
    [
        ("_File", [CommandIds.View, CommandIds.ViewAlternate, CommandIds.Edit, CommandIds.EditNew, CommandIds.HexEdit, CommandIds.EditSessions, "-", CommandIds.Copy, CommandIds.Duplicate,
            CommandIds.Move, CommandIds.Rename, CommandIds.MakeDirectory, CommandIds.Delete, CommandIds.DeletePermanent, "-",
            CommandIds.Pack, CommandIds.Unpack, CommandIds.TestArchive, "-",
            CommandIds.Checksum, CommandIds.VerifyChecksums, CommandIds.Attributes, CommandIds.CreateLink, CommandIds.BulkRename, CommandIds.ApplyCommand,
            CommandIds.AddToWorkingSet, CommandIds.RemoveFromSet,
            new Submenu("_Registry", [CommandIds.RegistryExport, CommandIds.RegistryImport, CommandIds.RegistrySaveData, CommandIds.RegistryLoadData,
                CommandIds.RegistryWritable, CommandIds.RegistryView]), "-",
            CommandIds.Undo, CommandIds.Properties, CommandIds.HiddenData, CommandIds.FileRecord, CommandIds.Reveal, CommandIds.OpenWithSystem, "-", CommandIds.Exit]),
        ("_Mark", [CommandIds.MarkToggleDown, CommandIds.MarkToggle, CommandIds.MarkSelectMask, CommandIds.MarkUnselectMask,
            CommandIds.MarkInvert, CommandIds.MarkInvertAll, CommandIds.MarkAll, CommandIds.MarkNone, "-", CommandIds.MarkSameExt,
            CommandIds.UnmarkSameExt, CommandIds.MarkSameName, CommandIds.UnmarkSameName, "-", CommandIds.MarkRestore, CommandIds.UnmarkHidden, "-",
            CommandIds.CopyNames, CommandIds.CopyPaths, CommandIds.CopyUncPaths]),
        ("_Navigate", [CommandIds.Parent, CommandIds.Enter, CommandIds.Root, CommandIds.Back, CommandIds.Forward, CommandIds.Home, "-",
            CommandIds.GoTo, CommandIds.LocationMenuSource, CommandIds.LocationMenuTarget, CommandIds.FindFolder, CommandIds.FolderHistory,
            CommandIds.FileHistory, CommandIds.Bookmarks, CommandIds.WorkingSets, "-", CommandIds.Refresh, CommandIds.ToggleHidden]),
        ("_Commands", [CommandIds.FindFiles, CommandIds.CompareDirectories, CommandIds.CompareFiles, CommandIds.FlatView, CommandIds.QuickFilter, "-",
            CommandIds.CommandLineFocus, CommandIds.InsertName, CommandIds.InsertPath, CommandIds.OpenTerminal, CommandIds.UserMenu, "-",
            CommandIds.CopyToClipboard, CommandIds.CutToClipboard, CommandIds.PasteFromClipboard, "-",
            CommandIds.ConnectNetworkDrive, CommandIds.DisconnectNetworkDrive, "-", CommandIds.SftpConnect, CommandIds.SftpDisconnect]),
        ("_Panels", [CommandIds.SwitchPanel, CommandIds.SwitchPanelBack, CommandIds.SwapPanels, CommandIds.OpenInTarget, CommandIds.TargetToSource,
            CommandIds.QuickView, "-", CommandIds.AddPanel, CommandIds.AddPanelBelow, CommandIds.ClosePanel, CommandIds.FocusPanelPicker,
            CommandIds.ChooseTarget, CommandIds.MaximizePanel,
            new Submenu("_Arrange", [CommandIds.PanelMoveLeft, CommandIds.PanelMoveRight, CommandIds.PanelMoveUp, CommandIds.PanelMoveDown, "-",
                CommandIds.PanelSwapPlaces, CommandIds.RotatePanels, CommandIds.EqualizePanels]), "-", CommandIds.NewTab, CommandIds.CloseTab, CommandIds.NextTab, CommandIds.PreviousTab, CommandIds.ReopenTab,
            CommandIds.TabList, CommandIds.DuplicateTab, CommandIds.CopyTabToTarget, CommandIds.LockTab, CommandIds.OpenInNewTab,
            CommandIds.OpenInNewTargetTab]),
        ("_View", [CommandIds.SortName, CommandIds.SortExtension, CommandIds.SortTime, CommandIds.SortSize, CommandIds.SortNone, "-",
            CommandIds.ColumnProfilePrefix + "0", CommandIds.ColumnProfilePrefix + "1", CommandIds.ColumnProfilePrefix + "2", "-",
            CommandIds.AnalyzeFolder, CommandIds.ColumnProfilePrefix + "3", CommandIds.ColumnProfilePrefix + "4", "-", CommandIds.ThemePick, CommandIds.ThemeCycle, "-",
            CommandIds.ToggleToolbar, CommandIds.ToggleDriveButtons]),
        ("_Tools", [CommandIds.FindDeleted, "-", CommandIds.Operations, CommandIds.Palette, CommandIds.Settings, "-", CommandIds.SaveWorkspace, CommandIds.LoadWorkspace, "-",
            CommandIds.DiagnosticsExport, CommandIds.HexRecovery]),
        ("_Help", [CommandIds.Help, CommandIds.CheckUpdates, CommandIds.About]),
    ];

    /// <summary>
    /// The toolbar's commands (D-49), grouped as Salamander groups its top toolbar: moving around, the panels, the
    /// clipboard, file operations, archives, marking, finding and comparing, and the view; "-" separates the groups.
    /// </summary>
    public static readonly string[] Toolbar =
    [
        CommandIds.Back, CommandIds.Forward, CommandIds.Parent, CommandIds.Root, CommandIds.Refresh, "-",
        CommandIds.LocationMenuSource, CommandIds.LocationMenuTarget, CommandIds.SwapPanels, CommandIds.NewTab, "-",
        CommandIds.CutToClipboard, CommandIds.CopyToClipboard, CommandIds.PasteFromClipboard, "-",
        CommandIds.Copy, CommandIds.Move, CommandIds.Rename, CommandIds.MakeDirectory, CommandIds.Delete, CommandIds.Properties, "-",
        CommandIds.Pack, CommandIds.Unpack, "-",
        CommandIds.MarkSelectMask, CommandIds.MarkUnselectMask, CommandIds.MarkInvert, CommandIds.MarkAll, "-",
        CommandIds.FindFiles, CommandIds.CompareDirectories, CommandIds.CompareFiles, CommandIds.QuickView, CommandIds.OpenTerminal, "-",
        CommandIds.ToggleHidden, CommandIds.Settings, CommandIds.Help,
    ];

    private static readonly Lazy<Dictionary<string, string>> s_paths = new(() =>
    {
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        void Walk(string path, IEnumerable<object> items)
        {
            foreach (var item in items)
            {
                if (item is Submenu sub) Walk(path + " › " + Plain(sub.Header), sub.Items);
                else if (item is string id && id != "-") paths.TryAdd(id, path);
            }
        }
        foreach (var (header, items) in Layout) Walk(Plain(header), items);
        return paths;
    });

    /// <summary>Where a command is in the menus ("Commands", "File › Registry"), or null when no menu has it.</summary>
    public static string? PathOf(string id) => s_paths.Value.GetValueOrDefault(id);

    /// <summary>A menu header without its access-key underscore ("_File" is File).</summary>
    private static string Plain(string header) => header.Replace("_", string.Empty, StringComparison.Ordinal);
}
