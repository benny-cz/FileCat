using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>Expected edit revisions are checked again after the owned upload and before target mutation.</summary>
public sealed class RemoteEditTargetPublicationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(RemoteProtocols.Sftp, "unchanged", false)]
    [InlineData(RemoteProtocols.Ftp, "unchanged", true)]
    [InlineData(RemoteProtocols.Sftp, "length", true)]
    [InlineData(RemoteProtocols.Sftp, "same-size-time", true)]
    [InlineData(RemoteProtocols.Ftp, "same-size-time", false)]
    [InlineData(RemoteProtocols.Ftp, "time", false)]
    [InlineData(RemoteProtocols.Sftp, "gone", false)]
    [InlineData(RemoteProtocols.Ftp, "link", true)]
    [InlineData(RemoteProtocols.Sftp, "directory", true)]
    public async Task Approved_replacement_does_not_overwrite_a_target_changed_during_upload(string protocol, string change, bool move)
    {
        using var rig = new Rig(protocol, atomic: false);
        var server = rig.Server;
        server.Dir("/owned");
        server.File("/sentinel", "link target stays");
        server.File(Target, "base");
        Snapshot? during = null;
        server.DuringUpload = () =>
        {
            switch (change)
            {
                case "length": server.Lookup(Target, false)!.Data = "someone else's longer bytes"u8.ToArray(); break;
                case "same-size-time": server.Lookup(Target, false)!.Data = "evil"u8.ToArray(); break;
                case "time": server.Lookup(Target, false)!.Modified = server.Lookup(Target, false)!.Modified.AddSeconds(3); break;
                case "gone": server.Dir("/owned").Children.Remove("notes.txt"); break;
                case "link": server.Link(Target, "/sentinel"); break;
                case "directory": server.Dir("/owned").Children.Remove("notes.txt"); server.Dir(Target); break;
            }
            during = rig.Snapshot();
        };
        var sourceHash = Hash(File.ReadAllBytes(rig.SourcePath));
        var job = rig.Jobs.Submit(new JobRequest
        {
            Kind = move ? JobKind.Move : JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(rig.SourcePath, EntryKind.File)],
            Destination = SftpProvider.At(rig.Profile, "/owned"), NewName = "notes.txt",
            Options = new TransferOptions { Conflicts = ConflictPolicy.Replace },
        });
        rig.Jobs.MaxConcurrent = 4; rig.Jobs.Schedule();
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || rig.Jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.NotNull(during);
        Assert.Equal("link target stays", server.Read("/sentinel"));
        Assert.DoesNotContain(server.Dir("/owned").Children.Keys, n => n.StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
        if (change == "unchanged")
        {
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(sourceHash, rig.Snapshot().SHA256);
            Assert.Equal(!move, File.Exists(rig.SourcePath));
        }
        else
        {
            Assert.Equal(JobState.Failed, job.State);
            Assert.Equal(during, rig.Snapshot());
            Assert.Equal(sourceHash, Hash(File.ReadAllBytes(rig.SourcePath)));
            Assert.Equal(0, job.BytesDone);
            Assert.Contains(job.Issues, i => i.Message.Contains(
                change == "same-size-time" ? "destination content changed on the server" : "destination changed on the server",
                StringComparison.Ordinal));
        }
    }

    public static TheoryData<string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
                foreach (string change in new[] { "unchanged", "length", "time", "same-size-time", "gone", "link", "directory", "appeared" })
                    foreach (bool atomic in new[] { false, true }) cases.Add(protocol, change, atomic);
            return cases;
        }
    }

    [Theory]
    [InlineData(RemoteProtocols.Sftp)]
    [InlineData(RemoteProtocols.Ftp)]
    public async Task Edit_refuses_same_metadata_content_change_before_upload(string protocol)
    {
        using var rig = new Rig(protocol, atomic: false);
        var server = rig.Server;
        server.Dir("/owned");
        server.File(Target, "base");
        var expected = new ContentRevision(4, server.Lookup(Target, false)!.Modified.Ticks);
        server.Lookup(Target, false)!.Data = "evil"u8.ToArray();
        Snapshot changed = rig.Snapshot();
        string sourceHash = Hash(File.ReadAllBytes(rig.SourcePath));
        var job = rig.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(rig.SourcePath, EntryKind.File)],
            Destination = SftpProvider.At(rig.Profile, "/owned"), NewName = "notes.txt",
            ExpectedTarget = expected, ExpectedTargetSha256 = Hash("base"u8.ToArray()),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Skip },
        });
        rig.Jobs.MaxConcurrent = 4; rig.Jobs.Schedule();
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || rig.Jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(changed, rig.Snapshot());
        Assert.Equal(sourceHash, Hash(File.ReadAllBytes(rig.SourcePath)));
        Assert.Equal(0, job.BytesDone);
        Assert.DoesNotContain(rig.Calls, c => c.Operation is "CreateNew" or "Rename" or "TryReplace" or "Delete");
        Assert.Contains(job.Issues, i => i.Message.Contains("content changed", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Edit_target_is_rechecked_after_upload_before_publication(string protocol, string change, bool atomic)
    {
        using var rig = new Rig(protocol, atomic);
        var server = rig.Server;
        server.Dir("/owned");
        server.File("/sentinel", "owned link target stays");
        if (change != "appeared") server.File(Target, "base");
        Snapshot initial = rig.Snapshot();
        ContentRevision? expected = change == "appeared" ? null : new(4, server.Lookup(Target, false)!.Modified.Ticks);
        Snapshot? during = null;
        int mutationAt = -1;
        server.DuringUpload = () =>
        {
            switch (change)
            {
                case "length": server.Lookup(Target, false)!.Data = "theirs, longer"u8.ToArray(); break;
                case "same-size-time": server.Lookup(Target, false)!.Data = "evil"u8.ToArray(); break;
                case "time": server.Lookup(Target, false)!.Modified = server.Lookup(Target, false)!.Modified.AddSeconds(1); break;
                case "gone": server.Dir("/owned").Children.Remove("notes.txt"); break;
                case "link": server.Link(Target, "/sentinel"); break;
                case "directory": server.Dir("/owned").Children.Remove("notes.txt"); server.Dir(Target); break;
                case "appeared": server.File(Target, "late arrival"); break;
            }
            during = rig.Snapshot();
            mutationAt = rig.Calls.Count;
        };
        string sourceHash = Hash(File.ReadAllBytes(rig.SourcePath));
        var job = rig.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(rig.SourcePath, EntryKind.File)],
            Destination = SftpProvider.At(rig.Profile, "/owned"), NewName = "notes.txt", ExpectedTarget = expected,
            ExpectedTargetSha256 = expected is null ? null : Hash("base"u8.ToArray()),
            Options = new TransferOptions { Conflicts = ConflictPolicy.Skip },
        });
        rig.Jobs.MaxConcurrent = 4; rig.Jobs.Schedule();
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || rig.Jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Snapshot after = rig.Snapshot();
        string[] names = server.Dir("/owned").Children.Keys.ToArray();
        var calls = rig.Calls.ToArray();
        var targetCalls = calls.Where(c => c.Path == Target && c.Operation is "Rename" or "TryReplace" or "Delete" or "MoveTo").ToArray();
        string sourceAfter = Hash(File.ReadAllBytes(rig.SourcePath));
        string sentinel = server.Read("/sentinel");
        output.WriteLine("EDIT_TARGET_PUBLICATION " + JsonSerializer.Serialize(new
        {
            protocol, change, atomic, Initial = initial, Expected = expected, During = during, After = after,
            MutationAt = mutationAt, Calls = calls, TargetCalls = targetCalls, Names = names,
            State = job.State.ToString(), job.BytesDone, rig.Decisions,
            Issues = job.Issues.Select(i => new { i.Message, Outcome = i.Outcome.ToString() }).ToArray(),
            SourceBefore = sourceHash, SourceAfter = sourceAfter, rig.SourcePath, rig.Root,
            Sentinel = sentinel, ActualOwnedWorkingFileAndUploadExecutor = true, ControlledInMemoryChannel = true,
            NativeServerOrAtomicCASOrPostCheckRaceQualified = false,
        }));
        Assert.NotNull(during);
        Assert.Equal(sourceHash, sourceAfter);
        Assert.Equal("owned link target stays", sentinel);
        Assert.Equal(change == "gone" ? [] : new[] { "notes.txt" }, names);
        Assert.Contains(calls.Skip(mutationAt), c => c.Operation == "List" && c.Path == "/owned");
        if (change == "unchanged")
        {
            Assert.Equal(JobState.Completed, job.State);
            Assert.Equal(sourceHash, after.SHA256);
            Assert.Equal(new FileInfo(rig.SourcePath).Length, job.BytesDone);
            Assert.Equal(0, rig.Decisions);
        }
        else
        {
            Assert.Equal(during, after);
            Assert.Equal(JobState.Failed, job.State);
            Assert.Equal(0, job.BytesDone);
            Assert.Equal(change == "appeared" ? 1 : 0, rig.Decisions);
            if (change == "appeared") Assert.Single(targetCalls, c => c.Operation == "Rename");
            else
            {
                Assert.Empty(targetCalls);
                Assert.Contains(job.Issues, i => i.Message.Contains("Not written:", StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData(RemoteProtocols.Sftp, "same-size-time", false)]
    [InlineData(RemoteProtocols.Ftp, "same-size-time", false)]
    [InlineData(RemoteProtocols.FtpExplicitTls, "metadata", false)]
    [InlineData(RemoteProtocols.FtpImplicitTls, "gone", false)]
    [InlineData(RemoteProtocols.Sftp, "same-size-time", true)]
    [InlineData(RemoteProtocols.Ftp, "gone", true)]
    public async Task Non_atomic_fallback_does_not_delete_a_target_changed_after_final_check(string protocol, string change, bool edit)
    {
        using var rig = new Rig(protocol, atomic: false);
        var server = rig.Server;
        server.Dir("/owned");
        server.File(Target, "base");
        var expected = new ContentRevision(4, server.Lookup(Target, false)!.Modified.Ticks);
        string sourceHash = Hash(File.ReadAllBytes(rig.SourcePath));
        Snapshot? changed = null;
        rig.BeforeTryReplace = () =>
        {
            var target = server.Lookup(Target, false)!;
            switch (change)
            {
                case "same-size-time": target.Data = "evil"u8.ToArray(); break;
                case "metadata": target.Modified = target.Modified.AddSeconds(3); break;
                case "gone": server.Dir("/owned").Children.Remove("notes.txt"); break;
            }
            changed = rig.Snapshot();
        };
        var job = rig.Jobs.Submit(new JobRequest
        {
            Kind = JobKind.Move,
            Sources = [ItemRef.ForFileSystemPath(rig.SourcePath, EntryKind.File)],
            Destination = SftpProvider.At(rig.Profile, "/owned"), NewName = "notes.txt",
            ExpectedTarget = edit ? expected : null,
            ExpectedTargetSha256 = edit ? Hash("base"u8.ToArray()) : null,
            Options = new TransferOptions { Conflicts = edit ? ConflictPolicy.Skip : ConflictPolicy.Replace },
        });
        rig.Jobs.MaxConcurrent = 4; rig.Jobs.Schedule();
        var clock = Stopwatch.StartNew();
        while (!job.State.IsFinished() || rig.Jobs.HasActiveWork)
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(20));
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.NotNull(changed);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(changed, rig.Snapshot());
        Assert.Equal(sourceHash, Hash(File.ReadAllBytes(rig.SourcePath)));
        Assert.Equal(0, job.BytesDone);
        Assert.DoesNotContain(server.Dir("/owned").Children.Keys,
            name => name.StartsWith(JournalRecovery.StagedPrefix, StringComparison.Ordinal));
        Assert.Contains(rig.Calls, call => call.Operation == "TryReplace" && call.Path == Target);
        Assert.DoesNotContain(rig.Calls, call => call.Operation == "Delete" && call.Path == Target);
        Assert.Contains(job.Issues, issue => issue.Message.Contains("Not written:", StringComparison.Ordinal));
    }

    private const string Target = "/owned/notes.txt";
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private sealed record Snapshot(string Kind, long Length, long ModifiedTicks, string? SHA256, string? Link);
    private sealed record Call(string Operation, string Path, string? Source = null);
    private sealed class Rig : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("fc-edit-target-").FullName;
        public string SourcePath { get; }
        public FakeSftpServer Server { get; } = new();
        public RemoteProfile Profile { get; }
        public JobManager Jobs { get; }
        public ConcurrentQueue<Call> Calls { get; } = new();
        public Action? BeforeTryReplace;
        public int Decisions;
        private readonly SftpConnections connections;
        public Rig(string protocol, bool atomic)
        {
            SourcePath = Path.Join(Root, "working.txt"); File.WriteAllBytes(SourcePath, "mine edited bytes"u8.ToArray());
            Server.PosixRename = atomic;
            Profile = new() { Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            var interaction = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce };
            for (int i = 0; i < 10; i++) interaction.Secrets.Enqueue("secret");
            connections = new SftpConnections(id => id == Profile.Id ? Profile : null, new Connector(Server, Calls, () => BeforeTryReplace?.Invoke()),
                new HostKeyTrust(Path.Join(Root, "known_hosts"), []), new SessionSecretStore(), interaction);
            var providers = new ProviderRegistry(); providers.Register(new LocalFileSystemProvider());
            providers.Register(new SftpProvider(connections, () => [Profile], p => p)); SftpJobs.Register();
            Jobs = new JobManager(new PortableFileOperations(), providers, Path.Join(Root, "journal")) { MaxConcurrent = 0 };
            Jobs.DecisionRequested += d => { Interlocked.Increment(ref Decisions); d.Resolve(new Decision(DecisionAction.Skip)); };
        }
        public Snapshot Snapshot()
        {
            var node = Server.Lookup(Target, false);
            return node is null ? new("missing", 0, 0, null, null)
                : new(node.LinkTarget is not null ? "link" : node.IsDirectory ? "directory" : "file",
                    node.Data.LongLength, node.Modified.Ticks, Hash(node.Data), node.LinkTarget);
        }
        public void Dispose()
        {
            foreach (var job in Jobs.Jobs) job.Cancel();
            Assert.True(SpinWait.SpinUntil(() => !Jobs.HasActiveWork, TimeSpan.FromSeconds(3)));
            connections.Dispose(); Assert.Equal(0, Server.ActiveChannels);
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(Root));
            Directory.Delete(Root, recursive: true); Assert.False(Directory.Exists(Root));
        }
    }
    private sealed class Connector(FakeSftpServer server, ConcurrentQueue<Call> calls, Action beforeTryReplace) : ISftpConnector
    {
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
            => new Channel(new FakeConnector(server).Connect(profile, context, ct), calls, beforeTryReplace);
    }
    private sealed class Channel(ISftpChannel inner, ConcurrentQueue<Call> calls, Action beforeTryReplace) : ISftpChannel
    {
        public bool IsConnected => inner.IsConnected;
        public string HomeDirectory => inner.HomeDirectory;
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct)
        {
            calls.Enqueue(new("List", path));
            return inner.List(path, ct).Select(e => (IRemoteEntry)new Entry(e, calls)).ToArray();
        }
        public RemoteStat? Stat(string path) { calls.Enqueue(new("Stat", path)); return inner.Stat(path); }
        public Stream OpenRead(string path) { calls.Enqueue(new("OpenRead", path)); return inner.OpenRead(path); }
        public Stream CreateNew(string path) { calls.Enqueue(new("CreateNew", path)); return inner.CreateNew(path); }
        public Stream OpenWriteAt(string path, long offset) { calls.Enqueue(new("OpenWriteAt", path)); return inner.OpenWriteAt(path, offset); }
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void Rename(string source, string target) { calls.Enqueue(new("Rename", target, source)); inner.Rename(source, target); }
        public bool TryReplace(string source, string target)
        {
            calls.Enqueue(new("TryReplace", target, source));
            beforeTryReplace();
            return inner.TryReplace(source, target);
        }
        public void SetModified(string path, DateTime utc) => inner.SetModified(path, utc);
        public void Dispose() => inner.Dispose();
    }
    private sealed class Entry(IRemoteEntry inner, ConcurrentQueue<Call> calls) : IRemoteEntry
    {
        public string Name => inner.Name;
        public string FullPath => inner.FullPath;
        public bool IsDirectory => inner.IsDirectory;
        public bool IsLink => inner.IsLink;
        public long Size => inner.Size;
        public DateTime ModifiedUtc => inner.ModifiedUtc;
        public void Delete() { calls.Enqueue(new("Delete", FullPath)); inner.Delete(); }
        public void MoveTo(string path) { calls.Enqueue(new("MoveTo", path, FullPath)); inner.MoveTo(path); }
    }
}
