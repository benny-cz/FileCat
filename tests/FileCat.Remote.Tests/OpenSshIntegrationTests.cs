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
    public void A_connection_is_not_held_to_the_small_socket_buffers_SSH_NET_sets()
    {
        // Release issue I41: SSH.NET's ConnectAsync leaves 137,072-byte buffers (Linux reports twice what was set), which
        // held a transfer to 1.2 MB/s at 100 ms. Linux caps what may be set at net.core.rmem_max (212,992 by default).
        var (channel, _) = Connect();
        using var _ = channel;
        int buffer = Assert.IsType<SshNetChannel>(channel).SocketBuffer;
        Assert.True(buffer > 2 * 137_072, $"receive buffer {buffer}");
        if (!OperatingSystem.IsLinux()) Assert.True(buffer >= SshNetConnector.SocketBufferBytes, $"receive buffer {buffer}");
    }

    [Fact]
    public void SSH_NET_keeps_its_socket_where_FileCat_widens_it()
    {
        // Runs everywhere, no server needed: an SSH.NET upgrade that moves the socket fails here, not silently in speed.
        Assert.NotNull(SshNetConnector.SessionProperty);
        Assert.Equal(typeof(Socket), SshNetConnector.SocketField?.FieldType);
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
        // An interrupted upload continues at an offset: nothing before it is truncated or rewritten.
        using (var more = channel.OpenWriteAt(root + "/new.txt", 5)) more.Write(", resumed"u8);
        Assert.Equal("hello, resumed", File.ReadAllText(Path.Combine(root, "new.txt")));
        using (var tail = channel.OpenWriteAt(root + "/new.txt", 5)) tail.Write(" world"u8);
        Assert.Equal("hello worldmed", File.ReadAllText(Path.Combine(root, "new.txt")));
        using (var fix = channel.CreateNew(root + "/fixed.txt")) fix.Write("hello world"u8);
        channel.List(root, TestContext.Current.CancellationToken).Single(e => e.Name == "new.txt").Delete();
        channel.Rename(root + "/fixed.txt", root + "/new.txt");
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
