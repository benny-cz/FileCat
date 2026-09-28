using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>A real FTP server for tests: pyftpdlib (MIT) when Python has it; tests skip otherwise.</summary>
internal sealed class TestFtpServer : IDisposable
{
    private readonly Process _process;
    public string Root { get; }
    public int Port { get; }
    public const string User = "tester";
    public const string Password = "secret-pass";

    private TestFtpServer(Process process, string root, int port)
    {
        _process = process;
        Root = root;
        Port = port;
    }

    private const string Script = """
        import sys
        from pyftpdlib.authorizers import DummyAuthorizer
        from pyftpdlib.servers import FTPServer
        root, user, password, cert = sys.argv[1], sys.argv[2], sys.argv[3], (sys.argv[4] if len(sys.argv) > 4 else "")
        auth = DummyAuthorizer()
        auth.add_user(user, password, root, perm="elradfmwMT")
        if cert:
            from pyftpdlib.handlers import TLS_FTPHandler as Handler
            Handler.certfile = cert
            Handler.tls_control_required = True
            Handler.tls_data_required = True
        else:
            from pyftpdlib.handlers import FTPHandler as Handler
        Handler.authorizer = auth
        Handler.use_gmt_times = True
        server = FTPServer(("127.0.0.1", 0), Handler)
        print("READY", server.socket.getsockname()[1], flush=True)
        server.serve_forever()
        """;

    private static string? Python()
    {
        foreach (var candidate in new[] { Environment.GetEnvironmentVariable("FILECAT_PYTHON"), "python3", "python" })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                using var p = Process.Start(new ProcessStartInfo(candidate, "-c \"import pyftpdlib\"") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false });
                if (p is null) continue;
                if (!p.WaitForExit(20_000)) { p.Kill(); continue; }
                if (p.ExitCode == 0) return candidate;
            }
            catch (System.ComponentModel.Win32Exception) { }
        }
        return null;
    }

    /// <param name="certificatePem">A PEM certificate with its key: explicit TLS (AUTH TLS) is then required.</param>
    public static TestFtpServer? TryStart(string? certificatePem = null)
    {
        if (Python() is not { } python) return null;
        var dir = Path.Combine(Path.GetTempPath(), "filecat-ftp-tests", Guid.NewGuid().ToString("N")[..8]);
        var root = Directory.CreateDirectory(Path.Combine(dir, "root")).FullName;
        var script = Path.Combine(dir, "server.py");
        File.WriteAllText(script, Script);
        string args = $"\"{script}\" \"{root}\" {User} {Password}";
        if (certificatePem is not null)
        {
            var cert = Path.Combine(dir, "server.pem");
            File.WriteAllText(cert, certificatePem);
            args += $" \"{cert}\"";
        }
        var process = Process.Start(new ProcessStartInfo(python, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
        var ready = process.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(30)) || ready.Result is not { } line || !line.StartsWith("READY ", StringComparison.Ordinal))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            return null; // pyftpdlib without TLS support (pyOpenSSL) cannot run the TLS server
        }
        return new TestFtpServer(process, root, int.Parse(line[6..]));
    }

    public RemoteProfile Profile(string protocol) => new()
    {
        Name = "test",
        Host = "127.0.0.1",
        Port = Port,
        User = User,
        Protocol = protocol,
        Temporary = true,
    };

    public void Dispose()
    {
        try { _process.Kill(true); } catch (InvalidOperationException) { }
        _process.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(Root)!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A self-signed certificate for 127.0.0.1 as PEM (certificate and key), and its SHA-256.</summary>
    public static (string Pem, string Sha256) SelfSigned(string name)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(san.Build());
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        return (cert.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem() + "\n", Convert.ToHexString(SHA256.HashData(cert.RawData)));
    }
}

internal sealed class FtpInteraction : IRemoteInteraction
{
    public HostKeyDecision CertificateAnswer { get; set; } = HostKeyDecision.Reject;
    public bool AllowPlain { get; set; }
    public List<CertificateCheck> CertificateQuestions { get; } = [];
    public int PlainQuestions { get; private set; }

    public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => HostKeyDecision.Reject;
    public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => new(TestFtpServer.Password, false);
    public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;

    public HostKeyDecision DecideCertificate(RemoteProfile profile, CertificateCheck check)
    {
        CertificateQuestions.Add(check);
        return CertificateAnswer;
    }

    public bool AllowUnencrypted(RemoteProfile profile)
    {
        PlainQuestions++;
        return AllowPlain;
    }
}

public sealed class FtpIntegrationTests : IDisposable
{
    private readonly string _state = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-ftp-state", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_state, true); } catch (IOException) { }
    }

    private SftpConnections Connections(RemoteProfile profile, FtpInteraction interaction) =>
        new(id => id == profile.Id ? profile : null, new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(_state, "known_hosts")), new SessionSecretStore(), interaction, new CertificateTrust(Path.Combine(_state, "trusted_certificates")));

    [Fact]
    public void Plain_FTP_needs_consent_and_then_behaves_like_the_other_remote_channels()
    {
        using var server = TestFtpServer.TryStart();
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        var interaction = new FtpInteraction();
        using var connections = Connections(profile, interaction);
        Assert.Throws<ConnectCanceledException>(() => connections.Lease(profile.Id, ct).Dispose());
        interaction.AllowPlain = true;
        using (var lease = connections.Lease(profile.Id, ct))
        {
            var channel = lease.Channel;
            Assert.Equal("/", channel.HomeDirectory);
            channel.CreateDirectory("/dir");
            Assert.ThrowsAny<IOException>(() => channel.CreateDirectory("/dir"));
            using (var s = channel.CreateNew("/dir/a.txt")) s.Write("hello world"u8);
            Assert.ThrowsAny<IOException>(() => channel.CreateNew("/dir/a.txt").Dispose());
            var entry = Assert.Single(channel.List("/dir", ct));
            Assert.Equal(("a.txt", 11L, false, false), (entry.Name, entry.Size, entry.IsDirectory, entry.IsLink));
            Assert.Equal(11, channel.Stat("/dir/a.txt")!.Value.Size);
            Assert.True(channel.Stat("/dir")!.Value.IsDirectory);
            Assert.Null(channel.Stat("/dir/missing.txt"));
            using (var read = channel.OpenRead("/dir/a.txt"))
            {
                // Out of order: a jump ends one transfer and resumes at the offset; the connection stays usable.
                read.Position = 6;
                var buffer = new byte[5];
                Assert.Equal(5, read.ReadAtLeast(buffer, 5));
                Assert.Equal("world", Encoding.ASCII.GetString(buffer));
                read.Position = 0;
                Assert.Equal(5, read.ReadAtLeast(buffer, 5));
                Assert.Equal("hello", Encoding.ASCII.GetString(buffer));
            }
            var stamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            channel.SetModified("/dir/a.txt", stamp);
            Assert.Equal(stamp, channel.Stat("/dir/a.txt")!.Value.ModifiedUtc);
            using (var s = channel.CreateNew("/dir/b.txt")) s.Write("b"u8);
            // An interrupted upload continues at the end of the file (APPE); anywhere else FTP cannot, and FileCat restarts.
            using (var more = channel.OpenWriteAt("/dir/b.txt", 1)) more.Write("cd"u8);
            Assert.Equal("bcd", File.ReadAllText(Path.Combine(server.Root, "dir", "b.txt")));
            Assert.Throws<NotSupportedException>(() => channel.OpenWriteAt("/dir/b.txt", 1).Dispose());
            Assert.ThrowsAny<IOException>(() => channel.Rename("/dir/a.txt", "/dir/b.txt"));
            Assert.False(channel.TryReplace("/dir/a.txt", "/dir/b.txt"));
            channel.List("/dir", ct).Single(e => e.Name == "a.txt").MoveTo("/dir/c.txt");
            Assert.Equal("hello world", File.ReadAllText(Path.Combine(server.Root, "dir", "c.txt")));
            Assert.ThrowsAny<IOException>(() => channel.CreateNew("/dir/evil\r\nDELE b.txt").Dispose());
            foreach (var e in channel.List("/dir", ct)) e.Delete();
            channel.List("/", ct).Single(e => e.Name == "dir").Delete();
            Assert.Empty(channel.List("/", ct));
        }
        Assert.Equal(2, interaction.PlainQuestions);
    }

    [Fact]
    public void FTPS_accepts_a_pinned_certificate_and_refuses_a_changed_one()
    {
        var (pem, sha) = TestFtpServer.SelfSigned("FileCat test A");
        var ct = TestContext.Current.CancellationToken;
        RemoteProfile profile;
        using (var server = TestFtpServer.TryStart(pem))
        {
            if (server is null) Assert.Skip("No FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
            profile = server.Profile(RemoteProtocols.FtpExplicitTls);
            var interaction = new FtpInteraction();
            using (var connections = Connections(profile, interaction))
            {
                Assert.Throws<CertificateRejectedException>(() => connections.Lease(profile.Id, ct).Dispose());
                var question = Assert.Single(interaction.CertificateQuestions);
                Assert.Equal((CertificateStatus.Unknown, sha), (question.Status, question.Certificate.Sha256));
                Assert.Contains(question.Certificate.Problems, p => p.Contains("self-signed", StringComparison.Ordinal));
                interaction.CertificateAnswer = HostKeyDecision.AcceptAndRemember;
                using (var lease = connections.Lease(profile.Id, ct))
                {
                    using (var s = lease.Channel.CreateNew("/secret.txt")) s.Write("over TLS"u8);
                    Assert.Equal("over TLS", File.ReadAllText(Path.Combine(server.Root, "secret.txt")));
                }
            }
            // A new session trusts the pinned certificate without asking.
            var quiet = new FtpInteraction();
            using (var connections = Connections(profile, quiet))
            using (var lease = connections.Lease(profile.Id, ct))
                Assert.Single(lease.Channel.List("/", ct));
            Assert.Empty(quiet.CertificateQuestions);
        }
        // The same server with another certificate: changed, refused unless the user accepts it again.
        var (other, _) = TestFtpServer.SelfSigned("FileCat test B");
        using (var server = TestFtpServer.TryStart(other))
        {
            Assert.NotNull(server);
            profile.Port = server.Port;
            var interaction = new FtpInteraction();
            File.WriteAllText(Path.Combine(_state, "trusted_certificates"), $"127.0.0.1:{server.Port} {sha}\n");
            using var connections = Connections(profile, interaction);
            Assert.Throws<CertificateRejectedException>(() => connections.Lease(profile.Id, ct).Dispose());
            Assert.Equal((CertificateStatus.Changed, sha), (interaction.CertificateQuestions[0].Status, interaction.CertificateQuestions[0].PinnedSha256));
        }
    }

    [Fact]
    public void An_FTPS_profile_never_falls_back_to_plain_FTP()
    {
        using var server = TestFtpServer.TryStart();
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var profile = server.Profile(RemoteProtocols.FtpExplicitTls);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        var ex = Assert.ThrowsAny<IOException>(() => connections.Lease(profile.Id, TestContext.Current.CancellationToken).Dispose());
        Assert.Contains("encryption", ex.Message);
    }
}
