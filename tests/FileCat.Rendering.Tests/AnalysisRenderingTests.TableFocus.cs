using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task TablePublicationPreservesKeyboardRoutingWithoutTakingFocusFromAnEditor()
    {
        foreach (int pointerX in new[] { 20, 65 })
        {
            using var styles = new ProductStyles(); using var model = new DiskSpaceViewModel(Fixture());
            await model.RebuildAsync(true); var prepared = model.Tree;
            var window = new DiskSpaceWindow { Height = 960 }; window.AddSession(model); window.Show();
            try
            {
                await Until(() => !ReferenceEquals(model.Tree, prepared));
                void Frame() { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); }
                Frame();
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(c => AutomationProperties.GetName(c) == "Ranked analysis entries");
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
                var point = row.TranslatePoint(new Point(pointerX, row.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point); window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Assert.Equal("n:1", model.Selection.Focus); Assert.True(list.IsKeyboardFocusWithin);
                Assert.Equal(pointerX == 20, window.FocusManager?.GetFocusedElement() is CheckBox);
                // A completed scan can still publish its final projection after the first row is clicked.
                // Replacing the recorded row must leave keyboard commands routed through the table.
                model.Metric = AnalysisMetric.FileSize; await model.RebuildAsync(true); Frame();
                Assert.Equal("n:1", model.Selection.Focus); Assert.True(list.IsKeyboardFocusWithin);
                Assert.Equal(pointerX == 20, window.FocusManager?.GetFocusedElement() is CheckBox);
                bool marked = model.Selection.Marks.Contains(1ul);
                window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
                Assert.Equal(!marked, model.Selection.Marks.Contains(1ul));
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await Until(() => model.Selection.Scope == "n:1"); Frame();
                var editor = window.GetVisualDescendants().OfType<TextBox>().Single(c => c.IsEffectivelyVisible && c.PlaceholderText == "Search within observations…");
                Assert.True(editor.Focus()); model.Sort = AnalysisTableSort.Name;
                await model.RebuildAsync(true); Frame(); Assert.Same(editor, window.FocusManager?.GetFocusedElement());
            }
            finally { window.ForceClose(); }
        }
    }
}
