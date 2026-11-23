using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Views;

namespace FileCat.App.Tests;

/// <summary>The inspector's plain-language names: Explorer's basic rights and scopes, Windows' well-known accounts, Unix classes.</summary>
public sealed class PermissionPresentationTests
{
    [Theory]
    [InlineData(0x001F01FFu, 3, true, "Full control")]
    [InlineData(0x10000000u, 0, false, "Full control")] // GENERIC_ALL
    [InlineData(0x001301BFu, 3, true, "Modify")]
    [InlineData(0x001200A9u, 3, true, "Read and execute")]
    [InlineData(0x001200A9u, 2, true, "List folder contents")]
    [InlineData(0x001200A9u, 2, false, "Read and execute")]
    [InlineData(0xA0000000u, 0, false, "Read and execute")] // GENERIC_READ | GENERIC_EXECUTE
    [InlineData(0x00120089u, 0, false, "Read")]
    [InlineData(0x80000000u, 0, false, "Read")] // GENERIC_READ
    [InlineData(0x00000089u, 0, false, "Special")] // Explorer's Read includes READ_CONTROL
    [InlineData(0x00100116u, 0, false, "Write")]
    [InlineData(0x00120116u, 0, false, "Write")]
    [InlineData(0x0012019Fu, 0, false, "Read and write")]
    [InlineData(0x001201BFu, 0, false, "Read, write and execute")]
    [InlineData(0x00000001u, 0, false, "Special")]
    [InlineData(0x00010000u, 0, false, "Special")]
    public void Windows_masks_take_Explorers_basic_names(uint mask, byte flags, bool directory, string expected) =>
        Assert.Equal(expected, PermissionPresentation.WindowsAccess(mask, flags, directory));

    [Theory]
    [InlineData(0x00, "This folder only")]
    [InlineData(0x03, "This folder, subfolders and files")]
    [InlineData(0x13, "This folder, subfolders and files")] // inherited
    [InlineData(0x02, "This folder and subfolders")]
    [InlineData(0x01, "This folder and files")]
    [InlineData(0x0B, "Subfolders and files only")]
    [InlineData(0x0A, "Subfolders only")]
    [InlineData(0x09, "Files only")]
    [InlineData(0x07, "This folder, subfolders and files, one level")]
    [InlineData(0x08, "Nothing (inherit only, no targets)")]
    public void Folder_rules_name_what_they_reach(byte flags, string expected)
    {
        Assert.Equal(expected, PermissionPresentation.WindowsAppliesTo(flags, directory: true));
        Assert.Equal("This file", PermissionPresentation.WindowsAppliesTo(flags, directory: false));
    }

    [Fact]
    public void Every_granted_right_is_named_for_its_object_kind()
    {
        var file = PermissionPresentation.WindowsRights(0x001F01FF, directory: false);
        Assert.Equal(["Read data", "Write data", "Append data", "Execute file", "Read attributes", "Read extended attributes",
            "Write attributes", "Write extended attributes", "Delete children", "Delete", "Read permissions", "Change permissions",
            "Take ownership", "Synchronize"], file);
        var folder = PermissionPresentation.WindowsRights(0x00000027, directory: true);
        Assert.Equal(["List folder", "Create files", "Create folders", "Traverse folder"], folder);
        // Generic bits are named by what Windows maps them to; bits no name covers are listed by value.
        Assert.Contains("Read data", PermissionPresentation.WindowsRights(0x80000000, directory: false));
        Assert.Equal(["Other rights 0x00000200"], PermissionPresentation.WindowsRights(0x00000200, directory: false));
        Assert.Contains("Access audit rules", PermissionPresentation.WindowsRights(0x01000000, directory: false));
    }

    [Theory]
    [InlineData("S-1-5-18", "SYSTEM")]
    [InlineData("S-1-5-32-544", "Administrators")]
    [InlineData("S-1-1-0", "Everyone")]
    [InlineData("S-1-3-0", "CREATOR OWNER")]
    [InlineData("S-1-5-11", "Authenticated Users")]
    [InlineData("S-1-15-2-1", "ALL APPLICATION PACKAGES")]
    [InlineData("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464", "TrustedInstaller")]
    [InlineData("S-1-16-12288", "High Mandatory Level")]
    [InlineData("S-1-5-21-1-2-3-1001", null)]
    public void Well_known_accounts_are_named_without_a_lookup(string sid, string? expected) =>
        Assert.Equal(expected, PermissionPresentation.WellKnownWindowsName(sid));

    [Fact]
    public void Integrity_labels_name_their_level_and_policy()
    {
        Assert.Equal("Low (no write up)", PermissionPresentation.IntegrityLabel("S-1-16-4096", 1));
        Assert.Equal("High (no write up, no read up, no execute up)", PermissionPresentation.IntegrityLabel("S-1-16-12288", 7));
        Assert.Equal("S-1-16-1234", PermissionPresentation.IntegrityLabel("S-1-16-1234", 0));
    }

    [Fact]
    public void Unix_classes_and_special_bits_are_named()
    {
        ushort mode = 0b111_101_100 | 0x800 | 0x200; // 4754 with sticky: rwsr-xr-T
        Assert.Equal("Read, write, execute", PermissionPresentation.UnixClassRights(mode, 6));
        Assert.Equal("Read, execute", PermissionPresentation.UnixClassRights(mode, 3));
        Assert.Equal("Read", PermissionPresentation.UnixClassRights(mode, 0));
        Assert.Equal("None", PermissionPresentation.UnixClassRights(0, 0));
        Assert.Equal(["Set user ID", "Sticky"], PermissionPresentation.UnixSpecialBits(mode));
    }

    /// <summary>Wide enough, a row's cells stand in weighted columns; narrow, they flow onto lines and the heading takes no room.</summary>
    [AvaloniaFact]
    public void Table_rows_use_columns_when_wide_and_flow_when_narrow()
    {
        double[] weights = [1, 2, 1], minimums = [4, 6, 4];
        PermissionTablePanel Row(bool heading = false)
        {
            var panel = new PermissionTablePanel(weights, minimums, heading);
            foreach (string text in new[] { "Allow", "Authenticated Users", "Modify" })
                panel.Children.Add(new TextBlock { Text = text, FontSize = 14, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis });
            return panel;
        }
        var wide = Row();
        wide.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, 14d);
        wide.Measure(new Size(600, double.PositiveInfinity));
        wide.Arrange(new Rect(0, 0, 600, wide.DesiredSize.Height));
        Assert.True(wide.InColumns);
        // 600 - 2 × 12 gap = 576: the columns take 144, 288 and 144.
        Assert.Equal(0, wide.Children[0].Bounds.X, 1);
        Assert.Equal(156, wide.Children[1].Bounds.X, 1);
        Assert.Equal(456, wide.Children[2].Bounds.X, 1);

        var narrow = Row();
        narrow.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, 14d);
        narrow.Measure(new Size(150, double.PositiveInfinity));
        narrow.Arrange(new Rect(0, 0, 150, narrow.DesiredSize.Height));
        Assert.False(narrow.InColumns);
        Assert.All(narrow.Children, child => Assert.InRange(child.Bounds.Right, 0, 150.5));
        Assert.True(narrow.Children.Max(child => child.Bounds.Bottom) > narrow.Children[0].Bounds.Height, "The cells should flow onto more than one line.");

        var heading = Row(heading: true);
        heading.SetValue(Avalonia.Controls.Documents.TextElement.FontSizeProperty, 14d);
        heading.Measure(new Size(150, double.PositiveInfinity));
        Assert.Equal(0, heading.DesiredSize.Height);
    }
}
