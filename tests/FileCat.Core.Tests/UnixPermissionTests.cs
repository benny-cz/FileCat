using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class UnixPermissionTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose()
    {
        // Folders made unwritable by a test must become writable again before they can be removed.
        if (!OperatingSystem.IsWindows())
        {
            foreach (var d in Directory.EnumerateDirectories(_dir.Path, "*", SearchOption.AllDirectories).Prepend(_dir.Path))
            {
                try { File.SetUnixFileMode(d, (UnixFileMode)0x1C0); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        _dir.Dispose();
    }

    [Theory]
    [InlineData(0x1ED, "rwxr-xr-x", "755")]
    [InlineData(0x1A4, "rw-r--r--", "644")]
    [InlineData(0x180, "rw-------", "600")]
    [InlineData(0x9ED, "rwsr-xr-x", "4755")]
    [InlineData(0x5A4, "rw-r-Sr--", "2644")]
    [InlineData(0x3FF, "rwxrwxrwt", "1777")]
    [InlineData(0x3A4, "rw-r--r-T", "1644")]
    [InlineData(0, "---------", "000")]
    public void Modes_read_as_ls_and_chmod_write_them(int mode, string text, string octal)
    {
        Assert.Equal(text, UnixPermissions.Format((UnixFileMode)mode));
        Assert.Equal(octal, UnixPermissions.Octal((UnixFileMode)mode));
        Assert.True(UnixPermissions.TryParseOctal(octal, out var parsed));
        Assert.Equal((UnixFileMode)mode, parsed);
    }

    [Theory]
    [InlineData("75")]
    [InlineData("0x1ed")]
    [InlineData("778")]
    [InlineData("12345")]
    [InlineData("")]
    [InlineData(null)]
    public void Only_three_or_four_octal_digits_are_a_mode(string? text) => Assert.False(UnixPermissions.TryParseOctal(text, out _));

    [Fact]
    public void Execute_reaches_files_inside_folders_only_when_they_are_already_executable()
    {
        var addExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
        var document = (UnixFileMode)0x1A4; // rw-r--r--
        var script = (UnixFileMode)0x1E4; // rwxr--r--
        // Marked items get exactly what was chosen; inside folders, documents stay documents (chmod's X).
        Assert.Equal((UnixFileMode)0x1ED, UnixPermissions.Apply(document, addExecute, 0, isDirectory: false, inside: false));
        Assert.Equal(document, UnixPermissions.Apply(document, addExecute, 0, isDirectory: false, inside: true));
        Assert.Equal((UnixFileMode)0x1ED, UnixPermissions.Apply(script, addExecute, 0, isDirectory: false, inside: true));
        Assert.Equal((UnixFileMode)0x1ED, UnixPermissions.Apply((UnixFileMode)0x1A4, addExecute, 0, isDirectory: true, inside: true));
        // Clearing is literal everywhere.
        Assert.Equal((UnixFileMode)0x1C0, UnixPermissions.Apply((UnixFileMode)0x1FF, 0, (UnixFileMode)0x3F, isDirectory: true, inside: true));
    }

    [Fact]
    public void Copies_drop_set_id_bits_and_are_filled_privately()
    {
        Assert.Equal((UnixFileMode)0x1ED, UnixPermissions.ForCopy((UnixFileMode)0xDED, isDirectory: false)); // 6755 → 755
        Assert.Equal((UnixFileMode)0x5ED, UnixPermissions.ForCopy((UnixFileMode)0xDED, isDirectory: true)); // folders keep set-group-ID
        Assert.Equal((UnixFileMode)0x180, UnixPermissions.WhileCopying((UnixFileMode)0x100, isDirectory: false)); // r-------- fills as rw-------
        Assert.Equal((UnixFileMode)0x1ED, UnixPermissions.WhileCopying((UnixFileMode)0x16D, isDirectory: true)); // r-xr-xr-x folder fills as rwxr-xr-x
    }

    [Fact]
    public void An_entry_reports_its_mode_owner_and_group()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("POSIX permissions are a Linux and macOS feature.");
        var file = _dir.File("private.key", "secret");
        File.SetUnixFileMode(file, (UnixFileMode)0x180);
        var stat = UnixPermissions.Stat(file);
        Assert.NotNull(stat);
        Assert.Equal((UnixFileMode)0x180, stat.Value.Mode);
        Assert.Equal(Environment.UserName, UnixPermissions.UserName(stat.Value.Uid));
        Assert.False(string.IsNullOrEmpty(UnixPermissions.GroupName(stat.Value.Gid)));
        Assert.Equal("4294967", UnixPermissions.UserName(4294967)); // an ID without a name shows as its number

        // A link reports its own entry, not its target's.
        var link = Path.Combine(_dir.Path, "link");
        File.CreateSymbolicLink(link, file);
        Assert.NotEqual((UnixFileMode)0x180, UnixPermissions.Stat(link)!.Value.Mode);
        Assert.Null(UnixPermissions.Stat(Path.Combine(_dir.Path, "missing")));
    }

    [Fact]
    public void Permission_columns_cover_files_and_folders()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("POSIX permissions are a Linux and macOS feature.");
        var folder = _dir.Dir("shared");
        File.SetUnixFileMode(folder, (UnixFileMode)0x1E8); // rwxr-x---
        var entry = new EntryData { Name = "shared", Kind = EntryKind.Directory };
        using var io = new Core.Threading.DeviceIoScheduler();
        var service = new MetadataService(io);
        var value = service.Compute("permissions", folder, entry, CancellationToken.None);
        Assert.Equal(MetadataState.Available, value.State);
        Assert.Equal("rwxr-x---", BuiltInFields.Permissions.Format(value.Value));
        Assert.Equal(Environment.UserName, service.Compute("owner", folder, entry, CancellationToken.None).Value);
        Assert.Contains(BuiltInFields.All, f => f.Id == "group");
        Assert.DoesNotContain(BuiltInFields.All, f => f.Id == "version");
    }

    private JobManager Jobs()
    {
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        return new JobManager(new PortableFileOperations(), providers, Path.Combine(_dir.Path, "journal"));
    }

    private static async Task<Job> WaitAsync(Job job)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Job still {job.State}");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    private static ItemRef Item(string path) => ItemRef.ForFileSystemPath(path, Directory.Exists(path) ? EntryKind.Directory : EntryKind.File);

    [Fact]
    public async Task A_copied_private_folder_stays_private()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("POSIX permissions are a Linux and macOS feature.");
        var ssh = _dir.Dir("src/.ssh");
        var key = _dir.File("src/.ssh/id_ed25519", "private key");
        var tool = _dir.File("src/.ssh/tool", "#!/bin/sh");
        File.SetUnixFileMode(key, (UnixFileMode)0x180); // rw-------
        File.SetUnixFileMode(tool, (UnixFileMode)0x9ED); // rwsr-xr-x
        File.SetUnixFileMode(ssh, (UnixFileMode)0x1C0); // rwx------
        var dst = _dir.Dir("dst");
        var job = await WaitAsync(Jobs().Submit(new JobRequest { Kind = JobKind.Copy, Sources = [Item(ssh)], Destination = Location.FileSystem(dst) }));
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal((UnixFileMode)0x1C0, File.GetUnixFileMode(Path.Combine(dst, ".ssh")));
        Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(Path.Combine(dst, ".ssh", "id_ed25519")));
        Assert.Equal((UnixFileMode)0x1ED, File.GetUnixFileMode(Path.Combine(dst, ".ssh", "tool"))); // no set-user-ID on a copy
        Assert.Equal("private key", File.ReadAllText(Path.Combine(dst, ".ssh", "id_ed25519")));
    }

    [Fact]
    public async Task A_recursive_change_reaches_folders_last_and_leaves_links_and_documents_alone()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("POSIX permissions are a Linux and macOS feature.");
        var root = _dir.Dir("site");
        var inner = _dir.Dir("site/inner");
        var page = _dir.File("site/inner/page.html", "<p>");
        var script = _dir.File("site/run.sh", "#!/bin/sh");
        var outside = _dir.File("outside.txt", "keep");
        File.SetUnixFileMode(page, (UnixFileMode)0x1B6); // rw-rw-rw-
        File.SetUnixFileMode(script, (UnixFileMode)0x1C0); // rwx------
        File.SetUnixFileMode(outside, (UnixFileMode)0x1B6);
        File.CreateSymbolicLink(Path.Combine(root, "to-outside"), outside);
        // Take write away from group and others; let everyone read, and run what is runnable (a+rX, go-w).
        var set = UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixPermissions.AnyExecute;
        var clear = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
        var job = await WaitAsync(Jobs().Submit(new JobRequest
        {
            Kind = JobKind.Attributes,
            Sources = [Item(root)],
            Attributes = new AttributeChangeSet(0, 0, null, null, Recursive: true, set, clear),
        }));
        Assert.Equal(JobState.Completed, job.State);
        Assert.Equal((UnixFileMode)0x1A4, File.GetUnixFileMode(page)); // rw-r--r--: a document stays a document
        Assert.Equal((UnixFileMode)0x1ED, File.GetUnixFileMode(script)); // rwxr-xr-x
        Assert.Equal((UnixFileMode)0x1ED, File.GetUnixFileMode(inner));
        Assert.Equal((UnixFileMode)0x1B6, File.GetUnixFileMode(outside)); // the link's target is untouched

        // Taking the owner's own access away works because each folder changes after everything inside it.
        job = await WaitAsync(Jobs().Submit(new JobRequest
        {
            Kind = JobKind.Attributes,
            Sources = [Item(root)],
            Attributes = new AttributeChangeSet(0, 0, null, null, Recursive: true, 0, UnixFileMode.UserRead | UnixFileMode.UserExecute),
        }));
        Assert.Equal(JobState.Completed, job.State);
        File.SetUnixFileMode(root, (UnixFileMode)0x1ED);
        File.SetUnixFileMode(inner, (UnixFileMode)0x1ED);
        Assert.Equal((UnixFileMode)0xA4, File.GetUnixFileMode(page)); // -w-r--r--
        Assert.Equal((UnixFileMode)0xAD, File.GetUnixFileMode(script)); // -w-r-xr-x
    }
}
