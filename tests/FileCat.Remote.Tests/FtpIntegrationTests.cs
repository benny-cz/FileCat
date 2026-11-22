using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FileCat.Core.Permissions;
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
        # FILECAT_FTP_DATA_CERT: data connections present this certificate instead of the control connection's.
        data_cert = os.environ.get("FILECAT_FTP_DATA_CERT")
        if cert and data_cert:
            from pyftpdlib.handlers import TLS_DTPHandler
            class DataContextSource(Handler):
                certfile = data_cert
                keyfile = None
                ssl_context = None
            data_context = DataContextSource.get_ssl_context()
            class DataHandler(TLS_DTPHandler):
                def secure_connection(self, ssl_context):
                    return super().secure_connection(data_context)
            Handler.dtp_handler = DataHandler
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
        listing_count = int(os.environ.get("FILECAT_FTP_VIRTUAL_LISTING", "0"))
        # FILECAT_FTP_VIRTUAL_SELF: the virtual listings also carry the folder itself, its parent, and LIST's total line.
        virtual_self = bool(os.environ.get("FILECAT_FTP_VIRTUAL_SELF"))
        if listing_count:
            BaseHandler = Handler
            class Handler(BaseHandler):
                def ftp_MLSD(self, path):
                    head = b"type=cdir;modify=20260101000000; /\r\ntype=pdir;modify=20260101000000; ..\r\n" if virtual_self else b""
                    data = head + b"".join(f"type=file;size=1;modify=20260101000000; item{i:05}.txt\r\n".encode() for i in range(listing_count))
                    self.push_dtp_data(data, isproducer=False, cmd="MLSD")
                def ftp_LIST(self, path):
                    head = (f"total {listing_count}\r\ndrwxr-xr-x 2 user group 4096 Jan 01 2026 .\r\n"
                            "drwxr-xr-x 3 user group 4096 Jan 01 2026 ..\r\n").encode() if virtual_self else b""
                    data = head + b"".join(f"-rw-r--r-- 1 user group 1 Jan 01 2026 item{i:05}.txt\r\n".encode() for i in range(listing_count))
                    self.push_dtp_data(data, isproducer=False, cmd="LIST")
        # FILECAT_FTP_VIRTUAL_PADDED "count:length": listings of count lines for the folder itself, each padded to length
        # bytes with a fact of no meaning, which are no entries: they reach the line and wire limits, not the entry limit.
        padded = os.environ.get("FILECAT_FTP_VIRTUAL_PADDED")
        if padded:
            padded_count, padded_length = (int(part) for part in padded.split(":"))
            BaseHandler = Handler
            class Handler(BaseHandler):
                def padded_lines(self, prefix, suffix):
                    pad = b"x" * max(0, padded_length - len(prefix) - len(suffix) - 2)
                    return (prefix + pad + suffix + b"\r\n") * padded_count
                def ftp_MLSD(self, path):
                    self.push_dtp_data(self.padded_lines(b"type=cdir;modify=20260101000000;x.pad=", b"; /"), isproducer=False, cmd="MLSD")
                def ftp_LIST(self, path):
                    self.push_dtp_data(self.padded_lines(b"total 0 ", b""), isproducer=False, cmd="LIST")
        # FILECAT_FTP_EMPTY_550: an empty folder is refused as some servers do, "550 No files found."
        if os.environ.get("FILECAT_FTP_EMPTY_550"):
            BaseHandler = Handler
            class Handler(BaseHandler):
                def ftp_MLSD(self, path):
                    if os.path.isdir(path) and not os.listdir(path):
                        return self.respond("550 No files found.")
                    return super().ftp_MLSD(path)
                def ftp_LIST(self, path):
                    if os.path.isdir(path) and not os.listdir(path):
                        return self.respond("550 No files found.")
                    return super().ftp_LIST(path)
        # FILECAT_FTP_PWD: the working folder the server reports, as a server with an unusual path would.
        pwd_reply = os.environ.get("FILECAT_FTP_PWD")
        if pwd_reply:
            BaseHandler = Handler
            class Handler(BaseHandler):
                def ftp_PWD(self, line):
                    self.respond('257 "%s" is the current directory.' % pwd_reply.replace('"', '""'))
        Handler.authorizer = auth
        Handler.use_gmt_times = True
        # FILECAT_FTP_ADVERTISE: the address PASV replies name (a server pointing data connections elsewhere);
        # FILECAT_FTP_NO_EPSV: EPSV refused, so a client falls back to PASV.
        if os.environ.get("FILECAT_FTP_ADVERTISE"):
            Handler.masquerade_address = os.environ["FILECAT_FTP_ADVERTISE"]
        if os.environ.get("FILECAT_FTP_NO_EPSV"):
            Handler.proto_cmds = {k: v for k, v in Handler.proto_cmds.items() if k != "EPSV"}
        permission_listing = os.environ.get("FILECAT_FTP_PERMISSION_LISTING")
        if permission_listing == "list":
            BaseHandler = Handler
            class Handler(BaseHandler):
                proto_cmds = {k: v for k, v in BaseHandler.proto_cmds.items() if k not in ("MLST", "MLSD")}
                def pre_process_command(self, line, command, argument):
                    if command == "LIST" and argument.startswith("-a "):
                        argument = argument[3:]
                    return super().pre_process_command(line, command, argument)
        if permission_listing == "mlsd":
            BaseHandler = Handler
            class Handler(BaseHandler):
                def ftp_MLSD(self, path):
                    if os.path.realpath(path) != os.path.realpath(root):
                        return super().ftp_MLSD(path)
                    data = (
                        b"type=file;size=1;UNIX.mode=0000;UNIX.owner=42;UNIX.group=7;UNIX.ownername=server-user;"
                        b"UNIX.groupname=server-group;Unique=A2;Perm=R; zero.txt\r\n"
                        b"type=file;size=1;perm=; another unix.mode=0777;\r\n"
                        b"type=file;size=1;unix.mode=0780; malformed.txt\r\n"
                        b"type=file;size=1;unix.mode=0700; duplicate.txt\r\n"
                        b"type=file;size=1;unix.mode=0755; duplicate.txt\r\n"
                    )
                    self.push_dtp_data(data, isproducer=False, cmd="MLSD")
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
        string? failAfterBackupCleanup = null, int virtualListingCount = 0, bool virtualSelfLines = false, bool emptyFolders550 = false,
        (int Lines, int Length)? virtualPadding = null, string? permissionListing = null, string? dataCertificatePem = null,
        string? workingFolderReply = null)
    {
        if (dataCertificatePem is not null && certificatePem is null)
            throw new ArgumentException("A data certificate requires a control certificate.", nameof(dataCertificatePem));
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
        if (virtualListingCount > 0) start.Environment["FILECAT_FTP_VIRTUAL_LISTING"] = virtualListingCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (virtualSelfLines) start.Environment["FILECAT_FTP_VIRTUAL_SELF"] = "1";
        if (emptyFolders550) start.Environment["FILECAT_FTP_EMPTY_550"] = "1";
        if (virtualPadding is { } padding)
            start.Environment["FILECAT_FTP_VIRTUAL_PADDED"] = FormattableString.Invariant($"{padding.Lines}:{padding.Length}");
        if (permissionListing is not null)
        {
            start.Environment["FILECAT_FTP_PERMISSION_LISTING"] = permissionListing;
            start.Environment["FILECAT_FTP_LOG"] = Path.Combine(dir, "ftp-permissions.log");
        }
        if (dataCertificatePem is not null)
        {
            var dataCert = Path.Combine(dir, "data.pem");
            File.WriteAllText(dataCert, dataCertificatePem);
            start.Environment["FILECAT_FTP_DATA_CERT"] = dataCert;
            start.Environment["FILECAT_FTP_LOG"] = Path.Combine(dir, "ftp-data-certificate.log");
        }
        if (workingFolderReply is not null) start.Environment["FILECAT_FTP_PWD"] = workingFolderReply;
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
            try { process.Kill(); } catch (InvalidOperationException) { }
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
        // This fixture launches one Python server with no children. On macOS,
        // tree-wide Kill can wait in descendant discovery while a dropped TLS
        // connection is still being torn down, escaping the test's deadlines.
        // Terminate only the owned server PID and bound the wait explicitly.
        try { _process.Kill(); } catch (InvalidOperationException) { }
        if (!_process.WaitForExit(10_000))
            throw new TimeoutException($"The owned FTP test server {_process.Id} did not stop within 10 seconds.");
    }

    /// <summary>Restart the same test-owned server on its original port and root after a connection loss.</summary>
    public void Restart()
    {
        Stop();
        _start.Environment["FILECAT_FTP_PORT"] = Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var launched = Launch(_start) ?? throw new IOException("The test FTP server did not restart.");
        if (launched.Port != Port)
        {
            launched.Process.Kill();
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

    [Theory]
    [InlineData(RemoteProtocols.Ftp)]
    [InlineData(RemoteProtocols.FtpExplicitTls)]
    [InlineData(RemoteProtocols.FtpImplicitTls)]
    public void Permission_inspection_reads_exact_MLSD_facts_without_creating_write_authority(string protocol)
    {
        string? pem = protocol == RemoteProtocols.Ftp ? null : TestFtpServer.SelfSigned("FileCat permissions").Pem;
        using var server = TestFtpServer.TryStart(pem, implicitTls: protocol == RemoteProtocols.FtpImplicitTls, permissionListing: "mlsd");
        if (server is null) Assert.Skip("Requires the owned pyftpdlib FTP or FTPS test server.");
        foreach (string name in new[] { "zero.txt", "another unix.mode=0777;", "malformed.txt", "duplicate.txt" })
            File.WriteAllText(Path.Combine(server.Root, name), "a");
        var profile = server.Profile(protocol);
        var interaction = new FtpInteraction { AllowPlain = true, CertificateAnswer = HostKeyDecision.AcceptOnce };
        using var connections = Connections(profile, interaction);
        var provider = new SftpProvider(connections, () => [profile], value => value);
        var location = SftpProvider.At(profile, "/");
        var authority = new PermissionAuthority(PermissionAuthorityKind.RemoteSession, profile.Id, 0);
        var names = new[] { "zero.txt", "another unix.mode=0777;", "malformed.txt", "duplicate.txt", "missing.txt", "bad\r\nname" };
        PermissionSubject[] subjects = names.Select(name => new PermissionSubject(location, name, name, authority,
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow, ListedSize: 1)).ToArray();
        var result = provider.InspectFtpPermissions(location, subjects, TestContext.Current.CancellationToken);
        Assert.True(result[0].Mode.Value.HasValue, result[0].BindingProblem ?? result[0].Mode.Reason);
        Assert.Equal((ushort)0, result[0].Mode.Value);
        Assert.Equal("42", result[0].Owner.Value?.Id);
        Assert.Equal("7", result[0].Group.Value?.Id);
        Assert.Equal("server-user", result[0].Ftp?.OwnerName);
        Assert.Equal("server-group", result[0].Ftp?.GroupName);
        Assert.Equal("A2", result[0].Ftp?.Unique);
        Assert.Equal("R", result[0].Ftp?.TransferRights);
        Assert.Equal(protocol, result[0].Ftp?.Protocol);
        Assert.Equal("MLSD", result[0].Ftp?.Source);
        Assert.Equal("/zero.txt", result[0].Subject.DisplayPath);
        Assert.Null(result[0].Binding);
        Assert.DoesNotContain(result[0].Capabilities, c => c.Action == PermissionAction.Change && c.Availability == PermissionAvailability.Available);
        Assert.Null(result[1].Mode.Value);
        Assert.Equal("", result[1].Ftp?.TransferRights);
        Assert.Equal(PermissionReadStatus.Incomplete, result[2].Mode.Status);
        Assert.Equal(PermissionReadStatus.Stale, result[3].Mode.Status);
        Assert.Equal(PermissionReadStatus.Gone, result[4].Mode.Status);
        Assert.Equal(PermissionReadStatus.Unsupported, result[5].Mode.Status);
        Assert.Equal("a", File.ReadAllText(Path.Combine(server.Root, "zero.txt")));
        string commands = File.ReadAllText(Path.Combine(Path.GetDirectoryName(server.Root)!, "ftp-permissions.log"));
        Assert.Contains("MLSD /", commands);
        Assert.DoesNotContain("SITE CHMOD", commands, StringComparison.OrdinalIgnoreCase);
        if (protocol == RemoteProtocols.Ftp) Assert.Equal(1, interaction.PlainQuestions);
        else Assert.Single(interaction.CertificateQuestions);
    }

    [Fact]
    public void Permission_inspection_reports_Unix_LIST_provenance_without_MLSD()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Requires the owned Unix LIST fixture.");
        using var server = TestFtpServer.TryStart(permissionListing: "list");
        if (server is null) Assert.Skip("Requires the owned pyftpdlib FTP test server.");
        string path = Path.Combine(server.Root, "file.txt");
        File.WriteAllText(path, "a");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        var provider = new SftpProvider(connections, () => [profile], value => value);
        var location = SftpProvider.At(profile, "/");
        var subject = new PermissionSubject(location, "file.txt", "file.txt", new(PermissionAuthorityKind.RemoteSession, profile.Id, 0),
            PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);
        var observation = Assert.Single(provider.InspectFtpPermissions(location, [subject], TestContext.Current.CancellationToken));
        Assert.True(observation.Mode.Value.HasValue, observation.BindingProblem ?? observation.Mode.Reason);
        Assert.Equal("Unix LIST", observation.Mode.Source);
        Assert.Equal((ushort)0x1a0, observation.Mode.Value);
        Assert.Null(observation.Ftp?.TransferRights);
        Assert.Null(observation.Owner.Value);
    }

    [Fact]
    public void Missing_FTPS_transfer_reply_retires_the_control_connection_before_retry()
    {
        using var server = TestFtpServer.TryStart(TestFtpServer.SelfSigned("FileCat missing FTPS reply").Pem);
        if (server is null) Assert.Skip("The owned FTPS test server is unavailable.");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.FtpExplicitTls);
        using var connections = Connections(profile, new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce });
        FtpChannel retired;
        using (var lease = connections.Lease(profile.Id, ct))
        {
            retired = Assert.IsType<FtpChannel>(lease.Channel);
            // The control socket remains open, but a server with no final reply sends nothing here.
            var elapsed = Stopwatch.StartNew();
            Assert.Null(retired.ReplyAfterBreak());
            Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(15), $"The missing reply took {elapsed.Elapsed}.");
            Assert.False(retired.IsConnected);
        }

        using var retry = connections.Lease(profile.Id, ct);
        Assert.NotSame(retired, retry.Channel);
        Assert.Empty(retry.Channel.List("/", ct));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Owned_FTP_and_FTPS_listings_stop_at_the_protocol_budget(bool tls)
    {
        string? pem = tls ? TestFtpServer.SelfSigned("FileCat listing budget").Pem : null;
        string protocol = tls ? RemoteProtocols.FtpExplicitTls : RemoteProtocols.Ftp;
        var interaction = new FtpInteraction { AllowPlain = true, CertificateAnswer = HostKeyDecision.AcceptOnce };
        var ct = TestContext.Current.CancellationToken;
        using (var normal = TestFtpServer.TryStart(pem, virtualListingCount: 3))
        {
            if (normal is null) Assert.Skip("The owned FTP/FTPS test server is unavailable.");
            var profile = normal.Profile(protocol);
            using var connections = Connections(profile, interaction);
            using var lease = connections.Lease(profile.Id, ct);
            Assert.Equal(3, lease.Channel.List("/", ct).Count);
        }
        using (var flood = TestFtpServer.TryStart(pem, virtualListingCount: RemoteListingBudget.MaxEntries + 1))
        {
            if (flood is null) Assert.Skip("The owned FTP/FTPS test server is unavailable.");
            var profile = flood.Profile(protocol);
            using var connections = Connections(profile, interaction);
            using var lease = connections.Lease(profile.Id, ct);
            // A job's listing takes the whole folder (review finding R-M9); a folder shown stops at its limit.
            Assert.Equal(RemoteListingBudget.MaxEntries + 1, lease.Channel.ListAll("/", ct).Count);
            long before = Process.GetCurrentProcess().WorkingSet64;
            Assert.Throws<RemoteListingLimitException>(() => lease.Channel.List("/", ct));
            long peak = Process.GetCurrentProcess().PeakWorkingSet64;
            TestContext.Current.TestOutputHelper?.WriteLine($"{protocol}: limit={RemoteListingBudget.MaxEntries}, working-set-before={before}, process-peak={peak}");
        }
    }

    [Fact]
    public void A_listing_skips_the_folder_itself_its_parent_and_its_total_line()
    {
        // Review finding R-M10: MLSD's cdir and pdir entries were kept, and with LIST's "total", "." and ".." they counted
        // toward the limit, so a folder holding exactly the limit was refused.
        using var server = TestFtpServer.TryStart(virtualListingCount: RemoteListingBudget.MaxEntries, virtualSelfLines: true);
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using var lease = connections.Lease(profile.Id, ct);
        var listed = lease.Channel.List("/", ct);
        Assert.Equal(RemoteListingBudget.MaxEntries, listed.Count);
        Assert.All(listed, e => Assert.StartsWith("item", e.Name, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, RemoteListingBudget.MaxLineBytes + 2)]
    [InlineData(300, 60_000)]
    public void A_listing_past_its_line_or_wire_limit_is_refused(int lines, int length)
    {
        // A Low of the review of d1757bca: the tests of AC-10 reached the entry limit, but no line or listing that passes
        // the 64 KiB line limit or the 16 MiB wire limit. Here one line just past the first, then 300 lines of 60,000 bytes
        // (18 MB, within the line limit and the limit of lines that are no entries) past the second.
        using var server = TestFtpServer.TryStart(virtualPadding: (lines, length));
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using var lease = connections.Lease(profile.Id, ct);
        Assert.Throws<RemoteListingLimitException>(() => lease.Channel.List("/", ct));
    }

    [Fact]
    public void A_padded_listing_within_its_limits_lists_nothing_it_does_not_hold()
    {
        // The same lines within both limits are the folder itself, which no listing holds as an entry.
        using var server = TestFtpServer.TryStart(virtualPadding: (100, 60_000));
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using var lease = connections.Lease(profile.Id, ct);
        Assert.Empty(lease.Channel.List("/", ct));
    }

    [Fact]
    public void A_listing_stopped_before_its_final_reply_closes_its_connection()
    {
        // Review finding R-M10: a listing cancelled or failed mid-transfer left its 226 or 426 reply unread, and the
        // connection went back to the pool, where the next command would have taken that reply for its own.
        using var server = TestFtpServer.TryStart(virtualListingCount: 20_000);
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using (var lease = connections.Lease(profile.Id, ct))
        {
            Assert.ThrowsAny<OperationCanceledException>(() => lease.Channel.List("/", new CancellationToken(canceled: true)));
            Assert.False(lease.Channel.IsConnected);
        }
        using var again = connections.Lease(profile.Id, ct);
        Assert.Equal(20_000, again.Channel.List("/", ct).Count);
    }

    [Fact]
    public void A_server_that_refuses_to_list_an_empty_folder_lists_it_empty()
    {
        // Review finding R-M10: "550 No files found" for an empty folder became an error; a missing folder still is one.
        using var server = TestFtpServer.TryStart(emptyFolders550: true);
        if (server is null) Assert.Skip("No FTP test server here (pip install pyftpdlib, or set FILECAT_PYTHON).");
        Directory.CreateDirectory(Path.Combine(server.Root, "empty"));
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(server.Root, "full")).FullName, "a.txt"), "a");
        var ct = TestContext.Current.CancellationToken;
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        using var lease = connections.Lease(profile.Id, ct);
        Assert.Empty(lease.Channel.List("/empty", ct));
        Assert.Equal(["a.txt"], lease.Channel.List("/full", ct).Select(e => e.Name));
        Assert.ThrowsAny<IOException>(() => lease.Channel.List("/missing", ct));
        Assert.True(lease.Channel.IsConnected);
    }

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

    // G-FTP, an ambiguous server folder path: a folder FTP cannot name unambiguously (relative, a doubled separator, or a
    // "." or ".." segment) is refused before any listing, including the working folder a server reports for "~".
    [Theory]
    [InlineData("~", "/srv/./ftp")]
    [InlineData("relative", null)]
    [InlineData("/srv//ftp", null)]
    [InlineData("/srv/./ftp", null)]
    [InlineData("/srv/../ftp", null)]
    public void Permission_inspection_refuses_an_ambiguous_server_folder_before_listing(string folder, string? workingFolder)
    {
        using var server = TestFtpServer.TryStart(permissionListing: "mlsd", workingFolderReply: workingFolder);
        if (server is null) Assert.Skip("Requires the owned pyftpdlib FTP test server.");
        File.WriteAllText(Path.Combine(server.Root, "zero.txt"), "a");
        var profile = server.Profile(RemoteProtocols.Ftp);
        using var connections = Connections(profile, new FtpInteraction { AllowPlain = true });
        var provider = new SftpProvider(connections, () => [profile], value => value);
        var location = SftpProvider.At(profile, folder);
        var authority = new PermissionAuthority(PermissionAuthorityKind.RemoteSession, profile.Id, 0);
        var subject = new PermissionSubject(location, "zero.txt", "zero.txt", authority, PermissionObjectKind.File,
            PermissionLinkPolicy.NoFollow, ListedSize: 1);
        var result = Assert.Single(provider.InspectFtpPermissions(location, [subject], TestContext.Current.CancellationToken));
        Assert.Equal(PermissionReadStatus.Failed, result.Mode.Status);
        Assert.Contains("unambiguous absolute path", result.BindingProblem);
        string commands = File.ReadAllText(Path.Combine(Path.GetDirectoryName(server.Root)!, "ftp-permissions.log"));
        Assert.DoesNotContain("MLSD", commands);
        Assert.DoesNotContain("LIST", commands);
    }

    // G-FTP, a certificate change during an FTPS session: the control connection presents the pinned certificate, but the
    // server's data connections present another one. Every data connection of a session must present the certificate the
    // session was accepted with, so the listing and the download are refused without a question.
    [Fact]
    public void FTPS_refuses_a_data_connection_presenting_another_certificate_within_the_session()
    {
        var (pem, sha) = TestFtpServer.SelfSigned("FileCat control");
        var (data, _) = TestFtpServer.SelfSigned("FileCat data");
        var ct = TestContext.Current.CancellationToken;
        using var server = TestFtpServer.TryStart(pem, dataCertificatePem: data);
        if (server is null) Assert.Skip("No FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
        File.WriteAllText(Path.Combine(server.Root, "present.txt"), "server content");
        var profile = server.Profile(RemoteProtocols.FtpExplicitTls);
        File.WriteAllText(Path.Combine(_state, "trusted_certificates"), $"127.0.0.1:{server.Port} {sha}\n");
        var interaction = new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce };
        using var connections = Connections(profile, interaction);
        using var lease = connections.Lease(profile.Id, ct);
        var listing = Assert.Throws<CertificateRejectedException>(() => lease.Channel.List("/", ct));
        Assert.Contains("different certificate", listing.Message);
        Assert.Throws<CertificateRejectedException>(() =>
        {
            using var read = lease.Channel.OpenRead("/present.txt");
            read.ReadByte();
        });
        Assert.Empty(interaction.CertificateQuestions);
    }

    // A session whose certificate was accepted once does not ask again for each of its data connections.
    [Fact]
    public void FTPS_session_accepted_once_lists_and_reads_without_asking_again()
    {
        var (pem, _) = TestFtpServer.SelfSigned("FileCat accepted once");
        var ct = TestContext.Current.CancellationToken;
        using var server = TestFtpServer.TryStart(pem);
        if (server is null) Assert.Skip("No FTPS test server here (pip install pyftpdlib pyopenssl, or set FILECAT_PYTHON).");
        File.WriteAllText(Path.Combine(server.Root, "present.txt"), "server content");
        var profile = server.Profile(RemoteProtocols.FtpExplicitTls);
        var interaction = new FtpInteraction { CertificateAnswer = HostKeyDecision.AcceptOnce };
        using var connections = Connections(profile, interaction);
        using var lease = connections.Lease(profile.Id, ct);
        for (int i = 0; i < 3; i++) Assert.Single(lease.Channel.List("/", ct));
        using (var read = lease.Channel.OpenRead("/present.txt"))
        using (var reader = new StreamReader(read)) Assert.Equal("server content", reader.ReadToEnd());
        Assert.Single(interaction.CertificateQuestions);
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
