using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.App.Tests;

public sealed class MetadataOutcomeUiTests
{
    [AvaloniaFact]
    public async Task Explicit_sort_keys_that_cannot_be_parsed_stay_unknown_in_both_directions()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-metadata-keys-" + Guid.NewGuid().ToString("N"));
        string folder = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
        File.WriteAllText(Path.Combine(folder, "a-invalid.txt"), "bad");
        File.WriteAllText(Path.Combine(folder, "b-low.txt"), "2");
        File.WriteAllText(Path.Combine(folder, "c-high.txt"), "10");
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
            services.Metadata.Register(new MetadataField("numeric-text", "Numeric text", MetadataCost.Cheap, _ => true,
                (path, _) => File.ReadAllText(path), value => (string)value!,
                SortKey: value => int.TryParse(value as string, out int number) ? number : null));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(folder));
            try
            {
                await FileListFixture.Wait(() => tab.Listing.State == ListingState.Complete);
                await tab.AnalyzeAsync("numeric-text");
                string[] Names() => Enumerable.Range(0, tab.Listing.VisibleCount)
                    .Select(i => tab.Listing.GetVisible(i)).Where(e => e.Kind == EntryKind.File).Select(e => e.Name).ToArray();
                Assert.Equal(["b-low.txt", "c-high.txt", "a-invalid.txt"], Names());
                Assert.Contains("retains all 3 items", tab.Banner);

                tab.SortByMetadata("numeric-text");
                await FileListFixture.Wait(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion);
                Assert.Equal(["c-high.txt", "b-low.txt", "a-invalid.txt"], Names());
            }
            finally { tab.Dispose(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(MetadataState.Denied, MetadataObservation.Current, "no access")]
    [InlineData(MetadataState.Available, MetadataObservation.LastKnown, "known (last known)")]
    [InlineData(MetadataState.Available, MetadataObservation.Current, "known")]
    [InlineData(MetadataState.Absent, MetadataObservation.Gone, "")]
    [InlineData(MetadataState.Failed, MetadataObservation.Current, "error")]
    public async Task Cells_distinguish_denied_last_known_and_current_results(MetadataState state, MetadataObservation observation, string expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-metadata-outcomes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "item.bin");
        File.WriteAllText(path, "owned");
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "state")));
            var workspace = new WorkspaceViewModel(services);
            var panel = new PanelViewModel(workspace, services);
            workspace.Panels.Add(panel);
            var tab = panel.OpenTab(Location.FileSystem(root));
            try
            {
                services.Metadata.Register(new MetadataField("outcome", "Outcome", MetadataCost.Cheap, _ => true,
                    (_, _) => new MetadataValue(state, state == MetadataState.Available ? "known" : null, "A reason.", observation),
                    v => (string)v!));
                await FileListFixture.Wait(() => tab.Listing.State == ListingState.Complete);
                int index = Enumerable.Range(0, tab.Listing.Store.Count).Single(i => tab.Listing.Store[i].Name == "item.bin");
                var entry = tab.Listing.Store[index];
                services.Metadata.Compute("outcome", path, entry, TestContext.Current.CancellationToken);
                Assert.Equal(expected, tab.GetMetadataText(entry, index, "outcome", out bool pending));
                Assert.False(pending);
                Assert.Equal("owned", File.ReadAllText(path));
            }
            finally { tab.Dispose(); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
