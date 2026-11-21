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

    /// <param name="sftpDescriptors">The SFTP server process may hold this many descriptors (the system's sftp-server is
    /// then used, not sshd's own): every folder it has open holds one. Null where no sftp-server is found.</param>
    public static TestSshd? TryStart(int? sftpDescriptors = null, bool disableLSetStat = false, string? privateReviewRoot = null,
        bool dropGroupReply = false)
    {
        if (OperatingSystem.IsWindows()) return null;
        string? sshd = Environment.GetEnvironmentVariable("FILECAT_SSHD") is { Length: > 0 } configured ? configured
            : new[] { "/usr/sbin/sshd", "/usr/local/sbin/sshd", "/opt/homebrew/sbin/sshd" }.FirstOrDefault(File.Exists);
        if (sshd is null) return null;
        string sftp = "internal-sftp";
        const string python = "/usr/bin/python3", ubuntuSftp = "/usr/lib/openssh/sftp-server";
        if (dropGroupReply && (privateReviewRoot is null || !File.Exists(python) || !File.Exists(ubuntuSftp))) return null;
        if (sftpDescriptors is { } descriptors)
        {
            if (new[] { "/usr/lib/openssh/sftp-server", "/usr/libexec/openssh/sftp-server", "/usr/libexec/sftp-server" }
                    .FirstOrDefault(File.Exists) is not { } server) return null;
            sftp = $"ulimit -n {descriptors}; exec {server}";
        }
        if (disableLSetStat) sftp += " -P lsetstat";
        string dir = Path.Combine(privateReviewRoot ?? Path.GetTempPath(), "filecat-sshd-" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (privateReviewRoot is not null)
        {
            string files = Path.Combine(dir, "files");
            System.IO.Directory.CreateDirectory(files, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            sftp += " -d " + files;
        }
        if (dropGroupReply)
        {
            string fixture = Path.Combine(dir, "drop-group-reply.py");
            string trace = Path.Combine(dir, "group-reply.trace");
            File.WriteAllText(fixture, DropGroupReplyFixture);
            // Creating the trace after review would change the observed ancestor directory.
            File.WriteAllText(trace, "");
            sftp = $"{python} {fixture} {ubuntuSftp} {dir}/files {trace}";
        }
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
            $"Subsystem sftp {sftp}", "LogLevel ERROR") + "\n");
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

    private const string DropGroupReplyFixture = """
        # Forward the fixture's SFTP frames to stock OpenSSH. Discard the group status only after it replies.
        import struct
        import subprocess
        import sys

        def take_frame(stream):
            header = stream.read(4)
            if not header:
                return None
            size, = struct.unpack(">I", header)
            if not 1 <= size <= 256 * 1024:
                raise ValueError("Unexpected SFTP fixture frame size")
            body = stream.read(size)
            if len(body) != size:
                raise EOFError("Incomplete SFTP fixture frame")
            return header + body

        def read_string(packet, offset):
            size, = struct.unpack_from(">I", packet, offset)
            start = offset + 4
            return packet[start:start + size], start + size

        server, directory, trace = sys.argv[1:]
        with subprocess.Popen([server, "-d", directory], stdin=subprocess.PIPE, stdout=subprocess.PIPE) as sftp:
            while request := take_frame(sys.stdin.buffer):
                sftp.stdin.write(request)
                sftp.stdin.flush()
                reply = take_frame(sftp.stdout)
                if not reply:
                    break
                if request[4] == 200:
                    extension, offset = read_string(request, 9)
                    if extension == b"lsetstat@openssh.com":
                        _, offset = read_string(request, offset)
                        fields = struct.unpack_from(">I", request, offset)[0]
                        if reply[4] != 101 or fields not in (2, 4):
                            raise ValueError("Unexpected lsetstat fixture attributes or reply")
                        status, = struct.unpack_from(">I", reply, 9)
                        values = struct.unpack_from(">II" if fields == 2 else ">I", request, offset + 4)
                        with open(trace, "a", encoding="ascii") as output:
                            print(fields, *values, status, file=output)
                        if fields == 2:
                            break
                sys.stdout.buffer.write(reply)
                sys.stdout.buffer.flush()
        """;

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
