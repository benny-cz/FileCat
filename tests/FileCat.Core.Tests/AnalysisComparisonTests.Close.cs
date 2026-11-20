using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisComparisonTests
{
    [Fact]
    public void OrdinarySavedViewsRejectGrowthColorWithoutComparisonContext()
    {
        Assert.Throws<InvalidDataException>(() => AnalysisSavedCatalog.ValidateView(AnalysisSavedView.Default with { ColorDimension = AnalysisColorDimension.Growth }));
        foreach (var color in new[] { AnalysisColorDimension.FileType, AnalysisColorDimension.HierarchyDepth, AnalysisColorDimension.ModifiedAge })
            AnalysisSavedCatalog.ValidateView(AnalysisSavedView.Default with { ColorDimension = color });
    }
    [Fact]
    public async Task ActualReadersKeepAdmissionUntilTheLastReleaseAndSignalCompletion()
    {
        var sample = Sample(("same", "k", 1, 1, "one"));
        var result = AnalysisComparison.Build(sample, sample, AnalysisMetric.FileSize, cancellation: Cancellation);
        using var reader = result.Retain(); using var nested = reader.Retain();
        result.Dispose(); Assert.False(result.CloseCompletion.IsCompleted);
        reader.Dispose(); Assert.False(result.CloseCompletion.IsCompleted);
        Assert.Single(nested.Rows);
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(sample, sample, AnalysisMetric.Files, cancellation: Cancellation));
        nested.Dispose(); await result.CloseCompletion.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        Assert.Throws<ObjectDisposedException>(() => nested.Retain());
        using var next = AnalysisComparison.Build(sample, sample, AnalysisMetric.Files, cancellation: Cancellation);
        Assert.Single(next.Rows);
    }
}
