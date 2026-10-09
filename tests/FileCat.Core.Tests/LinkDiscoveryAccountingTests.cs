using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class LinkDiscoveryAccountingTests(ITestOutputHelper output)
{
    public static TheoryData<int, JobKind, VerifyMode, string, string> Cases
    {
        get
        {
            var cases = new TheoryData<int, JobKind, VerifyMode, string, string>();
            foreach (int length in new[] { 17, 1048577 })
            foreach (var kind in new[] { JobKind.Copy, JobKind.Move })
            foreach (var verify in new[] { VerifyMode.Native, VerifyMode.ReadBack })
            foreach (string timing in new[] { "early", "late" })
            foreach (string mode in new[] { "literal", "follow", "skip", "ordinary" })
                cases.Add(length, kind, verify, timing, mode);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Link_metadata_bytes_are_not_counted_as_transferred_contents(int length, JobKind kind, VerifyMode verify, string timing, string mode)
    {
        if (mode != "ordinary" && new PortableFileOperations().CanCreateSymbolicLinks != true)
            Assert.Skip("This account cannot create owned symbolic links.");
        string root = Directory.CreateTempSubdirectory("fc-link-accounting-").FullName;
        string source = Path.Join(root, "source.dat"), referent = Path.Join(root, "referent.dat"), destination = Path.Join(root, "destination"), target = Path.Join(destination, "source.dat");
        byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 31 + 7)).ToArray();
        File.WriteAllBytes(referent, bytes);
        if (mode == "ordinary") File.WriteAllBytes(source, bytes); else File.CreateSymbolicLink(source, referent);
        using var files = new Files(source, destination, timing, mode);
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
        var jobs = new JobManager(files, providers, Path.Join(root, "journal")) { MaxConcurrent = 0 };
        int decisions = 0;
        jobs.DecisionRequested += d => { Interlocked.Increment(ref decisions); d.Resolve(new Decision(mode == "follow" ? DecisionAction.FollowLink : DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest { Kind = kind, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)], Destination = Location.FileSystem(destination), Options = new TransferOptions { Verify = verify, PreserveAttributes = false, PreserveTimestamps = false } });
            jobs.MaxConcurrent = 4; jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "Owned link-accounting control did not settle.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            long expectedBytes = mode is "ordinary" or "follow" ? length : 0;
            long expectedVerify = verify == VerifyMode.ReadBack ? 2 * expectedBytes : 0;
            if (kind == JobKind.Move && mode == "ordinary") expectedVerify += 2 * expectedBytes;
            string expectedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            bool sourceExists = File.Exists(source), targetExists = File.Exists(target);
            string? targetLink = targetExists ? new FileInfo(target).LinkTarget : null;
            output.WriteLine("LINK_DISCOVERY_ACCOUNTING " + JsonSerializer.Serialize(new
            {
                length, Kind = kind.ToString(), Verify = verify.ToString(), timing, mode,
                files.DiscoveryCalls, controlledLinkMetadataBytes = 83,
                job.ItemsTotal, job.ItemsDone, job.BytesTotal, job.BytesDone, job.VerifyBytesTotal, job.VerifyBytesDone,
                job.WorkBytesTotal, job.WorkBytesDone, State = job.State.ToString(),
                expectedBytes, expectedVerify, sourceExists, targetExists, targetLink,
                targetHash = targetExists ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(target))) : null,
                referentHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(referent))), expectedHash,
                decisions, root, source, target, referent,
                ActualOwnedLinksAndBytes = true, ControlledRootMetadataAndDiscoveryTiming = true, NativeHistoricalScheduleOrCandidateQualified = false,
            }));
            Assert.Equal(1, files.DiscoveryCalls);
            Assert.Equal(1, job.ItemsTotal);
            Assert.Equal(expectedBytes, job.BytesTotal); Assert.Equal(expectedBytes, job.BytesDone);
            Assert.Equal(expectedVerify, job.VerifyBytesTotal); Assert.Equal(expectedVerify, job.VerifyBytesDone);
            Assert.Equal(expectedBytes + expectedVerify, job.WorkBytesTotal); Assert.Equal(job.WorkBytesTotal, job.WorkBytesDone);
            Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(referent))));
            Assert.Equal(mode != "skip", targetExists);
            Assert.Equal(!(kind == JobKind.Move && mode is "literal" or "ordinary"), sourceExists);
            if (targetExists)
            {
                Assert.Equal(bytes, File.ReadAllBytes(target));
                if (mode == "literal") Assert.Equal(referent, targetLink); else Assert.Null(targetLink);
            }
            Assert.Equal(mode is "follow" or "skip" ? 1 : 0, decisions);
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel(); files.Release();
            Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(5)));
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(root)); Directory.Delete(root, true); Assert.False(Directory.Exists(root));
        }
    }

    private sealed class Files(string sourcePath, string destination, string timing, string mode) : PortableFileOperations, IDisposable
    {
        private readonly ManualResetEventSlim _executionEntered = new();
        private readonly ManualResetEventSlim _discoveryReturned = new();
        public int DiscoveryCalls;
        public override bool CopyPreservesMetadata => true;
        public override string GetVolumeRoot(string path) => path.StartsWith(destination, PathUtil.SafetyComparison) ? "<owned-destination-volume>" : "<owned-source-volume>";
        public override FileSystemItemInfo? TryGetInfo(string path)
        {
            bool discovery = path == sourcePath && new StackTrace(false).GetFrames().Any(f => f.GetMethod() is { Name: "Discover", DeclaringType.Name: "TransferExecutor" });
            var info = base.TryGetInfo(path);
            if (discovery)
            {
                Interlocked.Increment(ref DiscoveryCalls);
                if (timing == "late") Assert.True(_executionEntered.Wait(TimeSpan.FromSeconds(15)));
                _discoveryReturned.Set();
            }
            return info?.IsLink == true ? info with { Size = 83 } : info;
        }
        private void PublicationEntered()
        {
            _executionEntered.Set();
            Assert.True(_discoveryReturned.Wait(TimeSpan.FromSeconds(15)));
        }
        public override bool TryCopyLink(string source, string destination, bool isDirectory, out string? error)
        {
            PublicationEntered();
            if (mode == "literal") return base.TryCopyLink(source, destination, isDirectory, out error);
            error = "Owned forced literal-link refusal"; return false;
        }
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            PublicationEntered(); base.CopyFile(source, destination, options, progress, ct);
        }
        public void Release() { _executionEntered.Set(); _discoveryReturned.Set(); }
        public void Dispose() { _executionEntered.Dispose(); _discoveryReturned.Dispose(); }
    }
}
