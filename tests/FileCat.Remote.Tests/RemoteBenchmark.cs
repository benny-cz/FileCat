using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// P6 and P8 transfer measurements (plan §23, P8: "performance/resource measurements"): uploads and downloads through
/// FileCat's F5 jobs against real servers on this machine (pyftpdlib for FTP; OpenSSH's sshd for SFTP, on Linux and
/// macOS), compared with writing the same files straight through the connection. Set FILECAT_REMOTE_BENCH=1;
/// docs/validation/P6-P8-remote.md records the results.
/// </summary>
public sealed class RemoteBenchmark : IDisposable
{
    private const int SmallFiles = 1000;
    private const int LargeMiB = 64;
    private readonly string _dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-remote-bench", Guid.NewGuid().ToString("N")[..8])).FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    /// <summary>Accepts download marks (the portable file system has no Mark-of-the-Web).</summary>
    private sealed class MarkingOps : PortableFileOperations
    {
        public override bool WriteOriginMark(string path, string mark) => true;
    }

    private sealed class Interaction : IRemoteInteraction
    {
        public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check) => HostKeyDecision.AcceptOnce;
        public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request) => new(TestFtpServer.Password, false);
        public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) => null;
        public bool AllowUnencrypted(RemoteProfile profile) => true;
    }

    [Fact]
    public async Task Transfers_through_jobs_stay_close_to_the_connection_itself()
    {
        if (Environment.GetEnvironmentVariable("FILECAT_REMOTE_BENCH") is not { Length: > 0 }) Assert.Skip("Set FILECAT_REMOTE_BENCH=1 to measure remote transfers.");
        var results = new List<string>();
        using (var ftp = TestFtpServer.TryStart())
        {
            if (ftp is not null)
            {
                var profile = ftp.Profile(RemoteProtocols.Ftp);
                profile.PlainTextAccepted = true;
                results.AddRange(await MeasureAsync("FTP (pyftpdlib)", profile, "/"));
            }
        }
        using (var sshd = TestSshd.TryStart())
        {
            if (sshd is not null) results.AddRange(await MeasureAsync("SFTP (OpenSSH sshd)", sshd.Profile, sshd.Files));
        }
        if (results.Count == 0) Assert.Skip("No FTP or SFTP test server here (pyftpdlib, or sshd on Linux/macOS).");
        foreach (var line in results) TestContext.Current.TestOutputHelper?.WriteLine(line);
    }

    private async Task<List<string>> MeasureAsync(string name, RemoteProfile profile, string root)
    {
        var ct = TestContext.Current.CancellationToken;
        string state = Directory.CreateDirectory(Path.Combine(_dir, "state-" + profile.Protocol)).FullName;
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()),
            new HostKeyTrust(Path.Combine(state, "known_hosts")), new SessionSecretStore(), new Interaction());
        var sftp = new SftpProvider(connections, () => [profile], p => p);
        var providers = new ProviderRegistry();
        providers.Register(new LocalFileSystemProvider());
        providers.Register(sftp);
        SftpJobs.Register();
        var jobs = new JobManager(new MarkingOps(), providers, Path.Combine(state, "journal"));

        string small = Directory.CreateDirectory(Path.Combine(state, "small")).FullName;
        var note = new byte[1024];
        new Random(1).NextBytes(note);
        for (int i = 0; i < SmallFiles; i++) File.WriteAllBytes(Path.Combine(small, $"note-{i:0000}.bin"), note);
        string large = Path.Combine(state, "large.bin");
        var chunk = new byte[1024 * 1024];
        using (var file = File.Create(large))
            for (int i = 0; i < LargeMiB; i++)
            {
                new Random(i).NextBytes(chunk);
                file.Write(chunk);
            }

        async Task<TimeSpan> Job(JobRequest request)
        {
            var clock = Stopwatch.StartNew();
            var job = jobs.Submit(request);
            while (!job.State.IsFinished()) await Task.Delay(5, ct);
            Assert.True(job.State == JobState.Completed, $"{job.State}: {string.Join("; ", job.Issues.Select(i => i.Message))}");
            return clock.Elapsed;
        }

        var lines = new List<string>();
        using var lease = connections.Lease(profile.Id, ct);
        var channel = lease.Channel;
        string rawFolder = RemotePath.Combine(root, "raw");
        channel.CreateDirectory(rawFolder);

        // The connection itself: create and write each file, nothing else.
        var clock = Stopwatch.StartNew();
        foreach (var path in Directory.EnumerateFiles(small))
            using (var s = channel.CreateNew(RemotePath.Combine(rawFolder, Path.GetFileName(path)))) s.Write(note);
        var rawUp = clock.Elapsed;
        var up = await Job(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(small, EntryKind.Directory)], Destination = SftpProvider.At(profile, root) });
        lines.Add($"{name}, {SmallFiles:N0} files of 1 KiB up: F5 {up.TotalSeconds:F1} s, the connection alone {rawUp.TotalSeconds:F1} s ({up / rawUp:F1}×).");
        // Budgets (docs/validation/P6-P8-remote.md): a temporary name, a size check, and a rename cost a few round trips
        // per file; more than this is a regression like the per-file flushes and folder listings found here.
        Assert.True(up < rawUp * 5, $"Uploading small files took {up}, the connection alone {rawUp}.");

        clock.Restart();
        var buffer = new byte[64 * 1024];
        foreach (var entry in channel.List(rawFolder, ct))
            using (var s = channel.OpenRead(RemotePath.Combine(rawFolder, entry.Name)))
                while (s.Read(buffer) > 0) { }
        var rawDown = clock.Elapsed;
        string back = Directory.CreateDirectory(Path.Combine(state, "back")).FullName;
        var down = await Job(new JobRequest { Kind = JobKind.Copy, Sources = [new ItemRef(SftpProvider.At(profile, root), "small", EntryKind.Directory)], Destination = Location.FileSystem(back) });
        Assert.Equal(SmallFiles, Directory.GetFiles(Path.Combine(back, "small")).Length);
        lines.Add($"{name}, {SmallFiles:N0} files of 1 KiB down: F5 {down.TotalSeconds:F1} s, the connection alone {rawDown.TotalSeconds:F1} s ({down / rawDown:F1}×).");
        Assert.True(down < rawDown * 4, $"Downloading small files took {down}, the connection alone {rawDown}.");

        clock.Restart();
        using (var s = channel.CreateNew(RemotePath.Combine(rawFolder, "large.bin")))
        using (var input = File.OpenRead(large))
            input.CopyTo(s, 1024 * 1024);
        var rawLargeUp = clock.Elapsed;
        var largeUp = await Job(new JobRequest { Kind = JobKind.Copy, Sources = [ItemRef.ForFileSystemPath(large, EntryKind.File)], Destination = SftpProvider.At(profile, root) });
        clock.Restart();
        using (var s = channel.OpenRead(RemotePath.Combine(rawFolder, "large.bin")))
            while (s.Read(buffer) > 0) { }
        var rawLargeDown = clock.Elapsed;
        var largeDown = await Job(new JobRequest { Kind = JobKind.Copy, Sources = [new ItemRef(SftpProvider.At(profile, root), "large.bin", EntryKind.File, LargeMiB * 1024L * 1024)], Destination = Location.FileSystem(back) });
        Assert.Equal(File.ReadAllBytes(large), File.ReadAllBytes(Path.Combine(back, "large.bin")));
        static string Rate(TimeSpan t) => $"{LargeMiB / t.TotalSeconds:F0} MiB/s";
        lines.Add($"{name}, {LargeMiB} MiB up: F5 {Rate(largeUp)}, the connection alone {Rate(rawLargeUp)}; down: F5 {Rate(largeDown)}, the connection alone {Rate(rawLargeDown)}.");
        Assert.True(largeUp < rawLargeUp * 2.5 && largeDown < rawLargeDown * 2.5, "A large transfer through F5 fell far behind the connection.");
        return lines;
    }
}
