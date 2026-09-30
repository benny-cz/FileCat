using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Release plan V08 for SMB: FileCat reaches shares through Windows' own client, so these run its jobs against a real
/// share of another machine (in the campaign: Samba on the lent Ubuntu VM) and check what arrived against the server's
/// own reading of it. FILECAT_SMB_LAB: a folder on a disposable share, signed in already, where the tests create and
/// delete folders of their own. Optional: FILECAT_SMB_LAB_SERVER_LIST, a command that writes the server's listing of
/// the share (lines "hash &lt;sha256&gt;  ./&lt;path&gt;") to the file named by {out}; FILECAT_SMB_LAB_DROP, a command that drops
/// this machine's sessions on the server.
/// </summary>
public sealed class SmbLabTests : IDisposable
{
    private readonly string _local = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-smb-lab", Guid.NewGuid().ToString("N")[..8])).FullName;
    private readonly List<string> _remote = [];

    public void Dispose()
    {
        foreach (var folder in _remote)
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        try { Directory.Delete(_local, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string Share()
    {
        string? share = Environment.GetEnvironmentVariable("FILECAT_SMB_LAB");
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(share)) Assert.Skip("Set FILECAT_SMB_LAB to a folder on a disposable SMB share (Windows).");
        return share;
    }

    /// <summary>A folder of this test's own on the share, deleted afterwards.</summary>
    private string RemoteFolder()
    {
        string folder = Path.Combine(Share(), "filecat-smb-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        _remote.Add(folder);
        return folder;
    }

    private (JobManager Jobs, List<DecisionRequest> Asked) Manager(Func<DecisionRequest, DecisionAction> answer)
    {
        var providers = new ProviderRegistry();
        providers.Register(new WindowsFileSystemProvider());
        var jobs = new JobManager(new WindowsFileOperations(), providers, Path.Combine(_local, "journal-" + Guid.NewGuid().ToString("N")[..6]));
        var asked = new List<DecisionRequest>();
        jobs.DecisionRequested += d =>
        {
            lock (asked) asked.Add(d.Request);
            d.Resolve(new Decision(answer(d.Request)));
        };
        return (jobs, asked);
    }

    private static async Task<Job> FinishAsync(Job job)
    {
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        return job;
    }

    private static string Story(Job job, List<DecisionRequest> asked)
    {
        lock (asked)
            return $"{job.State}; asked: {string.Join(" | ", asked.Select(a => a is ErrorRequest e ? $"{e.Title}: {e.Message} ({e.ErrorClass})" : $"{a.Title}: {a.Message}"))}; " +
                   $"issues: {string.Join(" | ", job.Issues.Select(i => i.Message))}";
    }

    private static Dictionary<string, string> Hashes(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f).Replace('\\', '/'), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

    /// <summary>
    /// The server's own digests of the files under <paramref name="folder"/> (a path on the share), keyed like
    /// <see cref="Hashes"/>; null when no server listing command is set.
    /// </summary>
    private Dictionary<string, string>? ServerHashes(string folder)
    {
        string? command = Environment.GetEnvironmentVariable("FILECAT_SMB_LAB_SERVER_LIST");
        if (string.IsNullOrEmpty(command)) return null;
        string listing = Path.Combine(_local, "server-" + Guid.NewGuid().ToString("N")[..6] + ".txt");
        Shell(command.Replace("{out}", listing, StringComparison.Ordinal));
        string prefix = "./" + Path.GetRelativePath(Share(), folder).Replace('\\', '/') + "/";
        var result = new Dictionary<string, string>();
        foreach (string line in File.ReadAllLines(listing))
        {
            // "hash <64 hex>  ./<path>"
            if (!line.StartsWith("hash ", StringComparison.Ordinal) || line.Length < 5 + 64 + 2) continue;
            string path = line[(5 + 64 + 2)..];
            if (path.StartsWith(prefix, StringComparison.Ordinal)) result[path[prefix.Length..]] = line.Substring(5, 64).ToUpperInvariant();
        }
        return result;
    }

    private static void Shell(string command)
    {
        using var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command) { UseShellExecute = false, CreateNoWindow = true })!;
        if (!p.WaitForExit(TimeSpan.FromMinutes(2))) throw new TimeoutException(command);
    }

    /// <summary>A tree with small files, a large one (published from a staged name), non-ASCII names and an empty folder.</summary>
    private string Tree(string name)
    {
        string root = Directory.CreateDirectory(Path.Combine(_local, "src", name)).FullName;
        var random = new Random(11);
        for (int i = 0; i < 40; i++)
        {
            var bytes = new byte[random.Next(0, 70_000)];
            random.NextBytes(bytes);
            string file = Path.Combine(root, $"note {i:00}.bin");
            File.WriteAllBytes(file, bytes);
            File.SetLastWriteTimeUtc(file, new DateTime(2024, 2, 29, 13, 37, 42, DateTimeKind.Utc).AddTicks(1234567 * i));
        }
        Directory.CreateDirectory(Path.Combine(root, "Žluťoučký kůň", "empty"));
        var large = new byte[48 << 20];
        random.NextBytes(large);
        File.WriteAllBytes(Path.Combine(root, "Žluťoučký kůň", "large.bin"), large);
        return root;
    }

    [Fact]
    public async Task A_tree_goes_to_the_share_and_back_byte_for_byte_through_jobs()
    {
        string remote = RemoteFolder();
        string local = Tree("Lab tree");
        var expected = Hashes(local);
        var (jobs, asked) = Manager(_ => DecisionAction.CancelJob);

        var up = await FinishAsync(jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(local, EntryKind.Directory)],
            Destination = Location.FileSystem(remote),
        }));
        Assert.True(up.State == JobState.Completed, Story(up, asked));
        string arrived = Path.Combine(remote, "Lab tree");
        // What the server holds (its own reading where the lab provides one), and nothing staged beside it.
        var server = ServerHashes(arrived);
        Assert.Equal(expected.OrderBy(p => p.Key), (server ?? Hashes(arrived)).OrderBy(p => p.Key));
        Assert.True(Directory.Exists(Path.Combine(arrived, "Žluťoučký kůň", "empty")));
        Assert.DoesNotContain(Directory.EnumerateFileSystemEntries(remote, "*", SearchOption.AllDirectories), p => Path.GetFileName(p).StartsWith(".fc-", StringComparison.Ordinal));
        // Modification times kept; a server may round them, never by more than its file system's step.
        var drift = Directory.EnumerateFiles(local, "*", SearchOption.AllDirectories)
            .Max(f => (File.GetLastWriteTimeUtc(f) - File.GetLastWriteTimeUtc(Path.Combine(arrived, Path.GetRelativePath(local, f)))).Duration());
        Assert.True(drift <= TimeSpan.FromSeconds(2), $"Modification times moved by up to {drift}.");

        // And back again.
        string back = Directory.CreateDirectory(Path.Combine(_local, "back")).FullName;
        var down = await FinishAsync(jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(arrived, EntryKind.Directory)],
            Destination = Location.FileSystem(back),
        }));
        Assert.True(down.State == JobState.Completed, Story(down, asked));
        Assert.Equal(expected.OrderBy(p => p.Key), Hashes(Path.Combine(back, "Lab tree")).OrderBy(p => p.Key));
        Assert.True(Directory.Exists(Path.Combine(back, "Lab tree", "Žluťoučký kůň", "empty")));
        TestContext.Current.TestOutputHelper?.WriteLine($"server oracle: {(server is null ? "none (client reading)" : "yes")}; time drift up to {drift}");
    }

    [Fact]
    public async Task Deleting_on_a_share_keeps_the_items_unless_permanent_deletion_was_agreed()
    {
        string remote = RemoteFolder();
        string file = Path.Combine(remote, "keep me.txt");
        File.WriteAllText(file, "still here");
        Assert.Equal(RecycleClassification.NoRecycleBin, new WindowsFileOperations().ClassifyRecycle(file, 10));
        var (jobs, asked) = Manager(_ => DecisionAction.CancelJob);

        // To the Recycle Bin: a share has none, so nothing is deleted, and the job says why.
        var kept = await FinishAsync(jobs.Submit(new JobRequest { Kind = JobKind.Recycle, Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)] }));
        Assert.True(File.Exists(file), Story(kept, asked));
        Assert.Contains(kept.Issues, i => i.Message.Contains("no Recycle Bin", StringComparison.Ordinal));

        // With the user's agreement to delete permanently what the bin cannot take: gone.
        var gone = await FinishAsync(jobs.Submit(new JobRequest
        {
            Kind = JobKind.Recycle,
            Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
            Options = new TransferOptions { PermanentlyDeleteUnrecyclable = true },
        }));
        Assert.False(File.Exists(file), Story(gone, asked));
    }

    [Fact]
    public async Task A_move_within_the_share_renames_on_the_server()
    {
        string remote = RemoteFolder();
        string file = Path.Combine(remote, "video.bin");
        var bytes = new byte[64 << 20];
        new Random(13).NextBytes(bytes);
        File.WriteAllBytes(file, bytes);
        string into = Directory.CreateDirectory(Path.Combine(remote, "sorted")).FullName;
        var ops = new WindowsFileOperations();
        var before = ops.GetFileIdentity(file);
        var (jobs, asked) = Manager(_ => DecisionAction.CancelJob);

        var move = await FinishAsync(jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
            Destination = Location.FileSystem(into),
        }));
        Assert.True(move.State == JobState.Completed, Story(move, asked));
        string moved = Path.Combine(into, "video.bin");
        Assert.False(File.Exists(file));
        // The same file on the server, renamed (no copy through this machine): its identity is kept.
        if (before is not null) Assert.Equal(before, ops.GetFileIdentity(moved));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), (ServerHashes(into) ?? Hashes(into))["video.bin"]);
    }

    [Fact]
    public async Task A_downloaded_file_moved_to_the_share_asks_before_its_origin_is_lost()
    {
        string remote = RemoteFolder();
        string source = Path.Combine(Directory.CreateDirectory(Path.Combine(_local, "downloads")).FullName, "setup.exe");
        File.WriteAllText(source, "pretend installer");
        File.WriteAllText(source + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/setup.exe\r\n");
        var (jobs, asked) = Manager(r => r.Title.StartsWith("Moving would lose", StringComparison.Ordinal) ? DecisionAction.KeepSource : DecisionAction.CancelJob);

        var move = await FinishAsync(jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
            Destination = Location.FileSystem(remote),
        }));
        string arrived = Path.Combine(remote, "setup.exe");
        Assert.True(File.Exists(arrived), Story(move, asked));
        bool streams = File.Exists(arrived + ":Zone.Identifier");
        if (streams)
        {
            // The share keeps alternate data streams: the mark travelled with the file, nothing to ask.
            Assert.DoesNotContain(asked, a => a.Title.StartsWith("Moving would lose", StringComparison.Ordinal));
            Assert.False(File.Exists(source));
        }
        else
        {
            // It cannot: FileCat asked before the original (the only copy carrying the mark) would go; kept on request.
            Assert.Contains(asked, a => a.Title.StartsWith("Moving would lose", StringComparison.Ordinal) && a.Message.Contains("Mark of the Web", StringComparison.Ordinal));
            Assert.True(File.Exists(source) && File.Exists(source + ":Zone.Identifier"), Story(move, asked));
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"share keeps streams: {streams}; {Story(move, asked)}");
    }

    [Fact]
    public async Task A_file_open_on_the_share_is_replaced_or_reported_in_use()
    {
        string remote = RemoteFolder();
        string target = Path.Combine(remote, "log.txt");
        File.WriteAllText(target, "old");
        string source = Path.Combine(Directory.CreateDirectory(Path.Combine(_local, "newer")).FullName, "log.txt");
        File.WriteAllText(source, "newer");
        var (jobs, asked) = Manager(_ => DecisionAction.Skip);

        // Open the way FileCat's viewer opens files (sharing reading, writing and deletion), through the same client.
        Job copy;
        using (var viewer = new FileContentSource(target))
        {
            copy = await FinishAsync(jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)],
                Destination = Location.FileSystem(remote),
                Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
            }));
        }
        string now = File.ReadAllText(target);
        string story = Story(copy, asked);
        TestContext.Current.TestOutputHelper?.WriteLine($"content now: {now}; {story}");
        // Either replaced, or (when the server refuses to replace an open file) reported as in use with the old file
        // intact; never a half state and never a staged copy left behind.
        if (now == "newer") Assert.True(copy.State == JobState.Completed, story);
        else
        {
            Assert.Equal("old", now);
            Assert.Contains(asked, a => a is ErrorRequest { ErrorClass: "sharing" });
        }
        Assert.Equal(["log.txt"], Directory.GetFiles(remote).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_copy_to_the_share_cancelled_part_way_leaves_nothing()
    {
        string remote = RemoteFolder();
        string file = Path.Combine(Directory.CreateDirectory(Path.Combine(_local, "cancel")).FullName, "big.bin");
        var bytes = new byte[256 << 20];
        new Random(17).NextBytes(bytes);
        File.WriteAllBytes(file, bytes);
        var (jobs, asked) = Manager(_ => DecisionAction.CancelJob);
        var ct = TestContext.Current.CancellationToken;

        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
            Destination = Location.FileSystem(remote),
        });
        while (!job.State.IsFinished() && job.BytesDone < bytes.Length / 5) await Task.Delay(10, ct);
        Assert.False(job.State.IsFinished(), "The copy finished before it could be cancelled.");
        job.Cancel();
        await FinishAsync(job);
        Assert.Equal(JobState.Canceled, job.State);
        var left = Directory.EnumerateFileSystemEntries(remote).Select(Path.GetFileName).ToList();
        Assert.True(left.Count == 0, "Left on the share: " + string.Join(", ", left));
    }

    [Fact]
    public async Task A_copy_cut_off_by_the_server_arrives_intact_after_retrying()
    {
        string? drop = Environment.GetEnvironmentVariable("FILECAT_SMB_LAB_DROP");
        string remote = RemoteFolder();
        if (string.IsNullOrEmpty(drop)) Assert.Skip("Set FILECAT_SMB_LAB_DROP to a command that drops this machine's sessions on the server.");
        string file = Path.Combine(Directory.CreateDirectory(Path.Combine(_local, "drop")).FullName, "big.bin");
        var bytes = new byte[128 << 20];
        new Random(19).NextBytes(bytes);
        File.WriteAllBytes(file, bytes);
        // Whatever FileCat asks after the break, the user says: try again.
        var (jobs, asked) = Manager(r => r is ErrorRequest ? DecisionAction.Retry : DecisionAction.CancelJob);
        var ct = TestContext.Current.CancellationToken;

        var job = jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
            Destination = Location.FileSystem(remote),
            Options = new TransferOptions { RateLimit = 16 << 20 }, // about eight seconds: time to cut it
        });
        while (!job.State.IsFinished() && job.BytesDone < bytes.Length * 3L / 10) await Task.Delay(20, ct);
        Assert.False(job.State.IsFinished(), "The copy finished before the connection could be cut.");
        Shell(drop);
        await FinishAsync(job);
        string story = Story(job, asked);
        TestContext.Current.TestOutputHelper?.WriteLine(story);
        Assert.True(job.State == JobState.Completed, story);
        var server = ServerHashes(remote) ?? Hashes(remote);
        Assert.Equal(["big.bin"], server.Keys);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), server["big.bin"]);
        Assert.Equal(["big.bin"], Directory.EnumerateFileSystemEntries(remote).Select(Path.GetFileName));
    }
}
