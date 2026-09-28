using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>A minimal SSH agent holding one RSA key, on this platform's own transport (a named pipe or a Unix socket).</summary>
internal sealed class FakeAgent : IDisposable
{
    private readonly RSA _key = RSA.Create(2048);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serving;
    public bool RefuseSigning { get; set; }
    public string Address { get; }
    public int Signatures { get; private set; }

    public FakeAgent()
    {
        string name = "filecat-agent-" + Guid.NewGuid().ToString("N")[..12];
        if (OperatingSystem.IsWindows())
        {
            Address = @"\\.\pipe\" + name;
            _serving = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    try { await pipe.WaitForConnectionAsync(_stop.Token); }
                    catch (OperationCanceledException) { return; }
                    Serve(pipe);
                }
            });
        }
        else
        {
            Address = Path.Combine(Path.GetTempPath(), name + ".sock");
            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(Address));
            listener.Listen(4);
            _serving = Task.Run(async () =>
            {
                using (listener)
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        Socket client;
                        try { client = await listener.AcceptAsync(_stop.Token); }
                        catch (OperationCanceledException) { return; }
                        using var stream = new NetworkStream(client, ownsSocket: true);
                        Serve(stream);
                    }
                }
            });
        }
    }

    public byte[] Blob
    {
        get
        {
            var p = _key.ExportParameters(false);
            return Concat(String("ssh-rsa"u8), String(Mpint(p.Exponent!)), String(Mpint(p.Modulus!)));
        }
    }

    public bool Verify(byte[] data, byte[] signature)
    {
        int at = 0;
        string algorithm = Encoding.ASCII.GetString(Read(signature, ref at));
        var sig = Read(signature, ref at);
        var hash = algorithm switch { "rsa-sha2-256" => HashAlgorithmName.SHA256, "rsa-sha2-512" => HashAlgorithmName.SHA512, _ => default };
        return hash != default && _key.VerifyData(data, sig, hash, RSASignaturePadding.Pkcs1);
    }

    private void Serve(Stream stream)
    {
        var header = new byte[4];
        while (true)
        {
            try
            {
                if (!ReadExactly(stream, header)) return;
                var request = new byte[BinaryPrimitives.ReadUInt32BigEndian(header)];
                if (!ReadExactly(stream, request)) return;
                byte[] reply = request[0] switch
                {
                    11 => Concat([12], UInt32(1), String(Blob), String("test key"u8)),
                    13 => SignReply(request),
                    _ => [5],
                };
                stream.Write(UInt32((uint)reply.Length));
                stream.Write(reply);
                stream.Flush();
            }
            catch (IOException) { return; }
        }
    }

    private byte[] SignReply(byte[] request)
    {
        if (RefuseSigning) return [5];
        int at = 1;
        var blob = Read(request, ref at);
        var data = Read(request, ref at);
        uint flags = BinaryPrimitives.ReadUInt32BigEndian(request.AsSpan(at));
        if (!blob.AsSpan().SequenceEqual(Blob)) return [5];
        var (name, hash) = (flags & 4) != 0 ? ("rsa-sha2-512", HashAlgorithmName.SHA512) : (flags & 2) != 0 ? ("rsa-sha2-256", HashAlgorithmName.SHA256) : ("ssh-rsa", HashAlgorithmName.SHA1);
        Signatures++;
        return Concat([14], String(Concat(String(Encoding.ASCII.GetBytes(name)), String(_key.SignData(data, hash, RSASignaturePadding.Pkcs1)))));
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = stream.Read(buffer[done..]);
            if (n <= 0) return false;
            done += n;
        }
        return true;
    }

    private static byte[] Read(byte[] data, ref int at)
    {
        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
        var value = data.AsSpan(at + 4, length).ToArray();
        at += 4 + length;
        return value;
    }

    private static byte[] Mpint(byte[] unsigned) => unsigned[0] >= 0x80 ? [0, .. unsigned] : unsigned;
    private static byte[] UInt32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, v); return b; }
    private static byte[] String(ReadOnlySpan<byte> v) => Concat(UInt32((uint)v.Length), v.ToArray());
    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public void Dispose()
    {
        _stop.Cancel();
        try { _serving.Wait(2000); } catch (AggregateException) { }
        if (!OperatingSystem.IsWindows()) File.Delete(Address);
        _key.Dispose();
    }
}

public sealed class SshAgentTests
{
    [Fact]
    public void The_agent_lists_its_keys_and_signs_with_SHA2_never_SHA1()
    {
        using var agent = new FakeAgent();
        using var client = SshAgentClient.Connect(agent.Address);
        var keys = client.Identities();
        var key = Assert.Single(keys);
        Assert.Equal("test key", key.Comment);
        Assert.Equal("ssh-rsa", SshAgentClient.KeyType(key.Blob));
        var algorithms = client.Algorithms(keys);
        Assert.Equal(["rsa-sha2-512", "rsa-sha2-256"], algorithms.Select(a => a.Name));
        var challenge = RandomNumberGenerator.GetBytes(64);
        foreach (var algorithm in algorithms)
        {
            var signature = algorithm.Sign(challenge);
            Assert.True(agent.Verify(challenge, signature), algorithm.Name);
            Assert.Equal(key.Blob, algorithm.Data);
        }
    }

    [Fact]
    public void A_refusal_or_a_missing_agent_is_explained()
    {
        using var agent = new FakeAgent { RefuseSigning = true };
        using var client = SshAgentClient.Connect(agent.Address);
        var algorithm = client.Algorithms(client.Identities())[0];
        Assert.Contains("refused to sign", Assert.Throws<IOException>(() => algorithm.Sign([1, 2, 3])).Message, StringComparison.Ordinal);
        string nowhere = OperatingSystem.IsWindows() ? @"\\.\pipe\filecat-no-agent-" + Guid.NewGuid().ToString("N") : Path.Combine(Path.GetTempPath(), "filecat-no-agent.sock");
        Assert.Contains("No SSH agent answered", Assert.Throws<IOException>(() => SshAgentClient.Connect(nowhere)).Message, StringComparison.Ordinal);
    }

    /// <summary>Real OpenSSH: ssh-agent holds the test key, and sshd accepts it through FileCat (Linux and macOS CI).</summary>
    [Fact]
    public void A_real_server_accepts_a_key_that_only_the_agent_holds()
    {
        using var sshd = TestSshd.TryStart();
        if (sshd is null) Assert.Skip("No OpenSSH sshd here (install openssh-server or set FILECAT_SSHD).");
        string socket = Path.Combine(sshd.Directory, "agent.sock");
        var psi = new ProcessStartInfo("ssh-agent") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-D", "-a", socket }) psi.ArgumentList.Add(a);
        using var agentProcess = Process.Start(psi)!;
        try
        {
            for (int i = 0; i < 100 && !File.Exists(socket); i++) Thread.Sleep(50);
            var add = new ProcessStartInfo("ssh-add") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            add.ArgumentList.Add(Path.Combine(sshd.Directory, "client"));
            add.Environment["SSH_AUTH_SOCK"] = socket;
            using (var p = Process.Start(add)!)
            {
                p.WaitForExit();
                Assert.Equal(0, p.ExitCode);
            }
            var profile = sshd.Profile;
            profile.Auth = RemoteAuth.Agent;
            profile.KeyFile = null;
            var context = new ConnectContext
            {
                ApproveHostKey = key => key.AsSpan().SequenceEqual(sshd.HostKey),
                GetSecret = _ => throw new InvalidOperationException("Agent sign-in must not ask for a secret."),
                AnswerPrompts = (_, _) => null,
            };
            File.WriteAllText(Path.Combine(sshd.Files, "hello.txt"), "via agent");
            using var channel = new SshNetConnector(agentAddress: socket).Connect(profile, context, TestContext.Current.CancellationToken);
            Assert.Contains(channel.List(sshd.Files, TestContext.Current.CancellationToken), e => e.Name == "hello.txt");
        }
        finally
        {
            if (!agentProcess.HasExited) agentProcess.Kill();
        }
    }
}
