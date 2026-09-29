using System.Collections.Concurrent;
using Avalonia.Media;
using FileCat.Core.Commands;

namespace FileCat.App.Services;

/// <summary>
/// Original 16×16 line icons for commands, shown in the menus and on the toolbar (D-49; plan §18.2: no third-party icon
/// assets). Strokes are drawn in the theme's text color; a part that carries meaning takes the theme's color for it
/// (folders the folder color, deleting the error color, arrows that act the active accent). Made once per command and
/// theme; commands without a drawing have no icon.
/// </summary>
public static class CommandIcons
{
    private static readonly ConcurrentDictionary<(string Id, string Theme), IImage?> s_cache = new();

    /// <summary>A part of a drawing: its path, the palette color it takes, and whether it is filled (or dashed) rather than stroked.</summary>
    private readonly record struct Part(string Path, string Color = "Text", bool Fill = false, double Opacity = 1, bool Dashed = false);

    // Shapes several icons share.
    private const string Page = "M4,1.5 L9.5,1.5 L12.5,4.5 L12.5,14.5 L4,14.5 Z M9.5,1.5 L9.5,4.5 L12.5,4.5";
    private const string Folder = "M1.5,4 L1.5,13 L14.5,13 L14.5,5 L7.5,5 L6,3 L1.5,3 Z";
    private const string Drive = "M1.5,5.5 L14.5,5.5 L14.5,11.5 L1.5,11.5 Z";
    private const string Clipboard = "M5.5,2.5 L10.5,2.5 L10.5,4.5 L5.5,4.5 Z M5,3.5 L3,3.5 L3,14.5 L13,14.5 L13,3.5 L11,3.5";
    private const string Magnifier = "M6.5,2.5 C8.7,2.5 10.5,4.3 10.5,6.5 C10.5,8.7 8.7,10.5 6.5,10.5 C4.3,10.5 2.5,8.7 2.5,6.5 C2.5,4.3 4.3,2.5 6.5,2.5 Z M9.5,9.5 L14,14";
    private const string Circle = "M8,1.5 C11.6,1.5 14.5,4.4 14.5,8 C14.5,11.6 11.6,14.5 8,14.5 C4.4,14.5 1.5,11.6 1.5,8 C1.5,4.4 4.4,1.5 8,1.5 Z";
    private const string Box = "M2.5,2.5 L13.5,2.5 L13.5,13.5 L2.5,13.5 Z";
    private const string Panes = "M1.5,2.5 L7,2.5 L7,13.5 L1.5,13.5 Z M9,2.5 L14.5,2.5 L14.5,13.5 L9,13.5 Z";
    private const string Tab = "M1.5,6.5 L14.5,6.5 L14.5,14 L1.5,14 Z M2.5,6.5 L3.8,2.5 L9.2,2.5 L10.5,6.5";
    private const string Plus = "M11.5,9 L11.5,15 M8.5,12 L14.5,12";
    private const string Cross = "M9.5,9.5 L14,14 M14,9.5 L9.5,14";
    private const string Archive = "M2,5.5 L14,5.5 L14,14 L2,14 Z M1.5,2.5 L14.5,2.5 L14.5,5.5 L1.5,5.5 Z";
    private const string Clock = "M8,1.5 C11.6,1.5 14.5,4.4 14.5,8 C14.5,11.6 11.6,14.5 8,14.5 C4.4,14.5 1.5,11.6 1.5,8 C1.5,4.4 4.4,1.5 8,1.5 Z M8,4.5 L8,8 L10.5,9.5";
    private const string Trash = "M2.5,4.5 L13.5,4.5 M6,4.5 L6,2.5 L10,2.5 L10,4.5 M4,4.5 L4.8,14 L11.2,14 L12,4.5 M6.8,7 L6.8,11.5 M9.2,7 L9.2,11.5";
    private const string Eye = "M1.5,8 C3.5,4.3 12.5,4.3 14.5,8 C12.5,11.7 3.5,11.7 1.5,8 Z";
    private const string Pupil = "M8,6 C9.1,6 10,6.9 10,8 C10,9.1 9.1,10 8,10 C6.9,10 6,9.1 6,8 C6,6.9 6.9,6 8,6 Z";
    private const string Server = "M2.5,2 L13.5,2 L13.5,7 L2.5,7 Z M2.5,9 L13.5,9 L13.5,14 L2.5,14 Z M5,4.5 L6,4.5 M5,11.5 L6,11.5";
    private const string Key = "M1,4 L6,4 L7,5 L15,5 L15,13 L1,13 Z";
    private const string Minus = "M8.5,12 L14.5,12";
    private const string Collection = "M2.5,5 L10,5 L10,14.5 L2.5,14.5 Z M5,5 L5,2 L13.5,2 L13.5,12 L10,12";
    private const string CommandLine = "M1.5,10.5 L14.5,10.5 L14.5,14 L1.5,14 Z";
    private const string Columns = "M1.5,2.5 L14.5,2.5 L14.5,13.5 L1.5,13.5 Z M1.5,5.5 L14.5,5.5 M6,2.5 L6,13.5 M10.5,2.5 L10.5,13.5";
    private const string LetterE = "M6,4.5 L6,11.5 M6,4.5 L10,4.5 M6,8 L9,8 M6,11.5 L10,11.5";
    private const string LetterN = "M5.5,11.5 L5.5,4.5 L10.5,11.5 L10.5,4.5";

    public static IImage? Get(string commandId) => s_cache.GetOrAdd((commandId, ThemeManager.Current.Name), k => Create(k.Id));

    /// <summary>The drawing for a command: its own, or a family's (every column profile draws columns).</summary>
    private static Part[]? DrawingOf(string id) =>
        Drawings.TryGetValue(id, out var parts) ? parts
        : id.StartsWith(CommandIds.ColumnProfilePrefix, StringComparison.Ordinal) ? [new(Columns, "TextMuted"), new("M1.5,5.5 L14.5,5.5", "ActiveAccent")]
        : null;

    /// <summary>The commands that have an icon (the screenshot tool draws them all for review).</summary>
    public static IReadOnlyList<string> All => [.. Drawings.Keys];

    private static readonly Dictionary<string, Part[]> Drawings = new(StringComparer.Ordinal)
    {
        // File
        [CommandIds.Open] = [new(Box, "TextMuted"), new("M4.5,8 L11.5,8 M9,5.5 L11.5,8 L9,10.5", "ActiveAccent")],
        [CommandIds.View] = [new(Eye), new(Pupil, "ActiveAccent", Fill: true)],
        [CommandIds.ViewAlternate] = [new(Eye), new(Pupil, "TextMuted", Fill: true)],
        [CommandIds.Edit] = [new("M2.5,13.5 L3,10.5 L10.5,3 L13,5.5 L5.5,13 Z M9.5,4 L12,6.5"), new("M2.5,13.5 L5.5,13", "ActiveAccent")],
        [CommandIds.EditNew] = [new(Page), new(Plus, "Success")],
        [CommandIds.HexEdit] = [new(Box, "TextMuted"), new("M4.5,5 L4.5,11 M4.5,8 L7,8 M7,5 L7,11 M9,5 L12,11 M12,5 L9,11", "ActiveAccent")],
        [CommandIds.Copy] = [new("M3,4.5 L3,14.5 L10.5,14.5", "TextMuted"), new("M5.5,1.5 L11,1.5 L13.5,4 L13.5,12 L5.5,12 Z")],
        [CommandIds.Duplicate] = [new("M3,4.5 L3,14.5 L10.5,14.5", "TextMuted"), new("M5.5,1.5 L11,1.5 L13.5,4 L13.5,12 L5.5,12 Z"), new("M9.5,5 L9.5,9 M7.5,7 L11.5,7", "Success")],
        [CommandIds.Move] = [new("M1.5,2.5 L7.5,2.5 L7.5,13.5 L1.5,13.5 Z", "TextMuted"), new("M5,8 L14.5,8 M11.5,5 L14.5,8 L11.5,11", "ActiveAccent")],
        [CommandIds.Rename] = [new("M1.5,5 L14.5,5 L14.5,11 L1.5,11 Z", "TextMuted"), new("M9,3 L9,13 M7.5,3 L10.5,3 M7.5,13 L10.5,13", "ActiveAccent")],
        [CommandIds.MakeDirectory] = [new("M1.5,4 L1.5,13 L9,13 M14.5,8 L14.5,5 L7.5,5 L6,3 L1.5,3 Z", "FolderIcon"), new(Plus, "Success")],
        [CommandIds.Delete] = [new(Trash, "Error")],
        [CommandIds.DeletePermanent] = [new(Trash, "Error"), new("M1.5,1.5 L14.5,14.5", "Error")],
        [CommandIds.Pack] = [new(Archive, "ArchiveIcon"), new("M8,6.5 L8,12 M6,10 L8,12 L10,10", "Text")],
        [CommandIds.Unpack] = [new(Archive, "ArchiveIcon"), new("M8,12 L8,6.5 M6,8.5 L8,6.5 L10,8.5", "Text")],
        [CommandIds.TestArchive] = [new(Archive, "ArchiveIcon"), new("M5.5,9.5 L7.3,11.3 L10.5,7.8", "Success")],
        [CommandIds.Checksum] = [new("M6.5,2 L5,14 M11.5,2 L10,14 M2.5,5.5 L14,5.5 M2,10.5 L13.5,10.5")],
        [CommandIds.VerifyChecksums] = [new("M5.5,2 L4.5,10 M10,2 L9,10 M2.5,4.5 L11.5,4.5 M2,8 L11,8", "TextMuted"), new("M8.5,12 L10.5,14 L14.5,9.5", "Success")],
        [CommandIds.Attributes] = [new("M2.5,4 L13.5,4 M2.5,8 L13.5,8 M2.5,12 L13.5,12"), new("M6,2.5 L6,5.5 M10.5,6.5 L10.5,9.5 M4.5,10.5 L4.5,13.5", "ActiveAccent")],
        [CommandIds.CreateLink] = [new(Box, "TextMuted"), new("M5,11 L11,5 M7,5 L11,5 L11,9", "TextLink")],
        [CommandIds.BulkRename] = [new("M1.5,3 L10.5,3 M1.5,7 L10.5,7 M1.5,11 L10.5,11", "TextMuted"), new("M13,2 L13,14 M11.5,2 L14.5,2 M11.5,14 L14.5,14", "ActiveAccent")],
        [CommandIds.ApplyCommand] = [new(Box, "TextMuted"), new("M4.5,5.5 L7,8 L4.5,10.5 M8.5,10.5 L11.5,10.5", "ActiveAccent")],
        [CommandIds.Undo] = [new("M5,3.5 L2,6.5 L5,9.5 M2,6.5 L10,6.5 C12.5,6.5 14,8.2 14,10.2 C14,12.3 12.5,13.8 10,13.8 L6.5,13.8")],
        [CommandIds.Properties] = [new(Circle), new("M8,7 L8,11.5 M8,4.8 L8,4.9", "ActiveAccent")],
        [CommandIds.Exit] = [new("M9.5,2.5 L3,2.5 L3,13.5 L9.5,13.5", "TextMuted"), new("M6.5,8 L14.5,8 M11.5,5 L14.5,8 L11.5,11")],
        [CommandIds.EditSessions] = [new("M1.5,3 L8,3 M1.5,6 L8,6 M1.5,9 L5.5,9", "TextMuted"), new("M7.5,14 L7.9,11.6 L12.5,7 L14.5,9 L9.9,13.6 Z", "ActiveAccent")],
        [CommandIds.AddToWorkingSet] = [new(Collection, "FileIcon"), new(Plus, "Success")],
        [CommandIds.RemoveFromSet] = [new(Collection, "FileIcon"), new(Minus, "Error")],
        [CommandIds.RegistryExport] = [new(Key, "FolderIcon"), new("M8,11.5 L13.5,6 M10,6 L13.5,6 L13.5,9.5", "ActiveAccent")],
        [CommandIds.RegistryImport] = [new(Key, "FolderIcon"), new("M13.5,6 L8,11.5 M8,8 L8,11.5 L11.5,11.5", "ActiveAccent")],
        [CommandIds.RegistrySaveData] = [new(Page, "TextMuted"), new("M8,5.5 L8,11.5 M5.5,9 L8,11.5 L10.5,9", "ActiveAccent")],
        [CommandIds.RegistryLoadData] = [new(Page, "TextMuted"), new("M8,11.5 L8,5.5 M5.5,8 L8,5.5 L10.5,8", "ActiveAccent")],
        [CommandIds.RegistryWritable] = [new(Key, "FolderIcon"), new("M4.5,12 L4.8,10.2 L10,5 L11.8,6.8 L6.6,12 Z", "ActiveAccent")],
        [CommandIds.RegistryView] = [new(Key, "FolderIcon"), new("M4.5,10 C4.5,8.3 5.8,7 7.5,7 L11.5,7 M10,5.5 L11.5,7 L10,8.5", "ActiveAccent")],
        [CommandIds.Reveal] = [new(Folder, "FolderIcon"), new("M8.5,10 L13.5,5 M10,5 L13.5,5 L13.5,8.5", "Text")],
        [CommandIds.OpenWithSystem] = [new(Box, "TextMuted"), new("M6,10 L13.5,2.5 M9.5,2.5 L13.5,2.5 L13.5,6.5", "ActiveAccent")],
        // Mark
        [CommandIds.MarkToggleDown] = [new(Box), new("M5,8 L7.2,10.2 L11.2,5.8", "TextMarked")],
        [CommandIds.MarkToggle] = [new(Box), new("M5,8 L7.2,10.2 L11.2,5.8", "TextMarked")],
        [CommandIds.MarkSelectMask] = [new(Box, "TextMuted"), new("M8,4.5 L8,11.5 M4.5,8 L11.5,8", "TextMarked")],
        [CommandIds.MarkUnselectMask] = [new(Box, "TextMuted"), new("M4.5,8 L11.5,8", "TextMarked")],
        [CommandIds.MarkInvert] = [new(Box), new("M2.5,13.5 L13.5,2.5 L13.5,13.5 Z", "TextMarked", Fill: true, Opacity: 0.7)],
        [CommandIds.MarkAll] = [new("M1.5,3.5 L3,5 L5.5,2 M1.5,8.5 L3,10 L5.5,7 M1.5,13.5 L3,15 L5.5,12", "TextMarked"), new("M8,3.5 L14.5,3.5 M8,8.5 L14.5,8.5 M8,13.5 L14.5,13.5")],
        [CommandIds.MarkInvertAll] = [new(Box, "TextMarked"), new("M2.5,13.5 L13.5,2.5 L13.5,13.5 Z", "TextMarked", Fill: true, Opacity: 0.7)],
        [CommandIds.MarkSameExt] = [new(Box, "TextMuted"), new(LetterE, "TextMarked")],
        [CommandIds.UnmarkSameExt] = [new(Box, "TextMuted"), new(LetterE, "TextMuted")],
        [CommandIds.MarkSameName] = [new(Box, "TextMuted"), new(LetterN, "TextMarked")],
        [CommandIds.UnmarkSameName] = [new(Box, "TextMuted"), new(LetterN, "TextMuted")],
        [CommandIds.MarkRestore] = [new("M5,3.5 L2,6.5 L5,9.5 M2,6.5 L10,6.5 C12.5,6.5 14,8.2 14,10.2 C14,12.3 12.5,13.8 10,13.8 L6.5,13.8", "TextMarked")],
        [CommandIds.UnmarkHidden] = [new(Page, "TextMuted", Dashed: true), new("M6,10 L10.5,10", "TextMarked")],
        [CommandIds.CopyNames] = [new(Clipboard), new("M6,8.5 L10,8.5", "ActiveAccent")],
        [CommandIds.CopyPaths] = [new(Clipboard), new("M5.5,10.5 L7,6.5 M8,8.5 L10.5,8.5", "ActiveAccent")],
        [CommandIds.CopyUncPaths] = [new(Clipboard), new("M5,10.5 L6.5,6.5 M7.5,10.5 L9,6.5", "ActiveAccent")],
        [CommandIds.MarkNone] = [new("M2,2 L5,2 L5,5 L2,5 Z M2,7 L5,7 L5,10 L2,10 Z M2,12 L5,12 L5,15 L2,15 Z", "TextMuted"), new("M8,3.5 L14.5,3.5 M8,8.5 L14.5,8.5 M8,13.5 L14.5,13.5")],
        // Navigate
        [CommandIds.Parent] = [new("M8,13.5 L8,3 M3.5,7.5 L8,3 L12.5,7.5")],
        [CommandIds.Root] = [new("M3,2.5 L13,2.5", "ActiveAccent"), new("M8,14 L8,5.5 M4.5,9 L8,5.5 L11.5,9")],
        [CommandIds.Back] = [new("M13.5,8 L3,8 M7.5,3.5 L3,8 L7.5,12.5")],
        [CommandIds.Forward] = [new("M2.5,8 L13,8 M8.5,3.5 L13,8 L8.5,12.5")],
        [CommandIds.Enter] = [new("M13,3 L13,9 L4,9 M6.5,6.5 L4,9 L6.5,11.5")],
        [CommandIds.Home] = [new("M1.5,8 L8,2 L14.5,8 M3.5,6.5 L3.5,14 L12.5,14 L12.5,6.5"), new("M6.5,14 L6.5,10 L9.5,10 L9.5,14", "ActiveAccent")],
        [CommandIds.GoTo] = [new("M8,14.5 C8,14.5 3,9.5 3,6.5 C3,3.7 5.2,1.5 8,1.5 C10.8,1.5 13,3.7 13,6.5 C13,9.5 8,14.5 8,14.5 Z"), new("M8,5 C8.8,5 9.5,5.7 9.5,6.5 C9.5,7.3 8.8,8 8,8 C7.2,8 6.5,7.3 6.5,6.5 C6.5,5.7 7.2,5 8,5 Z", "ActiveAccent", Fill: true)],
        [CommandIds.LocationMenuLeft] = [new("M1.5,3.5 L14.5,3.5 L14.5,9.5 L1.5,9.5 Z", "DriveIcon"), new("M11,6.5 L12,6.5", "DriveIcon"), new("M2,11.5 L7,11.5 L4.5,14.5 Z", "ActiveAccent", Fill: true)],
        [CommandIds.LocationMenuRight] = [new("M1.5,3.5 L14.5,3.5 L14.5,9.5 L1.5,9.5 Z", "DriveIcon"), new("M11,6.5 L12,6.5", "DriveIcon"), new("M9,11.5 L14,11.5 L11.5,14.5 Z", "ActiveAccent", Fill: true)],
        [CommandIds.FindFolder] = [new(Folder, "FolderIcon", Opacity: 0.8), new("M10,7.5 C11.4,7.5 12.5,8.6 12.5,10 C12.5,11.4 11.4,12.5 10,12.5 C8.6,12.5 7.5,11.4 7.5,10 C7.5,8.6 8.6,7.5 10,7.5 Z M11.8,11.8 L14.5,14.5")],
        [CommandIds.FolderHistory] = [new(Clock)],
        [CommandIds.FileHistory] = [new(Clock, "TextMuted"), new("M8,4.5 L8,8 L10.5,9.5")],
        [CommandIds.Bookmarks] = [new("M8,1.8 L9.9,5.9 L14.3,6.3 L11,9.3 L12,13.7 L8,11.4 L4,13.7 L5,9.3 L1.7,6.3 L6.1,5.9 Z", "Warning")],
        [CommandIds.WorkingSets] = [new("M2.5,5 L10,5 L10,14.5 L2.5,14.5 Z", "FileIcon"), new("M5,5 L5,2 L13.5,2 L13.5,12 L10,12", "TextMuted")],
        [CommandIds.Refresh] = [new("M13.5,8 C13.5,11 11,13.5 8,13.5 C5,13.5 2.5,11 2.5,8 C2.5,5 5,2.5 8,2.5 C9.7,2.5 11.2,3.3 12.2,4.5"), new("M12.5,1.5 L12.5,4.8 L9.2,4.8", "ActiveAccent")],
        [CommandIds.ToggleHidden] = [new(Page, "TextMuted", Dashed: true), new("M6,9.5 C7.2,7.8 9.3,7.8 10.5,9.5 C9.3,11.2 7.2,11.2 6,9.5 Z", "ActiveAccent")],
        // Commands
        [CommandIds.FindFiles] = [new(Magnifier)],
        [CommandIds.CompareDirectories] = [new(Panes, "FolderIcon"), new("M3,6 L5.5,6 M3,9 L5.5,9 M10.5,6 L13,6 M10.5,9 L13,9", "Text")],
        [CommandIds.CompareFiles] = [new("M1.5,2 L6.5,2 L6.5,14 L1.5,14 Z M9.5,2 L14.5,2 L14.5,14 L9.5,14 Z", "TextMuted"), new("M3,5 L5,5 M3,8 L5,8 M11,5 L13,5 M11,8 L13,8", "Text"), new("M3,11 L5,11 M11,11 L13,11", "Warning")],
        [CommandIds.FlatView] = [new("M1.5,3 L14.5,3 M1.5,6.3 L14.5,6.3 M1.5,9.6 L14.5,9.6 M1.5,13 L14.5,13")],
        [CommandIds.QuickFilter] = [new("M2,3 L14,3 L9.5,8.5 L9.5,13 L6.5,14.5 L6.5,8.5 Z")],
        [CommandIds.CommandLineFocus] = [new("M1.5,10.5 L14.5,10.5 L14.5,14 L1.5,14 Z", "TextMuted"), new("M3.5,12.2 L6,12.2", "ActiveAccent")],
        [CommandIds.OpenTerminal] = [new("M1.5,2.5 L14.5,2.5 L14.5,13.5 L1.5,13.5 Z"), new("M4,6 L6.5,8 L4,10 M8,10 L11.5,10", "ActiveAccent")],
        [CommandIds.InsertName] = [new(CommandLine, "TextMuted"), new("M8,2 L8,8 M6,6 L8,8 L10,6", "ActiveAccent")],
        [CommandIds.InsertPath] = [new(CommandLine, "TextMuted"), new("M8,2 L8,8 M6,6 L8,8 L10,6 M11.5,4.5 L13,1.5", "ActiveAccent")],
        [CommandIds.UserMenu] = [new("M2,3.5 L3,3.5 M5,3.5 L14,3.5 M2,8 L3,8 M5,8 L14,8 M2,12.5 L3,12.5 M5,12.5 L14,12.5")],
        [CommandIds.CopyToClipboard] = [new(Clipboard), new("M6,8 L10,8 M6,11 L10,11", "ActiveAccent")],
        [CommandIds.CutToClipboard] = [new("M4.5,10 C5.6,10 6.5,10.9 6.5,12 C6.5,13.1 5.6,14 4.5,14 C3.4,14 2.5,13.1 2.5,12 C2.5,10.9 3.4,10 4.5,10 Z M11.5,10 C12.6,10 13.5,10.9 13.5,12 C13.5,13.1 12.6,14 11.5,14 C10.4,14 9.5,13.1 9.5,12 C9.5,10.9 10.4,10 11.5,10 Z"), new("M6,10.5 L12,2 M10,10.5 L4,2", "ActiveAccent")],
        [CommandIds.PasteFromClipboard] = [new(Clipboard, "TextMuted"), new("M6.5,7 L11,7 L13,9 L13,14.5 L6.5,14.5 Z")],
        [CommandIds.ConnectNetworkDrive] = [new(Drive, "DriveIcon"), new("M8,11.5 L8,14.5 M4.5,14.5 L11.5,14.5", "ActiveAccent")],
        [CommandIds.DisconnectNetworkDrive] = [new(Drive, "DriveIcon"), new("M8,11.5 L8,13 M5.5,13.5 L7,15 M7,13.5 L5.5,15", "Error")],
        [CommandIds.SftpConnect] = [new(Server, "DriveIcon"), new(Plus, "Success")],
        [CommandIds.SftpDisconnect] = [new(Server, "DriveIcon"), new(Cross, "Error")],
        // Panels
        [CommandIds.SwitchPanel] = [new(Panes, "TextMuted"), new("M3,8 L13,8 M10.5,5.5 L13,8 L10.5,10.5", "ActiveAccent")],
        [CommandIds.SwitchPanelBack] = [new(Panes, "TextMuted"), new("M13,8 L3,8 M5.5,5.5 L3,8 L5.5,10.5", "ActiveAccent")],
        [CommandIds.SwapPanels] = [new("M2,5.5 L14,5.5 M11,2.5 L14,5.5 L11,8.5 M14,10.5 L2,10.5 M5,7.5 L2,10.5 L5,13.5")],
        [CommandIds.OpenInTarget] = [new(Panes, "TextMuted"), new("M4.2,5.5 L4.2,10.5 M11.8,5.5 L11.8,10.5", "TargetAccent")],
        [CommandIds.TargetToSource] = [new(Panes, "TextMuted"), new("M11.8,5.5 L11.8,10.5 M4.2,5.5 L4.2,10.5", "ActiveAccent")],
        [CommandIds.QuickView] = [new(Panes, "TextMuted"), new("M9.5,8 C10.3,6.5 13.7,6.5 14.5,8 C13.7,9.5 10.3,9.5 9.5,8 Z", "ActiveAccent")],
        [CommandIds.AddPanel] = [new("M1.5,2.5 L9.5,2.5 L9.5,13.5 L1.5,13.5 Z", "TextMuted"), new(Plus, "Success")],
        [CommandIds.ClosePanel] = [new("M1.5,2.5 L9.5,2.5 L9.5,13.5 L1.5,13.5 Z", "TextMuted"), new(Cross, "Error")],
        [CommandIds.AddPanelBelow] = [new("M1.5,1.5 L14.5,1.5 L14.5,8 L1.5,8 Z", "TextMuted"), new("M8,9.5 L8,15 M5.25,12.25 L10.75,12.25", "Success")],
        [CommandIds.FocusPanelPicker] = [new(Panes, "TextMuted"), new("M3,6 L5.5,6 L5.5,10 L3,10 Z", "ActiveAccent", Fill: true)],
        [CommandIds.ChooseTarget] = [new(Panes, "TextMuted"), new("M11.75,6 C12.9,6 13.75,6.9 13.75,8 C13.75,9.1 12.9,10 11.75,10 C10.6,10 9.75,9.1 9.75,8 C9.75,6.9 10.6,6 11.75,6 Z", "TargetAccent")],
        [CommandIds.PanelMoveLeft] = [new(Box, "TextMuted"), new("M12,8 L4,8 M6.5,5.5 L4,8 L6.5,10.5", "ActiveAccent")],
        [CommandIds.PanelMoveRight] = [new(Box, "TextMuted"), new("M4,8 L12,8 M9.5,5.5 L12,8 L9.5,10.5", "ActiveAccent")],
        [CommandIds.PanelMoveUp] = [new(Box, "TextMuted"), new("M8,12 L8,4 M5.5,6.5 L8,4 L10.5,6.5", "ActiveAccent")],
        [CommandIds.PanelMoveDown] = [new(Box, "TextMuted"), new("M8,4 L8,12 M5.5,9.5 L8,12 L10.5,9.5", "ActiveAccent")],
        [CommandIds.PanelSwapPlaces] = [new(Panes, "TextMuted"), new("M3,6 L13,6 M11,4 L13,6 L11,8 M13,10 L3,10 M5,8 L3,10 L5,12", "ActiveAccent")],
        [CommandIds.RotatePanels] = [new("M3,6 C4,3.5 7,2.5 9.5,3.5 M8,1.8 L9.8,3.5 L8.3,5.2 M13,10 C12,12.5 9,13.5 6.5,12.5 M8,14.2 L6.2,12.5 L7.7,10.8")],
        [CommandIds.EqualizePanels] = [new("M2,3 L2,13 M14,3 L14,13", "TextMuted"), new("M4,8 L12,8 M6,6 L4,8 L6,10 M10,6 L12,8 L10,10", "ActiveAccent")],
        [CommandIds.MaximizePanel] = [new(Box, "TextMuted"), new("M5,11 L11,5 M7.5,5 L11,5 L11,8.5", "ActiveAccent")],
        [CommandIds.NewTab] = [new(Tab), new("M8,7.5 L8,11.5 M6,9.5 L10,9.5", "Success")],
        [CommandIds.CloseTab] = [new(Tab), new("M6.5,8 L9.5,11 M9.5,8 L6.5,11", "Error")],
        [CommandIds.ReopenTab] = [new(Tab, "TextMuted"), new("M6.5,7.5 L5,9 L6.5,10.5 M5,9 L9.5,9 C10.6,9 11,9.8 11,10.5", "ActiveAccent")],
        [CommandIds.NextTab] = [new(Tab, "TextMuted"), new("M5.5,10.25 L10.5,10.25 M8.5,8.25 L10.5,10.25 L8.5,12.25", "ActiveAccent")],
        [CommandIds.PreviousTab] = [new(Tab, "TextMuted"), new("M10.5,10.25 L5.5,10.25 M7.5,8.25 L5.5,10.25 L7.5,12.25", "ActiveAccent")],
        [CommandIds.DuplicateTab] = [new("M3.5,8.5 L3.5,15 L12.5,15", "TextMuted"), new("M5.5,6.5 L15,6.5 L15,12.5 L5.5,12.5 Z M6.5,6.5 L7.6,3.5 L11.4,3.5 L12.5,6.5")],
        [CommandIds.CopyTabToTarget] = [new(Tab, "TextMuted"), new("M5,10.25 L11,10.25 M9,8.25 L11,10.25 L9,12.25", "TargetAccent")],
        [CommandIds.LockTab] = [new("M4.5,7.5 L11.5,7.5 L11.5,14 L4.5,14 Z"), new("M6,7.5 L6,5 C6,3.9 6.9,3 8,3 C9.1,3 10,3.9 10,5 L10,7.5", "ActiveAccent")],
        [CommandIds.OpenInNewTab] = [new(Tab, "TextMuted"), new("M6,12 L10.5,7.5 M7.5,7.5 L10.5,7.5 L10.5,10.5", "ActiveAccent")],
        [CommandIds.OpenInNewTargetTab] = [new(Tab, "TextMuted"), new("M6,12 L10.5,7.5 M7.5,7.5 L10.5,7.5 L10.5,10.5", "TargetAccent")],
        [CommandIds.TabList] = [new(Tab, "TextMuted"), new("M5,8 L11,8 M5,10.5 L11,10.5", "Text")],
        // View
        [CommandIds.SortName] = [new("M2,12 L4.5,4 L7,12 M2.9,9.5 L6.1,9.5"), new("M11.5,3 L11.5,13 M9.5,11 L11.5,13 L13.5,11", "ActiveAccent")],
        [CommandIds.SortExtension] = [new("M2.5,7.5 L2.5,8 M5,4 L5,12 M5,4 L8,4 M5,8 L7.5,8 M5,12 L8,12"), new("M11.5,3 L11.5,13 M9.5,11 L11.5,13 L13.5,11", "ActiveAccent")],
        [CommandIds.SortTime] = [new("M5.5,3.5 C7.7,3.5 9.5,5.3 9.5,7.5 C9.5,9.7 7.7,11.5 5.5,11.5 C3.3,11.5 1.5,9.7 1.5,7.5 C1.5,5.3 3.3,3.5 5.5,3.5 Z M5.5,5.5 L5.5,7.5 L7,8.5"), new("M12.5,3 L12.5,13 M10.5,11 L12.5,13 L14.5,11", "ActiveAccent")],
        [CommandIds.SortSize] = [new("M2,13 L2,10 M4.5,13 L4.5,7 M7,13 L7,4"), new("M11.5,3 L11.5,13 M9.5,11 L11.5,13 L13.5,11", "ActiveAccent")],
        [CommandIds.SortNone] = [new("M2,4 L9,4 M2,8 L13,8 M2,12 L6,12")],
        [CommandIds.ToggleToolbar] = [new("M1.5,3 L14.5,3 L14.5,7.5 L1.5,7.5 Z"), new("M4,5.25 L5,5.25 M7.5,5.25 L8.5,5.25 M11,5.25 L12,5.25", "ActiveAccent"), new("M1.5,10.5 L14.5,10.5 M1.5,13.5 L10,13.5", "TextMuted")],
        [CommandIds.AnalyzeFolder] = [new(Circle), new("M8,8 L8,1.5 M8,8 L13.6,11.2", "ActiveAccent")],
        [CommandIds.ThemePick] = [new("M8,1.5 C4.4,1.5 1.5,4.2 1.5,7.6 C1.5,11.1 4.3,14.5 7.6,14.5 C9.2,14.5 9,12.8 8.3,12 C7.6,11.2 8.3,9.8 9.6,9.8 L11.4,9.8 C13.4,9.8 14.5,8.6 14.5,7 C14.5,3.9 11.6,1.5 8,1.5 Z"),
            new("M4.5,7 L4.6,7 M6.5,4.5 L6.6,4.5 M9.8,4.5 L9.9,4.5 M11.8,6.8 L11.9,6.8", "ActiveAccent")],
        [CommandIds.ThemeCycle] = [new("M8,1.5 C4.4,1.5 1.5,4.2 1.5,7.6 C1.5,11.1 4.3,14.5 7.6,14.5 C9.2,14.5 9,12.8 8.3,12 C7.6,11.2 8.3,9.8 9.6,9.8 L11.4,9.8 C13.4,9.8 14.5,8.6 14.5,7 C14.5,3.9 11.6,1.5 8,1.5 Z", "TextMuted"),
            new("M4.5,7 L4.6,7 M6.5,4.5 L6.6,4.5 M9.8,4.5 L9.9,4.5", "ActiveAccent")],
        // Tools
        [CommandIds.FindDeleted] = [new(Trash, "TextMuted"), new("M8,12.5 L8,7 M6,9 L8,7 L10,9", "Success")],
        [CommandIds.Operations] = [new("M1.5,3.5 L14.5,3.5 M1.5,8 L14.5,8 M1.5,12.5 L14.5,12.5", "TextMuted"), new("M1.5,3.5 L10,3.5 M1.5,8 L5.5,8", "Progress")],
        [CommandIds.Palette] = [new(Magnifier, "TextMuted"), new("M4.5,5 L6.5,6.5 L4.5,8", "ActiveAccent")],
        [CommandIds.Settings] = [new("M8,5 C9.7,5 11,6.3 11,8 C11,9.7 9.7,11 8,11 C6.3,11 5,9.7 5,8 C5,6.3 6.3,5 8,5 Z"),
            new("M8,1.5 L8,3.5 M8,12.5 L8,14.5 M1.5,8 L3.5,8 M12.5,8 L14.5,8 M3.4,3.4 L4.8,4.8 M11.2,11.2 L12.6,12.6 M3.4,12.6 L4.8,11.2 M11.2,4.8 L12.6,3.4", "ActiveAccent")],
        [CommandIds.SaveWorkspace] = [new("M2.5,2.5 L11.5,2.5 L13.5,4.5 L13.5,13.5 L2.5,13.5 Z M5,2.5 L5,6 L10.5,6 L10.5,2.5"), new("M4.5,13.5 L4.5,9.5 L11.5,9.5 L11.5,13.5", "ActiveAccent")],
        [CommandIds.LoadWorkspace] = [new(Folder, "FolderIcon"), new("M8,11.5 L8,7 M6,9 L8,7 L10,9", "OnAccent")],
        [CommandIds.HexRecovery] = [new(Box, "TextMuted"), new("M4.5,5 L4.5,11 M4.5,8 L7,8 M7,5 L7,11 M9,5 L12,11 M12,5 L9,11", "Warning")],
        [CommandIds.DiagnosticsExport] = [new("M1.5,8 L4.5,8 L6,4 L9,12.5 L10.5,8 L14.5,8", "ActiveAccent")],
        // The Find window's own actions.
        ["find.run"] = [new(Magnifier)],
        ["find.keepAgain"] = [new(Magnifier, "TextMuted"), new("M9,12 L11,14 L14.5,10", "Success")],
        ["find.removeAgain"] = [new(Magnifier, "TextMuted"), new(Minus, "Error")],
        ["find.addNew"] = [new(Magnifier, "TextMuted"), new(Plus, "Success")],
        ["find.stop"] = [new("M4,4 L12,4 L12,12 L4,12 Z", "Error", Fill: true)],
        ["find.skipFolder"] = [new(Folder, "FolderIcon", Opacity: 0.8), new("M6,7 L8.5,9 L6,11 M9,7 L11.5,9 L9,11", "ActiveAccent")],
        ["find.duplicates"] = [new("M1.5,2 L7,2 L7,11 L1.5,11 Z M9,5 L14.5,5 L14.5,14 L9,14 Z", "TextMuted"), new("M3,5 L5.5,5 M3,7.5 L5.5,7.5 M10.5,8 L13,8 M10.5,10.5 L13,10.5", "Warning")],
        ["find.hideSelected"] = [new("M1.5,3 L10.5,3 M1.5,7 L10.5,7 M1.5,11 L6,11", "TextMuted"), new(Minus, "Error")],
        ["find.hideDuplicates"] = [new("M1.5,2 L7,2 L7,9 L1.5,9 Z M4,4.5 L9.5,4.5 L9.5,11 L7,11", "TextMuted"), new(Minus, "Error")],
        ["find.allButOne"] = [new("M1.5,3.5 L3,5 L5.5,2 M1.5,8.5 L3,10 L5.5,7", "TextMarked"), new("M2,12 L5,12 L5,15 L2,15 Z", "TextMuted"), new("M8,3.5 L14.5,3.5 M8,8.5 L14.5,8.5 M8,13.5 L14.5,13.5")],
        ["find.log"] = [new(Page, "TextMuted"), new("M6,7 L10.5,7 M6,9.5 L10.5,9.5 M6,12 L9,12", "Text")],
        ["find.sortFolder"] = [new("M1.5,4 L1.5,12 L9,12 L9,5.5 L5,5.5 L4,4 Z", "FolderIcon"), new("M12.5,3 L12.5,13 M10.5,11 L12.5,13 L14.5,11", "ActiveAccent")],
        ["find.ignored"] = [new(Folder, "FolderIcon", Opacity: 0.8), new("M3,14.5 L13,4", "Error")],
        // The comparison window's own actions.
        ["compare.first"] = [new("M3,2.5 L13,2.5"), new("M8,14 L8,5 M4.5,8.5 L8,5 L11.5,8.5", "ActiveAccent")],
        ["compare.previous"] = [new("M8,13.5 L8,3 M4,7 L8,3 L12,7", "ActiveAccent")],
        ["compare.next"] = [new("M8,2.5 L8,13 M4,9 L8,13 L12,9", "ActiveAccent")],
        ["compare.last"] = [new("M3,13.5 L13,13.5"), new("M8,2 L8,11 M4.5,7.5 L8,11 L11.5,7.5", "ActiveAccent")],
        ["compare.copyHex"] = [new(Clipboard), new("M5.5,8 L7,8 M9,8 L10.5,8 M5.5,11 L7,11 M9,11 L10.5,11", "ActiveAccent")],
        ["compare.copyText"] = [new(Clipboard), new("M5.5,7.5 L10.5,7.5 M8,7.5 L8,12", "ActiveAccent")],
        ["compare.copyOffset"] = [new(Clipboard), new("M5.5,9.5 L10.5,9.5 M8.5,7.5 L10.5,9.5 L8.5,11.5", "ActiveAccent")],
        ["compare.selectAll"] = [new(Box, "TextMuted", Dashed: true), new("M5,5.5 L11,5.5 L11,10.5 L5,10.5 Z", "ActiveAccent", Fill: true, Opacity: 0.6)],
        // Help
        [CommandIds.Help] = [new(Circle), new("M6.2,6.2 C6.2,4.2 9.8,4.2 9.8,6.2 C9.8,7.6 8,7.9 8,9.3 M8,11.3 L8,11.4", "ActiveAccent")],
        [CommandIds.CheckUpdates] = [new("M2.5,10.5 L2.5,13.5 L13.5,13.5 L13.5,10.5", "TextMuted"), new("M8,2 L8,10 M5,7 L8,10 L11,7", "ActiveAccent")],
        [CommandIds.About] = [new(Circle), new("M8,7 L8,11.5 M8,4.8 L8,4.9", "ActiveAccent")],
    };

    private static IImage? Create(string id)
    {
        if (DrawingOf(id) is not { } parts) return null;
        var group = new DrawingGroup();
        // The 16×16 box, so every icon aligns the same.
        group.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Avalonia.Rect(0, 0, 16, 16)), Brush = Brushes.Transparent });
        foreach (var part in parts)
        {
            var brush = new SolidColorBrush(Resolve(part.Color), part.Opacity);
            var pen = new Pen(brush, 1.2, part.Dashed ? new DashStyle([1.5, 1.5], 0) : null, part.Dashed ? PenLineCap.Flat : PenLineCap.Round, PenLineJoin.Round);
            group.Children.Add(part.Fill
                ? new GeometryDrawing { Geometry = Geometry.Parse(part.Path), Brush = brush }
                : new GeometryDrawing { Geometry = Geometry.Parse(part.Path), Pen = pen });
        }
        return new DrawingImage(group);
    }

    private static Color Resolve(string key)
    {
        if (Avalonia.Application.Current?.TryGetResource("Fc" + key + "Color", null, out var v) == true && v is Color c) return c;
        return Colors.Gray;
    }

    /// <summary>A theme change draws every icon anew in the new colors.</summary>
    public static void Clear() => s_cache.Clear();
}
