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
        // Names as they are (release issue I36): FluentFTP's heuristics refuse legitimate names (";", "|", tabs, "%",
        // "..", bidirectional marks), and its "rename" mode would change names silently. What FTP cannot carry exactly
        // is refused by the channel itself (Safe); line breaks stay refused by the library too.
        config.SanitizeMode = FtpSanitize.Throw;
        config.SanitizeMultiline = true;
        config.SanitizeControlChars = false;
        config.SanitizeUrlEncoding = false;
        config.SanitizeTraversal = false;
        config.SanitizeUnicodeSpoofing = false;
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
            throw new IOException($"Could not connect to {profile.Display}: {RemoteErrorText.Reason(ex)}", ex);
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

internal sealed partial class FtpChannel : ISftpChannel
{
    private readonly FtpClient _client;

    public FtpChannel(FtpClient client)
    {
        _client = client;
        HomeDirectory = Run(() => client.GetWorkingDirectory()) is { Length: > 0 } pwd ? pwd : "/";
    }

    public bool IsConnected => _client.IsConnected;

    public string HomeDirectory { get; }

    /// <summary>
    /// Paths FTP cannot carry exactly, refused before anything is sent: a line break ends the command (and could smuggle
    /// another), NUL ends a name at the server, and FluentFTP turns every backslash into a folder separator and trims
    /// spaces from both ends of every path, so a name with a backslash, or one ending the path with a space, would reach a
    /// different item (release issue I36). SFTP carries all of these.
    /// </summary>
    internal static string Safe(string path) =>
        path.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0 ? throw new IOException("FTP cannot carry names with line breaks or NUL characters.")
        : path.Contains('\\') ? throw new IOException("This name contains a backslash, which FileCat's FTP connection would turn into a folder separator, reaching a different item; nothing was done with it. SFTP handles such names.")
        : path.Length > 0 && (char.IsWhiteSpace(path[0]) || char.IsWhiteSpace(path[^1]))
            ? throw new IOException("This name ends with a space (or the path begins with one), which FileCat's FTP connection would drop, reaching a different item; nothing was done with it. SFTP handles such names.")
        : path;

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
            throw new IOException($"The server refused: {ReplyText(ex)} ({ex.CompletionCode})", ex);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException || ex is IOException && !_client.IsConnected)
        {
            throw new RemoteDisconnectedException("The connection to the server was lost: " + RemoteErrorText.Reason(ex), ex);
        }
        catch (FtpException ex)
        {
            throw new IOException(RemoteErrorText.Reason(ex), ex);
        }
    }

    /// <summary>The server's own words: FluentFTP's message repeats the code first ("Code: 451 Message: …").</summary>
    internal static string ReplyText(FtpCommandException ex) => ReplyPrefix().Replace(ex.Message, "");

    [System.Text.RegularExpressions.GeneratedRegex(@"^Code:\s*\d+\s*Message:\s*")]
    private static partial System.Text.RegularExpressions.Regex ReplyPrefix();

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
            string name = ExactName(item.Name, item.Input, item.Type == FtpObjectType.Link ? item.LinkTarget : null);
            if (name is "." or ".." or "") continue;
            list.Add(new FtpEntry(this, _client, name, RemotePath.Combine(path, name), item.Type == FtpObjectType.Directory,
                item.Type == FtpObjectType.Link, item.Type == FtpObjectType.Directory ? 0 : item.Size, Utc(item.Modified), TimePrecision(item.Input)));
        }
        return (IReadOnlyList<IRemoteEntry>)list;
    });

    /// <summary>The fields of a Unix-style listing line before the name, up to the one space that precedes it.</summary>
    [System.Text.RegularExpressions.GeneratedRegex(@"^[-bcdlps][-rwxsStTlL]{9}[+@.]?\s+\d+\s+(?:\S+\s+){1,2}\d+\s+\S{3,4}\s+\d{1,2}\s+(?:\d{1,2}:\d{2}|\d{4}) ")]
    private static partial System.Text.RegularExpressions.Regex UnixListingPrefix();

    /// <summary>
    /// The name as the server has it (release issue I36). FluentFTP's parser of Unix-style listings — what servers
    /// without MLSD, such as vsftpd, send — trims spaces at the edges of names, so " notes" was listed as "notes" and
    /// then reached as that, possibly another item. In that format exactly one space separates the time or year from the
    /// name, so the name is taken from the listing's own line, when the line has that shape and agrees with the parser
    /// apart from spaces at the edges; otherwise the parser's name stands. <paramref name="linkTarget"/>: a link's
    /// target, which follows its name after " -> ".
    /// </summary>
    internal static string ExactName(string parsed, string? line, string? linkTarget)
    {
        if (string.IsNullOrEmpty(line) || UnixListingPrefix().Match(line) is not { Success: true } prefix) return parsed;
        string rest = line[prefix.Length..];
        if (!string.IsNullOrEmpty(linkTarget))
        {
            string arrow = " -> " + linkTarget;
            if (!rest.EndsWith(arrow, StringComparison.Ordinal)) return parsed;
            rest = rest[..^arrow.Length];
        }
        return rest.Length > 0 && rest.Trim() == parsed.Trim() ? rest : parsed;
    }

    private static DateTime Utc(DateTime t) => t == DateTime.MinValue ? DateTime.MinValue : t.Kind == DateTimeKind.Utc ? t : DateTime.SpecifyKind(t, DateTimeKind.Utc);

    /// <summary>
    /// How precisely a listing line states its time (release issue I45): MLSD's "modify=" fact to the second; a Unix-style
    /// LIST line (vsftpd's) to the minute for the last half year ("Oct 01 05:09") and to the day for older files
    /// ("Mar 04  2021"); other LIST formats (DOS-style "10-01-26  05:09AM") to the minute.
    /// </summary>
    internal static TimeSpan TimePrecision(string? line)
    {
        if (string.IsNullOrEmpty(line) || line.Contains("modify=", StringComparison.OrdinalIgnoreCase)) return TimeSpan.Zero;
        if (UnixListingPrefix().Match(line) is { Success: true } prefix)
        {
            // The prefix ends with the time ("05:09") or the year ("2021"), then the one space before the name.
            string fields = prefix.Value.TrimEnd();
            return fields[(fields.LastIndexOf(' ') + 1)..].Contains(':') ? TimeSpan.FromMinutes(1) : TimeSpan.FromDays(1);
        }
        return TimeSpan.FromMinutes(1);
    }

    public RemoteStat? Stat(string path) => Run(() =>
    {
        Safe(path);
        // MLST where the server has it, then SIZE, MDTM, and CWD. Each is a round trip, and a copy stats every file a few
        // times, so MLST's own answer is used whenever there is one. Without MLST, FluentFTP's GetObjectInfo lists the
        // whole parent folder instead (a data connection and every name in it, for each stat: 2.7 s per small file at a
        // 100 ms round trip, and growing with the folder), and reports a link rather than following it.
        FtpListItem? info = null;
        if (_client.HasFeature(FtpCapability.MLST))
        {
            try { info = _client.GetObjectInfo(path, false); }
            catch (FtpCommandException) { info = null; }
        }
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

    /// <summary>A file the caller has just stat'ed: its length is known, so no second stat (FTP needs one for the length).</summary>
    public Stream OpenRead(string path, long length) => new FtpReadStream(this, _client, Safe(path), length);

    public Stream CreateNew(string path)
    {
        Safe(path);
        // FTP has no exclusive create: FileCat writes only names it checked (and its own random temporary names).
        if (Exists(path)) throw new IOException("An item with this name already exists on the server.");
        var stream = Run(() => _client.OpenWrite(path, FtpDataType.Binary, false));
        return new FtpWriteStream(this, _client, stream);
    }

    /// <summary>
    /// FTP continues only at the end of a file (APPE), which the caller has checked is where the break was. A server that
    /// refuses to append — ProFTPD does unless AllowStoreRestart is on ("451 Append/Restart not permitted, try again") —
    /// cannot continue: the upload starts again (release issue I47), rather than asking the same question forever.
    /// </summary>
    public Stream OpenWriteAt(string path, long offset)
    {
        Safe(path);
        var stat = Stat(path) ?? throw new FileNotFoundException("The file is not on the server.", path);
        if (stat.Size != offset) throw new NotSupportedException("FTP continues a file only at its end.");
        var stream = Run(() =>
        {
            try { return _client.OpenAppend(path, FtpDataType.Binary, false); }
            catch (FtpCommandException ex) when (ex.CompletionCode is not "421")
            {
                throw new NotSupportedException($"the server does not continue uploads ({ex.CompletionCode} {ReplyText(ex)})", ex);
            }
        });
        return new FtpWriteStream(this, _client, stream);
    }

    /// <summary>How long the server's verdict is waited for after a transfer's data connection broke.</summary>
    internal static readonly TimeSpan BreakReplyWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The server's final reply after a transfer whose data connection broke, waited for briefly: a server that ended
    /// the transfer itself says why (a full disk, a quota), while a session that ended never answers — waiting for it
    /// took the whole 60 s read timeout when the lab's ProFTPD session was killed (I47). Null when no reply came; the
    /// connection is then ended at once (without QUIT), which also releases the wait, and the job's Retry reconnects.
    /// </summary>
    internal FtpReply? ReplyAfterBreak()
    {
        var waiting = Task.Run(() =>
        {
            try { return (FtpReply?)_client.GetReply(); }
            catch (Exception ex) when (ex is IOException or FtpException or SocketException or ObjectDisposedException or TimeoutException or InvalidOperationException) { return null; }
        });
        if (waiting.Wait(BreakReplyWait)) return waiting.Result;
        try
        {
            _client.Config.DisconnectWithQuit = false;
            _client.Disconnect();
        }
        catch (Exception ex) when (ex is IOException or FtpException or SocketException or ObjectDisposedException or TimeoutException or InvalidOperationException) { }
        waiting.Wait(BreakReplyWait);
        return null;
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

    /// <summary>
    /// MFMT where the server offers it (RFC draft, most servers); otherwise MDTM with a time, which vsftpd takes as setting
    /// it (when the argument is not itself an existing name) and other servers answer as a question about a file of that
    /// odd name, changing nothing. Without this, uploads to vsftpd carried the time they arrived (I43).
    /// </summary>
    public void SetModified(string path, DateTime utc)
    {
        Safe(path);
        string time = utc.ToUniversalTime().ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        // A time is a courtesy: the content already arrived, and the job's stat afterwards says whether the time held.
        try { Run(() => _client.HasFeature(FtpCapability.MFMT) ? _client.Execute($"MFMT {time} {path}") : _client.Execute($"MDTM {time} {path}")); }
        catch (IOException) when (_client.IsConnected) { }
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
    private sealed class FtpEntry(FtpChannel channel, FtpClient client, string name, string fullPath, bool isDirectory, bool isLink, long size, DateTime modifiedUtc, TimeSpan precision) : IRemoteEntry
    {
        public string Name => name;
        public string FullPath => fullPath;
        public bool IsDirectory => isDirectory;
        public bool IsLink => isLink;
        public long Size => size;
        public DateTime ModifiedUtc => modifiedUtc;
        public TimeSpan ModifiedPrecision => precision;
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
        int n;
        try { n = channel.Run(() => _data!.Read(buffer, offset, count)); }
        catch
        {
            _broken = true;
            throw;
        }
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

    /// <summary>
    /// Ends the current transfer and reads the server's final reply, so the control connection stays in step; after the
    /// transfer broke, the connection is ended instead (the reply may never come; I47).
    /// </summary>
    private void CloseData()
    {
        if (_data is null) return;
        try
        {
            _data.Dispose();
            if (_broken) channel.ReplyAfterBreak();
            else client.GetReply();
        }
        catch (Exception ex) when (ex is IOException or FtpException or TimeoutException) { }
        _data = null;
        _broken = false;
    }

    private bool _broken;

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

    /// <summary>A write's failure: the data connection broke, and the server's verdict may never come (I47).</summary>
    private Exception? _broken;

    public override void Write(byte[] buffer, int offset, int count) => Guarded(() => data.Write(buffer, offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var copy = buffer.ToArray();
        Guarded(() => data.Write(copy));
    }

    public override void Flush() => Guarded(data.Flush);

    private void Guarded(Action action)
    {
        try { channel.Run(action); }
        catch (Exception ex)
        {
            _broken = ex;
            throw;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            if (_broken is { } failure)
            {
                try { data.Dispose(); }
                catch (Exception ex) when (ex is IOException or FtpException or ObjectDisposedException) { }
                // The server's own reason where it gives one; else the session is gone and the connection was ended.
                var reply = channel.ReplyAfterBreak();
                if (reply is { Success: false } refused)
                    throw new IOException($"The server did not accept the file: {refused.Message} ({refused.Code})", failure);
                if (reply is null)
                    throw new RemoteDisconnectedException("The connection to the server was lost: " + RemoteErrorText.Reason(failure), failure);
            }
            else
            {
                data.Dispose();
                var reply = channel.Run(() => client.GetReply());
                if (!reply.Success) throw new IOException($"The server did not accept the file: {reply.Message} ({reply.Code})");
            }
        }
        base.Dispose(disposing);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
