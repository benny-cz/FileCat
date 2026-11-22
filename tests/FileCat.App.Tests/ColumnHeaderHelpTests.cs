using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// A column says what it shows. Each header explains its column where the title cannot, and Reparse type shows a quiet
/// "none" for an ordinary item instead of a blank cell that the next column's numbers could be read into.
/// </summary>
public sealed class ColumnHeaderHelpTests
{
    [AvaloniaFact]
    public async Task Headers_explain_their_columns_and_a_header_with_nothing_to_add_has_no_tooltip()
    {
        using var f = new FileListFixture("short.txt");
        f.Services.Columns.Replace([("Help", [
            new ColumnSpec(ColumnField.Name, "Name", 200, Star: true),
            new ColumnSpec(ColumnField.Attributes, "Attr", 60),
            new ColumnSpec(ColumnField.Metadata, "Hard links", 80, RightAlign: true) { MetadataId = FileDetailsFields.HardLinks },
            new ColumnSpec(ColumnField.Metadata, "Reparse type", 120) { MetadataId = FileDetailsFields.ReparseType },
            new ColumnSpec(ColumnField.Kind, "Kind", 70),
        ])]);
        f.Tab.ColumnProfile = 0;
        f.Window.Width = 1600; // every header in view: column widths scale with the font
        await f.Load();

        Assert.Equal("Name\nClick to sort by it; click again to reverse.", HeaderTip(f, 0));
        string attributes = HeaderTip(f, 1)!;
        Assert.StartsWith("Attr\nR read-only · H hidden", attributes);
        Assert.Equal(OperatingSystem.IsWindows(), attributes.Contains("P another reparse point (Reparse type says which)", StringComparison.Ordinal));
        Assert.Contains("Observed names for a regular file", HeaderTip(f, 2));

        string reparse = HeaderTip(f, 3)!;
        Assert.StartsWith("Reparse type\n", reparse);
        Assert.Contains("Most items are not reparse points and show none.", reparse);
        Assert.Contains("Point at a value for what it means, its tag number and its Windows name.", reparse);
        string gesture = (f.Services.Keymap.GetGestureText(CommandIds.FileRecord) ?? "").Split(',')[0].Trim();
        Assert.NotEqual("", gesture);
        Assert.Contains($"File → File-system details… ({gesture}) shows an item's reparse data and WOF backing.", reparse);
        Assert.EndsWith("Click to sort by it; click again to reverse.", reparse);

        // A provider column that neither sorts nor needs a key keeps its header quiet.
        Assert.Null(HeaderTip(f, 4));
    }

    /// <summary>
    /// Tooltips respect every theme: a header's tooltip is drawn on each palette's tooltip surface in its text color, with
    /// its hints in the muted color, and the quiet "none" reads at 4.5:1 on an active and an inactive panel alike.
    /// </summary>
    [AvaloniaFact]
    public async Task Header_tooltips_and_the_quiet_none_follow_every_theme()
    {
        string before = ThemeManager.Current.Name;
        using var f = new FileListFixture("short.txt");
        // FileCat's own styles, as App.axaml includes them (the tests' application has Fluent's only).
        f.Window.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://FileCat.App.Tests/"))
        {
            Source = new Uri("avares://FileCat/Themes/Styles.axaml"),
        });
        f.Services.Columns.Replace([("Help", [
            new ColumnSpec(ColumnField.Name, "Name", 200, Star: true),
            new ColumnSpec(ColumnField.Metadata, "Reparse type", 120) { MetadataId = FileDetailsFields.ReparseType },
        ])]);
        f.Tab.ColumnProfile = 0;
        await f.Load();
        try
        {
            foreach (var name in ThemeManager.Names.Where(n => n != "System"))
            {
                ThemeManager.Apply(name);
                Dispatcher.UIThread.RunJobs();
                Assert.StartsWith("Reparse type\n", HeaderTip(f, 1));
                var content = Assert.IsType<RichTip>(ToolTip.GetTip(f.List));
                // Opening attaches and styles the tooltip at once. Read it before queued input runs: the headless pointer
                // may then leave the list for the popup, which closes the tooltip as leaving it does in the app.
                ToolTip.SetIsOpen(f.List, true);
                var toolTip = content.GetVisualAncestors().OfType<ToolTip>().First();
                Assert.NotNull(TopLevel.GetTopLevel(toolTip));
                TestContext.Current.TestOutputHelper?.WriteLine($"{name}: tooltip {toolTip.Background} with {toolTip.Foreground} text");
                Assert.Equal(ColorOf("FcTipBackgroundColor"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Background).Color);
                Assert.Equal(ColorOf("FcText"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Foreground).Color);
                var hints = content.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("tipHint")).ToList();
                Assert.Equal(2, hints.Count); // where to look further, and how to sort
                Assert.All(hints, hint => Assert.Equal(ColorOf("FcTextMuted"), Assert.IsAssignableFrom<ISolidColorBrush>(hint.Foreground).Color));
                ToolTip.SetIsOpen(f.List, false);
                Dispatcher.UIThread.RunJobs();

                var window = ColorOf("FcWindow");
                foreach (string panel in new[] { "FcPanel", "FcPanelInactive" })
                {
                    var surface = ThemeManager.Opaque(ColorOf(panel), window);
                    double ratio = ThemeManager.Contrast(ThemeManager.Opaque(ColorOf("FcTextMuted"), surface), surface);
                    Assert.True(ratio >= 4.5, $"{name}: the quiet none on {panel} is {ratio:0.00}:1");
                }
            }
        }
        finally { ThemeManager.Apply(before); }
    }

    private static Color ColorOf(string key) => Avalonia.Application.Current!.TryGetResource(key, null, out var v) switch
    {
        true when v is Color c => c,
        true when v is ISolidColorBrush b => b.Color,
        _ => throw new KeyNotFoundException(key),
    };

    /// <summary>b.txt is an ordinary file and a.txt a symbolic link, so a "none first" order is the reverse of name order.</summary>
    private sealed class TagRecords : IFileRecords
    {
        public bool IsSupported => true;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader invoked.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Full reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct) => new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = FileObjectKind.File,
            ReparseTag = Path.GetFileName(path) == "a.txt"
                ? ObservedFact<uint>.Known(ReparseLinks.SymbolicLinkTag, FactScope.Entry, "fixture")
                : ObservedFact<uint>.Unavailable(FactState.NotApplicable, FactScope.Entry, "fixture", "No observed reparse tag."),
        };
    }

    [AvaloniaFact]
    public async Task Reparse_type_shows_a_quiet_none_that_sorts_before_every_type_and_explains_a_link()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        foreach (var field in FileDetailsFields.Create(new TagRecords(), windows: true)) services.Metadata.Register(field);
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 2);
            string[] Names() => [.. Enumerable.Range(0, tab.Listing.VisibleCount).Select(i => tab.Listing.GetVisible(i))
                .Where(entry => entry.Kind != EntryKind.Parent).Select(entry => entry.Name)];
            await tab.AnalyzeAsync(FileDetailsFields.ReparseType);
            await Until(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion && tab.Listing.Sort.MetadataId == FileDetailsFields.ReparseType);
            Assert.False(tab.Listing.Sort.Descending);
            Assert.Equal(new[] { "b.txt", "a.txt" }, Names()); // "none" is the smallest value
            tab.Listing.Sort = tab.Listing.Sort with { Descending = true };
            await Until(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion);
            Assert.Equal(new[] { "a.txt", "b.txt" }, Names()); // a descending sort brings the reparse points to the top

            MetadataCellPresentation Cell(string name)
            {
                int index = tab.Listing.FindStoreIndex(name);
                var entry = tab.Listing.Store[index];
                return tab.MetadataPresentation(entry, index, FileDetailsFields.ReparseType, tab.CaptureMetadataContext(entry, index), demand: false);
            }
            // The explanations keep their own lines: what it is, then the tag, then where the observation came from.
            var none = Cell("b.txt");
            Assert.Equal(("none", true, true), (none.Text, none.Quiet, none.NeedsExplanation));
            Assert.StartsWith("No reparse point observed", none.Detail);
            Assert.Contains("\nWOF compression (compact.exe /EXE, CompactOS) hides its tag", none.Detail);
            Assert.Contains("\nObserved reparse type of this entry.", none.Detail);
            var link = Cell("a.txt");
            Assert.Equal(("Symbolic link", false), (link.Text, link.Quiet));
            Assert.Equal(3, link.Detail.Split('\n').Length);
            Assert.StartsWith("Tag 0xA000000C · IO_REPARSE_TAG_SYMLINK", link.Detail.Split('\n')[1]);
            Assert.DoesNotContain(EscapedLineFeed, none.Detail + link.Detail);

            // A quiet value takes the muted color of a value still being read; the marked and cursor colors stay.
            var list = Assert.Single(window.GetVisualDescendants().OfType<FileListControl>(), view => view.Tab == tab);
            IBrush row = Brushes.Black;
            var muted = list.MetadataBrush(none with { State = MetadataState.Pending, Quiet = false }, row, marked: false, cursor: false);
            Assert.NotSame(row, muted);
            Assert.Same(muted, list.MetadataBrush(none, row, marked: false, cursor: false));
            Assert.Same(row, list.MetadataBrush(none, row, marked: true, cursor: false));
            Assert.Same(row, list.MetadataBrush(none, row, marked: false, cursor: true));
            Assert.Same(row, list.MetadataBrush(link, row, marked: false, cursor: false));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>How a line feed reads once escaped for display: a backslash, then u000A.</summary>
    private static readonly string EscapedLineFeed = @"\" + "u000A";

    [AvaloniaFact]
    public async Task Only_a_field_that_marks_its_detail_as_lines_shows_its_line_breaks()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        static MetadataField Field(string id, bool lines) => new(id, id, MetadataCost.Cheap, _ => true, (_, _) => "value",
            v => v as string ?? "", Detail: _ => "first line\nsecond line", NeedsExplanation: _ => true, DetailLines: lines);
        services.Metadata.Register(Field("test-lines", lines: true));
        services.Metadata.Register(Field("test-escaped", lines: false));
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 2);
            foreach (string id in new[] { "test-lines", "test-escaped" }) await tab.AnalyzeAsync(id);
            int index = tab.Listing.FindStoreIndex("a.txt");
            var entry = tab.Listing.Store[index];
            string Detail(string id) => tab.MetadataPresentation(entry, index, id, tab.CaptureMetadataContext(entry, index), demand: false).Detail;
            Assert.Equal("first line\nsecond line", Detail("test-lines"));
            // Any other detail may carry a provider's text, so a line feed in it stays visible as an escape.
            Assert.Equal("first line" + EscapedLineFeed + "second line", Detail("test-escaped"));
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    /// <summary>Moves the pointer onto a column's header after the current frame is drawn, and reads the list's tooltip.</summary>
    private static string? HeaderTip(FileListFixture f, int column)
    {
        Dispatcher.UIThread.RunJobs();
        f.Window.CaptureRenderedFrame()?.Dispose();
        double[] x = (double[])typeof(FileListControl).GetField("_columnX", System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!.GetValue(f.List)!;
        var header = new Point(x[column] + 12, f.List.GetRowBounds(0).Top / 2);
        Assert.True(header.Y > 0);
        Assert.True(header.X < f.List.Bounds.Width, $"Column {column} starts at {x[column]:F0}, beyond the list's {f.List.Bounds.Width:F0} pixels.");
        f.Window.MouseMove(new Point(0, 0));
        f.Window.MouseMove(f.List.TranslatePoint(header, f.Window)!.Value);
        Dispatcher.UIThread.RunJobs();
        return ToolTip.GetTip(f.List)?.ToString(); // a header's RichTip reads as its text
    }
}
