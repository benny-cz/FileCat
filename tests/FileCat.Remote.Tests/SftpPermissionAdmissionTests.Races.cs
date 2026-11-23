using FileCat.Core.Analysis;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Core.Resources;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

// G-REMOTE races driven against real OpenSSH.
public sealed partial class SftpPermissionAdmissionTests
{
    // A swap during the call: a proxy in front of the stock sftp-server moves the reviewed file aside and puts a different
    // file in its place just before the server receives the lsetstat. The server changes the replacement; the read after
    // the call no longer matches the reviewed object, so the result is not verified, and the original keeps its mode.
    [Fact]
    public async Task A_swap_during_the_SFTP_call_is_not_reported_verified()
    {
        using var fixture = new Fixture(swapOnLSetStat: true);
        string path = Path.Combine(fixture.Server.Files, "swap-target");
        File.WriteAllText(path, "witness");
        File.SetUnixFileMode(path, (UnixFileMode)0x180);
        string history = Directory.CreateTempSubdirectory("sftp-swap-history-").FullName;
        try
        {
            var registry = new ProviderRegistry();
            registry.Register(fixture.Provider);
            var manager = new JobManager(new PortableFileOperations(), registry, history, ArchiveJobBackends.Empty, new NoStorageTransforms(),
                createPermissionJournal: (leaf, ct) => UnixAnalysisPrivateDirectory.CreatePermissionJournal(history, leaf, ct));
            var subject = fixture.Subject(fixture.Server.Files, "swap-target");
            using var operation = fixture.Provider.PrepareSftpPermissionOperation(subject,
                new ItemRef(subject.OriginalLocation, "swap-target", EntryKind.File),
                new(PermissionScope.Objects, [new ChangePermissionMode(0x1a0, 0xfff)]), Cancellation);
            var done = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.JobFinished += job => done.TrySetResult(job);
            var job = manager.SubmitPermissions(operation);
            Assert.Same(job, await done.Task.WaitAsync(TimeSpan.FromSeconds(30), Cancellation));
            Assert.Contains("swapped", File.ReadAllText(Path.Combine(fixture.Server.Directory, "swap.trace")));
            var result = Assert.Single(job.PermissionResults);
            var step = Assert.Single(result.Components);
            TestContext.Current.TestOutputHelper?.WriteLine($"swap: {result.Verdict}/{step.Verdict} {step.Verification}: {step.Detail} | {result.Detail}");
            Assert.NotEqual(PermissionVerdict.Verified, result.Verdict);
            Assert.NotEqual(PermissionVerdict.AlreadyMatching, result.Verdict);
            Assert.NotNull(step.NativeCall);
            Assert.Equal((UnixFileMode)0x180, File.GetUnixFileMode(path + ".original"));
            Assert.Equal("witness", File.ReadAllText(path + ".original"));
            Assert.Equal((UnixFileMode)0x1a0, File.GetUnixFileMode(path)); // the server changed the replacement
        }
        finally { Directory.Delete(history, recursive: true); }
    }

    // A hard-link swap on a leaf without owner evidence. The harness gives a second account a folder below root-owned
    // folders, with a file of its own, and turns protected hard links off. That leaf's owner is not the account's
    // start-directory owner, so it is inspection-only. The other account then links this account's private file into
    // its place; the path now names this account's file, but its folder belongs to the other account, so it stays
    // inspection-only and the private file keeps its mode.
    [Fact]
    public void A_hard_link_swap_on_a_leaf_without_owner_evidence_never_reaches_a_review()
    {
        string spec = Environment.GetEnvironmentVariable("FILECAT_TEST_SFTP_HARDLINK_RACE") ?? string.Empty;
        string[] parts = spec.Split('|');
        if (parts.Length != 3) Assert.Skip("Requires the hard-link race harness with a second account.");
        string folder = parts[0], privateFile = parts[1], control = parts[2];
        using var fixture = new Fixture();
        string leaf = Path.Combine(folder, "item");
        var before = File.GetUnixFileMode(privateFile);
        Assert.Equal((UnixFileMode)0x180, before);
        string content = File.ReadAllText(privateFile);

        var owned = Assert.Throws<RefusedOperationException>(() => fixture.Review(folder, "item"));
        Assert.Equal("The SFTP leaf owner differs from the account's start-directory owner.", owned.Message);
        Assert.Equal("linked", AskRacer(control, "link"));
        Assert.Equal(new FileInfo(privateFile).Length, new FileInfo(leaf).Length);
        var linked = Assert.Throws<RefusedOperationException>(() => fixture.Review(folder, "item"));
        Assert.Equal("An SFTP ancestor is owned by another server account.", linked.Message);
        var subject = fixture.Subject(folder, "item");
        var inspected = Assert.Single(fixture.Provider.InspectSftpPermissions(subject.OriginalLocation, [subject], Cancellation));
        Assert.Equal((ushort)0x180, inspected.Mode.Value);
        Assert.Equal(before, File.GetUnixFileMode(privateFile));
        Assert.Equal(content, File.ReadAllText(privateFile));

        static string AskRacer(string control, string name)
        {
            File.WriteAllText(Path.Join(control, name + ".go"), string.Empty);
            string result = Path.Join(control, name + ".result");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(result))
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("The second account did not answer.");
                Thread.Sleep(20);
            }
            return File.ReadAllText(result).Trim();
        }
    }
}
