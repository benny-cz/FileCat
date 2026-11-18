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
    public async Task PackageFoldersUseCapturedMacBasisAndKeepContentsReachable()
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        using var styles = new ProductStyles();
        string input = Directory.CreateDirectory(Path.Combine(fixture.Root, "input")).FullName;
        string package = Directory.CreateDirectory(Path.Combine(input, "Example.APP")).FullName;
        File.WriteAllText(Path.Combine(package, "content.txt"), "contents");
        File.WriteAllText(Path.Combine(input, "Regular.app"), "regular file");

        using var model = new DiskSpaceViewModel(fixture.Services) { Chart = "Table", Metric = AnalysisMetric.FileSize };
        var window = new DiskSpaceWindow(); window.AddSession(model, [input]); window.Show();
        try
        {
            await model.StartAsync([input]);
            await Until(() => model.Tree?.Snapshot.Phase == AnalysisPhase.Finished, timeout: TimeSpan.FromSeconds(20));
            var snapshot = model.Tree!.Snapshot;
            Assert.Equal("contents-included", snapshot.Policy.PackagePolicy);
            var root = Assert.Single(snapshot.Nodes, node => node.ParentId == 0);
            var folder = Assert.Single(snapshot.Nodes, node => node.Name == "Example.APP");
            model.Focus(AnalysisTree.Key(root.Id), open: true);
            string caption = OperatingSystem.IsMacOS() ? "Example.APP (package)" : "Example.APP";
            bool Visible(string text) => window.GetVisualDescendants().OfType<TextBlock>()
                .Any(row => row.IsEffectivelyVisible && row.Text == text);
            await Until(() => { Frame(window); return Visible(caption) && Visible("Regular.app"); },
                () => "Scope " + model.Selection.Scope + "; rows " + string.Join(", ", model.Rows.Select(row => row.Name)));
            Assert.False(Visible("Regular.app (package)"));
            var item = Assert.Single(model.Rows, row => row.NodeId == folder.Id);
            Assert.Equal((UInt128)8, item.LogicalSize);
            model.Focus(AnalysisTree.Key(folder.Id), open: true);
            await Until(() => { Frame(window); return Visible("content.txt"); });
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
