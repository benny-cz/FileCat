using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows;

namespace FileCat.App.Tests;

public sealed class ElevatedExtractionAndZipSourceTests
{
    private sealed class SignalMediumSwap(string child, string trigger, string result) : WindowsFileOperations
    {
        public bool Attempted { get; private set; }

        public override IDisposable HoldDirectory(string path, string? root = null)
        {
            var lease = base.HoldDirectory(path, root);
            if (!Attempted && Path.GetFullPath(path).Equals(Path.GetFullPath(child), StringComparison.OrdinalIgnoreCase))
            {
                Attempted = true;
                File.WriteAllText(trigger, "The high-token job holds the child directory.");
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(result))
                {
                    if (DateTime.UtcNow >= deadline) throw new IOException("The medium writer did not respond while the directory was held.");
                    Thread.Sleep(20);
                }
            }
            return lease;
        }
    }

    private static string FixtureRoot()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_SECURITY_VM_LINK_FIXTURE");
        if (root is null) Assert.Skip("Run in the owned medium/high Windows fixture.");
        Assert.True(OperatingSystem.IsWindows() && WindowsTokenAuthority.IsHighIntegrity);
        return root!;
    }

    private static async Task<Job> Finish(Job job)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!job.State.IsFinished())
        {
            Assert.True(DateTime.UtcNow < deadline, "The owned security job did not finish.");
            job.Decision?.Resolve(new Decision(DecisionAction.Skip));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public async Task Native_high_token_ZIP_extraction_refuses_a_medium_staged_child_link()
    {
        string root = FixtureRoot();
        string destination = Path.Combine(root, "extract-destination");
        string outside = Path.Combine(root, "extract-outside");
        string link = Path.Combine(destination, "folder");
        string marker = Path.Combine(outside, "marker.txt");
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        Assert.Equal("OWNED-EXTRACT-OUTSIDE-UNTOUCHED", File.ReadAllText(marker));

        string work = Directory.CreateDirectory(Path.Combine(root, "ac20-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string archivePath = Path.Combine(work, "owned.zip");
            using (var file = File.Create(archivePath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("folder/inside.txt").Open()))
                writer.Write("OWNED-ARCHIVE-BYTES");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            providers.Register(new ZipProvider(Path.Combine(work, "spool")));
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(work, "journal"));
            JobRequest Request(string target) => new()
            {
                Kind = JobKind.Extract,
                Sources = [new ItemRef(ZipProvider.ForFile(archivePath), "folder", EntryKind.Directory)],
                Destination = Location.FileSystem(target),
            };

            var refused = await Finish(jobs.Submit(Request(destination)));
            Assert.NotEqual(JobState.Completed, refused.State);
            Assert.Equal("OWNED-EXTRACT-OUTSIDE-UNTOUCHED", File.ReadAllText(marker));
            Assert.False(File.Exists(Path.Combine(outside, "inside.txt")));
            Assert.Contains(refused.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase));

            string ordinary = Directory.CreateDirectory(Path.Combine(work, "ordinary")).FullName;
            var accepted = await Finish(jobs.Submit(Request(ordinary)));
            Assert.Equal(JobState.Completed, accepted.State);
            Assert.Equal("OWNED-ARCHIVE-BYTES", File.ReadAllText(Path.Combine(ordinary, "folder", "inside.txt")));
            File.WriteAllText(Path.Combine(root, "ac20-result.txt"),
                $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\nLink={link}\nOutsideMarker={File.ReadAllText(marker)}\nOutsideOutputExists={File.Exists(Path.Combine(outside, "inside.txt"))}\nRefusedState={refused.State}\nRefusedIssue={string.Join(" | ", refused.Issues.Select(i => i.Message))}\nOrdinaryState={accepted.State}\nOrdinaryBytes={File.ReadAllText(Path.Combine(ordinary, "folder", "inside.txt"))}\n");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Fact]
    public async Task Native_high_token_ZIP_extraction_holds_a_child_against_a_paced_medium_writer()
    {
        string root = FixtureRoot();
        string ready = Path.Combine(root, "ac20-paced-ready.txt");
        string trigger = Path.Combine(root, "ac20-paced-trigger.txt");
        string writerResult = Path.Combine(root, "ac20-paced-writer.txt");
        string destination = Directory.CreateDirectory(Path.Combine(root, "extract-paced-destination")).FullName;
        string child = Directory.CreateDirectory(Path.Combine(destination, "folder")).FullName;
        string outside = Path.Combine(root, "extract-outside");
        string marker = Path.Combine(outside, "marker.txt");
        var readyDeadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(ready))
        {
            Assert.True(DateTime.UtcNow < readyDeadline, "The medium writer was not ready.");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        Assert.Contains("S-1-16-8192", File.ReadAllText(ready));
        string work = Directory.CreateDirectory(Path.Combine(root, "ac20-paced-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string archivePath = Path.Combine(work, "owned.zip");
            using (var file = File.Create(archivePath))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("folder/inside.txt").Open()))
                writer.Write("OWNED-PACED-ARCHIVE-BYTES");
            var files = new SignalMediumSwap(child, trigger, writerResult);
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            providers.Register(new ZipProvider(Path.Combine(work, "spool")));
            var jobs = new JobManager(files, providers, Path.Combine(work, "journal"));
            var job = await Finish(jobs.Submit(new JobRequest
            {
                Kind = JobKind.Extract,
                Sources = [new ItemRef(ZipProvider.ForFile(archivePath), "folder", EntryKind.Directory)],
                Destination = Location.FileSystem(destination),
            }));
            Assert.True(files.Attempted);
            string writerEvidence = File.ReadAllText(writerResult);
            Assert.Contains("WriterIntegrity=S-1-16-8192", writerEvidence);
            Assert.Contains("RenameBlocked=True", writerEvidence);
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal("OWNED-PACED-ARCHIVE-BYTES", File.ReadAllText(Path.Combine(child, "inside.txt")));
            Assert.Equal("OWNED-EXTRACT-OUTSIDE-UNTOUCHED", File.ReadAllText(marker));
            Assert.False(File.Exists(Path.Combine(outside, "inside.txt")));
            File.WriteAllText(Path.Combine(root, "ac20-paced-result.txt"),
                $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\nHeldChild={child}\n{writerEvidence}\nJobState={job.State}\nChildBytes={File.ReadAllText(Path.Combine(child, "inside.txt"))}\nOutsideMarker={File.ReadAllText(marker)}\nOutsideOutputExists={File.Exists(Path.Combine(outside, "inside.txt"))}\n");
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    [Fact]
    public async Task Native_high_token_ZIP_pack_refuses_a_medium_staged_link_to_high_only_bytes()
    {
        string root = FixtureRoot();
        string source = Path.Combine(root, "zip-source-link.txt");
        string secret = Path.Combine(root, "zip-high-only-marker.txt");
        Assert.True((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0);
        Assert.Equal("OWNED-ZIP-HIGH-ONLY-MARKER", File.ReadAllText(secret));

        string work = Directory.CreateDirectory(Path.Combine(root, "ac35-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string zip = Path.Combine(work, "refused.zip");
            var providers = new ProviderRegistry();
            providers.Register(new LocalFileSystemProvider());
            var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(work, "journal"));
            var refused = await Finish(jobs.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Sources = [],
                Archive = new ArchivePlan(zip, null, [new ArchiveChange(ArchiveChangeKind.AddFile, "source.txt", source)]),
            }));
            Assert.Equal(JobState.Failed, refused.State);
            Assert.False(File.Exists(zip));
            Assert.Equal("OWNED-ZIP-HIGH-ONLY-MARKER", File.ReadAllText(secret));
            Assert.Contains(refused.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase));

            string existingZip = Path.Combine(work, "existing.zip");
            using (var initial = ZipFile.Open(existingZip, ZipArchiveMode.Create))
            using (var kept = new StreamWriter(initial.CreateEntry("kept.txt").Open()))
                kept.Write("OWNED-PRIOR-ZIP-BYTES");
            byte[] originalZip = File.ReadAllBytes(existingZip);
            var addRefused = await Finish(jobs.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Sources = [],
                Archive = new ArchivePlan(existingZip, ArchiveBaseline.Of(existingZip),
                    [new ArchiveChange(ArchiveChangeKind.AddFile, "source.txt", source)]),
            }));
            Assert.Equal(JobState.Failed, addRefused.State);
            Assert.Equal(originalZip, File.ReadAllBytes(existingZip));
            Assert.Contains(addRefused.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("OWNED-ZIP-HIGH-ONLY-MARKER", File.ReadAllText(secret));

            string ordinary = Path.Combine(work, "ordinary.txt");
            File.WriteAllText(ordinary, "OWNED-ORDINARY-PACK-BYTES");
            string ordinaryZip = Path.Combine(work, "ordinary.zip");
            var accepted = await Finish(jobs.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Sources = [],
                Archive = new ArchivePlan(ordinaryZip, null,
                    [new ArchiveChange(ArchiveChangeKind.AddFile, "ordinary.txt", ordinary)]),
            }));
            Assert.Equal(JobState.Completed, accepted.State);
            using var archive = ZipFile.OpenRead(ordinaryZip);
            using var reader = new StreamReader(Assert.Single(archive.Entries).Open());
            Assert.Equal("OWNED-ORDINARY-PACK-BYTES", reader.ReadToEnd());
            var addAccepted = await Finish(jobs.Submit(new JobRequest
            {
                Kind = JobKind.ArchiveUpdate,
                Sources = [],
                Archive = new ArchivePlan(existingZip, ArchiveBaseline.Of(existingZip),
                    [new ArchiveChange(ArchiveChangeKind.AddFile, "ordinary.txt", ordinary)]),
            }));
            Assert.Equal(JobState.Completed, addAccepted.State);
            using var updated = ZipFile.OpenRead(existingZip);
            Assert.Equal(2, updated.Entries.Count);
            using (var keptReader = new StreamReader(updated.GetEntry("kept.txt")!.Open()))
                Assert.Equal("OWNED-PRIOR-ZIP-BYTES", keptReader.ReadToEnd());
            using (var addedReader = new StreamReader(updated.GetEntry("ordinary.txt")!.Open()))
                Assert.Equal("OWNED-ORDINARY-PACK-BYTES", addedReader.ReadToEnd());
            File.WriteAllText(Path.Combine(root, "ac35-result.txt"),
                $"HighIntegrity={WindowsTokenAuthority.IsHighIntegrity}\nSourceLink={source}\nSecretBytes={File.ReadAllText(secret)}\nRefusedState={refused.State}\nRefusedIssue={string.Join(" | ", refused.Issues.Select(i => i.Message))}\nRefusedZipExists={File.Exists(zip)}\n" +
                $"ExistingAddRefusedState={addRefused.State}\nExistingZipUnchangedAfterRefusal=True\n" +
                $"OrdinaryState={accepted.State}\nOrdinaryEntryBytes=OWNED-ORDINARY-PACK-BYTES\n" +
                $"ExistingAddAcceptedState={addAccepted.State}\nExistingPriorBytes=OWNED-PRIOR-ZIP-BYTES\nExistingAddedBytes=OWNED-ORDINARY-PACK-BYTES\n");
        }
        finally { Directory.Delete(work, recursive: true); }
    }
}
