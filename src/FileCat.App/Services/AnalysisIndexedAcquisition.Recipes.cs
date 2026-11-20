using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.App.Services;

internal sealed partial class AnalysisIndexedAcquisition
{
    internal Task<ImmutableArray<AnalysisSavedRecipe>> ReadRecipesAsync(SessionIdentity identity, ImmutableArray<RootBinding> roots,
        CancellationToken cancellation) => RunRecipeStoreAsync((store, token) => store.ReadFor(identity, roots, token), cancellation);

    internal Task SaveRecipeAsync(AnalysisSavedRecipe recipe, SessionIdentity identity, ImmutableArray<RootBinding> roots,
        CancellationToken cancellation) => RunRecipeStoreAsync((store, token) =>
    {
        store.SaveNew(recipe, identity, roots, token);
        return true;
    }, cancellation);

    private async Task<T> RunRecipeStoreAsync<T>(Func<AnalysisSavedRecipes, CancellationToken, T> run, CancellationToken cancellation)
    {
        IDisposable actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_close is not null, this);
            if (_session is not null || _importStarted) throw new InvalidOperationException("This preparation already owns an analysis or private-store operation.");
            actual = _imports.Enter();
            _importStarted = true;
        }
        using (actual)
        {
            if (MemoryPreparationNotice is not null || _profile == Guid.Empty || Profile is null)
                throw new NotSupportedException("Secure private profile storage is required for saved recipes.");
            return await AnalysisIo.Run(_services.Io, PathUtil.GetDeviceKey(_privatePaths[0]), token =>
            {
                CheckPreparation(0, token);
                var directory = OpenDirectory(_privatePaths[0]);
                try { RequireSamePrivateRoot(directory, _protection[0] ?? throw new IOException("The prepared private profile is unavailable.")); }
                catch { DisposePreparedOnWorker(directory); throw; }
                using var store = new AnalysisPrivateInventoryStore(directory);
                return run(new AnalysisSavedRecipes(store), token);
            }, cancellation).ConfigureAwait(false);
        }
    }
}
