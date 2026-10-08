using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class UploadSourceEvidenceTests(ITestOutputHelper output)
{
    public static TheoryData<int, string, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, string, bool, bool>();
            foreach (int length in new[] { 0, 65537, 1048577 })
            foreach (string behavior in new[] { "stable", "unknown", "revision-during", "revision-lost", "length-before", "length-during", "short", "overrun", "negative", "overreported", "initial-revision-io", "final-revision-io", "partial-clean", "partial-caveat-before", "partial-caveat-during", "partial-range-before", "partial-range-during", "revision-before-verification" })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(length, behavior, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Provider_upload_retains_length_revision_partial_and_disposal_evidence(int length, string behavior, bool replace, bool readBack)
    {
        using var directory = new OwnedDirectory();
        byte[] data = Pattern(length), previous = "owned prior server destination\n"u8.ToArray();
        string source = directory.File("source.dat", data);
        var provider = new CopyProvider(source, length, behavior);
        using var setup = new Setup(directory.Path, provider, replace, previous);
        var job = await setup.Run([new ItemRef(new Location(provider.Scheme, "/"), "copy.dat", EntryKind.File, length)], readBack);
        bool partial = behavior.StartsWith("partial-", StringComparison.Ordinal);
        bool accepted = behavior is "stable" or "unknown" || partial || behavior == "short" && length == 0 || behavior == "revision-before-verification" && !readBack;
        bool warning = partial && behavior != "partial-clean";
        var target = setup.Server.Lookup("/up/copy.dat", false);
        output.WriteLine("UPLOAD_SOURCE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "provider", length, behavior, replace, readBack, accepted, warning, job.State,
            provider.Opens, provider.Disposals, provider.CopyRevisionQueries, provider.CopyReadBytes, Reads = provider.Reads.ToArray(),
            SourceSHA256 = Hash(File.ReadAllBytes(source)), OriginalSHA256 = Hash(data), PreviousSHA256 = Hash(previous),
            TargetSHA256 = target is null ? null : Hash(target.Data), TargetLength = target?.Data.Length,
            Names = setup.Names, Decisions = setup.Decisions, job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal,
            Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome }).ToArray(),
            ActualOwnedFileReadsAndUploadExecutor = true, SyntheticProviderAndInMemoryServer = true, ActualServerPhysicalNativeDesktopAtomicOrCandidateQualified = false,
        }));
        Assert.Equal(data, File.ReadAllBytes(source)); Assert.Equal(provider.Opens, provider.Disposals);
        if (behavior != "unknown") Assert.InRange(provider.CopyReadBytes, 0, (long)length + 1);
        Assert.Equal(replace || accepted ? ["copy.dat"] : Array.Empty<string>(), setup.Names);
        if (accepted)
        {
            Assert.Equal(warning ? JobState.CompletedWithIssues : JobState.Completed, job.State); Assert.Equal(data, target!.Data);
            Assert.Equal(0, setup.Decisions); Assert.Equal(1 + (readBack && !partial ? 1 : 0), provider.Opens);
            Assert.Equal(2, provider.CopyRevisionQueries); Assert.Equal(length, job.BytesDone);
            Assert.Equal(readBack && !partial ? 2L * length : 0, job.VerifyBytesDone);
            if (warning) Assert.Contains(job.Issues, i => i.Severity == IssueSeverity.Warning);
        }
        else
        {
            Assert.True(job.State is JobState.Failed or JobState.CompletedWithIssues); Assert.NotEmpty(job.Issues);
            Assert.Equal(0, job.BytesDone); Assert.Equal(0, job.VerifyBytesDone); Assert.Equal(0, job.VerifyBytesTotal);
            if (replace) Assert.Equal(previous, target!.Data); else Assert.Null(target);
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
            foreach (bool readBack in new[] { false, true }) if (declared + delta >= 0) cases.Add(declared, delta, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(ArchiveCases))]
    public async Task Progressive_upload_keeps_member_slack_and_refuses_damage(int declared, int delta, bool replace, bool readBack)
    {
        using var directory = new OwnedDirectory(); byte[] data = Pattern(declared + delta), previous = "owned prior server destination\n"u8.ToArray();
        string source = directory.File("member.dat", data); var provider = new ProgressiveProvider(source, declared, directory.Dir("spool"));
        using var setup = new Setup(directory.Path, provider, replace, previous);
        var job = await setup.Run([new ItemRef(new Location(provider.Scheme, "/"), "copy.dat", EntryKind.File, declared)], readBack);
        bool accepted = delta >= 0 && delta <= MemberLimits.Slack; var target = setup.Server.Lookup("/up/copy.dat", false);
        output.WriteLine("UPLOAD_SOURCE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "progressive", declared, delta, replace, readBack, accepted, job.State, provider.Opens, provider.Disposals,
            SourceSHA256 = Hash(File.ReadAllBytes(source)), ExpectedSHA256 = Hash(data), PreviousSHA256 = Hash(previous),
            TargetSHA256 = target is null ? null : Hash(target.Data), TargetLength = target?.Data.Length, Names = setup.Names, Decisions = setup.Decisions,
            SpoolFiles = Directory.GetFiles(provider.Spool).Length, job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal,
            Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome }).ToArray(),
            ActualOwnedFileProgressiveContentMemberLimitsAndUploadExecutor = true, SyntheticContainerMetadataAndInMemoryServer = true,
            ActualContainerCorpusServerPhysicalNativeDesktopAtomicOrCandidateQualified = false,
        }));
        Assert.Equal(data, File.ReadAllBytes(source)); Assert.Equal(provider.Opens, provider.Disposals); Assert.Empty(Directory.GetFiles(provider.Spool));
        Assert.Equal(replace || accepted ? ["copy.dat"] : Array.Empty<string>(), setup.Names);
        if (accepted)
        {
            Assert.Equal(JobState.Completed, job.State); Assert.Equal(data, target!.Data); Assert.Equal(data.Length, job.BytesDone);
            Assert.Equal(readBack ? 2L * data.Length : 0, job.VerifyBytesDone); Assert.Equal(0, setup.Decisions);
        }
        else
        {
            Assert.True(job.State is JobState.Failed or JobState.CompletedWithIssues); Assert.NotEmpty(job.Issues); Assert.Equal(0, job.BytesDone);
            if (replace) Assert.Equal(previous, target!.Data); else Assert.Null(target);
        }
    }

    public static TheoryData<int, string, bool, bool, bool> LocalCases
    {
        get
        {
            var cases = new TheoryData<int, string, bool, bool, bool>();
            foreach (int length in new[] { 0, 65537, 1048577 })
            foreach (string behavior in new[] { "stable", "growth", "short", "same-size-changed" })
            foreach (bool move in new[] { false, true })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) if (length > 0 || behavior == "stable") cases.Add(length, behavior, move, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(LocalCases))]
    public async Task Local_upload_keeps_changed_source_and_prior_destination(int length, string behavior, bool move, bool replace, bool readBack)
    {
        using var directory = new OwnedDirectory(); byte[] data = Pattern(length), previous = "owned prior server destination\n"u8.ToArray();
        string source = directory.File("copy.dat", data); File.SetLastWriteTimeUtc(source, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        byte[] changed = behavior == "growth" ? [..data, 0xA5] : behavior == "short" ? data[..^1] : data.Select(b => (byte)(b ^ 0x5A)).ToArray();
        using var setup = new Setup(directory.Path, null, replace, previous); int swaps = 0;
        if (behavior != "stable") setup.Server.DuringUpload = () => { File.Move(source, source + ".prior"); File.WriteAllBytes(source, changed); swaps++; };
        var job = await setup.Run([ItemRef.ForFileSystemPath(source, EntryKind.File)], readBack, move);
        bool accepted = behavior == "stable", exists = File.Exists(source); var target = setup.Server.Lookup("/up/copy.dat", false);
        output.WriteLine("UPLOAD_SOURCE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "local", length, behavior, move, replace, readBack, accepted, exists, swaps, job.State,
            SourceSHA256 = exists ? Hash(File.ReadAllBytes(source)) : null, ExpectedSourceSHA256 = Hash(accepted ? data : changed),
            OriginalSHA256 = Hash(data), PreviousSHA256 = Hash(previous), TargetSHA256 = target is null ? null : Hash(target.Data), TargetLength = target?.Data.Length,
            Names = setup.Names, Decisions = setup.Decisions, job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal,
            Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome }).ToArray(),
            ActualOwnedFileReplacementAndUploadExecutor = true, SyntheticInMemoryServer = true, ActualServerPhysicalNativeDesktopAtomicOrCandidateQualified = false,
        }));
        Assert.Equal(!accepted || !move, exists); Assert.Equal(accepted ? 0 : 1, swaps);
        if (exists) Assert.Equal(accepted ? data : changed, File.ReadAllBytes(source));
        Assert.Equal(replace || accepted ? ["copy.dat"] : Array.Empty<string>(), setup.Names);
        if (accepted)
        {
            Assert.Equal(JobState.Completed, job.State); Assert.Equal(data, target!.Data); Assert.Equal(length, job.BytesDone);
            Assert.Equal(readBack ? 2L * length : 0, job.VerifyBytesDone); Assert.Equal(0, setup.Decisions);
        }
        else
        {
            Assert.True(job.State is JobState.Failed or JobState.CompletedWithIssues); Assert.NotEmpty(job.Issues);
            Assert.Equal(0, job.BytesDone); Assert.Equal(0, job.VerifyBytesDone); Assert.Equal(0, job.VerifyBytesTotal);
            if (replace) Assert.Equal(previous, target!.Data); else Assert.Null(target);
        }
    }

    public static TheoryData<bool, bool, bool> TimestampCases
    {
        get
        {
            var cases = new TheoryData<bool, bool, bool>();
            foreach (bool known in new[] { false, true })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(known, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(TimestampCases))]
    public async Task Provider_upload_uses_the_opened_revision_time_before_listing_time(bool known, bool replace, bool readBack)
    {
        using var directory = new OwnedDirectory(); byte[] data = Pattern(65537), previous = "owned prior server destination\n"u8.ToArray();
        string source = directory.File("source.dat", data); var provider = new CopyProvider(source, data.Length, known ? "stable" : "unknown");
        long listed = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks, expected = known ? 700000000000000000L : listed;
        using var setup = new Setup(directory.Path, provider, replace, previous);
        var job = await setup.Run([new ItemRef(new Location(provider.Scheme, "/"), "copy.dat", EntryKind.File, data.Length, listed)], readBack);
        var target = setup.Server.Lookup("/up/copy.dat", false);
        output.WriteLine("UPLOAD_SOURCE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "timestamp", known, replace, readBack, listed, expected, job.State, provider.Opens, provider.Disposals, provider.CopyRevisionQueries,
            SourceSHA256 = Hash(File.ReadAllBytes(source)), ExpectedSHA256 = Hash(data), PreviousSHA256 = Hash(previous),
            TargetSHA256 = target is null ? null : Hash(target.Data), TargetModifiedTicks = target?.Modified.Ticks, Names = setup.Names, Decisions = setup.Decisions,
            job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal,
            ActualOwnedFileReadsAndUploadExecutor = true, SyntheticProviderRevisionAndStaleListingTimeAndInMemoryServer = true,
            ActualServerPhysicalNativeDesktopAtomicOrCandidateQualified = false,
        }));
        Assert.Equal(JobState.Completed, job.State); Assert.Equal(data, target!.Data); Assert.Equal(expected, target.Modified.Ticks);
        Assert.Equal(provider.Opens, provider.Disposals); Assert.Equal(2, provider.CopyRevisionQueries); Assert.Equal(0, setup.Decisions);
        Assert.Equal(["copy.dat"], setup.Names);
    }

    public static TheoryData<int, bool, bool, bool, bool> RetryCases
    {
        get
        {
            var cases = new TheoryData<int, bool, bool, bool, bool>();
            foreach (int length in new[] { 524289, 1048577 })
            foreach (bool grow in new[] { false, true })
            foreach (bool move in new[] { false, true })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(length, grow, move, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(RetryCases))]
    public async Task An_explicit_upload_retry_retains_the_new_local_version(int length, bool grow, bool move, bool replace, bool readBack)
    {
        using var directory = new OwnedDirectory(); byte[] data = Pattern(length), previous = "owned prior server destination\n"u8.ToArray();
        byte[] changed = Pattern(length + (grow ? 17 : -17)).Select(b => (byte)(b ^ 0x5A)).ToArray();
        string source = directory.File("copy.dat", data); File.SetLastWriteTimeUtc(source, new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        using var setup = new Setup(directory.Path, null, replace, previous); setup.Server.DropAfterBytes = length / 2;
        int mutations = 0;
        setup.RetryFirst = () =>
        {
            File.WriteAllBytes(source, changed); File.SetLastWriteTimeUtc(source, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            setup.Server.Down = false; setup.Server.DropAfterBytes = null; mutations++;
        };
        var job = await setup.Run([ItemRef.ForFileSystemPath(source, EntryKind.File)], readBack, move);
        var target = setup.Server.Lookup("/up/copy.dat", false); bool exists = File.Exists(source);
        output.WriteLine("UPLOAD_SOURCE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "retry", length, grow, move, replace, readBack, exists, mutations, job.State, Decisions = setup.Decisions,
            SourceSHA256 = exists ? Hash(File.ReadAllBytes(source)) : null, ExpectedSHA256 = Hash(changed), OriginalSHA256 = Hash(data),
            PreviousSHA256 = Hash(previous), TargetSHA256 = target is null ? null : Hash(target.Data), TargetLength = target?.Data.Length,
            Names = setup.Names, ContinuedAt = setup.Server.WritesAt.ToArray(), setup.Server.Connects, job.BytesDone, job.BytesTotal, job.VerifyBytesDone, job.VerifyBytesTotal,
            Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome }).ToArray(),
            ActualOwnedFileMutationExplicitRetryAndUploadExecutor = true, SyntheticDisconnectAndInMemoryServer = true,
            ActualServerPhysicalNativeDesktopAtomicOrCandidateQualified = false,
        }));
        Assert.Equal(JobState.Completed, job.State); Assert.Equal(1, mutations); Assert.Equal(1, setup.Decisions);
        Assert.Equal(!move, exists); if (exists) Assert.Equal(changed, File.ReadAllBytes(source));
        Assert.Equal(changed, target!.Data); Assert.Equal(["copy.dat"], setup.Names); Assert.Empty(setup.Server.WritesAt);
        Assert.Equal(2, setup.Server.Connects); Assert.Equal(changed.Length, job.BytesDone); Assert.Equal(changed.Length, job.BytesTotal);
        Assert.Equal(readBack ? 2L * changed.Length : 0, job.VerifyBytesDone); Assert.Equal(job.VerifyBytesDone, job.VerifyBytesTotal);
    }

    private sealed class Setup : IDisposable
    {
        public FakeSftpServer Server { get; } = new();
        private readonly SftpConnections _connections;
        private readonly JobManager _jobs;
        private readonly RemoteProfile _profile = new() { Name = "Owned fake server", Host = "files.example", User = "user" };
        public int Decisions;
        public Action? RetryFirst;
        public string[] Names => Server.Lookup("/up", true)!.Children.Keys.ToArray();
        public Setup(string directory, ResourceProvider? source, bool replace, byte[] previous)
        {
            Server.Dir("/up"); if (replace) Server.File("/up/copy.dat", "").Data = previous;
            var interaction = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
            for (int i = 0; i < 10; i++) interaction.Secrets.Enqueue("secret");
            _connections = new SftpConnections(id => id == _profile.Id ? _profile : null, new FakeConnector(Server),
                new HostKeyTrust(System.IO.Path.Join(directory, "known_hosts"), []), new SessionSecretStore(), interaction);
            var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
            if (source is not null) providers.Register(source);
            providers.Register(new SftpProvider(_connections, () => [_profile], p => p)); SftpJobs.Register();
            _jobs = new JobManager(new PortableFileOperations(), providers, System.IO.Path.Join(directory, "journal")) { MaxConcurrent = 0 };
            _jobs.DecisionRequested += d =>
            {
                if (Interlocked.Increment(ref Decisions) == 1 && RetryFirst is not null)
                {
                    RetryFirst(); d.Resolve(new Decision(DecisionAction.Retry));
                }
                else d.Resolve(new Decision(DecisionAction.Skip));
            };
        }
        public async Task<Job> Run(IReadOnlyList<ItemRef> sources, bool readBack, bool move = false)
        {
            var job = _jobs.Submit(new JobRequest { Kind = move ? JobKind.Move : JobKind.Copy, Sources = sources,
                Destination = SftpProvider.At(_profile, "/up"), Options = new TransferOptions { Verify = readBack ? VerifyMode.ReadBack : VerifyMode.Native, Conflicts = ConflictPolicy.Replace } });
            _jobs.MaxConcurrent = 4; _jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!job.State.IsFinished() || _jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20)); await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            return job;
        }
        public void Dispose()
        {
            foreach (var job in _jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !_jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
            _connections.Dispose();
        }
    }
    private sealed class OwnedDirectory : IDisposable
    {
        private static readonly string Root = System.IO.Path.Join(System.IO.Path.GetTempPath(), "filecat-upload-source");
        public string Path { get; } = System.IO.Path.Join(Root, Guid.NewGuid().ToString("N"));
        public OwnedDirectory() => Directory.CreateDirectory(Path);
        public string File(string name, byte[] bytes) { string p = System.IO.Path.Join(Path, name); System.IO.File.WriteAllBytes(p, bytes); return p; }
        public string Dir(string name) => Directory.CreateDirectory(System.IO.Path.Join(Path, name)).FullName;
        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path);
            Assert.Equal(System.IO.Path.GetFileName(Path), System.IO.Path.GetRelativePath(Root, resolved)); Directory.Delete(resolved, recursive: true);
        }
    }
    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
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
            if (opened == 2 && behavior == "revision-before-verification") _changed = true;
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
