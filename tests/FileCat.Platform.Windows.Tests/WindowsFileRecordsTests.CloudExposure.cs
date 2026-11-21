using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Search;
using FileCat.Core.Selection;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// FILECAT_CLOUD_PLACEHOLDER_EXPOSURE_PLAN.md, X-LIST, X-DETAILS and the native part of X-POLICY. This process serves a
/// sync root with one placeholder that is only in the cloud and local, in-sync placeholders (three files, a folder and a
/// file in it). A child copy of the test assembly runs in Windows's default disguised mode, as FileCat does:
/// <list type="bullet">
/// <item>its native listing shows every placeholder's cloud tag, while the flags operations follow stay the disguised listing's;</item>
/// <item>its details and file records describe each placeholder as a Cloud Files placeholder;</item>
/// <item>copy, move, delete and Find, started from the listing's own entries, treat them as ordinary files and folders;</item>
/// <item>the provider never receives a data request.</item>
/// </list>
/// </summary>
public sealed partial class WindowsFileRecordsTests
{
    private const string CloudExposureParent = "FILECAT_TEST_CLOUD_EXPOSURE_PARENT";
    private const string ExposureProviderName = "FileCat exposure test";

    private static readonly (string Relative, string Text)[] ExposureFiles =
    [
        ("kept.txt", "kept bytes"), ("moved.txt", "moved bytes"), ("gone.txt", "gone bytes"), (Path.Join("folder", "inner.txt"), "inner bytes"),
    ];

    private sealed class ExposureProvider
    {
        public bool Registered, Connected;
        public long Connection;
        public nint Table;
    }

    [Fact]
    public async Task Cloud_exposure_child_describes_and_operates_as_its_parent_asks()
    {
        string? parent = Environment.GetEnvironmentVariable(CloudExposureParent);
        if (string.IsNullOrEmpty(parent)) Assert.Skip("Started only by the cross-process Cloud Files exposure fixture.");
        var ct = TestContext.Current.CancellationToken;
        var log = TestContext.Current.TestOutputHelper;
        string root = Path.Join(parent, "sync"), output = Directory.CreateDirectory(Path.Join(parent, "out")).FullName;
        sbyte process = RtlQueryProcessPlaceholderCompatibilityMode(), arrived = PlaceholderView.ThreadMode();
        int thread = Environment.CurrentManagedThreadId;

        // X-LIST. The find loop runs on this thread and must leave it in the mode it had, so operations here stay disguised.
        var location = Location.FileSystem(root);
        var native = new CountingSink(); var managed = new CountingSink();
        await new WindowsFileSystemProvider { UseNativeEnumeration = true }.EnumerateAsync(location, native, ct);
        Assert.Equal(thread, Environment.CurrentManagedThreadId);
        Assert.Equal(arrived, PlaceholderView.ThreadMode());
        Assert.True((File.GetAttributes(Path.Join(root, "kept.txt")) & FileAttributes.ReparsePoint) == 0, "This thread no longer sees the exposed view.");
        await new LocalFileSystemProvider().EnumerateAsync(location, managed, ct);
        Assert.Equal(["cloud.txt", "folder", "gone.txt", "kept.txt", "moved.txt"], native.Entries.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(native.Entries.Keys.Order(StringComparer.Ordinal), managed.Entries.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, entry) in native.Entries)
        {
            var disguised = managed.Entries[name];
            log?.WriteLine($"Child: {name} tag 0x{entry.NativeReparseTag:X8}, attributes 0x{entry.Attributes:X8} (disguised 0x{disguised.Attributes:X8}), flags {entry.Flags}");
            Assert.True(ReparsePresentation.IsCloudTag(entry.NativeReparseTag), $"{name}: tag 0x{entry.NativeReparseTag:X8}");
            Assert.Equal(0u, disguised.NativeReparseTag);
            Assert.NotEqual(0u, entry.Attributes & (uint)FileAttributes.ReparsePoint);
            Assert.Equal(disguised.Attributes, entry.Attributes & ~(uint)WindowsFileSystemProvider.DisguisedPlaceholderAttributes);
            Assert.Equal(disguised.Flags, entry.Flags);
            Assert.False(entry.Has(EntryFlags.Link), name);
            Assert.False(EntryPresentation.IsLink(entry, location), name);
            Assert.Equal("Cloud-managed item", EntryPresentation.Kind(entry, location));
            Assert.Equal(string.Empty, EntryPresentation.Kind(disguised, location));
        }

        // X-DETAILS: the summary, the full report and the file record describe each placeholder as one.
        var records = new WindowsFileRecords();
        foreach (var (name, kind) in new[] { ("cloud.txt", FileObjectKind.File), ("kept.txt", FileObjectKind.File), ("folder", FileObjectKind.Directory) })
        {
            string path = Path.Join(root, name);
            var summary = records.ReadSummary(path, ct);
            Assert.Equal(kind, summary.Kind);
            Assert.True(summary.ReparseTag.State == FactState.Known && ReparsePresentation.IsCloudTag(summary.ReparseTag.Value),
                $"{name}: reparse tag {summary.ReparseTag.State} 0x{summary.ReparseTag.Value:X8}");
            Assert.Equal(FactState.Known, summary.Cloud.State);
            Assert.Equal(FactState.NotInspected, summary.Target.State);
            Assert.True(FileDetailsTargets.IsOrdinaryNavigationTarget(summary, windows: true), name);
            var full = records.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(summary.ReparseTag.Value, full.Summary.ReparseTag.Value);
            Assert.Equal(summary.Cloud, full.Summary.Cloud);
            var report = Read(path, privileged: false);
            var cloud = Section(report, "Cloud placeholder (Cloud Files)");
            Assert.StartsWith(ExposureProviderName + " 1.0 · ", Field(cloud, "Provider"), StringComparison.Ordinal);
            Assert.Equal("in sync (provider-reported)", Field(cloud, "In sync"));
            Assert.NotEmpty(Field(cloud, "Pin state"));
            Assert.Contains(" · file ID 0x", Field(cloud, "Sync root"), StringComparison.Ordinal);
            if (kind == FileObjectKind.File)
                Assert.Equal(name == "cloud.txt" ? $"{RecordText.Bytes(0)} of {RecordText.Bytes(5000)}: none of it (only in the cloud)"
                    : $"{RecordText.Bytes(10)} of {RecordText.Bytes(10)}: all of it", Field(cloud, "On this device"));
            else Assert.DoesNotContain(cloud.Fields, f => f.Name == "On this device");
            log?.WriteLine($"Child: {name} details tag 0x{summary.ReparseTag.Value:X8}; record: pin state {Field(cloud, "Pin state")}; provider {Field(cloud, "Provider")}");
        }
        Assert.Equal(arrived, PlaceholderView.ThreadMode());

        // X-POLICY: jobs and Find started from the listing's own entries treat the placeholders as ordinary files and folders.
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider { UseNativeEnumeration = true });
        var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Join(parent, "journal"));
        ItemRef Listed(string name) => ItemRef.FromEntry(location, native.Entries[name]);
        await RunExposureJob(jobs, new JobRequest { Kind = JobKind.Copy, Sources = [Listed("kept.txt"), Listed("folder")], Destination = Location.FileSystem(output) });
        Assert.Equal("kept bytes", File.ReadAllText(Path.Join(output, "kept.txt")));
        Assert.Equal("inner bytes", File.ReadAllText(Path.Join(output, "folder", "inner.txt"))); // entered, not skipped as a link
        Assert.Equal((FileAttributes)0, File.GetAttributes(Path.Join(output, "folder")) & FileAttributes.ReparsePoint);
        await RunExposureJob(jobs, new JobRequest { Kind = JobKind.Move, Sources = [Listed("moved.txt")], Destination = Location.FileSystem(Path.Join(root, "folder")) });
        Assert.Equal("moved bytes", File.ReadAllText(Path.Join(root, "folder", "moved.txt")));
        Assert.False(File.Exists(Path.Join(root, "moved.txt")));
        await RunExposureJob(jobs, new JobRequest { Kind = JobKind.Delete, Sources = [Listed("gone.txt")] });
        Assert.False(File.Exists(Path.Join(root, "gone.txt")));
        Assert.True(Mask.TryParse("inner.txt", out var inner, out _));
        var found = new ResultSet("exposure", "exposure", "exposure");
        new SearchSession(new SearchQuery { Roots = [root], Names = inner, IncludeDirectories = false }, found).Run(ct);
        Assert.True(found.IsComplete);
        Assert.Equal([Path.Join("folder", "inner.txt")], found.Snapshot().Select(r => Path.GetRelativePath(root, r.Item.FileSystemPath!)));
        log?.WriteLine("Child: copied kept.txt and folder out, moved moved.txt into folder, deleted gone.txt, found folder\\inner.txt");

        Assert.Equal(arrived, PlaceholderView.ThreadMode());
        Assert.Equal(process, RtlQueryProcessPlaceholderCompatibilityMode());
    }

    [Fact]
    public async Task Another_process_describes_placeholders_as_cloud_files_and_operates_on_them_as_ordinary_ones()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Cloud Files are Windows'.");
        if (!Opted("FILECAT_TEST_CLOUD_FILES")) Assert.Skip("Set FILECAT_TEST_CLOUD_FILES=1 to register a temporary Cloud Files sync root in a test folder.");
        string parent = NewFolder(), root = Path.Join(parent, "sync");
        Directory.CreateDirectory(root);
        var provider = new ExposureProvider();
        int fetchesBefore = Volatile.Read(ref _fetches);
        try
        {
            if (!OnNtfs(root)) Assert.Skip("The test folder is not on NTFS.");
            ServeExposureFixture(root, provider);
            string exe = Path.ChangeExtension(typeof(WindowsFileRecordsTests).Assembly.Location, ".exe");
            await RunCloudChild(exe, nameof(Cloud_exposure_child_describes_and_operates_as_its_parent_asks), new() { [CloudExposureParent] = parent }, "exposure");
            Assert.True(Volatile.Read(ref _fetches) == fetchesBefore, "The provider received a data request.");
            using (var handle = CreateFile(Path.Join(root, "cloud.txt"), 0x0010_0080, 7, 0, 3, 0x0220_0000, 0))
            {
                var after = WindowsFileRecords.PlaceholderInfo(handle, [], out string? failed);
                Assert.True(after is not null, failed);
                Assert.Equal(0L, after.OnDiskDataSize); // still only in the cloud
            }
            // Moved inside the sync root, the placeholder stayed one.
            using (var handle = CreateFile(Path.Join(root, "folder", "moved.txt"), 0x0010_0080, 7, 0, 3, 0x0220_0000, 0))
                Assert.True(WindowsFileRecords.PlaceholderInfo(handle, [], out string? failed) is not null, failed);
        }
        finally
        {
            try
            {
                // The placeholders first, while their provider is connected.
                if (provider.Connected)
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.Delete(file);
                    foreach (string folder in Directory.EnumerateDirectories(root)) Directory.Delete(folder, recursive: true);
                }
            }
            finally
            {
                if (provider.Connected) CfDisconnectSyncRoot(provider.Connection);
                if (provider.Registered) CfUnregisterSyncRoot(root);
                if (provider.Table != 0) FreeTable(provider.Table);
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    private static async Task RunExposureJob(JobManager jobs, JobRequest request)
    {
        var job = jobs.Submit(request);
        var deadline = DateTime.UtcNow.AddMinutes(1);
        while (!job.State.IsFinished())
        {
            if (job.Decision is { } asked)
            {
                asked.Resolve(new Decision(DecisionAction.CancelJob));
                Assert.Fail($"{request.Kind} asked \"{asked.Request.Title}: {asked.Request.Message}\"");
            }
            Assert.True(DateTime.UtcNow < deadline, $"{request.Kind} did not finish within a minute.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.True(job.State == JobState.Completed, $"{request.Kind}: {job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}");
    }

    /// <summary>
    /// Registers <paramref name="root"/> for a provider that serves no data, creates a placeholder that is only in the cloud,
    /// and converts local files and a folder into in-sync placeholders. Records each step in <paramref name="provider"/>.
    /// </summary>
    private static unsafe void ServeExposureFixture(string root, ExposureProvider provider)
    {
        fixed (char* name = ExposureProviderName)
        fixed (char* version = "1.0")
        fixed (byte* rootIdentity = "filecat-exposure-root"u8)
        {
            var registration = new CfSyncRegistration
            {
                StructSize = (uint)sizeof(CfSyncRegistration), ProviderName = (nint)name, ProviderVersion = (nint)version,
                SyncRootIdentity = (nint)rootIdentity, SyncRootIdentityLength = 21, ProviderId = Guid.NewGuid(),
            };
            var policies = new CfSyncPolicies { StructSize = (uint)sizeof(CfSyncPolicies), HydrationPrimary = 2, PopulationPrimary = 3 };
            int registered = CfRegisterSyncRoot(root, &registration, &policies, 0x2 | 0x4);
            Assert.True(registered >= 0, $"CfRegisterSyncRoot: 0x{registered:X8}");
            provider.Registered = true;
        }
        var table = (CfCallbackRegistration*)NativeMemory.AllocZeroed((nuint)(2 * sizeof(CfCallbackRegistration)));
        provider.Table = (nint)table;
        table[0] = new CfCallbackRegistration { Type = 0 /* FETCH_DATA */, Callback = (nint)(delegate* unmanaged[Stdcall]<nint, nint, void>)&Fetched };
        table[1] = new CfCallbackRegistration { Type = -1 };
        int connect = CfConnectSyncRoot(root, table, 0, 0, out long connection);
        Assert.True(connect >= 0, $"CfConnectSyncRoot: 0x{connect:X8}");
        (provider.Connected, provider.Connection) = (true, connection);

        long now = DateTime.UtcNow.ToFileTimeUtc();
        var identity = "filecat-exposure-cloud"u8.ToArray();
        fixed (char* relative = "cloud.txt")
        fixed (byte* id = identity)
        {
            var create = new CfPlaceholderCreateInfo
            {
                RelativeFileName = (nint)relative, CreationTime = now, LastAccessTime = now, LastWriteTime = now, ChangeTime = now,
                FileAttributes = 0x80, FileSize = 5000, FileIdentity = (nint)id, FileIdentityLength = (uint)identity.Length, Flags = 0x2 /* in sync */,
            };
            int made = CfCreatePlaceholders(root, &create, 1, 0, out uint processed);
            Assert.True(made >= 0 && processed == 1 && create.Result >= 0, $"CfCreatePlaceholders: 0x{made:X8}");
        }
        Directory.CreateDirectory(Path.Join(root, "folder"));
        foreach (var (relative, text) in ExposureFiles) File.WriteAllText(Path.Join(root, relative), text);
        foreach (string relative in ExposureFiles.Select(f => f.Relative).Append("folder")) ConvertToPlaceholder(Path.Join(root, relative));
    }

    private static unsafe void ConvertToPlaceholder(string path)
    {
        using var handle = CreateFile(path, 0xC000_0000 /* read, write */, 7, 0, 3, 0x0200_0000 /* folders too */, 0);
        Assert.False(handle.IsInvalid, $"Open {path} to convert it: {Marshal.GetLastPInvokeError()}");
        byte[] identity = Encoding.UTF8.GetBytes("filecat-exposure:" + Path.GetFileName(path));
        fixed (byte* id = identity)
        {
            int converted = CfConvertToPlaceholder(handle, id, (uint)identity.Length, 0x1 /* mark in sync */, null, 0);
            Assert.True(converted >= 0, $"CfConvertToPlaceholder {Path.GetFileName(path)}: 0x{converted:X8}");
        }
    }

    [LibraryImport("cldapi.dll")]
    private static unsafe partial int CfConvertToPlaceholder(SafeFileHandle handle, byte* identity, uint identityLength, uint flags, long* usn, nint overlapped);
}
