using FileCat.Core.State;

namespace FileCat.Core.Tests;

public sealed class JsonFileStoreNewTests
{
    [Fact]
    public void SaveNew_refuses_an_existing_name_without_changing_main_or_backup()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "workspace.json");
        JsonFileStore.Save(path, new WorkspaceState { Name = "Original" }, StateJsonContext.Default.WorkspaceState);
        File.WriteAllText(path + ".bak", "older backup");
        byte[] main = File.ReadAllBytes(path), backup = File.ReadAllBytes(path + ".bak");

        Assert.Throws<IOException>(() => JsonFileStore.SaveNew(path,
            new WorkspaceState { Name = "New" }, StateJsonContext.Default.WorkspaceState));

        Assert.Equal(main, File.ReadAllBytes(path));
        Assert.Equal(backup, File.ReadAllBytes(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Replacement_guard_runs_after_serialization_and_preserves_existing_files_on_refusal()
    {
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "workspace.json");
        JsonFileStore.Save(path, new WorkspaceState { Name = "Original" }, StateJsonContext.Default.WorkspaceState);
        byte[] main = File.ReadAllBytes(path);
        bool called = false;

        Assert.Throws<IOException>(() => JsonFileStore.Save(path,
            new WorkspaceState { Name = "New" }, StateJsonContext.Default.WorkspaceState,
            () => { called = true; return false; }));

        Assert.True(called);
        Assert.Equal(main, File.ReadAllBytes(path));
        Assert.False(File.Exists(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }
}
