using System.Security.Cryptography;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>Actual FTP transfers must finish before a copy's final source revision is queried (I229).</summary>
public sealed class FtpReadCompletionTests : IDisposable
{
    private readonly string _state = Directory.CreateTempSubdirectory("filecat-ftp-completion-").FullName;

    public static TheoryData<string, int, bool> ChannelCases => new()
    {
        { RemoteProtocols.Ftp, 0, false }, { RemoteProtocols.Ftp, 0, true },
        { RemoteProtocols.Ftp, 1, false }, { RemoteProtocols.Ftp, 1, true },
        { RemoteProtocols.Ftp, 65537, false }, { RemoteProtocols.Ftp, 65537, true },
        { RemoteProtocols.FtpExplicitTls, 0, false }, { RemoteProtocols.FtpExplicitTls, 0, true },
        { RemoteProtocols.FtpExplicitTls, 1, false }, { RemoteProtocols.FtpExplicitTls, 1, true },
        { RemoteProtocols.FtpExplicitTls, 65537, false }, { RemoteProtocols.FtpExplicitTls, 65537, true },
    };

    public static TheoryData<string, int> ContentCases => new()
    {
        { RemoteProtocols.Ftp, 0 }, { RemoteProtocols.Ftp, 1 }, { RemoteProtocols.Ftp, 65537 },
        { RemoteProtocols.FtpExplicitTls, 0 }, { RemoteProtocols.FtpExplicitTls, 1 }, { RemoteProtocols.FtpExplicitTls, 65537 },
    };

    private static TestFtpServer Server(string protocol)
    {
        string? pem = protocol == RemoteProtocols.Ftp ? null : TestFtpServer.SelfSigned("FileCat EOF control").Pem;
        var server = TestFtpServer.TryStart(pem);
        if (server is null) Assert.Skip("No owned FTP/TLS fixture here (pyftpdlib and pyOpenSSL are required).");
        return server;
    }

    private SftpConnections Connections(RemoteProfile profile) => new(id => id == profile.Id ? profile : null,
        new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
        new HostKeyTrust(Path.Combine(_state, "known_hosts")), new SessionSecretStore(),
        new FtpInteraction { AllowPlain = true, CertificateAnswer = HostKeyDecision.AcceptOnce },
        new CertificateTrust(Path.Combine(_state, "certificates")));

    private static byte[] Data(int length) => Enumerable.Range(0, length)
        .Select(i => (byte)((i * 31 + i / 257 + 17) & 255)).ToArray();

    [Theory]
    [MemberData(nameof(ChannelCases))]
    public void EOF_leaves_the_channel_ready_for_metadata_before_the_reader_is_disposed(string protocol, int length, bool span)
    {
        using var server = Server(protocol);
        byte[] original = Data(length);
        string disk = Path.Combine(server.Root, "bytes.dat");
        File.WriteAllBytes(disk, original);
        var profile = server.Profile(protocol);
        using var connections = Connections(profile);
        using var lease = connections.Lease(profile.Id, TestContext.Current.CancellationToken);
        var before = lease.Channel.Stat("/bytes.dat");
        using var read = lease.Channel.OpenRead("/bytes.dat", length);
        using var returned = new MemoryStream();
        var buffer = new byte[8191];
        Assert.Equal(0, read.Read(buffer, 0, 0));
        while (returned.Length < length)
        {
            int n = span ? read.Read(buffer.AsSpan()) : read.Read(buffer, 0, buffer.Length);
            Assert.InRange(n, 1, (int)Math.Min(buffer.Length, length - returned.Length));
            returned.Write(buffer, 0, n);
            long position = read.Position;
            Assert.Equal(0, read.Read(buffer, 0, 0));
            Assert.Equal(position, read.Position);
        }
        Assert.Equal(0, span ? read.Read(buffer.AsSpan()) : read.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, span ? read.Read(buffer.AsSpan()) : read.Read(buffer, 0, buffer.Length));
        // Still inside the reader lifetime: copying now checks its source revision over this control connection.
        Assert.Equal(before, lease.Channel.Stat("/bytes.dat"));
        Assert.True(lease.Channel.IsConnected);
        Assert.Equal(original, returned.ToArray());
        Assert.Equal(SHA256.HashData(original), SHA256.HashData(File.ReadAllBytes(disk)));
        Assert.Equal("bytes.dat", Assert.Single(lease.Channel.List("/", TestContext.Current.CancellationToken)).Name);
    }

    [Theory]
    [MemberData(nameof(ContentCases))]
    public void Provider_content_checks_its_final_revision_while_the_content_is_still_owned(string protocol, int length)
    {
        using var server = Server(protocol);
        byte[] original = Data(length);
        File.WriteAllBytes(Path.Combine(server.Root, "bytes.dat"), original);
        var profile = server.Profile(protocol);
        using var connections = Connections(profile);
        var provider = new SftpProvider(connections, () => [profile], p => p);
        using var content = provider.OpenContent(new ItemRef(SftpProvider.At(profile, "/"), "bytes.dat", EntryKind.File))!;
        var revision = content.GetRevision();
        Assert.NotNull(revision);
        Assert.Equal(length, revision.Value.Length);
        var buffer = new byte[16384];
        using var returned = new MemoryStream();
        while (returned.Length < length)
        {
            int n = content.Read(returned.Length, buffer);
            Assert.InRange(n, 1, (int)Math.Min(buffer.Length, length - returned.Length));
            returned.Write(buffer, 0, n);
        }
        Assert.Equal(0, content.Read(length, buffer));
        Assert.Equal(revision, content.GetRevision());
        Assert.Equal(0, content.Read(length, buffer));
        Assert.Equal(revision, content.GetRevision());
        Assert.Equal(original, returned.ToArray());
    }

    [Theory]
    [MemberData(nameof(ContentCases))]
    public void A_completed_read_can_seek_and_start_another_transfer_on_the_same_channel(string protocol, int length)
    {
        using var server = Server(protocol);
        byte[] original = Data(length);
        File.WriteAllBytes(Path.Combine(server.Root, "bytes.dat"), original);
        var profile = server.Profile(protocol);
        using var connections = Connections(profile);
        using var lease = connections.Lease(profile.Id, TestContext.Current.CancellationToken);
        var before = lease.Channel.Stat("/bytes.dat");
        using var read = lease.Channel.OpenRead("/bytes.dat", length);
        using var first = new MemoryStream();
        read.CopyTo(first);
        Assert.Equal(original, first.ToArray());
        Assert.Equal(before, lease.Channel.Stat("/bytes.dat"));
        Assert.Equal(0, read.Seek(0, SeekOrigin.Begin));
        using var second = new MemoryStream();
        read.CopyTo(second);
        Assert.Equal(original, second.ToArray());
        Assert.Equal(before, lease.Channel.Stat("/bytes.dat"));
        Assert.Equal(length, read.Position);
    }

    [Theory]
    [InlineData(RemoteProtocols.Ftp)]
    [InlineData(RemoteProtocols.FtpExplicitTls)]
    public void An_early_server_EOF_also_finishes_the_transfer_before_metadata_is_queried(string protocol)
    {
        using var server = Server(protocol);
        byte[] original = Data(65537);
        File.WriteAllBytes(Path.Combine(server.Root, "bytes.dat"), original);
        var profile = server.Profile(protocol);
        using var connections = Connections(profile);
        using var lease = connections.Lease(profile.Id, TestContext.Current.CancellationToken);
        var before = lease.Channel.Stat("/bytes.dat");
        // The file is shorter than the captured length. The executor will reject that copy; the reader still releases
        // its transfer so the next command and cleanup do not consume the previous transfer's reply.
        using var read = lease.Channel.OpenRead("/bytes.dat", original.Length + 1);
        using var returned = new MemoryStream();
        read.CopyTo(returned);
        Assert.Equal(original, returned.ToArray());
        Assert.Equal(original.Length, read.Position);
        Assert.Equal(before, lease.Channel.Stat("/bytes.dat"));
        Assert.True(lease.Channel.IsConnected);
    }

    [Theory]
    [InlineData(RemoteProtocols.Ftp, 0)]
    [InlineData(RemoteProtocols.Ftp, 1)]
    [InlineData(RemoteProtocols.Ftp, 32768)]
    [InlineData(RemoteProtocols.Ftp, 65537)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 0)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 1)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 32768)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 65537)]
    public void A_revision_after_a_partial_read_allows_the_same_content_to_continue(string protocol, int prefix)
    {
        using var server = Server(protocol);
        byte[] original = Data(65537);
        File.WriteAllBytes(Path.Combine(server.Root, "bytes.dat"), original);
        var profile = server.Profile(protocol);
        using var connections = Connections(profile);
        var provider = new SftpProvider(connections, () => [profile], p => p);
        using var content = provider.OpenContent(new ItemRef(SftpProvider.At(profile, "/"), "bytes.dat", EntryKind.File))!;
        var before = content.GetRevision();
        Assert.NotNull(before);
        var returned = new byte[original.Length];
        int at = 0;
        while (at < prefix)
        {
            int n = content.Read(at, returned.AsSpan(at, prefix - at));
            Assert.InRange(n, 1, prefix - at);
            at += n;
        }
        Assert.Equal(before, content.GetRevision());
        Assert.Equal(before, content.GetRevision());
        while (at < returned.Length)
        {
            int n = content.Read(at, returned.AsSpan(at, Math.Min(8192, returned.Length - at)));
            Assert.InRange(n, 1, returned.Length - at);
            at += n;
        }
        Assert.Equal(0, content.Read(at, returned.AsSpan(0, 1)));
        Assert.Equal(before, content.GetRevision());
        Assert.Equal(original, returned);
    }

    public void Dispose() => Directory.Delete(_state, recursive: true);
}
