using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePrimaryPreviewUsesBoundBytesAndRefusesChangedContent(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string input = Path.Combine(Path.GetTempPath(), "filecat-dsa-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(input);
        string file = Path.Combine(input, "reviewed.txt");
        const string afterLimit = "Content beyond the preview limit.";
        byte[] payload = Encoding.UTF8.GetBytes("Reviewed text \u202E with a directional mark.\n" +
            new string('x', AnalysisPrimaryContentTarget.PreviewBytes) + afterLimit);
        File.WriteAllBytes(file, payload);

        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool) { Width = 1080, Height = 800 };
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            Frame(window);
            ListBox table;
            if (indexed)
            {
                table = IndexedList(window);
                table.SelectedItem = Assert.Single(table.Items.OfType<AnalysisHierarchyRow>(), row => row.Path.Kind == AnalysisKind.Directory);
                Click(window, "Explore selected folder");
                await Until(() => table.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "reviewed.txt"), () => ComparisonText(window));
                table.SelectedItem = Assert.Single(table.Items.OfType<AnalysisHierarchyRow>(), row => row.Path.Name == "reviewed.txt");
                await Until(() => IndexedDetails(window).Text?.Contains("reviewed.txt", StringComparison.Ordinal) == true, () => ComparisonText(window));
            }
            else
            {
                var model = opened.Memory!;
                var snapshot = model.Tree!.Snapshot;
                var entry = Assert.Single(snapshot.Nodes, row => row.Name == "reviewed.txt");
                model.Focus(AnalysisTree.Key(entry.ParentId), open: true);
                await Until(() => model.Rows.Any(row => row.NodeId == entry.Id), () => ComparisonText(window));
                model.Focus(AnalysisTree.Key(entry.Id));
                table = Assert.Single(window.GetVisualDescendants().OfType<ListBox>());
            }
            Frame(window);
            Assert.True(table.Focus());
            Assert.Null(Preview());
            PressPreview();
            if (!OperatingSystem.IsWindows())
            {
                await Until(() => ComparisonText(window).Contains("Current content preview is unavailable for this source", StringComparison.Ordinal),
                    () => ComparisonText(window));
                Assert.Null(Preview());
                Assert.Equal(payload, File.ReadAllBytes(file));
                return;
            }
            await Until(() => Preview()?.Text?.StartsWith("Reviewed text \\u202E", StringComparison.Ordinal) == true, () => ComparisonText(window));
            var preview = Preview()!;
            Assert.True(preview.IsReadOnly);
            Assert.DoesNotContain(afterLimit, preview.Text);
            Assert.Contains($"First {AnalysisPrimaryContentTarget.PreviewBytes:N0} of {payload.Length:N0} bytes", ComparisonText(window));
            Assert.DoesNotContain('\u202E', preview.Text!);
            Click(window, "Close preview");
            await Until(() => Preview() is null);
            Assert.Equal(payload, File.ReadAllBytes(file));

            File.WriteAllText(file, "The file changed after the scan.");
            Assert.True(table.Focus());
            PressPreview();
            await Until(() => ComparisonText(window).Contains("Preview unavailable", StringComparison.Ordinal), () => ComparisonText(window));
            Assert.Null(Preview());
            Assert.Equal("The file changed after the scan.", File.ReadAllText(file));
        }
        finally
        {
            window.ForceClose();
            await window.WaitForIndexedCloseAsync();
            Directory.Delete(input, recursive: true);
            await AssertAllRuntimePlacesAvailableAsync(pool);
        }

        TextBox? Preview() => ComparisonControl<TextBox>(window, "Live primary file preview");
        void PressPreview()
        {
            window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            window.KeyRelease(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
            Frame(window);
        }
    }
}
