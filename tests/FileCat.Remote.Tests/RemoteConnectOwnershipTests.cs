using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>A channel belongs to its connector caller until post-connect setup hands it to the pool.</summary>
public sealed class RemoteConnectOwnershipTests(ITestOutputHelper output)
{
    public static TheoryData<string, string, string, bool> Cases
    {
        get
        {
            var cases = new TheoryData<string, string, string, bool>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
            {
                foreach (string site in new[] { "secret-write", "profile-notify", "save-warning" })
                    foreach (string failure in new[] { "denied", "disposed", "invalid" })
                        foreach (bool closeFailure in new[] { false, true }) cases.Add(protocol, site, failure, closeFailure);
                foreach (string failure in new[] { "io", "authentication" })
                    foreach (bool closeFailure in new[] { false, true }) cases.Add(protocol, "save-warning", failure, closeFailure);
                cases.Add(protocol, "healthy", "none", false);
                cases.Add(protocol, "save-warning-healthy", "none", false);
            }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Post_connect_failures_close_unadmitted_channels_and_preserve_the_original_error(string protocol, string site, string failure, bool closeFailure)
    {
        using var rig = new Rig(protocol);
        using var unrelated = rig.Connections.Lease(rig.Other.Id, TestContext.Current.CancellationToken);
        var other = (HeldChannel)unrelated.Channel;
        Exception? primary = failure == "none" ? null : failure switch
        {
            "denied" => new UnauthorizedAccessException("Owned post-connect denial"),
            "disposed" => new ObjectDisposedException("owned post-connect callback"),
            "io" => new IOException("Owned warning callback I/O"),
            "authentication" => new RemoteAuthenticationException("Owned callback error, after successful authentication"),
            _ => new InvalidOperationException("Owned post-connect callback error"),
        };
        rig.Store.WriteFailure = site == "secret-write" ? primary : site.StartsWith("save-warning", StringComparison.Ordinal) ? new IOException("Owned store write rejected") : null;
        rig.Interaction.InformFailure = site == "save-warning" ? primary : null;
        rig.Connector.CloseFailure = closeFailure ? new IOException("Owned secondary channel close error") : null;
        int notifications = 0;
        rig.Connections.ProfileChanged += profile =>
        {
            if (profile.Id != rig.Profile.Id) return;
            notifications++;
            if (site == "profile-notify" && primary is not null) throw primary;
        };
        SftpLease? acquired = null;
        Exception? observed = Record.Exception(() => acquired = rig.Connections.Lease(rig.Profile.Id, TestContext.Current.CancellationToken));
        var initial = rig.Connector.Channels.Where(c => c.ProfileId == rig.Profile.Id).ToArray();
        int[] closes = initial.Select(c => c.Closes).ToArray();
        bool[] released = initial.Select(c => Exclusive(c.HolderPath)).ToArray();
        int secretQuestions = rig.Interaction.SecretQuestions;
        int storeWrites = rig.Store.Writes;
        int warnings = rig.Interaction.Warnings;
        byte[] acceptedBytes = acquired is null ? [] : Read(acquired.Channel);
        acquired?.Dispose();
        rig.Store.WriteFailure = null;
        rig.Interaction.InformFailure = null;
        rig.Connector.CloseFailure = null;
        foreach (var channel in rig.Connector.Channels) channel.CloseFailure = null;
        var capacity = new List<SftpLease>();
        byte[] recovered = [];
        bool failedChannelsNotReused;
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bound.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            for (int i = 0; i < rig.Capacity; i++) capacity.Add(rig.Connections.Lease(rig.Profile.Id, bound.Token));
            recovered = Read(capacity[0].Channel);
            failedChannelsNotReused = primary is null || capacity.All(l => !initial.Contains(l.Channel));
        }
        finally { foreach (var lease in capacity) lease.Dispose(); }
        byte[] unrelatedBytes = Read(other);
        output.WriteLine("REMOTE_CONNECT_OWNERSHIP " + JsonSerializer.Serialize(new
        {
            protocol, site, failure, closeFailure, InitialChannels = initial.Length, InitialCloseCounts = closes, InitialHoldersReleased = released,
            OriginalErrorRetained = ReferenceEquals(primary, observed), ErrorType = observed?.GetType().Name, ErrorStack = observed?.StackTrace,
            SecretQuestions = secretQuestions, StoreWrites = storeWrites, Warnings = warnings, Notifications = notifications,
            AcceptedBytesSHA256 = Hash(acceptedBytes), RecoveredBytesSHA256 = Hash(recovered), ExpectedBytesSHA256 = Hash("owned channel bytes\n"u8.ToArray()),
            UnrelatedBytesSHA256 = Hash(unrelatedBytes), UnrelatedCloses = other.Closes, CapacityAcquired = capacity.Count, rig.Capacity,
            FailedChannelsNotReused = failedChannelsNotReused, InitialHolderPaths = initial.Select(c => c.HolderPath).ToArray(),
            ActualOwnedNativeFileHolders = true, ControlledAdapterOnlyNoNativeServerCredentialStoreOrCandidateClaim = true,
        }));
        Assert.Same(primary, observed);
        Assert.Single(initial);
        Assert.Equal(2, secretQuestions); // one unrelated connection, then one successfully authenticated target
        Assert.Equal(1, storeWrites);
        Assert.Equal(site.StartsWith("save-warning", StringComparison.Ordinal) ? 1 : 0, warnings);
        Assert.Equal(site is "profile-notify" or "healthy" ? 1 : 0, notifications);
        Assert.All(closes, count => Assert.Equal(primary is null ? 0 : 1, count));
        Assert.All(released, value => Assert.Equal(primary is not null, value));
        Assert.True(failedChannelsNotReused);
        Assert.Equal(rig.Capacity, capacity.Count);
        Assert.Equal("owned channel bytes\n"u8.ToArray(), recovered);
        Assert.Equal(recovered, unrelatedBytes);
        Assert.Equal(0, other.Closes);
        if (primary is null) Assert.Equal(recovered, acceptedBytes);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static byte[] Read(ISftpChannel channel)
    {
        using var stream = channel.OpenRead("/one.dat");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
    private static bool Exclusive(string path)
    {
        try { using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); return true; }
        catch (IOException) { return false; }
    }
    private sealed class Rig : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("fc-connect-ownership-").FullName;
        public RemoteProfile Profile { get; }
        public RemoteProfile Other { get; }
        public OwnedSecretStore Store { get; } = new();
        public Interaction Interaction { get; } = new();
        public Connector Connector { get; }
        public SftpConnections Connections { get; }
        public int Capacity => Profile.IsFtp ? SftpConnections.MaxPerFtpServer : SftpConnections.MaxPerServer;
        public Rig(string protocol)
        {
            Profile = new RemoteProfile { Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            Other = new RemoteProfile { Host = "other.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
            var server = new FakeSftpServer();
            server.File("/one.dat", "owned channel bytes\n");
            Connector = new Connector(server, _root);
            Connections = new SftpConnections(id => id == Profile.Id ? Profile : id == Other.Id ? Other : null,
                Connector, new HostKeyTrust(Path.Combine(_root, "known_hosts")), Store, Interaction);
            Interaction.SaveFor = Profile.Id;
        }
        public void Dispose()
        {
            foreach (var channel in Connector.Channels) channel.CloseFailure = null;
            Connections.Dispose();
            foreach (var channel in Connector.Channels) channel.Cleanup();
            Assert.All(Connector.Channels, c => Assert.True(Exclusive(c.HolderPath)));
            Directory.Delete(_root, recursive: true);
            Assert.False(Directory.Exists(_root));
        }
    }
    private sealed class OwnedSecretStore : ISecretStore
    {
        public Exception? WriteFailure;
        public int Writes;
        public bool IsPersistent => true;
        public string? Read(string key) => null;
        public void Write(string key, string secret, string? label = null) { Writes++; if (WriteFailure is { } failure) throw failure; }
        public void Delete(string key) { }
    }
    private sealed class Interaction : IRemoteInteraction
    {
        public string? SaveFor;
        public int SecretQuestions;
        public int Warnings;
        public Exception? InformFailure;
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => HostKeyDecision.AcceptOnce;
        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) { SecretQuestions++; return new SecretAnswer("secret", profile.Id == SaveFor); }
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;
        public void Inform(RemoteProfile profile, string message) { Warnings++; if (InformFailure is { } failure) throw failure; }
    }
    private sealed class Connector(FakeSftpServer server, string root) : ISftpConnector
    {
        public List<HeldChannel> Channels { get; } = [];
        public Exception? CloseFailure;
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = new HeldChannel(new FakeConnector(server).Connect(profile, context, ct), profile.Id,
                Path.Combine(root, "holder-" + Channels.Count + ".dat"), CloseFailure);
            Channels.Add(channel);
            return channel;
        }
    }
    private sealed class HeldChannel : ISftpChannel
    {
        private readonly ISftpChannel _inner;
        private readonly FileStream _holder;
        public string ProfileId { get; }
        public string HolderPath { get; }
        public Exception? CloseFailure;
        public int Closes;
        public HeldChannel(ISftpChannel inner, string id, string path, Exception? failure)
        {
            _inner = inner; ProfileId = id; HolderPath = path; CloseFailure = failure;
            File.WriteAllBytes(path, "owned native holder\n"u8.ToArray());
            _holder = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        public bool IsConnected => _inner.IsConnected;
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
        public void Dispose() { Closes++; Cleanup(); if (CloseFailure is { } failure) throw failure; }
        public void Cleanup() { _holder.Dispose(); _inner.Dispose(); }
    }
}
