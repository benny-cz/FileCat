using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisCatalogTests
{
    [Fact]
    public void PrivateRecipeEntriesShareTheWriterLockAndDoNotBecomeInventories()
    {
        Ordinary();
        using var directory = new MemoryDirectory();
        using var store = new AnalysisPrivateInventoryStore(directory);
        var catalog = new AnalysisSavedCatalog(store);
        var recipes = new AnalysisSavedRecipes(store);
        var snapshot = AnalysisAccountingTests.Golden();
        var recipe = AnalysisSavedRecipe.Create(Guid.NewGuid(), "Small logs", snapshot.Identity, snapshot.Roots,
            [snapshot.Roots[0].Id], "ext=log size<1KiB", false, AnalysisMetric.FileSize);
        Guid profile = new AnalysisPrivateProfileState(store).LoadOrCreate(Cancellation);
        byte[] storedProfile = directory.Files[AnalysisPrivateNames.PrivateState].ToArray();
        Guid inventory = Guid.NewGuid();

        using (catalog.BeginNamedWrite("captured.fcdsa", inventory, snapshot.Coverage, snapshot.Taint, AnalysisSavedView.Default, Cancellation))
            Assert.Throws<IOException>(() => recipes.SaveNew(recipe, snapshot.Identity, snapshot.Roots, Cancellation));
        Assert.DoesNotContain(AnalysisPrivateNames.Recipe(recipe.Id), directory.Files.Keys);
        catalog.SaveNew("captured.fcdsa", snapshot, inventory, AnalysisSavedView.Default, Cancellation);
        recipes.SaveNew(recipe, snapshot.Identity, snapshot.Roots, Cancellation);
        Assert.Equal(recipe.Id, Assert.Single(recipes.ReadFor(snapshot.Identity, snapshot.Roots, Cancellation)).Id);
        Assert.Empty(recipes.ReadFor(snapshot.Identity with { SessionId = Guid.NewGuid() }, snapshot.Roots, Cancellation));
        Assert.Throws<IOException>(() => recipes.SaveNew(recipe with { Id = Guid.NewGuid(), Name = "SMALL LOGS" }, snapshot.Identity,
            snapshot.Roots, Cancellation));
        using var saved = catalog.Read(Cancellation);
        Assert.Equal(inventory, Assert.Single(saved.Entries).InventoryId);
        Assert.Empty(saved.UnindexedNames);
        Assert.Equal(storedProfile, directory.Files[AnalysisPrivateNames.PrivateState]);
        Assert.Equal(profile, new AnalysisPrivateProfileState(store).ReadExisting(Cancellation));
    }

    [Fact]
    public void PrivateRecipeWithMismatchedGeneratedIdentityLeavesOtherPrivateRecordsIntact()
    {
        Ordinary();
        using var directory = new MemoryDirectory();
        using var store = new AnalysisPrivateInventoryStore(directory);
        var snapshot = AnalysisAccountingTests.Golden();
        var recipe = AnalysisSavedRecipe.Create(Guid.NewGuid(), "Small files", snapshot.Identity, snapshot.Roots,
            [snapshot.Roots[0].Id], "size<1KiB", false, AnalysisMetric.FileSize);
        Guid inventory = Guid.NewGuid();
        var catalog = new AnalysisSavedCatalog(store);
        catalog.SaveNew("kept.fcdsa", snapshot, inventory, AnalysisSavedView.Default, Cancellation);
        directory.Files.Add(AnalysisPrivateNames.Recipe(Guid.NewGuid()), recipe.Encode());
        var before = directory.Files.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var recipes = new AnalysisSavedRecipes(store);
        Assert.Throws<InvalidDataException>(() => recipes.ReadFor(snapshot.Identity, snapshot.Roots, Cancellation));
        Assert.Throws<InvalidDataException>(() => recipes.SaveNew(recipe, snapshot.Identity, snapshot.Roots, Cancellation));
        using var index = catalog.Read(Cancellation);
        Assert.Equal(inventory, Assert.Single(index.Entries).InventoryId);
        Assert.Equal(before.Keys, directory.Files.Keys);
        foreach (var pair in before) Assert.Equal(pair.Value, directory.Files[pair.Key]);
    }
}
