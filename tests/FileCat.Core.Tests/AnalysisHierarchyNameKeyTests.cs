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

    [Fact]
    public void RawNameKeysIncludeSupplementaryScalarsReplacementCharactersAndUnpairedUnitsInRuntimeOrder()
    {
        var names = new List<string>();
        string[] units = ["\u0001", "\uD7FF", "\uD800", "\uD801", "\uDBFF", "\uDC00", "\uDFFF", "\uE000", "\uFFFD", "\uFFFE", "\uFFFF", "𐐀", "𐐨"];
        for (int scalar = 0x10000; scalar <= 0x10ffff; scalar += 7717) names.Add(char.ConvertFromUtf32(scalar));
        names.Add(char.ConvertFromUtf32(0x10ffff)); names.AddRange(units);
        foreach (string left in units) foreach (string right in units) names.Add(left + right);
        names.AddRange(names.ToArray().Select(name => "prefix" + name));
        var rows = names.Select((name, at) => new { Name = name, Id = (ulong)at + 1 }).ToArray();
        var expected = rows.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id).Select(item => item.Id);
        var actual = rows.Select(item => new AnalysisSortEntry(AnalysisHierarchyNameKey.Create(0, item.Id, item.Name), item.Id)).Order(AnalysisExternalSort.Order).Select(item => item.Value);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RawNameKeysStayInsideTheExistingBoundForMaximumCapturedRepresentations()
    {
        foreach (string name in new[] { new string('x', AnalysisLimits.RawNameBytes), new string('\uFFFF', AnalysisLimits.RawNameBytes / 2), string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x10ffff), AnalysisLimits.RawNameBytes / 4)) })
            Assert.InRange(AnalysisHierarchyNameKey.Create(0, 1, name).Length, 1, AnalysisExternalSort.MaximumKeyBytes);
    }
}
