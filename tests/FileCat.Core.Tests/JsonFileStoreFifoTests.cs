using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.Core.Tests;

public sealed class JsonFileStoreFifoTests
{
    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Nonregular_primary_loads_backup_read_only_without_waiting_or_preserving_it(bool linked)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix state-file FIFO control."); return; }
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "settings.json");
        string fifo = linked ? Path.Combine(dir.Path, "fifo") : path;
        Assert.Equal(0, MkFifo(fifo, 0x180));
        if (linked) File.CreateSymbolicLink(path, fifo);
        File.WriteAllText(path + ".bak", "{\"SchemaVersion\":1,\"Theme\":\"backup\"}");

        var clock = Stopwatch.StartNew();
        var loaded = JsonFileStore.Load(path, StateJsonContext.Default.AppSettings,
            AppSettings.CurrentSchema, () => new AppSettings(), out var status);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"State load took {clock.Elapsed}.");
        Assert.Equal(StateLoadStatus.UnavailableReadOnly, status);
        Assert.Equal("backup", loaded.Theme);
        Assert.False(UnixFiles.Stat(fifo)!.Value.IsRegular);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*"));
    }

    [Fact]
    public void Nonregular_backup_is_not_read_or_preserved_after_bad_primary()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix state-file FIFO control."); return; }
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "settings.json");
        File.WriteAllText(path, "{invalid json");
        Assert.Equal(0, MkFifo(path + ".bak", 0x180));

        var clock = Stopwatch.StartNew();
        var loaded = JsonFileStore.Load(path, StateJsonContext.Default.AppSettings,
            AppSettings.CurrentSchema, () => new AppSettings(), out var status);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"Backup admission took {clock.Elapsed}.");
        Assert.Equal(StateLoadStatus.UnavailableReadOnly, status);
        Assert.Equal("System", loaded.Theme);
        Assert.Equal("{invalid json", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.corrupt-*"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Save_refuses_a_nonregular_state_or_backup_without_waiting(bool backup)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix state-file FIFO control."); return; }
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "settings.json");
        if (backup) File.WriteAllText(path, "previous");
        string fifo = backup ? path + ".bak" : path;
        Assert.Equal(0, MkFifo(fifo, 0x180));

        var clock = Stopwatch.StartNew();
        Assert.Throws<IOException>(() => JsonFileStore.Save(path,
            new AppSettings { Theme = "new" }, StateJsonContext.Default.AppSettings));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"State save took {clock.Elapsed}.");
        Assert.False(UnixFiles.Stat(fifo)!.Value.IsRegular);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp-*"));
        if (backup) Assert.Equal("previous", File.ReadAllText(path));
    }

    [Fact]
    public void Save_does_not_open_a_crash_left_fixed_temporary_fifo()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix state-file FIFO control."); return; }
        using var dir = new TempDir();
        string path = Path.Combine(dir.Path, "settings.json");
        JsonFileStore.Save(path, new AppSettings { Theme = "before" }, StateJsonContext.Default.AppSettings);
        string fifo = path + ".tmp";
        Assert.Equal(0, MkFifo(fifo, 0x180));

        var clock = Stopwatch.StartNew();
        JsonFileStore.Save(path, new AppSettings { Theme = "after" }, StateJsonContext.Default.AppSettings);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(2), $"State save took {clock.Elapsed}.");
        Assert.False(UnixFiles.Stat(fifo)!.Value.IsRegular);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp-*"));
        var loaded = JsonFileStore.Load(path, StateJsonContext.Default.AppSettings,
            AppSettings.CurrentSchema, () => new AppSettings(), out var status);
        Assert.Equal(StateLoadStatus.Loaded, status);
        Assert.Equal("after", loaded.Theme);
    }
}
