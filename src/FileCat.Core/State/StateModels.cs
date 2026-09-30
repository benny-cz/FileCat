using System.Text.Json.Serialization;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Core.State;

/// <summary>An external program described as an executable plus structured argument tokens (plan §14.2).</summary>
public sealed class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Absolute path of a real executable (not a .cmd/.bat launcher unless ShellMode).</summary>
    public string Executable { get; set; } = string.Empty;
    /// <summary>Argument tokens; placeholders such as {file}, {files}, {listfile}, {dir}, {target}.</summary>
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = "{dir}";
    /// <summary>Explicitly accepts running through a shell (enables .cmd/.bat with metacharacters).</summary>
    public bool ShellMode { get; set; }
    public string? Hotkey { get; set; }
    /// <summary>For per-type associations: "view" (F3), "edit" (F4), or "open" (Enter).</summary>
    public string? Intent { get; set; }
    /// <summary>Optional masks for per-type associations ("*.log;*.txt").</summary>
    public string? Mask { get; set; }
    /// <summary>Items in a user-command submenu.</summary>
    public List<ToolDefinition>? Children { get; set; }
}

public sealed class SavedFilter
{
    public string Name { get; set; } = string.Empty;
    public string Mask { get; set; } = string.Empty;
}

public sealed class TerminalSettings
{
    /// <summary>"cmd", "powershell", "pwsh", "wt" (Windows Terminal), or "posix".</summary>
    public string Shell { get; set; } = OperatingSystem.IsWindows() ? "cmd" : "posix";
    public string? CustomExecutable { get; set; }
}

public sealed class ColumnProfile
{
    public string Name { get; set; } = "Details";
    public List<ColumnSetting> Columns { get; set; } = [];
}

public sealed class ColumnSetting
{
    public string Field { get; set; } = "name";
    public double Width { get; set; } = 100;
    public bool Visible { get; set; } = true;
}

public sealed class AppSettings : IVersionedState
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    /// <summary>"Classic", "ClassicDark", "Cyberpunk", "Psychedelic", "Steampunk", "HighContrast", or "System".</summary>
    public string Theme { get; set; } = "System";
    /// <summary>Animated theme light, edges, and glitches; off, they stand still. The system's reduce-motion setting also stops them.</summary>
    public bool ThemeAnimations { get; set; } = true;
    public bool ShowHidden { get; set; } = true;
    public bool NaturalSort { get; set; } = true;
    public bool DirectoriesFirst { get; set; } = true;
    public bool ConfirmRecycle { get; set; } = true;
    public bool SizeFolderOnSpace { get; set; } = true;
    /// <summary>Space also sizes folders on network, removable, and cloud-placeholder locations.</summary>
    public bool SizeFolderOnSlowLocations { get; set; }
    public bool ShowFunctionKeyBar { get; set; } = true;
    public bool ShowCommandLine { get; set; } = true;
    public bool ShowToolbar { get; set; } = true;
    /// <summary>The place buttons above each panel: drives and everything else the location menu offers (D-52, D-53).</summary>
    public bool ShowDriveButtons { get; set; } = true;
    public bool ShowTabsAlways { get; set; } = true;
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; } = 13;
    public string RowDensity { get; set; } = "Compact";
    /// <summary>"Culture" (system short date/time) or an invariant .NET format string.</summary>
    public string DateFormat { get; set; } = "Culture";
    public string SizeFormat { get; set; } = "Auto";
    public ToolDefinition? Editor { get; set; }
    public ToolDefinition? Viewer { get; set; }
    public ToolDefinition? DiffTool { get; set; }
    public TerminalSettings Terminal { get; set; } = new();
    public List<ToolDefinition> UserCommands { get; set; } = [];
    /// <summary>Named masks used as <c>@name</c> wherever a mask is accepted (plan §11 saved filters).</summary>
    public List<SavedFilter> SavedFilters { get; set; } = [];
    /// <summary>Find's saved searches (plan §11); one may load whenever Find opens.</summary>
    public List<Search.SavedSearch> SavedSearches { get; set; } = [];
    /// <summary>Folders Find does not search, each switchable.</summary>
    public List<Search.IgnoredFolderEntry> SearchIgnoredFolders { get; set; } = [];
    /// <summary>Find shows its log by itself after a search that could not read something.</summary>
    public bool SearchLogOnErrors { get; set; }
    /// <summary>Saved SFTP connections (no secrets; see <see cref="ISecretStore"/>).</summary>
    public List<RemoteProfile> RemoteProfiles { get; set; } = [];
    public List<ToolDefinition> Associations { get; set; } = [];
    public Dictionary<string, string[]> KeyBindings { get; set; } = new(StringComparer.Ordinal);
    public List<ColumnProfile> ColumnProfiles { get; set; } = [];
    public bool KeepAwakeDuringJobs { get; set; }
    public bool CheckForUpdates { get; set; }
    public DateTime? LastUpdateCheckUtc { get; set; }
    public bool DiagnosticMode { get; set; }
    public int RecentlyClosedTabs { get; set; } = 25;
    public int HistorySize { get; set; } = 200;
    public string DefaultVerify { get; set; } = "Native";
    public bool QuickSearchMatchAnywhere { get; set; }
    public bool SingleInstance { get; set; } = true;
    public string ViewerEncoding { get; set; } = "Auto";
    public bool ViewerWrap { get; set; } = true;
    /// <summary>Files up to this size are checked against the checksums and signatures beside them when shown (D-57); larger ones on request.</summary>
    public int VerifyAutomaticallyUpToMiB { get; set; } = 256;
    /// <summary>Thumbnails in quick view and programs' own icons from Windows Shell handlers, run in the restricted helper (TV-16).</summary>
    public bool ShellPictures { get; set; } = true;
    /// <summary>Shell pictures also on network and removable drives, whose handlers may reach the network (opt-in).</summary>
    public bool ShellPicturesOnNetworkAndRemovable { get; set; }
}

public sealed class TabState
{
    public Location? Location { get; set; }
    public bool Locked { get; set; }
    /// <summary>Locked tabs that return to their root when revisited (project roots).</summary>
    public bool ReturnToRoot { get; set; }
    public Location? LockedRoot { get; set; }
    public SortField SortField { get; set; } = SortField.Name;
    public bool SortDescending { get; set; }
    public string? Filter { get; set; }
    public int ColumnProfile { get; set; }
    public string? FocusName { get; set; }
    public List<Location> BackHistory { get; set; } = [];
    public List<Location> ForwardHistory { get; set; } = [];
}

public sealed class PanelState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public List<TabState> Tabs { get; set; } = [];
    public int ActiveTab { get; set; }
    /// <summary>Explicit target panel id; null means the implicit "other panel" with two panels.</summary>
    public string? TargetPanelId { get; set; }
    public double Size { get; set; } = 1;
}

public sealed class WindowPlacement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 1280;
    public double Height { get; set; } = 800;
    public bool Maximized { get; set; }
}

public sealed class WorkspaceState : IVersionedState
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public string Name { get; set; } = "Default";
    public List<PanelState> Panels { get; set; } = [];
    public string? ActivePanelId { get; set; }
    /// <summary>"Columns" (side by side) or "Rows": how P2 laid out every panel, read when <see cref="Tree"/> is missing.</summary>
    public string Layout { get; set; } = "Columns";
    /// <summary>Where each panel is (ADR-18); a tree that does not show exactly the saved panels is ignored.</summary>
    public PanelLayoutNode? Tree { get; set; }
    public WindowPlacement? Window { get; set; }
    public bool OperationsPaneOpen { get; set; }
}

public sealed class BookmarkEntry
{
    public string Name { get; set; } = string.Empty;
    public Location? Location { get; set; }
    /// <summary>Numbered slot 0–9 (Ctrl+0–9), or null for an unnumbered bookmark.</summary>
    public int? Slot { get; set; }
}

public sealed class HistoryEntry
{
    public Location? Location { get; set; }
    /// <summary>File name within the location for file history; null for folders.</summary>
    public string? Name { get; set; }
    public DateTime LastUsedUtc { get; set; }
    public bool Pinned { get; set; }
}

public sealed class HistoryState : IVersionedState
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public List<BookmarkEntry> Bookmarks { get; set; } = [];
    public List<HistoryEntry> Folders { get; set; } = [];
    public List<HistoryEntry> Files { get; set; } = [];
    public List<string> CommandLine { get; set; } = [];
    /// <summary>Commands run for each item (Apply command, Ctrl+G).</summary>
    public List<string> ApplyCommands { get; set; } = [];
    public List<string> CopyDestinations { get; set; } = [];
    public List<string> Masks { get; set; } = [];
    public List<string> SearchNames { get; set; } = [];
    public List<string> SearchTexts { get; set; } = [];
    /// <summary>Where Find searched (its Look in field).</summary>
    public List<string> SearchFolders { get; set; } = [];
    /// <summary>Command ids run from the command search, the palette, or the menus, most recent first (plan §4.4).</summary>
    public List<string> RecentCommands { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true, ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(WorkspaceState))]
[JsonSerializable(typeof(HistoryState))]
[JsonSerializable(typeof(Search.WorkingSetState))]
public sealed partial class StateJsonContext : JsonSerializerContext;
