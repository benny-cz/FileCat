using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentSourceReviewShowsTheExplicitChoiceAndDefaultsToCancel(bool approve)
    {
        using var styles = new ProductStyles();
        using var ui = new HistoryDialogFixture(380, 430, 20);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        string chosen = Path.Combine(Path.GetTempPath(), "current-item.bin");
        var pending = DiskSpaceWindow.ReviewCurrentSourceAsync(ui.Dialogs, "recorded-\u202E.bin", chosen, cancel.Token);
        await Until(() => ui.Dialogs.IsOpen);
        Frame(ui.Window);
        var keep = ui.Find("Cancel");
        Assert.True(keep.IsDefault && keep.IsCancel);
        Assert.Same(keep, ui.Window.FocusManager?.GetFocusedElement());
        Assert.False(ui.Find("Analyze current selection").IsDefault);
        string text = string.Join('\n', ui.Window.GetVisualDescendants().OfType<TextBlock>().Where(label => label.IsEffectivelyVisible).Select(label => label.Text));
        Assert.Contains("recorded-\\u202E.bin", text);
        Assert.Contains(chosen, text);
        Assert.Contains("new Standard analysis", text);
        ui.Click(approve ? "Analyze current selection" : "Cancel");
        Assert.Equal(approve, await pending);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentSourceBindingScansOnlyTheExplicitlyChosenItemAfterReview(bool folder)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string absent = Path.Combine(fixture.Root, "recorded-source-does-not-exist");
        string chosen = Path.Combine(fixture.Root, folder ? "current folder" : "current picked.txt");
        string payload = folder ? Path.Combine(Directory.CreateDirectory(chosen).FullName, "payload.txt") : chosen;
        byte[] content = [1, 2, 3];
        File.WriteAllBytes(payload, content);
        var recorded = Fixture();
        recorded = recorded with { Origin = AnalysisOrigin.ImportedInventory, Taint = AnalysisTaint.ImportedUnverified | AnalysisTaint.OrdinarySensitive,
            Roots = [recorded.Roots[0] with { CanonicalLocator = absent }] };
        AnalysisSnapshotLease Capture() => new(recorded, () => { }, Capture);
        using var model = new DiskSpaceViewModel(Capture(), fixture.Services, memoryFallback: false);
        var window = new DiskSpaceWindow { Width = 1180, Height = 780 };
        var picker = DispatchProxy.Create<IStorageProvider, CurrentSourcePickerProxy>();
        var proxy = (CurrentSourcePickerProxy)(object)picker;
        proxy.Choice = chosen;
        typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, picker);
        window.AddSession(model); window.Show();
        try
        {
            bool Visible(string title) => window.GetVisualDescendants().OfType<Button>()
                .Any(button => button.IsEffectivelyVisible && button.IsEnabled && button.Content as string == title);
            await model.RebuildAsync(true);
            await Until(() => model.Tree is not null);
            model.Focus(folder ? "n:2" : "n:4"); Frame(window);
            await Until(() => Visible("Bind current source…"));
            Click(window, "Bind current source…");
            await Until(() => Visible("Analyze current selection"));
            Click(window, "Cancel");
            await Until(() => proxy.DisposedItems == 1 && Visible("Bind current source…"));
            Assert.Equal(1, window.SessionCount);
            Click(window, "Bind current source…");
            await Until(() => Visible("Analyze current selection"));
            Click(window, "Analyze current selection");
            string completion = folder ? "Finished · 2 observed paths" : "Finished · 1 observed paths";
            await Until(() => window.SessionCount == 2 && proxy.DisposedItems == 2 && window.GetVisualDescendants().OfType<TextBlock>()
                .Any(label => label.IsEffectivelyVisible && label.Text?.StartsWith(completion, StringComparison.Ordinal) == true),
                timeout: TimeSpan.FromSeconds(30));
            Assert.Equal(2, proxy.PickerCalls);
            Assert.Equal(AnalysisOrigin.ImportedInventory, model.Tree!.Snapshot.Origin);
            Assert.Equal(absent, model.Tree.Snapshot.Roots.Single().CanonicalLocator);
            Assert.False(Directory.Exists(absent));
            Assert.Equal(content, File.ReadAllBytes(payload));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task CurrentSourceBindingRefusesAChangedItemType()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        string changed = Directory.CreateDirectory(Path.Combine(fixture.Root, "file replaced by folder")).FullName;
        using var model = DiskSpaceViewModel.ForCurrentSource(fixture.Services, AnalysisKind.File);
        await model.StartAsync([changed]);
        Assert.Null(model.Tree);
        Assert.Contains("changed type", model.Status);
        Assert.Empty(Directory.GetFileSystemEntries(changed));
    }

    public class CurrentSourcePickerProxy : DispatchProxy
    {
        public string Choice = "";
        public int PickerCalls, DisposedItems;
        private Uri? _path;
        private Action? _dispose;

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "OpenFilePickerAsync" => Task.FromResult<IReadOnlyList<IStorageFile>>([Pick<IStorageFile>(args![0]!)]),
            "OpenFolderPickerAsync" => Task.FromResult<IReadOnlyList<IStorageFolder>>([Pick<IStorageFolder>(args![0]!)]),
            "get_Path" => _path,
            "Dispose" => DisposeItem(),
            _ => throw new InvalidOperationException("Unexpected storage picker call: " + method?.Name)
        };

        private T Pick<T>(object options) where T : class, IStorageItem
        {
            var (suggested, multiple) = options switch
            {
                FilePickerOpenOptions file => (file.SuggestedStartLocation, file.AllowMultiple),
                FolderPickerOpenOptions folder => (folder.SuggestedStartLocation, folder.AllowMultiple),
                _ => throw new InvalidOperationException("Unexpected picker options.")
            };
            Assert.Null(suggested);
            Assert.False(multiple);
            PickerCalls++;
            var selected = Create<T, CurrentSourcePickerProxy>();
            var item = (CurrentSourcePickerProxy)(object)selected;
            item._path = new Uri(Choice);
            item._dispose = () => DisposedItems++;
            return selected;
        }

        private object? DisposeItem() { _dispose?.Invoke(); return null; }
    }
}
