using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Recovery;

namespace FileCat.Core.Tests;

/// <summary>Owned regular images only: publishing a recovered item must not replace the image being recovered.</summary>
public sealed class RecoveryImageTargetTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("same-name")]
    [InlineData("new-name")]
    [InlineData("relative-folder")]
    [InlineData("container")]
    [InlineData("sibling-file")]
    public async Task Recovery_preserves_its_image_at_the_final_output_path(string route)
    {
        using var root = new TempDir();
        var recovery = new RecoveryProvider();
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider()); providers.Register(recovery);
        string destination = root.Dir("destination");
        string imageFolder = route switch
        {
            "relative-folder" => root.Dir("destination/nested"),
            "container" => root.Dir("destination/docs"),
            _ => destination,
        };
        string image = Path.Combine(imageFolder, route is "new-name" or "sibling-file" ? "owned.img" : "_EPORT.TXT");
        byte[] recovered = "whole owned deleted-file bytes"u8.ToArray();
        WriteImage(image, recovered); byte[] before = File.ReadAllBytes(image);
        string marker = Path.Combine(destination, "keep.bin"); byte[] markerBytes = "untouched owned marker"u8.ToArray(); File.WriteAllBytes(marker, markerBytes);
        var source = RecoveryProvider.ForImage(image, 1);
        var docs = source.WithPath("docs"); var sink = new Sink();
        await recovery.EnumerateAsync(docs, sink, TestContext.Current.CancellationToken);
        var report = Assert.Single(sink.Entries, e => e.Name == "_EPORT.TXT");
        var item = route == "container" ? new ItemRef(source, "docs", EntryKind.Directory) : recovery.GetItemRef(docs, report);
        if (route == "relative-folder") item = new ItemRef(item.Parent, item.Name, item.Kind, item.Size, item.Modified) { Flags = item.Flags, Ordinal = item.Ordinal, RelativeFolder = "nested" };
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Combine(root.Path, "journal"));
        var decisions = new List<DecisionRequest>();
        jobs.DecisionRequested += pending =>
        {
            decisions.Add(pending.Request);
            pending.Resolve(new Decision(DecisionAction.Skip));
        };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [item], Destination = Location.FileSystem(destination),
                NewName = route == "new-name" ? "owned.img" : null,
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            });
            var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned job did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            byte[] after = File.ReadAllBytes(image);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                route, image, job.State, job.BytesDone, Decisions = decisions.Select(d => new { Type = d.GetType().Name, d.Title, d.Message }),
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }),
                SourceBeforeBytes = Convert.ToBase64String(before), SourceAfterBytes = Convert.ToBase64String(after),
                SourceBeforeSHA256 = Convert.ToHexString(SHA256.HashData(before)), SourceAfterSHA256 = Convert.ToHexString(SHA256.HashData(after)),
                RecoveredBytes = Convert.ToBase64String(recovered),
                DestinationFiles = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Select(p => new { RelativePath = Path.GetRelativePath(destination, p), Length = new FileInfo(p).Length }),
            }));
            Assert.Equal(before, after); Assert.Equal(markerBytes, File.ReadAllBytes(marker));
            if (route == "sibling-file")
            {
                Assert.True(job.State is JobState.Completed or JobState.CompletedWithIssues);
                Assert.Equal(recovered, File.ReadAllBytes(Path.Combine(destination, report.Name)));
            }
            else
            {
                Assert.Equal(JobState.Failed, job.State); Assert.Equal(0, job.BytesDone);
                Assert.DoesNotContain(Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories), p => Path.GetFileName(p).StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
            }
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5)));
            recovery.CloseAll();
        }
    }
    private static void WriteImage(string path, byte[] content)
    {
        var bytes = new byte[64 * 512]; bytes[0] = 0xEB; bytes[1] = 0x3C; bytes[2] = 0x90;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), 512); bytes[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1); bytes[16] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(17), 16); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(19), 64);
        bytes[21] = 0xF8; BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(22), 1); bytes[510] = 0x55; bytes[511] = 0xAA;
        foreach (int fat in new[] { 512, 1024 }) { bytes[fat] = 0xF8; bytes[fat + 1] = 0xFF; bytes[fat + 2] = 0xFF; bytes[fat + 3] = 0xFF; bytes[fat + 4] = 0x0F; }
        "DOCS       "u8.CopyTo(bytes.AsSpan(1536)); bytes[1536 + 11] = 0x10; bytes[1536 + 12] = 0x08;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1536 + 26), 2);
        "REPORT  TXT"u8.CopyTo(bytes.AsSpan(2048)); bytes[2048] = 0xE5; bytes[2048 + 11] = 0x20;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2048 + 26), 3); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(2048 + 28), (uint)content.Length);
        content.CopyTo(bytes.AsSpan(2560)); File.WriteAllBytes(path, bytes);
    }
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Entries.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }
}
