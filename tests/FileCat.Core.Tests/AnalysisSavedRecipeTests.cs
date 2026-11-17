using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisSavedRecipeTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Session = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly SessionIdentity Identity = new(Session, 1, 3, "captured", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
    { TimeZoneId = TimeZoneInfo.Utc.Id };
    private static readonly RootBinding First = new("1", "file", "first", "first", null, IdentityAssurance.Unknown, "First");
    private static readonly RootBinding Second = First with { Id = "2", Label = "Second" };

    [Fact]
    public void SavedFilterRoundTripKeepsExplicitScopeEvenWhenTheQueryCanMatchAnotherRoot()
    {
        var original = AnalysisSavedRecipe.Create(Id, "Old installers", Identity, [First, Second], ["1"], "ext=msi OR root=2", false, AnalysisMetric.OnDisk);
        var decoded = AnalysisSavedRecipe.Decode(original.Encode());
        Assert.Equal(original.Id, decoded.Id);
        Assert.Equal(original.Name, decoded.Name);
        Assert.Equal(original.Query, decoded.Query);
        Assert.Equal(original.PresentationMetric, decoded.PresentationMetric);
        Assert.Equal(original.RootIds.ToArray(), decoded.RootIds.ToArray());
        var filter = decoded.CompileFor(Identity, [First, Second]);
        Assert.Equal(AnalysisTruth.True, filter.Evaluate(Item("1", "setup.MSI")));
        Assert.Equal(AnalysisTruth.False, filter.Evaluate(Item("2", "setup.MSI")));
        Assert.Equal(AnalysisTruth.False, filter.Evaluate(Item("1", "data.txt")));
        Assert.Throws<InvalidOperationException>(() => decoded.CompileFor(Identity with { SessionId = Guid.NewGuid() }, [First, Second]));
        Assert.Throws<InvalidOperationException>(() => decoded.CompileFor(Identity with { Epoch = 2 }, [First, Second]));
        Assert.Throws<InvalidOperationException>(() => decoded.CompileFor(Identity, [First with { Disposition = AnalysisRootDisposition.Covered }, Second]));
        Assert.Throws<InvalidOperationException>(() => decoded.CompileFor(Identity, [First with { CanonicalLocator = "different captured folder" }, Second]));
    }

    [Theory]
    [InlineData(",\"command\":\"cmd.exe\"")]
    [InlineData(",\"runAsAdministrator\":false")]
    [InlineData(",\"source\":\"LiveSession\"")]
    [InlineData(",\"name\":\"duplicate\"")]
    [InlineData(",\"revision\":\"9\"")]
    public void SavedFilterRefusesUnknownAndDuplicateProperties(string additional)
    {
        string valid = Encoding.UTF8.GetString(Sample().Encode());
        Assert.Throws<InvalidDataException>(() => AnalysisSavedRecipe.Decode(Encoding.UTF8.GetBytes(valid[..^1] + additional + "}")));
    }

    [Fact]
    public void SavedFilterRejectsOversizedOrMalformedDataWithoutExecutingQueryText()
    {
        Assert.Throws<InvalidDataException>(() => AnalysisSavedRecipe.Decode(new byte[AnalysisSavedRecipe.MaximumBytes + 1]));
        Assert.Throws<InvalidDataException>(() => AnalysisSavedRecipe.Decode("["u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => (Sample() with { RootIds = ["1", "1"] }).Encode());
        Assert.Throws<InvalidDataException>(() => (Sample() with { Query = new string('a', AnalysisLimits.QueryBytes + 1) }).Encode());

        var literal = (Sample() with { Query = "name=\"cmd.exe & whoami\"" }).Encode();
        var filter = AnalysisSavedRecipe.Decode(literal).CompileFor(Identity, [First]);
        Assert.Equal(AnalysisTruth.True, filter.Evaluate(Item("1", "cmd.exe & whoami")));
        Assert.Equal(AnalysisTruth.False, filter.Evaluate(Item("1", "other")));
    }

    private static AnalysisSavedRecipe Sample() => AnalysisSavedRecipe.Create(Id, "Private recipe", Identity, [First], ["1"], "ext=msi", true, AnalysisMetric.FileSize);
    private static AnalysisFilterItem Item(string root, string name) => new(new(1, root, 0, name, AnalysisKind.File), null, name,
        AnalysisMeasurement.Missing(MeasurementState.Unsupported));
}
