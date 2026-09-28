using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FileCat.Core.State;
using FileCat.Remote.Sftp;
using FluentFTP;
using FluentFTP.Exceptions;

namespace FileCat.Remote.Ftp;

/// <summary>
/// FTP and FTPS over FluentFTP (MIT, P8), behind the same channel as SFTP, so listings, transfers, uploads through
/// temporary names, and edit sessions behave alike. Differences stay explicit:
/// <list type="bullet">
/// <item>FTPS requires a certificate the OS validates, or one the user pinned; a changed certificate is never accepted silently.</item>
/// <item>Unencrypted FTP is only ever an explicit choice (ADR-17 addendum).</item>
/// <item>Transfers are binary, so content is never converted.</item>
/// <item>Names with line breaks are refused, since FTP commands end at a line break.</item>
/// <item>FTP has no atomic replace, so publishing deletes the old file just before the new one takes its name.</item>
/// </list>
/// </summary>
public sealed class FluentFtpConnector : ISftpConnector
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken ct)
    {
        bool anonymous = profile.User is "anonymous" or "ftp";
        string password = anonymous ? "filecat@" : context.GetSecret(false) ?? throw new ConnectCanceledException();
        var client = new FtpClient(profile.Host, profile.User.Length > 0 ? profile.User : "anonymous", password, profile.Port);
        var config = client.Config;
        config.EncryptionMode = profile.Protocol switch
        {
            RemoteProtocols.FtpExplicitTls => FtpEncryptionMode.Explicit,
            RemoteProtocols.FtpImplicitTls => FtpEncryptionMode.Implicit,
            _ => FtpEncryptionMode.None,
        };
        config.DataConnectionEncryption = true;
        config.ValidateAnyCertificate = false;
        config.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
        config.ReadTimeout = 60_000;
        config.DataConnectionConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
        config.DataConnectionReadTimeout = 60_000;
        config.SocketKeepAlive = true;
        config.RetryAttempts = 1;
        config.TimeConversion = FtpDate.UTC;
        config.DataConnectionType = FtpDataConnectionType.AutoPassive;
        client.Encoding = Encoding.UTF8;
        bool rejected = false;
        client.ValidateCertificate += (_, e) =>
        {
            e.Accept = context.ApproveCertificate(Describe(profile, e.Certificate, e.PolicyErrors));
            rejected |= !e.Accept;
        };
        try
        {
            using (ct.Register(client.Dispose))
            {
                client.Connect();
            }
            ct.ThrowIfCancellationRequested();
            return new FtpChannel(client);
        }
        catch (Exception ex) when (Unwrap(ex) is ConnectCanceledException or PromptDeferredException)
        {
            // A question was declined or deferred: that, not FluentFTP's wrapping, is what the user should see.
            client.Dispose();
            throw Unwrap(ex);
        }
        catch (Exception ex) when (rejected)
        {
            client.Dispose();
            throw new CertificateRejectedException($"The certificate of {profile.Display} was not accepted, so FileCat did not connect.", ex);
        }
        catch (FtpAuthenticationException ex)
        {
            client.Dispose();
            throw new RemoteAuthenticationException($"{profile.Display} did not accept the credentials: {ex.Message}", ex);
        }
        catch (FtpSecurityNotAvailableException ex)
        {
            client.Dispose();
            throw new IOException($"{profile.Display} does not offer encryption (TLS), so FileCat did not connect. Choose unencrypted FTP only if you accept that passwords and files travel in the clear.", ex);
        }
        catch (Exception ex) when (ex is FtpException or SocketException or TimeoutException or IOException or System.Security.Authentication.AuthenticationException)
        {
            client.Dispose();
            if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            throw new IOException($"Could not connect to {profile.Display}: {ex.Message}", ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static Exception Unwrap(Exception ex) =>
        ex is ConnectCanceledException or PromptDeferredException ? ex : ex.InnerException is { } inner ? Unwrap(inner) : ex;

    private static CertificateInfo Describe(RemoteProfile profile, X509Certificate certificate, SslPolicyErrors errors)
    {
        using var cert = new X509Certificate2(certificate);
        var problems = new List<string>();
        if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0) problems.Add($"It is not issued for \"{profile.Host}\".");
        if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0) problems.Add("No trusted authority vouches for it (it may be self-signed).");
        if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0) problems.Add("The server sent no certificate.");
        var now = DateTime.UtcNow;
        if (now < cert.NotBefore.ToUniversalTime() || now > cert.NotAfter.ToUniversalTime()) problems.Add("It is outside its validity period.");
        return new CertificateInfo(cert.Subject, cert.Issuer, cert.NotBefore.ToUniversalTime(), cert.NotAfter.ToUniversalTime(),
            Convert.ToHexString(SHA256.HashData(cert.RawData)), problems);
    }
}

/// <summary>The certificate an FTPS server presented, and why the OS does not accept it (empty when it does).</summary>
public sealed record CertificateInfo(string Subject, string Issuer, DateTime NotBeforeUtc, DateTime NotAfterUtc, string Sha256, IReadOnlyList<string> Problems);

/// <summary>The user or FileCat's trust store did not accept an FTPS server's certificate.</summary>
public sealed class CertificateRejectedException(string message, Exception? inner = null) : IOException(message, inner);

internal sealed class FtpChannel : ISftpChannel
{
    private readonly FtpClient _client;

    public FtpChannel(FtpClient client)
    {
        _client = client;
        HomeDirectory = Run(() => client.GetWorkingDirectory()) is { Length: > 0 } pwd ? pwd : "/";
    }

    public bool IsConnected => _client.IsConnected;

    public string HomeDirectory { get; }

    /// <summary>FTP commands end at a line break; a path containing one could smuggle another command.</summary>
    private static string Safe(string path) =>
        path.Contains('\r') || path.Contains('\n') ? throw new IOException("FTP cannot carry names with line breaks.") : path;

    /// <summary>FluentFTP's failures as the channel's contract: IOException and its kinds, never library types.</summary>
    internal T Run<T>(Func<T> action)
    {
        try { return action(); }
        catch (FtpCommandException ex) when (ex.CompletionCode is "550" or "450" && ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(ex.Message, ex);
        }
        catch (FtpCommandException ex) when (ex.CompletionCode is "530" or "532")
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
        catch (FtpCommandException ex)
        {
            throw new IOException($"The server refused: {ex.Message} ({ex.CompletionCode})", ex);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException || ex is IOException && !_client.IsConnected)
        {
            throw new RemoteDisconnectedException("The connection to the server was lost: " + ex.Message, ex);
        }
        catch (FtpException ex)
        {
            throw new IOException(ex.Message, ex);
        }
    }

    internal void Run(Action action) => Run(() =>
    {
        action();
        return true;
    });

    public IReadOnlyList<IRemoteEntry> List(string path, CancellationToken ct) => Run(() =>
    {
        var items = _client.GetListing(Safe(path), FtpListOption.AllFiles);
        var list = new List<IRemoteEntry>(items.Length);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.Name is "." or ".." or "") continue;
            list.Add(new FtpEntry(this, _client, item.Name, RemotePath.Combine(path, item.Name), item.Type == FtpObjectType.Directory,
                item.Type == FtpObjectType.Link, item.Type == FtpObjectType.Directory ? 0 : item.Size, Utc(item.Modified)));
        }
        return (IReadOnlyList<IRemoteEntry>)list;
    });

    private static DateTime Utc(DateTime t) => t == DateTime.MinValue ? DateTime.MinValue : t.Kind == DateTimeKind.Utc ? t : DateTime.SpecifyKind(t, DateTimeKind.Utc);

    public RemoteStat? Stat(string path) => Run(() =>
    {
        Safe(path);
        // MLST where the server has it (FluentFTP answers null otherwise), then SIZE, MDTM, and CWD. Each is a round trip,
        // and a copy stats every file a few times, so MLST's own answer is used whenever there is one.
        FtpListItem? info;
        try { info = _client.GetObjectInfo(path, false); }
        catch (FtpCommandException) { info = null; }
        if (info is not null)
        {
            // MLST's "modify" fact is the time in UTC (RFC 3659); MDTM only when a server leaves it out.
            var modified = info.Modified;
            if (modified == DateTime.MinValue && info.Type == FtpObjectType.File)
            {
                try { modified = _client.GetModifiedTime(path); }
                catch (FtpCommandException) { }
            }
            return new RemoteStat(info.Type == FtpObjectType.Directory, info.Type == FtpObjectType.Link, info.Type == FtpObjectType.Directory ? 0 : info.Size, Utc(modified));
        }
        // A server with MLST describes every path it has: no answer means nothing by this name (SIZE and CWD would agree).
        if (_client.HasFeature(FtpCapability.MLST)) return null;
        long size = _client.GetFileSize(path, -1);
        if (size >= 0)
        {
            DateTime modified;
            try { modified = Utc(_client.GetModifiedTime(path)); }
            catch (FtpCommandException) { modified = DateTime.MinValue; }
            return new RemoteStat(false, false, size, modified);
        }
        return _client.DirectoryExists(path) ? new RemoteStat(true, false, 0, DateTime.MinValue) : (RemoteStat?)null;
    });

    private bool Exists(string path) => Stat(path) is not null;

    public Stream OpenRead(string path)
    {
        var stat = Stat(path) ?? throw new FileNotFoundException("The file is not on the server.", path);
        if (stat.IsDirectory) throw new IOException("This is a folder.");
        return new FtpReadStream(this, _client, Safe(path), stat.Size);
    }

    public Stream CreateNew(string path)
    {
        Safe(path);
        // FTP has no exclusive create: FileCat writes only names it checked (and its own random temporary names).
        if (Exists(path)) throw new IOException("An item with this name already exists on the server.");
        var stream = Run(() => _client.OpenWrite(path, FtpDataType.Binary, false));
        return new FtpWriteStream(this, _client, stream);
    }

    /// <summary>FTP continues only at the end of a file (APPE), which the caller has checked is where the break was.</summary>
    public Stream OpenWriteAt(string path, long offset)
    {
        Safe(path);
        var stat = Stat(path) ?? throw new FileNotFoundException("The file is not on the server.", path);
        if (stat.Size != offset) throw new NotSupportedException("FTP continues a file only at its end.");
        var stream = Run(() => _client.OpenAppend(path, FtpDataType.Binary, false));
        return new FtpWriteStream(this, _client, stream);
    }

    public void CreateDirectory(string path)
    {
        Safe(path);
        if (Exists(path)) throw new IOException("An item with this name already exists on the server.");
        if (!Run(() => _client.CreateDirectory(path, false))) throw new IOException("The server did not create the folder.");
    }

    public void Rename(string source, string target)
    {
        Safe(source);
        Safe(target);
        // RNTO overwrites on many servers, so an existing target is refused first.
        if (Exists(target)) throw new IOException("An item with this name already exists on the server.");
        Run(() => _client.Rename(source, target));
    }

    /// <summary>FTP has no atomic replace; the caller deletes the old file just before renaming (and says so).</summary>
    public bool TryReplace(string source, string target) => false;

    public void SetModified(string path, DateTime utc)
    {
        if (!_client.HasFeature(FtpCapability.MFMT)) return;
        try { Run(() => _client.SetModifiedTime(Safe(path), utc)); }
        catch (IOException) when (_client.IsConnected) { } // a time is a courtesy; the content already arrived
    }

    /// <summary>Removes exactly one entry: RMD for an empty folder, DELE for a file or a link (never its target).</summary>
    internal void Delete(string path, bool directory) => Run(() =>
    {
        Safe(path);
        if (directory)
        {
            var reply = _client.Execute("RMD " + path);
            if (!reply.Success) throw new IOException($"The server did not remove the folder: {reply.Message} ({reply.Code})");
        }
        else _client.DeleteFile(path);
    });

    internal void Move(string source, string target) => Rename(source, target);

    public void Dispose() => _client.Dispose();

    /// <summary>A listed entry: changes act on exactly this name (a link is deleted or renamed itself).</summary>
    private sealed class FtpEntry(FtpChannel channel, FtpClient client, string name, string fullPath, bool isDirectory, bool isLink, long size, DateTime modifiedUtc) : IRemoteEntry
    {
        public string Name => name;
        public string FullPath => fullPath;
        public bool IsDirectory => isDirectory;
        public bool IsLink => isLink;
        public long Size => size;
        public DateTime ModifiedUtc => modifiedUtc;
        public void Delete() => channel.Delete(fullPath, isDirectory && !isLink);
        public void MoveTo(string newPath) => channel.Move(fullPath, newPath);
        public override string ToString() => $"{fullPath} ({(client.IsConnected ? "connected" : "closed")})";
    }
}

/// <summary>
/// A seekable read over FTP: data flows from one RETR at a time; a seek elsewhere ends it and resumes at the new offset
/// with REST, so viewers can jump through large files.
/// </summary>
internal sealed class FtpReadStream(FtpChannel channel, FtpClient client, string path, long length) : Stream
{
    private Stream? _data;
    private long _dataPosition;
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, length);
    }

    public override int Read(Span<byte> buffer)
    {
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            int n = Read(rented, 0, buffer.Length);
            rented.AsSpan(0, n).CopyTo(buffer);
            return n;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= length || count == 0) return 0;
        if (_data is null || _dataPosition != _position)
        {
            CloseData();
            long at = _position;
            _data = channel.Run(() => client.OpenRead(path, FtpDataType.Binary, at, false));
            _dataPosition = _position;
        }
        int n = channel.Run(() => _data!.Read(buffer, offset, count));
        _position += n;
        _dataPosition = _position;
        return n;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => length + offset,
    };

    /// <summary>Ends the current transfer and reads the server's final reply, so the control connection stays in step.</summary>
    private void CloseData()
    {
        if (_data is null) return;
        try
        {
            _data.Dispose();
            client.GetReply();
        }
        catch (Exception ex) when (ex is IOException or FtpException or TimeoutException) { }
        _data = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) CloseData();
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>An upload: closing it waits for the server's verdict, and a refusal surfaces as an error.</summary>
internal sealed class FtpWriteStream(FtpChannel channel, FtpClient client, Stream data) : Stream
{
    private bool _closed;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) => channel.Run(() => data.Write(buffer, offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var copy = buffer.ToArray();
        channel.Run(() => data.Write(copy));
    }

    public override void Flush() => channel.Run(data.Flush);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            data.Dispose();
            var reply = channel.Run(() => client.GetReply());
            if (!reply.Success) throw new IOException($"The server did not accept the file: {reply.Message} ({reply.Code})");
        }
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
