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
    private Process _process;
    private readonly ProcessStartInfo _start;
    public string Root { get; }
    public int Port { get; }
    public const string User = "tester";
    public const string Password = "secret-pass";

    private TestFtpServer(Process process, ProcessStartInfo start, string root, int port)
    {
        _process = process;
        _start = start;
        Root = root;
        Port = port;
    }

    private const string Script = """
        import logging, os, sys
        # FILECAT_FTP_LOG names a file for the server's command log (diagnostics); otherwise only warnings.
        log = os.environ.get("FILECAT_FTP_LOG")
        logging.basicConfig(level=logging.DEBUG if log else logging.WARNING, filename=log or None)
        from pyftpdlib.authorizers import DummyAuthorizer
        from pyftpdlib.servers import FTPServer
        root, user, password, cert = sys.argv[1], sys.argv[2], sys.argv[3], (sys.argv[4] if len(sys.argv) > 4 else "")
        implicit_tls = len(sys.argv) > 5 and sys.argv[5] == "implicit"
        auth = DummyAuthorizer()
        auth.add_user(user, password, root, perm="elradfmwMT")
        if cert:
            from pyftpdlib.handlers import TLS_FTPHandler
            if implicit_tls:
                class Handler(TLS_FTPHandler):
                    def on_connect(self):
                        # Wrap before pyftpdlib sends its 220 greeting: implicit FTPS begins with TLS.
                        self.secure_connection(self.get_ssl_context())
            else:
                Handler = TLS_FTPHandler
            Handler.certfile = cert
            Handler.tls_control_required = True
            Handler.tls_data_required = True
        else:
            from pyftpdlib.handlers import FTPHandler as Handler
        late_name = os.environ.get("FILECAT_FTP_MUTATE_RNFR")
        fail_name = os.environ.get("FILECAT_FTP_FAIL_AFTER_BACKUP_RNTO")
        publish_fail_name = os.environ.get("FILECAT_FTP_FAIL_AFTER_STAGE_RNTO")
        cleanup_fail_name = os.environ.get("FILECAT_FTP_FAIL_AFTER_BACKUP_DELE")
        if late_name or fail_name or publish_fail_name or cleanup_fail_name:
            BaseHandler = Handler
            class Handler(BaseHandler):
                mutated = False
                failed = False
                def ftp_RNFR(self, path):
                    # Change exactly the owned destination after the client's
                    # final content check but before the server moves it aside.
                    if (not Handler.mutated and os.path.basename(path) == late_name
                            and self.fs.validpath(path)):
                        original = os.stat(path)
                        with open(path, "wb") as item:
                            item.write(b"evil")
                        os.utime(path, ns=(original.st_atime_ns, original.st_mtime_ns))
                        Handler.mutated = True
                    return super().ftp_RNFR(path)
                def ftp_RNTO(self, path):
                    source = self._rnfr
                    if (fail_name and not Handler.failed and source and
                            os.path.basename(source) == fail_name and
                            os.path.basename(path).startswith(".fc-") and
                            self.fs.validpath(source) and self.fs.validpath(path)):
                        self._rnfr = None
                        self.run_as_current_user(self.fs.rename, source, path)
                        Handler.failed = True
                        self.respond("451 Rename result cannot be confirmed.")
                        return (source, path)
                    if (publish_fail_name and not Handler.failed and source and
                            os.path.basename(source).startswith(".fc-") and
                            os.path.basename(path) == publish_fail_name and
                            self.fs.validpath(source) and self.fs.validpath(path)):
                        self._rnfr = None
                        self.run_as_current_user(self.fs.rename, source, path)
                        Handler.failed = True
                        self.respond("451 Publication result cannot be confirmed.")
                        return (source, path)
                    return super().ftp_RNTO(path)
                def ftp_DELE(self, path):
                    if (cleanup_fail_name and not Handler.failed and
                            os.path.basename(path).startswith(".fc-") and
                            os.path.isfile(os.path.join(root, cleanup_fail_name)) and
                            self.fs.validpath(path)):
                        self.run_as_current_user(self.fs.remove, path)
                        Handler.failed = True
                        self.respond("451 Backup cleanup result cannot be confirmed.")
                        return path
                    return super().ftp_DELE(path)
        Handler.authorizer = auth
        Handler.use_gmt_times = True
        # FILECAT_FTP_ADVERTISE: the address PASV replies name (a server pointing data connections elsewhere);
        # FILECAT_FTP_NO_EPSV: EPSV refused, so a client falls back to PASV.
        if os.environ.get("FILECAT_FTP_ADVERTISE"):
            Handler.masquerade_address = os.environ["FILECAT_FTP_ADVERTISE"]
        if os.environ.get("FILECAT_FTP_NO_EPSV"):
            Handler.proto_cmds = {k: v for k, v in Handler.proto_cmds.items() if k != "EPSV"}
        server = FTPServer(("127.0.0.1", int(os.environ.get("FILECAT_FTP_PORT", "0"))), Handler)
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
    /// <param name="advertise">The address PASV replies name instead of the server's own, with EPSV refused.</param>
    public static TestFtpServer? TryStart(string? certificatePem = null, string? advertise = null, bool implicitTls = false,
        string? mutateOnRename = null, string? failAfterBackupRename = null, string? failAfterStagePublish = null,
        string? failAfterBackupCleanup = null)
    {
        if (implicitTls && certificatePem is null) throw new ArgumentException("Implicit TLS requires a test certificate.", nameof(certificatePem));
        if (mutateOnRename is not null && mutateOnRename != "late.bin")
            throw new ArgumentException("Only the owned late.bin fixture may be changed during rename.", nameof(mutateOnRename));
        if (failAfterBackupRename is not null && failAfterBackupRename != "ambiguous.bin")
            throw new ArgumentException("Only the owned ambiguous.bin fixture may receive an ambiguous backup rename result.", nameof(failAfterBackupRename));
        if (failAfterStagePublish is not null && failAfterStagePublish != "ambiguous.bin")
            throw new ArgumentException("Only the owned ambiguous.bin fixture may receive an ambiguous publication result.", nameof(failAfterStagePublish));
        if (failAfterBackupCleanup is not null && failAfterBackupCleanup != "ambiguous.bin")
            throw new ArgumentException("Only the owned ambiguous.bin fixture may receive an ambiguous cleanup result.", nameof(failAfterBackupCleanup));
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
            if (implicitTls) args += " implicit";
        }
        var start = new ProcessStartInfo(python, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        if (advertise is not null)
        {
            start.Environment["FILECAT_FTP_ADVERTISE"] = advertise;
            start.Environment["FILECAT_FTP_NO_EPSV"] = "1";
        }
        if (mutateOnRename is not null) start.Environment["FILECAT_FTP_MUTATE_RNFR"] = mutateOnRename;
        if (failAfterBackupRename is not null) start.Environment["FILECAT_FTP_FAIL_AFTER_BACKUP_RNTO"] = failAfterBackupRename;
        if (failAfterStagePublish is not null) start.Environment["FILECAT_FTP_FAIL_AFTER_STAGE_RNTO"] = failAfterStagePublish;
        if (failAfterBackupCleanup is not null) start.Environment["FILECAT_FTP_FAIL_AFTER_BACKUP_DELE"] = failAfterBackupCleanup;
        var launched = Launch(start);
        if (launched is null) return null;
        return new TestFtpServer(launched.Value.Process, start, root, launched.Value.Port);
    }

    private static (Process Process, int Port)? Launch(ProcessStartInfo start)
    {
        var process = Process.Start(start)!;
        var ready = process.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(30)) || ready.Result is not { } line || !line.StartsWith("READY ", StringComparison.Ordinal))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            process.Dispose();
            return null; // pyftpdlib without TLS support (pyOpenSSL) cannot run the TLS server
        }
        // The server logs to its output streams: unread, a full pipe buffer would stop it after a few hundred transfers.
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        return (process, int.Parse(line[6..]));
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
        Stop();
        _process.Dispose();
        try { Directory.Delete(Path.GetDirectoryName(Root)!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>Drop only this test-owned server connection, retaining its backing files for interruption checks.</summary>
    public void Stop()
    {
        try { _process.Kill(true); } catch (InvalidOperationException) { }
        _process.WaitForExit(10_000);
    }

    /// <summary>Restart the same test-owned server on its original port and root after a connection loss.</summary>
    public void Restart()
    {
        Stop();
        _start.Environment["FILECAT_FTP_PORT"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var launched = Launch(_start) ?? throw new IOException("The test FTP server did not restart.");
        if (launched.Port != Port)
        {
            launched.Process.Kill(true);
            launched.Process.Dispose();
            throw new IOException("The test FTP server restarted on a different port.");
        }
        var previous = _process;
        _process = launched.Process;
        previous.Dispose();
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

    [Fact]
    public void An_MLSD_server_cannot_hide_a_directory_link_from_the_delete_listing()
    {
        using var server = TestFtpServer.TryStart();
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        string real = Directory.CreateDirectory(Path.Combine(server.Root, "real")).FullName;
        string doomed = Directory.CreateDirectory(Path.Combine(server.Root, "doomed")).FullName;
        string kept = Path.Combine(real, "kept.txt");
        File.WriteAllText(kept, "keep this target");
        File.WriteAllText(Path.Combine(server.Root, ".hidden"), "keep hidden entries visible");
        try { Directory.CreateSymbolicLink(Path.Combine(doomed, "to-real"), real); }
        catch (Exception ex) when (OperatingSystem.IsWindows() && (ex is UnauthorizedAccessException or IOException))
        {
            Assert.Skip("This Windows runner cannot create a symbolic link for the FTP fixture.");
        }

        var profile = server.Profile(RemoteProtocols.Ftp);
        profile.PlainTextAccepted = true;
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using var lease = connections.Lease(profile.Id, TestContext.Current.CancellationToken);
        Assert.Contains(lease.Channel.List(".", TestContext.Current.CancellationToken), e => e.Name == ".hidden");
        var link = Assert.Single(lease.Channel.List("doomed", TestContext.Current.CancellationToken));
        Assert.Equal("to-real", link.Name);
        Assert.True(link.IsLink);
        link.Delete();
        Assert.Empty(lease.Channel.List("doomed", TestContext.Current.CancellationToken));
        Assert.Equal("keep this target", File.ReadAllText(kept));
    }

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
    public void Data_connections_go_to_the_server_whatever_address_its_PASV_reply_names()
    {
        // Release plan B05: a PASV reply names the address of the data connection. FluentFTP replaced only unroutable
        // ones, so a server could aim FileCat's uploads and downloads at any public host. This one has no EPSV and names a
        // documentation address no host answers on.
        using var server = TestFtpServer.TryStart(advertise: "203.0.113.7");
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using var lease = connections.Lease(profile.Id, ct);
        var clock = Stopwatch.StartNew();
        using (var s = lease.Channel.CreateNew("/up.txt")) s.Write("to the server only"u8);
        Assert.Equal("to the server only", File.ReadAllText(Path.Combine(server.Root, "up.txt")));
        Assert.Equal("up.txt", Assert.Single(lease.Channel.List("/", ct)).Name);
        using (var reader = new StreamReader(lease.Channel.OpenRead("/up.txt"))) Assert.Equal("to the server only", reader.ReadToEnd());
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"took {clock.Elapsed}: a data connection waited on the named address");
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

    [Fact]
    public void Names_FTP_can_carry_travel_exactly()
    {
        // Release issue I36: FluentFTP refused these legitimate names as "injection" (";", "|", tab, "%", "..", a
        // bidirectional mark); they are only names, and arrive as themselves.
        using var server = TestFtpServer.TryStart();
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        string[] names = ["semi;colon", "100%", "%20 not a space", "..leading dots", "gpj\u202Eexe.txt", "a+b=c", "-rf"];
        if (!OperatingSystem.IsWindows()) names = [.. names, "pipe|x", "tab\tx"]; // not valid in Windows names
        using var lease = connections.Lease(profile.Id, ct);
        foreach (string name in names)
        {
            using (var s = lease.Channel.CreateNew("/" + name)) s.Write(Encoding.UTF8.GetBytes(name));
            using var read = lease.Channel.OpenRead("/" + name);
            Assert.Equal(name, new StreamReader(read).ReadToEnd());
        }
        Assert.Equal(names.Order(StringComparer.Ordinal), lease.Channel.List("/", ct).Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(names.Order(StringComparer.Ordinal), Directory.GetFiles(server.Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_name_with_a_backslash_is_refused_rather_than_reaching_another_item()
    {
        // FluentFTP turns every backslash in a path into a folder separator: deleting "back\slash" would delete
        // "back/slash". FileCat refuses such names over FTP instead, and the other file stays.
        if (OperatingSystem.IsWindows()) Assert.Skip("A backslash cannot be part of a file name on Windows, where this server keeps its files.");
        using var server = TestFtpServer.TryStart();
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        File.WriteAllText(Path.Combine(server.Root, "back\\slash"), "the listed file");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(server.Root, "back")).FullName, "slash"), "another file");
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using var lease = connections.Lease(profile.Id, ct);
        var listed = Assert.Single(lease.Channel.List("/", ct), e => e.Name == "back\\slash");
        var refused = Assert.ThrowsAny<IOException>(listed.Delete);
        Assert.Contains("backslash", refused.Message);
        Assert.ThrowsAny<IOException>(() => lease.Channel.OpenRead("/back\\slash").Dispose());
        Assert.Equal("the listed file", File.ReadAllText(Path.Combine(server.Root, "back\\slash")));
        Assert.Equal("another file", File.ReadAllText(Path.Combine(server.Root, "back", "slash")));
    }
}
