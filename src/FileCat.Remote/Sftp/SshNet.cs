using System.Net.Sockets;
using System.Reflection;
using FileCat.Core.State;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;

namespace FileCat.Remote.Sftp;

/// <summary>
/// SFTP over SSH.NET (MIT). Remote items are changed only through entries of a fresh listing: SSH.NET's path-based
/// delete and rename resolve the whole path with realpath first, which follows a link in the last part and would act
/// on the link's target (see ADR-17).
/// </summary>
/// <param name="agentAddress">The SSH agent to use for agent sign-in; null for this user's own (SSH_AUTH_SOCK, or on
/// Windows the OpenSSH agent service's pipe).</param>
public sealed class SshNetConnector(string? agentAddress = null) : ISftpConnector
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
    {
        var methods = new List<AuthenticationMethod>();
        PrivateKeyFile? key = null;
        SshAgentClient? agent = null;
        SftpClient? client = null;
        try
        {
            switch (profile.Auth)
            {
                case RemoteAuth.Key:
                    key = LoadKey(profile, context);
                    methods.Add(new PrivateKeyAuthenticationMethod(profile.User, key));
                    break;
                case RemoteAuth.Agent:
                    // Only needed while signing in: the agent signs the server's challenge, and the key never leaves it.
                    agent = SshAgentClient.Connect(agentAddress ?? SshAgentClient.DefaultAddress()
                        ?? throw new IOException("No SSH agent is known here: SSH_AUTH_SOCK is not set. Start ssh-agent and add your key with ssh-add."));
                    var identities = agent.Identities();
                    if (identities.Count == 0)
                        throw new IOException("The SSH agent holds no keys. Add your key to it (ssh-add, or unlock your password manager), then connect again.");
                    methods.Add(new PrivateKeyAuthenticationMethod(profile.User, new AgentKeySource(agent.Algorithms(identities))));
                    break;
                case RemoteAuth.KeyboardInteractive:
                    methods.Add(Interactive(profile, context));
                    break;
                default:
                    methods.Add(new PasswordAuthenticationMethod(profile.User, context.GetSecret(false) ?? throw new ConnectCanceledException()));
                    // Servers that take passwords only through keyboard-interactive (PAM) get the same password.
                    methods.Add(Interactive(profile, context));
                    break;
            }
            var info = new ConnectionInfo(profile.Host, profile.Port, profile.User, methods.ToArray()) { Timeout = ConnectTimeout };
            client = new SftpClient(info) { KeepAliveInterval = TimeSpan.FromSeconds(30), OperationTimeout = TimeSpan.FromMinutes(2) };
            client.HostKeyReceived += (_, e) => e.CanTrust = context.ApproveHostKey(e.HostKey);
            client.ConnectAsync(ct).GetAwaiter().GetResult();
            var channel = new SshNetChannel(client, key) { SocketBuffer = WidenSocketBuffers(client) };
            client = null;
            key = null;
            return channel;
        }
        catch (Exception ex) when (Unwrap(ex) is ConnectCanceledException or PromptDeferredException)
        {
            // A question was declined or deferred: that, not SSH.NET's wrapping, is what the user should see.
            throw Unwrap(ex);
        }
        catch (SshAuthenticationException ex)
        {
            throw new RemoteAuthenticationException(profile.Auth == RemoteAuth.Agent
                ? $"{profile.Display} accepted none of the SSH agent's keys for {profile.User}: {ex.Message}"
                : $"{profile.Display} did not accept the credentials: {ex.Message}", ex);
        }
        catch (SshConnectionException ex)
        {
            throw new IOException($"Could not connect to {profile.Display}: {RemoteErrorText.Reason(ex)}", ex);
        }
        catch (SshOperationTimeoutException ex)
        {
            throw new IOException($"{profile.Display} did not answer within {ConnectTimeout.TotalSeconds:0} seconds.", ex);
        }
        catch (SocketException ex)
        {
            throw new IOException($"Could not reach {profile.Display}: {ex.Message}", ex);
        }
        catch (SshException ex)
        {
            throw new IOException($"Could not connect to {profile.Display}: {RemoteErrorText.Reason(ex)}", ex);
        }
        finally
        {
            client?.Dispose();
            key?.Dispose();
            agent?.Dispose();
        }
    }

    /// <summary>
    /// The socket buffers a connection gets. SSH.NET fixes them once connected (2 × 68,536 bytes after ConnectAsync, 10 ×
    /// after Connect), which turns off the system's window tuning and holds a transfer to one buffer per round trip:
    /// 1.2 MB/s down and 1.7 up at 100 ms (release issue I41). At 4 MiB the same link carried 12.5 down and 8.4 up; at
    /// 16 MiB, beyond what Windows' window scaling advertises, reads stayed slow. On Linux the system caps the size
    /// (net.core.rmem_max).
    /// </summary>
    internal const int SocketBufferBytes = 4 << 20;

    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>SSH.NET has no setting for its socket; these are where 2026.0 keeps it (a test fails if they move).</summary>
    internal static readonly PropertyInfo? SessionProperty = typeof(BaseClient).GetProperty("Session", Hidden);

    internal static readonly FieldInfo? SocketField = typeof(Session).GetField("_socket", Hidden);

    /// <summary>
    /// Raises a connected client's socket buffers to <see cref="SocketBufferBytes"/>; returns the receive buffer the
    /// system then reports, or -1 where the socket cannot be reached (the connection works as SSH.NET made it).
    /// </summary>
    internal static int WidenSocketBuffers(SftpClient client)
    {
        try
        {
            if (SessionProperty?.GetValue(client) is not Session session || SocketField?.GetValue(session) is not Socket socket) return -1;
            socket.ReceiveBufferSize = SocketBufferBytes;
            socket.SendBufferSize = SocketBufferBytes;
            return socket.ReceiveBufferSize;
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or TargetInvocationException or ArgumentException)
        {
            return -1;
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is not (ConnectCanceledException or PromptDeferredException) && ex.InnerException is { } inner) ex = inner;
        return ex;
    }

    private static PrivateKeyFile LoadKey(RemoteProfile profile, ConnectContext context)
    {
        if (string.IsNullOrWhiteSpace(profile.KeyFile)) throw new IOException("No private key file is set for this connection.");
        if (!File.Exists(profile.KeyFile)) throw new IOException($"The private key file \"{profile.KeyFile}\" does not exist.");
        try
        {
            return new PrivateKeyFile(profile.KeyFile);
        }
        catch (SshPassPhraseNullOrEmptyException)
        {
            string passphrase = context.GetSecret(true) ?? throw new ConnectCanceledException();
            try
            {
                return new PrivateKeyFile(profile.KeyFile, passphrase);
            }
            catch (Exception ex) when (ex is SshException or InvalidOperationException or ArgumentException or FormatException)
            {
                throw new RemoteAuthenticationException("The key passphrase is not correct.", ex);
            }
        }
        catch (Exception ex) when (ex is SshException or InvalidOperationException or ArgumentException or FormatException)
        {
            throw new IOException($"The private key file could not be read: {ex.Message}", ex);
        }
    }

    private static KeyboardInteractiveAuthenticationMethod Interactive(RemoteProfile profile, ConnectContext context)
    {
        var method = new KeyboardInteractiveAuthenticationMethod(profile.User);
        method.AuthenticationPrompt += (_, e) =>
        {
            var answers = context.AnswerPrompts(e.Instruction ?? "", e.Prompts.Select(p => (p.Request, p.IsEchoed)).ToList());
            if (answers is null) throw new ConnectCanceledException();
            for (int i = 0; i < e.Prompts.Count && i < answers.Count; i++) e.Prompts[i].Response = answers[i];
        };
        return method;
    }
}

internal sealed class SshNetChannel(SftpClient client, IDisposable? key) : ISftpChannel
{
    /// <summary>The connection's socket receive buffer as the system reports it after widening (-1: not reached).</summary>
    internal int SocketBuffer { get; init; } = -1;

    public bool IsConnected => client.IsConnected;

    public string HomeDirectory { get; } = client.WorkingDirectory;

    public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => Wrap(() =>
    {
        var entries = new List<IRemoteEntry>();
        foreach (var file in client.ListDirectory(path))
        {
            ct.ThrowIfCancellationRequested();
            if (file.Name is "." or "..") continue;
            entries.Add(new Entry(file, RemotePath.Combine(path, file.Name)));
        }
        return (IReadOnlyList<IRemoteEntry>)entries;
    });

    public RemoteStat? Stat(string path) => Wrap<RemoteStat?>(() =>
    {
        try
        {
            var a = client.GetAttributes(path);
            return new RemoteStat(a.IsDirectory, a.IsSymbolicLink, a.Size, a.LastWriteTimeUtc);
        }
        catch (SftpPathNotFoundException)
        {
            return null;
        }
    });

    public Stream OpenRead(string path) => Wrap<Stream>(() => new ChannelStream(client.Open(path, FileMode.Open, FileAccess.Read)));

    public Stream CreateNew(string path) => Wrap<Stream>(() => new ChannelStream(client.Open(path, FileMode.CreateNew, FileAccess.Write)));

    public bool AppendsOneRequestAtATime => true;

    /// <summary>SSH.NET's upload keeps many write requests in flight (5.7 MB/s where stream writes made 0.29 at 100 ms).</summary>
    public void UploadNew(Stream input, string path) => Wrap(() =>
    {
        // canOverride false: opened with create-exclusive, as CreateNew is.
        client.UploadFile(input, path, canOverride: false);
        return true;
    });

    public Stream OpenWriteAt(string path, long offset) => Wrap<Stream>(() =>
    {
        var stream = client.Open(path, FileMode.Open, FileAccess.Write); // never truncates
        stream.Seek(offset, SeekOrigin.Begin);
        return new ChannelStream(stream);
    });

    public void CreateDirectory(string path) => Wrap(() => client.CreateDirectory(path));

    public void Rename(string source, string target) => Wrap(() => client.RenameFile(source, target, isPosix: false));

    public bool TryReplace(string source, string target) => Wrap(() =>
    {
        try
        {
            client.RenameFile(source, target, isPosix: true);
            return true;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    });

    public void SetModified(string path, DateTime utc)
    {
        // Some servers refuse to set times (no SETSTAT); the content already arrived, and the job's stat tells.
        try { Wrap(() => client.SetLastWriteTimeUtc(path, utc)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && ex is not RemoteDisconnectedException && client.IsConnected) { }
    }

    public void Dispose()
    {
        try { client.Dispose(); }
        catch (Exception ex) when (ex is SshException or SocketException or ObjectDisposedException) { }
        key?.Dispose();
    }

    internal static void Wrap(Action action) => Wrap(() =>
    {
        action();
        return 0;
    });

    /// <summary>SSH.NET errors as the exceptions jobs already understand.</summary>
    internal static T Wrap<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (SftpPathNotFoundException ex)
        {
            throw new FileNotFoundException("The item does not exist on the server: " + ex.Message, ex);
        }
        catch (SftpPermissionDeniedException ex)
        {
            throw new UnauthorizedAccessException("The server denied permission: " + ex.Message, ex);
        }
        catch (SshConnectionException ex)
        {
            throw new RemoteDisconnectedException("The connection to the server was lost: " + RemoteErrorText.Reason(ex), ex);
        }
        catch (SshOperationTimeoutException ex)
        {
            throw new RemoteDisconnectedException("The server stopped answering.", ex);
        }
        catch (ObjectDisposedException ex)
        {
            throw new RemoteDisconnectedException("The connection was closed.", ex);
        }
        catch (SocketException ex)
        {
            throw new RemoteDisconnectedException("The connection to the server was lost: " + RemoteErrorText.Reason(ex), ex);
        }
        catch (SshException ex)
        {
            throw new IOException(RemoteErrorText.Reason(ex), ex);
        }
    }

    private sealed class Entry(ISftpFile file, string fullPath) : IRemoteEntry
    {
        public string Name => file.Name;
        public string FullPath => fullPath;
        public bool IsDirectory => file.IsDirectory;
        public bool IsLink => file.IsSymbolicLink;
        public long Size => file.Length;
        public DateTime ModifiedUtc => file.LastWriteTimeUtc;

        // SftpFile acts on its own listed path (remove, rmdir, rename) without realpath.
        public void Delete() => Wrap(file.Delete);

        public void MoveTo(string newPath) => Wrap(() => file.MoveTo(newPath));
    }

    /// <summary>An SFTP file stream whose errors surface like local I/O errors.</summary>
    private sealed class ChannelStream(SftpFileStream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => Wrap(() => inner.Length);

        public override long Position
        {
            get => Wrap(() => inner.Position);
            set => Wrap(() => inner.Position = value);
        }

        public override void Flush() => Wrap(inner.Flush);
        public override int Read(byte[] buffer, int offset, int count) => Wrap(() => inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer)
        {
            int length = buffer.Length;
            var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
            try
            {
                int n = Wrap(() => inner.Read(rented, 0, length));
                rented.AsSpan(0, n).CopyTo(buffer);
                return n;
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }

        public override long Seek(long offset, SeekOrigin origin) => Wrap(() => inner.Seek(offset, origin));
        public override void SetLength(long value) => Wrap(() => inner.SetLength(value));
        public override void Write(byte[] buffer, int offset, int count) => Wrap(() => inner.Write(buffer, offset, count));

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { inner.Dispose(); }
                catch (Exception ex) when (ex is SshException or SocketException or ObjectDisposedException) { }
            }
            base.Dispose(disposing);
        }
    }
}
