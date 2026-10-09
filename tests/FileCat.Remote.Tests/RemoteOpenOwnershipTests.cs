using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>Display metadata must complete before ownership of an opened content stream is acquired.</summary>
public sealed class RemoteOpenOwnershipTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, bool>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
            {
                foreach (string failure in new[] { "io", "denied", "disposed", "invalid", "disconnected" })
                    foreach (bool secondary in new[] { false, true }) cases.Add(protocol, failure, secondary);
                cases.Add(protocol, "none", false);
                cases.Add(protocol, "none", true);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Display_lookup_failure_does_not_abandon_an_opened_stream(string protocol, string failure, bool secondary)
    {
        using var rig = new Rig(protocol);
        using var other = rig.Connections.Lease(rig.Other.Id, TestContext.Current.CancellationToken);
        var unrelated = (Channel)other.Channel;
        Exception? primary = failure switch
        {
            "io" => new IOException("Owned display profile lookup failure"),
            "denied" => new UnauthorizedAccessException("Owned display profile lookup failure"),
            "disposed" => new ObjectDisposedException("owned display profile lookup"),
            "invalid" => new InvalidOperationException("Owned display profile lookup failure"),
            "disconnected" => new RemoteDisconnectedException("Owned display profile lookup failure"),
            _ => null,
        };
        rig.LookupFailure = primary;
        rig.Connector.CloseFailure = secondary ? new IOException("Owned secondary channel close failure") : null;
        IContentSource? content = null;
        Exception? observed = Record.Exception(() => content = rig.Provider.OpenContent(rig.Item));
        var initial = rig.Connector.Channels.Single(c => c.ProfileId == rig.Profile.Id);
        string[] initialPaths = initial.Reads.Select(s => s.Path).ToArray();
        int readsAfterOpen = initial.Reads.Count;
        bool[] releasedAfterOpen = initialPaths.Select(Exclusive).ToArray();
        byte[] accepted = [];
        string? display = content?.DisplayName;
        if (content is not null)
        {
            using (content)
            {
                accepted = new byte[rig.Bytes.Length];
                Assert.Equal(accepted.Length, content.Read(0, accepted));
            }
        }
        int[] closeCounts = initial.Reads.Select(s => s.Closes).ToArray();
        bool[] releasedAfterDispose = initialPaths.Select(Exclusive).ToArray();
        int channelCloses = initial.Closes;
        rig.LookupFailure = null;
        rig.Connector.CloseFailure = null;
        foreach (var channel in rig.Connector.Channels) channel.CloseFailure = null;
        var capacity = new List<SftpLease>();
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(5));
        try { for (int i = 0; i < rig.Capacity; i++) capacity.Add(rig.Connections.Lease(rig.Profile.Id, bound.Token)); }
        finally { foreach (var lease in capacity) lease.Dispose(); }
        byte[] recovered = new byte[rig.Bytes.Length];
        using (var next = rig.Provider.OpenContent(rig.Item)!) Assert.Equal(recovered.Length, next.Read(0, recovered));
        byte[] otherBytes;
        using (var next = unrelated.OpenRead("/one.dat"))
        using (var buffer = new MemoryStream()) { next.CopyTo(buffer); otherBytes = buffer.ToArray(); }
        output.WriteLine("REMOTE_OPEN_OWNERSHIP " + JsonSerializer.Serialize(new
        {
            protocol, failure, secondary, OriginalErrorRetained = ReferenceEquals(primary, observed),
            ErrorType = observed?.GetType().Name, ErrorStack = observed?.StackTrace,
            InitialStreamPaths = initialPaths, ReadsAfterOpen = readsAfterOpen,
            InitialHoldersReleasedAfterOpen = releasedAfterOpen, InitialStreamCloseCounts = closeCounts,
            InitialHoldersReleasedAfterDispose = releasedAfterDispose, InitialChannelCloses = channelCloses,
            CapacityAcquired = capacity.Count, rig.Capacity, AcceptedSHA256 = Hash(accepted), RecoveredSHA256 = Hash(recovered),
            ExpectedSHA256 = Hash(rig.Bytes), UnrelatedSHA256 = Hash(otherBytes), UnrelatedCloses = unrelated.Closes,
            DisplayName = display, ActualOwnedReadOnlyFileStreams = true,
            ControlledProfileCallbackNotNativeServerOrObservedProfileRaceOrCandidate = true,
        }));
        Assert.Same(primary, observed);
        Assert.Equal(primary is null ? 1 : 0, readsAfterOpen);
        Assert.All(closeCounts, count => Assert.Equal(1, count));
        Assert.All(releasedAfterDispose, value => Assert.True(value));
        Assert.Equal(failure == "disconnected" ? 1 : 0, channelCloses);
        Assert.Equal(rig.Capacity, capacity.Count);
        Assert.Equal(rig.Bytes, recovered);
        Assert.Equal(rig.Bytes, otherBytes);
        Assert.Equal(0, unrelated.Closes);
        if (primary is null)
        {
            Assert.Equal(rig.Bytes, accepted);
            Assert.All(releasedAfterOpen, value => Assert.False(value));
            Assert.Equal(protocol + "://" + rig.Profile.Display + "/one.dat", display);
        }
    }

    private static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static bool Exclusive(string path)
    {
        try { using var holder = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private sealed class Rig : IDisposable
    {
        private readonly string root = Directory.CreateTempSubdirectory("fc-open-ownership-").FullName;
        public byte[] Bytes { get; } = "owned remote content bytes\n"u8.ToArray();
        public RemoteProfile Profile { get; }
        public RemoteProfile Other { get; }
        public Connector Connector { get; }
        public SftpConnections Connections { get; }
        public SftpProvider Provider { get; }
        public ItemRef Item => new(SftpProvider.At(Profile, "/"), "one.dat", EntryKind.File);
        public Exception? LookupFailure;
        public bool LookupArmed;
        public int Capacity => Profile.IsFtp ? SftpConnections.MaxPerFtpServer : SftpConnections.MaxPerServer;
        public Rig(string protocol)
        {
            Profile = new RemoteProfile { Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            Other = new RemoteProfile { Host = "other.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            var server = new FakeSftpServer();server.File("/one.dat", "owned remote content bytes\n");
            Connector = new Connector(server, this, root);
            var interaction = new ScriptedInteraction();interaction.Secrets.Enqueue("secret");interaction.Secrets.Enqueue("secret");
            Connections = new SftpConnections(id =>
            {
                if (id == Profile.Id && LookupArmed)
                {
                    LookupArmed = false;
                    if (LookupFailure is { } problem) throw problem;
                }
                return id == Profile.Id ? Profile : id == Other.Id ? Other : null;
            }, Connector, new HostKeyTrust(Path.Combine(root, "known_hosts")), new SessionSecretStore(), interaction);
            Provider = new SftpProvider(Connections, () => [Profile], p => p);
        }
        public void Dispose()
        {
            LookupFailure = null;LookupArmed = false;
            foreach (var channel in Connector.Channels) channel.CloseFailure = null;
            Connections.Dispose();
            foreach (var stream in Connector.Channels.SelectMany(c => c.Reads)) stream.Dispose();
            Assert.All(Connector.Channels.SelectMany(c => c.Reads), s => Assert.True(Exclusive(s.Path)));
            string parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            Assert.StartsWith(parent, Path.GetFullPath(root));
            Directory.Delete(root, recursive: true);Assert.False(Directory.Exists(root));
        }
    }
    private sealed class Connector(FakeSftpServer server, Rig rig, string root) : ISftpConnector
    {
        public List<Channel> Channels { get; } = [];
        public Exception? CloseFailure;
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var result = new Channel(new FakeConnector(server).Connect(profile, context, ct), rig, root, profile.Id, Channels.Count)
            { CloseFailure = profile.Id == rig.Profile.Id ? CloseFailure : null };
            Channels.Add(result);return result;
        }
    }
    private sealed class Channel(ISftpChannel inner, Rig rig, string root, string profileId, int index) : ISftpChannel
    {
        public string ProfileId => profileId;
        public List<HeldRead> Reads { get; } = [];
        public Exception? CloseFailure;
        public int Closes;
        public bool IsConnected => inner.IsConnected;
        public string HomeDirectory => inner.HomeDirectory;
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => inner.List(path, ct);
        public RemoteStat? Stat(string path)
        {
            var stat = inner.Stat(path);
            if (profileId == rig.Profile.Id) rig.LookupArmed = true;
            return stat;
        }
        public Stream OpenRead(string path)
        {
            using var original = inner.OpenRead(path);
            string file = Path.Combine(root, "read-" + index + "-" + Reads.Count + ".dat");
            using (var target = File.Create(file)) original.CopyTo(target);
            var held = new HeldRead(file);Reads.Add(held);return held;
        }
        public Stream CreateNew(string path) => inner.CreateNew(path);
        public Stream OpenWriteAt(string path, long offset) => inner.OpenWriteAt(path, offset);
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public void Rename(string source, string target) => inner.Rename(source, target);
        public bool TryReplace(string source, string target) => inner.TryReplace(source, target);
        public void SetModified(string path, DateTime utc) => inner.SetModified(path, utc);
        public void Dispose() { Closes++;inner.Dispose();if (CloseFailure is { } problem) throw problem; }
    }
    private sealed class HeldRead : FileStream
    {
        public string Path { get; }
        public int Closes;
        private bool closed;
        public HeldRead(string path) : base(path, FileMode.Open, FileAccess.Read, FileShare.Read) { Path = path; }
        protected override void Dispose(bool disposing)
        {
            if (!closed) { closed = true;Closes++; }
            base.Dispose(disposing);
        }
    }
}
