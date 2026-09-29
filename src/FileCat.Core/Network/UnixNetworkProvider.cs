using FileCat.Core.Resources;

namespace FileCat.Core.Network;

/// <summary>A computer, file server, or share in a Network listing on Linux and macOS.</summary>
public sealed record UnixNetworkTag(NetworkHost? Host, SmbShare? Share) : IDisplayDetails
{
    public string KindText => Share is not null ? "Share" : Host?.Source switch
    {
        "WS-Discovery" => "Computer",
        "Bonjour" => "File server",
        _ => "Server",
    };

    public string DetailsText => Share is not null
        ? Share.Comment ?? ""
        : string.Join(" · ", new[] { Host?.Detail, Host?.Address?.ToString(), Host?.Source == "known" ? "reached before" : "announced by " + Host?.Source }
            .Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// The Network on Linux and macOS (D-54): the computers and file servers nearby (servers already reached, then those
/// that answer discovery), a server's SMB shares (gvfs or smbclient on Linux, smbutil on macOS), and each share opened
/// where the system mounts it (gvfs on Linux; NetFS on macOS, which asks for sign-in in its own dialog). A share is a
/// location prepared first (<see cref="PrepareAsync"/>): the panel then shows its folder, with everything a folder
/// allows. Addresses are smb://server and smb://server/share, as the desktops write them.
/// </summary>
public sealed class UnixNetworkProvider : ResourceProvider
{
    public static readonly TimeSpan DiscoveryWait = TimeSpan.FromSeconds(3);
    public static Location Root { get; } = new(Schemes.Network, string.Empty);

    public override string Scheme => Schemes.Network;

    /// <summary>Servers already reached (history, mounted shares): listed at once, before discovery answers.</summary>
    public Func<IEnumerable<string>>? KnownServers { get; set; }

    /// <summary>
    /// Asks for a user name and password for a server (and share, when one is being opened), with why; null when the
    /// user declines. Linux only: macOS asks in its own dialog.
    /// </summary>
    public Func<string, string?, string, Task<NetworkCredentials?>>? SignIn { get; set; }

    /// <summary>Credentials given for a server in this session, used again for its other shares.</summary>
    private readonly Dictionary<string, NetworkCredentials> _given = new(StringComparer.OrdinalIgnoreCase);

    public override string GetDisplayPath(Location location) => location.Path.Length == 0 ? "Network" : location.Path;

    public override string GetDisplayName(Location location) => Parse(location.Path) switch
    {
        (null, _) => "Network",
        ({ } server, null) => server,
        (_, { } share) => share,
    };

    public override Location? GetParent(Location location) => Parse(location.Path) switch
    {
        (null, _) => null,
        (_, null) => Root,
        ({ } server, _) => new Location(Schemes.Network, "smb://" + server),
    };

    public override string? GetNameInParent(Location location) => Parse(location.Path) switch
    {
        (null, _) => null,
        ({ } server, null) => server,
        (_, { } share) => share,
    };

    public override string GetDeviceKey(Location location) => "net:" + (Parse(location.Path).Server ?? "");

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        string t = text.Trim();
        if (t.Equals("Network", StringComparison.OrdinalIgnoreCase) || t.Equals("network:", StringComparison.OrdinalIgnoreCase))
        {
            location = Root;
            return true;
        }
        if (!t.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)) return false;
        var (server, share) = Parse(t);
        if (server is null || !NetworkDiscovery.IsHostName(server)) return false;
        location = new Location(Schemes.Network, "smb://" + server + (share is null ? "" : "/" + t[("smb://" + server + "/").Length..].TrimEnd('/')));
        return true;
    }

    /// <summary>"smb://server/share/sub" → (server, share); the Network root → (null, null).</summary>
    public static (string? Server, string? Share) Parse(string path)
    {
        if (!path.StartsWith("smb://", StringComparison.OrdinalIgnoreCase)) return (null, null);
        var parts = path["smb://".Length..].Split('/', 3);
        string? server = parts[0].Length > 0 ? parts[0] : null;
        string? share = parts.Length > 1 && parts[1].Length > 0 ? Uri.UnescapeDataString(parts[1]) : null;
        return (server, share);
    }

    public override async Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var (server, share) = Parse(location.Path);
        if (server is null)
        {
            await EnumerateNetworkAsync(sink, ct).ConfigureAwait(false);
            return;
        }
        if (share is not null) throw new InvalidOperationException("A share opens where the system mounts it.");
        IReadOnlyList<SmbShare> shares;
        try
        {
            shares = await SmbTools.ListSharesAsync(server, Given(server), ct).ConfigureAwait(false);
        }
        catch (SmbSignInRequiredException) when (!OperatingSystem.IsMacOS() && SignIn is not null)
        {
            var credentials = await SignIn(server, null, "The server lists its shares only after you sign in.").ConfigureAwait(false)
                              ?? throw new OperationCanceledException("Signing in was canceled.");
            shares = await SmbTools.ListSharesAsync(server, credentials, ct).ConfigureAwait(false);
            lock (_given) _given[server] = credentials;
        }
        catch (SmbSignInRequiredException) when (OperatingSystem.IsMacOS())
        {
            throw new SmbSignInRequiredException($"{server} lists its shares only after signing in: type smb://{server}/share (with the share's name) in the path, and macOS asks for the user name and password.");
        }
        foreach (var s in shares)
        {
            ct.ThrowIfCancellationRequested();
            var entry = new EntryData(s.Name, EntryKind.Share) { Tag = new UnixNetworkTag(null, s) };
            if (s.IsHidden) entry.Flags |= EntryFlags.Hidden;
            sink.AddBatch([entry]);
        }
    }

    private NetworkCredentials? Given(string server)
    {
        lock (_given) return _given.GetValueOrDefault(server);
    }

    private async Task EnumerateNetworkAsync(IEnumerationSink sink, CancellationToken ct)
    {
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(NetworkHost host)
        {
            lock (listed)
            {
                if (!listed.Add(host.Server)) return;
                sink.AddBatch([new EntryData(host.Name, EntryKind.Server) { Tag = new UnixNetworkTag(host, null) }]);
            }
        }
        foreach (string server in (KnownServers?.Invoke() ?? []).Concat(SmbTools.Mounts().Select(m => m.Server)))
            if (NetworkDiscovery.IsHostName(server)) Add(new NetworkHost(server, server, null, "known"));
        await NetworkDiscovery.DiscoverAsync(Add, DiscoveryWait, ct).ConfigureAwait(false);
        if (listed.Count == 0)
            sink.ReportIssue("No computer or file server answered on this network. Type smb://server in the path to open one by its name: some devices do not announce themselves.");
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry) => entry.Tag switch
    {
        UnixNetworkTag { Host: { } host } => new Location(Schemes.Network, "smb://" + host.Server),
        UnixNetworkTag { Share: { } share } when Parse(parent.Path).Server is { } server => new Location(Schemes.Network, $"smb://{server}/{Uri.EscapeDataString(share.Name)}"),
        _ => null,
    };

    /// <summary>A share: mounted (or found mounted), then shown as the folder it is, with any path below it.</summary>
    public override Task<Location>? PrepareAsync(Location location, CancellationToken ct)
    {
        var (server, share) = Parse(location.Path);
        if (server is null || share is null) return null;
        string below = location.Path.Split('/', 5) is { Length: 5 } parts ? parts[4] : "";
        return Task.Run(async () =>
        {
            string folder = SmbTools.FindMount(server, share) ?? await MountAsync(server, share, ct).ConfigureAwait(false);
            string path = below.Length == 0 ? folder : Path.Join(folder, Uri.UnescapeDataString(below));
            return Location.FileSystem(path);
        }, ct);
    }

    private async Task<string> MountAsync(string server, string share, CancellationToken ct)
    {
        if (OperatingSystem.IsMacOS()) return MacNetFS.Mount(server, share);
        try
        {
            return await SmbTools.MountWithGvfsAsync(server, share, Given(server), ct).ConfigureAwait(false);
        }
        catch (SmbSignInRequiredException ex) when (SignIn is not null)
        {
            var credentials = await SignIn(server, share, ex.Message).ConfigureAwait(false) ?? throw new OperationCanceledException("Signing in was canceled.");
            string folder = await SmbTools.MountWithGvfsAsync(server, share, credentials, ct).ConfigureAwait(false);
            lock (_given) _given[server] = credentials;
            return folder;
        }
    }
}
