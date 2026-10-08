using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class TransferVerificationBoundsTests(ITestOutputHelper output)
{
    public static TheoryData<int, string, bool> ProviderCases
    {
        get
        {
            var cases = new TheoryData<int, string, bool>();
            foreach (int length in new[] { 0, 65537, 1048577 })
            foreach (string behavior in new[] { "stable", "unknown", "revision-before", "revision-during", "reported-length", "partial-before", "partial-during", "partial-clean", "partial-range-before", "partial-range-during", "overrun", "negative" })
            foreach (bool replace in new[] { false, true }) cases.Add(length, behavior, replace);
            foreach (int length in new[] { 65537, 1048577 })
            foreach (string behavior in new[] { "resume-same", "resume-new", "short" })
            foreach (bool replace in new[] { false, true }) cases.Add(length, behavior, replace);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ProviderCases))]
    public async Task Read_back_requires_a_complete_stable_finite_source(int length, string behavior, bool replace)
    {
        using var dir = new TempDir();
        byte[] original = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        string source = dir.File("source.dat", ""), destination = dir.Dir("destination"), target = Path.Join(destination, "copy.dat");
        File.WriteAllBytes(source, original);
        byte[] previous = "owned existing destination\n"u8.ToArray();
        if (replace) File.WriteAllBytes(target, previous);
        var provider = new VerificationProvider(source, length, behavior);
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(provider);
        var jobs = new JobManager(new PortableFileOperations(), providers, dir.Dir("journal")) { MaxConcurrent = 0 };
        int decisions = 0;
        jobs.DecisionRequested += d => { Interlocked.Increment(ref decisions); d.Resolve(new Decision(DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [new ItemRef(new Location(provider.Scheme, "/"), "copy.dat", EntryKind.File, length)],
                Destination = Location.FileSystem(destination), Options = new TransferOptions { Verify = VerifyMode.ReadBack, Conflicts = ConflictPolicy.Replace },
            });
            jobs.MaxConcurrent = 4; jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), "Owned provider copy did not finish.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            bool accepted = behavior is "stable" or "unknown" or "partial-clean" || behavior.StartsWith("resume-", StringComparison.Ordinal);
            byte[] expected = behavior == "resume-new" ? original.Select(b => (byte)(b ^ 0x5A)).ToArray() : original;
            int expectedOpens = behavior.StartsWith("resume-", StringComparison.Ordinal) ? 3 : 2;
            var staged = Directory.GetFiles(destination, JournalRecovery.StagedPrefix + "*");
            output.WriteLine("TRANSFER_VERIFICATION_BOUNDS " + JsonSerializer.Serialize(new
            {
                Control = "provider-read-back", length, behavior, replace, accepted, job.State, provider.Opens, provider.Disposals,
                provider.VerificationReadBytes, provider.VerificationRevisionQueries, VerificationReads = provider.Reads.ToArray(), decisions,
                SourceSHA256 = Digest(File.ReadAllBytes(source)), TargetSHA256 = File.Exists(target) ? Digest(File.ReadAllBytes(target)) : null,
                OriginalSHA256 = Digest(original), ExpectedSourceSHA256 = Digest(expected), ResumedOrRestarted = behavior.StartsWith("resume-", StringComparison.Ordinal), PreviousSHA256 = Digest(previous), StagedFiles = staged.Length,
                job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal, Issues = job.Issues.Select(i => new { i.Message, i.Outcome }).ToArray(),
                ActualOwnedSourceAndDestination = true, SyntheticProviderRevisionPartialAndReadControls = true,
                NativeServerDesktopPhysicalAtomicOrCandidateQualified = false,
            }));
            Assert.Equal(expected, File.ReadAllBytes(source)); Assert.Equal(expectedOpens, provider.Opens); Assert.Equal(expectedOpens, provider.Disposals); Assert.Empty(staged);
            Assert.InRange(provider.VerificationReadBytes, 0, (long)length + 1);
            Assert.InRange(provider.VerificationRevisionQueries, 0, 2);
            if (accepted)
            {
                Assert.Equal(JobState.Completed, job.State); Assert.Equal(expected, File.ReadAllBytes(target)); Assert.Equal(0, decisions);
                Assert.Equal(2L * length, job.VerifyBytesDone); Assert.Equal(job.VerifyBytesDone, job.VerifyBytesTotal);
            }
            else
            {
                Assert.True(job.State is JobState.CompletedWithIssues or JobState.Failed); Assert.NotEmpty(job.Issues);
                if (replace) Assert.Equal(previous, File.ReadAllBytes(target)); else Assert.False(File.Exists(target));
            }
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
        }
    }

    public static TheoryData<int, string> FileCases
    {
        get
        {
            var cases = new TheoryData<int, string>();
            foreach (int length in new[] { 0, 65537, 1048577 })
            {
                cases.Add(length, "stable"); cases.Add(length, "cancel");
                if (length > 0) foreach (string change in new[] { "append", "growing", "truncate" }) cases.Add(length, change);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(FileCases))]
    public void Hashing_an_owned_file_does_not_follow_growth_past_its_opening_length(int length, string change)
    {
        using var dir = new TempDir(); string path = dir.File("source.dat", "");
        byte[] original = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray(); File.WriteAllBytes(path, original);
        using var stop = new CancellationTokenSource(); if (change == "cancel") stop.Cancel();
        long lastProgress = 0; int mutations = 0; byte[]? digest = null;
        var error = Record.Exception(() => digest = PortableFileOperations.HashFile(path, HashAlgorithmName.SHA256, stop.Token, done =>
        {
            lastProgress = done;
            if (change is "stable" or "cancel" || mutations >= (change == "growing" ? 4 : 1)) return;
            mutations++;
            using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            if (change == "truncate") writer.SetLength(0);
            else { writer.Position = writer.Length; writer.Write(new byte[1048576]); }
        }));
        output.WriteLine("TRANSFER_VERIFICATION_BOUNDS " + JsonSerializer.Serialize(new
        {
            Control = "owned-file-hash", length, change, ErrorType = error?.GetType().Name, lastProgress, mutations,
            FinalLength = new FileInfo(path).Length, ReturnedSHA256 = digest is null ? null : Convert.ToHexString(digest),
            OriginalSHA256 = Digest(original), FinalSHA256 = Digest(File.ReadAllBytes(path)), ActualOwnedReadAndConcurrentWrite = true,
            SyntheticProgressBoundaryMutation = true, NativeAtomicAliasOrHistoricalSchedulingQualified = false,
        }));
        Assert.InRange(lastProgress, 0, (long)length);
        if (change == "stable") { Assert.Null(error); Assert.Equal(SHA256.HashData(original), digest); }
        else if (change == "cancel") { Assert.IsAssignableFrom<OperationCanceledException>(error); Assert.Equal(0, lastProgress); Assert.Equal(original, File.ReadAllBytes(path)); }
        else { Assert.IsAssignableFrom<IOException>(error); Assert.Null(digest); Assert.True(mutations > 0); }
    }

    private static string Digest(byte[] data) => Convert.ToHexString(SHA256.HashData(data));

    private sealed class VerificationProvider(string sourcePath, int length, string behavior) : ResourceProvider
    {
        private readonly string _sourcePath = sourcePath, _behavior = behavior;
        private readonly int _length = length;
        private long _version;
        public int Opens, Disposals, VerificationRevisionQueries;
        public long VerificationReadBytes;
        public readonly System.Collections.Concurrent.ConcurrentQueue<ReadObservation> Reads = new();
        public override string Scheme => "verification-owned";
        public override string GetDisplayPath(Location location) => Scheme + ":" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource OpenContent(ItemRef item)
        {
            int opened = Interlocked.Increment(ref Opens);
            bool resumed = behavior.StartsWith("resume-", StringComparison.Ordinal);
            if (resumed && opened == 2 && behavior == "resume-new")
            {
                File.WriteAllBytes(sourcePath, File.ReadAllBytes(sourcePath).Select(b => (byte)(b ^ 0x5A)).ToArray());
                _version++;
            }
            bool verifying = opened == (resumed ? 3 : 2);
            return verifying && behavior.StartsWith("partial-", StringComparison.Ordinal) ? new PartialSource(this, true) : new Source(this, verifying);
        }
        public sealed record ReadObservation(long Offset, int Requested, int Returned);
        private class Source(VerificationProvider owner, bool verifying) : IContentSource
        {
            private readonly FileContentSource _file = new(owner._sourcePath);
            protected bool ReadStarted;
            public string DisplayName => "owned copy.dat";
            public long Length => owner._behavior == "unknown" ? -1 : verifying && owner._behavior == "reported-length" ? owner._length + 1L : owner._length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision()
            {
                if (verifying) Interlocked.Increment(ref owner.VerificationRevisionQueries);
                return owner._behavior == "unknown" ? null : new(owner._length, 700000000000000000L + owner._version +
                    (verifying && (owner._behavior == "revision-before" || owner._behavior == "revision-during" && ReadStarted) ? 1 : 0));
            }
            public int Read(long offset, Span<byte> buffer)
            {
                ReadStarted = true; int n;
                if (!verifying && owner.Opens == 1 && owner._behavior.StartsWith("resume-", StringComparison.Ordinal))
                {
                    long boundary = owner._length / 2;
                    if (offset >= boundary) throw new IOException("Owned control: first transfer disconnected.");
                    return _file.Read(offset, buffer[..(int)Math.Min(buffer.Length, boundary - offset)]);
                }
                if (verifying && owner._behavior == "negative") n = -1;
                else if (verifying && owner._behavior == "short")
                    n = offset >= owner._length - 1 ? 0 : _file.Read(offset, buffer[..(int)Math.Min(buffer.Length, owner._length - 1 - offset)]);
                else if (verifying && owner._behavior == "overrun")
                {
                    // Finite owned adverse control: never supply an infinite source to the baseline.
                    n = (int)Math.Max(0, Math.Min(buffer.Length, owner._length + 2097152L - offset));
                    buffer[..n].Fill(0xA5);
                    if (offset < owner._length) _file.Read(offset, buffer[..Math.Min(n, (int)(owner._length - offset))]);
                }
                else n = _file.Read(offset, buffer);
                if (verifying) { owner.Reads.Enqueue(new(offset, buffer.Length, n)); Interlocked.Add(ref owner.VerificationReadBytes, Math.Max(0, n)); }
                return n;
            }
            public void Dispose() { _file.Dispose(); Interlocked.Increment(ref owner.Disposals); }
        }
        private sealed class PartialSource(VerificationProvider owner, bool verifying) : Source(owner, verifying), IPartialContent
        {
            public IReadOnlyList<(long Offset, long Length)> MissingRanges => owner._behavior == "partial-range-before" || owner._behavior == "partial-range-during" && ReadStarted ? [(0, Math.Max(1, owner._length))] : [];
            public string? Caveat => owner._behavior == "partial-before" || owner._behavior == "partial-during" && ReadStarted ? "Owned control: reopened verification bytes are uncertain." : null;
        }
    }
}
