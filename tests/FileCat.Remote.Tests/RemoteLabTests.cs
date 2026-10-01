using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// Release plan V08 against real servers of another machine (in the campaign: the lent Ubuntu VM with OpenSSH, vsftpd for
/// FTP with explicit TLS and a second vsftpd for implicit TLS, one self-signed certificate): trust on first use and a
/// changed host key, a certificate pinned once and asked about again when it changes, consent before a password goes
/// unencrypted, and trees that go up and come back byte for byte through jobs, checked against the server's own reading
/// of them. Throwaway credentials only: FILECAT_REMOTE_LAB (the host), FILECAT_REMOTE_LAB_USER, _PASSWORD.
/// </summary>
public sealed class RemoteLabTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fc-remote-lab-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static (string Host, string User, string Password) Lab()
    {
        string? host = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB");
        string? user = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB_USER");
        string? password = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB_PASSWORD");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(user) || password is null)
            Assert.Skip("Set FILECAT_REMOTE_LAB, FILECAT_REMOTE_LAB_USER and FILECAT_REMOTE_LAB_PASSWORD to a disposable test server.");
        return (host, user, password);
    }

    /// <summary>The user's answers, recorded: what FileCat asked, and what it was told.</summary>
    private sealed class Interaction(string password) : IRemoteInteraction
    {
        public readonly List<HostKeyCheck> HostKeys = [];
        public readonly List<CertificateCheck> Certificates = [];
        public int UnencryptedAsked;
        public HostKeyDecision HostKeyAnswer = HostKeyDecision.AcceptAndRemember;
        public HostKeyDecision CertificateAnswer = HostKeyDecision.AcceptAndRemember;

        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check)
        {
            lock (HostKeys) HostKeys.Add(check);
            return HostKeyAnswer;
        }

        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => new(password, false);

        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) =>
            prompts.Select(_ => password).ToList();

        public HostKeyDecision DecideCertificate(RemoteProfile profile, CertificateCheck check)
        {
            lock (Certificates) Certificates.Add(check);
            return CertificateAnswer;
        }

        public bool AllowUnencrypted(RemoteProfile profile)
        {
            Interlocked.Increment(ref UnencryptedAsked);
            return false; // the user declines
        }
    }

    /// <summary>
    /// The portable file operations, recording the origin marks FileCat asks for (the Windows and Unix operations write
    /// them as Zone.Identifier and extended attributes; their writing is tested with them).
    /// </summary>
    private sealed class LabOps : PortableFileOperations
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Marks = new();

        public override bool WriteOriginMark(string path, string mark)
        {
            Marks.Enqueue(mark);
            return true;
        }
    }

    private sealed class Stack : IDisposable
    {
        public required SftpConnections Connections { get; init; }
        public required JobManager Jobs { get; init; }
        public required RemoteProfile Profile { get; init; }
        public required LabOps Files { get; init; }

        public void Dispose() => Connections.Dispose();
    }

    /// <summary>FileCat's remote stack as the app builds it, with its trust files in <paramref name="state"/>.</summary>
    private static Stack Open(RemoteProfile profile, Interaction interaction, string state)
    {
        Directory.CreateDirectory(state);
        var connections = new SftpConnections(id => id == profile.Id ? profile : null, new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(state, "known_hosts")), new SessionSecretStore(), interaction,
            new CertificateTrust(Path.Combine(state, "trusted_certificates")));
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new SftpProvider(connections, () => [profile], p => p));
        SftpJobs.Register();
        var files = new LabOps();
        return new Stack { Connections = connections, Profile = profile, Files = files, Jobs = new JobManager(files, providers, Path.Combine(state, "journal")) };
    }

    /// <summary>
    /// The lab's port for a protocol: the case's own (OpenSSH, vsftpd), or one FILECAT_REMOTE_LAB_PORTS names, so the same
    /// cases run against a second implementation ("sftp=2222,ftpes=2121,ftps=2990,ftp=2121" for the lab's ProFTPD).
    /// </summary>
    private static int LabPort(string protocol, int port)
    {
        foreach (string pair in (Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB_PORTS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (pair.Split('=') is [var name, var value] && name.Trim() == protocol && int.TryParse(value, out int p)) return p;
        return port;
    }

    private static RemoteProfile Profile(string protocol, int port, (string Host, string User, string Password) lab) => new()
    {
        Name = "lab " + protocol,
        Host = lab.Host,
        Port = LabPort(protocol, port),
        User = lab.User,
        Protocol = protocol,
        Temporary = true,
    };

    private static async Task<Job> RunAsync(Stack stack, JobRequest request)
    {
        var job = stack.Jobs.Submit(request);
        var deadline = DateTime.UtcNow.AddMinutes(5);
        while (!job.State.IsFinished())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException(job.State.ToString());
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
        return job;
    }

    [Fact]
    public void Sftp_trusts_a_host_key_on_first_use_and_refuses_a_changed_one()
    {
        var lab = Lab();
        var ct = TestContext.Current.CancellationToken;
        string state = Path.Combine(_dir, "sftp-trust");
        var interaction = new Interaction(lab.Password);
        var profile = Profile(RemoteProtocols.Sftp, 22, lab);

        // First use: FileCat asks, the user trusts and remembers.
        using (var stack = Open(profile, interaction, state))
        using (var lease = stack.Connections.Lease(profile.Id, ct))
            Assert.NotEmpty(lease.Channel.List(".", ct));
        var first = Assert.Single(interaction.HostKeys);
        Assert.Equal(HostKeyStatus.Unknown, first.Status);

        // Remembered: no question the second time.
        using (var stack = Open(profile, interaction, state))
        using (var lease = stack.Connections.Lease(profile.Id, ct))
            Assert.NotEmpty(lease.Channel.List(".", ct));
        Assert.Single(interaction.HostKeys);

        // The remembered key replaced by another (as if the server changed or an impostor answered): FileCat says so,
        // and a refusal stops the connection before any password is sent.
        string knownHosts = Path.Combine(state, "known_hosts");
        string line = File.ReadAllLines(knownHosts).Single(l => l.Length > 0 && !l.StartsWith('#'));
        var fields = line.Split(' ');
        var forged = RandomNumberGenerator.GetBytes(32);
        byte[] blob = [.. new byte[] { 0, 0, 0, 11 }, .. "ssh-ed25519"u8, .. new byte[] { 0, 0, 0, 32 }, .. forged];
        File.WriteAllText(knownHosts, $"{fields[0]} ssh-ed25519 {Convert.ToBase64String(blob)}\n");
        interaction.HostKeyAnswer = HostKeyDecision.Reject;
        using (var stack = Open(profile, interaction, state))
            Assert.ThrowsAny<Exception>(() => stack.Connections.Lease(profile.Id, ct));
        var changed = interaction.HostKeys.Last();
        Assert.True(changed.Status is HostKeyStatus.Changed or HostKeyStatus.Unknown, $"{changed.Status}");
        if (fields[1] == "ssh-ed25519") Assert.Equal(HostKeyStatus.Changed, changed.Status);
    }

    [Theory]
    [InlineData(RemoteProtocols.FtpExplicitTls, 21)]
    [InlineData(RemoteProtocols.FtpImplicitTls, 990)]
    public void Ftps_asks_about_a_self_signed_certificate_once_and_pins_it(string protocol, int port)
    {
        var lab = Lab();
        var ct = TestContext.Current.CancellationToken;
        string state = Path.Combine(_dir, "certs-" + protocol);
        var interaction = new Interaction(lab.Password);
        var profile = Profile(protocol, port, lab);

        // Refused: nothing is listed, and nothing is remembered.
        interaction.CertificateAnswer = HostKeyDecision.Reject;
        using (var stack = Open(profile, interaction, state))
            Assert.ThrowsAny<Exception>(() => stack.Connections.Lease(profile.Id, ct));
        Assert.Equal(CertificateStatus.Unknown, Assert.Single(interaction.Certificates).Status);

        // Trusted and remembered: listed; the next connection does not ask.
        interaction.CertificateAnswer = HostKeyDecision.AcceptAndRemember;
        using (var stack = Open(profile, interaction, state))
        using (var lease = stack.Connections.Lease(profile.Id, ct))
            lease.Channel.List(".", ct);
        Assert.Equal(2, interaction.Certificates.Count);
        using (var stack = Open(profile, interaction, state))
        using (var lease = stack.Connections.Lease(profile.Id, ct))
            lease.Channel.List(".", ct);
        Assert.Equal(2, interaction.Certificates.Count);

        // A different certificate than the pinned one: asked about again, as changed.
        string pins = Path.Combine(state, "trusted_certificates");
        File.WriteAllText(pins, System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(pins), "[0-9A-Fa-f]{64}", new string('0', 64)));
        interaction.CertificateAnswer = HostKeyDecision.Reject;
        using (var stack = Open(profile, interaction, state))
            Assert.ThrowsAny<Exception>(() => stack.Connections.Lease(profile.Id, ct));
        Assert.Equal(CertificateStatus.Changed, interaction.Certificates.Last().Status);
    }

    [Fact]
    public void Plain_ftp_sends_no_password_without_consent()
    {
        var lab = Lab();
        var ct = TestContext.Current.CancellationToken;
        var interaction = new Interaction(lab.Password);
        var profile = Profile(RemoteProtocols.Ftp, 21, lab);
        using (var stack = Open(profile, interaction, Path.Combine(_dir, "plain")))
            Assert.ThrowsAny<Exception>(() => stack.Connections.Lease(profile.Id, ct));
        Assert.Equal(1, interaction.UnencryptedAsked);
    }

    [Theory]
    [InlineData(RemoteProtocols.Sftp, 22)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 21)]
    public async Task A_cancelled_upload_leaves_nothing_under_its_name(string protocol, int port)
    {
        var lab = Lab();
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile(protocol, port, lab);
        using var stack = Open(profile, new Interaction(lab.Password), Path.Combine(_dir, "cancel-" + protocol));
        string file = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "cancel-src-" + protocol)).FullName, "big.bin");
        var bytes = new byte[256 << 20];
        new Random(3).NextBytes(bytes);
        File.WriteAllBytes(file, bytes);
        string remoteRoot = "filecat-lab-" + Guid.NewGuid().ToString("N")[..8];
        using (var lease = stack.Connections.Lease(profile.Id, ct)) lease.Channel.CreateDirectory(remoteRoot);
        try
        {
            var job = stack.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
                Destination = SftpProvider.At(profile, remoteRoot),
            });
            // Cancelled a fifth of the way in.
            while (!job.State.IsFinished() && job.BytesDone < bytes.Length / 5) await Task.Delay(10, ct);
            Assert.False(job.State.IsFinished(), "The upload finished before it could be cancelled.");
            job.Cancel();
            while (!job.State.IsFinished()) await Task.Delay(20, ct);
            Assert.Equal(JobState.Canceled, job.State);
            // Nothing under the file's name, and no partial copy under another.
            using var check = stack.Connections.Lease(profile.Id, ct);
            var left = check.Channel.List(remoteRoot, ct).Select(e => $"{e.Name} ({e.Size} bytes)").ToList();
            Assert.True(left.Count == 0, "Left on the server: " + string.Join(", ", left));
        }
        finally
        {
            await RunAsync(stack, new JobRequest { Kind = JobKind.Delete, Sources = [new ItemRef(SftpProvider.At(profile, "."), remoteRoot, EntryKind.Directory)] });
        }
    }

    [Theory]
    [InlineData(RemoteProtocols.Sftp, 22)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 21)]
    public async Task An_upload_cut_off_by_the_server_continues_and_arrives_intact(string protocol, int port)
    {
        var lab = Lab();
        // A command run on this machine that drops the test account's connections on the server (a server restart, a
        // network cut); the lab's command kills the account's session processes.
        string? drop = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB_DROP");
        if (string.IsNullOrEmpty(drop)) Assert.Skip("Set FILECAT_REMOTE_LAB_DROP to a command that drops the test account's connections.");
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile(protocol, port, lab);
        using var stack = Open(profile, new Interaction(lab.Password), Path.Combine(_dir, "drop-" + protocol));
        string file = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "drop-src-" + protocol)).FullName, "big.bin");
        var bytes = new byte[128 << 20];
        new Random(5).NextBytes(bytes);
        File.WriteAllBytes(file, bytes);
        string remoteRoot = "filecat-lab-" + Guid.NewGuid().ToString("N")[..8];
        using (var lease = stack.Connections.Lease(profile.Id, ct)) lease.Channel.CreateDirectory(remoteRoot);
        // Whatever FileCat asks after the break (a connection error), the user says: try again.
        var asked = new List<string>();
        stack.Jobs.DecisionRequested += d =>
        {
            lock (asked) asked.Add(d.Request.Title + ": " + d.Request.Message);
            d.Resolve(new Decision(DecisionAction.Retry));
        };
        try
        {
            var job = stack.Jobs.Submit(new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
                Destination = SftpProvider.At(profile, remoteRoot),
                Options = new TransferOptions { RateLimit = 16 << 20 }, // about eight seconds: time to cut it
            });
            while (!job.State.IsFinished() && job.BytesDone < bytes.Length * 3L / 10) await Task.Delay(20, ct);
            Assert.False(job.State.IsFinished(), "The upload finished before the connection could be cut.");
            using (var cut = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + drop) { UseShellExecute = false }))
                await cut!.WaitForExitAsync(ct);
            var deadline = DateTime.UtcNow.AddMinutes(5);
            while (!job.State.IsFinished() && DateTime.UtcNow < deadline) await Task.Delay(50, ct);
            string story = $"{job.State}; asked: {string.Join(" | ", asked)}; issues: {string.Join(" | ", job.Issues.Select(i => i.Message))}";
            Assert.True(job.State == JobState.Completed, story);
            // It continued after the part on the server was checked, rather than starting again.
            Assert.Contains(job.Issues, i => i.Message.Contains("continued at", StringComparison.Ordinal));
            // Intact on the server, read back through a fresh connection; nothing else left beside it.
            using var check = stack.Connections.Lease(profile.Id, ct);
            var entries = check.Channel.List(remoteRoot, ct);
            Assert.Equal(["big.bin"], entries.Select(e => e.Name));
            using (var s = check.Channel.OpenRead(RemotePath.Combine(remoteRoot, "big.bin")))
                Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), Convert.ToHexString(SHA256.HashData(s)));
            TestContext.Current.TestOutputHelper?.WriteLine(story);
        }
        finally
        {
            await RunAsync(stack, new JobRequest { Kind = JobKind.Delete, Sources = [new ItemRef(SftpProvider.At(profile, "."), remoteRoot, EntryKind.Directory)] });
        }
    }

    /// <summary>
    /// Names that shells, option parsers, URL encoders and Unicode normalizers treat specially; valid on every client.
    /// </summary>
    private static readonly string[] OddNames =
    [
        "-rf", "--help", " leading space", "two  spaces", "#hash", "100%", "%20 not a space", "semi;colon", "quote's",
        "brackets [1]", "a+b=c", "Žluťoučký kůň.txt", "emoji 😀.txt", "..leading dots", "e\u0301 decomposed.txt", "\u00e9 composed.txt",
        "ｆｕｌｌｗｉｄｔｈ.txt", "日本語.txt",
    ];

    [Theory]
    [InlineData(RemoteProtocols.Sftp, 22)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 21)]
    public async Task Odd_names_arrive_on_the_server_exactly_and_come_back_the_same(string protocol, int port)
    {
        // Release plan V08: no quoting, option parsing, encoding or normalization changes a name on the way, checked
        // against the bytes of the names on the server's own file system (FILECAT_REMOTE_LAB_SERVER_NAMES), not through
        // FileCat's reading of the server.
        var lab = Lab();
        string? names = Environment.GetEnvironmentVariable("FILECAT_REMOTE_LAB_SERVER_NAMES");
        if (string.IsNullOrEmpty(names)) Assert.Skip("Set FILECAT_REMOTE_LAB_SERVER_NAMES to a command that lists a server folder's names as bytes.");
        var ct = TestContext.Current.CancellationToken;
        var profile = Profile(protocol, port, lab);
        using var stack = Open(profile, new Interaction(lab.Password), Path.Combine(_dir, "names-" + protocol));
        // Anything FileCat asks is recorded and skipped, so the result says which name failed and why.
        var asked = new List<string>();
        stack.Jobs.DecisionRequested += q =>
        {
            lock (asked) asked.Add(q.Request.Title + ": " + q.Request.Message);
            q.Resolve(new Decision(DecisionAction.Skip));
        };
        string local = Directory.CreateDirectory(Path.Combine(_dir, "names-src-" + protocol, "Odd names")).FullName;
        foreach (string name in OddNames) File.WriteAllText(Path.Combine(local, name), name);
        string remoteRoot = "filecat-lab-" + Guid.NewGuid().ToString("N")[..8];
        string home;
        using (var lease = stack.Connections.Lease(profile.Id, ct))
        {
            lease.Channel.CreateDirectory(remoteRoot);
            home = lease.Channel.HomeDirectory;
        }
        try
        {
            var up = await RunAsync(stack, new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(local, EntryKind.Directory)],
                Destination = SftpProvider.At(profile, remoteRoot),
            });
            Assert.True(up.State == JobState.Completed, $"{up.State}; asked: {string.Join(" | ", asked)}; issues: {string.Join("; ", up.Issues.Select(i => $"{i.Path}: {i.Message}"))}");

            // The server's file system: exactly these names, as UTF-8 bytes, each file with its own content's size.
            string listing = Path.Combine(_dir, "names-" + protocol + ".txt");
            using (var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                       "/c " + names.Replace("{root}", RemotePath.Combine(RemotePath.Combine(home, remoteRoot), "Odd names"), StringComparison.Ordinal).Replace("{out}", listing, StringComparison.Ordinal))
                   { UseShellExecute = false, CreateNoWindow = true })!)
                await p.WaitForExitAsync(ct);
            var onServer = File.ReadAllLines(listing).Select(l => l.Split(' ')).Where(f => f.Length == 3 && f[0] == "f")
                .ToDictionary(f => System.Text.Encoding.UTF8.GetString(Convert.FromHexString(f[1])), f => long.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(OddNames.Order(StringComparer.Ordinal), onServer.Keys.Order(StringComparer.Ordinal));
            Assert.All(OddNames, n => Assert.Equal(System.Text.Encoding.UTF8.GetByteCount(n), onServer[n]));

            // And back: the same names, the same contents.
            string back = Directory.CreateDirectory(Path.Combine(_dir, "names-back-" + protocol)).FullName;
            var down = await RunAsync(stack, new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [new ItemRef(SftpProvider.At(profile, remoteRoot), "Odd names", EntryKind.Directory)],
                Destination = Location.FileSystem(back),
            });
            Assert.True(down.State == JobState.Completed, $"{down.State}: {string.Join("; ", down.Issues.Select(i => i.Message))}");
            var returned = Directory.GetFiles(Path.Combine(back, "Odd names")).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(OddNames.Order(StringComparer.Ordinal), returned);
            Assert.All(OddNames, n => Assert.Equal(n, File.ReadAllText(Path.Combine(back, "Odd names", n))));
        }
        finally
        {
            await RunAsync(stack, new JobRequest { Kind = JobKind.Delete, Sources = [new ItemRef(SftpProvider.At(profile, "."), remoteRoot, EntryKind.Directory)] });
        }
    }

    [Fact]
    public void Names_with_spaces_at_their_edges_are_listed_exactly_and_never_reach_another_item_over_ftp()
    {
        // Release issue I36: FluentFTP's parser of vsftpd's listing (no MLSD) trimmed these names, and it trims every path
        // it sends, so FileCat would have reached "both" when " both " was meant. Made over SFTP (exact bytes), then over
        // FTP: every name is listed exactly; names that begin with spaces are read and deleted as themselves; names that
        // end with one cannot be sent exactly, so FileCat refuses them; the look-alikes without spaces stay untouched.
        var lab = Lab();
        var ct = TestContext.Current.CancellationToken;
        string[] leading = [" leading", "  two"];
        string[] trailing = [" both ", "trailing space "];
        string[] lookAlikes = ["both", "trailing space"];
        string[] all = [.. leading, .. trailing, .. lookAlikes];
        string folder = "filecat-lab-" + Guid.NewGuid().ToString("N")[..8];
        var sftp = Profile(RemoteProtocols.Sftp, 22, lab);
        using var sftpStack = Open(sftp, new Interaction(lab.Password), Path.Combine(_dir, "edges-sftp"));
        using (var lease = sftpStack.Connections.Lease(sftp.Id, ct))
        {
            lease.Channel.CreateDirectory(folder);
            foreach (string name in all)
                using (var s = lease.Channel.CreateNew(RemotePath.Combine(folder, name))) s.Write(System.Text.Encoding.UTF8.GetBytes(name));
        }
        try
        {
            var ftp = Profile(RemoteProtocols.FtpExplicitTls, 21, lab);
            using var ftpStack = Open(ftp, new Interaction(lab.Password), Path.Combine(_dir, "edges-ftp"));
            using (var lease = ftpStack.Connections.Lease(ftp.Id, ct))
            {
                var listed = lease.Channel.List(folder, ct);
                Assert.Equal(all.Order(StringComparer.Ordinal), listed.Select(e => e.Name).Order(StringComparer.Ordinal));
                foreach (var entry in listed.Where(e => leading.Contains(e.Name)))
                {
                    using (var read = lease.Channel.OpenRead(entry.FullPath))
                        Assert.Equal(entry.Name, new StreamReader(read).ReadToEnd());
                    entry.Delete();
                }
                foreach (var entry in listed.Where(e => trailing.Contains(e.Name)))
                {
                    Assert.Contains("ends with a space", Assert.ThrowsAny<IOException>(() => lease.Channel.OpenRead(entry.FullPath).Dispose()).Message);
                    Assert.Contains("ends with a space", Assert.ThrowsAny<IOException>(entry.Delete).Message);
                }
            }
            // On the server: the names that begin with spaces are gone; everything else is as it was.
            using (var lease = sftpStack.Connections.Lease(sftp.Id, ct))
            {
                Assert.Equal(trailing.Concat(lookAlikes).Order(StringComparer.Ordinal), lease.Channel.List(folder, ct).Select(e => e.Name).Order(StringComparer.Ordinal));
                foreach (string name in lookAlikes)
                    using (var read = lease.Channel.OpenRead(RemotePath.Combine(folder, name)))
                        Assert.Equal(name, new StreamReader(read).ReadToEnd());
            }
        }
        finally
        {
            using var lease = sftpStack.Connections.Lease(sftp.Id, CancellationToken.None);
            foreach (var entry in lease.Channel.List(folder, CancellationToken.None)) entry.Delete();
            lease.Channel.List(".", CancellationToken.None).Single(e => e.Name == folder).Delete();
        }
    }

    [Theory]
    [InlineData(RemoteProtocols.Sftp, 22)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 21)]
    public async Task An_upload_the_server_refuses_says_so_and_leaves_nothing(string protocol, int port)
    {
        var lab = Lab();
        var profile = Profile(protocol, port, lab);
        using var stack = Open(profile, new Interaction(lab.Password), Path.Combine(_dir, "refused-" + protocol));
        string file = Path.Combine(Directory.CreateDirectory(Path.Combine(_dir, "refused-src-" + protocol)).FullName, "note.txt");
        File.WriteAllText(file, "not allowed here");
        // The server's root folder: the test account may read it, not write it. The job asks what to do; skip.
        stack.Jobs.DecisionRequested += d => d.Resolve(new Decision(DecisionAction.Skip));
        var job = await RunAsync(stack, new JobRequest
        {
            Kind = JobKind.Copy,
            Sources = [ItemRef.ForFileSystemPath(file, EntryKind.File)],
            Destination = SftpProvider.At(profile, "/"),
        });
        Assert.NotEqual(JobState.Completed, job.State);
        Assert.Equal(1, job.ItemsFailed + job.ItemsSkipped);
        var issue = Assert.Single(job.Issues, i => i.Severity == IssueSeverity.Error);
        Assert.Matches("(?i)permission|denied|not allowed|refused", issue.Message);
        using var check = stack.Connections.Lease(profile.Id, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(check.Channel.List("/", TestContext.Current.CancellationToken), e => e.Name == "note.txt" || e.Name.StartsWith(".fc", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RemoteProtocols.Sftp, 22)]
    [InlineData(RemoteProtocols.FtpExplicitTls, 21)]
    [InlineData(RemoteProtocols.FtpImplicitTls, 990)]
    public async Task A_tree_goes_up_and_comes_back_byte_for_byte_through_jobs(string protocol, int port)
    {
        var lab = Lab();
        var ct = TestContext.Current.CancellationToken;
        var interaction = new Interaction(lab.Password);
        var profile = Profile(protocol, port, lab);
        using var stack = Open(profile, interaction, Path.Combine(_dir, "tree-" + protocol));

        // A tree with small files, a large one, a name with spaces and non-ASCII letters, and an empty folder.
        string local = Directory.CreateDirectory(Path.Combine(_dir, "up-" + protocol, "Lab tree")).FullName;
        var random = new Random(7);
        for (int i = 0; i < 40; i++)
        {
            var bytes = new byte[random.Next(0, 70_000)];
            random.NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(local, $"note {i:00}.bin"), bytes);
        }
        Directory.CreateDirectory(Path.Combine(local, "Žluťoučký kůň", "empty"));
        var large = new byte[48 << 20];
        random.NextBytes(large);
        File.WriteAllBytes(Path.Combine(local, "Žluťoučký kůň", "large.bin"), large);
        var expected = Directory.EnumerateFiles(local, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(local, f).Replace('\\', '/'), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

        string remoteRoot = "filecat-lab-" + Guid.NewGuid().ToString("N")[..8];
        using (var lease = stack.Connections.Lease(profile.Id, ct)) lease.Channel.CreateDirectory(remoteRoot);
        try
        {
            var up = await RunAsync(stack, new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [ItemRef.ForFileSystemPath(local, EntryKind.Directory)],
                Destination = SftpProvider.At(profile, remoteRoot),
            });
            Assert.True(up.State == JobState.Completed, $"{up.State}: {string.Join("; ", up.Issues.Select(i => i.Message))}");

            // The server's own reading of what arrived: every file, its size and its bytes, and nothing half-written.
            using (var lease = stack.Connections.Lease(profile.Id, ct))
            {
                var seen = new Dictionary<string, string>();
                void Walk(string remote, string relative)
                {
                    foreach (var entry in lease.Channel.List(remote, ct))
                    {
                        string path = RemotePath.Combine(remote, entry.Name), rel = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
                        if (entry.IsDirectory) Walk(path, rel);
                        else
                        {
                            using var s = lease.Channel.OpenRead(path);
                            seen[rel] = Convert.ToHexString(SHA256.HashData(s));
                        }
                    }
                }
                Walk(RemotePath.Combine(remoteRoot, "Lab tree"), "");
                Assert.Equal(expected.OrderBy(p => p.Key), seen.OrderBy(p => p.Key));
                Assert.Contains(lease.Channel.List(RemotePath.Combine(remoteRoot, "Lab tree/Žluťoučký kůň"), ct), e => e.Name == "empty" && e.IsDirectory);
            }

            // And back again through a job.
            string back = Directory.CreateDirectory(Path.Combine(_dir, "back-" + protocol)).FullName;
            var down = await RunAsync(stack, new JobRequest
            {
                Kind = JobKind.Copy,
                Sources = [new ItemRef(SftpProvider.At(profile, remoteRoot), "Lab tree", EntryKind.Directory)],
                Destination = Location.FileSystem(back),
            });
            Assert.True(down.State == JobState.Completed, $"{down.State}: {string.Join("; ", down.Issues.Select(i => i.Message))}");
            var returned = Directory.EnumerateFiles(Path.Combine(back, "Lab tree"), "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(Path.Combine(back, "Lab tree"), f).Replace('\\', '/'), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
            Assert.Equal(expected.OrderBy(p => p.Key), returned.OrderBy(p => p.Key));
            Assert.True(Directory.Exists(Path.Combine(back, "Lab tree", "Žluťoučký kůň", "empty")));
            // Every downloaded file carries its origin: the Internet zone and the server it came from.
            string host = $"HostUrl={protocol}://{lab.Host}/";
            Assert.Equal(expected.Count, stack.Files.Marks.Count);
            Assert.All(stack.Files.Marks, m => Assert.True(m.Contains("ZoneId=3", StringComparison.Ordinal) && m.Contains(host, StringComparison.Ordinal), m));
        }
        finally
        {
            // The server is left as it was: the lab folder goes through FileCat's own delete job.
            var cleanup = await RunAsync(stack, new JobRequest
            {
                Kind = JobKind.Delete,
                Sources = [new ItemRef(SftpProvider.At(profile, "."), remoteRoot, EntryKind.Directory)],
            });
            Assert.True(cleanup.State == JobState.Completed, $"Cleanup {cleanup.State}: {string.Join("; ", cleanup.Issues.Select(i => i.Message))}");
        }
    }
}
