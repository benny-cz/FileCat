using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task ExcludedFolderInspectorExplainsCapturedBoundaryInCompactView()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        using var styles = new ProductStyles();
        string input = Directory.CreateDirectory(Path.Combine(fixture.Root, "input")).FullName;
        Directory.CreateDirectory(Path.Combine(input, "Cache"));
        File.WriteAllText(Path.Combine(input, "visible.txt"), "visible");
        using var model = new DiskSpaceViewModel(fixture.Services) { Chart = "Table" };
        var window = new DiskSpaceWindow { Width = 380, Height = 430, FontSize = 20 };
        window.AddSession(model, [input]);
        window.Show();
        try
        {
            await model.StartAsync([input], [new(0, "Cache")]);
            await Until(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished, timeout: TimeSpan.FromSeconds(20));
            var snapshot = model.Tree!.Snapshot;
            var root = Assert.Single(snapshot.Nodes, node => node.ParentId == 0);
            var excluded = Assert.Single(snapshot.Nodes, node => node.Name == "Cache");
            model.NavigateToScope(AnalysisTree.Key(root.Id));
            model.Focus(AnalysisTree.Key(excluded.Id));
            _ = model.FocusedDisplayPath(out var boundary);
            Assert.Equal("This folder and its contents were skipped under the captured folder exclusion rules.", boundary);
            Frame(window);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible &&
                text.Text == "Value" && AutomationLabel(text) == "Active metric");
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
