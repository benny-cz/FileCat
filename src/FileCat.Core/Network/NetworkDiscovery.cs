using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;

namespace FileCat.Core.Network;

/// <summary>A computer or file server found on the local network (D-54).</summary>
/// <param name="Name">What it is called (its computer name, or the name it announces).</param>
/// <param name="Server">The name to reach its shares by (a computer name, "name.local", or an address).</param>
/// <param name="Address">Where it answered from.</param>
/// <param name="Source">How it was found: "WS-Discovery", "Bonjour", or "known".</param>
public sealed record NetworkHost(string Name, string Server, IPAddress? Address, string Source)
{
    /// <summary>A workgroup or domain it named, or a device model; null when it said nothing more.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Finds the computers and file servers on the local network (D-54) the way the systems' own browsers do, in managed
/// code on every platform: WS-Discovery (how Windows computers, and Samba's wsdd, announce themselves; Explorer's
/// Network lists these) and multicast DNS for SMB services (Macs, NAS boxes, and Linux with Avahi; Finder lists
/// these). Probes go out on every multicast-capable IPv4 interface, answers are reported as they arrive, a device's
/// name is asked only of the address that answered, and everything read is bounded.
/// </summary>
public static class NetworkDiscovery
{
    private static readonly IPEndPoint WsDiscovery = new(IPAddress.Parse("239.255.255.250"), 3702);
    private static readonly IPEndPoint MulticastDns = new(IPAddress.Parse("224.0.0.251"), 5353);
    private const int MaxDatagram = 65_507;

    /// <summary>
    /// Probes for <paramref name="wait"/> and reports each host once as it answers (a WS-Discovery answer after its
    /// name is read, within moments). The probes go to <paramref name="targets"/> when given (tests), else multicast.
    /// </summary>
    public static async Task DiscoverAsync(Action<NetworkHost> found, TimeSpan wait, CancellationToken ct,
        IPEndPoint? wsdTarget = null, IPEndPoint? mdnsTarget = null, IReadOnlyList<IPAddress>? interfaces = null)
    {
        // One row per device: the first name it answers with wins, and a device known only by its address waits
        // until the end, in case it answers with a name after all.
        var servers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addresses = new HashSet<IPAddress>();
        var nameless = new List<NetworkHost>();
        void Report(NetworkHost host)
        {
            lock (servers)
            {
                if (host.Address is { } address && host.Server == address.ToString())
                {
                    nameless.Add(host);
                    return;
                }
                if (host.Address is { } known && addresses.Contains(known) || !servers.Add(host.Server)) return;
                if (host.Address is { } a) addresses.Add(a);
            }
            found(host);
        }
        var local = interfaces ?? LocalAddresses();
        if (local.Count == 0) return;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stop.CancelAfter(wait);
        var work = new List<Task>();
        foreach (var address in local)
        {
            work.Add(ProbeWsdAsync(address, wsdTarget ?? WsDiscovery, Report, stop.Token));
            work.Add(ProbeMdnsAsync(address, mdnsTarget ?? MulticastDns, Report, stop.Token));
        }
        await Task.WhenAll(work).ConfigureAwait(false);
        foreach (var host in nameless.DistinctBy(h => h.Address))
        {
            lock (servers)
            {
                if (addresses.Contains(host.Address!) || !servers.Add(host.Server)) continue;
                addresses.Add(host.Address!);
            }
            found(host);
        }
    }

    /// <summary>The IPv4 addresses of the interfaces that are up and can send multicast (not loopback).</summary>
    public static IReadOnlyList<IPAddress> LocalAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || !nic.SupportsMulticast) continue;
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(unicast.Address)) result.Add(unicast.Address);
            }
        }
        catch (NetworkInformationException) { }
        return result;
    }

    // ---- WS-Discovery ----------------------------------------------------------------------------------------

    private static async Task ProbeWsdAsync(IPAddress local, IPEndPoint target, Action<NetworkHost> report, CancellationToken ct)
    {
        using var socket = Open(local);
        if (socket is null) return;
        var names = new List<Task>();
        try
        {
            byte[] probe = Encoding.UTF8.GetBytes(ProbeMessage(Guid.NewGuid()));
            // Two copies, as WS-Discovery clients send over UDP (a lost datagram is common on Wi-Fi).
            await socket.SendToAsync(probe, SocketFlags.None, target, ct).ConfigureAwait(false);
            await socket.SendToAsync(probe, SocketFlags.None, target, ct).ConfigureAwait(false);
            var buffer = new byte[MaxDatagram];
            var answered = new HashSet<string>(StringComparer.Ordinal);
            while (!ct.IsCancellationRequested)
            {
                var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct).ConfigureAwait(false);
                if (received.RemoteEndPoint is not IPEndPoint from) continue;
                foreach (var match in ParseProbeMatches(buffer.AsSpan(0, received.ReceivedBytes)))
                {
                    if (!answered.Add(match.Endpoint)) continue;
                    // The name is asked only of the device that answered, never of an address it named elsewhere.
                    string? metadata = match.Addresses.FirstOrDefault(a => IsAt(a, from.Address));
                    names.Add(NameAsync(match.Endpoint, metadata, from.Address, report, ct));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
        await Task.WhenAll(names).ConfigureAwait(false);
    }

    private static bool IsAt(string url, IPAddress address) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp &&
        IPAddress.TryParse(uri.Host, out var host) && host.Equals(address);

    private static async Task NameAsync(string endpoint, string? metadataUrl, IPAddress from, Action<NetworkHost> report, CancellationToken ct)
    {
        (string Name, string? Detail)? named = null;
        if (metadataUrl is not null)
        {
            try { named = await GetComputerNameAsync(metadataUrl, endpoint, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or XmlException or IOException) { }
        }
        if (named is { } n) report(new NetworkHost(n.Name, n.Name, from, "WS-Discovery") { Detail = n.Detail });
        else report(new NetworkHost(from.ToString(), from.ToString(), from, "WS-Discovery"));
    }

    private static readonly HttpClient Metadata = new(new SocketsHttpHandler { UseProxy = false, ConnectTimeout = TimeSpan.FromSeconds(2) })
    {
        Timeout = TimeSpan.FromSeconds(3),
        MaxResponseContentBufferSize = 256 * 1024,
    };

    /// <summary>A WS-Transfer Get of the device's metadata: its computer name ("NAME/Workgroup:GROUP").</summary>
    private static async Task<(string Name, string? Detail)?> GetComputerNameAsync(string url, string endpoint, CancellationToken ct)
    {
        using var content = new StringContent(GetMessage(endpoint, Guid.NewGuid()), Encoding.UTF8, "application/soap+xml");
        using var response = await Metadata.PostAsync(url, content, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        return ParseComputer(await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
    }

    internal static string ProbeMessage(Guid id) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:wsd="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:wsdp="http://schemas.xmlsoap.org/ws/2006/02/devprof" xmlns:pub="http://schemas.microsoft.com/windows/pub/2005/07"><soap:Header><wsa:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To><wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action><wsa:MessageID>urn:uuid:{id}</wsa:MessageID></soap:Header><soap:Body><wsd:Probe><wsd:Types>wsdp:Device pub:Computer</wsd:Types></wsd:Probe></soap:Body></soap:Envelope>
        """;

    internal static string GetMessage(string endpoint, Guid id) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing"><soap:Header><wsa:To>{System.Security.SecurityElement.Escape(endpoint)}</wsa:To><wsa:Action>http://schemas.xmlsoap.org/ws/2004/09/transfer/Get</wsa:Action><wsa:MessageID>urn:uuid:{id}</wsa:MessageID><wsa:ReplyTo><wsa:Address>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:Address></wsa:ReplyTo></soap:Header><soap:Body/></soap:Envelope>
        """;

    /// <summary>The devices in a ProbeMatches message: each one's endpoint and the addresses it gave.</summary>
    internal static List<(string Endpoint, List<string> Addresses)> ParseProbeMatches(ReadOnlySpan<byte> message)
    {
        var result = new List<(string, List<string>)>();
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(message.ToArray()), SafeXml);
            string? endpoint = null;
            List<string>? addresses = null;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "ProbeMatch")
                {
                    endpoint = null;
                    addresses = [];
                }
                else if (reader.NodeType == XmlNodeType.Element && addresses is not null && reader.LocalName == "Address")
                    endpoint = reader.ReadElementContentAsString().Trim();
                else if (reader.NodeType == XmlNodeType.Element && addresses is not null && reader.LocalName == "XAddrs")
                    addresses.AddRange(reader.ReadElementContentAsString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "ProbeMatch" && addresses is not null)
                {
                    if (!string.IsNullOrEmpty(endpoint)) result.Add((endpoint, addresses));
                    addresses = null;
                }
            }
        }
        catch (XmlException) { }
        return result;
    }

    /// <summary>The computer name from WS-Discovery metadata ("NAME/Workgroup:GROUP" or "NAME/Domain:corp").</summary>
    internal static (string Name, string? Detail)? ParseComputer(byte[] metadata)
    {
        if (metadata.Length > 256 * 1024) return null;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(metadata), SafeXml);
            string? friendly = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.LocalName == "Computer")
                {
                    string text = reader.ReadElementContentAsString().Trim();
                    int slash = text.IndexOf('/');
                    string name = (slash < 0 ? text : text[..slash]).Trim();
                    string? group = slash < 0 ? null : text[(slash + 1)..].Replace(":", ": ").Trim();
                    if (IsHostName(name)) return (name, group);
                }
                else if (reader.LocalName == "FriendlyName") friendly = reader.ReadElementContentAsString().Trim();
            }
            return friendly is not null && IsHostName(friendly) ? (friendly, null) : null;
        }
        catch (XmlException) { return null; }
    }

    private static readonly XmlReaderSettings SafeXml = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 1_000_000,
        IgnoreComments = true,
    };

    /// <summary>A name that can be used as a server name: letters, digits, '-', '_', '.', at most 253 characters.</summary>
    internal static bool IsHostName(string name) =>
        name.Length is > 0 and <= 253 && name.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.') && !name.StartsWith('.');

    // ---- Multicast DNS ---------------------------------------------------------------------------------------

    private static async Task ProbeMdnsAsync(IPAddress local, IPEndPoint target, Action<NetworkHost> report, CancellationToken ct)
    {
        using var socket = Open(local);
        if (socket is null) return;
        try
        {
            // From an ordinary port, so responders answer this port directly ("legacy unicast").
            byte[] query = MdnsQuery("_smb._tcp.local");
            await socket.SendToAsync(query, SocketFlags.None, target, ct).ConfigureAwait(false);
            await socket.SendToAsync(query, SocketFlags.None, target, ct).ConfigureAwait(false);
            var buffer = new byte[MaxDatagram];
            while (!ct.IsCancellationRequested)
            {
                var received = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), ct).ConfigureAwait(false);
                if (received.RemoteEndPoint is not IPEndPoint from) continue;
                foreach (var host in ParseMdnsAnswer(buffer.AsSpan(0, received.ReceivedBytes), from.Address)) report(host);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    /// <summary>A query for the PTR records of <paramref name="service"/>, asking for unicast answers.</summary>
    internal static byte[] MdnsQuery(string service)
    {
        var bytes = new List<byte> { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 }; // id 0, flags 0, one question
        foreach (string label in service.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(label));
        }
        bytes.Add(0);
        bytes.AddRange(new byte[] { 0, 12, 0x80, 1 }); // PTR, class IN with the unicast-response bit
        return [.. bytes];
    }

    /// <summary>
    /// The SMB services in an mDNS answer: each instance's name, the host its SRV record names, and that host's
    /// IPv4 address when the answer carries it (else where the answer came from).
    /// </summary>
    internal static List<NetworkHost> ParseMdnsAnswer(ReadOnlySpan<byte> message, IPAddress from)
    {
        var hosts = new List<NetworkHost>();
        if (message.Length < 12) return hosts;
        int questions = BinaryPrimitives.ReadUInt16BigEndian(message[4..]);
        int records = BinaryPrimitives.ReadUInt16BigEndian(message[6..]) + BinaryPrimitives.ReadUInt16BigEndian(message[8..]) + BinaryPrimitives.ReadUInt16BigEndian(message[10..]);
        int at = 12;
        var instances = new List<string>();
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var addresses = new Dictionary<string, IPAddress>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (int i = 0; i < questions; i++)
            {
                ReadName(message, ref at);
                at += 4;
            }
            for (int i = 0; i < records && at < message.Length; i++)
            {
                string owner = ReadName(message, ref at);
                if (at + 10 > message.Length) break;
                ushort type = BinaryPrimitives.ReadUInt16BigEndian(message[at..]);
                int length = BinaryPrimitives.ReadUInt16BigEndian(message[(at + 8)..]);
                int data = at + 10;
                if (data + length > message.Length) break;
                switch (type)
                {
                    case 12 when owner.Equals("_smb._tcp.local", StringComparison.OrdinalIgnoreCase): // PTR
                        int p = data;
                        instances.Add(ReadName(message, ref p));
                        break;
                    case 33 when length >= 7: // SRV: priority, weight, port, target
                        int s = data + 6;
                        targets[owner] = ReadName(message, ref s);
                        break;
                    case 1 when length == 4: // A
                        addresses[owner] = new IPAddress(message.Slice(data, 4));
                        break;
                }
                at = data + length;
            }
        }
        catch (IndexOutOfRangeException) { }
        catch (ArgumentOutOfRangeException) { }
        foreach (string instance in instances)
        {
            string name = instance.EndsWith("._smb._tcp.local", StringComparison.OrdinalIgnoreCase) ? instance[..^"._smb._tcp.local".Length] : instance;
            string server = targets.TryGetValue(instance, out var target) ? target.TrimEnd('.') : name + ".local";
            if (!IsHostName(server)) continue;
            var address = addresses.TryGetValue(server, out var a) ? a : from;
            hosts.Add(new NetworkHost(name.Length > 0 ? name : server, server, address, "Bonjour"));
        }
        return hosts;
    }

    /// <summary>A DNS name at <paramref name="at"/>, following compression pointers (a bounded number of them).</summary>
    private static string ReadName(ReadOnlySpan<byte> message, ref int at)
    {
        var labels = new List<string>();
        int position = at, jumps = 0;
        bool jumped = false;
        while (true)
        {
            byte length = message[position];
            if (length == 0)
            {
                position++;
                break;
            }
            if ((length & 0xC0) == 0xC0)
            {
                if (++jumps > 16) throw new IndexOutOfRangeException("Too many name pointers.");
                int pointer = ((length & 0x3F) << 8) | message[position + 1];
                if (!jumped) at = position + 2;
                jumped = true;
                position = pointer;
                continue;
            }
            labels.Add(Encoding.UTF8.GetString(message.Slice(position + 1, length)));
            position += 1 + length;
        }
        if (!jumped) at = position;
        return string.Join('.', labels);
    }

    private static Socket? Open(IPAddress local)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(local, 0));
        }
        catch (SocketException)
        {
            socket?.Dispose();
            return null;
        }
        try
        {
            // Out of this interface, and no further than the local network.
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
            socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
        }
        catch (SocketException) { }
        return socket;
    }
}
