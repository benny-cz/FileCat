using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>Controlled close faults with real owned file holders; no native server-fault incidence claim.</summary>
public sealed class RemotePoolRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string> Cases
    {
        get
        {
            var rows = new TheoryData<string, string, string>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
            foreach (string operation in new[] { "idle", "disconnect", "stale-admission", "job-retry" })
            foreach (string fault in new[] { "none", "io", "denied", "disposed" }) rows.Add(protocol, operation, fault);
            return rows;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Pool_retirement_drains_every_channel_and_retry_can_replace_a_broken_one(string protocol, string operation, string fault)
    {
        using var rig = new Rig(protocol);
        using var unrelated = rig.Connections.Lease(rig.Other.Id, TestContext.Current.CancellationToken);
        byte[] input = "owned remote bytes\n"u8.ToArray();
        var retired = new List<FaultChannel>();
        Exception? observed = null;
        Exception? expected = null;
        byte[] recovered = [];
        int decisions = 0, attempts = 0;
        if (operation == "job-retry")
        {
            var job = new Job(new JobRequest { Kind = JobKind.Copy, Destination = SftpProvider.At(rig.Profile, "/"), Sources = [] }, "Owned retry", "owned", [], []);
            using var journal = JobJournal.Create(rig.Root, job);
            var executor = new RetryExecutor(job, journal, rig.Provider, rig.Connector, fault);
            var work = Task.Run(() => Record.Exception(executor.Execute), TestContext.Current.CancellationToken);
            var clock = Stopwatch.StartNew();
            try
            {
                while (!work.IsCompleted)
                {
                    if (job.Decision is { Task.IsCompleted: false } decision)
                    {
                        decisions++;
                        decision.Resolve(new Decision(DecisionAction.Retry));
                    }
                    if (clock.Elapsed > TimeSpan.FromSeconds(10)) { job.Cancel(); throw new TimeoutException("Owned remote retry did not finish."); }
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                }
                observed = await work;
            }
            finally { job.Cancel(); await work; }
            retired.Add(executor.Original!);
            recovered = executor.Recovered;
            attempts = executor.Attempts;
        }
        else
        {
            var leases = new List<SftpLease>();
            for (int i = 0; i < rig.Capacity; i++) leases.Add(rig.Connections.Lease(rig.Profile.Id, TestContext.Current.CancellationToken));
            retired.AddRange(leases.Select(l => (FaultChannel)l.Channel));
            foreach (var channel in retired) channel.Failure = Failure(fault, channel.HolderPath);
            foreach (var lease in leases) lease.Dispose();
            if (operation == "idle") observed = Record.Exception(() => rig.Connections.CloseIdle(DateTime.UtcNow + SftpConnections.IdleTimeout + TimeSpan.FromSeconds(1)));
            else if (operation == "disconnect")
            {
                expected = retired[^1].Failure; // Returned in order; idle stack drains last returned first.
                observed = Record.Exception(() => rig.Connections.Disconnect(rig.Profile.Id));
            }
            else
            {
                foreach (var channel in retired) channel.Broken = true;
                observed = Record.Exception(() => { using var lease = rig.Connections.Lease(rig.Profile.Id, TestContext.Current.CancellationToken); recovered = Read(lease.Channel); });
            }
        }
        int[] closeCounts = retired.Select(c => c.Closes).ToArray();
        bool[] unlocked = retired.Select(c => CanOpenExclusively(c.HolderPath)).ToArray();
        int beforeRepeated = retired.Sum(c => c.Closes);
        Exception? repeated = operation == "disconnect" ? Record.Exception(() => rig.Connections.Disconnect(rig.Profile.Id)) : null;
        if (operation == "idle") repeated = Record.Exception(() => rig.Connections.CloseIdle(DateTime.UtcNow + SftpConnections.IdleTimeout + TimeSpan.FromSeconds(1)));
        int afterRepeated = retired.Sum(c => c.Closes);
        // Cleanup faults are disabled only after the primary observations, before checking subsequent usable capacity.
        foreach (var channel in rig.Connector.Channels) channel.Failure = null;
        int acquired = 0;
        var capacityLeases = new List<SftpLease>();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            for (int i = 0; i < rig.Capacity; i++) capacityLeases.Add(rig.Connections.Lease(rig.Profile.Id, bound.Token));
            acquired = capacityLeases.Count;
            if (recovered.Length == 0) recovered = Read(capacityLeases[0].Channel);
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { }
        finally { foreach (var lease in capacityLeases) lease.Dispose(); }
        var rows = rig.Connector.Channels.Select(c => new { c.ProfileId, c.HolderPath, c.Closes, c.Broken, InputSHA256 = Hash(File.ReadAllBytes(c.HolderPath)) }).ToArray();
        output.WriteLine("REMOTE_POOL_RETIREMENT " + JsonSerializer.Serialize(new
        {
            protocol, operation, fault, ObservedType = observed?.GetType().Name, ObservedMessage = observed?.Message,
            ObservedStack = observed?.StackTrace, ExpectedType = expected?.GetType().Name,
            OriginalStandaloneFailureRetained = ReferenceEquals(observed, expected), closeCounts, unlocked,
            RepeatedType = repeated?.GetType().Name, beforeRepeated, afterRepeated, acquired, rig.Capacity,
            RecoveredSHA256 = Hash(recovered), ExpectedSHA256 = Hash(input), decisions, attempts,
            UnrelatedCloses = ((FaultChannel)unrelated.Channel).Closes, UnrelatedBytesSHA256 = Hash(Read(unrelated.Channel)),
            ActualOwnedNativeHolders = rows, ControlledChannelFaults = true, NativeServerIncidenceOrCandidateQualified = false,
        }));
        Assert.Same(expected, observed);
        Assert.All(closeCounts, count => Assert.Equal(1, count));
        Assert.All(unlocked, Assert.True);
        Assert.Null(repeated); Assert.Equal(beforeRepeated, afterRepeated);
        Assert.Equal(rig.Capacity, acquired); Assert.Equal(input, recovered);
        Assert.Equal(0, ((FaultChannel)unrelated.Channel).Closes); Assert.Equal(input, Read(unrelated.Channel));
        if (operation == "job-retry") { Assert.Equal(1, decisions); Assert.Equal(2, attempts); }
    }

    private static Exception? Failure(string kind, string path) => kind switch
    {
        "io" => new IOException("Owned close: " + path), "denied" => new UnauthorizedAccessException("Owned close: " + path),
        "disposed" => new ObjectDisposedException(path), _ => null,
    };
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static byte[] Read(ISftpChannel channel) { using var stream = channel.OpenRead("/one.dat"); using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray(); }
    private static bool CanOpenExclusively(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private sealed class RetryExecutor(Job job, JobJournal journal, SftpProvider provider, Connector connector, string fault)
        : SftpExecutorBase(job, new PortableFileOperations(), journal, provider)
    {
        public FaultChannel? Original;
        public byte[] Recovered = [];
        public int Attempts;
        protected override Location ConnectionLocation => job.Request.Destination!;
        protected override void Run()
        {
            Remote("/one.dat", "read the owned file", () =>
            {
                Attempts++;
                var channel = (FaultChannel)Channel;
                byte[] bytes = Read(channel);
                if (Attempts == 1)
                {
                    Original = channel; channel.Failure = Failure(fault, channel.HolderPath); channel.Broken = true;
                    throw new RemoteDisconnectedException("Owned connection drop after exact read.");
                }
                Recovered = bytes;
            });
        }
    }
    private sealed class Rig : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("fc-pool-retirement-").FullName;
        public RemoteProfile Profile { get; }
        public RemoteProfile Other { get; }
        public Connector Connector { get; }
        public SftpConnections Connections { get; }
        public SftpProvider Provider { get; }
        public int Capacity => Profile.IsFtp ? SftpConnections.MaxPerFtpServer : SftpConnections.MaxPerServer;
        public Rig(string protocol)
        {
            Profile = new RemoteProfile { Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            Other = new RemoteProfile { Host = "other.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            var server = new FakeSftpServer(); server.File("/one.dat", "owned remote bytes\n");
            Connector = new Connector(server, Root);
            var ui = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce }; ui.Secrets.Enqueue("secret"); ui.Secrets.Enqueue("secret");
            Connections = new SftpConnections(id => id == Profile.Id ? Profile : id == Other.Id ? Other : null, Connector,
                new HostKeyTrust(Path.Combine(Root, "known_hosts")), new SessionSecretStore(), ui);
            Provider = new SftpProvider(Connections, () => [Profile, Other], p => p);
        }
        public void Dispose()
        {
            foreach (var channel in Connector.Channels) channel.Failure = null;
            Connections.Dispose();
            foreach (var channel in Connector.Channels) channel.Cleanup();
            Assert.All(Connector.Channels, c => Assert.True(CanOpenExclusively(c.HolderPath)));
            Directory.Delete(Root, recursive: true); Assert.False(Directory.Exists(Root));
        }
    }
    private sealed class Connector(FakeSftpServer server, string root) : ISftpConnector
    {
        public List<FaultChannel> Channels { get; } = [];
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = new FaultChannel(new FakeConnector(server).Connect(profile, context, ct), profile.Id, Path.Combine(root, "holder-" + Channels.Count + ".dat"));
            Channels.Add(channel); return channel;
        }
    }
    private sealed class FaultChannel : ISftpChannel
    {
        private readonly ISftpChannel _inner;
        private readonly FileStream _holder;
        public string ProfileId { get; }
        public string HolderPath { get; }
        public Exception? Failure;
        public bool Broken;
        public int Closes;
        public FaultChannel(ISftpChannel inner, string profileId, string path)
        {
            _inner = inner; ProfileId = profileId; HolderPath = path;
            File.WriteAllBytes(path, "owned held bytes\n"u8.ToArray());
            _holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        public bool IsConnected => !Broken && _inner.IsConnected;
        public string HomeDirectory => _inner.HomeDirectory;
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => _inner.List(path, ct);
        public RemoteStat? Stat(string path) => _inner.Stat(path);
        public Stream OpenRead(string path) => _inner.OpenRead(path);
        public Stream CreateNew(string path) => _inner.CreateNew(path);
        public Stream OpenWriteAt(string path, long offset) => _inner.OpenWriteAt(path, offset);
        public void CreateDirectory(string path) => _inner.CreateDirectory(path);
        public void Rename(string source, string target) => _inner.Rename(source, target);
        public bool TryReplace(string source, string target) => _inner.TryReplace(source, target);
        public void SetModified(string path, DateTime utc) => _inner.SetModified(path, utc);
        public void Dispose() { Closes++; Cleanup(); if (Failure is { } failure) throw failure; }
        public void Cleanup() { _holder.Dispose(); _inner.Dispose(); }
    }
}
