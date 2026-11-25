using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// Release issue I42: small files copied up and down through FileCat's jobs at a long round trip, with every byte and
/// time checked after the way back. The delay sits in <see cref="LatencyProxy"/> on this machine (50 ms each way unless
/// FILECAT_I42_ONE_WAY_MS says otherwise), between FileCat and:
/// <list type="bullet">
/// <item>an SFTP server named by FILECAT_I42_SFTP_HOST (with _PORT, _USER, _KEY: a private key file; _ROOT: a folder,
/// relative to the account's home unless absolute), reached directly, without the delay, to set up and check;</item>
/// <item>with FILECAT_I42_FTPS=1, the pyftpdlib test server with explicit TLS on 127.0.0.2, its control and data
/// connections all through the relay.</item>
/// </list>
/// Set FILECAT_I42_BENCH=1; FILECAT_I42_FILES (200), _FILE_BYTES (4096) and _RUNS (3) shape it, and FILECAT_I42_OUT names a
/// file the results are appended to. SFTP requests are counted by type for each job.
/// </summary>
public sealed class SmallFileLatencyBenchmark : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fc-i42-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    private static int EnvInt(string name, int fallback) =>
        Env(name) is { } text ? int.Parse(text, CultureInfo.InvariantCulture) : fallback;

    private sealed class Interaction : IRemoteInteraction
    {
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => HostKeyDecision.AcceptOnce;
        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => new(TestFtpServer.Password, false);
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;
        public HostKeyDecision DecideCertificate(RemoteProfile profile, CertificateCheck check) => HostKeyDecision.AcceptOnce;
    }

    /// <summary>Accepts download marks (the portable file system has no Mark-of-the-Web).</summary>
    private sealed class MarkingOps : PortableFileOperations
    {
        public override bool WriteOriginMark(string path, string mark) => true;
    }

    /// <summary>Counts the SFTP requests of every connection it makes, by type.</summary>
    private sealed class CountingConnector(ISftpConnector inner) : ISftpConnector
    {
        public readonly ConcurrentDictionary<string, int> Counts = new(StringComparer.Ordinal);

        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
        {
            var channel = inner.Connect(profile, context, ct);
            if (channel is SshNetChannel ssh) ssh.ObserveRequests(type => Counts.AddOrUpdate(type, 1, (_, n) => n + 1));
            return channel;
        }

        public Dictionary<string, int> Take()
        {
            var taken = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var key in Counts.Keys)
                if (Counts.TryRemove(key, out int n)) taken[key] = n;
            return taken;
        }
    }

    private sealed record RunResult(TimeSpan Up, TimeSpan Down, Dictionary<string, int> UpRequests, Dictionary<string, int> DownRequests);

    [Fact]
    public async Task Small_files_go_up_and_come_back_whole_at_a_long_round_trip()
    {
        if (Env("FILECAT_I42_BENCH") is null) Assert.Skip("Set FILECAT_I42_BENCH=1 (and FILECAT_I42_SFTP_HOST or FILECAT_I42_FTPS=1) to measure small-file copies at a long round trip.");
        var oneWay = TimeSpan.FromMilliseconds(EnvInt("FILECAT_I42_ONE_WAY_MS", 50));
        int files = EnvInt("FILECAT_I42_FILES", 200), size = EnvInt("FILECAT_I42_FILE_BYTES", 4096), runs = EnvInt("FILECAT_I42_RUNS", 3);
        string source = MakeSources(files, size);
        var lines = new List<string>
        {
            FormattableString.Invariant($"I42 small files: {files} files of {size} bytes, {runs} runs, {oneWay.TotalMilliseconds:0} ms each way, {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}"),
        };
        bool any = false;
        if (Env("FILECAT_I42_SFTP_HOST") is { } host)
        {
            any = true;
            lines.AddRange(await MeasureSftpAsync(host, source, files, runs, oneWay));
        }
        if (Env("FILECAT_I42_FTPS") is not null)
        {
            any = true;
            lines.AddRange(await MeasureFtpsAsync(source, files, runs, oneWay));
        }
        if (!any) Assert.Skip("Name a server: FILECAT_I42_SFTP_HOST, or FILECAT_I42_FTPS=1.");
        foreach (var line in lines) TestContext.Current.TestOutputHelper?.WriteLine(line);
        if (Env("FILECAT_I42_OUT") is { } output) File.AppendAllLines(output, lines);
    }

    /// <summary>Distinct content per file, and modified times in whole seconds (what SFTP v3 and MFMT carry).</summary>
    private string MakeSources(int files, int size)
    {
        string folder = Directory.CreateDirectory(Path.Combine(_dir, "source", "small")).FullName;
        var origin = new DateTime(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < files; i++)
        {
            var bytes = new byte[size];
            new Random(4200 + i).NextBytes(bytes);
            string path = Path.Combine(folder, FormattableString.Invariant($"note-{i:0000}.bin"));
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, origin.AddMinutes(-7 * i).AddSeconds(-i));
        }
        return folder;
    }

    private (SftpConnections Connections, JobManager Jobs, CountingConnector Counter) Stack(RemoteProfile profile, string name)
    {
        string state = Directory.CreateDirectory(Path.Combine(_dir, "state-" + name)).FullName;
        var counter = new CountingConnector(new SshNetConnector());
        var connections = new SftpConnections(id => id == profile.Id ? profile : null, new ProtocolConnector(counter, new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(state, "known_hosts"), []), new SessionSecretStore(), new Interaction(),
            new CertificateTrust(Path.Combine(state, "trusted_certificates")));
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(new SftpProvider(connections, () => [profile], p => p));
        SftpJobs.Register();
        return (connections, new JobManager(new MarkingOps(), providers, Path.Combine(state, "journal")), counter);
    }

    private static async Task<TimeSpan> RunJobAsync(JobManager jobs, JobRequest request)
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = Stopwatch.StartNew();
        var job = jobs.Submit(request);
        while (!job.State.IsFinished())
        {
            if (clock.Elapsed > TimeSpan.FromMinutes(30)) throw new TimeoutException(job.State.ToString());
            await Task.Delay(5, ct);
        }
        var elapsed = clock.Elapsed;
        Assert.True(job.State == JobState.Completed, $"{job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}");
        Assert.DoesNotContain(job.Issues, i => i.Severity != IssueSeverity.Info);
        return elapsed;
    }

    /// <summary>The copies that came back: every byte, and each file's modified time to the second.</summary>
    private static void CheckLocalCopies(string source, string back, int files)
    {
        var copies = Directory.GetFiles(back);
        Assert.Equal(files, copies.Length);
        foreach (string original in Directory.GetFiles(source))
        {
            string copy = Path.Combine(back, Path.GetFileName(original));
            Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(copy));
            Assert.Equal(File.GetLastWriteTimeUtc(original), File.GetLastWriteTimeUtc(copy));
        }
    }

    private async Task<List<string>> MeasureSftpAsync(string host, string source, int files, int runs, TimeSpan oneWay)
    {
        var ct = TestContext.Current.CancellationToken;
        int port = EnvInt("FILECAT_I42_SFTP_PORT", 22);
        string user = Env("FILECAT_I42_SFTP_USER") ?? throw new InvalidOperationException("FILECAT_I42_SFTP_USER is not set.");
        string key = Env("FILECAT_I42_SFTP_KEY") ?? throw new InvalidOperationException("FILECAT_I42_SFTP_KEY is not set.");
        var target = (await Dns.GetHostAddressesAsync(host, ct)).First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        using var proxy = new LatencyProxy(new IPEndPoint(target, port), oneWay);
        var far = new RemoteProfile { Name = "i42 far", Host = "127.0.0.1", Port = proxy.Port, User = user, Auth = RemoteAuth.Key, KeyFile = key, Temporary = true };
        var near = new RemoteProfile { Name = "i42 near", Host = host, Port = port, User = user, Auth = RemoteAuth.Key, KeyFile = key, Temporary = true };
        var context = new ConnectContext { ApproveHostKey = _ => true, GetSecret = _ => null, AnswerPrompts = (_, _) => null };
        using var direct = new SshNetConnector().Connect(near, context, ct);
        string root = Env("FILECAT_I42_SFTP_ROOT") ?? "filecat-i42";
        if (!root.StartsWith('/')) root = RemotePath.Combine(direct.HomeDirectory, root);
        var (connections, jobs, counter) = Stack(far, "sftp");
        using var owned = connections;
        // Connected once before timing: the copies, not the handshake, are measured.
        using (var warm = connections.Lease(far.Id, ct))
        {
            var started = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) warm.Channel.Stat(root);
            Assert.True(started.Elapsed >= 5 * 2 * oneWay * 0.95, $"The relay held five stats for only {started.Elapsed}.");
        }
        counter.Take();
        var results = new List<RunResult>();
        for (int run = 0; run < runs; run++)
        {
            string folder = RemotePath.Combine(root, FormattableString.Invariant($"run-{run}-{Guid.NewGuid():N}")[..14]);
            direct.CreateDirectory(folder);
            try
            {
                var up = await RunJobAsync(jobs, new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.Directory)], Destination = SftpProvider.At(far, folder) });
                var upRequests = counter.Take();
                string remote = RemotePath.Combine(folder, "small");
                var listed = direct.ListAll(remote, ct).ToDictionary(e => e.Name, StringComparer.Ordinal);
                Assert.Equal(files, listed.Count);
                foreach (string original in Directory.GetFiles(source))
                {
                    var entry = listed[Path.GetFileName(original)];
                    Assert.False(entry.IsLink || entry.IsDirectory);
                    Assert.Equal(new FileInfo(original).Length, entry.Size);
                    Assert.Equal(File.GetLastWriteTimeUtc(original), entry.ModifiedUtc);
                }
                string back = Directory.CreateDirectory(Path.Combine(_dir, FormattableString.Invariant($"sftp-back-{run}"))).FullName;
                var down = await RunJobAsync(jobs, new JobRequest { Kind = JobKind.Copy, Sources = [new ItemRef(SftpProvider.At(far, folder), "small", EntryKind.Directory)], Destination = Location.FileSystem(back) });
                var downRequests = counter.Take();
                CheckLocalCopies(source, Path.Combine(back, "small"), files);
                Directory.Delete(back, recursive: true);
                results.Add(new RunResult(up, down, upRequests, downRequests));
            }
            finally
            {
                RemoveTree(direct, folder, ct);
            }
        }
        return Report("SFTP", files, results);
    }

    private static void RemoveTree(ISftpChannel channel, string folder, CancellationToken ct)
    {
        foreach (var entry in channel.ListAll(folder, ct))
        {
            if (entry.IsDirectory && !entry.IsLink) RemoveTree(channel, entry.FullPath, ct);
            else entry.Delete();
        }
        string? parent = RemotePath.Parent(folder);
        channel.ListAll(parent!, ct).Single(e => e.Name == RemotePath.Name(folder)).Delete();
    }

    private async Task<List<string>> MeasureFtpsAsync(string source, int files, int runs, TimeSpan oneWay)
    {
        var ct = TestContext.Current.CancellationToken;
        var (pem, _) = TestFtpServer.SelfSigned("FileCat I42");
        const int firstPassive = 51200, lastPassive = 51259;
        using var server = TestFtpServer.TryStart(pem, bindAddress: "127.0.0.2", passivePorts: (firstPassive, lastPassive));
        if (server is null) return ["FTPS: no pyftpdlib with TLS here; not measured."];
        var ports = new List<(int, int)> { (0, server.Port) };
        for (int p = firstPassive; p <= lastPassive; p++) ports.Add((p, p));
        using var proxy = new LatencyProxy(IPAddress.Parse("127.0.0.2"), ports, oneWay);
        var far = server.Profile(RemoteProtocols.FtpExplicitTls);
        far.Port = proxy.Port;
        var (connections, jobs, _) = Stack(far, "ftps");
        using var owned = connections;
        using (var warm = connections.Lease(far.Id, ct))
        {
            var started = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++) warm.Channel.Stat("/");
            Assert.True(started.Elapsed >= 5 * 2 * oneWay * 0.95, $"The relay held five stats for only {started.Elapsed}.");
        }
        var results = new List<RunResult>();
        for (int run = 0; run < runs; run++)
        {
            string name = FormattableString.Invariant($"run-{run}-{Guid.NewGuid():N}")[..14];
            string folder = Directory.CreateDirectory(Path.Combine(server.Root, name)).FullName;
            try
            {
                var up = await RunJobAsync(jobs, new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(source, EntryKind.Directory)], Destination = SftpProvider.At(far, "/" + name) });
                // The server's own files: every byte and time as sent.
                CheckLocalCopies(source, Path.Combine(folder, "small"), files);
                string back = Directory.CreateDirectory(Path.Combine(_dir, FormattableString.Invariant($"ftps-back-{run}"))).FullName;
                var down = await RunJobAsync(jobs, new JobRequest { Kind = JobKind.Copy, Sources = [new ItemRef(SftpProvider.At(far, "/" + name), "small", EntryKind.Directory)], Destination = Location.FileSystem(back) });
                CheckLocalCopies(source, Path.Combine(back, "small"), files);
                Directory.Delete(back, recursive: true);
                results.Add(new RunResult(up, down, [], []));
            }
            finally
            {
                try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        return Report("FTPS (explicit TLS, pyftpdlib)", files, results);
    }

    private static List<string> Report(string name, int files, List<RunResult> results)
    {
        static TimeSpan Median(IEnumerable<TimeSpan> values)
        {
            var sorted = values.OrderBy(v => v).ToList();
            return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
        }
        static string Requests(Dictionary<string, int> counts, int files) => counts.Count == 0 ? "not counted"
            : FormattableString.Invariant($"{counts.Values.Sum() / (double)files:0.00} per file (") +
              string.Join(", ", counts.OrderByDescending(c => c.Value).Select(c => FormattableString.Invariant($"{c.Key} {c.Value}"))) + ")";
        var up = Median(results.Select(r => r.Up));
        var down = Median(results.Select(r => r.Down));
        var lines = new List<string>
        {
            FormattableString.Invariant($"{name} up: median {up.TotalSeconds:0.0} s, {up.TotalMilliseconds / files:0} ms per file; runs {string.Join(", ", results.Select(r => r.Up.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)))} s"),
            FormattableString.Invariant($"{name} down: median {down.TotalSeconds:0.0} s, {down.TotalMilliseconds / files:0} ms per file; runs {string.Join(", ", results.Select(r => r.Down.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)))} s"),
        };
        if (results.Count > 0)
        {
            lines.Add($"{name} up requests (last run): {Requests(results[^1].UpRequests, files)}");
            lines.Add($"{name} down requests (last run): {Requests(results[^1].DownRequests, files)}");
        }
        return lines;
    }
}
