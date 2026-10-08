using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class UploadVerificationBoundsTests(ITestOutputHelper output)
{
    public static TheoryData<int, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, string, bool>();
            foreach (int length in new[] { 65537, 1048577 })
            foreach (string behavior in new[] { "stable", "source-growth", "remote-growth", "both-growth", "source-short" })
            foreach (bool replace in new[] { false, true }) cases.Add(length, behavior, replace);
            cases.Add(0, "stable", false); cases.Add(0, "stable", true); return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Upload_read_back_requires_exact_copied_lengths(int length, string behavior, bool replace)
    {
        string directory = Path.Combine(Path.GetTempPath(), "filecat-upload-bounds", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        byte[] data = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        byte[] tail = Enumerable.Repeat((byte)0xA5, 2097152).ToArray();
        byte[] previous = "owned prior server destination\n"u8.ToArray();
        string source = Path.Join(directory, "copy.dat"), prior = source + ".prior";
        File.WriteAllBytes(source, data);
        var server = new FakeSftpServer(); server.Dir("/up");
        if (replace) server.File("/up/copy.dat", "").Data = previous;
        int swaps = 0, remoteMutations = 0, remoteReads = 0;
        if (behavior is "source-growth" or "both-growth" or "source-short") server.DuringUpload = () =>
        {
            File.Move(source, prior);
            File.WriteAllBytes(source, behavior == "source-short" ? data[..^1] : [..data, ..tail]);
            swaps++;
        };
        server.OnRead = path =>
        {
            remoteReads++;
            if (behavior is "remote-growth" or "both-growth")
            {
                var node = server.Lookup(path, true)!; node.Data = [..node.Data, ..tail]; remoteMutations++;
            }
        };
        var interaction = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
        for (int i = 0; i < 10; i++) interaction.Secrets.Enqueue("secret");
        var profile = new RemoteProfile { Name = "Owned fake server", Host = "files.example", User = "user" };
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new FakeConnector(server),
            new HostKeyTrust(Path.Join(directory, "known_hosts"), []), new SessionSecretStore(), interaction);
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        providers.Register(new SftpProvider(connections, () => [profile], p => p)); SftpJobs.Register();
        var jobs = new JobManager(new PortableFileOperations(), providers, Path.Join(directory, "journal")) { MaxConcurrent = 0 };
        int decisions = 0; jobs.DecisionRequested += d => { Interlocked.Increment(ref decisions); d.Resolve(new Decision(DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = SftpProvider.At(profile, "/up"),
                Options = new TransferOptions { Verify = VerifyMode.ReadBack, Conflicts = ConflictPolicy.Replace },
            });
            jobs.MaxConcurrent = 4; jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15)); await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            bool accepted = behavior == "stable"; var target = server.Lookup("/up/copy.dat", false);
            string[] names = server.Lookup("/up", true)!.Children.Keys.ToArray();
            byte[] expectedSource = behavior == "source-short" ? data[..^1] : behavior is "source-growth" or "both-growth" ? [..data, ..tail] : data;
            output.WriteLine("UPLOAD_VERIFICATION_BOUNDS " + JsonSerializer.Serialize(new
            {
                length, behavior, replace, accepted, job.State, swaps, remoteMutations, remoteReads, decisions, Names = names,
                SourceSHA256 = Hash(File.ReadAllBytes(source)), ExpectedSourceSHA256 = Hash(expectedSource), OriginalSHA256 = Hash(data),
                PreviousSHA256 = Hash(previous), TargetSHA256 = target is null ? null : Hash(target.Data), TargetLength = target?.Data.Length,
                job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal, Issues = job.Issues.Select(i => new { i.Message, i.Outcome }).ToArray(),
                ActualOwnedLocalFileReadsAndSourceReplacement = true, ActualSftpUploadExecutor = true, SyntheticInMemoryServerPostStatMutation = true,
                RealServerPhysicalNativeDesktopAtomicOrCandidateQualified = false,
            }));
            Assert.Equal(expectedSource, File.ReadAllBytes(source)); Assert.Equal(behavior is "source-growth" or "both-growth" or "source-short" ? 1 : 0, swaps);
            Assert.Equal(replace || accepted ? ["copy.dat"] : Array.Empty<string>(), names);
            Assert.InRange(job.VerifyBytesDone, 0, 2L * length);
            if (accepted)
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(data, target!.Data); Assert.Equal(0, decisions);
                Assert.Equal(2L * length, job.VerifyBytesDone); Assert.Equal(job.VerifyBytesDone, job.VerifyBytesTotal);
            }
            else
            {
                Assert.True(job.State is JobState.Failed or JobState.CompletedWithIssues); Assert.NotEmpty(job.Issues); Assert.Equal(1, decisions);
                Assert.Equal(0, job.VerifyBytesDone); Assert.Equal(0, job.VerifyBytesTotal);
                if (replace) Assert.Equal(previous, target!.Data); else Assert.Null(target);
            }
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
            connections.Dispose();
            string resolved = Path.GetFullPath(directory);
            Assert.True(Path.GetRelativePath(Path.Join(Path.GetTempPath(), "filecat-upload-bounds"), resolved).StartsWith(Path.GetFileName(directory), StringComparison.Ordinal));
            Directory.Delete(resolved, recursive: true);
        }
    }
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
}
