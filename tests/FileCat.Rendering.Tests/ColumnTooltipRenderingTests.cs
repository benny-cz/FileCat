using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.Rendering.Tests;

/// <summary>
/// The file list's optional columns and their tooltips as this platform draws them, with its own fonts, in every theme:
/// the list with its values, a header's tooltip and a value's tooltip, each on the theme's tooltip surface, the parts
/// that matter in its key color and the source in its muted color. The values come from a fixture reader, so every
/// platform shows the same kinds; with FILECAT_TEST_COLUMN_RENDER_OUT set, the frames are kept there as PNG files.
/// </summary>
public sealed class ColumnTooltipRenderingTests
{
    /// <summary>plain.txt is ordinary, "link" a symbolic link to "folder".</summary>
    private sealed class Records : IFileRecords
    {
        public bool IsSupported => true;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader invoked.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Full reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            string name = Path.GetFileName(path);
            bool folder = name == "folder";
            return new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
            {
                Kind = name == "link" ? FileObjectKind.SymbolicLink : folder ? FileObjectKind.Directory : FileObjectKind.File,
                Allocated = ObservedFact<long>.Known(folder ? 0 : 4096, FactScope.Entry, "fixture"),
                LinkCount = ObservedFact<uint>.Known(1, FactScope.Entry, "fixture"),
                ReparseTag = name == "link" ? ObservedFact<uint>.Known(ReparseLinks.SymbolicLinkTag, FactScope.Entry, "fixture")
                    : ObservedFact<uint>.Unavailable(FactState.NotApplicable, FactScope.Entry, "fixture", "No observed reparse tag."),
            };
        }
    }

    [AvaloniaFact]
    public async Task Columns_and_their_tooltips_draw_in_every_theme()
    {
        string? output = Environment.GetEnvironmentVariable("FILECAT_TEST_COLUMN_RENDER_OUT");
        if (output is { Length: > 0 }) Directory.CreateDirectory(output);
        string platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        string root = Path.Combine(Path.GetTempPath(), "filecat-column-render-" + Guid.NewGuid().ToString("N"));
        string files = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        File.WriteAllText(Path.Combine(files, "plain.txt"), "plain");
        Directory.CreateDirectory(Path.Combine(files, "folder"));
        try { Directory.CreateSymbolicLink(Path.Combine(files, "link"), Path.Combine(files, "folder")); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { } // Windows without the link privilege

        string theme = ThemeManager.RequestedName;
        var fluent = new FluentTheme();
        var styles = new StyleInclude(new Uri("avares://FileCat/")) { Source = new Uri("avares://FileCat/Themes/Styles.axaml") };
        Application.Current!.Styles.Add(fluent); Application.Current.Styles.Add(styles);
        var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
        foreach (var field in FileDetailsFields.Create(new Records(), windows: true)) services.Metadata.Register(field);
        Window? window = null;
        try
        {
            services.Columns.Replace([("Rendered", [
                new ColumnSpec(ColumnField.Name, "Name", 200, Star: true),
                new ColumnSpec(ColumnField.Size, "Size", 80, RightAlign: true),
                new ColumnSpec(ColumnField.Attributes, "Attr", 50),
                new ColumnSpec(ColumnField.Metadata, "Reparse type", 150) { MetadataId = FileDetailsFields.ReparseType },
                new ColumnSpec(ColumnField.Metadata, "Hard links", 80, RightAlign: true) { MetadataId = FileDetailsFields.HardLinks },
                new ColumnSpec(ColumnField.Metadata, "Allocated", 100, RightAlign: true) { MetadataId = FileDetailsFields.Allocated },
            ])]);
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(files));
            tab.ColumnProfile = 0;
            var list = new FileListControl { Tab = tab };
            var shown = new Window { Width = 1100, Height = 360, Content = list };
            window = shown;
            shown.Show();
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing);
            string[] fields = [FileDetailsFields.ReparseType, FileDetailsFields.Allocated];
            string[] names = File.Exists(Path.Combine(files, "link")) || Directory.Exists(Path.Combine(files, "link")) ? ["plain.txt", "link"] : ["plain.txt"];
            MetadataCellPresentation Cell(string name, string id)
            {
                int index = tab.Listing.FindStoreIndex(name);
                var entry = tab.Listing.Store[index];
                return tab.MetadataPresentation(entry, index, id, tab.CaptureMetadataContext(entry, index), demand: false);
            }
            // The values are read off the UI thread once their rows are drawn.
            await Until(() =>
            {
                shown.CaptureRenderedFrame()?.Dispose();
                return names.All(name => fields.All(id => Cell(name, id).State is MetadataState.Available));
            });
            Assert.Equal("none", Cell("plain.txt", FileDetailsFields.ReparseType).Text);

            var columns = tab.Columns;
            string valueName = names[^1]; // the symbolic link where there is one: its tag line shows the code style
            var value = Cell(valueName, FileDetailsFields.ReparseType);
            foreach (string name in ThemeManager.Names.Where(n => n != "System"))
            {
                ThemeManager.Apply(name);
                Dispatcher.UIThread.RunJobs();
                Save(shown, $"{platform}-{name}-columns.png");
                Show(shown, list, Assert.IsType<RichTip>(list.HeaderTip(columns[3])), $"{platform}-{name}-header-tip.png", $"{name} header", "tipTerm");
                Show(shown, list, new ValueTip("Reparse type", value.Text, value.Detail.Split('\n', StringSplitOptions.RemoveEmptyEntries)),
                    $"{platform}-{name}-value-tip.png", $"{name} value", value.Text == "none" ? null : "tipCode");
            }
        }
        finally
        {
            window?.Close();
            services.Dispose();
            Application.Current.Styles.Remove(styles); Application.Current.Styles.Remove(fluent);
            ThemeManager.Apply(theme);
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }

        void Save(Window target, string file)
        {
            using var frame = target.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (output is { Length: > 0 }) frame.Save(Path.Combine(output, file), new PngBitmapEncoderOptions());
        }

        // The frame cut to one part of it (a tooltip), so the frames of every theme compare side by side.
        void SaveCrop(Window target, Visual part, string file)
        {
            using var frame = target.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (output is not { Length: > 0 }) return;
            var at = part.TranslatePoint(new Point(0, 0), target)!.Value;
            double scale = frame.PixelSize.Width / target.Bounds.Width;
            var rect = new PixelRect((int)(at.X * scale), (int)(at.Y * scale), (int)Math.Ceiling(part.Bounds.Width * scale),
                (int)Math.Ceiling(part.Bounds.Height * scale)).Intersect(new PixelRect(frame.PixelSize));
            using var crop = new WriteableBitmap(rect.Size, frame.Dpi, frame.Format, frame.AlphaFormat);
            using (var pixels = crop.Lock()) frame.CopyPixels(rect, pixels.Address, pixels.RowBytes * rect.Height, pixels.RowBytes);
            crop.Save(Path.Combine(output, file), new PngBitmapEncoderOptions());
        }

        // Opens the list's own tooltip content in the middle of the list (the pointer elsewhere, so nothing closes it),
        // checks the theme's tooltip colors and the highlighted part's key color, and keeps the frame.
        void Show(Window target, FileListControl list, Control content, string file, string what, string? highlighted)
        {
            ToolTip.SetTip(list, content);
            ToolTip.SetPlacement(list, PlacementMode.Center);
            ToolTip.SetIsOpen(list, true);
            var toolTip = content.GetVisualAncestors().OfType<ToolTip>().FirstOrDefault();
            Assert.True(toolTip is not null, $"{what}: no tooltip opened.");
            // The theme fades a tooltip in; the frame shows it as it settles, fully opaque.
            toolTip.Transitions = null;
            toolTip.Opacity = 1;
            Assert.Equal(ColorOf("FcTipBackgroundColor"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Background).Color);
            Assert.Equal(ColorOf("FcText"), Assert.IsAssignableFrom<ISolidColorBrush>(toolTip.Foreground).Color);
            if (highlighted is not null)
            {
                var parts = content.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains(highlighted)).ToList();
                Assert.NotEmpty(parts);
                Assert.All(parts, part => Assert.Equal(ColorOf("FcTipKey"), Assert.IsAssignableFrom<ISolidColorBrush>(part.Foreground).Color));
            }
            SaveCrop(target, toolTip, file);
            Assert.True(TopLevel.GetTopLevel(toolTip) is not null, $"{what}: the tooltip closed before its frame was drawn.");
            ToolTip.SetIsOpen(list, false);
            ToolTip.SetTip(list, null);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Color ColorOf(string key) => Application.Current!.TryGetResource(key, null, out var v) switch
    {
        true when v is Color c => c,
        true when v is ISolidColorBrush b => b.Color,
        _ => throw new KeyNotFoundException(key),
    };

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 1500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
