using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>Bounded private recipe metadata on the existing guarded store worker.</summary>
internal sealed class AnalysisSavedRecipes(AnalysisPrivateInventoryStore store)
{
    internal const int MaximumRecipes = 64;
    private const int MaximumMetadataBytes = 2 * 1024 * 1024;

    internal ImmutableArray<AnalysisSavedRecipe> ReadFor(SessionIdentity identity, ImmutableArray<RootBinding> roots, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return [.. Read(store.Entries(cancellation), cancellation, out _).Where(recipe => recipe.BelongsTo(identity, roots))
            .OrderBy(recipe => recipe.Name, StringComparer.OrdinalIgnoreCase).ThenBy(recipe => recipe.Id)];
    }

    internal void SaveNew(AnalysisSavedRecipe recipe, SessionIdentity identity, ImmutableArray<RootBinding> roots, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        ArgumentNullException.ThrowIfNull(identity);
        cancellation.ThrowIfCancellationRequested();
        _ = recipe.CompileFor(identity, roots);
        byte[] encoded = recipe.Encode();
        using var verifiedWriter = new AnalysisWorkerClose(store.OpenCatalogLock(cancellation));
        string[] names = store.Entries(cancellation);
        if (names.Length >= AnalysisPrivateNames.DirectoryEntries)
            throw new IOException("The private analysis store has reached its entry limit.");
        var previous = Read(names, cancellation, out int storedBytes);
        if (previous.Length >= MaximumRecipes) throw new IOException("The private saved recipe limit was reached.");
        if (encoded.Length > MaximumMetadataBytes - storedBytes) throw new IOException("The private saved recipe byte limit was reached.");
        if (previous.Any(stored => stored.Id == recipe.Id || stored.Session == recipe.Session && stored.Epoch == recipe.Epoch &&
            stored.Name.Equals(recipe.Name, StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Choose a different recipe name for these captured observations.");
        store.WriteMetadataNew(AnalysisPrivateNames.Recipe(recipe.Id), encoded, cancellation);
    }

    private ImmutableArray<AnalysisSavedRecipe> Read(string[] names, CancellationToken cancellation, out int bytes)
    {
        var output = ImmutableArray.CreateBuilder<AnalysisSavedRecipe>();
        bytes = 0;
        foreach (string name in names)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!AnalysisPrivateNames.TryRecipe(name, out Guid id)) continue;
            if (output.Count >= MaximumRecipes) throw new InvalidDataException("The private saved recipe limit was reached.");
            byte[] data = store.ReadMetadata(name, cancellation, AnalysisSavedRecipe.MaximumBytes);
            if (data.Length > MaximumMetadataBytes - bytes) throw new InvalidDataException("The private saved recipe byte limit was reached.");
            bytes += data.Length;
            var recipe = AnalysisSavedRecipe.Decode(data);
            if (recipe.Id != id) throw new InvalidDataException("The saved recipe and its generated name have different identities.");
            output.Add(recipe);
        }
        return output.ToImmutable();
    }
}
