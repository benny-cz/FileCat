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

/// <summary>Owned bytes through the real upload job; provider listings and the destination server are controlled.</summary>
public sealed class UploadTreeEvidenceTests(ITestOutputHelper output)
{
    public static TheoryData<int, string, bool, bool, bool> Trees
    {
        get
        {
            var cases = new TheoryData<int, string, bool, bool, bool>();
            foreach (int length in new[] { 0, 65537 })
            foreach (string behavior in new[] { "stable", "warning-before", "warning-after", "warning-both", "enumeration-io", "enumeration-data", "enumeration-denied", "no-location", "invalid-empty", "invalid-dot", "invalid-parent", "invalid-slash", "invalid-absolute", "invalid-nul", "invalid-empty-folder", "invalid-dot-folder", "invalid-parent-folder", "invalid-slash-folder", "invalid-absolute-folder", "invalid-nul-folder", "valid-dotfile", "valid-backslash", "valid-unicode", "link-file", "link-folder", "ref-link-file", "ref-link-folder", "filter", "parent-row", "cancel-ref" })
            foreach (bool nested in new[] { false, true })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(length, behavior, nested, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Trees))]
    public async Task Folder_upload_preserves_warning_name_link_and_member_scope(int length, string behavior, bool nested, bool replace, bool readBack)
    {
        using var owned = new OwnedDirectory();
        byte[] good = Pattern(length), other = good.Select(b => (byte)(b ^ 0x5A)).ToArray(), previous = "owned prior bytes\n"u8.ToArray();
        string goodPath = owned.File("good.dat", good), otherPath = owned.File("other.dat", other);
        var provider = new TreeProvider(goodPath, otherPath, length, behavior, nested);
        using var setup = new Setup(owned.Path, provider);
        string folder = "/up/tree" + (nested ? "/sub" : "");
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (replace)
        {
            setup.Server.File(folder + "/sentinel.dat", "").Data = previous;
            setup.Server.File(folder + "/good.dat", "").Data = previous;
            expected[folder + "/sentinel.dat"] = Hash(previous); expected[folder + "/good.dat"] = Hash(previous);
            if (!provider.BadIsFolder && !behavior.StartsWith("invalid-", StringComparison.Ordinal) && behavior != "parent-row")
            {
                setup.Server.File(folder + "/" + provider.BadName, "").Data = previous;
                expected[folder + "/" + provider.BadName] = Hash(previous);
            }
        }
        bool fatalListing = behavior.StartsWith("enumeration-", StringComparison.Ordinal);
        bool warning = behavior.StartsWith("warning-", StringComparison.Ordinal);
        bool invalid = behavior.StartsWith("invalid-", StringComparison.Ordinal);
        bool link = behavior.Contains("link-", StringComparison.Ordinal);
        bool canceled = behavior == "cancel-ref";
        bool acceptedBad = !fatalListing && !invalid && !link && behavior is not ("no-location" or "filter" or "parent-row" or "cancel-ref");
        bool completedRoot = !fatalListing && !warning && !invalid && behavior != "no-location" && !canceled;
        if (!fatalListing) expected[folder + "/good.dat"] = Hash(good);
        if (acceptedBad) expected[folder + "/" + provider.BadName] = Hash(other);
        provider.Cancel = () => setup.Current!.Cancel();
        var job = await setup.Run(new ItemRef(new Location(provider.Scheme, "/"), "tree", EntryKind.Directory), readBack,
            behavior == "filter" ? Core.Selection.Mask.Parse("good.*") : null);
        var snapshot = Snapshot(setup.Server);
        output.WriteLine("UPLOAD_TREE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "tree", length, behavior, nested, replace, readBack, acceptedBad, completedRoot, job.State,
            provider.BadName, provider.BadIsFolder, provider.Opens, provider.Disposals, provider.OtherOpens,
            Events = provider.Events.ToArray(), Snapshot = snapshot, ExpectedSnapshot = expected, Directories = Directories(setup.Server),
            GoodSourceSHA256 = Hash(File.ReadAllBytes(goodPath)), OtherSourceSHA256 = Hash(File.ReadAllBytes(otherPath)), OriginalGoodSHA256 = Hash(good), OriginalOtherSHA256 = Hash(other), PreviousSHA256 = Hash(previous),
            job.ItemsTotal, job.ItemsDone, job.ItemsSkipped, job.ItemsFailed, job.BytesTotal, job.BytesDone, job.VerifyBytesTotal, job.VerifyBytesDone, CompletedRoots = job.CompletedRootIndices.ToArray(),
            Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome }).ToArray(), setup.Decisions,
            ActualOwnedFileReadsAndUploadExecutor = true, SyntheticListingLinkFlagsAndInMemoryServer = true, ActualLinkTraversalServerPhysicalNativeDesktopAtomicOrCandidateQualified = false,
        }));
        Assert.Equal(good, File.ReadAllBytes(goodPath)); Assert.Equal(other, File.ReadAllBytes(otherPath));
        Assert.Equal(provider.Opens, provider.Disposals); Assert.Equal(expected, snapshot);
        Assert.Equal((nested ? new[] { "", "/home", "/home/user", "/up", "/up/tree", "/up/tree/sub" } : ["", "/home", "/home/user", "/up", "/up/tree"]), Directories(setup.Server));
        Assert.Equal(completedRoot ? [0] : Array.Empty<int>(), job.CompletedRootIndices);
        if (warning)
        {
            Assert.Equal(JobState.CompletedWithIssues, job.State);
            Assert.Contains(job.Issues, i => i.Severity == IssueSeverity.Warning && i.Message.Contains("owned listing warning", StringComparison.Ordinal));
            Assert.Equal(behavior == "warning-both" ? 2 : 1, job.Issues.Count(i => i.Severity == IssueSeverity.Warning));
        }
        if (invalid || behavior is "link-file" or "link-folder" or "filter" or "parent-row")
        {
            Assert.Equal(0, provider.OtherOpens);
            Assert.DoesNotContain(provider.Events, e => e == "ref:" + provider.BadName || e == "child:" + provider.BadName);
        }
        if (behavior.StartsWith("ref-link-", StringComparison.Ordinal)) Assert.Equal(0, provider.OtherOpens);
        if (invalid || behavior == "no-location") Assert.Contains(job.Issues, i => i.Severity == IssueSeverity.Error);
        if (link) Assert.Contains(job.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase));
        if (canceled) Assert.Equal(JobState.Canceled, job.State);
        if (!fatalListing && !invalid && !link && behavior is not ("no-location" or "cancel-ref"))
        {
            int count = acceptedBad ? 2 : 1;
            Assert.Equal(count, job.ItemsTotal); Assert.Equal(count, job.ItemsDone);
            Assert.Equal((long)count * length, job.BytesTotal); Assert.Equal((long)count * length, job.BytesDone);
        }
    }

    public static TheoryData<int, bool, bool, bool> RootLinks
    {
        get
        {
            var cases = new TheoryData<int, bool, bool, bool>();
            foreach (int length in new[] { 0, 65537 })
            foreach (bool folder in new[] { false, true })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true }) cases.Add(length, folder, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(RootLinks))]
    public async Task A_selected_provider_link_does_not_implicitly_open_its_target(int length, bool folder, bool replace, bool readBack)
    {
        using var owned = new OwnedDirectory(); byte[] data = Pattern(length), previous = "prior linked destination\n"u8.ToArray();
        string path = owned.File("source.dat", data); var provider = new TreeProvider(path, path, length, "stable", false);
        using var setup = new Setup(owned.Path, provider);
        if (replace) setup.Server.File("/up/tree", "").Data = previous;
        var item = new ItemRef(new Location(provider.Scheme, "/"), "tree", folder ? EntryKind.Directory : EntryKind.File, length) { Flags = EntryFlags.Link };
        var job = await setup.Run(item, readBack);
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal); if (replace) expected["/up/tree"] = Hash(previous);
        output.WriteLine("UPLOAD_TREE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "root-link", length, folder, replace, readBack, job.State, provider.Opens, provider.Disposals, Events = provider.Events.ToArray(),
            Snapshot = Snapshot(setup.Server), ExpectedSnapshot = expected, SourceSHA256 = Hash(File.ReadAllBytes(path)), OriginalSHA256 = Hash(data), PreviousSHA256 = Hash(previous),
            CompletedRoots = job.CompletedRootIndices.ToArray(), job.BytesDone, job.VerifyBytesDone, Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome }).ToArray(),
            ActualOwnedFilesAndExecutor = true, SyntheticLinkFlagsAndServer = true, ActualLinkNativeOrCandidateQualified = false,
        }));
        Assert.Equal(expected, Snapshot(setup.Server)); Assert.Equal(data, File.ReadAllBytes(path)); Assert.Equal(0, provider.Opens);
        Assert.Empty(provider.Events); Assert.Empty(job.CompletedRootIndices); Assert.Equal(JobState.CompletedWithIssues, job.State);
        Assert.Contains(job.Issues, i => i.Message.Contains("link", StringComparison.OrdinalIgnoreCase)); Assert.Equal(0, job.BytesDone); Assert.Equal(0, job.VerifyBytesDone);
    }

    public static TheoryData<int, string, string, bool, bool> Faults
    {
        get
        {
            var cases = new TheoryData<int, string, string, bool, bool>();
            foreach (int length in new[] { 0, 65537 })
            foreach (string stage in new[] { "initial-revision", "read-after", "final-revision", "caveat", "ranges", "dispose", "verify-open", "verify-revision", "verify-read", "verify-final" })
            foreach (string exception in new[] { "not-supported", "disposed", "invalid-op", "io", "denied", "data", "cancel" })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true })
                if (readBack || !stage.StartsWith("verify-", StringComparison.Ordinal)) cases.Add(length, stage, exception, replace, readBack);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task Failed_provider_reads_verification_and_disposal_leave_no_staged_upload(int length, string stage, string exception, bool replace, bool readBack)
    {
        using var owned = new OwnedDirectory(); byte[] data = Pattern(length), previous = "prior failure destination\n"u8.ToArray();
        string path = owned.File("source.dat", data); var provider = new FaultProvider(path, length, stage, exception);
        using var setup = new Setup(owned.Path, provider); if (replace) setup.Server.File("/up/copy.dat", "").Data = previous;
        var job = await setup.Run(new ItemRef(new Location(provider.Scheme, "/"), "copy.dat", EntryKind.File, length), readBack);
        var expected = new SortedDictionary<string, string>(StringComparer.Ordinal); if (replace) expected["/up/copy.dat"] = Hash(previous);
        output.WriteLine("UPLOAD_TREE_EVIDENCE " + JsonSerializer.Serialize(new
        {
            Control = "fault", length, stage, exception, replace, readBack, job.State, provider.Attempts, provider.Opens, provider.Disposals,
            Snapshot = Snapshot(setup.Server), ExpectedSnapshot = expected, SourceSHA256 = Hash(File.ReadAllBytes(path)), OriginalSHA256 = Hash(data), PreviousSHA256 = Hash(previous),
            job.BytesDone, job.VerifyBytesDone, job.VerifyBytesTotal, CompletedRoots = job.CompletedRootIndices.ToArray(), setup.Decisions,
            Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome, i.Cause }).ToArray(),
            ActualOwnedReadsAndUploadExecutor = true, ControlledExceptionAndInMemoryServer = true, ActualServerPhysicalNativeOrCandidateQualified = false,
        }));
        Assert.Equal(expected, Snapshot(setup.Server)); Assert.Equal(data, File.ReadAllBytes(path)); Assert.Equal(provider.Opens, provider.Disposals);
        Assert.Empty(job.CompletedRootIndices); Assert.Equal(0, job.BytesDone); Assert.Equal(0, job.VerifyBytesDone); Assert.Equal(0, job.VerifyBytesTotal);
        if (exception == "cancel") Assert.Equal(JobState.Canceled, job.State);
        else
        {
            Assert.True(job.State is JobState.Failed or JobState.CompletedWithIssues);
            Assert.Contains(job.Issues, i => exception == "denied" ? i.Severity == IssueSeverity.Error && i.Cause == "access" : i.Message.Contains("owned provider failure", StringComparison.Ordinal));
        }
        Assert.Equal(exception is "io" or "denied" or "data" ? 1 : 0, setup.Decisions);
    }

    private sealed class Setup : IDisposable
    {
        public FakeSftpServer Server { get; } = new();
        private readonly SftpConnections _connections;
        private readonly JobManager _jobs;
        private readonly RemoteProfile _profile = new() { Name = "Owned tree server", Host = "files.example", User = "user" };
        public int Decisions;
        public Job? Current;
        public Setup(string directory, ResourceProvider source)
        {
            Server.Dir("/up");
            var interaction = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce }; interaction.Secrets.Enqueue("secret");
            _connections = new SftpConnections(id => id == _profile.Id ? _profile : null, new FakeConnector(Server),
                new HostKeyTrust(System.IO.Path.Join(directory, "known_hosts"), []), new SessionSecretStore(), interaction);
            var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(source);
            providers.Register(new SftpProvider(_connections, () => [_profile], p => p)); SftpJobs.Register();
            _jobs = new JobManager(new PortableFileOperations(), providers, System.IO.Path.Join(directory, "journal")) { MaxConcurrent = 0 };
            _jobs.DecisionRequested += d => { Interlocked.Increment(ref Decisions); d.Resolve(new Decision(DecisionAction.Skip)); };
        }
        public async Task<Job> Run(ItemRef source, bool readBack, Core.Selection.Mask? filter = null)
        {
            Current = _jobs.Submit(new JobRequest { Kind = JobKind.Copy, Sources = [source], Destination = SftpProvider.At(_profile, "/up"),
                Options = new TransferOptions { Verify = readBack ? VerifyMode.ReadBack : VerifyMode.Native, Conflicts = ConflictPolicy.Replace, Filter = filter } });
            _jobs.MaxConcurrent = 4; _jobs.Schedule(); var clock = Stopwatch.StartNew();
            while (!Current.State.IsFinished() || _jobs.HasActiveWork)
            {
                Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20)); await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            return Current;
        }
        public void Dispose()
        {
            foreach (var job in _jobs.Jobs) job.Cancel(); Assert.True(SpinWait.SpinUntil(() => !_jobs.HasActiveWork, TimeSpan.FromSeconds(3))); _connections.Dispose();
        }
    }

    private sealed class TreeProvider(string goodPath, string otherPath, int length, string behavior, bool nested) : ResourceProvider
    {
        public override string Scheme => "owned-upload-tree";
        public int Opens, OtherOpens, Disposals;
        public Action? Cancel;
        public readonly List<string> Events = [];
        public string BadName => behavior.Replace("-folder", "", StringComparison.Ordinal) switch
        {
            "invalid-empty" => "", "invalid-dot" => ".", "invalid-parent" => "..", "invalid-slash" => "part/name", "invalid-absolute" => "/absolute", "invalid-nul" => "nul\0name",
            "valid-dotfile" => ".config", "valid-backslash" => "..\\name", "valid-unicode" => "žluťoučký-猫.dat", "parent-row" => "..", "no-location" => "bad-dir", _ => "copy.dat",
        };
        public bool BadIsFolder => behavior is "link-folder" or "ref-link-folder" or "no-location" || behavior.StartsWith("invalid-", StringComparison.Ordinal) && behavior.EndsWith("-folder", StringComparison.Ordinal);
        public override string GetDisplayPath(Location location) => Scheme + ":" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry)
        {
            Events.Add("child:" + entry.Name);
            return entry.Name == "bad-dir" ? null : new Location(Scheme, parent.Path.TrimEnd('/') + "/" + entry.Name);
        }
        public override ItemRef GetItemRef(Location listing, in EntryData entry)
        {
            Events.Add("ref:" + entry.Name);
            if (behavior == "cancel-ref" && entry.Name == BadName) Cancel?.Invoke();
            var item = ItemRef.FromEntry(listing, entry);
            return behavior.StartsWith("ref-link-", StringComparison.Ordinal) && entry.Name == BadName
                ? new ItemRef(item.Parent, item.Name, item.Kind, item.Size) { Flags = EntryFlags.Link } : item;
        }
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Events.Add("list:" + location.Path);
            if (nested && location.Path == "/tree") { sink.AddBatch([new EntryData("sub", EntryKind.Directory)]); return Task.CompletedTask; }
            if (location.Path != "/tree" + (nested ? "/sub" : ""))
            {
                sink.AddBatch([new EntryData("external.dat", EntryKind.File, length)]); return Task.CompletedTask;
            }
            if (behavior is "warning-before" or "warning-both") sink.ReportIssue("owned listing warning before readable batch");
            var bad = new EntryData(BadName, behavior == "parent-row" ? EntryKind.Parent : BadIsFolder ? EntryKind.Directory : EntryKind.File, length);
            if (behavior is "link-file" or "link-folder") bad.Flags = EntryFlags.Link;
            sink.AddBatch([new EntryData("good.dat", EntryKind.File, length), bad]);
            if (behavior is "warning-after" or "warning-both") sink.ReportIssue("owned listing warning after readable batch");
            if (behavior == "enumeration-io") throw new IOException("owned listing failure");
            if (behavior == "enumeration-data") throw new InvalidDataException("owned listing failure");
            if (behavior == "enumeration-denied") throw new UnauthorizedAccessException("owned listing failure");
            return Task.CompletedTask;
        }
        public override IContentSource OpenContent(ItemRef item)
        {
            Interlocked.Increment(ref Opens); bool other = item.Name != "good.dat"; if (other) Interlocked.Increment(ref OtherOpens);
            return new OwnedSource(other ? otherPath : goodPath, () => Interlocked.Increment(ref Disposals));
        }
    }

    private sealed class OwnedSource(string path, Action disposed) : IContentSource
    {
        private readonly FileContentSource _file = new(path);
        public string DisplayName => "owned upload control";
        public long Length => _file.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public ContentRevision? GetRevision() => new(Length, 700000000000000000L, "owned-id");
        public int Read(long offset, Span<byte> buffer) => _file.Read(offset, buffer);
        public void Dispose() { _file.Dispose(); disposed(); }
    }

    private sealed class FaultProvider(string path, int length, string stage, string exception) : ResourceProvider
    {
        private readonly string _path = path, _stage = stage;
        private readonly int _length = length;
        public override string Scheme => "owned-upload-fault";
        public int Attempts, Opens, Disposals;
        public override string GetDisplayPath(Location location) => Scheme + ":" + location.Path;
        public override Location? GetParent(Location location) => null;
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.ReadContent;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.CompletedTask;
        private void Fail() => throw exception switch
        {
            "not-supported" => new NotSupportedException("owned provider failure"), "disposed" => new ObjectDisposedException("owned provider failure"),
            "invalid-op" => new InvalidOperationException("owned provider failure"), "io" => new IOException("owned provider failure"),
            "denied" => new UnauthorizedAccessException("owned provider failure"), "data" => new InvalidDataException("owned provider failure"), _ => new OperationCanceledException("owned provider failure"),
        };
        public override IContentSource OpenContent(ItemRef item)
        {
            bool verifying = Interlocked.Increment(ref Attempts) > 1;
            if (verifying && stage == "verify-open") Fail();
            Interlocked.Increment(ref Opens);
            return stage is "caveat" or "ranges" ? new PartialSource(this, verifying) : new Source(this, verifying);
        }
        private class Source(FaultProvider owner, bool verifying) : IContentSource
        {
            private readonly FileContentSource _file = new(owner._path);
            private bool _ended;
            public string DisplayName => "owned failing source";
            public long Length => owner._length;
            public bool CanSeek => true;
            public string? LocalPath => null;
            public ContentRevision? GetRevision()
            {
                if ((!verifying && owner._stage == "initial-revision" || verifying && owner._stage == "verify-revision") && !_ended ||
                    (!verifying && owner._stage == "final-revision" || verifying && owner._stage == "verify-final") && _ended) owner.Fail();
                return new(owner._length, 700000000000000000L, "owned-id");
            }
            public int Read(long offset, Span<byte> buffer)
            {
                if ((!verifying && owner._stage == "read-after" || verifying && owner._stage == "verify-read") && offset >= owner._length) owner.Fail();
                int n = _file.Read(offset, buffer); _ended |= n == 0; return n;
            }
            public void Dispose()
            {
                _file.Dispose(); Interlocked.Increment(ref owner.Disposals); if (!verifying && owner._stage == "dispose") owner.Fail();
            }
        }
        private sealed class PartialSource(FaultProvider owner, bool verifying) : Source(owner, verifying), IPartialContent
        {
            public IReadOnlyList<(long Offset, long Length)> MissingRanges { get { if (owner._stage == "ranges") owner.Fail(); return []; } }
            public string? Caveat { get { if (owner._stage == "caveat") owner.Fail(); return null; } }
        }
    }

    private sealed class OwnedDirectory : IDisposable
    {
        private static readonly string Root = System.IO.Path.Join(System.IO.Path.GetTempPath(), "filecat-upload-tree");
        public string Path { get; } = System.IO.Path.Join(Root, Guid.NewGuid().ToString("N"));
        public OwnedDirectory() => Directory.CreateDirectory(Path);
        public string File(string name, byte[] bytes) { string p = System.IO.Path.Join(Path, name); System.IO.File.WriteAllBytes(p, bytes); return p; }
        public void Dispose()
        {
            string resolved = System.IO.Path.GetFullPath(Path); Assert.Equal(System.IO.Path.GetFileName(Path), System.IO.Path.GetRelativePath(Root, resolved)); Directory.Delete(resolved, true);
        }
    }
    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static SortedDictionary<string, string> Snapshot(FakeSftpServer server)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Walk(FakeSftpServer.Node node, string path)
        {
            if (!node.IsDirectory) { files[path] = Hash(node.Data); return; }
            foreach (var child in node.Children.Values) Walk(child, path.TrimEnd('/') + "/" + child.Name);
        }
        lock (server.Lock) Walk(server.Root, ""); return files;
    }
    private static string[] Directories(FakeSftpServer server)
    {
        var paths = new List<string>();
        void Walk(FakeSftpServer.Node node, string path)
        {
            if (!node.IsDirectory) return;
            paths.Add(path); foreach (var child in node.Children.Values) Walk(child, path.TrimEnd('/') + "/" + child.Name);
        }
        lock (server.Lock) Walk(server.Root, ""); return paths.Order(StringComparer.Ordinal).ToArray();
    }
}
