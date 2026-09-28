using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Mtp;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Device edge cases (FILECAT_MTP_TEST=1, optionally FILECAT_MTP_DEVICE), found on a real phone: its storage ignores
/// letter case while MTP lists two names, and replacing used to delete the old file before the new one was complete.
/// Everything happens inside FileCat-test, which is removed.
/// </summary>
[Collection(MtpTests.Device)]
public sealed class MtpRobustnessTests : IDisposable
{
    private readonly string _local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-mtp-edge", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_local, recursive: true); } catch (IOException) { }
    }

    private sealed class Rig : IDisposable
    {
        public required string DeviceId { get; init; }
        public required PortableObject Storage { get; init; }
        public required MtpProvider Mtp { get; init; }
        public required JobManager Jobs { get; init; }
        public required Location Folder { get; init; }

        public List<EntryData> List(Location? folder = null)
        {
            var list = new List<EntryData>();
            Mtp.EnumerateAsync(folder ?? Folder, new Sink(list), CancellationToken.None).GetAwaiter().GetResult();
            return list;
        }

        public string Names() => string.Join(", ", List().Select(e => $"{e.Name}={e.Size}").Order());

        public byte[] Read(string name)
        {
            using var content = Mtp.OpenContent(new ItemRef(Folder, name, EntryKind.File))!;
            var data = new byte[content.Length];
            int done = 0;
            while (done < data.Length)
            {
                int n = content.Read(done, data.AsSpan(done));
                if (n <= 0) break;
                done += n;
            }
            return data[..done];
        }

        public void Dispose()
        {
            Mtp.CloseAll();
            using var cleanup = WpdSession.Open(DeviceId);
            if (cleanup.Children(Storage.Id, CancellationToken.None).FirstOrDefault(o => o.Name == MtpTests.TestFolder) is { } folder) cleanup.Delete(folder.Id, recursive: true);
        }
    }

    private sealed class Sink(List<EntryData> list) : IEnumerationSink
    {
        public void AddBatch(ReadOnlySpan<EntryData> entries) => list.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    private Rig? Open()
    {
        var opened = MtpTests.OpenTestDevice();
        if (opened is null) return null;
        var (session, storage) = opened.Value;
        string deviceId = session.Id;
        if (session.Children(storage.Id, CancellationToken.None).FirstOrDefault(o => o.Name == MtpTests.TestFolder && o.IsFolder) is { } stale) session.Delete(stale.Id, recursive: true);
        session.CreateFolder(storage.Id, MtpTests.TestFolder);
        session.Dispose();
        var mtp = new MtpProvider();
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        providers.Register(mtp);
        MtpJobs.Register();
        return new Rig
        {
            DeviceId = deviceId,
            Storage = storage,
            Mtp = mtp,
            Jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_local, "journal")),
            Folder = new Location(Schemes.Mtp, storage.Name + "/" + MtpTests.TestFolder, session: deviceId),
        };
    }

    private static async Task<Job> Run(Rig rig, JobRequest request, Func<Job, bool>? cancelWhen = null)
    {
        var job = rig.Jobs.Submit(request);
        while (!job.State.IsFinished())
        {
            // Nobody answers questions here: one is a finding, never a reason to wait forever.
            if (job.Decision is { } asked)
            {
                asked.Resolve(new Decision(DecisionAction.CancelJob));
                Assert.Fail($"The job asked \"{asked.Request.Title}: {asked.Request.Message}\"");
            }
            if (cancelWhen?.Invoke(job) == true) job.Cancel();
            await Task.Delay(5);
        }
        return job;
    }

    private string LocalFile(string name, byte[] content)
    {
        string dir = Path.Combine(_local, "src", Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static JobRequest Upload(Rig rig, string path, ConflictPolicy conflicts = ConflictPolicy.Ask) => new()
    {
        Kind = JobKind.Copy,
        Sources = [ItemRef.ForFileSystemPath(path, EntryKind.File)],
        Destination = rig.Folder,
        Options = new TransferOptions { Conflicts = conflicts },
    };

    private static string Describe(Job job) => $"{job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}";

    [Fact]
    public async Task Empty_files_Unicode_names_and_names_differing_in_letter_case()
    {
        using var rig = Open();
        if (rig is null) Assert.Skip("Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario.");

        var empty = await Run(rig, Upload(rig, LocalFile("empty.txt", [])));
        Assert.True(empty.State == JobState.Completed, Describe(empty));
        Assert.Empty(rig.Read("empty.txt"));
        string czech = "Příliš žluťoučký kůň ✓.txt";
        var unicode = await Run(rig, Upload(rig, LocalFile(czech, "kůň"u8.ToArray())));
        Assert.True(unicode.State == JobState.Completed, Describe(unicode));
        Assert.Equal("kůň"u8.ToArray(), rig.Read(czech));

        // The phone's storage ignores letter case: "CASE.TXT" would overwrite "case.txt" while MTP lists both.
        await Run(rig, Upload(rig, LocalFile("case.txt", "lower"u8.ToArray())));
        var skipped = await Run(rig, Upload(rig, LocalFile("CASE.TXT", "UPPER"u8.ToArray()), ConflictPolicy.Skip));
        Assert.Equal("lower"u8.ToArray(), rig.Read("case.txt"));
        Assert.Single(rig.List(), e => e.Name.Equals("case.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(skipped.Issues, i => i.Outcome == StepOutcome.Skipped);
        var kept = await Run(rig, Upload(rig, LocalFile("CASE.TXT", "UPPER"u8.ToArray()), ConflictPolicy.KeepBothRenameIncoming));
        Assert.True(kept.State == JobState.Completed, Describe(kept));
        Assert.Equal("UPPER"u8.ToArray(), rig.Read("CASE (2).TXT"));
        Assert.Equal("lower"u8.ToArray(), rig.Read("case.txt"));
        var replaced = await Run(rig, Upload(rig, LocalFile("CASE.TXT", "UPPER"u8.ToArray()), ConflictPolicy.Replace));
        Assert.True(replaced.State == JobState.Completed, Describe(replaced));
        Assert.Equal("UPPER"u8.ToArray(), rig.Read("CASE.TXT"));
        Assert.Single(rig.List(), e => e.Name.Equals("case.txt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(rig.List(), e => e.Name.StartsWith("~filecat-", StringComparison.Ordinal));

        // Renames and new folders collide the same way; renaming an item to its own name in another case is fine.
        var clash = await Run(rig, new JobRequest { Kind = JobKind.Rename, Sources = [new ItemRef(rig.Folder, "empty.txt", EntryKind.File)], NewName = "case.txt" });
        Assert.NotEqual(JobState.Completed, clash.State);
        var caseOnly = await Run(rig, new JobRequest { Kind = JobKind.Rename, Sources = [new ItemRef(rig.Folder, "empty.txt", EntryKind.File)], NewName = "Empty.txt" });
        Assert.True(caseOnly.State == JobState.Completed, Describe(caseOnly));
        Assert.Contains(rig.List(), e => e.Name == "Empty.txt");
    }

    [Fact]
    public async Task Canceling_part_way_leaves_nothing_behind_and_never_loses_the_file_being_replaced()
    {
        using var rig = Open();
        if (rig is null) Assert.Skip("Set FILECAT_MTP_TEST=1 with an unlocked phone in file-transfer mode to run the device scenario.");
        var big = new byte[48 * 1024 * 1024];
        new Random(1).NextBytes(big);

        var canceled = await Run(rig, Upload(rig, LocalFile("big.bin", big)), j => j.BytesDone > big.Length / 3);
        Assert.Equal(JobState.Canceled, canceled.State);
        Assert.DoesNotContain(rig.List(), e => e.Name == "big.bin" || e.Name.StartsWith("~filecat-", StringComparison.Ordinal));

        await Run(rig, Upload(rig, LocalFile("keep.bin", [1, 2, 3])));
        var canceledReplace = await Run(rig, Upload(rig, LocalFile("keep.bin", big), ConflictPolicy.Replace), j => j.BytesDone > big.Length / 3);
        Assert.Equal(JobState.Canceled, canceledReplace.State);
        Assert.Equal(new byte[] { 1, 2, 3 }, rig.Read("keep.bin"));
        Assert.DoesNotContain(rig.List(), e => e.Name.StartsWith("~filecat-", StringComparison.Ordinal));

        var replaced = await Run(rig, Upload(rig, LocalFile("keep.bin", big), ConflictPolicy.Replace));
        Assert.True(replaced.State == JobState.Completed, Describe(replaced));
        Assert.True(big.AsSpan().SequenceEqual(rig.Read("keep.bin")));
        Assert.Equal("keep.bin=" + big.Length, rig.Names());
    }
}
