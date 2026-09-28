using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Remote.Sftp;

/// <summary>
/// SFTP folders in panels (plan §14.1). A location's path is an absolute remote path, or "~" until the server has told
/// FileCat its home folder, and its session is the connection profile's id. Nothing here assumes atomic replace,
/// change watches, stable IDs, checksums, or server-side copy.
/// </summary>
public sealed class SftpProvider : ResourceProvider, IOriginMarkSource
{
    public const string Home = "~";
    private const int MaxLinksResolved = 500;

    private readonly Func<RemoteProfile, RemoteProfile> _addTemporary;
    private readonly Func<IReadOnlyList<RemoteProfile>> _savedProfiles;

    /// <param name="savedProfiles">Saved connections, which typed sftp:// addresses reuse when they name the same server.</param>
    /// <param name="addTemporary">Registers a session-only profile for a typed address and returns the one to use.</param>
    public SftpProvider(SftpConnections connections, Func<IReadOnlyList<RemoteProfile>> savedProfiles, Func<RemoteProfile, RemoteProfile> addTemporary)
    {
        Connections = connections;
        _savedProfiles = savedProfiles;
        _addTemporary = addTemporary;
    }

    public SftpConnections Connections { get; }

    public override string Scheme => Schemes.Sftp;

    public static Location At(RemoteProfile profile, string? path = null) =>
        new(Schemes.Sftp, string.IsNullOrEmpty(path) ? Home : path, session: profile.Id);

    private RemoteProfile? Profile(Location location) => location.Session is { } id ? Connections.Profile(id) : null;

    /// <summary>The absolute remote path; "~" and "~/…" use the home folder (the channel's, or the one already known).</summary>
    public string Resolve(Location location, ISftpChannel? channel = null)
    {
        string path = location.Path;
        if (path != Home && !path.StartsWith("~/", StringComparison.Ordinal)) return path;
        string? home = channel?.HomeDirectory ?? (location.Session is { } id ? Connections.HomeOf(id) : null);
        if (home is null) return path;
        return path == Home ? home : RemotePath.Combine(home, path[2..]);
    }

    public SftpLease Lease(Location location, CancellationToken ct) =>
        Connections.Lease(location.Session ?? throw new IOException("This SFTP location names no connection."), ct);

    public override string GetDisplayPath(Location location)
    {
        string path = Resolve(location);
        return "sftp://" + (Profile(location)?.Display ?? "(closed connection)") + (path.StartsWith('/') ? path : "/" + path);
    }

    public override string GetDisplayName(Location location)
    {
        string path = Resolve(location);
        if (path == "/" || path == Home) return Profile(location)?.Display ?? "SFTP";
        return RemotePath.Name(path);
    }

    public override Location? GetParent(Location location)
    {
        string path = Resolve(location);
        if (!path.StartsWith('/')) return null;
        return RemotePath.Parent(path) is { } parent ? location.WithPath(parent) : null;
    }

    public override string? GetNameInParent(Location location)
    {
        string path = Resolve(location);
        return path.StartsWith('/') && path != "/" ? RemotePath.Name(path) : null;
    }

    /// <summary>One queue per server: its bandwidth and connection limit are what jobs share.</summary>
    public override string GetDeviceKey(Location location) =>
        Profile(location) is { } p ? $"sftp://{p.Host.ToLowerInvariant()}:{p.Port}" : "sftp";

    public override LocationCapabilities GetCapabilities(Location location) =>
        LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.Recycle => "Servers have no Recycle Bin; items on a server are deleted permanently after you confirm.",
        LocationCapabilities.Watch => "Servers do not report changes; press Ctrl+R to refresh.",
        _ => base.ExplainUnavailable(location, capability),
    };

    /// <summary>
    /// sftp://[user@]host[:port][/path] (the IETF SFTP URI draft): the path is absolute, "/~/…" is inside the home
    /// folder, and no path opens the home folder. A password in the address is ignored, never stored.
    /// </summary>
    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        var t = text.Trim();
        if (!t.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || uri.Host.Length == 0) return false;
        string user = Uri.UnescapeDataString(uri.UserInfo.Split(':')[0]);
        int port = uri.Port > 0 ? uri.Port : 22;
        string host = uri.IdnHost.Trim('[', ']');
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path is "" or "/" || path == "/~") path = Home;
        else if (path.StartsWith("/~/", StringComparison.Ordinal)) path = path[1..];
        var saved = _savedProfiles().FirstOrDefault(p => string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase) && p.Port == port &&
                                                         (user.Length == 0 || string.Equals(p.User, user, StringComparison.Ordinal)));
        var profile = saved ?? _addTemporary(new RemoteProfile
        {
            Name = (user.Length > 0 ? user + "@" : "") + host,
            Host = host,
            Port = port,
            User = user.Length > 0 ? user : Environment.UserName,
            Temporary = true,
        });
        location = At(profile, path);
        return true;
    }

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.Run(() =>
    {
        // Off the UI thread: connecting may need the user (host key, password) through the UI.
        using var lease = Lease(location, ct);
        try
        {
            string path = Resolve(location, lease.Channel);
            var list = lease.Channel.List(path, ct);
            var batch = new List<EntryData>(list.Count);
            int links = 0;
            foreach (var e in list)
            {
                ct.ThrowIfCancellationRequested();
                if (RemotePath.ProblemWithName(e.Name) is { } problem)
                {
                    sink.ReportIssue($"{problem} It is not shown.");
                    continue;
                }
                var kind = e.IsDirectory ? EntryKind.Directory : EntryKind.File;
                long size = e.Size;
                var modified = e.ModifiedUtc;
                var flags = e.Name.StartsWith('.') ? EntryFlags.Hidden : EntryFlags.None;
                if (e.IsLink)
                {
                    // Show what the link points to; the link itself is what delete and rename act on.
                    flags |= EntryFlags.Link;
                    if (links++ < MaxLinksResolved && lease.Channel.Stat(e.FullPath) is { IsLink: false } target)
                    {
                        kind = target.IsDirectory ? EntryKind.Directory : EntryKind.File;
                        size = target.Size;
                        modified = target.ModifiedUtc;
                    }
                    else flags |= EntryFlags.Unavailable;
                }
                batch.Add(new EntryData(e.Name, kind, kind == EntryKind.Directory ? -1 : size, modified.Ticks) { Flags = flags });
            }
            sink.AddBatch(batch.ToArray());
        }
        catch (RemoteDisconnectedException)
        {
            lease.Broken = true;
            throw;
        }
    }, ct);

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Kind == EntryKind.Parent) return GetParent(parent);
        if (entry.Kind != EntryKind.Directory) return null;
        string folder = Resolve(parent);
        return parent.WithPath(folder == Home ? "~/" + entry.Name : RemotePath.Combine(folder, entry.Name));
    }

    /// <summary>The absolute path of an item in a listed folder.</summary>
    public string PathOf(ItemRef item, ISftpChannel? channel = null) => RemotePath.Combine(Resolve(item.Parent, channel), item.Name);

    /// <summary>
    /// Content for viewing and copying: the item keeps one connection leased until the content is disposed. Call it
    /// off the UI thread, because connecting may ask the user.
    /// </summary>
    public override IContentSource? OpenContent(ItemRef item)
    {
        var lease = Lease(item.Parent, CancellationToken.None);
        try
        {
            string path = PathOf(item, lease.Channel);
            var stat = lease.Channel.Stat(path) ?? throw new FileNotFoundException($"\"{item.Name}\" no longer exists on the server.");
            if (stat.IsDirectory) throw new IOException($"\"{item.Name}\" is a folder.");
            return new SftpContentSource(lease, lease.Channel.OpenRead(path), GetDisplayPath(item.Parent.WithPath(path)), stat);
        }
        catch (Exception ex)
        {
            if (ex is RemoteDisconnectedException) lease.Broken = true;
            lease.Dispose();
            throw;
        }
    }

    /// <summary>Downloads are marked as coming from the internet (ZoneId 3), naming the server but never the user.</summary>
    public string? GetOriginMark(Location container) =>
        Profile(container) is { } p ? $"[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=sftp://{p.Host}{(p.Port == 22 ? "" : ":" + p.Port)}/\r\n" : "[ZoneTransfer]\r\nZoneId=3\r\n";
}

/// <summary>Random-access content of a remote file over one leased connection.</summary>
internal sealed class SftpContentSource(SftpLease lease, Stream stream, string displayName, RemoteStat stat) : IContentSource
{
    private readonly object _lock = new();
    private bool _disposed;

    public string DisplayName => displayName;

    public long Length => stat.Size;

    public bool CanSeek => true;

    public string? LocalPath => null;

    public int Read(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                stream.Position = offset;
                return stream.Read(buffer);
            }
            catch (RemoteDisconnectedException)
            {
                lease.Broken = true;
                throw;
            }
        }
    }

    public ContentRevision? GetRevision() => new ContentRevision(stat.Size, stat.ModifiedUtc.Ticks);

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            stream.Dispose();
        }
        lease.Dispose();
    }
}
