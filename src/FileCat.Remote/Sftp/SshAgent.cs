using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using Renci.SshNet;
using Renci.SshNet.Security;

namespace FileCat.Remote.Sftp;

/// <summary>
/// Keys held by an SSH agent (draft-miller-ssh-agent): OpenSSH's ssh-agent, the Windows OpenSSH Authentication Agent
/// service, and the agents of Pageant, 1Password, Bitwarden, and KeePassXC, which speak the same protocol. FileCat lists
/// the keys and asks the agent to sign; private keys never leave the agent, and the agent is never forwarded.
/// </summary>
public sealed class SshAgentClient : IDisposable
{
    /// <summary>The pipe of the Windows OpenSSH Authentication Agent service (also used by 1Password, Bitwarden, KeePassXC).</summary>
    public const string WindowsPipe = @"\\.\pipe\openssh-ssh-agent";
    public const uint RsaSha256 = 2, RsaSha512 = 4;
    private const byte Failure = 5, RequestIdentities = 11, IdentitiesAnswer = 12, SignRequest = 13, SignResponse = 14;
    private const int MaxMessage = 256 * 1024;

    private readonly Stream _stream;
    private readonly object _lock = new();

    private SshAgentClient(Stream stream) => _stream = stream;

    /// <summary>Where this user's agent listens: SSH_AUTH_SOCK, or on Windows the OpenSSH service's pipe; null when unknown.</summary>
    public static string? DefaultAddress() =>
        Environment.GetEnvironmentVariable("SSH_AUTH_SOCK") is { Length: > 0 } socket ? socket : OperatingSystem.IsWindows() ? WindowsPipe : null;

    /// <summary>Connects to an agent at a named pipe (\\.\pipe\…) or a Unix socket path. Throws <see cref="IOException"/> when none answers.</summary>
    public static SshAgentClient Connect(string address)
    {
        try
        {
            if (address.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
            {
                var pipe = new NamedPipeClientStream(".", address[9..], PipeDirection.InOut);
                try
                {
                    pipe.Connect(2000);
                }
                catch
                {
                    pipe.Dispose();
                    throw;
                }
                return new SshAgentClient(pipe);
            }
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try
            {
                socket.Connect(new UnixDomainSocketEndPoint(address));
            }
            catch
            {
                socket.Dispose();
                throw;
            }
            return new SshAgentClient(new NetworkStream(socket, ownsSocket: true));
        }
        catch (Exception ex) when (ex is TimeoutException or SocketException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            throw new IOException(OperatingSystem.IsWindows()
                ? "No SSH agent answered. Start the \"OpenSSH Authentication Agent\" service, or the SSH agent of your password manager, and add your key to it."
                : "No SSH agent answered at SSH_AUTH_SOCK. Start ssh-agent (or your desktop's agent) and add your key with ssh-add.", ex);
        }
    }

    /// <summary>The keys the agent holds: public key blobs and their comments.</summary>
    public IReadOnlyList<(byte[] Blob, string Comment)> Identities()
    {
        var reply = Call([RequestIdentities]);
        if (reply.Length < 5 || reply[0] != IdentitiesAnswer) throw new IOException("The SSH agent did not list its keys.");
        var reader = new Reader(reply, 1);
        uint count = reader.UInt32();
        if (count > 1000) throw new IOException("The SSH agent listed too many keys.");
        var keys = new List<(byte[], string)>();
        for (uint i = 0; i < count; i++) keys.Add((reader.Bytes(), Encoding.UTF8.GetString(reader.Bytes())));
        return keys;
    }

    /// <summary>A signature over <paramref name="data"/> with the agent's key <paramref name="blob"/>, in SSH wire format.</summary>
    public byte[] Sign(byte[] blob, byte[] data, uint flags)
    {
        var request = new byte[1 + 4 + blob.Length + 4 + data.Length + 4];
        request[0] = SignRequest;
        int at = 1;
        at = Put(request, at, blob);
        at = Put(request, at, data);
        BinaryPrimitives.WriteUInt32BigEndian(request.AsSpan(at), flags);
        var reply = Call(request);
        if (reply.Length > 0 && reply[0] == Failure)
            throw new IOException("The SSH agent refused to sign (the key may need confirmation, or its password manager may be locked).");
        if (reply.Length < 5 || reply[0] != SignResponse) throw new IOException("The SSH agent did not sign.");
        return new Reader(reply, 1).Bytes();
    }

    private static int Put(byte[] target, int at, byte[] value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(target.AsSpan(at), (uint)value.Length);
        value.CopyTo(target, at + 4);
        return at + 4 + value.Length;
    }

    private byte[] Call(byte[] request)
    {
        lock (_lock)
        {
            var header = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)request.Length);
            _stream.Write(header);
            _stream.Write(request);
            _stream.Flush();
            ReadExactly(header);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
            if (length is 0 or > MaxMessage) throw new IOException("The SSH agent answered out of bounds.");
            var reply = new byte[length];
            ReadExactly(reply);
            return reply;
        }
    }

    private void ReadExactly(Span<byte> buffer)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = _stream.Read(buffer[done..]);
            if (n <= 0) throw new IOException("The SSH agent closed the connection.");
            done += n;
        }
    }

    public void Dispose() => _stream.Dispose();

    /// <summary>The key type a public key blob names ("ssh-ed25519", "ssh-rsa", …).</summary>
    public static string KeyType(byte[] blob)
    {
        try { return Encoding.ASCII.GetString(new Reader(blob, 0).Bytes()); }
        catch (IOException) { return string.Empty; }
    }

    /// <summary>
    /// What to offer the server for each agent key: RSA as rsa-sha2-512 and rsa-sha2-256 (never SHA-1), every other key
    /// type as itself. Each offer counts against the server's attempt limit, so this is the whole list.
    /// </summary>
    public IReadOnlyList<HostAlgorithm> Algorithms(IReadOnlyList<(byte[] Blob, string Comment)> identities)
    {
        var algorithms = new List<HostAlgorithm>();
        foreach (var (blob, _) in identities)
        {
            string type = KeyType(blob);
            if (type == "ssh-rsa")
            {
                algorithms.Add(new AgentHostAlgorithm("rsa-sha2-512", blob, RsaSha512, this));
                algorithms.Add(new AgentHostAlgorithm("rsa-sha2-256", blob, RsaSha256, this));
            }
            else if (type.Length > 0)
            {
                algorithms.Add(new AgentHostAlgorithm(type, blob, 0, this));
            }
        }
        return algorithms;
    }

    /// <summary>A reader of SSH wire data (big-endian lengths).</summary>
    private sealed class Reader(byte[] data, int at)
    {
        private int _at = at;

        public uint UInt32()
        {
            if (_at + 4 > data.Length) throw new IOException("The SSH agent's answer is truncated.");
            uint v = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(_at));
            _at += 4;
            return v;
        }

        public byte[] Bytes()
        {
            uint length = UInt32();
            if (length > data.Length - _at) throw new IOException("The SSH agent's answer is truncated.");
            var value = data.AsSpan(_at, (int)length).ToArray();
            _at += (int)length;
            return value;
        }
    }
}

/// <summary>One agent key as SSH.NET's host algorithm: its public blob, and signatures the agent makes.</summary>
internal sealed class AgentHostAlgorithm(string name, byte[] blob, uint flags, SshAgentClient agent) : HostAlgorithm(name)
{
    public override byte[] Data => blob;

    public override byte[] Sign(byte[] data) => agent.Sign(blob, data, flags);

    /// <summary>Signing in only makes signatures; verifying one is the server's part.</summary>
    public override bool VerifySignature(byte[] data, byte[] signature) => throw new NotSupportedException();
}

/// <summary>The agent's keys as one SSH.NET key source.</summary>
internal sealed class AgentKeySource(IReadOnlyList<HostAlgorithm> algorithms) : IPrivateKeySource
{
    public IReadOnlyCollection<HostAlgorithm> HostKeyAlgorithms => algorithms;
}
