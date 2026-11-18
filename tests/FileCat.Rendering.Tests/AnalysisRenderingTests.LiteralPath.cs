using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisNameRules.Windows, "C:\\", "C:\\Projects\\source.cs")]
    [InlineData(AnalysisNameRules.Unix, "/", "/Projects/source.cs")]
    [InlineData(AnalysisNameRules.Unix, "/fixture\\", "/fixture\\/Projects/source.cs")]
    public async Task ClipboardPathPreservesCapturedRootAndSeparators(AnalysisNameRules names, string root, string descendant)
    {
        var snapshot = Fixture();
        snapshot = snapshot with { Roots = [snapshot.Roots[0] with { CanonicalLocator = root, NameRules = names }] };
        using var model = new DiskSpaceViewModel(snapshot);
        await model.RebuildAsync(true);
        model.Focus("n:1");
        Assert.Equal(root, model.FocusedPath());
        model.Focus("n:4");
        Assert.Equal(descendant, model.FocusedPath());
    }
}
