namespace FileCat.Core.Commands;

/// <summary>Where a binding is active. Resolution walks from the most specific context outwards.</summary>
public enum CommandContext
{
    Global,
    Panel,
    QuickSearch,
    CommandLine,
    Viewer,
}

/// <summary>
/// One stable command intent (plan §4.4). Menus, toolbar, palette, key bar, and shortcuts all dispatch
/// the same id. Ids are persistent and never localized.
/// </summary>
public sealed record CommandDefinition(string Id, string Title, string Category)
{
    public string[] DefaultGestures { get; init; } = [];
    public CommandContext Context { get; init; } = CommandContext.Panel;
    /// <summary>Short label for the function-key bar.</summary>
    public string? KeyBarLabel { get; init; }
    public string? Description { get; init; }
    /// <summary>Words people search for that the title does not use ("recovery", "undelete"): the command search finds them.</summary>
    public string[] Keywords { get; init; } = [];
}

public static class CommandIds
{
    public const string View = "file.view";
    public const string ViewAlternate = "file.viewAlternate";
    public const string Edit = "file.edit";
    public const string EditNew = "file.editNew";
    public const string Copy = "file.copy";
    public const string Duplicate = "file.duplicate";
    public const string Move = "file.move";
    public const string Rename = "file.rename";
    public const string MakeDirectory = "file.mkdir";
    public const string Delete = "file.delete";
    public const string DeletePermanent = "file.deletePermanent";
    public const string Open = "file.open";
    public const string OpenWithSystem = "file.openWithSystem";
    public const string Properties = "file.properties";
    public const string ContextMenu = "file.contextMenu";
    public const string Reveal = "file.reveal";
    public const string Checksum = "file.checksum";
    public const string VerifyChecksums = "file.verifyChecksums";
    public const string Pack = "file.pack";
    public const string Unpack = "file.unpack";
    public const string TestArchive = "file.testarchive";
    public const string EditSessions = "file.editsessions";
    public const string Undo = "file.undo";
    public const string Attributes = "file.attributes";
    public const string CreateLink = "file.createLink";
    public const string BulkRename = "file.bulkRename";
    public const string ApplyCommand = "file.applyCommand";
    public const string RemoveFromSet = "file.removeFromSet";
    public const string AddToWorkingSet = "file.addToWorkingSet";
    public const string WorkingSets = "navigate.workingSets";

    public const string MarkToggleDown = "mark.toggleDown";
    public const string MarkToggle = "mark.toggle";
    public const string MarkSelectMask = "mark.select";
    public const string MarkUnselectMask = "mark.unselect";
    public const string MarkInvert = "mark.invert";
    public const string MarkInvertAll = "mark.invertAll";
    public const string MarkAll = "mark.all";
    public const string MarkAllComplete = "mark.allComplete";
    public const string MarkNone = "mark.none";
    public const string MarkSameExt = "mark.sameExt";
    public const string UnmarkSameExt = "mark.sameExtUnselect";
    public const string MarkSameName = "mark.sameName";
    public const string UnmarkSameName = "mark.sameNameUnselect";
    public const string MarkRestore = "mark.restore";
    public const string UnmarkHidden = "mark.unmarkHidden";

    public const string Parent = "nav.parent";
    public const string Enter = "nav.enter";
    public const string Root = "nav.root";
    public const string Back = "nav.back";
    public const string Forward = "nav.forward";
    public const string GoTo = "nav.goto";
    public const string Refresh = "nav.refresh";
    public const string LocationMenuLeft = "nav.locationLeft";
    public const string LocationMenuRight = "nav.locationRight";
    public const string FindFolder = "nav.findFolder";
    public const string FileHistory = "nav.fileHistory";
    public const string FolderHistory = "nav.folderHistory";
    public const string Bookmarks = "nav.bookmarks";
    public const string BookmarkSetPrefix = "nav.bookmarkSet";
    public const string BookmarkGoPrefix = "nav.bookmarkGo";
    /// <summary>Distinct from <see cref="BookmarkGoPrefix"/>, which would prefix-match a longer id.</summary>
    public const string BookmarkTargetPrefix = "nav.targetBookmark";
    public const string ToggleHidden = "nav.toggleHidden";
    public const string Home = "nav.home";

    public const string SwitchPanel = "panel.switch";
    public const string SwitchPanelBack = "panel.switchBack";
    public const string SwapPanels = "panel.swap";
    public const string OpenInTarget = "panel.openInTarget";
    public const string TargetToSource = "panel.targetToSource";
    public const string QuickView = "panel.quickView";
    public const string AddPanel = "panel.add";
    public const string ClosePanel = "panel.close";
    public const string FocusPanelPicker = "panel.focusPicker";
    public const string ChooseTarget = "panel.chooseTarget";
    public const string MaximizePanel = "panel.maximize";
    public const string PanelMoveLeft = "panel.moveLeft";
    public const string PanelMoveRight = "panel.moveRight";
    public const string PanelMoveUp = "panel.moveUp";
    public const string PanelMoveDown = "panel.moveDown";
    public const string PanelSwapPlaces = "panel.swapPlaces";
    public const string AddPanelBelow = "panel.addBelow";
    public const string EqualizePanels = "panel.equalize";
    public const string RotatePanels = "panel.rotate";

    public const string NewTab = "tab.new";
    public const string CloseTab = "tab.close";
    public const string NextTab = "tab.next";
    public const string PreviousTab = "tab.prev";
    public const string ReopenTab = "tab.reopen";
    public const string TabList = "tab.list";
    public const string DuplicateTab = "tab.duplicate";
    public const string CopyTabToTarget = "tab.copyToTarget";
    public const string LockTab = "tab.lock";
    public const string OpenInNewTab = "tab.openFolderInNewTab";
    public const string OpenInNewTargetTab = "tab.openFolderInNewTargetTab";

    public const string SortName = "sort.name";
    public const string SortExtension = "sort.ext";
    public const string SortTime = "sort.time";
    public const string SortSize = "sort.size";
    public const string SortNone = "sort.none";
    public const string ColumnProfilePrefix = "view.columns";
    public const string QuickFilter = "view.filter";
    public const string FlatView = "view.flat";
    public const string AnalyzeFolder = "view.analyze";

    public const string FindFiles = "search.find";
    public const string RegistryExport = "registry.export";
    public const string RegistryImport = "registry.import";
    public const string RegistryWritable = "registry.writable";
    public const string RegistryView = "registry.view";
    public const string RegistrySaveData = "registry.savedata";
    public const string RegistryLoadData = "registry.loaddata";
    public const string HexEdit = "hex.edit";
    public const string HexRecovery = "hex.recovery";
    public const string CompareDirectories = "compare.dirs";
    public const string CompareFiles = "compare.files";
    /// <summary>Deleted items of a disk image, read-only (P10).</summary>
    public const string FindDeleted = "tools.findDeleted";
    public const string CommandLineFocus = "cmdline.focus";
    public const string CommandHistory = "cmdline.history";
    public const string InsertName = "cmdline.insertName";
    public const string InsertPath = "cmdline.insertPath";
    public const string OpenTerminal = "terminal.open";

    public const string CopyToClipboard = "clipboard.copy";
    public const string CutToClipboard = "clipboard.cut";
    public const string PasteFromClipboard = "clipboard.paste";
    public const string CopyNames = "clipboard.copyNames";
    public const string CopyPaths = "clipboard.copyPaths";
    public const string CopyUncPaths = "clipboard.copyUnc";

    public const string ConnectNetworkDrive = "net.connect";
    public const string DisconnectNetworkDrive = "net.disconnect";
    public const string SftpConnect = "net.sftpConnect";
    public const string SftpDisconnect = "net.sftpDisconnect";

    public const string Palette = "app.palette";
    public const string Operations = "app.operations";
    public const string UserMenu = "app.userMenu";
    public const string Menu = "app.menu";
    public const string Settings = "app.settings";
    public const string Help = "app.help";
    public const string Exit = "app.exit";
    public const string About = "app.about";
    public const string CheckUpdates = "app.checkUpdates";
    public const string ClearHistory = "app.clearHistory";
    public const string ViewerWindows = "app.viewerWindows";
    public const string SaveWorkspace = "app.saveWorkspace";
    public const string LoadWorkspace = "app.loadWorkspace";
    public const string DiagnosticsExport = "app.diagnostics";
    public const string ThemeCycle = "app.themeCycle";
    public const string ThemePick = "app.themePick";
}

public sealed class CommandRegistry
{
    private readonly Dictionary<string, CommandDefinition> _byId = new(StringComparer.Ordinal);
    private readonly List<CommandDefinition> _all = [];

    public IReadOnlyList<CommandDefinition> All => _all;

    public CommandDefinition? Get(string id) => _byId.GetValueOrDefault(id);

    public void Add(CommandDefinition definition)
    {
        if (!_byId.TryAdd(definition.Id, definition)) throw new InvalidOperationException($"Duplicate command id {definition.Id}.");
        _all.Add(definition);
    }

    /// <summary>Gives a command its one-line description (the command search and the keyboard reference show it).</summary>
    public void Describe(string id, string description)
    {
        if (!_byId.TryGetValue(id, out var definition)) return;
        var updated = definition with { Description = description };
        _byId[id] = updated;
        _all[_all.IndexOf(definition)] = updated;
    }

    /// <summary>Adds search words to a command (plan §4.4); unknown ids are ignored.</summary>
    public void AddKeywords(string id, params string[] keywords)
    {
        if (!_byId.TryGetValue(id, out var definition)) return;
        var updated = definition with { Keywords = [.. definition.Keywords, .. keywords] };
        _byId[id] = updated;
        _all[_all.IndexOf(definition)] = updated;
    }

    /// <summary>
    /// Default commands and bindings: agreement-first across Salamander, Total Commander, and FAR,
    /// with the conflicts resolved as recorded in ADR-16 (plan §4.5). Ctrl+Alt+letter chords are avoided
    /// because they are AltGr characters on many European layouts.
    /// </summary>
    /// <param name="translations">
    /// Optional translated titles by command id, and key-bar labels by "id#bar" (plan D-25: English first, localizable
    /// without code changes). Ids and gestures are never translated.
    /// </param>
    public static CommandRegistry CreateDefault(IReadOnlyDictionary<string, string>? translations = null)
    {
        var r = new CommandRegistry();
        void Add(string id, string title, string category, string? keyBar = null, CommandContext ctx = CommandContext.Panel, params string[] gestures)
        {
            if (translations is not null)
            {
                if (translations.TryGetValue(id, out var t) && !string.IsNullOrWhiteSpace(t)) title = t;
                if (translations.TryGetValue(id + "#bar", out var b) && !string.IsNullOrWhiteSpace(b)) keyBar = b;
            }
            r.Add(new CommandDefinition(id, title, category) { DefaultGestures = gestures, KeyBarLabel = keyBar, Context = ctx });
        }

        const string F = "File", M = "Mark", N = "Navigate", P = "Panels", T = "Tabs", V = "View", C = "Commands", A = "Application";

        Add(CommandIds.View, "View", F, "View", CommandContext.Panel, "F3");
        Add(CommandIds.ViewAlternate, "View as hex / alternate viewer", F, "Hex", CommandContext.Panel, "Alt+F3");
        Add(CommandIds.Edit, "Edit", F, "Edit", CommandContext.Panel, "F4");
        Add(CommandIds.EditNew, "Edit new file…", F, "New file", CommandContext.Panel, "Shift+F4");
        Add(CommandIds.Copy, "Copy…", F, "Copy", CommandContext.Panel, "F5");
        Add(CommandIds.Duplicate, "Duplicate here…", F, "Duplicate", CommandContext.Panel, "Shift+F5");
        Add(CommandIds.Move, "Move / rename…", F, "Move", CommandContext.Panel, "F6");
        Add(CommandIds.Rename, "Rename in place", F, "Rename", CommandContext.Panel, "F2", "Shift+F6");
        Add(CommandIds.MakeDirectory, "Create folder…", F, "MkDir", CommandContext.Panel, "F7");
        Add(CommandIds.Delete, "Delete…", F, "Delete", CommandContext.Panel, "F8", "Delete");
        Add(CommandIds.DeletePermanent, "Delete permanently…", F, "Delete!", CommandContext.Panel, "Shift+F8", "Shift+Delete");
        Add(CommandIds.Open, "Open", F, null, CommandContext.Panel, "Enter");
        Add(CommandIds.OpenWithSystem, "Open with system application", F, null, CommandContext.Panel, "Shift+Enter");
        Add(CommandIds.Properties, "Properties", F, null, CommandContext.Panel, "Alt+Enter");
        Add(CommandIds.ContextMenu, "Context menu", F, null, CommandContext.Panel, "Shift+F10", "Apps");
        Add(CommandIds.Reveal, OperatingSystem.IsWindows() ? "Reveal in Explorer" : "Reveal in file manager", F, "Reveal", CommandContext.Panel, "Shift+F3");
        Add(CommandIds.Checksum, "Calculate checksums…", F);
        Add(CommandIds.VerifyChecksums, "Verify checksum manifest…", F);
        Add(CommandIds.Pack, "Pack into ZIP…", F, "Pack", CommandContext.Panel, "Alt+F5");
        Add(CommandIds.Unpack, "Unpack…", F, "Unpack", CommandContext.Panel, "Alt+F6", "Alt+F9");
        Add(CommandIds.TestArchive, "Test archive integrity", F);
        Add(CommandIds.EditSessions, "Edit sessions…", F);
        Add(CommandIds.Undo, "Undo last operation…", F, null, CommandContext.Panel, "Ctrl+Z");
        Add(CommandIds.Attributes, "Change attributes and times…", F);
        Add(CommandIds.CreateLink, "Create link…", F);
        Add(CommandIds.BulkRename, "Bulk rename…", F, null, CommandContext.Panel, "Ctrl+M");
        Add(CommandIds.ApplyCommand, "Run a command for each item…", F, null, CommandContext.Panel, "Ctrl+G");
        Add(CommandIds.AddToWorkingSet, "Add to working set…", F, null, CommandContext.Panel, "Ctrl+Shift+W");
        Add(CommandIds.RemoveFromSet, "Remove from set (keeps the items)", F, null, CommandContext.Panel, "Ctrl+Delete");

        Add(CommandIds.MarkToggleDown, "Mark and move down", M, null, CommandContext.Panel, "Insert");
        Add(CommandIds.MarkToggle, "Mark (sizes a folder)", M, null, CommandContext.Panel, "Space");
        Add(CommandIds.MarkSelectMask, "Select by mask…", M, null, CommandContext.Panel, "Num+");
        Add(CommandIds.MarkUnselectMask, "Unselect by mask…", M, null, CommandContext.Panel, "Num-");
        Add(CommandIds.MarkInvert, "Invert selection (files)", M, null, CommandContext.Panel, "Num*");
        Add(CommandIds.MarkInvertAll, "Invert selection (files and folders)", M, null, CommandContext.Panel, "Shift+Num*");
        Add(CommandIds.MarkAll, "Select all", M, null, CommandContext.Panel, "Ctrl+A", "Ctrl+Num+");
        Add(CommandIds.MarkAllComplete, "Select all once the listing is complete", M, null, CommandContext.Panel, "Ctrl+Shift+A");
        Add(CommandIds.MarkNone, "Unselect all", M, null, CommandContext.Panel, "Ctrl+Num-");
        Add(CommandIds.MarkSameExt, "Select same extension", M, null, CommandContext.Panel, "Shift+Num+");
        Add(CommandIds.UnmarkSameExt, "Unselect same extension", M, null, CommandContext.Panel, "Shift+Num-");
        Add(CommandIds.MarkSameName, "Select same name", M, null, CommandContext.Panel, "Alt+Num+");
        Add(CommandIds.UnmarkSameName, "Unselect same name", M, null, CommandContext.Panel, "Alt+Num-");
        Add(CommandIds.MarkRestore, "Restore previous selection", M, null, CommandContext.Panel, "Num/");
        Add(CommandIds.UnmarkHidden, "Unmark items hidden by the filter", M);

        Add(CommandIds.Parent, "Go to parent", N, null, CommandContext.Panel, "Ctrl+PgUp", "Backspace");
        Add(CommandIds.Enter, "Enter folder or archive", N, null, CommandContext.Panel, "Ctrl+PgDn");
        Add(CommandIds.Root, "Go to root", N, null, CommandContext.Panel, "Ctrl+\\");
        Add(CommandIds.Back, "Back", N, null, CommandContext.Panel, "Alt+Left");
        Add(CommandIds.Forward, "Forward", N, null, CommandContext.Panel, "Alt+Right");
        Add(CommandIds.GoTo, "Go to path…", N, "Go to", CommandContext.Panel, "Shift+F7", "Ctrl+L");
        Add(CommandIds.Refresh, "Reread", N, null, CommandContext.Panel, "Ctrl+R");
        Add(CommandIds.LocationMenuLeft, "Change left (or active) location…", N, "Left", CommandContext.Panel, "Alt+F1");
        Add(CommandIds.LocationMenuRight, "Change right (or target) location…", N, "Right", CommandContext.Panel, "Alt+F2");
        Add(CommandIds.FindFolder, "Find folder…", N, "Find dir", CommandContext.Panel, "Alt+F10");
        Add(CommandIds.FileHistory, "File history…", N, "File hist", CommandContext.Panel, "Alt+F11");
        Add(CommandIds.FolderHistory, "Folder history…", N, "Dir hist", CommandContext.Panel, "Alt+F12");
        Add(CommandIds.Bookmarks, "Bookmarks…", N, null, CommandContext.Panel, "Ctrl+D");
        for (int i = 0; i <= 9; i++)
        {
            Add(CommandIds.BookmarkSetPrefix + i, $"Set bookmark {i} to this location", N, null, CommandContext.Panel, $"Ctrl+Shift+{i}");
            Add(CommandIds.BookmarkGoPrefix + i, $"Go to bookmark {i}", N, null, CommandContext.Panel, $"Ctrl+{i}");
            Add(CommandIds.BookmarkTargetPrefix + i, $"Open bookmark {i} in the target panel", N, null, CommandContext.Panel, $"Alt+Shift+{i}");
        }
        Add(CommandIds.ToggleHidden, "Show hidden and system items", N, null, CommandContext.Panel, "Ctrl+H");
        Add(CommandIds.Home, "Go to home folder", N);
        Add(CommandIds.WorkingSets, "Working sets", N);

        Add(CommandIds.SwitchPanel, "Switch to target panel", P, null, CommandContext.Panel, "Tab");
        Add(CommandIds.SwitchPanelBack, "Switch to previous panel", P, null, CommandContext.Panel, "Shift+Tab");
        Add(CommandIds.SwapPanels, "Swap source and target locations", P, null, CommandContext.Panel, "Ctrl+U");
        Add(CommandIds.OpenInTarget, "Open focused folder in target panel", P, null, CommandContext.Panel,
            "Ctrl+Left", "Ctrl+Right", "Ctrl+Shift+Left", "Ctrl+Shift+Right");
        Add(CommandIds.TargetToSource, "Set target panel to this location", P, null, CommandContext.Panel, "Ctrl+Shift+Down");
        Add(CommandIds.QuickView, "Quick view in target panel", P, null, CommandContext.Panel, "Ctrl+Q");
        Add(CommandIds.AddPanel, "Add panel to the right", P);
        Add(CommandIds.AddPanelBelow, "Add panel below", P);
        Add(CommandIds.ClosePanel, "Close panel", P);
        Add(CommandIds.FocusPanelPicker, "Focus panel…", P, "Panels", CommandContext.Panel, "F12");
        Add(CommandIds.ChooseTarget, "Choose target panel…", P, "Target", CommandContext.Panel, "Shift+F12");
        Add(CommandIds.MaximizePanel, "Maximize or restore panel", P, "Maximize", CommandContext.Panel, "F11");
        // Arranging panels (ADR-18): the keyboard's way to do what dragging a panel's number does.
        Add(CommandIds.PanelMoveLeft, "Move panel left", P, null, CommandContext.Panel, "Alt+Shift+Left");
        Add(CommandIds.PanelMoveRight, "Move panel right", P, null, CommandContext.Panel, "Alt+Shift+Right");
        Add(CommandIds.PanelMoveUp, "Move panel up", P, null, CommandContext.Panel, "Alt+Shift+Up");
        Add(CommandIds.PanelMoveDown, "Move panel down", P, null, CommandContext.Panel, "Alt+Shift+Down");
        Add(CommandIds.PanelSwapPlaces, "Swap places with the target panel", P);
        Add(CommandIds.RotatePanels, "Turn panels side by side or stacked", P);
        Add(CommandIds.EqualizePanels, "Equalize panel sizes", P);

        Add(CommandIds.NewTab, "New tab", T, null, CommandContext.Panel, "Ctrl+T");
        Add(CommandIds.CloseTab, "Close tab", T, null, CommandContext.Panel, "Ctrl+W");
        Add(CommandIds.NextTab, "Next tab", T, null, CommandContext.Panel, "Ctrl+Tab");
        Add(CommandIds.PreviousTab, "Previous tab", T, null, CommandContext.Panel, "Ctrl+Shift+Tab");
        Add(CommandIds.ReopenTab, "Reopen closed tab", T, null, CommandContext.Panel, "Ctrl+Shift+T");
        Add(CommandIds.TabList, "Tab list…", T, null, CommandContext.Panel, "Ctrl+Shift+L");
        Add(CommandIds.DuplicateTab, "Duplicate tab", T);
        Add(CommandIds.CopyTabToTarget, "Copy tab to target panel", T);
        Add(CommandIds.LockTab, "Lock tab", T);
        Add(CommandIds.OpenInNewTab, "Open focused folder in new tab", T, null, CommandContext.Panel, "Ctrl+Up");
        Add(CommandIds.OpenInNewTargetTab, "Open focused folder in new target tab", T, null, CommandContext.Panel, "Ctrl+Shift+Up");

        Add(CommandIds.SortName, "Sort by name", V, "Name", CommandContext.Panel, "Ctrl+F3");
        Add(CommandIds.SortExtension, "Sort by extension", V, "Ext", CommandContext.Panel, "Ctrl+F4");
        Add(CommandIds.SortTime, "Sort by time", V, "Time", CommandContext.Panel, "Ctrl+F5");
        Add(CommandIds.SortSize, "Sort by size", V, "Size", CommandContext.Panel, "Ctrl+F6");
        Add(CommandIds.SortNone, "Unsorted", V, "Unsorted", CommandContext.Panel, "Ctrl+F7");
        for (int i = 0; i <= 9; i++)
            Add(CommandIds.ColumnProfilePrefix + i, $"Column profile {i}", V, null, CommandContext.Panel, $"Alt+{i}");
        Add(CommandIds.QuickFilter, "Quick filter…", V, null, CommandContext.Panel, "Ctrl+S");
        Add(CommandIds.FlatView, "Flat view (all files below this folder)", V, null, CommandContext.Panel, "Ctrl+B");
        Add(CommandIds.AnalyzeFolder, "Analyze folder (complete metadata sort)", V);

        Add(CommandIds.FindFiles, "Find files…", C, "Find", CommandContext.Panel, "Alt+F7");
        Add(CommandIds.RegistryExport, "Export Registry selection to .reg…", F);
        Add(CommandIds.RegistryImport, "Import .reg into selected Registry scope…", F);
        Add(CommandIds.RegistryWritable, "Open writable Registry location…", F);
        Add(CommandIds.RegistryView, "Switch Registry view (default, 64-bit, 32-bit)…", F);
        Add(CommandIds.RegistrySaveData, "Save Registry value data to a file…", F);
        Add(CommandIds.RegistryLoadData, "Load Registry value data from a file…", F);
        Add(CommandIds.HexEdit, "Edit file bytes in hex…", F);
        Add(CommandIds.HexRecovery, "Recover interrupted hex save…", F, null, CommandContext.Global);
        Add(CommandIds.CompareDirectories, "Compare directories…", C, "Compare", CommandContext.Panel, "Ctrl+F10");
        Add(CommandIds.CompareFiles, "Compare files…", C, null, CommandContext.Panel, "Ctrl+I");
        Add(CommandIds.FindDeleted, "Recover deleted files…", C);
        Add(CommandIds.CommandLineFocus, "Focus command line", C, null, CommandContext.Panel, "Ctrl+E");
        Add(CommandIds.CommandHistory, "Command history…", C, null, CommandContext.Panel, "Alt+F8");
        Add(CommandIds.InsertName, "Insert focused name into command line", C, null, CommandContext.Panel, "Ctrl+Enter");
        Add(CommandIds.InsertPath, "Insert focused path into command line", C, null, CommandContext.Panel, "Ctrl+Shift+Enter");
        Add(CommandIds.OpenTerminal, "Open terminal here", C, null, CommandContext.Panel, "Ctrl+`");
        Add(CommandIds.CopyToClipboard, "Copy items to clipboard", C, null, CommandContext.Panel, "Ctrl+C");
        Add(CommandIds.CutToClipboard, "Cut items to clipboard", C, null, CommandContext.Panel, "Ctrl+X");
        Add(CommandIds.PasteFromClipboard, "Paste items or go to pasted path", C, null, CommandContext.Panel, "Ctrl+V");
        Add(CommandIds.CopyNames, "Copy names", C, null, CommandContext.Panel, "Ctrl+Shift+N");
        Add(CommandIds.CopyPaths, "Copy full paths", C, null, CommandContext.Panel, "Ctrl+Shift+C");
        Add(CommandIds.CopyUncPaths, "Copy UNC paths", C, null, CommandContext.Panel, "Ctrl+Shift+U");
        Add(CommandIds.ConnectNetworkDrive, "Connect network drive…", C);
        Add(CommandIds.DisconnectNetworkDrive, "Disconnect network drive…", C);
        Add(CommandIds.SftpConnect, "Connect to a server (SFTP, FTPS, FTP)…", C);
        Add(CommandIds.SftpDisconnect, "Disconnect from the server", C);

        Add(CommandIds.Palette, "Search commands…", A, null, CommandContext.Global, "Ctrl+Shift+P");
        Add(CommandIds.Operations, "Operations", A, null, CommandContext.Global, "Ctrl+J");
        Add(CommandIds.UserMenu, "User commands…", A, "User", CommandContext.Panel, "F9");
        Add(CommandIds.Menu, "Menu bar", A, "Menu", CommandContext.Global, "F10");
        Add(CommandIds.Settings, "Settings…", A, null, CommandContext.Global, "Ctrl+Comma");
        Add(CommandIds.Help, "Keyboard reference", A, "Help", CommandContext.Global, "F1");
        Add(CommandIds.Exit, "Exit", A, "Quit", CommandContext.Global, "Alt+F4");
        Add(CommandIds.About, "About FileCat", A);
        Add(CommandIds.CheckUpdates, "Check for updates…", A);
        Add(CommandIds.ClearHistory, "Clear history…", A);
        Add(CommandIds.ViewerWindows, "Viewer and editor windows…", A, null, CommandContext.Global);
        Add(CommandIds.SaveWorkspace, "Save workspace as…", A);
        Add(CommandIds.LoadWorkspace, "Open workspace…", A);
        Add(CommandIds.DiagnosticsExport, "Export diagnostics…", A);
        Add(CommandIds.ThemePick, "Theme…", A);
        Add(CommandIds.ThemeCycle, "Next theme", A);
        AddSearchWords(r);
        return r;
    }

    /// <summary>
    /// The words people type for commands whose titles say it differently (plan §4.4, UX-010): the command search
    /// finds "Recover deleted files" for "undelete", "unerase", or "restore deleted".
    /// </summary>
    private static void AddSearchWords(CommandRegistry r)
    {
        void K(string id, params string[] words) => r.AddKeywords(id, words);
        // What a title cannot say: which items a command takes, and where its result goes.
        r.Describe(CommandIds.CompareFiles, "Two marked files, one marked in each panel, or the file under the cursor and the file of the same name in the other panel.");
        r.Describe(CommandIds.FindDeleted, "Choose a drive or a disk image: its deleted files open in a new tab, to copy (F5) to another drive.");
        r.Describe(CommandIds.ChooseTarget, "Where F5 and F6 copy and move to, with three or more panels; “Set as target” on a panel's header does the same.");
        r.Describe(CommandIds.CompareDirectories, "Marks what differs between the two panels' folders; Include subfolders lists every difference below and can synchronize.");
        K(CommandIds.FindDeleted, "recover", "recovery", "undelete", "unerase", "restore deleted", "lost files", "deleted files", "find deleted files", "disk image", "scan drive");
        K(CommandIds.FindFiles, "search", "locate", "look for", "grep", "find in files");
        K(CommandIds.Settings, "options", "preferences", "configuration", "configure", "setup");
        K(CommandIds.BulkRename, "multi rename", "mass rename", "batch rename", "renamer");
        K(CommandIds.CompareDirectories, "diff", "differences", "synchronize", "sync", "compare folders");
        K(CommandIds.CompareFiles, "diff", "differences", "compare two files", "same name", "text compare", "binary compare");
        K(CommandIds.Checksum, "hash", "sha256", "sha1", "md5", "crc");
        K(CommandIds.VerifyChecksums, "verify", "hash", "sfv", "md5sum", "sha256sums");
        K(CommandIds.Pack, "zip", "compress", "create archive");
        K(CommandIds.Unpack, "extract", "unzip", "decompress", "unpack archive");
        K(CommandIds.TestArchive, "verify archive", "integrity", "check archive");
        K(CommandIds.Attributes, "read-only", "hidden", "timestamps", "dates", "touch", "modified time");
        K(CommandIds.CreateLink, "symlink", "symbolic link", "junction", "hard link", "shortcut");
        K(CommandIds.ApplyCommand, "run for each", "batch", "execute for each");
        K(CommandIds.HexEdit, "binary", "bytes", "patch");
        K(CommandIds.ViewAlternate, "hex view");
        K(CommandIds.View, "preview", "lister", "read");
        K(CommandIds.Edit, "editor", "notepad");
        K(CommandIds.Delete, "remove", "erase", "trash", "recycle bin");
        K(CommandIds.DeletePermanent, "remove permanently", "erase permanently", "wipe");
        K(CommandIds.MakeDirectory, "new folder", "mkdir", "create folder", "new directory");
        K(CommandIds.EditNew, "new file", "create file", "touch");
        K(CommandIds.Move, "relocate", "rename");
        K(CommandIds.Rename, "rename file");
        K(CommandIds.Properties, "info", "details", "file properties");
        K(CommandIds.Reveal, "explorer", "show in folder", "finder", "file manager");
        K(CommandIds.OpenWithSystem, "open with", "default program", "associated program");
        K(CommandIds.OpenTerminal, "shell", "console", "cmd", "powershell", "command prompt", "bash");
        K(CommandIds.QuickFilter, "filter", "narrow");
        K(CommandIds.FlatView, "branch view", "all files", "recursive listing", "flatten");
        K(CommandIds.ToggleHidden, "hidden files", "show hidden", "dotfiles", "system files");
        K(CommandIds.ThemePick, "appearance", "colors", "colours", "dark mode", "light mode", "skin", "look");
        K(CommandIds.ThemeCycle, "switch theme", "change theme");
        K(CommandIds.SwapPanels, "exchange panels", "switch sides");
        K(CommandIds.MaximizePanel, "zoom", "full screen", "maximize");
        K(CommandIds.AddPanel, "new panel", "split", "third panel", "vertical split", "dock");
        K(CommandIds.AddPanelBelow, "new panel", "split", "horizontal split", "stack", "dock");
        foreach (var move in new[] { CommandIds.PanelMoveLeft, CommandIds.PanelMoveRight, CommandIds.PanelMoveUp, CommandIds.PanelMoveDown })
            K(move, "dock", "arrange", "layout", "rearrange", "move panel");
        K(CommandIds.PanelSwapPlaces, "exchange panels", "switch places", "dock", "layout", "arrange");
        K(CommandIds.RotatePanels, "vertical", "horizontal", "stacked", "side by side", "orientation", "rotate", "layout");
        K(CommandIds.EqualizePanels, "same size", "even", "balance", "reset sizes", "layout");
        K(CommandIds.ClosePanel, "remove panel");
        K(CommandIds.QuickView, "preview pane", "thumbnail");
        K(CommandIds.Operations, "jobs", "progress", "transfers", "queue", "tasks", "background");
        K(CommandIds.Palette, "command palette", "commands", "actions", "run command");
        K(CommandIds.Help, "shortcuts", "keyboard", "hotkeys", "keys", "key bindings");
        K(CommandIds.About, "version");
        K(CommandIds.CheckUpdates, "update", "new version");
        K(CommandIds.DiagnosticsExport, "logs", "bug report", "crash report");
        K(CommandIds.ConnectNetworkDrive, "map drive", "network share", "smb", "map network drive");
        K(CommandIds.DisconnectNetworkDrive, "unmap drive", "disconnect share");
        K(CommandIds.SftpConnect, "ssh", "ftp", "ftps", "server", "remote", "connect");
        K(CommandIds.SftpDisconnect, "disconnect server", "close connection");
        K(CommandIds.CopyPaths, "full path", "file path", "copy path");
        K(CommandIds.CopyNames, "file names", "copy name");
        K(CommandIds.Bookmarks, "favorites", "favourites", "hotlist", "hot paths");
        K(CommandIds.FolderHistory, "recent folders");
        K(CommandIds.FileHistory, "recent files");
        K(CommandIds.WorkingSets, "collections", "basket");
        K(CommandIds.Undo, "revert", "take back");
        K(CommandIds.AnalyzeFolder, "folder sizes", "space usage", "largest files", "disk usage");
        K(CommandIds.UserMenu, "custom commands", "tools");
        K(CommandIds.SaveWorkspace, "session", "save layout");
        K(CommandIds.LoadWorkspace, "session", "restore layout");
        K(CommandIds.EditSessions, "remote edits", "pending uploads", "edited files");
        K(CommandIds.CommandLineFocus, "prompt", "command line");
        K(CommandIds.LocationMenuLeft, "drives", "change drive", "locations");
        K(CommandIds.LocationMenuRight, "drives", "change drive", "locations");
        K(CommandIds.FindFolder, "jump to folder", "go to folder", "cd");
        K(CommandIds.GoTo, "path", "address", "location", "cd");
        K(CommandIds.Refresh, "reload", "rescan", "reread");
        K(CommandIds.RegistryExport, "reg file", "export registry", "regedit");
        K(CommandIds.RegistryImport, "reg file", "import registry", "regedit");
        K(CommandIds.HexRecovery, "recover hex save", "interrupted save");
    }
}

/// <summary>Resolved bindings with per-context lookup and conflict detection.</summary>
public sealed class Keymap
{
    private readonly Dictionary<(CommandContext, KeyChord), string> _map = new();
    private readonly Dictionary<string, List<KeyChord>> _byCommand = new(StringComparer.Ordinal);
    private readonly List<string> _conflicts = [];

    public Keymap(CommandRegistry registry, IReadOnlyDictionary<string, string[]>? overrides = null)
    {
        Registry = registry;
        foreach (var def in registry.All)
        {
            var gestures = overrides is not null && overrides.TryGetValue(def.Id, out var o) ? o : def.DefaultGestures;
            foreach (var g in gestures)
            {
                if (!KeyChord.TryParse(g, out var chord))
                {
                    _conflicts.Add($"Invalid binding '{g}' for {def.Id}.");
                    continue;
                }
                if (_map.TryGetValue((def.Context, chord), out var existing))
                {
                    _conflicts.Add($"{chord} is bound to both {existing} and {def.Id}; {existing} wins.");
                    continue;
                }
                _map[(def.Context, chord)] = def.Id;
                if (!_byCommand.TryGetValue(def.Id, out var list)) _byCommand[def.Id] = list = [];
                list.Add(chord);
            }
        }
    }

    public CommandRegistry Registry { get; }

    public IReadOnlyList<string> Conflicts => _conflicts;

    /// <summary>Resolves a chord through the given contexts (most specific first); Global is always last.</summary>
    public string? Resolve(KeyChord chord, params CommandContext[] contexts)
    {
        foreach (var c in contexts)
        {
            if (_map.TryGetValue((c, chord), out var id)) return id;
        }
        return _map.TryGetValue((CommandContext.Global, chord), out var g) ? g : null;
    }

    public IReadOnlyList<KeyChord> GetChords(string commandId) =>
        _byCommand.TryGetValue(commandId, out var l) ? l : [];

    public string? GetGestureText(string commandId) =>
        _byCommand.TryGetValue(commandId, out var l) && l.Count > 0 ? l[0].ToDisplayString() : null;

    /// <summary>Function-key bar labels for one modifier combination (FAR-style per-modifier sets).</summary>
    public IReadOnlyList<(int Number, string? CommandId, string Label)> GetFunctionKeyBar(KeyMods mods, CommandContext context)
    {
        var result = new List<(int, string?, string)>(12);
        for (int i = 1; i <= 12; i++)
        {
            var id = Resolve(new KeyChord("F" + i, mods), context);
            var def = id is null ? null : Registry.Get(id);
            result.Add((i, id, def?.KeyBarLabel ?? def?.Title ?? string.Empty));
        }
        return result;
    }
}
