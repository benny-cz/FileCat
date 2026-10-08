using System.Text.Json;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>Closing failed remote content must release capacity and stop a disposed connection owner.</summary>
public sealed class RemoteLeaseLifetimeTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, string, bool>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
            {
                foreach (string failure in new[] { "io", "denied", "disposed" })
                {
                    cases.Add(protocol, "content-close", failure, false);
                    cases.Add(protocol, "content-close", failure, true);
                    cases.Add(protocol, "broken-lease-close", failure, false);
                    cases.Add(protocol, "owner-idle-close-fault", failure, false);
                }
                cases.Add(protocol, "owner-already-disposed", "none", false);
                cases.Add(protocol, "owner-disposed-during-connect", "none", false);
                cases.Add(protocol, "healthy", "none", false);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Failed_close_releases_capacity_and_disposed_owners_do_not_connect(string protocol, string action, string failure, bool secondaryCloseFailure)
    {
        using var rig = new Rig(protocol);
        Exception? observed = null;
        int streamCloses = 0, channelCloses = 0, capacity = 0;
        bool sameFailure = true;
        if (action == "content-close")
        {
            rig.Connector.StreamFailure = Failure(failure, "owned stream close");
            rig.Connector.ChannelFailure = secondaryCloseFailure ? new IOException("owned channel close") : null;
            using var content = rig.Provider.OpenContent(rig.Item)!;
            byte[] bytes = new byte[3];
            Assert.Equal(3, content.Read(0, bytes));
            Assert.Equal(new byte[] { 111, 110, 101 }, bytes);
            observed = Record.Exception(content.Dispose);
            sameFailure = ReferenceEquals(observed, rig.Connector.StreamFailure);
            content.Dispose(); // idempotence: failed close must not acquire or release capacity twice
            streamCloses = rig.Connector.Channels[0].StreamCloses;
            channelCloses = rig.Connector.Channels[0].Closes;
            rig.Connector.StreamFailure = null;
            rig.Connector.ChannelFailure = null;
            capacity = rig.RentFullCapacity();
        }
        else if (action == "broken-lease-close")
        {
            rig.Connector.ChannelFailure = Failure(failure, "owned channel close");
            var lease = rig.Connections.Lease(rig.Profile.Id, TestContext.Current.CancellationToken);
            lease.Broken = true;
            observed = Record.Exception(lease.Dispose);
            sameFailure = ReferenceEquals(observed, rig.Connector.ChannelFailure);
            lease.Dispose();
            channelCloses = rig.Connector.Channels[0].Closes;
            rig.Connector.ChannelFailure = null;
            capacity = rig.RentFullCapacity();
        }
        else if (action == "owner-idle-close-fault")
        {
            var leases = new List<SftpLease>();
            for (int i = 0; i < rig.Capacity; i++)
                leases.Add(rig.Connections.Lease(rig.Profile.Id, TestContext.Current.CancellationToken));
            foreach (var lease in leases) lease.Dispose();
            rig.Connector.ChannelFailure = Failure(failure, "owned idle channel close");
            observed = Record.Exception(rig.Connections.Dispose);
            sameFailure = ReferenceEquals(observed, rig.Connector.ChannelFailure);
            channelCloses = rig.Connector.Channels.Sum(c => c.Closes);
        }
        else if (action.StartsWith("owner-", StringComparison.Ordinal))
        {
            if (action == "owner-already-disposed") rig.Connections.Dispose();
            else rig.Connector.DuringConnect = rig.Connections.Dispose;
            SftpLease? acquired = null;
            observed = Record.Exception(() => acquired = rig.Connections.Lease(rig.Profile.Id, TestContext.Current.CancellationToken));
            acquired?.Dispose();
            channelCloses = rig.Connector.Channels.Sum(c => c.Closes);
        }
        else
        {
            using (var content = rig.Provider.OpenContent(rig.Item)!)
            {
                byte[] bytes = new byte[3];
                Assert.Equal(3, content.Read(0, bytes));
                Assert.Equal(new byte[] { 111, 110, 101 }, bytes);
                Assert.Equal(new ContentRevision(3, rig.Server.Lookup("/one.dat", true)!.Modified.Ticks), content.GetRevision());
            }
            capacity = rig.RentFullCapacity();
        }
        output.WriteLine(JsonSerializer.Serialize(new
        {
            Protocol = protocol, Action = action, Failure = failure, SecondaryCloseFailure = secondaryCloseFailure,
            ActualFailure = observed?.GetType().Name, OriginalFailureRetained = sameFailure,
            StreamCloses = streamCloses, ChannelCloses = channelCloses, CapacityAcquired = capacity,
            ExpectedCapacity = rig.Capacity, Connects = rig.Connector.Channels.Count,
            ActiveOwnedChannelsBeforeFixtureCleanup = rig.Server.ActiveChannels,
            NoNativeProtocolOrServerIncidenceClaim = true,
        }));
        if (action == "owner-idle-close-fault")
        {
            Assert.True(sameFailure, "Owner cleanup must preserve its first close failure.");
            Assert.NotNull(observed);
            Assert.Equal(rig.Capacity, channelCloses);
            Assert.Equal(0, rig.Server.ActiveChannels);
            return;
        }
        if (action.StartsWith("owner-", StringComparison.Ordinal))
        {
            Assert.IsType<ObjectDisposedException>(observed);
            Assert.Equal(action == "owner-already-disposed" ? 0 : 1, rig.Connector.Channels.Count);
            Assert.Equal(0, rig.Server.ActiveChannels);
            return;
        }
        Assert.True(sameFailure, "Secondary cleanup must not replace the original close failure.");
        if (action != "healthy")
        {
            Assert.NotNull(observed);
            Assert.Equal(1, channelCloses);
            if (action == "content-close") Assert.Equal(1, streamCloses);
        }
        Assert.Equal(rig.Capacity, capacity);
    }

    private static Exception Failure(string kind, string message) => kind switch
    {
        "io" => new IOException(message),
        "denied" => new UnauthorizedAccessException(message),
        _ => new ObjectDisposedException(message),
    };

    private sealed class Rig : IDisposable
    {
        private readonly string _state = Directory.CreateTempSubdirectory("filecat-remote-lease-lifetime-").FullName;
        public FakeSftpServer Server { get; } = new();
        public RemoteProfile Profile { get; }
        public FaultConnector Connector { get; }
        public SftpConnections Connections { get; }
        public SftpProvider Provider { get; }
        public ItemRef Item => new(SftpProvider.At(Profile, "/"), "one.dat", EntryKind.File);
        public int Capacity => Profile.IsFtp ? SftpConnections.MaxPerFtpServer : SftpConnections.MaxPerServer;

        public Rig(string protocol)
        {
            Profile = new RemoteProfile { Name = "Owned close control", Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            Server.File("/one.dat", "one");
            var ui = new ScriptedInteraction();
            ui.Secrets.Enqueue("secret");
            Connector = new FaultConnector(Server);
            Connections = new SftpConnections(id => id == Profile.Id ? Profile : null, Connector,
                new HostKeyTrust(Path.Combine(_state, "known_hosts")), new SessionSecretStore(), ui);
            Provider = new SftpProvider(Connections, () => [Profile], p => p);
        }

        public int RentFullCapacity()
        {
            var leases = new List<SftpLease>();
            try
            {
                using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
                bound.CancelAfter(TimeSpan.FromSeconds(2));
                for (int i = 0; i < Capacity; i++)
                {
                    var lease = Connections.Lease(Profile.Id, bound.Token);
                    leases.Add(lease);
                    Assert.Equal(3, lease.Channel.Stat("/one.dat")!.Value.Size);
                }
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested) { }
            finally { foreach (var lease in leases) lease.Dispose(); }
            return leases.Count;
        }

        public void Dispose()
        {
            Connector.ChannelFailure = null;
            foreach (var channel in Connector.Channels) channel.CloseForFixtureCleanup();
            Connections.Dispose();
            Assert.Equal(0, Server.ActiveChannels);
            Directory.Delete(_state, true);
        }
    }

    private sealed class FaultConnector(FakeSftpServer server) : ISftpConnector
    {
        public Exception? StreamFailure { get; set; }
        public Exception? ChannelFailure { get; set; }
        public Action? DuringConnect { get; set; }
        public List<FaultChannel> Channels { get; } = [];
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = new FaultChannel(this, new FakeConnector(server).Connect(profile, context, ct));
            Channels.Add(channel);
            DuringConnect?.Invoke();
            return channel;
        }
    }

    private sealed class FaultChannel(FaultConnector owner, ISftpChannel inner) : ISftpChannel
    {
        public int Closes { get; private set; }
        public int StreamCloses { get; private set; }
        public bool IsConnected => inner.IsConnected;
        public string HomeDirectory => inner.HomeDirectory;
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => inner.List(path, ct);
        public RemoteStat? Stat(string path) => inner.Stat(path);
        public Stream OpenRead(string path) => new FaultRead(inner.OpenRead(path), this, owner.StreamFailure);
        public Stream CreateNew(string path) => inner.CreateNew(path);
        public Stream OpenWriteAt(string path, long offset) => inner.OpenWriteAt(path, offset);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void Rename(string source, string target) => inner.Rename(source, target);
        public bool TryReplace(string source, string target) => inner.TryReplace(source, target);
        public void SetModified(string path, DateTime utc) => inner.SetModified(path, utc);
        public void Dispose() { Closes++; inner.Dispose(); if (owner.ChannelFailure is { } fail) throw fail; }
        public void CloseForFixtureCleanup() => inner.Dispose();

        private sealed class FaultRead(Stream inner, FaultChannel owner, Exception? failure) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override int Read(Span<byte> buffer) => inner.Read(buffer);
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void Flush() => inner.Flush();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            protected override void Dispose(bool disposing)
            {
                if (disposing) { owner.StreamCloses++; inner.Dispose(); if (failure is not null) throw failure; }
                base.Dispose(disposing);
            }
        }
    }
}
