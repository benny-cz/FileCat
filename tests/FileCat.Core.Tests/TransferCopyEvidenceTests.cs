using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class TransferCopyEvidenceTests(ITestOutputHelper output)
{
    public static TheoryData<int, string, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, string, bool, bool>();
            foreach (int length in new[] { 0, 65537, 1048577 })
            foreach (string behavior in new[] { "stable", "unknown", "revision-during", "revision-lost", "length-before", "length-during", "short", "overrun", "negative", "overreported", "initial-revision-io", "final-revision-io", "partial-clean", "partial-caveat-before", "partial-caveat-during", "partial-range-before", "partial-range-during" })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(length, behavior, replace, readBack);
            foreach (int length in new[] { 524289, 1048577 })
            foreach (string behavior in new[] { "resume-same", "resume-native-id", "resume-modified" })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(length, behavior, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_copy_stage_retains_consistent_complete_source_evidence(int length, string behavior, bool replace, bool readBack)
    {
        using var dir = new TempDir();
        byte[] original = Pattern(length); string source = dir.File("source.dat", ""); File.WriteAllBytes(source, original);
        string destination = dir.Dir("destination"), target = Path.Join(destination, "copy.dat");
        byte[] previous = "owned prior destination\n"u8.ToArray(); if (replace) File.WriteAllBytes(target, previous);
        var provider = new CopyProvider(source, length, behavior);
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(provider);
        var jobs = new JobManager(new PortableFileOperations(), providers, dir.Dir("journal")) { MaxConcurrent = 0 };
        int decisions = 0;
        jobs.DecisionRequested += d => { Interlocked.Increment(ref decisions); d.Resolve(new Decision(DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [new ItemRef(new Location(provider.Scheme, "/"), "copy.dat", EntryKind.File, length)],
                Destination = Location.FileSystem(destination), Options = new TransferOptions { Verify = readBack ? VerifyMode.ReadBack : VerifyMode.Native, Conflicts = ConflictPolicy.Replace },
            });
            jobs.MaxConcurrent = 4; jobs.Schedule(); await Wait(job, jobs);
            bool partial = behavior.StartsWith("partial-", StringComparison.Ordinal);
            bool resumed = behavior.StartsWith("resume-", StringComparison.Ordinal);
            bool accepted = behavior is "stable" or "unknown" || partial || resumed || behavior == "short" && length == 0;
            bool warning = partial && behavior != "partial-clean";
            byte[] expected = File.ReadAllBytes(source);
            int staged = Directory.GetFiles(destination, JournalRecovery.StagedPrefix + "*").Length;
            output.WriteLine("TRANSFER_COPY_EVIDENCE " + JsonSerializer.Serialize(new
            {
                Control = "copy-provider", length, behavior, replace, readBack, accepted, warning, job.State,
                provider.Opens, provider.Disposals, provider.CopyRevisionQueries, provider.CopyReadBytes, Reads = provider.Reads.ToArray(), decisions,
                SourceSHA256 = Hash(expected), OriginalSHA256 = Hash(original), TargetSHA256 = File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null,
                PreviousSHA256 = Hash(previous), StagedFiles = staged, job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal,
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }).ToArray(),
                OwnedFileReadsSourceMutationStagingAndDestination = true, SyntheticProviderRevisionLengthPartialAndReadControls = true,
                NativeServerPhysicalDesktopAtomicOrCandidateQualified = false,
            }));
            Assert.Equal(provider.Opens, provider.Disposals); Assert.Empty(Directory.GetFiles(destination, JournalRecovery.StagedPrefix + "*"));
            if (!resumed && behavior != "unknown") Assert.InRange(provider.CopyReadBytes, 0, (long)length + 1);
            if (accepted)
            {
                Assert.Equal(warning ? JobState.CompletedWithIssues : JobState.Completed, job.State);
                Assert.Equal(expected, File.ReadAllBytes(target)); Assert.Equal(0, decisions);
                Assert.Equal((resumed ? 2 : 1) + (readBack && !partial ? 1 : 0), provider.Opens);
                Assert.Equal(length, job.BytesDone);
                Assert.Equal(readBack && !partial ? 2L * length : 0, job.VerifyBytesDone);
                if (!resumed) Assert.Equal(2, provider.CopyRevisionQueries);
                if (warning) Assert.Contains(job.Issues, i => i.Severity == IssueSeverity.Warning);
            }
            else
            {
                Assert.True(job.State is JobState.Failed or JobState.CompletedWithIssues); Assert.NotEmpty(job.Issues);
                Assert.Equal(1, provider.Opens); Assert.Equal(0, decisions); Assert.Equal(0, job.BytesDone);
                if (replace) Assert.Equal(previous, File.ReadAllBytes(target)); else Assert.False(File.Exists(target));
            }
            if (!resumed || behavior == "resume-same") Assert.Equal(original, expected);
            else Assert.NotEqual(Hash(original), Hash(expected));
        }
        finally
        {
            foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
        }
    }

    public static TheoryData<int, int, bool, bool> ArchiveCases
    {
        get
        {
            var cases = new TheoryData<int, int, bool, bool>();
            foreach (int declared in new[] { 0, 65537, 1048577 })
            foreach (int delta in new[] { 0, 1, (int)MemberLimits.Slack, (int)MemberLimits.Slack + 1, -1 })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true })
                if (declared + delta >= 0) cases.Add(declared, delta, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ArchiveCases))]
    public async Task Progressive_members_keep_their_explicit_declared_size_slack(int declared, int delta, bool replace, bool readBack)
    {
        using var dir = new TempDir(); byte[] data = Pattern(declared + delta);
        string source = dir.File("archive-member.dat", ""); File.WriteAllBytes(source, data);
        string destination = dir.Dir("destination"), target = Path.Join(destination, "copy.dat");
        byte[] previous = "owned prior destination\n"u8.ToArray(); if (replace) File.WriteAllBytes(target, previous);
        var provider = new ProgressiveProvider(source, declared, dir.Dir("spool"));
        var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(provider);
        var jobs = new JobManager(new PortableFileOperations(), providers, dir.Dir("journal")) { MaxConcurrent = 0 };
        int decisions = 0; jobs.DecisionRequested += d => { Interlocked.Increment(ref decisions); d.Resolve(new Decision(DecisionAction.Skip)); };
        try
        {
            var job = jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy, Sources = [new ItemRef(new Location(provider.Scheme, "/"), "copy.dat", EntryKind.File, declared)],
                Destination = Location.FileSystem(destination), Options = new TransferOptions { Verify = readBack ? VerifyMode.ReadBack : VerifyMode.Native, Conflicts = ConflictPolicy.Replace },
            });
            jobs.MaxConcurrent = 4; jobs.Schedule(); await Wait(job, jobs);
            bool memberValid = delta >= 0 && delta <= MemberLimits.Slack, accepted = memberValid && (!readBack || delta == 0);
            output.WriteLine("TRANSFER_COPY_EVIDENCE " + JsonSerializer.Serialize(new
            {
                Control = "progressive-member", declared, delta, replace, readBack, accepted, memberValid, job.State,
                provider.Opens, provider.Disposals, decisions, SourceSHA256 = Hash(File.ReadAllBytes(source)), ExpectedSHA256 = Hash(data),
                TargetSHA256 = File.Exists(target) ? Hash(File.ReadAllBytes(target)) : null, PreviousSHA256 = Hash(previous),
                StagedFiles = Directory.GetFiles(destination, JournalRecovery.StagedPrefix + "*").Length,
                SpoolFiles = Directory.GetFiles(provider.Spool).Length, job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal,
                Issues = job.Issues.Select(i => new { i.Message, i.Outcome }).ToArray(),
                ActualProgressiveContentMemberLimitsAndOwnedFileReads = true, SyntheticContainerMetadata = true,
                NativeContainerCorpusServerDesktopPhysicalAtomicOrCandidateQualified = false,
            }));
            Assert.Equal(provider.Opens, provider.Disposals); Assert.Empty(Directory.GetFiles(destination, JournalRecovery.StagedPrefix + "*")); Assert.Empty(Directory.GetFiles(provider.Spool));
            Assert.Equal(data, File.ReadAllBytes(source));
            if (accepted) { Assert.Equal(JobState.Completed, job.State); Assert.Equal(data, File.ReadAllBytes(target)); Assert.Equal(data.Length, job.BytesDone); Assert.Equal(0, decisions); }
            else
            {
                Assert.True(job.State is JobState.Failed or JobState.CompletedWithIssues); Assert.NotEmpty(job.Issues); Assert.Equal(0, job.BytesDone);
                Assert.Equal(memberValid ? 1 : 0, decisions);
                if (replace) Assert.Equal(previous, File.ReadAllBytes(target)); else Assert.False(File.Exists(target));
            }
        }
        finally { foreach (var job in jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !jobs.HasActiveWork, TimeSpan.FromSeconds(3))); }
    }

    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static async Task Wait(Job job, JobManager jobs)
    {
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20), "Owned copy job did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class CopyProvider(string path, int length, string behavior) : ResourceProvider
    {
        private readonly string _path = path, _behavior = behavior;
        private readonly int _length = length;
        private bool _changed;
        public int Opens, Disposals, CopyRevisionQueries;
        public long CopyReadBytes;
        public readonly System.Collections.Concurrent.ConcurrentQueue<ReadObservation> Reads = new();
        public override string Scheme => "copy-owned";
        public override string GetDisplayPath(Location location) => Scheme + ":" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource OpenContent(ItemRef item)
        {
            int opened = Interlocked.Increment(ref Opens);
            if (opened == 2 && behavior is "resume-native-id" or "resume-modified")
            {
                byte[] bytes = File.ReadAllBytes(path);
                // Keep both 64 KiB resume comparisons identical; alter only the middle already copied.
                for (int i = 65537; i < length / 2 - 65536; i++) bytes[i] ^= 0x5A;
                File.WriteAllBytes(path, bytes); _changed = true;
            }
            bool verifying = opened == (behavior.StartsWith("resume-", StringComparison.Ordinal) ? 3 : 2);
            return behavior.StartsWith("partial-", StringComparison.Ordinal) ? new PartialSource(this, verifying) : new Source(this, verifying);
        }
        public sealed record ReadObservation(long Offset, int Requested, int Returned, bool Verifying);
        private class Source(CopyProvider owner, bool verifying) : IContentSource
        {
            private readonly FileContentSource _file = new(owner._path);
            protected bool ReadStarted;
            public string DisplayName => "owned copy.dat";
            public long Length => owner._behavior == "unknown" ? -1 : !verifying && (owner._behavior == "length-before" || owner._behavior == "length-during" && ReadStarted) ? owner._length + 1L : owner._length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision()
            {
                if (!verifying) Interlocked.Increment(ref owner.CopyRevisionQueries);
                if (!verifying && (owner._behavior == "initial-revision-io" && !ReadStarted || owner._behavior == "final-revision-io" && ReadStarted)) throw new IOException("Owned control: revision query refused.");
                if (owner._behavior == "unknown" || owner._behavior == "revision-lost" && owner._changed) return null;
                return new(owner._length, 700000000000000000L + (owner._changed && owner._behavior != "resume-native-id" ? 1 : 0), owner._changed && owner._behavior == "resume-native-id" ? "changed-native-id" : "initial-native-id");
            }
            public int Read(long offset, Span<byte> buffer)
            {
                ReadStarted = true;
                if (!verifying && owner._behavior is "revision-during" or "revision-lost") owner._changed = true;
                int n;
                if (!verifying && owner.Opens == 1 && owner._behavior.StartsWith("resume-", StringComparison.Ordinal))
                {
                    long boundary = owner._length / 2;
                    if (offset >= boundary) throw new IOException("Owned control: first copy disconnected.");
                    n = _file.Read(offset, buffer[..(int)Math.Min(buffer.Length, boundary - offset)]);
                }
                else if (!verifying && owner._behavior == "negative") n = -1;
                else if (!verifying && owner._behavior == "overreported") n = buffer.Length + 1;
                else if (!verifying && owner._behavior == "short") n = offset >= Math.Max(0, owner._length - 1) ? 0 : _file.Read(offset, buffer[..(int)Math.Min(buffer.Length, owner._length - 1 - offset)]);
                else if (!verifying && owner._behavior == "overrun")
                {
                    n = (int)Math.Max(0, Math.Min(buffer.Length, owner._length + 2097152L - offset)); buffer[..n].Fill(0xA5);
                    if (offset < owner._length) _file.Read(offset, buffer[..Math.Min(n, (int)(owner._length - offset))]);
                }
                else n = _file.Read(offset, buffer);
                if (!verifying && n >= 0 && n <= buffer.Length) Interlocked.Add(ref owner.CopyReadBytes, n);
                owner.Reads.Enqueue(new(offset, buffer.Length, n, verifying)); return n;
            }
            public void Dispose() { _file.Dispose(); Interlocked.Increment(ref owner.Disposals); }
        }
        private sealed class PartialSource(CopyProvider owner, bool verifying) : Source(owner, verifying), IPartialContent
        {
            public IReadOnlyList<(long Offset, long Length)> MissingRanges => owner._behavior == "partial-range-before" || owner._behavior == "partial-range-during" && ReadStarted ? [(0, Math.Max(1, owner._length))] : [];
            public string? Caveat => owner._behavior == "partial-caveat-before" || owner._behavior == "partial-caveat-during" && ReadStarted ? "Owned partial control: recovered bytes are uncertain." : null;
        }
    }

    private sealed class ProgressiveProvider(string path, int declared, string spool) : ResourceProvider
    {
        public readonly string Spool = spool;
        public int Opens, Disposals;
        public override string Scheme => "progressive-owned";
        public override string GetDisplayPath(Location location) => Scheme + ":" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        public override IContentSource OpenContent(ItemRef item)
        {
            Interlocked.Increment(ref Opens);
            return new ProgressiveContent("owned progressive member", new object(), () => new ForwardStream(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)),
                MemberLimits.Of(declared, new FileInfo(path).Length, 4 * 1024 * 1024, 1000), 1024, Spool, () => Interlocked.Increment(ref Disposals));
        }
        private sealed class ForwardStream(Stream inner) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override int Read(Span<byte> buffer) => inner.Read(buffer);
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        }
    }
}
