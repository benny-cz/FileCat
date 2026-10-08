using System.Text.Json;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class RemoteOpenFailureTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string, string> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, string, string>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
            {
                foreach (string stage in new[] { "stat", "read" })
                    foreach (string primary in new[] { "disconnected", "io", "denied" })
                        foreach (string secondary in new[] { "none", "io", "denied", "disposed" })
                            cases.Add(protocol, stage, primary, secondary);
                cases.Add(protocol, "healthy", "none", "none");
                cases.Add(protocol, "healthy", "none", "io");
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Opening_retains_the_original_failure_when_retiring_its_channel(string protocol, string stage, string primary, string secondary)
    {
        using var rig = new Rig(protocol);
        rig.Connector.Stage = stage;
        rig.Connector.Primary = Failure(primary, "owned original " + stage + " failure");
        rig.Connector.Secondary = Failure(secondary, "owned secondary channel-close failure");
        Exception? original = rig.Connector.Primary;
        Exception? observed = null;
        byte[] healthyBytes = [];
        IContentSource? content = null;
        observed = Record.Exception(() => content = rig.Provider.OpenContent(rig.Item));
        if (content is not null)
        {
            using (content)
            {
                healthyBytes = new byte[3];
                Assert.Equal(3, content.Read(0, healthyBytes));
                Assert.Equal(new byte[] { 111, 110, 101 }, healthyBytes);
            }
        }
        bool originalRetained = ReferenceEquals(observed, original);
        int failedChannelCloses = rig.Connector.Channels[0].Closes;
        int activeAfterOpen = rig.Server.ActiveChannels;
        rig.Connector.Primary = null;
        rig.Connector.Secondary = null;
        rig.Connector.Stage = "healthy";
        int fullCapacity = rig.RentFullCapacity();
        byte[] recoveredBytes = new byte[3];
        using (var recovered = rig.Provider.OpenContent(rig.Item)!)
            Assert.Equal(3, recovered.Read(0, recoveredBytes));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Protocol = protocol, Stage = stage, Primary = primary, Secondary = secondary,
            OriginalFailureType = original?.GetType().Name, ExpectedFailureType = FailureType(primary),
            OriginalMessage = original?.Message, OriginalStackTrace = original?.StackTrace,
            ObservedFailureType = observed?.GetType().Name,
            ObservedMessage = observed?.Message, OriginalFailureRetained = originalRetained,
            FailedChannelCloses = failedChannelCloses, ActiveChannelsAfterOpening = activeAfterOpen,
            ExpectedCapacity = rig.Capacity, CapacityAcquired = fullCapacity,
            HealthyBytesBase64 = Convert.ToBase64String(healthyBytes), RecoveredBytesBase64 = Convert.ToBase64String(recoveredBytes),
            NoNativeProtocolOrServerIncidenceClaim = true,
        }));
        Assert.True(originalRetained, "Opening must retain its original exception object when channel cleanup also fails.");
        Assert.Equal(rig.Capacity, fullCapacity);
        Assert.Equal(new byte[] { 111, 110, 101 }, recoveredBytes);
        if (stage == "healthy")
        {
            Assert.Null(observed);
            Assert.Equal(0, failedChannelCloses);
            Assert.Equal(1, activeAfterOpen);
        }
        else
        {
            Assert.NotNull(observed);
            Assert.Equal(1, failedChannelCloses);
            Assert.Equal(0, activeAfterOpen);
        }
    }

    private static string? FailureType(string kind) => kind switch
    {
        "disconnected" => nameof(RemoteDisconnectedException),
        "io" => nameof(IOException),
        "denied" => nameof(UnauthorizedAccessException),
        "disposed" => nameof(ObjectDisposedException),
        _ => null,
    };

    private static Exception? Failure(string kind, string message) => kind switch
    {
        "disconnected" => new RemoteDisconnectedException(message),
        "io" => new IOException(message),
        "denied" => new UnauthorizedAccessException(message),
        "disposed" => new ObjectDisposedException(message),
        _ => null,
    };

    private sealed class Rig : IDisposable
    {
        private readonly string state = Directory.CreateTempSubdirectory("filecat-remote-open-failure-").FullName;
        public FakeSftpServer Server { get; } = new();
        public RemoteProfile Profile { get; }
        public FaultConnector Connector { get; }
        public SftpConnections Connections { get; }
        public SftpProvider Provider { get; }
        public ItemRef Item => new(SftpProvider.At(Profile, "/"), "one.dat", EntryKind.File);
        public int Capacity => Profile.IsFtp ? SftpConnections.MaxPerFtpServer : SftpConnections.MaxPerServer;

        public Rig(string protocol)
        {
            Profile = new RemoteProfile { Name = "Owned open failure", Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            Server.File("/one.dat", "one");
            var interaction = new ScriptedInteraction();
            interaction.Secrets.Enqueue("secret");
            Connector = new FaultConnector(Server);
            Connections = new SftpConnections(id => id == Profile.Id ? Profile : null, Connector,
                new HostKeyTrust(Path.Combine(state, "known_hosts")), new SessionSecretStore(), interaction);
            Provider = new SftpProvider(Connections, () => [Profile], p => p);
        }

        public int RentFullCapacity()
        {
            var leases = new List<SftpLease>();
            using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            bound.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                for (int i = 0; i < Capacity; i++)
                    leases.Add(Connections.Lease(Profile.Id, bound.Token));
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { }
            finally { foreach (var lease in leases) lease.Dispose(); }
            return leases.Count;
        }

        public void Dispose()
        {
            Connector.Primary = null;
            Connector.Secondary = null;
            foreach (var channel in Connector.Channels) channel.Cleanup();
            Connections.Dispose();
            Assert.Equal(0, Server.ActiveChannels);
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(state));
            Directory.Delete(state, recursive: true);
            Assert.False(Directory.Exists(state));
        }
    }

    private sealed class FaultConnector(FakeSftpServer server) : ISftpConnector
    {
        public string Stage { get; set; } = "healthy";
        public Exception? Primary { get; set; }
        public Exception? Secondary { get; set; }
        public List<FaultChannel> Channels { get; } = [];
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = new FaultChannel(this, new FakeConnector(server).Connect(profile, context, ct));
            Channels.Add(channel);
            return channel;
        }
    }

    private sealed class FaultChannel(FaultConnector owner, ISftpChannel inner) : ISftpChannel
    {
        private bool failed;
        public int Closes { get; private set; }
        public bool IsConnected => !failed && inner.IsConnected;
        public string HomeDirectory => inner.HomeDirectory;
        private void Fail(string stage)
        {
            if (owner.Stage == stage && owner.Primary is { } failure)
            {
                failed = true;
                throw failure;
            }
        }
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => inner.List(path, ct);
        public RemoteStat? Stat(string path) { Fail("stat"); return inner.Stat(path); }
        public Stream OpenRead(string path) { Fail("read"); return inner.OpenRead(path); }
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
