using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// A user-mode OpenSSH sshd on a free loopback port with generated host and client keys, serving this machine's files
/// to this user (Linux and macOS; CI installs openssh-server). FILECAT_SSHD names another sshd binary. Tests skip
/// where no sshd exists, such as on Windows.
/// </summary>
internal sealed class TestSshd : IDisposable
{
    private readonly Process _process;

    private TestSshd(Process process, string directory, int port, byte[] hostKey)
    {
        _process = process;
        Directory = directory;
        Port = port;
        HostKey = hostKey;
        Files = System.IO.Directory.CreateDirectory(Path.Combine(directory, "files")).FullName;
    }

    public string Directory { get; }
    public int Port { get; }
    public byte[] HostKey { get; }
    /// <summary>A folder the tests use through SFTP and, for setup and checks, directly.</summary>
    public string Files { get; }

    public RemoteProfile Profile => new()
    {
        Name = "test sshd", Host = "127.0.0.1", Port = Port, User = Environment.UserName, Auth = RemoteAuth.Key,
        KeyFile = Path.Combine(Directory, "client"),
    };

    public static TestSshd? TryStart()
    {
        if (OperatingSystem.IsWindows()) return null;
        string? sshd = Environment.GetEnvironmentVariable("FILECAT_SSHD") is { Length: > 0 } configured ? configured
            : new[] { "/usr/sbin/sshd", "/usr/local/sbin/sshd", "/opt/homebrew/sbin/sshd" }.FirstOrDefault(File.Exists);
        if (sshd is null) return null;
        string dir = System.IO.Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-sshd-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        Run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", Path.Combine(dir, "host"));
        Run("ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", Path.Combine(dir, "client"));
        File.Copy(Path.Combine(dir, "client.pub"), Path.Combine(dir, "authorized_keys"));
        int port;
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        File.WriteAllText(Path.Combine(dir, "sshd_config"), string.Join('\n',
            $"Port {port}", "ListenAddress 127.0.0.1", $"HostKey {dir}/host", $"PidFile {dir}/sshd.pid", $"AuthorizedKeysFile {dir}/authorized_keys",
            "StrictModes no", "UsePAM no", "PasswordAuthentication no", "KbdInteractiveAuthentication no", "PubkeyAuthentication yes",
            "Subsystem sftp internal-sftp", "LogLevel ERROR") + "\n");
        var psi = new ProcessStartInfo(sshd) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in new[] { "-D", "-e", "-f", Path.Combine(dir, "sshd_config") }) psi.ArgumentList.Add(a);
        var process = Process.Start(psi)!;
        process.BeginErrorReadLine();
        process.BeginOutputReadLine();
        for (int i = 0; i < 100; i++)
        {
            if (process.HasExited) return null;
            try
            {
                using var client = new TcpClient();
                client.Connect(IPAddress.Loopback, port);
                // "ssh-ed25519 AAAA… comment": the public key blob is the second field.
                var hostKey = Convert.FromBase64String(File.ReadAllText(Path.Combine(dir, "host.pub")).Split(' ')[1]);
                return new TestSshd(process, dir, port, hostKey);
            }
            catch (SocketException)
            {
                Thread.Sleep(50);
            }
        }
        process.Kill();
        return null;
    }

    private static void Run(string program, params string[] args)
    {
        var psi = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"{program} failed: {p.StandardError.ReadToEnd()}");
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) _process.Kill();
            _process.WaitForExit(5000);
        }
        catch (InvalidOperationException) { }
        _process.Dispose();
        try { System.IO.Directory.Delete(Directory, recursive: true); }
        catch (IOException) { }
    }
}

public sealed class OpenSshIntegrationTests : IDisposable
{
    private readonly TestSshd? _sshd = TestSshd.TryStart();

    public void Dispose() => _sshd?.Dispose();

    private (ISftpChannel Channel, string Root) Connect()
    {
        if (_sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        var context = new ConnectContext
        {
            ApproveHostKey = key => key.AsSpan().SequenceEqual(_sshd.HostKey),
            GetSecret = _ => null,
            AnswerPrompts = (_, _) => null,
        };
        return (new SshNetConnector().Connect(_sshd.Profile, context, TestContext.Current.CancellationToken), _sshd.Files);
    }

    [Fact]
    public void Links_are_renamed_and_deleted_themselves_never_their_targets()
    {
        var (channel, root) = Connect();
        using var _ = channel;
        File.WriteAllText(Path.Combine(root, "target.txt"), "keep");
        File.CreateSymbolicLink(Path.Combine(root, "link.txt"), Path.Combine(root, "target.txt"));
        System.IO.Directory.CreateDirectory(Path.Combine(root, "dir"));
        File.WriteAllText(Path.Combine(root, "dir", "inner.txt"), "inner");
        System.IO.Directory.CreateSymbolicLink(Path.Combine(root, "dirlink"), Path.Combine(root, "dir"));
        var ct = TestContext.Current.CancellationToken;

        var entries = channel.List(root, ct).ToDictionary(e => e.Name);
        Assert.True(entries["link.txt"].IsLink);
        Assert.True(entries["dirlink"].IsLink);
        Assert.False(entries["dirlink"].IsDirectory);
        Assert.Equal(new RemoteStat(false, false, 4, channel.Stat(root + "/target.txt")!.Value.ModifiedUtc), channel.Stat(root + "/link.txt"));

        entries["link.txt"].MoveTo(root + "/renamed.txt");
        Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "target.txt")));
        Assert.NotNull(new FileInfo(Path.Combine(root, "renamed.txt")).LinkTarget);

        var now = channel.List(root, ct).ToDictionary(e => e.Name);
        now["renamed.txt"].Delete();
        now["dirlink"].Delete();
        Assert.False(File.Exists(Path.Combine(root, "renamed.txt")) || new FileInfo(Path.Combine(root, "renamed.txt")).LinkTarget is not null);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "target.txt")));
        Assert.Equal("inner", File.ReadAllText(Path.Combine(root, "dir", "inner.txt")));
        Assert.False(System.IO.Directory.Exists(Path.Combine(root, "dirlink")));
    }

    [Fact]
    public void Files_are_created_exclusively_read_at_offsets_and_replaced_atomically()
    {
        var (channel, root) = Connect();
        using var _ = channel;
        File.WriteAllText(Path.Combine(root, "existing.txt"), "old");
        using (var stream = channel.CreateNew(root + "/new.txt")) stream.Write("hello world"u8);
        Assert.Equal(11, channel.Stat(root + "/new.txt")!.Value.Size);
        using (var read = channel.OpenRead(root + "/new.txt"))
        {
            read.Position = 6;
            var buffer = new byte[5];
            Assert.Equal(5, read.Read(buffer));
            Assert.Equal("world", Encoding.ASCII.GetString(buffer));
        }
        Assert.ThrowsAny<IOException>(() => channel.CreateNew(root + "/existing.txt").Dispose());
        Assert.ThrowsAny<IOException>(() => channel.Rename(root + "/new.txt", root + "/existing.txt"));
        Assert.True(channel.TryReplace(root + "/new.txt", root + "/existing.txt"));
        Assert.Equal("hello world", File.ReadAllText(Path.Combine(root, "existing.txt")));
        Assert.False(File.Exists(Path.Combine(root, "new.txt")));

        var stamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
        channel.SetModified(root + "/existing.txt", stamp);
        Assert.Equal(stamp, channel.Stat(root + "/existing.txt")!.Value.ModifiedUtc);
        channel.CreateDirectory(root + "/made");
        Assert.True(System.IO.Directory.Exists(Path.Combine(root, "made")));
        Assert.ThrowsAny<IOException>(() => channel.CreateDirectory(root + "/made"));
        Assert.Null(channel.Stat(root + "/missing"));
        Assert.Throws<FileNotFoundException>(() => channel.List(root + "/missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_wrong_host_key_stops_the_connection()
    {
        if (_sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        var context = new ConnectContext { ApproveHostKey = _ => false, GetSecret = _ => null, AnswerPrompts = (_, _) => null };
        Assert.ThrowsAny<IOException>(() => new SshNetConnector().Connect(_sshd.Profile, context, TestContext.Current.CancellationToken));
    }
}
