using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class RemoteConsumerRetirementTests(ITestOutputHelper output)
{
    public static TheoryData<string, string> Cases
    {
        get
        {
            var rows = new TheoryData<string, string>();
            foreach (string protocol in new[] { RemoteProtocols.Sftp, RemoteProtocols.Ftp, RemoteProtocols.FtpExplicitTls, RemoteProtocols.FtpImplicitTls })
            foreach (string route in new[] { "broken-lease", "failed-lease", "disconnected-lease", "shutdown-lease", "closed-content", "failed-stream", "failed-channel", "live-content" })
                rows.Add(protocol, route);
            return rows;
        }
    }

    private sealed record Rig(IDisposable Held, WeakReference Owner, WeakReference Channel,
        WeakReference Payload, WeakReference? Stream, bool OriginalFailureRetained, bool Idempotent);

    private static byte[] Known() => Enumerable.Range(0, 32768).Select(i => (byte)(17 + i * 43)).ToArray();

    [Theory, MemberData(nameof(Cases))]
    public void Retired_remote_consumers_release_their_owners_and_closed_payloads(string protocol, string route)
    {
        string root = Directory.CreateTempSubdirectory("fc-remote-consumer-retirement-").FullName;
        string path = Path.Combine(root, "known.bin");
        File.WriteAllBytes(path, Known());
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Rig? rig = null;
        try
        {
            rig = Visit(root, path, protocol, route);
            for (int i = 0; i < 8; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Thread.Sleep(10); }
            bool owner = rig.Owner.IsAlive, channel = rig.Channel.IsAlive, payload = rig.Payload.IsAlive;
            bool? stream = rig.Stream?.IsAlive;
            bool live = route == "live-content", exactRead = true;
            if (live)
            {
                byte[] actual = new byte[32768];
                var source = (IContentSource)rig.Held;
                exactRead = source.Read(0, actual) == actual.Length && actual.SequenceEqual(Known()) && source.GetRevision()?.Length == actual.Length;
            }
            bool exclusive = Exclusive(path);
            bool unchanged = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == hash;
            output.WriteLine("REMOTE_CONSUMER_RETIREMENT " + JsonSerializer.Serialize(new
            {
                protocol, route, owner, channel, payload, stream, exclusive, unchanged, exactRead,
                rig.OriginalFailureRetained, rig.Idempotent, knownBytes = 32768, hash,
                heldConsumer = true, actualOwnedFileHandles = true, controlledInMemoryProtocol = true,
                managedReachabilityNotProcessPeak = true, noNativeServerOrSecretIncidenceClaim = true,
            }));
            GC.KeepAlive(rig.Held);
            Assert.True(unchanged && exactRead && rig.OriginalFailureRetained && rig.Idempotent);
            Assert.Equal(live, owner);
            Assert.Equal(live, channel);
            Assert.Equal(live, payload);
            if (stream is not null) Assert.Equal(live, stream);
            Assert.Equal(!live, exclusive);
        }
        finally
        {
            if (rig is not null)
            {
                var owner = rig.Owner.Target as SftpConnections;
                rig.Held.Dispose();
                owner?.Dispose();
            }
            Assert.True(Exclusive(path));
            Assert.True(File.ReadAllBytes(path).SequenceEqual(Known()));
            Directory.Delete(root, true);
            Assert.False(Directory.Exists(root));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Rig Visit(string root, string path, string protocol, string route)
    {
        var profile = new RemoteProfile { Host = "owned.invalid", User = "user", Protocol = protocol, PlainTextAccepted = true };
        var server = new FakeSftpServer(); server.File("/known.bin", "owned");
        var ui = new ScriptedInteraction { HostKeyAnswer = HostKeyDecision.AcceptOnce }; ui.Secrets.Enqueue("secret");
        var connector = new Connector(server, path);
        var owner = new SftpConnections(id => id == profile.Id ? profile : null, connector,
            new HostKeyTrust(Path.Combine(root, "known_hosts")), new SessionSecretStore(), ui);
        var ownerReference = new WeakReference(owner);
        bool content = route.EndsWith("content", StringComparison.Ordinal) || route is "failed-stream" or "failed-channel";
        IDisposable held;
        if (content)
        {
            var provider = new SftpProvider(owner, () => [profile], p => p);
            held = provider.OpenContent(new ItemRef(SftpProvider.At(profile, "/"), "known.bin", EntryKind.File))!;
            byte[] bytes = new byte[32768];
            Assert.Equal(bytes.Length, ((IContentSource)held).Read(0, bytes));
            Assert.True(bytes.SequenceEqual(Known()));
        }
        else held = owner.Lease(profile.Id, TestContext.Current.CancellationToken);
        var channel = (OwnedChannel)connector.Channel!.Target!;
        var payload = new WeakReference(channel.Bytes);
        var stream = connector.Stream;
        var failure = new IOException("Owned remote consumer close control");
        if (route == "failed-stream") ((TrackedStream)stream!.Target!).Failure = failure;
        if (route is "failed-lease" or "failed-channel") channel.Failure = failure;
        if (route is "broken-lease" or "failed-lease") ((SftpLease)held).Broken = true;
        if (route == "failed-channel") channel.Broken = true;
        if (route == "disconnected-lease") owner.Disconnect(profile.Id);
        if (route == "shutdown-lease") owner.Dispose();
        bool original = true, idempotent = true;
        if (route != "live-content")
        {
            var observed = Record.Exception(held.Dispose);
            original = route.StartsWith("failed-", StringComparison.Ordinal) ? ReferenceEquals(observed, failure) : observed is null;
            idempotent = Record.Exception(held.Dispose) is null;
            channel.Failure = null;
            owner.Dispose();
        }
        return new Rig(held, ownerReference, connector.Channel, payload, stream, original, idempotent);
    }

    private static bool Exclusive(string path)
    {
        try { using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); return true; }
        catch (IOException) { return false; }
    }

    private sealed class Connector(FakeSftpServer server, string path) : ISftpConnector
    {
        public WeakReference? Channel, Stream;
        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = new OwnedChannel(new FakeConnector(server).Connect(profile, context, ct), this, path);
            Channel = new WeakReference(channel);
            return channel;
        }
    }

    private sealed class OwnedChannel(ISftpChannel inner, Connector observer, string path) : ISftpChannel
    {
        private readonly FileStream _holder = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        public byte[] Bytes { get; } = File.ReadAllBytes(path);
        public Exception? Failure;
        public bool Broken;
        public bool IsConnected => !Broken && inner.IsConnected;
        public string HomeDirectory => inner.HomeDirectory;
        public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => inner.List(path, ct);
        public RemoteStat? Stat(string path) => inner.Stat(path) is { } value ? value with { Size = Bytes.Length } : null;
        public Stream OpenRead(string name)
        {
            var stream = new TrackedStream(Bytes, path); observer.Stream = new WeakReference(stream); return stream;
        }
        public Stream CreateNew(string path) => throw new NotSupportedException();
        public Stream OpenWriteAt(string path, long offset) => throw new NotSupportedException();
        public void CreateDirectory(string path) => throw new NotSupportedException();
        public void Rename(string source, string target) => throw new NotSupportedException();
        public bool TryReplace(string source, string target) => throw new NotSupportedException();
        public void SetModified(string path, DateTime utc) => throw new NotSupportedException();
        public void Dispose() { _holder.Dispose(); inner.Dispose(); if (Failure is not null) throw Failure; }
    }

    private sealed class TrackedStream(byte[] bytes, string path) : MemoryStream(bytes, writable: false)
    {
        private readonly FileStream _holder = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        public Exception? Failure;
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _holder.Dispose(); base.Dispose(disposing); if (Failure is not null) throw Failure; }
        }
    }
}
