using System.Text;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Mtp;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// MTP through Windows Portable Devices. Listing devices always runs; the device scenario runs only with
/// FILECAT_MTP_TEST=1 and works exclusively inside a folder named FileCat-test on the first storage, which it removes.
/// </summary>
[Collection(Device)]
public sealed class MtpTests
{
    /// <summary>Device tests share one phone and its FileCat-test folder, so they run one after another.</summary>
    public const string Device = "MTP device";

    public const string TestFolder = "FileCat-test";

    [Fact]
    public void Listing_devices_never_fails_and_leaves_out_drives()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows Portable Devices exist only on Windows.");
        var devices = WpdSession.ListDevices();
        Assert.DoesNotContain(devices, d => d.Id.Contains("wpdbusenum", StringComparison.OrdinalIgnoreCase));
    }

    internal static (WpdSession Session, PortableObject Storage)? OpenTestDevice()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_MTP_TEST") != "1") return null;
        string? wanted = Environment.GetEnvironmentVariable("FILECAT_MTP_DEVICE");
        var device = WpdSession.ListDevices().FirstOrDefault(d => wanted is null || d.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase));
        if (device is null) return null;
        var session = WpdSession.Open(device.Id);
        var storage = session.Children("DEVICE", CancellationToken.None).FirstOrDefault(o => o.IsStorage);
        if (storage is null)
        {
            session.Dispose();
            return null;
        }
        return (session, storage);
    }

    [Fact]
    public void A_device_folder_can_be_created_filled_read_renamed_and_removed()
    {
        var opened = OpenTestDevice();
        if (opened is null) Assert.Skip("Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario.");
        var (session, storage) = opened.Value;
        var ct = TestContext.Current.CancellationToken;
        using (session)
        {
            // Only ever inside FileCat-test: an earlier run's leftover is removed first.
            if (session.Children(storage.Id, ct).FirstOrDefault(o => o.Name == TestFolder && o.IsFolder) is { } stale) session.Delete(stale.Id, recursive: true);
            string folder = session.CreateFolder(storage.Id, TestFolder);
            try
            {
                var payload = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("FileCat MTP test line\n", 5000)));
                using (var write = session.CreateFile(folder, "hello.txt", payload.Length)) write.Write(payload);
                var file = Assert.Single(session.Children(folder, ct));
                Assert.Equal(("hello.txt", (long)payload.Length, false), (file.Name, file.Size, file.IsFolder));

                using (var read = session.OpenRead(file.Id))
                {
                    var back = new MemoryStream();
                    read.CopyTo(back);
                    Assert.Equal(payload, back.ToArray());
                }

                session.Rename(file.Id, "renamed.txt");
                Assert.Equal("renamed.txt", Assert.Single(session.Children(folder, ct)).Name);

                string sub = session.CreateFolder(folder, "sub");
                using (var write = session.CreateFile(sub, "inner.bin", 3)) write.Write([1, 2, 3]);
                Assert.Equal(2, session.Children(folder, ct).Count);
                session.Delete(sub, recursive: true);
                session.Delete(Assert.Single(session.Children(folder, ct)).Id, recursive: false);
                Assert.Empty(session.Children(folder, ct));
            }
            finally
            {
                session.Delete(folder, recursive: true);
            }
            Assert.DoesNotContain(session.Children(storage.Id, ct), o => o.Name == TestFolder);
        }
    }

    [Fact]
    public async Task Jobs_upload_a_tree_download_it_rename_and_delete_inside_the_test_folder()
    {
        var opened = OpenTestDevice();
        if (opened is null) Assert.Skip("Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario.");
        var (session, storage) = opened.Value;
        string deviceId = session.Id;
        var ct = TestContext.Current.CancellationToken;
        if (session.Children(storage.Id, ct).FirstOrDefault(o => o.Name == TestFolder && o.IsFolder) is { } stale) session.Delete(stale.Id, recursive: true);
        session.CreateFolder(storage.Id, TestFolder);
        session.Dispose();

        var local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-mtp", Guid.NewGuid().ToString("N")[..8])).FullName;
        try
        {
            var tree = Directory.CreateDirectory(Path.Combine(local, "up", "album")).FullName;
            File.WriteAllText(Path.Combine(tree, "a.txt"), "alpha");
            Directory.CreateDirectory(Path.Combine(tree, "nested"));
            File.WriteAllBytes(Path.Combine(tree, "nested", "b.bin"), Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray());

            var mtp = new MtpProvider();
            var providers = new Core.Resources.ProviderRegistry();
            providers.Register(new WindowsFileSystemProvider());
            providers.Register(mtp);
            MtpJobs.Register();
            var jobs = new Core.Jobs.JobManager(new WindowsFileOperations(), providers, Path.Combine(local, "journal"));
            var testFolder = new Core.Resources.Location(Core.Resources.Schemes.Mtp, storage.Name + "/" + TestFolder, session: deviceId);

            async Task<Core.Jobs.Job> Run(Core.Jobs.JobRequest request)
            {
                var job = jobs.Submit(request);
                while (!job.State.IsFinished()) await Task.Delay(20, ct);
                Assert.True(job.State == Core.Jobs.JobState.Completed, $"{job.Title}: {job.State} {string.Join("; ", job.Issues.Select(i => i.Message))}");
                return job;
            }

            await Run(new Core.Jobs.JobRequest
            {
                Kind = Core.Jobs.JobKind.Copy,
                Sources = [Core.Resources.ItemRef.ForFileSystemPath(tree, Core.Resources.EntryKind.Directory)],
                Destination = testFolder,
            });
            var listed = new List<Core.Resources.EntryData>();
            await mtp.EnumerateAsync(testFolder.WithPath(testFolder.Path + "/album"), new ListSink(listed), ct);
            Assert.Equal(["a.txt", "nested"], listed.Select(e => e.Name).Order());

            var down = Directory.CreateDirectory(Path.Combine(local, "down")).FullName;
            await Run(new Core.Jobs.JobRequest
            {
                Kind = Core.Jobs.JobKind.Copy,
                Sources = [new Core.Resources.ItemRef(testFolder, "album", Core.Resources.EntryKind.Directory)],
                Destination = Core.Resources.Location.FileSystem(down),
            });
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(down, "album", "a.txt")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(tree, "nested", "b.bin")), File.ReadAllBytes(Path.Combine(down, "album", "nested", "b.bin")));

            await Run(new Core.Jobs.JobRequest { Kind = Core.Jobs.JobKind.Rename, Sources = [new Core.Resources.ItemRef(testFolder, "album", Core.Resources.EntryKind.Directory)], NewName = "album2" });
            await Run(new Core.Jobs.JobRequest { Kind = Core.Jobs.JobKind.CreateDirectory, Destination = testFolder, NewName = "empty" });
            await Run(new Core.Jobs.JobRequest
            {
                Kind = Core.Jobs.JobKind.Delete,
                Sources = [new Core.Resources.ItemRef(testFolder, "album2", Core.Resources.EntryKind.Directory), new Core.Resources.ItemRef(testFolder, "empty", Core.Resources.EntryKind.Directory)],
            });
            listed.Clear();
            await mtp.EnumerateAsync(testFolder, new ListSink(listed), ct);
            Assert.Empty(listed);
            mtp.CloseAll();
        }
        finally
        {
            using var cleanup = WpdSession.Open(deviceId);
            if (cleanup.Children(storage.Id, CancellationToken.None).FirstOrDefault(o => o.Name == TestFolder) is { } folder) cleanup.Delete(folder.Id, recursive: true);
            try { Directory.Delete(local, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Read-only (FILECAT_MTP_READTEST=1): any device, including an iPhone, whose storage Windows offers read-only. The
    /// first file found is read completely and at an earlier offset again; nothing is written and no names are reported.
    /// </summary>
    [Fact]
    public async Task A_device_file_reads_completely_and_again_from_an_earlier_offset()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_MTP_READTEST") != "1")
            Assert.Skip("Set FILECAT_MTP_READTEST=1 with an unlocked device to run the read-only check.");
        var device = WpdSession.ListDevices().FirstOrDefault();
        if (device is null) Assert.Skip("No portable device is connected.");
        var ct = TestContext.Current.CancellationToken;
        using (var session = WpdSession.Open(device.Id))
        {
            PortableObject? file = null;
            var pending = new Queue<(string Id, int Depth)>([("DEVICE", 0)]);
            while (file is null && pending.Count > 0)
            {
                var (id, depth) = pending.Dequeue();
                foreach (var child in session.Children(id, ct))
                {
                    if (!child.IsFolder && child.Size is > 0 and < 64 * 1024 * 1024) { file = child; break; }
                    if (child.IsFolder && depth < 4) pending.Enqueue((child.Id, depth + 1));
                }
            }
            if (file is null) Assert.Skip("The device offers no file to read.");
            using (var read = session.OpenRead(file.Id))
            {
                var all = new MemoryStream();
                read.CopyTo(all);
                Assert.Equal(file.Size, all.Length);
            }
        }
        // Through the provider: reading behind the current position starts a new transfer and gives the same bytes.
        var mtp = new MtpProvider();
        var root = new Core.Resources.Location(Core.Resources.Schemes.Mtp, string.Empty, session: device.Id);
        var folder = root;
        Core.Resources.EntryData found = default;
        for (int depth = 0; depth < 5 && found.Name is null; depth++)
        {
            var listed = new List<Core.Resources.EntryData>();
            await mtp.EnumerateAsync(folder, new ListSink(listed), ct);
            var candidate = listed.FirstOrDefault(e => e.Kind == Core.Resources.EntryKind.File && e.Size is > 1024 and < 64 * 1024 * 1024 && !e.Has(Core.Resources.EntryFlags.Unavailable));
            if (candidate.Name is not null) { found = candidate; break; }
            var next = listed.FirstOrDefault(e => e.Kind == Core.Resources.EntryKind.Directory && !e.Has(Core.Resources.EntryFlags.Unavailable));
            if (next.Name is null) break;
            folder = mtp.GetChildLocation(folder, next)!;
        }
        if (found.Name is null) Assert.Skip("The device offers no file to read through the provider.");
        using var content = mtp.OpenContent(mtp.GetItemRef(folder, found))!;
        var tail = new byte[512];
        var head = new byte[512];
        Assert.Equal(512, content.Read(content.Length - 512, tail));
        Assert.Equal(512, content.Read(0, head)); // backwards: a new transfer
        var again = new byte[512];
        Assert.Equal(512, content.Read(content.Length - 512, again));
        Assert.Equal(tail, again);
        mtp.CloseAll();
    }

    private sealed class ListSink(List<Core.Resources.EntryData> list) : Core.Resources.IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<Core.Resources.EntryData> entries)
        {
            foreach (var e in entries) list.Add(e);
        }

        public void ReportIssue(string message) { }
    }
}
