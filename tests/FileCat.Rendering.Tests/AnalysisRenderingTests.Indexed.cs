using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using FileCat.App.Controls.DiskSpace;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task IndexedTableChangesDirectionAndLogicalOrderWithoutLosingCapturedFocus()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); var explorer = await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
            Click(window, "Explore selected folder"); await Until(() => IndexedReady(window, explorer, 1)); Frame(window);
            var order = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "Indexed table order");
            var direction = window.GetVisualDescendants().OfType<CheckBox>().Single(check => AutomationProperties.GetName(check) == "Indexed descending table order");
            order.SelectedIndex = 2;
            await Until(() => explorer.Page?.Sort == AnalysisHierarchySort.FileSize && IndexedReady(window, explorer, 1)); Frame(window);
            var before = list.Items.OfType<AnalysisHierarchyRow>().ToArray();
            Assert.True(before.Zip(before.Skip(1)).All(pair => pair.First.Logical.KnownBytes >= pair.Second.Logical.KnownBytes));
            var selected = before.Single(row => row.Path.Kind == AnalysisKind.Directory); list.SelectedItem = selected;
            await Until(() => explorer.Inspection?.Row.Path.Id == selected.Path.Id);
            direction.IsChecked = false;
            await Until(() => explorer.Page?.Descending == false && IndexedReady(window, explorer, 1)); Frame(window);
            // Focus remains on its captured identity even when the new first page omits that row.
            Assert.Null(list.SelectedItem); Assert.Equal(selected.Path.Id, explorer.Inspection!.Row.Path.Id);
            var after = list.Items.OfType<AnalysisHierarchyRow>().ToArray();
            Assert.True(after.Zip(after.Skip(1)).All(pair => pair.First.Logical.KnownBytes <= pair.Second.Logical.KnownBytes));
            Assert.Equal(AnalysisMetric.Files, explorer.Metric);
            direction.IsChecked = true;
            await Until(() => explorer.Page?.Descending == true && IndexedReady(window, explorer, 1)); Frame(window);
            Assert.Equal(selected.Path.Id, Assert.IsType<AnalysisHierarchyRow>(list.SelectedItem).Path.Id);
            direction.IsChecked = false;
            await Until(() => explorer.Page?.Descending == false && IndexedReady(window, explorer, 1));
            // Query replacement retains the requested table field/direction in its complete new page.
            Click(window, "Apply filter");
            await Until(() => list.ItemCount == 1 && list.Items.OfType<AnalysisHierarchyRow>().Single().Path.Id == 1); Frame(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()); Click(window, "Explore selected folder");
            await Until(() => list.ItemCount == 128); Frame(window);
            var replacement = list.Items.OfType<AnalysisHierarchyRow>().ToArray();
            Assert.True(replacement.Zip(replacement.Skip(1)).All(pair => pair.First.Logical.KnownBytes <= pair.Second.Logical.KnownBytes));
            Assert.DoesNotContain(replacement, row => row.Path.Kind == AnalysisKind.Directory);
            order.SelectedIndex = 3;
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith("Name · Ascending", StringComparison.Ordinal) == true)); Frame(window);
            var names = list.Items.OfType<AnalysisHierarchyRow>().Select(row => row.Path.Name).ToArray();
            Assert.Equal(names.Order(StringComparer.OrdinalIgnoreCase), names);
            order.SelectedIndex = 4;
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith("Metadata state · Ascending", StringComparison.Ordinal) == true)); Frame(window);
            Assert.All(list.Items.OfType<AnalysisHierarchyRow>(), row => Assert.Equal(MeasurementState.Known, row.Path.MetadataState));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedTabPagesAndInspectsCapturedNativeMetadataAfterSourceClosure()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); var explorer = await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken);
        await fixture.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); var root = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
            Assert.Equal(1UL, root.Path.Id); list.SelectedItem = root; Click(window, "Explore selected folder");
            await Until(() => IndexedReady(window, explorer, 1)); Frame(window);
            Assert.Equal(128, list.ItemCount); Assert.True(explorer.Page!.Children.HasMore);
            var firstIds = list.Items.OfType<AnalysisHierarchyRow>().Select(row => row.Path.Id).ToArray();
            Click(window, "Next entries"); await Until(() => explorer.Page!.Children.Offset != 0 && IndexedReady(window, explorer, 1)); Frame(window);
            Assert.DoesNotContain(list.Items.OfType<AnalysisHierarchyRow>(), row => firstIds.Contains(row.Path.Id));
            var selected = list.Items.OfType<AnalysisHierarchyRow>().First(); list.SelectedItem = selected;
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(text => AutomationProperties.GetName(text) == "Captured metadata inspector" && text.Text?.Contains("captured path ID: " + selected.Path.Id, StringComparison.Ordinal) == true)); Frame(window);
            var inspector = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Captured metadata inspector");
            Assert.Contains("captured path ID: " + selected.Path.Id, inspector.Text); Assert.True(inspector.IsReadOnly);
            Click(window, "Focus inspector"); Frame(window); Assert.Same(inspector, window.FocusManager?.GetFocusedElement());
            Click(window, "Previous entries"); await Until(() => explorer.Page!.Children.Offset == 0 && IndexedReady(window, explorer, 1)); Frame(window);
            Assert.Equal(firstIds, list.Items.OfType<AnalysisHierarchyRow>().Select(row => row.Path.Id));
            var folder = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Kind == AnalysisKind.Directory);
            list.SelectedItem = folder; Click(window, "Explore selected folder"); await Until(() => IndexedReady(window, explorer, folder.Path.Id));
            Click(window, "Back"); await Until(() => IndexedReady(window, explorer, 1));
            Click(window, "Forward"); await Until(() => IndexedReady(window, explorer, folder.Path.Id));
            Click(window, "Parent"); await Until(() => IndexedReady(window, explorer, 1));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Null(explorer.Page); Assert.Null(explorer.Chart); Assert.Empty(Directory.GetFiles(fixture.PrivatePath));
    }

    [AvaloniaFact]
    public async Task IndexedTabReplacesOnlyCompleteValidQueriesAndKeepsTemporaryCloseReview()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.OnDisk, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window);
            var editor = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Filter indexed observations");
            Assert.True(editor.Focus()); editor.Text = "name:match-target.bin"; Click(window, "Apply filter");
            await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Single().Logical.Files == 1); Frame(window);
            Assert.Same(editor, window.FocusManager?.GetFocusedElement());
            var shown = list.ItemsSource; editor.Text = "size:nope"; Click(window, "Apply filter");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => AutomationProperties.GetName(text) == "Indexed analysis status" && text.Text?.StartsWith("The previous view remains displayed.", StringComparison.Ordinal) == true));
            Assert.Same(shown, list.ItemsSource);
            Click(window, "Clear filter"); await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Single().Logical.Files == 303);
            window.Close(); Frame(window); Assert.True(window.IsVisible);
            var keep = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Content as string == "Keep open");
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); keep.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Frame(window);
            Assert.True(window.IsVisible); Assert.Equal(1, window.SessionCount);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(fixture.Payload, File.ReadAllBytes(fixture.PayloadPath));
    }

    [AvaloniaFact]
    public async Task IndexedChartsRenderFromTheSameBoundedViewAcrossThemesAndNarrowLayouts()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken);
        window.Show();
        string output = Path.Combine(AppContext.BaseDirectory, "analysis-indexed-renders"); Directory.CreateDirectory(output);
        try
        {
            foreach (string theme in new[] { "Classic", "ClassicDark", "HighContrast", "Cyberpunk" })
            foreach (int width in new[] { 1180, 380 })
            {
                ThemeManager.Apply(theme); window.Width = width; window.Height = 780; window.FontSize = width == 380 ? 20 : 14;
                var mode = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "Indexed chart mode");
                for (int index = 0; index < 3; index++)
                {
                    mode.SelectedIndex = index; Frame(window);
                    if (width == 1180)
                    {
                        var inspector = window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Captured metadata inspector");
                        var list = IndexedList(window); Assert.True(inspector.TranslatePoint(default, window)!.Value.X > list.TranslatePoint(default, window)!.Value.X + list.Bounds.Width);
                    }
                    Assert.Contains(window.GetVisualDescendants().OfType<ListBox>(), list => list.IsEffectivelyVisible && AutomationProperties.GetName(list) == "Indexed analysis entries");
                    using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                    frame.Save(Path.Combine(output, $"{theme}-{width}-{index}.png"), new PngBitmapEncoderOptions());
                }
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    private static void Frame(Window window)
    { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); }
    private static ListBox IndexedList(Window window) => window.GetVisualDescendants().OfType<ListBox>().Single(list => AutomationProperties.GetName(list) == "Indexed analysis entries");
    private static bool IndexedReady(Window window, AnalysisDatasetExplorer explorer, ulong parent) => explorer.Page?.ParentId == parent &&
        window.GetVisualDescendants().OfType<TextBlock>().Any(text => AutomationProperties.GetName(text) == "Indexed analysis status" &&
            (text.Text?.StartsWith("Complete under captured policy.", StringComparison.Ordinal) == true || text.Text?.StartsWith("Partial namespace observations;", StringComparison.Ordinal) == true));
    private static void Click(Window window, string label) => window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Content as string == label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [AvaloniaFact]
    public async Task IndexedQueryPublicationWaitsForTheCapturedChartGestureToEnd()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync();
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window); var chart = window.GetVisualDescendants().OfType<TreemapControl>().Single();
        Point point = chart.TranslatePoint(new Point(15, 15), window)!.Value; bool pressed = false;
        try
        {
            var list = IndexedList(window); var original = list.ItemsSource;
            window.MouseMove(point); window.MouseDown(point, MouseButton.Left); pressed = true;
            window.GetVisualDescendants().OfType<TextBox>().Single(text => AutomationProperties.GetName(text) == "Filter indexed observations").Text = "name:match-target.bin";
            Click(window, "Apply filter");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Query ready. Finish the current interaction to display it."));
            Assert.Same(original, list.ItemsSource); Assert.Equal(303UL, Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()).Logical.Files);
            window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "Indexed table order").SelectedIndex = 2;
            window.GetVisualDescendants().OfType<CheckBox>().Single(check => AutomationProperties.GetName(check) == "Indexed descending table order").IsChecked = false;
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Query ready. Finish the current interaction to display it."));
            Assert.Same(original, list.ItemsSource);
            window.MouseUp(point, MouseButton.Left); pressed = false;
            await Until(() => Assert.Single(list.Items.OfType<AnalysisHierarchyRow>()).Logical.Files == 1);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.EndsWith("File size · Ascending", StringComparison.Ordinal) == true);
        }
        finally { if (pressed) window.MouseUp(point, MouseButton.Left); window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedTabExplainsIdentityUnverifiedAmountsInsteadOfPresentingThemAsUniqueArea()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var fixture = await IndexedFixture.CreateAsync(unverified: true);
        var window = new DiskSpaceWindow(); await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.UniqueFileSize, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.Contains("identity-unverified path bytes are excluded from unique chart area", StringComparison.Ordinal) == true);
            var metric = window.GetVisualDescendants().OfType<ComboBox>().Single(combo => AutomationProperties.GetName(combo) == "Indexed metric");
            metric.SelectedIndex = (int)AnalysisMetric.OnDisk;
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.Contains("reported for paths with unverified identities; aliases may repeat", StringComparison.Ordinal) == true));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    private sealed class IndexedFixture : IAsyncDisposable
    {
        private readonly DeviceIoScheduler _io = new();
        private readonly string _parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FileCatValidation");
        internal string Root { get; }
        internal string PrivatePath => Path.Combine(Root, "private");
        internal string PayloadPath => Path.Combine(Root, "source", "folder", "match-target.bin");
        internal byte[] Payload { get; } = [1, 2, 3];
        internal AnalysisDatasetLease? Lease;
        private IndexedFixture() { Root = Path.Combine(_parent, "indexed-window-" + Guid.NewGuid().ToString("N")); }
        internal static async Task<IndexedFixture> CreateAsync(bool unverified = false, bool namespaceEntry = false, bool sharedAlias = false, int extraExtensions = 0)
        {
            var fixture = new IndexedFixture(); Directory.CreateDirectory(Path.Combine(fixture.Root, "source", "folder"));
            File.WriteAllBytes(fixture.PayloadPath, fixture.Payload); File.WriteAllBytes(Path.Combine(fixture.Root, "source", "folder", "second.bin"), [9]);
            for (int index = 0; index < 300; index++) File.WriteAllBytes(Path.Combine(fixture.Root, "source", $"file-{index:D4}.bin"), [7]);
            File.WriteAllBytes(Path.Combine(fixture.Root, "source", "bidi\u202E.bin"), [8]);
            for (int index = 0; index < extraExtensions; index++) File.WriteAllBytes(Path.Combine(fixture.Root, "source", $"category-{index:D4}.ext{index:D4}"), [6]);
            if (sharedAlias) CreateGroupedNativeAlias(fixture.PayloadPath, Path.Combine(fixture.Root, "source", "alias.jpg"));
            string device = PathUtil.GetDeviceKey(fixture.Root); AnalysisPrivateDirectory? storage = null; IAnalysisBackend? source = null; AnalysisSession? session = null;
            bool completed = false;
            try
            {
                storage = await AnalysisIo.Run(fixture._io, device, _ => OperatingSystem.IsWindows() ? (AnalysisPrivateDirectory)new WindowsAnalysisPrivateDirectory(fixture.Root, "private") : new UnixAnalysisPrivateDirectory(fixture.Root, "private"), TestContext.Current.CancellationToken);
                source = await AnalysisIo.Run(fixture._io, device, _ => unverified || namespaceEntry ? new UnverifiedFiles(Prepare(Path.Combine(fixture.Root, "source")), unverified, namespaceEntry) : Prepare(Path.Combine(fixture.Root, "source")), TestContext.Current.CancellationToken);
                session = new AnalysisCoordinator(fixture._io, "indexed-window-fixture").Start([source], storage: new(device,
                    () => Interlocked.Exchange(ref storage, null) ?? throw new InvalidOperationException("Private owner transferred."), [new(device, () => unverified || namespaceEntry ? new UnverifiedFiles(Prepare(fixture.PrivatePath), unverified) : Prepare(fixture.PrivatePath))], PreviewPaths: 2)); source = null;
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
                Assert.Equal(AnalysisPhase.Finished, session.Snapshot().Phase); Assert.Equal(0UL, session.SourceCloseFailures);
                fixture.Lease = await session.AcquireDatasetAsync(TestContext.Current.CancellationToken); Assert.NotNull(fixture.Lease); Assert.Equal(305UL + (namespaceEntry ? 1UL : 0UL) + (sharedAlias ? 1UL : 0UL) + (ulong)extraExtensions, fixture.Lease.Descriptor.Paths); completed = true; return fixture;
            }
            finally
            {
                if (session is not null) { session.Dispose(); await session.DatasetCloseCompletion; }
                if (source is not null) await AnalysisIo.ReleaseAsync(fixture._io, source);
                if (storage is not null) await AnalysisIo.Run(fixture._io, device, _ => { storage.Dispose(); return true; }, TestContext.Current.CancellationToken);
                if (!completed) await fixture.DisposeAsync();
            }
        }
        private static IAnalysisBackend Prepare(string path) => OperatingSystem.IsWindows() ? new WindowsAnalysisBackend(path) : new UnixAnalysisBackend(path);
        private sealed class UnverifiedFiles(IAnalysisBackend inner, bool unverified = true, bool namespaceEntry = false) : IAnalysisBackend
        {
            public RootBinding Root => inner.Root;
            public AnalysisEntry RootEntry => inner.RootEntry;
            public string Basis => inner.Basis;
            public string IoDeviceKey => inner.IoDeviceKey;
            public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation)
            {
                foreach (var entry in inner.Children(directory, cancellation)) yield return unverified && entry.Kind == AnalysisKind.File ? entry with { Identity = null } : entry;
                if (namespaceEntry && directory.RelativeName.IsEmpty)
                {
                    const string name = "namespace-link";
                    byte[] raw = OperatingSystem.IsWindows() ? System.Text.Encoding.Unicode.GetBytes(name) : System.Text.Encoding.UTF8.GetBytes(name);
                    yield return new([], name, [.. raw], AnalysisKind.Link, null, AnalysisMeasurement.Missing(MeasurementState.Unsupported),
                        AnalysisMeasurement.Missing(MeasurementState.Unsupported), AnalysisMeasurement.Missing(MeasurementState.Unsupported), null, "", NameEncoding: OperatingSystem.IsWindows() ? "utf16le" : "utf8");
                }
            }
            public void ValidateReadScope(AnalysisEntry directory) => inner.ValidateReadScope(directory);
            private IAnalysisBackend Inner => inner;
            public AnalysisRootContainment ContainsRoot(IAnalysisBackend other) => inner.ContainsRoot(other is UnverifiedFiles weak ? weak.Inner : other);
            public void Dispose() => inner.Dispose();
        }
        internal async Task ReleaseInitialLeaseAsync() { if (Lease is { } lease) { Lease = null; await lease.DisposeAsync(); } }
        public async ValueTask DisposeAsync()
        {
            await ReleaseInitialLeaseAsync(); _io.Dispose();
            if (Directory.Exists(Root)) { Assert.Equal(_parent, Path.GetDirectoryName(Path.GetFullPath(Root))); Directory.Delete(Root, true); }
        }
    }
}
