using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisRecipeTests
{
    private static readonly DateTimeOffset Observed = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly RootBinding Root = new("root", "local", "Captured", "Captured", null, IdentityAssurance.Unknown, "Captured");
    private static readonly RootBinding OtherRoot = Root with { Id = "other" };

    [Fact]
    public void MetadataRulesRequireDistinctExplicitCapturedLocations()
    {
        Assert.Throws<ArgumentException>(() => new AnalysisRecipes(new(AnalysisMetadataRecipe.LargestFiles, []), [Root], Observed));
        Assert.Throws<ArgumentException>(() => new AnalysisRecipes(new(AnalysisMetadataRecipe.LargestFiles, ["root", "root"]), [Root], Observed));
        Assert.Throws<ArgumentException>(() => new AnalysisRecipes(new(AnalysisMetadataRecipe.LargestFiles, ["C:\\"]), [Root], Observed));
        Assert.Throws<ArgumentException>(() => new AnalysisRecipes(new(AnalysisMetadataRecipe.LargestFiles, ["root"]),
            [Root with { Disposition = AnalysisRootDisposition.Covered }], Observed));
        Assert.Throws<ArgumentException>(() => new AnalysisRecipes(new(AnalysisMetadataRecipe.DownloadsInstallers, ["root", "other"]),
            [Root, OtherRoot], Observed));
        Assert.Throws<ArgumentException>(() => new AnalysisRecipes(new(AnalysisMetadataRecipe.DownloadsInstallers, ["root"]),
            [Root with { Kind = AnalysisKind.File }], Observed));
    }

    [Fact]
    public void OldLargeUsesObservationTimeAndPreservesTimestampUncertainty()
    {
        var recipe = Rule(AnalysisMetadataRecipe.OldLargeFiles);
        Assert.Equal((UInt128?)AnalysisRecipes.DefaultOldLargeBytes, recipe.MinimumBytes);
        Assert.Equal(180, recipe.MinimumAgeDays);
        var node = File(1, 0, "large.dat");
        var threshold = Observed.AddDays(-180);
        var old = new AnalysisObject("file", "identity", AnalysisMeasurement.Known(AnalysisRecipes.DefaultOldLargeBytes),
            AnalysisMeasurement.Known(1), threshold.AddTicks(-1));
        Assert.Equal(AnalysisRecipeMatch.Candidate, Evaluate(recipe, node, old).Match);
        Assert.Equal(AnalysisRecipeMatch.NoMatch, Evaluate(recipe, node, old with { ModifiedUtc = threshold }).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, node, old with { ModifiedPrecisionTicks = 2 }).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, node, old with { ModifiedUtc = null }).Match);
        Assert.Equal(AnalysisRecipeMatch.NoMatch, Evaluate(recipe, node,
            old with { LogicalLength = AnalysisMeasurement.Known(AnalysisRecipes.DefaultOldLargeBytes - 1) }).Match);
        var adjustable = new AnalysisRecipes(new(AnalysisMetadataRecipe.OldLargeFiles, ["root"], 1024, 7), [Root], Observed);
        Assert.Equal(AnalysisRecipeMatch.Candidate, Evaluate(adjustable, node,
            old with { LogicalLength = AnalysisMeasurement.Known(1024), ModifiedUtc = Observed.AddDays(-8) }).Match);
    }

    [Fact]
    public void DownloadsUsesOnlyItsDesignatedRootAndDoesNotClassifyUnverifiedText()
    {
        var recipe = Rule(AnalysisMetadataRecipe.DownloadsInstallers);
        var installer = File(2, 1, "Setup.MSI");
        Assert.Equal(AnalysisRecipeMatch.Candidate, Evaluate(recipe, installer).Match);
        Assert.Equal(AnalysisRecipeMatch.Candidate, Evaluate(recipe, installer with { Name = "bundle.ZIP" }).Match);
        Assert.Equal(AnalysisRecipeMatch.OutsideScope, Evaluate(recipe, installer with { RootId = "other" }).Match);
        Assert.Equal(AnalysisRecipeMatch.NoMatch, Evaluate(recipe, installer with { Name = "library.DLL" }).Match);
        Assert.Equal(AnalysisRecipeMatch.Excluded, Evaluate(recipe, installer with { Cloud = true }).Match);
        Assert.Equal(AnalysisRecipeMatch.NoMatch, Evaluate(recipe, installer with { Kind = AnalysisKind.Link }).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, installer with
        {
            Name = "�.msi", NameEncoding = "unix-bytes", RawName = [0xff, (byte)'.', (byte)'m', (byte)'s', (byte)'i']
        }).Match);
    }

    [Fact]
    public void LargestFilesRequiresKnownSelectedMeasureAndKeepsZero()
    {
        var recipe = Rule(AnalysisMetadataRecipe.LargestFiles);
        var node = File(1, 0, "empty");
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, node).Match);
        Assert.Equal(AnalysisRecipeMatch.Candidate, Evaluate(recipe, node, measured: AnalysisMeasurement.Known(0)).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, node with { MetadataState = MeasurementState.Changed },
            measured: AnalysisMeasurement.Known(4)).Match);
    }

    [Fact]
    public void GeneratedOutputRequiresTheMatchingProjectAndExactObservedParent()
    {
        var recipe = Rule(AnalysisMetadataRecipe.GeneratedBuildOutput);
        var project = Directory(10, 0, "Example");
        var obj = Directory(11, 10, "obj");
        var candidate = File(12, 11, "project.assets.json");
        var marker = File(13, 10, "Example.csproj");
        var match = Evaluate(recipe, candidate, namespaceRows: [project, obj, marker]);
        Assert.Equal(AnalysisRecipeMatch.Candidate, match.Match);
        Assert.Equal(11UL, match.ContextPath);
        Assert.Equal(13UL, match.MarkerPath);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, candidate, namespaceRows: [project, obj]).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, candidate, namespaceRows: [project, obj, marker with { Kind = AnalysisKind.Link }]).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, candidate, namespaceRows: [project, obj, marker with { RootId = "other" }]).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, candidate, namespaceRows: [project, obj, marker with { Cloud = true }]).Match);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, candidate, namespaceRows: [project, obj with { IncludedByPolicy = false }, marker]).Match);
        Assert.Equal(AnalysisRecipeMatch.NoMatch, Evaluate(recipe, candidate with { Name = "customer-data.json" }, namespaceRows: [project, obj, marker]).Match);
    }

    [Fact]
    public void ChromeCacheRequiresItsRecordedProfileAndBothMarkers()
    {
        var recipe = Rule(AnalysisMetadataRecipe.ApplicationCache);
        var google = Directory(1, 0, "Google");
        var chrome = Directory(2, 1, "Chrome");
        var userData = Directory(3, 2, "User Data");
        var profile = Directory(4, 3, "Default");
        var cache = Directory(5, 4, "Cache");
        var data = Directory(6, 5, "Cache_Data");
        var localState = File(7, 3, "Local State");
        var preferences = File(8, 4, "Preferences");
        var candidate = File(9, 6, "01234567abcdef00_0");
        AnalysisNode[] graph = [google, chrome, userData, profile, cache, data, localState, preferences];
        var match = Evaluate(recipe, candidate, namespaceRows: graph);
        Assert.Equal(AnalysisRecipeMatch.Candidate, match.Match);
        Assert.Equal(6UL, match.ContextPath);
        Assert.Equal(7UL, match.MarkerPath);
        Assert.Equal(8UL, match.AdditionalMarkerPath);
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, candidate, namespaceRows: graph[..^1]).Match);
        graph[1] = chrome with { Name = "Other" };
        Assert.Equal(AnalysisRecipeMatch.Unknown, Evaluate(recipe, candidate, namespaceRows: graph).Match);
        graph[1] = chrome;
        Assert.Equal(AnalysisRecipeMatch.NoMatch, Evaluate(recipe, candidate with { Name = "saved-notes" }, namespaceRows: graph).Match);
    }

    private static AnalysisRecipes Rule(AnalysisMetadataRecipe recipe) => new(new(recipe, ["root"]), [Root, OtherRoot], Observed);
    private static AnalysisNode File(ulong id, ulong parent, string name) => new(id, "root", parent, name, AnalysisKind.File, "file");
    private static AnalysisNode Directory(ulong id, ulong parent, string name) => new(id, "root", parent, name, AnalysisKind.Directory);

    private static AnalysisRecipeFinding Evaluate(AnalysisRecipes recipe, AnalysisNode node, AnalysisObject? recordedObject = null,
        AnalysisMeasurement measured = default, AnalysisNode[]? namespaceRows = null)
    {
        namespaceRows ??= [];
        return recipe.Evaluate(node, recordedObject, measured, id => namespaceRows.SingleOrDefault(row => row.Id == id),
            (parent, name) => namespaceRows.SingleOrDefault(row => row.ParentId == parent.Id && row.Name == name));
    }
}
