using System.Text.Json;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class RemoteJobRetirementFailureTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, string, string>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
                foreach (string retirement in new[] { "idle", "broken", "owner-closed" })
                    foreach (string primary in new[] { "none", "io", "denied", "cancelled" })
                        foreach (string secondary in new[] { "none", "io", "denied", "disposed" })
                            cases.Add(protocol, retirement, primary, secondary);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Job_failure_survives_retiring_its_connection(string protocol, string retirement, string primary, string secondary)
    {
        string state = Directory.CreateTempSubdirectory("filecat-owned-job-retirement-").FullName;
        var server = new FakeSftpServer();
        server.File("/one.dat", "one");
        var profile = new RemoteProfile { Name = "Owned job retirement", Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
        var connector = new FaultConnector(server) { Secondary = Failure(secondary, "owned secondary job-channel close") };
        var interaction = new ScriptedInteraction();
        interaction.Secrets.Enqueue("secret");
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, connector,
            new HostKeyTrust(Path.Combine(state, "known_hosts")), new SessionSecretStore(), interaction);
        var provider = new SftpProvider(connections, () => [profile], p => p);
        Exception? original = Failure(primary, "owned original remote-job work failure");
        var job = new Job(new JobRequest { Kind = JobKind.Copy, Sources = [], Destination = SftpProvider.At(profile, "/") }, "Owned retirement control", "owned", [], []);
        string? journalPath = null;
        try
        {
            Exception? observed;
            byte[] actualBytes;
            using (var journal = JobJournal.Create(state, job))
            {
                journalPath = journal.Path;
                var executor = new ControlExecutor(job, journal, provider, connections, connector, retirement, original);
                observed = Record.Exception(executor.Execute);
                actualBytes = executor.ActualBytes;
            }
            int closes = connector.Channels[0].Closes;
            int active = server.ActiveChannels;
            bool closed = retirement != "idle";
            Exception? expected = original ?? (closed ? connector.Secondary : null);
            bool primaryRetained = ReferenceEquals(observed, expected);
            int capacity = profile.IsFtp ? SftpConnections.MaxPerFtpServer : SftpConnections.MaxPerServer;
            int acquired = 0;
            connector.Secondary = null;
            if (retirement != "owner-closed")
            {
                var leases = new List<SftpLease>();
                using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                bound.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    for (int i = 0; i < capacity; i++) leases.Add(connections.Lease(profile.Id, bound.Token));
                    acquired = leases.Count;
                }
                finally { foreach (var lease in leases) lease.Dispose(); }
            }
            int connectsBeforeClosedAdmission = connector.Channels.Count;
            Exception? admission = retirement == "owner-closed"
                ? Record.Exception(() => connections.Lease(profile.Id, TestContext.Current.CancellationToken)) : null;
            int connectsAfterClosedAdmission = connector.Channels.Count;
            using var read = new MemoryStream();
            using (var recovered = new FakeConnector(server).Connect(profile, new ConnectContext
                { GetSecret = _ => "secret", ApproveHostKey = _ => true, AnswerPrompts = (_, _) => [] }, TestContext.Current.CancellationToken))
            using (var source = recovered.OpenRead("/one.dat")) source.CopyTo(read);
            byte[] recoveredBytes = read.ToArray();
            output.WriteLine(JsonSerializer.Serialize(new
            {
                Protocol = protocol, Retirement = retirement, Primary = primary, Secondary = secondary,
                OriginalType = original?.GetType().Name, OriginalMessage = original?.Message, OriginalStack = original?.StackTrace,
                ObservedType = observed?.GetType().Name, ObservedMessage = observed?.Message,
                ExpectedType = expected?.GetType().Name, OriginalWorkOrStandaloneCloseRetained = primaryRetained,
                ActualInputBytesBase64 = Convert.ToBase64String(actualBytes), RecoveredBytesBase64 = Convert.ToBase64String(recoveredBytes),
                ActualChannelCloseCount = closes, ActiveChannelsBeforeRecovery = active,
                CapacityExpected = capacity, CapacityAcquired = acquired,
                ClosedOwnerAdmissionType = admission?.GetType().Name, ConnectsBeforeClosedAdmission = connectsBeforeClosedAdmission,
                ConnectsAfterClosedAdmission = connectsAfterClosedAdmission, ActualJournalBytes = new FileInfo(journalPath).Length,
                JournalPath = journalPath, NoNativeProtocolOrServerFaultIncidenceClaim = true,
            }));
            Assert.True(primaryRetained, "A secondary job-channel close must not replace the original work exception object.");
            Assert.Equal(new byte[] { 111, 110, 101 }, actualBytes);
            Assert.Equal(actualBytes, recoveredBytes);
            Assert.Equal(closed ? 1 : 0, closes);
            Assert.Equal(closed ? 0 : 1, active);
            if (retirement == "owner-closed")
            {
                Assert.IsType<ObjectDisposedException>(admission);
                Assert.Equal(connectsBeforeClosedAdmission, connectsAfterClosedAdmission);
            }
            else Assert.Equal(capacity, acquired);
        }
        finally
        {
            connector.Secondary = null;
            connections.Dispose();
            foreach (var channel in connector.Channels) channel.Cleanup();
            Assert.Equal(0, server.ActiveChannels);
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(state));
            Directory.Delete(state, recursive: true);
            Assert.False(Directory.Exists(state));
        }
    }

    private static Exception? Failure(string kind, string message) => kind switch
    {
        "io" => new IOException(message), "denied" => new UnauthorizedAccessException(message),
        "cancelled" => new OperationCanceledException(message), "disposed" => new ObjectDisposedException(message), _ => null,
    };

    private sealed class ControlExecutor(Job job, JobJournal journal, SftpProvider provider, SftpConnections connections,
        FaultConnector connector, string retirement, Exception? primary) : SftpExecutorBase(job, new PortableFileOperations(), journal, provider)
    {
        public byte[] ActualBytes { get; private set; } = [];
        protected override Location ConnectionLocation => job.Request.Destination!;
        protected override void Run()
        {
            var channel = Channel;
            using (var source = channel.OpenRead("/one.dat"))
            using (var bytes = new MemoryStream()) { source.CopyTo(bytes); ActualBytes = bytes.ToArray(); }
            if (retirement == "broken") connector.Channels[0].Broken = true;
            else if (retirement == "owner-closed") connections.Dispose();
            if (primary is not null) throw primary;
        }
    }

    private sealed class FaultConnector(FakeSftpServer server) : ISftpConnector
    {
        public Exception? Secondary { get; set; }
        public List<FaultChannel> Channels { get; } = [];
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = new FaultChannel(this, new FakeConnector(server).Connect(profile, context, ct));
            Channels.Add(channel); return channel;
        }
    }

    private sealed class FaultChannel(FaultConnector owner, ISftpChannel inner) : ISftpChannel
    {
        public bool Broken { get; set; }
        public int Closes { get; private set; }
        public bool IsConnected => !Broken && inner.IsConnected;
        public string HomeDirectory => inner.HomeDirectory;
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => inner.List(path, ct);
        public RemoteStat? Stat(string path) => inner.Stat(path);
        public Stream OpenRead(string path) => inner.OpenRead(path);
        public Stream CreateNew(string path) => inner.CreateNew(path);
        public Stream OpenWriteAt(string path, long offset) => inner.OpenWriteAt(path, offset);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void Rename(string source, string target) => inner.Rename(source, target);
        public bool TryReplace(string source, string target) => inner.TryReplace(source, target);
        public void SetModified(string path, DateTime utc) => inner.SetModified(path, utc);
        public void Dispose() { Closes++; inner.Dispose(); if (owner.Secondary is { } failure) throw failure; }
        public void Cleanup() => inner.Dispose();
    }
}
