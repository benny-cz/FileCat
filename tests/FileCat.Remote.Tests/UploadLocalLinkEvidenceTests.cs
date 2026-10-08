using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>Real owned local links through upload jobs, against controlled or user-mode loopback OpenSSH servers.</summary>
public sealed class UploadLocalLinkEvidenceTests(ITestOutputHelper output)
{
    public static TheoryData<int, string, bool, bool, bool, bool> Cases
    {
        get
        {
            var cases = new TheoryData<int, string, bool, bool, bool, bool>();
            foreach (int length in new[] { 0, 65537 })
            foreach (string behavior in new[] { "plain-file", "selected-file-link", "unflagged-file-link", "selected-directory-link", "unflagged-directory-link", "selected-dangling-link", "swapped-file-link", "tree-with-links" })
            foreach (bool moving in new[] { false, true })
            foreach (bool replace in new[] { false, true })
            foreach (bool readBack in new[] { false, true })
            foreach (bool nativeServer in new[] { false, true }) cases.Add(length, behavior, moving, replace, readBack, nativeServer);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_local_upload_keeps_links_out_of_implicit_content_scope(int length, string behavior, bool moving, bool replace, bool readBack, bool nativeServer)
    {
        using var owned = new Owned();
        using var sshd = nativeServer ? TestSshd.TryStart() : null;
        if (nativeServer && sshd is null) Assert.Skip("This owned OpenSSH case requires a user-mode sshd; it is unavailable here.");
        byte[] data = Pattern(length), target = Pattern(length).Select(b => (byte)(b ^ 0x5A)).ToArray(), prior = "owned original destination\n"u8.ToArray();
        string targetFile = Path.Combine(owned.Path, "target.dat"), targetDirectory = Directory.CreateDirectory(Path.Combine(owned.Path, "target-dir")).FullName;
        File.WriteAllBytes(targetFile, target); File.WriteAllBytes(Path.Combine(targetDirectory, "target.dat"), target);
        string source = Path.Combine(owned.Path, "source"), saved = Path.Combine(owned.Path, "saved-source.dat");
        bool tree = behavior == "tree-with-links", folder = tree || behavior.Contains("directory", StringComparison.Ordinal);
        bool link = behavior is not ("plain-file" or "tree-with-links");
        bool flagged = behavior.StartsWith("selected-", StringComparison.Ordinal);
        void MakeLink(string path, string destination, bool directory)
        {
            try
            {
                if (directory) Directory.CreateSymbolicLink(path, destination);
                else File.CreateSymbolicLink(path, destination);
            }
            catch (UnauthorizedAccessException) { Assert.Skip("Creating an owned native link requires a capability unavailable to this account."); }
        }
        if (tree)
        {
            Directory.CreateDirectory(source); File.WriteAllBytes(Path.Combine(source, "good.dat"), data);
            MakeLink(Path.Combine(source, "file-link"), targetFile, false); MakeLink(Path.Combine(source, "directory-link"), targetDirectory, true);
        }
        else if (behavior is "plain-file" or "swapped-file-link") File.WriteAllBytes(source, data);
        else MakeLink(source, behavior == "selected-dangling-link" ? Path.Combine(owned.Path, "never-created.dat") : folder ? targetDirectory : targetFile, folder);
        var stamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(targetFile, stamp); File.SetLastWriteTimeUtc(Path.Combine(targetDirectory, "target.dat"), stamp);
        if (!link || behavior == "swapped-file-link") File.SetLastWriteTimeUtc(tree ? Path.Combine(source, "good.dat") : source, stamp);
        using var setup = new Setup(owned.Path, sshd);
        if (behavior == "swapped-file-link") setup.Operations.BeforeSourceInfo = path =>
        {
            if (path != source) return false;
            File.Move(source, saved); MakeLink(source, targetFile, false); return true;
        };
        if (replace)
        {
            if (folder)
            {
                setup.MakeDirectory("source"); setup.Write("source/good.dat", prior); setup.Write("source/sentinel.dat", prior);
            }
            else setup.Write("source", prior);
        }
        var expected = setup.Files(); var expectedDirectories = setup.Directories();
        if (!link)
        {
            expected[tree ? "source/good.dat" : "source"] = Hash(data);
            if (tree && !expectedDirectories.Contains("source")) expectedDirectories = expectedDirectories.Append("source").Order(StringComparer.Ordinal).ToArray();
        }
        var item = new ItemRef(Location.FileSystem(owned.Path), "source", folder ? EntryKind.Directory : EntryKind.File) { Flags = flagged ? EntryFlags.Link : EntryFlags.None };
        var job = await setup.Run(new JobRequest
        {
            Kind = moving ? JobKind.Move : JobKind.Copy, Sources = [item], Destination = setup.Destination,
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace, Verify = readBack ? VerifyMode.ReadBack : VerifyMode.Native },
        });
        var actual = setup.Files(); var actualDirectories = setup.Directories();
        string? localLink = new FileInfo(source).LinkTarget ?? new DirectoryInfo(source).LinkTarget;
        bool sourcePresent = File.Exists(source) || Directory.Exists(source) || localLink is not null;
        bool goodPresent = tree && File.Exists(Path.Combine(source, "good.dat"));
        bool accepted = !link, completedRoot = accepted && (!tree || !moving);
        output.WriteLine("UPLOAD_LOCAL_LINK_EVIDENCE " + JsonSerializer.Serialize(new
        {
            length, behavior, moving, replace, readBack, nativeServer, accepted, completedRoot, job.State,
            SourceLinkTarget = localLink, SourcePresent = sourcePresent, GoodSourcePresent = goodPresent, setup.Operations.SourceInfoSwaps,
            SourceTargetSHA256 = Hash(File.ReadAllBytes(targetFile)), DirectoryTargetSHA256 = Hash(File.ReadAllBytes(Path.Combine(targetDirectory, "target.dat"))), OriginalTargetSHA256 = Hash(target),
            OriginalSourceSHA256 = Hash(data), PreviousDestinationSHA256 = Hash(prior), SavedSourceSHA256 = File.Exists(saved) ? Hash(File.ReadAllBytes(saved)) : null,
            ActualFiles = actual, ExpectedFiles = expected, ActualDirectories = actualDirectories, ExpectedDirectories = expectedDirectories,
            job.ItemsTotal, job.ItemsDone, job.ItemsSkipped, job.ItemsFailed, job.BytesTotal, job.BytesDone, job.VerifyBytesTotal, job.VerifyBytesDone,
            CompletedRoots = job.CompletedRootIndices.ToArray(), Issues = job.Issues.Select(i => new { i.Severity, i.Message, i.Outcome }).ToArray(),
            Decisions = setup.Decisions.ToArray(), Server = nativeServer ? "owned user-mode loopback OpenSSH" : "controlled in-memory SFTP",
            ActualNativeLocalLinks = true, ActualRemoteNamespaceReadDirectly = nativeServer, PhysicalDeviceAtomicAliasOrCandidateQualified = false,
        }));
        Assert.Equal(expected, actual); Assert.Equal(expectedDirectories, actualDirectories);
        Assert.Equal(target, File.ReadAllBytes(targetFile)); Assert.Equal(target, File.ReadAllBytes(Path.Combine(targetDirectory, "target.dat")));
        Assert.Equal(completedRoot ? [0] : Array.Empty<int>(), job.CompletedRootIndices);
        Assert.Equal(accepted ? length : 0, job.BytesDone); Assert.Equal(accepted && readBack ? 2L * length : 0, job.VerifyBytesDone);
        Assert.Equal(tree || link ? JobState.CompletedWithIssues : JobState.Completed, job.State);
        Assert.Empty(setup.Decisions);
        if (link)
        {
            Assert.NotNull(localLink); Assert.True(sourcePresent); Assert.Equal(0, job.ItemsDone);
            Assert.Contains(job.Issues, i => i.Message.Contains("Links are not followed", StringComparison.Ordinal));
            if (behavior == "swapped-file-link") { Assert.Equal(1, setup.Operations.SourceInfoSwaps); Assert.Equal(data, File.ReadAllBytes(saved)); }
        }
        else if (tree)
        {
            Assert.True(sourcePresent); Assert.Equal(!moving, goodPresent);
            Assert.NotNull(new FileInfo(Path.Combine(source, "file-link")).LinkTarget); Assert.NotNull(new DirectoryInfo(Path.Combine(source, "directory-link")).LinkTarget);
            Assert.Equal(2, job.ItemsSkipped); if (!moving) Assert.Equal(data, File.ReadAllBytes(Path.Combine(source, "good.dat")));
        }
        else
        {
            Assert.Equal(!moving, sourcePresent); if (!moving) Assert.Equal(data, File.ReadAllBytes(source));
        }
    }

    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)((i * 37 + 19) % 251)).ToArray();
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed class Owned : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("fc-upload-local-link-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
    }
    private sealed class Operations : PortableFileOperations
    {
        public Func<string, bool>? BeforeSourceInfo;
        public int SourceInfoSwaps;
        public override FileSystemItemInfo? TryGetInfo(string path)
        {
            if (BeforeSourceInfo?.Invoke(path) == true) { BeforeSourceInfo = null; SourceInfoSwaps++; }
            return base.TryGetInfo(path);
        }
    }
    private sealed class Interaction(string? fingerprint) : IRemoteInteraction
    {
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) =>
            fingerprint is null || check.Fingerprint == fingerprint ? HostKeyDecision.AcceptOnce : HostKeyDecision.Reject;
        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => fingerprint is null ? new("secret", false) : null;
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;
    }
    private sealed class Setup : IDisposable
    {
        private readonly FakeSftpServer? _fake;
        private readonly string? _native;
        private readonly SftpConnections _connections;
        private readonly JobManager _jobs;
        public Operations Operations { get; } = new();
        public List<string> Decisions { get; } = [];
        public Location Destination { get; }
        public Setup(string state, TestSshd? sshd)
        {
            RemoteProfile profile;
            ISftpConnector connector;
            string? fingerprint = null;
            if (sshd is null)
            {
                _fake = new FakeSftpServer(); _fake.Dir("/up");
                profile = new RemoteProfile { Name = "owned fake", Host = "owned.example", User = "user" };
                connector = new FakeConnector(_fake); Destination = SftpProvider.At(profile, "/up");
            }
            else
            {
                _native = Directory.CreateDirectory(Path.Combine(sshd.Files, "up")).FullName;
                profile = sshd.Profile; connector = new SshNetConnector(); Destination = SftpProvider.At(profile, _native);
                fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(sshd.HostKey)).TrimEnd('=');
            }
            _connections = new SftpConnections(id => id == profile.Id ? profile : null, connector, new HostKeyTrust(Path.Combine(state, "known_hosts"), []), new SessionSecretStore(), new Interaction(fingerprint));
            var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider()); providers.Register(new SftpProvider(_connections, () => [profile], p => p));
            SftpJobs.Register(); _jobs = new JobManager(Operations, providers, Path.Combine(state, "journal"));
        }
        public void MakeDirectory(string relative)
        {
            if (_fake is not null) _fake.Dir("/up/" + relative);
            else Directory.CreateDirectory(Path.Combine(_native!, relative));
        }
        public void Write(string relative, byte[] data)
        {
            if (_fake is not null) _fake.File("/up/" + relative, "").Data = data;
            else { string p = Path.Combine(_native!, relative); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllBytes(p, data); }
        }
        public SortedDictionary<string, string> Files()
        {
            var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (_native is not null)
            {
                foreach (string p in Directory.EnumerateFiles(_native, "*", SearchOption.AllDirectories)) result[Path.GetRelativePath(_native, p).Replace('\\', '/')] = Hash(File.ReadAllBytes(p));
            }
            else
            {
                void Walk(FakeSftpServer.Node node, string prefix)
                {
                    foreach (var (name, child) in node.Children)
                    {
                        string p = prefix + name;
                        if (child.IsDirectory) Walk(child, p + "/"); else result[p] = Hash(child.Data);
                    }
                }
                Walk(_fake!.Lookup("/up", true)!, "");
            }
            return result;
        }
        public string[] Directories()
        {
            var result = new List<string> { "." };
            if (_native is not null) result.AddRange(Directory.EnumerateDirectories(_native, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(_native, p).Replace('\\', '/')));
            else
            {
                void Walk(FakeSftpServer.Node node, string prefix)
                {
                    foreach (var (name, child) in node.Children.Where(v => v.Value.IsDirectory)) { string p = prefix + name; result.Add(p); Walk(child, p + "/"); }
                }
                Walk(_fake!.Lookup("/up", true)!, "");
            }
            return result.Order(StringComparer.Ordinal).ToArray();
        }
        public async Task<Job> Run(JobRequest request)
        {
            var job = _jobs.Submit(request); var deadline = Stopwatch.StartNew();
            while (!job.State.IsFinished())
            {
                if (job.Decision is { Task.IsCompleted: false } decision) { Decisions.Add(decision.Request.Message); decision.Resolve(new Decision(DecisionAction.Skip)); }
                if (deadline.Elapsed > TimeSpan.FromSeconds(40)) { job.Cancel(); throw new TimeoutException("Owned local-link upload did not finish."); }
                await Task.Delay(5, TestContext.Current.CancellationToken);
            }
            return job;
        }
        public void Dispose() => _connections.Dispose();
    }
}
