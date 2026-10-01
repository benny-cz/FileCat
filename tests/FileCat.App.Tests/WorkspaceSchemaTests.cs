using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.State;

namespace FileCat.App.Tests;

/// <summary>
/// Release plan V11 and §19.1: state written by a newer FileCat opens read-only and is never saved over. Settings and
/// history did so; the window layout (workspace.json) did not, and it is saved every minute and on exit — two minutes
/// of an older FileCat left no copy of the newer layout anywhere, its backup included (release issue I71).
/// </summary>
public sealed class WorkspaceSchemaTests
{
    [AvaloniaFact]
    public void A_layout_saved_by_a_newer_FileCat_is_not_saved_over()
    {
        string root = Path.Combine(Path.GetTempPath(), "filecat-v11-workspace", Guid.NewGuid().ToString("N"));
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: Path.Combine(root, "data")));
            string file = services.Paths.WorkspaceFile;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // What a later FileCat could have written: a schema this one does not know, and a field it has never heard of.
            string newer = "{\"SchemaVersion\": " + (WorkspaceState.CurrentSchema + 1) + ", \"SomethingNewer\": {\"kept\": true}}";

            foreach (bool reset in new[] { false, true })
            {
                File.WriteAllText(file, newer);
                File.Delete(file + ".bak");
                var vm = new MainViewModel(services);
                Assert.Null(vm.LoadSavedWorkspace(reset));
                Assert.True(vm.WorkspaceReadOnly);
                // The minute's autosave, twice, and the save on exit.
                vm.SaveWorkspace(null);
                vm.SaveWorkspace(null);
                vm.SaveWorkspace(new WindowPlacement());
                Assert.Equal(newer, File.ReadAllText(file));
                Assert.False(File.Exists(file + ".bak"), "the newer layout was rotated into the backup: it was saved over");
            }

            // The control: a layout of this FileCat's own schema is saved as before.
            File.WriteAllText(file, "{\"SchemaVersion\": " + WorkspaceState.CurrentSchema + "}");
            var current = new MainViewModel(services);
            Assert.NotNull(current.LoadSavedWorkspace());
            Assert.False(current.WorkspaceReadOnly);
            current.SaveWorkspace(null);
            Assert.Contains("\"Name\"", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }
}
