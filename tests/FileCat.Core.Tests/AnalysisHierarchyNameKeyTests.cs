using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisHierarchyNameKeyTests
{
    [Fact]
    public void EveryNonzeroUtf16UnitUsesTheRuntimeOrdinalIgnoreCaseOrder()
    {
        var names = Enumerable.Range(1, char.MaxValue).Select(value => new { Id = (ulong)value, Name = new string((char)value, 1) }).ToArray();
        var expected = names.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).Select(item => item.Id).ToArray();
        var actual = names.Select(item => new AnalysisSortEntry(AnalysisHierarchyNameKey.Create(0, item.Id, item.Name), item.Id))
            .Order(AnalysisExternalSort.Order).Select(item => item.Value).ToArray();
        Assert.Equal(expected, actual);
        Assert.Throws<InvalidDataException>(() => AnalysisHierarchyNameKey.Create(0, 1, "\0"));
    }
}
