using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisFilterTests
{
    private static AnalysisFilterItem Item(string name = "photo.jpg", AnalysisMeasurement? length = null) => new(
        new(2, "root", 1, name, AnalysisKind.File, "o"),
        new("o", "dev:o", length ?? AnalysisMeasurement.Known(2048), AnalysisMeasurement.Known(1), DateTimeOffset.Parse("2026-11-04T12:00:00Z")),
        "Pictures/" + name, AnalysisMeasurement.Missing(MeasurementState.Unsupported));
    private static AnalysisTruth Match(string query, AnalysisFilterItem? item = null, bool sensitive = false) =>
        AnalysisFilter.Parse(query, TimeZoneInfo.Utc, sensitive).Evaluate(item ?? Item());

    [Theory]
    [InlineData("name:photo AND size>=2KiB", AnalysisTruth.True)]
    [InlineData("name=other OR ext=.JPG AND size>1KB", AnalysisTruth.True)]
    [InlineData("(name=other OR ext=png) AND size>1KB", AnalysisTruth.False)]
    [InlineData("NOT (ext=png OR size<1KB)", AnalysisTruth.True)]
    [InlineData("\"photo\" NOT name:other", AnalysisTruth.True)]
    [InlineData("allocated>0", AnalysisTruth.Unknown)]
    [InlineData("NOT allocated=0", AnalysisTruth.Unknown)]
    [InlineData("allocated>0 OR name:photo", AnalysisTruth.True)]
    [InlineData("allocated>0 AND name:other", AnalysisTruth.False)]
    [InlineData("modified=2026-11-04", AnalysisTruth.True)]
    [InlineData("modified<=2026-11-04", AnalysisTruth.True)]
    [InlineData("modified>2026-11-04", AnalysisTruth.False)]
    [InlineData("modified-before:2026-11-05", AnalysisTruth.True)]
    [InlineData("modified>=2026-11-04T11:00:00Z", AnalysisTruth.True)]
    [InlineData("type=image cloud=false", AnalysisTruth.True)]
    public void GrammarAndMissingMeasurements(string text, AnalysisTruth expected) => Assert.Equal(expected, Match(text));

    [Theory]
    [InlineData("size:10")]
    [InlineData("size<-1")]
    [InlineData("size>1,000")]
    [InlineData("size>1.0001KiB")]
    [InlineData("size>0.1B")]
    [InlineData("typo=1")]
    [InlineData("name>hello")]
    [InlineData("cloud:maybe")]
    [InlineData("state:imaginary")]
    [InlineData("change:added")]
    [InlineData("delta>1")]
    [InlineData("name:")]
    [InlineData("a OR")]
    [InlineData("a AND")]
    [InlineData("NOT NOT a")]
    [InlineData("(a")]
    [InlineData("\"x\\q\"")]
    [InlineData("modified>2026-11-04T12:00:00")]
    public void InvalidQueriesAreRejected(string text) => Assert.ThrowsAny<FormatException>(() => AnalysisFilter.Parse(text, TimeZoneInfo.Utc));

    [Fact]
    public void QueryLimitsAndCaseAreEnforced()
    {
        Assert.Throws<FormatException>(() => AnalysisFilter.Parse(new string('a', 8193), TimeZoneInfo.Utc));
        Assert.Throws<FormatException>(() => AnalysisFilter.Parse(string.Join(" ", Enumerable.Repeat("a", 129)), TimeZoneInfo.Utc));
        Assert.Throws<FormatException>(() => AnalysisFilter.Parse(new string('(', 17) + "a" + new string(')', 17), TimeZoneInfo.Utc));
        Assert.Equal(AnalysisTruth.True, Match("PHOTO"));
        Assert.Equal(AnalysisTruth.False, Match("PHOTO", sensitive: true));
    }

    [Theory]
    [InlineData("1.5KiB", "1536")]
    [InlineData("1.001KB", "1001")]
    [InlineData("340282366920938463463374607431768211455", "340282366920938463463374607431768211455")]
    public void ByteParsingIsExact(string value, string expected) => Assert.Equal(UInt128.Parse(expected), AnalysisFilter.ParseBytes(value));

    [Fact]
    public void OverflowAndUncertaintyArePreserved()
    {
        Assert.Throws<OverflowException>(() => AnalysisFilter.ParseBytes(UInt128.MaxValue + "KB"));
        Assert.Equal(AnalysisTruth.Unknown, Match("size>1", Item(length: AnalysisMeasurement.Missing(MeasurementState.Changed))));
        Assert.Equal(AnalysisTruth.True, Match("state:unknown", Item(length: AnalysisMeasurement.Missing(MeasurementState.Changed))));
    }

    [Theory]
    [InlineData("../evil")][InlineData("..")][InlineData(".")][InlineData("")][InlineData("C:evil")]
    [InlineData("x:stream")][InlineData("NUL.txt")][InlineData("COM¹")][InlineData("LPT1.doc")][InlineData("trailing.")]
    [InlineData("trailing ")][InlineData("a\\b")][InlineData("a\0b")][InlineData("CONIN$")]
    public void UnsafeWindowsChildNamesCannotBecomeLivePaths(string name) => Assert.False(AnalysisNames.IsOrdinaryChild(name, true));

    [Fact]
    public void SafeDisplayAndExportsDoNotInterpretSourceText()
    {
        Assert.Equal("evil\\u202Etxt\\u000A", AnalysisNames.Display("evil\u202Etxt\n"));
        Assert.Equal("\"' =SUM(A1)\"", AnalysisNames.CsvCell(" =SUM(A1)"));
        Assert.Equal("\"a\"\"b\"", AnalysisNames.CsvCell("a\"b"));
        Assert.True(AnalysisNames.IsOrdinaryChild("actual\\unix:name", false));
    }
}
