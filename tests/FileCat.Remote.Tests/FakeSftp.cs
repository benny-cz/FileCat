using System.Buffers.Binary;
using System.Text;
using FileCat.Core.State;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

/// <summary>
/// An in-memory SFTP server with OpenSSH semantics: listings report links as links (lstat), reads follow links,
/// exclusive creates and plain renames never overwrite, posix-rename replaces, and removing a link removes only the link.
/// </summary>
internal sealed class FakeSftpServer
{
    public sealed class Node
    {
        public required string Name;
        public bool IsDirectory;
        public string? LinkTarget;
        public byte[] Data = [];
        public DateTime Modified = new(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        public readonly SortedDictionary<string, Node> Children = new(StringComparer.Ordinal);
    }

    public readonly object Lock = new();
    public Node Root { get; } = new() { Name = "", IsDirectory = true };
    public string Home { get; set; } = "/home/user";
    public byte[] HostKey { get; set; } = KeyBlob("ssh-ed25519", 1);
    public string Password { get; set; } = "secret";
    public bool PosixRename { get; set; } = true;
    public int Connects;
    public int ActiveChannels;
    /// <summary>When set, every channel operation fails as if the connection dropped.</summary>
    public bool Down { get; set; }

    /// <summary>Drops the connection once a written file would pass this many bytes (what arrived before stays).</summary>
    public long? DropAfterBytes { get; set; }

    /// <summary>The offsets uploads were continued at.</summary>
    public List<long> WritesAt { get; } = [];

    /// <summary>A faulty server or disk: the next written file keeps the byte at this offset inverted.</summary>
    public long? CorruptAt { get; set; }

    /// <summary>False: a server that does not set times when asked (vsftpd without MFMT, before I43), and says nothing.</summary>
    public bool KeepsTimes { get; set; } = true;

    public FakeSftpServer() => Dir(Home);

    public static byte[] KeyBlob(string type, byte seed)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var blob = new byte[4 + typeBytes.Length + 4 + 32];
        BinaryPrimitives.WriteUInt32BigEndian(blob, (uint)typeBytes.Length);
        typeBytes.CopyTo(blob, 4);
        BinaryPrimitives.WriteUInt32BigEndian(blob.AsSpan(4 + typeBytes.Length), 32);
        for (int i = 0; i < 32; i++) blob[8 + typeBytes.Length + i] = (byte)(seed * 31 + i);
        return blob;
    }

    public Node Dir(string path)
    {
        lock (Lock)
        {
            var node = Root;
            foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (!node.Children.TryGetValue(part, out var child))
                    node.Children[part] = child = new Node { Name = part, IsDirectory = true };
                node = child;
            }
            return node;
        }
    }

    public Node File(string path, string content)
    {
        lock (Lock)
        {
            var parent = Dir(RemotePath.Parent(path)!);
            var node = new Node { Name = RemotePath.Name(path), Data = Encoding.UTF8.GetBytes(content) };
            parent.Children[node.Name] = node;
            return node;
        }
    }

    public void Link(string path, string target)
    {
        lock (Lock)
        {
            var parent = Dir(RemotePath.Parent(path)!);
            parent.Children[RemotePath.Name(path)] = new Node { Name = RemotePath.Name(path), LinkTarget = target };
        }
    }

    public string Read(string path) => Encoding.UTF8.GetString(Lookup(path, followFinal: true)!.Data);

    public bool Exists(string path) => Lookup(path, followFinal: false) is not null;

    /// <summary>Resolves a path; intermediate links are always followed, the final one only when asked.</summary>
    public Node? Lookup(string path, bool followFinal, int depth = 0)
    {
        if (depth > 16) return null;
        lock (Lock)
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var node = Root;
            string at = "/";
            for (int i = 0; i < parts.Length; i++)
            {
                if (!node.IsDirectory || !node.Children.TryGetValue(parts[i], out var child)) return null;
                at = RemotePath.Combine(at, parts[i]);
                bool last = i == parts.Length - 1;
                if (child.LinkTarget is { } target && (!last || followFinal))
                {
                    string resolved = target.StartsWith('/') ? target : RemotePath.Combine(RemotePath.Parent(at)!, target);
                    child = Lookup(resolved, followFinal: true, depth + 1);
                    if (child is null) return null;
                }
                node = child;
            }
            return node;
        }
    }

    public (Node Parent, string Name) ParentOf(string path)
    {
        var parent = Lookup(RemotePath.Parent(path) ?? "/", followFinal: true) ?? throw new FileNotFoundException("No such folder: " + path);
        if (!parent.IsDirectory) throw new IOException("Not a folder.");
        return (parent, RemotePath.Name(path));
    }
}

internal sealed class FakeConnector(FakeSftpServer server) : ISftpConnector
{
    public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
    {
        if (server.Down) throw new IOException("Could not reach the server.");
        if (!context.ApproveHostKey(server.HostKey)) throw new IOException("Key exchange negotiation failed.");
        if (profile.Auth == RemoteAuth.Password && context.GetSecret(false) != server.Password)
            throw new RemoteAuthenticationException("Permission denied.");
        if (profile.Auth == RemoteAuth.KeyboardInteractive)
        {
            var answers = context.AnswerPrompts("", [("Password: ", false)]);
            if (answers is not [var answer] || answer != server.Password) throw new RemoteAuthenticationException("Permission denied.");
        }
        Interlocked.Increment(ref server.Connects);
        Interlocked.Increment(ref server.ActiveChannels);
        return new FakeChannel(server);
    }
}

internal sealed class FakeChannel(FakeSftpServer server) : ISftpChannel
{
    public FakeSftpServer Server => server;

    private bool _closed;

    public bool IsConnected => !_closed && !server.Down;

    public string HomeDirectory => server.Home;

    private void Check()
    {
        if (!IsConnected) throw new RemoteDisconnectedException("The connection to the server was lost.");
    }

    public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct)
    {
        Check();
        lock (server.Lock)
        {
            var dir = server.Lookup(path, followFinal: true) ?? throw new FileNotFoundException("No such folder: " + path);
            if (!dir.IsDirectory) throw new IOException("Not a folder: " + path);
            return dir.Children.Values.Select(n => (IRemoteEntry)new Entry(this, server, n, RemotePath.Combine(path, n.Name))).ToList();
        }
    }

    public RemoteStat? Stat(string path)
    {
        Check();
        lock (server.Lock)
        {
            var node = server.Lookup(path, followFinal: true) ?? server.Lookup(path, followFinal: false);
            if (node is null) return null;
            return new RemoteStat(node.IsDirectory, node.LinkTarget is not null, node.Data.Length, node.Modified);
        }
    }

    public Stream OpenRead(string path)
    {
        Check();
        lock (server.Lock)
        {
            var node = server.Lookup(path, followFinal: true) ?? throw new FileNotFoundException("No such file: " + path);
            if (node.IsDirectory) throw new IOException("It is a folder.");
            return new MemoryStream(node.Data.ToArray(), writable: false);
        }
    }

    public Stream CreateNew(string path)
    {
        Check();
        lock (server.Lock)
        {
            var (parent, name) = server.ParentOf(path);
            if (parent.Children.ContainsKey(name)) throw new IOException("The file exists: " + path);
            var node = new FakeSftpServer.Node { Name = name };
            parent.Children[name] = node;
            return new CommitStream(this, node);
        }
    }

    public Stream OpenWriteAt(string path, long offset)
    {
        Check();
        lock (server.Lock)
        {
            var node = server.Lookup(path, followFinal: false) ?? throw new FileNotFoundException("No such file: " + path);
            server.WritesAt.Add(offset);
            var stream = new CommitStream(this, node);
            stream.Write(node.Data.AsSpan(0, (int)Math.Min(offset, node.Data.Length)));
            return stream;
        }
    }

    public void CreateDirectory(string path)
    {
        Check();
        lock (server.Lock)
        {
            var (parent, name) = server.ParentOf(path);
            if (parent.Children.ContainsKey(name)) throw new IOException("It exists: " + path);
            parent.Children[name] = new FakeSftpServer.Node { Name = name, IsDirectory = true };
        }
    }

    public void Rename(string source, string target)
    {
        Check();
        lock (server.Lock)
        {
            var (from, fromName) = server.ParentOf(source);
            var (to, toName) = server.ParentOf(target);
            if (!from.Children.TryGetValue(fromName, out var node)) throw new FileNotFoundException("No such item: " + source);
            if (to.Children.ContainsKey(toName)) throw new IOException("The target exists: " + target);
            from.Children.Remove(fromName);
            node.Name = toName;
            to.Children[toName] = node;
        }
    }

    public bool TryReplace(string source, string target)
    {
        Check();
        if (!server.PosixRename) return false;
        lock (server.Lock)
        {
            var (from, fromName) = server.ParentOf(source);
            var (to, toName) = server.ParentOf(target);
            if (!from.Children.TryGetValue(fromName, out var node)) throw new FileNotFoundException("No such item: " + source);
            if (to.Children.TryGetValue(toName, out var existing) && existing.IsDirectory) throw new IOException("The target is a folder.");
            from.Children.Remove(fromName);
            node.Name = toName;
            to.Children[toName] = node;
            return true;
        }
    }

    public void SetModified(string path, DateTime utc)
    {
        Check();
        lock (server.Lock)
        {
            var node = server.Lookup(path, followFinal: true) ?? throw new FileNotFoundException(path);
            if (server.KeepsTimes) node.Modified = utc;
        }
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        Interlocked.Decrement(ref server.ActiveChannels);
    }

    private sealed class Entry(FakeChannel channel, FakeSftpServer server, FakeSftpServer.Node node, string fullPath) : IRemoteEntry
    {
        public string Name => node.Name;
        public string FullPath => fullPath;
        public bool IsDirectory => node.IsDirectory;
        public bool IsLink => node.LinkTarget is not null;
        public long Size => node.Data.Length;
        public DateTime ModifiedUtc => node.Modified;

        public void Delete()
        {
            channel.Check();
            lock (server.Lock)
            {
                var (parent, name) = server.ParentOf(fullPath);
                if (!parent.Children.TryGetValue(name, out var current) || !ReferenceEquals(current, node)) throw new FileNotFoundException("It changed: " + fullPath);
                if (node.IsDirectory && node.Children.Count > 0) throw new IOException("The folder is not empty.");
                parent.Children.Remove(name);
            }
        }

        public void MoveTo(string newPath)
        {
            channel.Check();
            lock (server.Lock)
            {
                var (parent, name) = server.ParentOf(fullPath);
                var (to, toName) = server.ParentOf(newPath);
                if (!parent.Children.TryGetValue(name, out var current) || !ReferenceEquals(current, node)) throw new FileNotFoundException("It changed: " + fullPath);
                if (to.Children.ContainsKey(toName)) throw new IOException("The target exists: " + newPath);
                parent.Children.Remove(name);
                node.Name = toName;
                to.Children[toName] = node;
            }
        }
    }

    /// <summary>Writes land in the node as they happen, like a server file being written.</summary>
    private sealed class CommitStream(FakeChannel channel, FakeSftpServer.Node node) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            channel.Check();
            // A scripted drop: the server keeps what arrived before the connection went.
            if (channel.Server.DropAfterBytes is { } limit && Length + count > limit)
            {
                int kept = (int)Math.Max(0, limit - Length);
                base.Write(buffer, offset, kept);
                node.Data = ToArray();
                channel.Server.DropAfterBytes = null;
                channel.Server.Down = true;
                throw new RemoteDisconnectedException("The connection to the server was lost.");
            }
            base.Write(buffer, offset, count);
            Stored();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            channel.Check();
            base.Write(buffer);
            Stored();
        }

        private void Stored()
        {
            if (channel.Server.CorruptAt is { } at && at < Length)
            {
                GetBuffer()[at] ^= 0xFF;
                channel.Server.CorruptAt = null;
            }
            node.Data = ToArray();
        }
    }
}

/// <summary>Scripted answers for connection prompts.</summary>
internal sealed class ScriptedInteraction : IRemoteInteraction
{
    public HostKeyDecision HostKeyAnswer { get; set; } = HostKeyDecision.AcceptAndRemember;
    public Queue<string?> Secrets { get; } = new();
    public bool SaveSecret { get; set; }
    public List<HostKeyCheck> HostKeyQuestions { get; } = [];
    public List<SecretRequest> SecretQuestions { get; } = [];

    public HostKeyDecision DecideHostKey(RemoteProfile profile, HostKeyCheck check)
    {
        HostKeyQuestions.Add(check);
        return HostKeyAnswer;
    }

    public SecretAnswer? AskSecret(RemoteProfile profile, SecretRequest request)
    {
        SecretQuestions.Add(request);
        return Secrets.Count > 0 && Secrets.Dequeue() is { } s ? new SecretAnswer(s, SaveSecret) : null;
    }

    public IReadOnlyList<string>? AnswerPrompts(RemoteProfile profile, string instruction, IReadOnlyList<(string Prompt, bool Echo)> prompts) =>
        Secrets.Count > 0 && Secrets.Dequeue() is { } s ? [s] : null;
}
