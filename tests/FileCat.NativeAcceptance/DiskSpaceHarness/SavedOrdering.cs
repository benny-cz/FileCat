using FileCat.App.ViewModels;
using FileCat.Core.Analysis;

namespace DiskSpaceHarness;

internal sealed partial class HarnessApp
{
    private object? _savedOrderingProbe;
    private async Task SavedOrderingProbeAsync()
    {
        var source = _models[0]; bool created;
        using (var first = await source.ReadSavedCatalogAsync()) created = first.Entries.Length == 0;
        if (created)
        {
            source.Chart = "Table"; source.Sort = AnalysisTableSort.Name;
            var root = source.Tree!.Snapshot.Nodes.Single(n => n.ParentId == 0);
            string scope = AnalysisTree.Key(root.Id); source.Focus(scope, open: true);
            var until = DateTimeOffset.UtcNow.AddSeconds(15);
            while (source.Selection.Scope != scope && DateTimeOffset.UtcNow < until) await Task.Delay(50);
            if (source.Selection.Scope != scope) throw new TimeoutException("Generated source navigation did not publish.");
            foreach (bool descending in new[] { false, true })
            {
                source.SortDescending = descending; await source.RebuildAsync(true);
                await source.SaveNewAsync(descending ? "descending.fcdsa" : "ascending.fcdsa");
            }
        }
        var results = new List<object>();
        using var catalog = await source.ReadSavedCatalogAsync();
        if (catalog.Entries.Length != 2) throw new InvalidDataException("Expected exactly two generated ordering inventories.");
        foreach (var entry in catalog.Entries.OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            bool descending = entry.Name == "descending.fcdsa";
            if (entry.Name is not ("ascending.fcdsa" or "descending.fcdsa") || entry.View.Sort != AnalysisTableSort.Name || entry.View.SortDescending != descending)
                throw new InvalidDataException("Generated saved ordering metadata did not round trip.");
            var restored = await source.OpenSavedAsync(entry.Name, entry);
            try
            {
                await restored.RebuildAsync(true);
                if (restored.Sort != AnalysisTableSort.Name || restored.SortDescending != descending || restored.Chart != "Table" ||
                    restored.Selection.Scope == AnalysisTree.RootKey || restored.Selection.Marks.Count != 0 || restored.Selection.Basket.Count != 0 ||
                    restored.Running || restored.Tree!.Snapshot.Origin != AnalysisOrigin.ImportedInventory || restored.Rows.Length < 2)
                    throw new InvalidDataException("Generated offline view failed to restore its inert ordering and scope.");
                string[] names = restored.Rows.Select(r => r.Name).ToArray();
                var expected = descending ? names.OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase) : names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
                if (!names.SequenceEqual(expected)) throw new InvalidDataException("Restored native table rows have the wrong name order.");
                if (descending) _window!.AddSession(restored);
                results.Add(new { entry.Name, restored.Sort, restored.SortDescending, scope = restored.Selection.Scope,
                    rows = names, offline = !restored.Running, emptyMarksAndBasket = restored.Selection.Marks.Count == 0 && restored.Selection.Basket.Count == 0 });
                if (descending) { _models.Add(restored); restored = null!; }
            }
            finally { restored?.Dispose(); }
        }
        using var filenameOnly = await source.OpenSavedAsync("ascending.fcdsa"); await filenameOnly.RebuildAsync(true);
        if (filenameOnly.Sort != AnalysisTableSort.ActiveMetric || !filenameOnly.SortDescending || filenameOnly.Selection.Scope != AnalysisTree.RootKey)
            throw new InvalidDataException("Filename-only opening unexpectedly restored catalog view state.");
        _savedOrderingProbe = new { created, reopenedAfterProcessRestart = !created, filenameOnlyUsesDefaultView = true, results };
    }
}
