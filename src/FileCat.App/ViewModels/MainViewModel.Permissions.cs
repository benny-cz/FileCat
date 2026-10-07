using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Layout;
using FileCat.App.Services;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.ViewModels;

/// <summary>
/// Linux and macOS (P9): the attributes dialog edits permissions (owner, group, others × read, write, execute, plus the
/// special bits), in boxes or as octal. Only the permissions you change are applied: to the marked items and, when asked,
/// to everything inside them.
/// </summary>
public sealed partial class MainViewModel
{
    private static readonly (string Row, (string Column, UnixFileMode Bit)[] Bits)[] PermissionGrid =
    [
        ("Owner", [("read", UnixFileMode.UserRead), ("write", UnixFileMode.UserWrite), ("execute", UnixFileMode.UserExecute)]),
        ("Group", [("read", UnixFileMode.GroupRead), ("write", UnixFileMode.GroupWrite), ("execute", UnixFileMode.GroupExecute)]),
        ("Others", [("read", UnixFileMode.OtherRead), ("write", UnixFileMode.OtherWrite), ("execute", UnixFileMode.OtherExecute)]),
    ];

    private static readonly (string Label, UnixFileMode Bit)[] SpecialBits =
    [
        ("Set user ID", UnixFileMode.SetUser), ("Set group ID", UnixFileMode.SetGroup), ("Sticky", UnixFileMode.StickyBit),
    ];

    [UnsupportedOSPlatform("windows")]
    private async Task ChangeUnixAttributesAsync(TabViewModel tab, IReadOnlyList<ItemRef> sel, AttributeMetadata metadata, PreparationScope scope)
    {
        bool? State(UnixFileMode bit) => metadata.State(bit);

        UnixFileMode touched = 0;
        bool updating = false;
        string? shown = null;
        var boxes = new List<(CheckBox Box, UnixFileMode Bit)>();
        CheckBox Box(string name, UnixFileMode bit, object? content = null)
        {
            var initial = State(bit);
            var box = new CheckBox { IsChecked = initial, IsThreeState = initial is null, Content = content, MinWidth = content is null ? 0 : 24 };
            Avalonia.Automation.AutomationProperties.SetName(box, name);
            boxes.Add((box, bit));
            return box;
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("90,70,70,80"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Avalonia.Thickness(0, 2) };
        string[] headers = ["", "Read", "Write", "Execute"];
        for (int c = 0; c < headers.Length; c++)
        {
            var header = new TextBlock { Text = headers[c], Classes = { "muted" } };
            Grid.SetColumn(header, c);
            grid.Children.Add(header);
        }
        for (int r = 0; r < PermissionGrid.Length; r++)
        {
            var (row, bits) = PermissionGrid[r];
            var label = new TextBlock { Text = row, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, r + 1);
            grid.Children.Add(label);
            for (int c = 0; c < bits.Length; c++)
            {
                var box = Box($"{row} {bits[c].Column}", bits[c].Bit);
                Grid.SetRow(box, r + 1);
                Grid.SetColumn(box, c + 1);
                grid.Children.Add(box);
            }
        }
        var special = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        foreach (var (text, bit) in SpecialBits) special.Children.Add(Box(text, bit, text));

        var octal = new TextBox { Width = 80, PlaceholderText = "mixed" };
        Avalonia.Automation.AutomationProperties.SetName(octal, "Permissions as octal");
        var summary = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new Avalonia.Media.FontFamily("monospace"), Classes = { "muted" } };
        UnixFileMode? Current()
        {
            UnixFileMode mode = 0;
            foreach (var (box, bit) in boxes)
            {
                if (box.IsChecked is null) return null;
                if (box.IsChecked == true) mode |= bit;
            }
            return mode;
        }
        void ShowOctal()
        {
            var mode = Current();
            shown = mode is { } m ? UnixPermissions.Octal(m) : string.Empty;
            octal.Text = shown;
            summary.Text = mode is { } s ? UnixPermissions.Format(s) : "Mixed: unchanged where marked items differ";
        }
        foreach (var (box, bit) in boxes)
        {
            box.IsCheckedChanged += (_, _) =>
            {
                if (updating) return;
                if (box.IsChecked is null) touched &= ~bit;
                else touched |= bit;
                updating = true;
                ShowOctal();
                updating = false;
            };
        }
        // TextChanged arrives after the change (posted), so text the boxes wrote is recognized by value, not by a flag.
        octal.TextChanged += (_, _) =>
        {
            if (updating || octal.Text == shown || !UnixPermissions.TryParseOctal(octal.Text, out var typed)) return;
            shown = octal.Text;
            // A typed mode is meant exactly: every bit applies.
            updating = true;
            foreach (var (box, bit) in boxes)
            {
                box.IsThreeState = false;
                box.IsChecked = (typed & bit) != 0;
            }
            touched = UnixPermissions.Access | UnixPermissions.Special;
            summary.Text = UnixPermissions.Format(typed);
            updating = false;
        };
        ShowOctal();

        bool mac = OperatingSystem.IsMacOS();
        bool? hiddenState = metadata.State(FileAttributes.Hidden);
        var hidden = new CheckBox { Content = "Hidden in Finder", IsChecked = hiddenState, IsThreeState = hiddenState is null, IsVisible = mac };
        var modified = new TextBox { Text = metadata.Single?.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, PlaceholderText = "unchanged (yyyy-MM-dd HH:mm:ss)" };
        // Linux file systems keep a creation time but offer no way to set it.
        var created = new TextBox { Text = metadata.Single?.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty, PlaceholderText = "unchanged", IsVisible = mac };
        Avalonia.Automation.AutomationProperties.SetName(modified, "Modified");
        Avalonia.Automation.AutomationProperties.SetName(created, "Created");
        bool folders = metadata.HasFolders;
        var recursive = new CheckBox { Content = "Also apply to everything inside the marked folders (links are not followed)", IsVisible = folders };

        var body = new StackPanel { Spacing = 6, MinWidth = 480 };
        body.Children.Add(new TextBlock { Text = sel.Count == 1 ? sel[0].Name : Formatters.Plural(sel.Count, "item", "items"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        body.Children.Add(grid);
        body.Children.Add(special);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Octal:", VerticalAlignment = VerticalAlignment.Center }, octal, summary } });
        body.Children.Add(new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Text = "Only what you change is applied; a mixed box leaves each item as it is. Execute lets people open a folder; inside folders it is added only to files that are already executable.",
            Classes = { "muted", "small" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520,
        });
        body.Children.Add(hidden);
        body.Children.Add(new TextBlock { Text = "Modified:" });
        body.Children.Add(modified);
        body.Children.Add(new TextBlock { Text = "Created:", IsVisible = mac });
        body.Children.Add(created);
        body.Children.Add(recursive);
        var answer = await Dialogs.ShowCustomAsync("Permissions and times", body, [new DialogButton("Cancel", "cancel", IsCancel: true), new DialogButton("Apply", "ok", IsDefault: true)]);
        if (answer as string != "ok" || Services.Io.IsStopped) return;

        UnixFileMode set = 0, clear = 0;
        foreach (var (box, bit) in boxes)
        {
            if ((touched & bit) == 0) continue;
            if (box.IsChecked == true) set |= bit;
            else if (box.IsChecked == false) clear |= bit;
        }
        FileAttributes setAttributes = 0, clearAttributes = 0;
        if (mac && hidden.IsChecked != hiddenState)
        {
            if (hidden.IsChecked == true) setAttributes = FileAttributes.Hidden;
            else if (hidden.IsChecked == false) clearAttributes = FileAttributes.Hidden;
        }
        DateTime? Parse(TextBox t, DateTime? original) =>
            DateTime.TryParse(t.Text, out var d) && (original is null || Math.Abs((d.ToUniversalTime() - original.Value).TotalSeconds) >= 1) ? d.ToUniversalTime() : null;
        var mod = Parse(modified, metadata.Single?.ModifiedUtc);
        var cre = mac ? Parse(created, metadata.Single?.CreatedUtc) : null;
        bool deep = recursive.IsChecked == true && folders;
        // Without folders to go into, boxes set back to what every item already has change nothing.
        bool permissionsChange = deep ? (set | clear) != 0 : metadata.ModesChange(set, clear);
        if (!permissionsChange && setAttributes == 0 && clearAttributes == 0 && mod is null && cre is null)
        {
            Notify("Nothing to change.");
            return;
        }
        var job = Services.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Attributes,
            Sources = sel,
            Attributes = new AttributeChangeSet(setAttributes, clearAttributes, mod, cre, deep,
                permissionsChange ? set : 0, permissionsChange ? clear : 0),
        });
        scope.SelectionTransferred = true;
        Track(job, tab);
    }
}
