using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>Recording topology, real copy paths and owned bytes; no device or physical source is opened.</summary>
public sealed class RecoveryDestinationAdmissionTests
{
    public static IEnumerable<object?[]> DirectoryCases()
    {
        foreach (string route in new[] { "new-folder", "existing-folder", "relative-folder", "linked-folder" })
        foreach (bool? same in new bool?[] { true, null, false })
            yield return [route, same];
    }

    [Theory]
    [MemberData(nameof(DirectoryCases))]
    public async Task Every_recovery_destination_folder_is_checked_before_writing(string route, bool? same)
    {
        using var root = new TempDir();
        var recovery = new RecoveryProvider();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(recovery);
        byte[] recovered = "whole owned deleted-file bytes"u8.ToArray();
        string image = Path.Combine(root.Path, "owned-fat12.img");
        WriteImage(image, recovered);
        byte[] sourceBefore = File.ReadAllBytes(image);
        recovery.OpenDevice = (_, _, _) => new ImageFileSource(image);
        var location = recovery.ForDevice("owned-recording-device", "owned recording source", 1);
        var listing = new Sink();
        await recovery.EnumerateAsync(location.WithPath("docs"), listing, TestContext.Current.CancellationToken);
        var report = Assert.Single(listing.Entries, e => e.Name == "_EPORT.TXT");
        string destination = root.Dir("destination");
        string name = route == "relative-folder" ? "nested" : "docs";
        string child = Path.Combine(destination, name);
        string actual = child;
        if (route == "linked-folder")
        {
            actual = root.Dir("linked-target");
            try { Directory.CreateSymbolicLink(child, actual); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                recovery.CloseAll();
                Assert.Skip("Owned directory symlink unavailable in this account: " + ex.GetType().Name);
                return;
            }
        }
        else if (route == "existing-folder") Directory.CreateDirectory(child);
        byte[] marker = "owned destination marker"u8.ToArray();
        if (Directory.Exists(actual)) File.WriteAllBytes(Path.Combine(actual, "keep.bin"), marker);
        var checks = new List<string>();
        recovery.SharesDisk = (_, folder) =>
        {
            checks.Add(folder);
            return PathUtil.IsSameOrUnder(Path.GetFullPath(folder), Path.GetFullPath(child)) ? same : false;
        };
        var item = route == "relative-folder"
            ? new ItemRef(location.WithPath("docs"), report.Name, report.Kind, report.Size) { RelativeFolder = "nested" }
            : new ItemRef(location, "docs", EntryKind.Directory);
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root.Path, "journal"));
        try
        {
            Job job = await Run(jobs, item, destination);
            Observe(new
            {
                route, same, job.State, job.BytesDone, Checks = checks,
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                SourceBeforeSHA256 = Convert.ToHexString(SHA256.HashData(sourceBefore)),
                SourceAfterSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(image))),
                ChildExists = Directory.Exists(child),
                LinkTarget = new DirectoryInfo(child).LinkTarget,
                ActualFiles = Directory.Exists(actual) ? Directory.GetFiles(actual).Order(StringComparer.Ordinal).Select(p =>
                    new { Name = Path.GetFileName(p), Bytes = new FileInfo(p).Length, SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) }).ToArray() : [],
            });
            Assert.Equal(sourceBefore, File.ReadAllBytes(image));
            if (route is "existing-folder" or "linked-folder")
                Assert.Equal(marker, File.ReadAllBytes(Path.Combine(actual, "keep.bin")));
            if (same == false)
            {
                Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues);
                Assert.DoesNotContain(job.Issues, i => i.Message.Contains("same physical disk", StringComparison.Ordinal) || i.Message.Contains("cannot tell", StringComparison.Ordinal));
                Assert.Equal(recovered, File.ReadAllBytes(Path.Combine(actual, report.Name)));
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State);
                Assert.Contains(job.Issues, i => i.Message.Contains(same == true ? "same physical disk" : "cannot tell", StringComparison.Ordinal));
                Assert.False(File.Exists(Path.Combine(actual, report.Name)));
                if (route is "new-folder" or "relative-folder") Assert.False(Directory.Exists(child));
                else Assert.Equal(["keep.bin"], Directory.GetFiles(actual).Select(Path.GetFileName));
                Assert.DoesNotContain(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories),
                    f => Path.GetFileName(f).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
                Assert.Contains(child, checks);
            }
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5)));
            recovery.CloseAll();
        }
    }

    public static IEnumerable<object?[]> LateCases()
    {
        foreach (string moment in new[] { "conflict", "open", "metadata" })
        foreach (bool? same in new bool?[] { true, null, false })
            yield return [moment, same];
    }

    [Theory]
    [MemberData(nameof(LateCases))]
    public async Task A_destination_changed_before_staging_is_refused_and_closes_content(string moment, bool? same)
    {
        using var root = new TempDir();
        string destination = root.Dir("destination");
        byte[] incoming = "whole owned recovery transfer bytes"u8.ToArray();
        byte[] original = "existing owned target"u8.ToArray();
        string target = Path.Combine(destination, "copy.bin");
        if (moment == "conflict") File.WriteAllBytes(target, original);
        var provider = new LateProvider(incoming, moment, same);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(provider);
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root.Path, "journal"));
        jobs.DecisionRequested += request =>
        {
            Assert.Equal("conflict", moment);
            provider.Changed = true;
            request.Resolve(new Decision(DecisionAction.Replace));
        };
        try
        {
            var job = await Run(jobs, new ItemRef(new Location(provider.Scheme, ""), "copy.bin", EntryKind.File, incoming.Length), destination);
            Observe(new
            {
                moment, same, job.State, job.BytesDone, provider.Changed, provider.Opens, provider.Closes, provider.Reads,
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                ActualFiles = Directory.GetFiles(destination).Order(StringComparer.Ordinal).Select(p =>
                    new { Name = Path.GetFileName(p), Bytes = new FileInfo(p).Length, SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))) }).ToArray(),
            });
            Assert.True(provider.Changed);
            Assert.Equal(1, provider.Opens);
            Assert.Equal(1, provider.Closes);
            if (same == false)
            {
                Assert.Equal(JobState.Completed, job.State);
                Assert.Equal(incoming, File.ReadAllBytes(target));
                Assert.True(provider.Reads > 0);
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State);
                Assert.Equal(0, provider.Reads);
                Assert.Equal(0, job.BytesDone);
                Assert.Contains(job.Issues, i => i.Message.Contains(same == true ? "same physical disk" : "cannot tell", StringComparison.Ordinal));
                if (moment == "conflict") Assert.Equal(original, File.ReadAllBytes(target));
                else Assert.False(File.Exists(target));
                Assert.Equal(moment == "conflict" ? ["copy.bin"] : Array.Empty<string>(),
                    Directory.GetFiles(destination).Select(Path.GetFileName));
            }
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5)));
        }
    }

    private static void WriteImage(string path, byte[] content)
    {
        // One owned 32 KiB FAT12 volume: a live docs folder and one deleted, unallocated file.
        var bytes = new byte[64 * 512];
        bytes[0] = 0xEB; bytes[1] = 0x3C; bytes[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), 512);
        bytes[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1);
        bytes[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(17), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(19), 64);
        bytes[21] = 0xF8;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1);
        bytes[510] = 0x55; bytes[511] = 0xAA;
        foreach (int fat in new[] { 512, 1024 })
        {
            bytes[fat] = 0xF8; bytes[fat + 1] = 0xFF; bytes[fat + 2] = 0xFF;
            bytes[fat + 3] = 0xFF; bytes[fat + 4] = 0x0F; // allocated folder cluster2; file cluster3 free
        }
        "DOCS       "u8.CopyTo(bytes.AsSpan(1536));
        bytes[1536 + 11] = 0x10; bytes[1536 + 12] = 0x08; // lowercase docs
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1536 + 26), 2);
        "REPORT  TXT"u8.CopyTo(bytes.AsSpan(2048));
        bytes[2048] = 0xE5; bytes[2048 + 11] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2048 + 26), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2048 + 28), (uint)content.Length);
        content.CopyTo(bytes.AsSpan(2560));
        File.WriteAllBytes(path, bytes);
    }

    private static void Observe(object value) =>
        TestContext.Current.TestOutputHelper?.WriteLine("RECOVERY_DESTINATION_OBSERVATION " + JsonSerializer.Serialize(value));

    private static async Task<Job> Run(JobManager jobs, ItemRef source, string destination)
    {
        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [source], Destination = Location.FileSystem(destination),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Ask },
        });
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned job did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    private sealed class LateProvider(byte[] bytes, string moment, bool? same) : ResourceProvider
    {
        private readonly byte[] _bytes = bytes;
        private readonly string _moment = moment;
        private readonly RecoveryProvider _guard = new() { SharesDisk = (_, _) => same };
        private readonly Location _device = new(Schemes.Recovery, "", new Location(Schemes.Device, "owned-recording-device"));
        public bool Changed;
        public int Opens, Closes, Reads;
        public override string Scheme => "owned-recovery-destination";
        public override string GetDisplayPath(Location location) => location.Path;
        public override Location? GetParent(Location location) => null;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override string? CheckTransferDestination(Location source, string destinationDirectory) =>
            Changed ? _guard.CheckTransferDestination(_device, destinationDirectory) : null;
        public override IContentSource OpenContent(ItemRef item)
        {
            Opens++;
            if (moment == "open") Changed = true;
            return new Source(this);
        }
        private sealed class Source(LateProvider owner) : IContentSource
        {
            public string DisplayName => "owned recovery admission bytes";
            public long Length => owner._bytes.Length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision()
            {
                if (owner._moment == "metadata") owner.Changed = true;
                return null;
            }
            public int Read(long offset, Span<byte> buffer)
            {
                owner.Reads++;
                int n = (int)Math.Min(buffer.Length, Math.Max(0, Length - offset));
                owner._bytes.AsSpan((int)offset, n).CopyTo(buffer);
                return n;
            }
            public void Dispose() => owner.Closes++;
        }
    }
}
