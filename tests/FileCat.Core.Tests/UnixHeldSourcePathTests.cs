using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Recovery;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

/// <summary>Owned regular files stand in for Unix descriptors; no device, authorization or UI opens.</summary>
public sealed class UnixHeldSourcePathTests
{
    [Theory]
    [InlineData("replace", false)]
    [InlineData("remove", false)]
    [InlineData("retarget", false)]
    [InlineData("unchanged", true)]
    [InlineData("same-symlink", true)]
    public async Task Recovery_admission_requires_the_current_path_to_name_its_held_Unix_source(string route, bool allowed)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Native descriptor/path identities require Linux or macOS.");
        using var root = new TempDir();
        byte[] recovered = "whole bytes from the held source"u8.ToArray();
        byte[] replacement = "whole bytes from the new source "u8.ToArray();
        string original = Path.Combine(root.Path, "original.img"), other = Path.Combine(root.Path, "other.img");
        string path = Path.Combine(root.Path, "selected.img");
        WriteImage(original, recovered);
        WriteImage(other, replacement);
        bool linked = route is "retarget" or "same-symlink";
        if (linked) File.CreateSymbolicLink(path, original);
        else File.Move(original, path);
        string heldPath = linked ? original : Path.Combine(root.Path, "held.img");
        var selected = UnixFiles.Stat(path, followLinks: true)!.Value;
        var recovery = new RecoveryProvider();
        UnixDeviceSource? held = null;
        int topologyCalls = 0;
        recovery.OpenDevice = (p, description, ct) => held = UnixDeviceSource.Open(p, description, ct);
        recovery.SharesDisk = (_, _) => { topologyCalls++; return false; };
        var location = recovery.ForDevice(path, "owned descriptor", 1);
        var sink = new Sink();
        await recovery.EnumerateAsync(location.WithPath("docs"), sink, TestContext.Current.CancellationToken);
        var report = Assert.Single(sink.Entries, e => e.Name == "_EPORT.TXT");
        Assert.NotNull(held);
        if (route is "replace" or "remove")
        {
            File.Move(path, heldPath);
            if (route == "replace") File.Move(other, path);
        }
        else if (linked)
        {
            File.Delete(path);
            File.CreateSymbolicLink(path, route == "retarget" ? other : original);
        }
        var current = UnixFiles.Stat(path, followLinks: true);
        bool same = current is { } value && value.Device == selected.Device && value.Inode == selected.Inode;
        Assert.Equal(allowed, same);
        byte[] heldBytes = new byte[recovered.Length];
        Assert.Equal(heldBytes.Length, held.Read(2560, heldBytes));
        Assert.Equal(recovered, heldBytes);
        string destination = root.Dir("destination");
        byte[] marker = "preserved destination bytes"u8.ToArray();
        File.WriteAllBytes(Path.Combine(destination, "keep.bin"), marker);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(recovery);
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root.Path, "journal"));
        try
        {
            var admission = recovery.CheckTransferDestination(location, destination);
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [new ItemRef(location.WithPath("docs"), report.Name, EntryKind.File, report.Size)],
                Destination = Location.FileSystem(destination),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Ask },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned descriptor copy did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            var files = Directory.GetFiles(destination).Order(StringComparer.Ordinal).Select(p => new
            {
                Name = Path.GetFileName(p), Bytes = new FileInfo(p).Length,
                SHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))),
            }).ToArray();
            TestContext.Current.TestOutputHelper?.WriteLine("UNIX_HELD_PATH_OBSERVATION " + JsonSerializer.Serialize(new
            {
                route, allowed, selected.Device, selected.Inode, Current = current, SameEntry = same,
                HeldBytesSHA256 = Convert.ToHexString(SHA256.HashData(heldBytes)),
                HeldImageSHA256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(route is "replace" or "remove" ? heldPath : linked ? original : path))),
                Admission = admission, ScanIssues = sink.Issues, topologyCalls, job.State, job.BytesDone,
                Issues = job.Issues.Select(x => x.Message), Files = files,
            }));
            Assert.Equal(marker, File.ReadAllBytes(Path.Combine(destination, "keep.bin")));
            if (allowed)
            {
                Assert.Null(admission);
                Assert.Equal(JobState.Completed, job.State);
                Assert.Equal(recovered, File.ReadAllBytes(Path.Combine(destination, report.Name)));
                Assert.True(topologyCalls > 0);
            }
            else
            {
                Assert.NotNull(admission);
                Assert.Contains("changed or was removed", admission, StringComparison.Ordinal);
                Assert.Equal(JobState.Failed, job.State);
                Assert.Equal(0, job.BytesDone);
                Assert.Equal(0, topologyCalls);
                Assert.False(File.Exists(Path.Combine(destination, report.Name)));
                Assert.Equal(["keep.bin"], files.Select(x => x.Name));
            }
        }
        finally
        {
            recovery.CloseAll();
        }
    }

    private static void WriteImage(string path, byte[] content)
    {
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
            bytes[fat + 3] = 0xFF; bytes[fat + 4] = 0x0F;
        }
        "DOCS       "u8.CopyTo(bytes.AsSpan(1536));
        bytes[1536 + 11] = 0x10; bytes[1536 + 12] = 0x08;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1536 + 26), 2);
        "REPORT  TXT"u8.CopyTo(bytes.AsSpan(2048));
        bytes[2048] = 0xE5; bytes[2048 + 11] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2048 + 26), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2048 + 28), (uint)content.Length);
        content.CopyTo(bytes.AsSpan(2560));
        File.WriteAllBytes(path, bytes);
    }

    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public List<string> Issues { get; } = [];
        public void ReportIssue(string message) => Issues.Add(message);
    }
}
