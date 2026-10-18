using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.ViewModels;
using FileCat.Core.Search;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class FileListNamePresentationTests
{
    private sealed record ProviderDetails(string KindText, string DetailsText) : IDisplayDetails;

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
        var point = fixture.List.TranslatePoint(new Point(60, fixture.List.GetRowBounds(row).Center.Y), fixture.Window)!.Value;
        fixture.Window.MouseMove(point);
        Assert.Equal("Full name: x.iso\\u200B\nname contains 1 hidden character: zero-width space U+200B",
            ToolTip.GetTip(fixture.List));
        int storeIndex = fixture.Tab.Listing.GetStoreIndex(row);
        Assert.Contains("zero-width space U+200B", fixture.List.DescribeItem(storeIndex));
        fixture.Click(row);
        Assert.Contains("x.iso\\u200B", fixture.Tab.StatusRight);
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
