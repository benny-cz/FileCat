using System.Numerics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.Commands;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public void CleanupReviewTotalsSeparateBasisCoverageAndOverlappingComponents()
    {
        var source = Fixture();
        var size = AnalysisMeasurement.Known(4);
        var unknown = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        source = source with
        {
            Nodes =
            [
                new(1, "r", 0, "folder", AnalysisKind.Directory),
                new(2, "r", 1, "file", AnalysisKind.File, "a"),
                new(3, "r", 1, "alias", AnalysisKind.File, "a"),
                new(4, "r", 1, "partial", AnalysisKind.File, "b"),
                new(5, "r", 1, "identity-unknown", AnalysisKind.File, "c"),
                new(6, "r", 1, "excluded", AnalysisKind.File, "c") { IncludedByPolicy = false },
                new(8, "r", 1, "ambiguous", AnalysisKind.File, "d")
            ],
            Objects =
            [
                new("a", "native-a", size, AnalysisMeasurement.Known(2)),
                new("b", "native-b", unknown, AnalysisMeasurement.Known(1)),
                new("c", null, AnalysisMeasurement.Known(2), unknown),
                new("d", "native-d", AnalysisMeasurement.Known(5), unknown),
                new("outside", "native-d", AnalysisMeasurement.Known(10), unknown)
            ],
            Facts =
            [
                Fact("a", "total", "apfs", AnalysisMeasurement.Known(128), "fork"),
                Fact("a", "fork", "apfs-fork", AnalysisMeasurement.Known(32)),
                Fact("b", "primary", "ntfs", AnalysisMeasurement.Known(9)),
                Fact("b", "named", "ntfs-named", unknown),
                Fact("c", "total", "apfs", AnalysisMeasurement.Known(90)),
                Fact("d", "primary", "ntfs", AnalysisMeasurement.Known(120))
            ]
        };
        var totals = AnalysisMemoryCleanupTotals.Read(source, [1, 2, 3, 4, 5, 6, 7, 8], TestContext.Current.CancellationToken);
        Assert.Equal(8, totals.Selected);
        Assert.Equal(new BigInteger(15), totals.LogicalBytes);
        Assert.Equal(4, totals.MeasuredFiles);
        Assert.Equal(1, totals.UnknownSizes);
        Assert.Equal(1, totals.Excluded);
        Assert.Equal(1, totals.Folders);
        Assert.Equal(2, totals.MeasuredAllocationObjects);
        Assert.Equal(3, totals.IncompleteAllocationPaths);
        Assert.Equal(new AnalysisCleanupAllocation("apfs", "total", new BigInteger(128)), totals.Allocation[0]);
        Assert.Equal(new AnalysisCleanupAllocation("ntfs", "primary", new BigInteger(9)), totals.Allocation[1]);
        Assert.Equal(2, totals.Allocation.Length);

        StorageFact Fact(string owner, string component, string basis, AnalysisMeasurement measurement, params string[] includes) =>
            new(owner + "-" + component, owner, null, component, basis, measurement, "fixture", source.Identity.ObservedUtc, [.. includes]);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCleanupReviewSeparatesBasketFromShortcutSelectionAndKeepsFiles(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "cleanup-review");
        Directory.CreateDirectory(input);
        for (int i = 0; i < 39; i++)
            File.WriteAllBytes(Path.Combine(input, $"retain-{i:D2}.bin"), [4, 8, 12, (byte)i]);
        CreateGroupedNativeAlias(Path.Combine(input, "retain-38.bin"), Path.Combine(input, "retain-39.bin"));

        var pool = new AnalysisSessionAdmission();
        var keymap = new Keymap(CommandRegistry.CreateDefault(), new Dictionary<string, string[]>
        {
            [CommandIds.DiskSpaceReviewCancel] = ["Ctrl+R"]
        });
        var window = new DiskSpaceWindow(null, () => keymap, pool) { Width = 1080, Height = 800 };
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            var model = opened.Memory;
            Frame(window);
            Click(window, "Mark matching files in this scope");
            await Until(() => Counts(40, 0), () => ComparisonText(window));
            if (indexed) Click(window, "Add to review basket");
            else model!.StageSelection();
            await Until(() => Counts(40, 40));
            Click(window, "Clear marks");
            await Until(() => Counts(0, 40));

            if (indexed)
            {
                var table = IndexedList(window);
                var before = table.ItemsSource;
                var query = ComparisonControl<TextBox>(window, "Filter indexed observations")!;
                query.Text = "name:retain-00.bin";
                Click(window, "Apply filter");
                await Until(() => !ReferenceEquals(before, table.ItemsSource), () => ComparisonText(window));
            }
            else
            {
                model!.Query = "name:retain-00.bin";
                await model.RebuildAsync(true);
            }
            Click(window, "Mark matching files in this scope");
            await Until(() => Counts(1, 40), () => ComparisonText(window));
            Assert.True((indexed ? IndexedList(window) : window.GetVisualDescendants().OfType<ListBox>().Single()).Focus());

            Press(Key.F8, PhysicalKey.F8);
            var list = await EntriesAsync(1);
            Assert.Contains("retain-00.bin", list.Items[0]!.ToString());
            Assert.Contains("Marked entries · 1 path · 0 included · 1 kept", ComparisonText(window));
            AssertDefaultAndDisabled("Recycle included entries");
            var action = ComparisonControl<ComboBox>(window, "Cleanup action")!;
            action.SelectedIndex = 1;
            Frame(window);
            Assert.Contains("Reviewed permanent deletion is unavailable", ComparisonText(window));
            AssertDefaultAndDisabled("Permanently delete included entries");
            list.SelectedIndex = 0;
            Click(window, "Inspect recorded entry");
            await Until(() => ComparisonControl<TextBlock>(window, "Recorded cleanup entry details")?.Text?.Contains("Recorded file size · 4 bytes", StringComparison.Ordinal) == true,
                () => ComparisonText(window));
            string evidence = ComparisonControl<TextBlock>(window, "Recorded cleanup entry details")!.Text!;
            Assert.Contains("Recorded path · ", evidence);
            Assert.Contains("cleanup-review/retain-00.bin", evidence);
            Assert.Contains("Reported allocation", evidence);
            Press(Key.R, PhysicalKey.R, RawInputModifiers.Control);
            await Until(() => ComparisonControl<ListBox>(window, "Cleanup review entries") is null);

            Press(Key.F5, PhysicalKey.F5);
            await EntriesAsync(1);
            Assert.Contains("Copy to a new destination", ComparisonText(window));
            AssertDefaultAndDisabled("Copy included entries");
            Click(window, "Cancel / Keep files");
            Press(Key.F6, PhysicalKey.F6);
            await EntriesAsync(1);
            Assert.Contains("Move to a new destination", ComparisonText(window));
            AssertDefaultAndDisabled("Move included entries");
            Click(window, "Cancel / Keep files");

            Click(window, "Review basket actions…");
            list = await EntriesAsync(32);
            await Until(() => ComparisonText(window).Contains("39 hidden by filters", StringComparison.Ordinal), () => ComparisonText(window));
            await Until(() => ComparisonControl<TextBlock>(window, "Recorded cleanup selection totals")?.Text is { } totals &&
                totals.Contains("160 bytes across 40 measured file paths", StringComparison.Ordinal) &&
                totals.Contains("39 distinct verified objects measured", StringComparison.Ordinal), () => ComparisonText(window));
            Assert.Contains("Review basket · 40 paths · 0 included · 40 kept", ComparisonText(window));
            var firstPage = list.Items.Cast<object>().Select(row => row.ToString()!).ToHashSet(StringComparer.Ordinal);
            Click(window, "Next review entries");
            await Until(() => list.ItemCount == 8);
            Assert.DoesNotContain(list.Items.Cast<object>(), row => firstPage.Contains(row.ToString()!));
            Assert.Contains("Entries 33–40 of 40", ComparisonText(window));
            Click(window, "Previous review entries");
            await Until(() => list.ItemCount == 32);
            Assert.True(firstPage.SetEquals(list.Items.Cast<object>().Select(row => row.ToString()!)));
            Click(window, "Cancel / Keep files");

            Assert.True(Counts(1, 40));
            Assert.Equal(40, Directory.GetFiles(input).Length);
            for (int i = 0; i < 40; i++)
                Assert.Equal(new byte[] { 4, 8, 12, (byte)Math.Min(i, 38) }, File.ReadAllBytes(Path.Combine(input, $"retain-{i:D2}.bin")));

            bool Counts(int marks, int basket) => indexed ? IndexedSelectionCount(window, marks, basket) :
                model!.Selection.Marks.Count == marks && model.Selection.Basket.Count == basket;
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);

        void Press(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            window.KeyPress(key, modifiers, physical, null);
            window.KeyRelease(key, modifiers, physical, null);
            Frame(window);
        }

        async Task<ListBox> EntriesAsync(int count)
        {
            await Until(() => ComparisonControl<ListBox>(window, "Cleanup review entries")?.ItemCount == count, () => ComparisonText(window));
            Frame(window);
            return ComparisonControl<ListBox>(window, "Cleanup review entries")!;
        }

        void AssertDefaultAndDisabled(string confirmation)
        {
            var keep = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Cancel / Keep files"));
            var destructive = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && Equals(button.Content, confirmation));
            Assert.True(keep.IsDefault);
            Assert.False(destructive.IsEnabled);
            Assert.False(destructive.IsDefault);
        }
    }
}
