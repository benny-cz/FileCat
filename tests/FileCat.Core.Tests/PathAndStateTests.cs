using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Core.Tests;

public class PathAndStateTests
{
    [Fact]
    public void Folder_scan_is_breadth_first_bounded_and_skips_hidden_folders()
    {
        using var dir = new TempDir();
        dir.Dir(Path.Combine("a", "deep", "deeper"));
        dir.Dir("b");
        var hidden = dir.Dir(".secret"); // hidden on Unix by its name, on Windows by the attribute
        File.SetAttributes(hidden, FileAttributes.Directory | FileAttributes.Hidden);
        dir.Dir(Path.Combine(".secret", "inside"));

        var all = Core.FileSystem.FolderScan.Run(dir.Path, 100, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.False(all.Stopped);
        Assert.Equal(["a", "b", Path.Combine("a", "deep"), Path.Combine("a", "deep", "deeper")],
            all.Folders.Select(f => Path.GetRelativePath(dir.Path, f)));

        var first = Core.FileSystem.FolderScan.Run(dir.Path, 2, TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(first.Stopped);
        Assert.Equal(["a", "b"], first.Folders.Select(f => Path.GetRelativePath(dir.Path, f)));
    }

    [Fact]
    public void Members_a_crash_left_in_the_temp_folder_go_after_a_day_and_nothing_else_does()
    {
        // Release plan B06: spooled archive members are deleted on close, but on Linux and macOS a crash leaves them.
        using var dir = new TempDir();
        var paths = AppPaths.Resolve(overrideRoot: dir.Path);
        string Make(string name, double hoursOld)
        {
            string file = Path.Combine(paths.TempDirectory, name);
            File.WriteAllText(file, "x");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-hoursOld));
            return file;
        }
        string oldMember = Make("member-0123456789abcdef.tmp", 30), oldNested = Make("nested-0123456789abcdef.zip", 30);
        string freshMember = Make("member-fedcba9876543210.tmp", 1), oldOther = Make("rename-0123.txt", 30);
        Assert.Equal(2, paths.SweepTemporaryLeftovers(TimeSpan.FromDays(1)));
        Assert.False(File.Exists(oldMember));
        Assert.False(File.Exists(oldNested));
        Assert.True(File.Exists(freshMember));
        Assert.True(File.Exists(oldOther));
    }

    [Fact]
    public void With_a_data_folder_everything_FileCat_writes_is_in_it_and_the_usual_places_are_only_worked_out()
    {
        // Release plan V09 (I09): to recover from the disk that holds FileCat's usual places, FileCat is started with all
        // of its files elsewhere (--data). Every folder it writes in must then lie in that folder, scratch and hex originals too.
        using var dir = new TempDir();
        string data = Path.Combine(dir.Path, "recovery data");
        var paths = AppPaths.Resolve(dataRoot: data);
        Assert.Equal(Path.GetFullPath(data), paths.DataRoot);
        Assert.NotEmpty(paths.WriteFolders);
        Assert.All(paths.WriteFolders, f => Assert.True(PathUtil.IsSameOrUnder(f.Folder, data), $"{f.What}: {f.Folder}"));
        Assert.Contains(paths.WriteFolders, f => f.Folder == paths.ListingScratchDirectory);
        Assert.Contains(paths.WriteFolders, f => f.Folder == paths.HexRecoveryDirectory);
        Assert.Contains(paths.WriteFolders, f => f.Folder == paths.ElevationExchangeDirectory);
        Assert.True(Directory.Exists(paths.ListingScratchDirectory) && Directory.Exists(paths.HexRecoveryDirectory));
        // A profile keeps its own folders inside the data folder as well.
        Assert.All(AppPaths.Resolve("second", dataRoot: data).WriteFolders, f => Assert.True(PathUtil.IsSameOrUnder(f.Folder, data), $"{f.What}: {f.Folder}"));

        // The usual places, for a portable copy: worked out, nothing made.
        string program = dir.Dir("portable copy");
        File.WriteAllText(Path.Combine(program, AppPaths.PortableMarker), "");
        var usual = AppPaths.Usual(baseDirectory: program);
        Assert.True(usual.IsPortable);
        Assert.Equal(Path.Combine(program, "Data"), usual.SettingsDirectory);
        Assert.False(Directory.Exists(Path.Combine(program, "Data")));
    }

    [Fact]
    public void Containment_is_segment_aware()
    {
        var root = Path.Combine(Path.GetTempPath(), "abc");
        Assert.True(PathUtil.IsSameOrUnder(Path.Combine(root, "x"), root));
        Assert.True(PathUtil.IsSameOrUnder(root, root));
        Assert.False(PathUtil.IsSameOrUnder(root + "def", root));
        Assert.True(PathUtil.SubtreesOverlap(root, Path.Combine(root, "x", "y")));
    }

    [Fact]
    public void Unc_detection()
    {
        Assert.True(PathUtil.IsUncServerRoot(@"\\server"));
        Assert.True(PathUtil.IsUncServerRoot(@"\\server\"));
        Assert.False(PathUtil.IsUncServerRoot(@"\\server\share"));
        Assert.True(PathUtil.IsUncShareRoot(@"\\server\share"));
        Assert.True(PathUtil.IsUncShareRoot(@"\\server\share\"));
        Assert.False(PathUtil.IsUncShareRoot(@"\\server\share\dir"));
        Assert.Equal(@"\\server", PathUtil.GetUncServer(@"\\server\share\dir"));
        Assert.False(PathUtil.IsUncPath(@"\\?\C:\x"));
    }

    [Fact]
    public async Task A_folder_the_system_does_not_report_is_followed_by_its_time_stamp()
    {
        using var dir = new TempDir();
        // A local temporary folder is not on the network: its changes are reported by notifications.
        Assert.False(PathUtil.IsOnNetwork(dir.Path));
        int changes = 0;
        using var poller = new FolderPoller(dir.Path, () => Interlocked.Increment(ref changes), TimeSpan.FromMilliseconds(100));
        await Task.Delay(300, TestContext.Current.CancellationToken);
        Assert.Equal(0, changes);
        // A file made in it moves the folder's time stamp: reported within a few reads.
        var before = Directory.GetLastWriteTimeUtc(dir.Path);
        for (int i = 0; Directory.GetLastWriteTimeUtc(dir.Path) == before && i < 50; i++)
        {
            File.WriteAllText(Path.Combine(dir.Path, $"new-{i}.txt"), "x");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        for (int i = 0; i < 100 && Volatile.Read(ref changes) == 0; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(changes > 0);
    }

    [Fact]
    public void Drive_letters_show_upper_case_however_they_were_typed()
    {
        var fs = new LocalFileSystemProvider();
        if (!OperatingSystem.IsWindows())
        {
            // No drive letters here: paths are shown as they are.
            Assert.Equal("/home/c:x", PathUtil.WithUpperDrive("/home/c:x"));
            return;
        }
        Assert.Equal(@"C:\Users\x", PathUtil.WithUpperDrive(@"c:\Users\x"));
        Assert.Equal(@"\\?\D:\x", PathUtil.WithUpperDrive(@"\\?\d:\x"));
        Assert.Equal(@"\\server\c$", PathUtil.WithUpperDrive(@"\\server\c$"));
        Assert.Equal("X:", PathUtil.WithUpperDrive("x:"));

        // Typed: the location itself carries the upper case letter.
        Assert.True(fs.TryParse(@"c:\windows", null, out var typed));
        Assert.Equal(@"C:\windows", typed!.Path);
        Assert.True(fs.TryParse("d:", null, out var root));
        Assert.Equal(@"D:\", root!.Path);

        // Stored or linked with a lower case letter: shown upper case all the same, in the path, a root's tab name,
        // and an archive's path.
        var stored = Location.FileSystem(@"e:\data");
        Assert.Equal(@"E:\data", fs.GetDisplayPath(stored));
        Assert.Equal(@"E:\", fs.GetDisplayName(Location.FileSystem(@"e:\")));
        var zip = new Core.Archives.ZipProvider(Path.GetTempPath());
        Assert.Equal(@"E:\data\a.zip\inner", zip.GetDisplayPath(new Location(Schemes.Zip, "inner", Location.FileSystem(@"e:\data\a.zip"))));
    }

    [Fact]
    public void Unique_names_do_not_stack_suffixes()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a.txt", "a (2).txt", "a (3).txt" };
        Assert.Equal("a (4).txt", PathUtil.MakeUniqueName("a.txt", existing.Contains));
        Assert.Equal("a (4).txt", PathUtil.MakeUniqueName("a (2).txt", existing.Contains));
        Assert.Equal("dir.v1 (2)", PathUtil.MakeUniqueName("dir.v1", n => n == "dir.v1", isDirectory: true));
    }

    [Fact]
    public void Windows_name_rules()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows' name rules.");
        Assert.NotNull(PathUtil.ValidateNewName("CON"));
        Assert.NotNull(PathUtil.ValidateNewName("con.txt"));
        Assert.NotNull(PathUtil.ValidateNewName("a:b"));
        Assert.NotNull(PathUtil.ValidateNewName("trailing."));
        Assert.NotNull(PathUtil.ValidateNewName(""));
        Assert.Null(PathUtil.ValidateNewName("normal name.txt"));
        Assert.Null(PathUtil.ValidateNewName("consistent.txt"));
    }

    [Fact]
    public void Location_serialization_round_trips_nested_containers()
    {
        var zip = new Location(Schemes.Zip, "dir/sub", Location.FileSystem(@"C:\a b\x.zip"), session: "cp437");
        var text = zip.Serialize();
        var back = Location.Deserialize(text);
        Assert.Equal(zip, back);
        Assert.Null(Location.Deserialize("{broken"));
    }

    [Fact]
    public void State_store_recovers_from_corruption_and_refuses_newer_schema()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "settings.json");
        var s = new AppSettings { Theme = "Cyberpunk" };
        JsonFileStore.Save(path, s, StateJsonContext.Default.AppSettings);
        JsonFileStore.Save(path, new AppSettings { Theme = "Psychedelic" }, StateJsonContext.Default.AppSettings);

        var loaded = JsonFileStore.Load(path, StateJsonContext.Default.AppSettings, AppSettings.CurrentSchema, () => new AppSettings(), out var st);
        Assert.Equal(StateLoadStatus.Loaded, st);
        Assert.Equal("Psychedelic", loaded.Theme);

        File.WriteAllText(path, "{ not json");
        loaded = JsonFileStore.Load(path, StateJsonContext.Default.AppSettings, AppSettings.CurrentSchema, () => new AppSettings(), out st);
        Assert.Equal(StateLoadStatus.RecoveredFromBackup, st);
        Assert.Equal("Cyberpunk", loaded.Theme);
        Assert.NotEmpty(Directory.GetFiles(dir.Path, "settings.json.corrupt-*"));

        File.WriteAllText(path, "{\"SchemaVersion\": 99, \"Theme\": \"Future\"}");
        loaded = JsonFileStore.Load(path, StateJsonContext.Default.AppSettings, AppSettings.CurrentSchema, () => new AppSettings(), out st);
        Assert.Equal(StateLoadStatus.NewerSchemaReadOnly, st);
    }

    [Fact]
    public void Workspace_state_round_trips_locations()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "ws.json");
        var ws = new WorkspaceState
        {
            Panels = [new PanelState { Tabs = [new TabState { Location = Location.FileSystem(dir.Path), Locked = true }] }],
        };
        JsonFileStore.Save(path, ws, StateJsonContext.Default.WorkspaceState);
        var back = JsonFileStore.Load(path, StateJsonContext.Default.WorkspaceState, 1, () => new WorkspaceState(), out _);
        Assert.Equal(Location.FileSystem(dir.Path), back.Panels[0].Tabs[0].Location);
        Assert.True(back.Panels[0].Tabs[0].Locked);
    }

    [Fact]
    public void FileCats_own_folders_are_its_users_alone_on_Linux_and_macOS()
    {
        // Release plan P16: history, journals, previews and hex originals are file names and contents; under the usual
        // umask other local accounts could read them. A folder made before (0755) is tightened when FileCat starts.
        if (OperatingSystem.IsWindows()) Assert.Skip("Windows keeps the user's application data private by its ACLs.");
        using var dir = new TempDir();
        string root = Path.Combine(dir.Path, "state");
        Directory.CreateDirectory(root);
        File.SetUnixFileMode(root, (UnixFileMode)0x1ED); // 0755, as an earlier start left it
        var paths = FileCat.Core.State.AppPaths.Resolve(overrideRoot: root);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
        Assert.True(Directory.Exists(paths.JournalDirectory) && Directory.Exists(paths.TempDirectory));
    }
}
