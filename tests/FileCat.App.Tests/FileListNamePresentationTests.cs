using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Platform;
using FileCat.Core.Search;
using FileCat.Core.Resources;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

public sealed class FileListNamePresentationTests
{
    private sealed record ProviderDetails(string KindText, string DetailsText) : IDisplayDetails;

    [AvaloniaFact]
    public void Provider_owned_kind_is_shared_by_picture_tiles_and_spoken_files_and_folders()
    {
        var location = new Location(Schemes.Sftp, "/owned", session: "fixture");
        foreach (var kind in new[] { EntryKind.File, EntryKind.Directory })
        {
            var entry = new EntryData("managed", kind) { Tag = new ProviderDetails("Provider item", "Provider details") };
            Assert.Equal("Provider item", EntryPresentation.Kind(entry, location));
            Assert.Equal("Provider item", FileListControl.PictureDetail(entry, location));
            Assert.Equal("Provider item", FileListControl.ItemKind(entry));
            Assert.DoesNotContain("Provider item", FileListControl.ItemWarnings(entry, location));
        }
    }

    private sealed class CloudRootShell : PortableShellServices
    {
        internal IReadOnlyList<CloudSyncRoot> Roots { get; set; } = [];
        internal int Reads { get; private set; }
        public override IReadOnlyList<CloudSyncRoot> CloudSyncRoots
        {
            get { Reads++; return Roots; }
        }
    }

    private sealed class CloudRootPlatform : PortablePlatform
    {
        internal CloudRootPlatform(CloudRootShell shell) => Shell = shell;
    }

    private static void Columns(FileListControl list, ColumnSpec[] columns) =>
        typeof(FileListControl).GetField("_columns", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(list, columns);

    [AvaloniaFact]
    public void Every_profile_presents_split_extensions_with_visible_identity_evidence()
    {
        var list = new FileListControl();
        var ordinary = new EntryData("x.iso", EntryKind.File);
        var hidden = new EntryData("x.iso\u200B", EntryKind.File);
        var bidi = new EntryData("x.is\u202Eo", EntryKind.File);
        var profiles = ColumnProfiles.Defaults.Select(profile => profile.Columns)
            .Append([new ColumnSpec(ColumnField.Name, "Name", 180),
                new ColumnSpec(ColumnField.Extension, "Extension", 36)])
            .ToArray();
        foreach (var profile in profiles)
        {
            Columns(list, profile);
            var name = profile.Single(column => column.Field == ColumnField.Name);
            var extension = profile.FirstOrDefault(column => column.Field == ColumnField.Extension);
            string cleanName = list.CellString(ordinary, name);
            string hiddenName = list.CellString(hidden, name);
            if (extension is null)
            {
                Assert.Equal("x.iso", cleanName);
                Assert.Equal("x.iso\\u200B", hiddenName);
            }
            else
            {
                Assert.Equal("x", cleanName);
                Assert.Equal("x", hiddenName);
                Assert.Equal("iso", list.CellString(ordinary, extension));
                Assert.Equal("iso\\u200B", list.CellString(hidden, extension));
                Assert.Equal("is\\u202Eo", list.CellString(bidi, extension));
            }
            Assert.NotEqual(
                cleanName + (extension is null ? "" : list.CellString(ordinary, extension)),
                hiddenName + (extension is null ? "" : list.CellString(hidden, extension)));
        }
        // Display work leaves all operation and sort identities untouched.
        Assert.Equal("x.iso\u200B", hidden.Name);
        Assert.Equal("iso\u200B", NameParts.GetExtension(hidden.Name));
    }

    [AvaloniaFact]
    public void Extension_presentation_preserves_legitimate_unicode_and_escapes_layout_controls()
    {
        var list = new FileListControl();
        var ext = new ColumnSpec(ColumnField.Extension, "Ext", 56);
        var persian = new EntryData("x.می\u200Cخواهم", EntryKind.File);
        Assert.Equal("می\u200Cخواهم", list.CellString(persian, ext));
        var line = new EntryData("x.iso\u2028pdf", EntryKind.File);
        Assert.Equal("iso\\u2028pdf", list.CellString(line, ext));
        var nonfunctionalSelector = new EntryData("x.1\uFE0F", EntryKind.File);
        Assert.Equal("1\\uFE0F", list.CellString(nonfunctionalSelector, ext));
    }

    [AvaloniaFact]
    public void Full_safe_identity_is_available_on_hover_and_described_for_screen_readers()
    {
        var hidden = new EntryData("x.iso\u200B", EntryKind.File);
        Assert.Equal("Full name: x.iso\\u200B\nname contains 1 hidden character: zero-width space U+200B",
            FileListControl.IdentityTip(hidden));
        Assert.Equal("x.iso\\u200B", FileListControl.ItemName(hidden));
        Assert.Contains("zero-width space U+200B", FileListControl.ItemWarnings(hidden));
        var bidi = new EntryData("x.is\u202Eo", EntryKind.File);
        Assert.Contains("right-to-left override U+202E", FileListControl.ItemWarnings(bidi));
        Assert.Null(FileListControl.IdentityTip(new EntryData("x.iso", EntryKind.File)));
    }

    [AvaloniaFact]
    public async Task Hovering_the_actual_row_exposes_the_complete_safe_filename()
    {
        using var fixture = new FileListFixture("x.iso", "x.iso\u200B");
        await fixture.Load();
        int row = Enumerable.Range(0, fixture.Tab.Listing.VisibleCount)
            .Single(i => fixture.Tab.Listing.GetVisible(i).Name == "x.iso\u200B");
        // Settle layout first: on a slow runner the row bounds could still be the pre-layout ones, so the move missed the row.
        fixture.Window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var point = fixture.List.TranslatePoint(new Point(60, fixture.List.GetRowBounds(row).Center.Y), fixture.Window)!.Value;
        fixture.Window.MouseMove(point);
        await FileListFixture.Wait(() => ToolTip.GetTip(fixture.List) is not null);
        Assert.Equal("Full name: x.iso\\u200B\nname contains 1 hidden character: zero-width space U+200B",
            ToolTip.GetTip(fixture.List));
        int storeIndex = fixture.Tab.Listing.GetStoreIndex(row);
        Assert.Contains("zero-width space U+200B", fixture.List.DescribeItem(storeIndex));
        fixture.Click(row);
        Assert.Contains("x.iso\\u200B", fixture.Tab.StatusRight);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Row_descriptions_use_cached_cloud_root_context_in_list_and_picture_modes(bool pictures)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows Cloud Files presentation.");
        var shell = new CloudRootShell();
        var previous = PlatformFactory.WindowsFactory;
        PlatformFactory.WindowsFactory = () => new CloudRootPlatform(shell);
        try
        {
            var (services, vm, window, root) = AccessibilityTests.OpenMainWindow(
                prepareFiles: folder => shell.Roots = [new("Fixture", folder)]);
            try
            {
                var tab = vm.ActiveTab!;
                await FileListFixture.Wait(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount == 3);
                var list = Assert.Single(window.GetVisualDescendants().OfType<FileListControl>(), view => view.Tab == tab);
                list.PictureMode = pictures;
                int index = tab.Listing.FindStoreIndex("a.txt");
                Assert.True(index >= 0);
                Assert.Contains("Pin policy: unspecified; local content: not established; synchronization: not inspected",
                    list.DescribeItem(index));
                Assert.Equal(EntryKind.Parent, tab.Listing.GetVisible(0).Kind);
                Assert.DoesNotContain("Pin policy", list.DescribeItem(tab.Listing.GetStoreIndex(0)));
                Assert.Equal(1, shell.Reads);
            }
            finally { AccessibilityTests.Close(services, window, root); }
        }
        finally { PlatformFactory.WindowsFactory = previous; }
    }

    [AvaloniaFact]
    public void A_cached_cloud_tag_supplies_file_row_speech_outside_registered_roots()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows Cloud Files presentation.");
        var entry = new EntryData("file.txt", EntryKind.File)
        {
            Attributes = (uint)FileAttributes.ReparsePoint,
            NativeReparseTag = 0x9000F01A,
        };
        string warnings = FileListControl.ItemWarnings(entry);
        Assert.Contains("Cloud-managed item", warnings);
        Assert.Contains("Pin policy: unspecified; local content: not established; synchronization: not inspected", warnings);
        Assert.DoesNotContain("opening may download", warnings);
        Assert.Equal(EntryFlags.None, entry.Flags);
    }

    [AvaloniaTheory]
    [InlineData(0x00000000u, "Reparse point · type unavailable")]
    [InlineData(0x8000FFFFu, "Reparse point · unknown type")]
    [InlineData(0xA000FFFFu, "Reparse point · unknown type")]
    [InlineData(0xA0000003u, "Junction or volume mount")]
    [InlineData(0xA000001Du, "WSL symbolic link")]
    [InlineData(0x8000001Bu, "App execution alias")]
    [InlineData(0x80000017u, "WOF-backed file")]
    [InlineData(0x9000F01Au, "Cloud-managed item")]
    [InlineData(0x80000023u, "Unix-domain socket")]
    public void Picture_tiles_show_the_same_cached_Windows_reparse_type_as_accessible_rows(uint tag, string kind)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native reparse presentation.");
        var location = Location.FileSystem(Path.GetTempPath());
        var entry = new EntryData("fixture.jpg", EntryKind.File)
        {
            Attributes = (uint)FileAttributes.ReparsePoint,
            NativeReparseTag = tag,
        };
        Assert.Equal(kind, FileListControl.PictureDetail(entry, location));
        Assert.Contains(kind, FileListControl.ItemWarnings(entry, location));

        entry.Flags |= EntryFlags.Offline;
        Assert.Equal("Opening may require a download", FileListControl.PictureDetail(entry, location));
    }

    [AvaloniaTheory]
    [InlineData(0x80000024u, "WSL FIFO", false)]
    [InlineData(0x80000025u, "WSL character device", false)]
    [InlineData(0x80000026u, "WSL block device", false)]
    [InlineData(0x9000001Au, "Cloud-managed item", true)]
    [InlineData(0x9000101Au, "Cloud-managed item", true)]
    [InlineData(0x9000701Au, "Cloud-managed item", true)]
    [InlineData(0x9000E01Au, "Cloud-managed item", true)]
    public void Wsl_special_files_and_every_cloud_subtag_share_one_label_across_tiles_speech_and_letters(uint tag, string kind, bool cloud)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native reparse presentation.");
        var location = Location.FileSystem(Path.GetTempPath());
        foreach (var item in new[] { EntryKind.File, EntryKind.Directory })
        {
            var entry = new EntryData("fixture.bin", item)
            {
                Attributes = (uint)(FileAttributes.ReparsePoint | (item == EntryKind.Directory ? FileAttributes.Directory : 0)),
                NativeReparseTag = tag,
                Flags = EntryFlags.Link,
            };
            Assert.Equal(kind, EntryPresentation.Kind(entry, location));
            Assert.Equal(kind, FileListControl.PictureDetail(entry, location));
            string warnings = FileListControl.ItemWarnings(entry, location);
            Assert.Contains(", " + kind, warnings);
            Assert.DoesNotContain("Symbolic link", warnings);
            Assert.Equal(cloud, warnings.Contains("Pin policy: unspecified; local content: not established; synchronization: not inspected", StringComparison.Ordinal));
            Assert.DoesNotContain("opening may download", warnings);
            Assert.False(EntryPresentation.IsLink(entry, location));
            Assert.Equal("P", FileCat.App.Services.Formatters.Attributes(entry, location));
            Assert.Equal(EntryFlags.Link, entry.Flags); // presentation never clears the operational flag
        }
        // A result row reads the tag through its captured original folder, while result provenance keeps the Kind text.
        var results = new Location(Schemes.ResultSet, "", session: "fixture");
        var result = new EntryData("fixture.bin", EntryKind.File)
        {
            Attributes = (uint)FileAttributes.ReparsePoint,
            NativeReparseTag = tag,
            Flags = EntryFlags.Link,
            Tag = new ResultTag(location, "relative", EntryKind.File),
        };
        Assert.False(EntryPresentation.IsLink(result, results));
        Assert.Equal("P", FileCat.App.Services.Formatters.Attributes(result, results));
        Assert.Equal("File", FileListControl.PictureDetail(result, results));
        Assert.Equal(cloud, FileListControl.ItemWarnings(result, results).Contains("Pin policy: unspecified", StringComparison.Ordinal));
    }

    /// <summary>The link overlay the control draws, recording each destination it is drawn to.</summary>
    private sealed class RecordingOverlay(Avalonia.Media.IImage inner) : Avalonia.Media.IImage
    {
        public List<Rect> Drawn { get; } = [];
        public Size Size => inner.Size;
        public void Draw(Avalonia.Media.DrawingContext context, Rect sourceRect, Rect destRect)
        {
            Drawn.Add(destRect);
            inner.Draw(context, sourceRect, destRect);
        }
    }

    /// <summary>Renders the control once into a recording context and returns where the link overlay was drawn.</summary>
    private static List<Rect> DrawnOverlays(FileListControl list, RecordingOverlay overlay)
    {
        overlay.Drawn.Clear();
        using (var context = new Avalonia.Media.DrawingGroup().Open()) list.Render(context);
        return [.. overlay.Drawn];
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task List_and_picture_link_overlays_follow_the_confirmed_link_type_not_the_legacy_flag(bool pictures)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native reparse presentation.");
        var original = Location.FileSystem(Path.GetTempPath());
        EntryData Row(string name, EntryKind kind, uint tag, bool reparse, EntryFlags flags) => new(name, kind, kind == EntryKind.File ? 1 : -1)
        {
            Attributes = (uint)((reparse ? FileAttributes.ReparsePoint : 0) | (kind == EntryKind.Directory ? FileAttributes.Directory : 0)),
            NativeReparseTag = tag,
            Flags = flags,
            Tag = new ResultTag(original, "", kind), // read through the captured filesystem original
        };
        EntryData[] rows =
        [
            Row("a-plain", EntryKind.Directory, 0, false, EntryFlags.None),
            Row("b-symlink", EntryKind.Directory, 0xA000000C, true, EntryFlags.Link),
            Row("c-mount", EntryKind.Directory, 0xA0000003, true, EntryFlags.Link),
            Row("d-unavailable", EntryKind.Directory, 0, true, EntryFlags.Link),
            Row("e-cloud", EntryKind.Directory, 0x9000F01A, true, EntryFlags.Link),
            Row("f-wof", EntryKind.Directory, 0x80000017, true, EntryFlags.Link),
            Row("g-unknown", EntryKind.Directory, 0xA000FFFF, true, EntryFlags.Link),
            Row("h-provider-link", EntryKind.Directory, 0, false, EntryFlags.Link),
            Row("i-shortcut.lnk", EntryKind.File, 0, false, EntryFlags.None),
        ];
        using var fixture = new FileListFixture(new FileListFixture.ListProvider { Entries = rows });
        var icons = fixture.Services.Icons;
        icons.UseNativeIcons = false; // the drawn arrow is FileCat's own overlay instance
        var overlay = new RecordingOverlay(icons.LinkOverlay);
        typeof(FileCat.App.Services.IconProvider).GetField("_vectorOverlay", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(icons, overlay);
        Assert.Same(overlay, icons.LinkOverlay);
        await fixture.Load();
        fixture.List.PictureMode = pictures;
        fixture.Window.UpdateLayout();
        // Lay the control out large enough that every row and tile is in view for this one recorded frame.
        var size = new Size(1280, 960);
        fixture.List.Measure(size);
        fixture.List.Arrange(new Rect(size));
        var listing = fixture.Tab.Listing;
        Assert.Equal(rows.Length, listing.VisibleCount);
        Assert.All(Enumerable.Range(0, rows.Length), row => Assert.True(fixture.List.GetRowBounds(row).Bottom <= fixture.List.Bounds.Height,
            $"row {row} at {fixture.List.GetRowBounds(row)} is in view of {fixture.List.Bounds} (window {fixture.Window.ClientSize})"));

        var drawn = DrawnOverlays(fixture.List, overlay);
        string[] withOverlay = [.. Enumerable.Range(0, listing.VisibleCount)
            .Where(row => drawn.Any(rect => fixture.List.GetRowBounds(row).Contains(rect.Center)))
            .Select(row => listing.GetVisible(row).Name).Order(StringComparer.Ordinal)];
        Assert.Equal(["b-symlink", "c-mount", "h-provider-link", "i-shortcut.lnk"], withOverlay);
        Assert.Equal(withOverlay.Length, drawn.Count); // one arrow per confirmed link or shortcut, none elsewhere
        // The drawn decision is the shared presentation helper's, and the legacy flag is left untouched.
        Assert.Equal(rows.Where(e => EntryPresentation.IsLink(e, fixture.Tab.Location) || FileCat.App.Services.IconProvider.IsShortcut(e))
            .Select(e => e.Name).Order(StringComparer.Ordinal), withOverlay);
        Assert.All(rows, e => Assert.Equal(e.Name is not ("a-plain" or "i-shortcut.lnk"), e.Has(EntryFlags.Link)));
    }

    [Fact]
    public void Drive_labels_and_remote_names_are_safe_in_the_focused_status()
    {
        var drive = new EntryData("X:", EntryKind.Drive)
        {
            Tag = new FileCat.Core.FileSystem.DriveTag("X:\\", "label\u200B", "Fixed", "NTFS", 1, 2, true, "share\u202Ename"),
        };
        var status = TabViewModel.DescribeFocused(drive);
        Assert.Contains("label\\u200B", status);
        Assert.Contains("share\\u202Ename", status);
    }

    [AvaloniaFact]
    public async Task Result_set_folder_and_provider_detail_cells_escape_hidden_characters()
    {
        using var fixture = new FileListFixture("source");
        await fixture.Load();
        var result = new EntryData("source", EntryKind.File)
        {
            Tag = new ResultTag(new FileCat.Core.Resources.Location(FileListFixture.Scheme, "first"), "relative", EntryKind.File)
            {
                Folder = "folder\u202Ename",
                Note = "group\u200B1",
            },
        };
        Assert.Equal("folder\\u202Ename", fixture.List.CellString(result, ColumnProfiles.ResultSet.Single(c => c.Field == ColumnField.Folder)));
        Assert.Equal("group\\u200B1", fixture.List.CellString(result, new ColumnSpec(ColumnField.Details, "Details", 150)));
        var provider = new EntryData("source", EntryKind.File)
        {
            Tag = new ProviderDetails("type\u202Ename", "detail\u200Bvalue"),
        };
        Assert.Equal("type\\u202Ename", fixture.List.CellString(provider, new ColumnSpec(ColumnField.Kind, "Kind", 70)));
        Assert.Equal("detail\\u200Bvalue", fixture.List.CellString(provider, new ColumnSpec(ColumnField.Details, "Details", 150)));
        Assert.Equal("folder\u202Ename", fixture.Tab.GetFolderText(result)); // raw sorting provenance
    }
}
