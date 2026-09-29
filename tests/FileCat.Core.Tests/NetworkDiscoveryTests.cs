using System.Net;
using System.Net.Sockets;
using System.Text;
using FileCat.Core.Network;

namespace FileCat.Core.Tests;

/// <summary>D-54: finding the local network's computers and file servers (WS-Discovery and multicast DNS).</summary>
public sealed class NetworkDiscoveryTests
{
    private const string Endpoint = "urn:uuid:1f7b8c3a-0000-4000-8000-00155d000001";

    private static string ProbeMatches(string xaddrs) => $"""
        <?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:wsd="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:wsdp="http://schemas.xmlsoap.org/ws/2006/02/devprof" xmlns:pub="http://schemas.microsoft.com/windows/pub/2005/07"><soap:Header><wsa:To>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:To><wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</wsa:Action><wsa:MessageID>urn:uuid:5d2c1a4e-1111-4222-8333-444455556666</wsa:MessageID><wsa:RelatesTo>urn:uuid:0e9e8f7a-1111-4222-8333-444455556666</wsa:RelatesTo><wsd:AppSequence InstanceId="3" MessageNumber="1"></wsd:AppSequence></soap:Header><soap:Body><wsd:ProbeMatches><wsd:ProbeMatch><wsa:EndpointReference><wsa:Address>{Endpoint}</wsa:Address></wsa:EndpointReference><wsd:Types>wsdp:Device pub:Computer</wsd:Types><wsd:XAddrs>{xaddrs}</wsd:XAddrs><wsd:MetadataVersion>2</wsd:MetadataVersion></wsd:ProbeMatch></wsd:ProbeMatches></soap:Body></soap:Envelope>
        """;

    /// <summary>What Windows answers a WS-Transfer Get with (the device host first, then the computer it runs on).</summary>
    private const string Metadata = """
        <?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:wsx="http://schemas.xmlsoap.org/ws/2004/09/mex" xmlns:wsdp="http://schemas.xmlsoap.org/ws/2006/02/devprof" xmlns:pub="http://schemas.microsoft.com/windows/pub/2005/07"><soap:Header><wsa:Action>http://schemas.xmlsoap.org/ws/2004/09/transfer/GetResponse</wsa:Action></soap:Header><soap:Body><wsx:Metadata><wsx:MetadataSection Dialect="http://schemas.xmlsoap.org/ws/2006/02/devprof/ThisDevice"><wsdp:ThisDevice><wsdp:FriendlyName>Microsoft Publication Service Device Host</wsdp:FriendlyName><wsdp:FirmwareVersion>1.0</wsdp:FirmwareVersion></wsdp:ThisDevice></wsx:MetadataSection><wsx:MetadataSection Dialect="http://schemas.xmlsoap.org/ws/2006/02/devprof/Relationship"><wsdp:Relationship Type="http://schemas.xmlsoap.org/ws/2006/02/devprof/host"><wsdp:Host><wsa:EndpointReference><wsa:Address>urn:uuid:1f7b8c3a-0000-4000-8000-00155d000001</wsa:Address></wsa:EndpointReference><wsdp:Types>pub:Computer</wsdp:Types><pub:Computer>TESTBOX/Workgroup:WORKGROUP</pub:Computer></wsdp:Host></wsdp:Relationship></wsx:MetadataSection></wsx:Metadata></soap:Body></soap:Envelope>
        """;

    /// <summary>An mDNS answer: a PTR to "NAS._smb._tcp.local", its SRV naming nas.local, and nas.local's address.</summary>
    private static byte[] MdnsAnswer(IPAddress address)
    {
        var bytes = new List<byte> { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 2 };
        void Name(params string[] labels)
        {
            foreach (string label in labels)
            {
                bytes.Add((byte)label.Length);
                bytes.AddRange(Encoding.ASCII.GetBytes(label));
            }
            bytes.Add(0);
        }
        void Record(ushort type, IList<byte> data)
        {
            bytes.AddRange(new byte[] { (byte)(type >> 8), (byte)type, 0x80, 1, 0, 0, 0x11, 0x94, (byte)(data.Count >> 8), (byte)data.Count });
            bytes.AddRange(data);
        }
        int service = bytes.Count;
        Name("_smb", "_tcp", "local");
        // PTR: "NAS" followed by a pointer to "_smb._tcp.local".
        Record(12, [3, (byte)'N', (byte)'A', (byte)'S', (byte)(0xC0 | service >> 8), (byte)service]);
        int instance = bytes.Count;
        bytes.AddRange(new byte[] { 3, (byte)'N', (byte)'A', (byte)'S', (byte)(0xC0 | service >> 8), (byte)service });
        var srv = new List<byte> { 0, 0, 0, 0, 0x01, 0xBD };
        srv.AddRange(new byte[] { 3, (byte)'n', (byte)'a', (byte)'s', 5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0 });
        Record(33, srv);
        Name("nas", "local");
        Record(1, address.GetAddressBytes());
        return [.. bytes];
    }

    [Fact]
    public void A_WS_Discovery_answer_gives_the_device_and_the_addresses_it_named()
    {
        var matches = NetworkDiscovery.ParseProbeMatches(Encoding.UTF8.GetBytes(ProbeMatches("http://192.168.0.20:5357/1f7b8c3a/ http://[fe80::1]:5357/1f7b8c3a/")));
        var (endpoint, addresses) = Assert.Single(matches);
        Assert.Equal(Endpoint, endpoint);
        Assert.Equal(["http://192.168.0.20:5357/1f7b8c3a/", "http://[fe80::1]:5357/1f7b8c3a/"], addresses);
        // Not XML, or XML with a document type (which is never read): nothing, and no failure.
        Assert.Empty(NetworkDiscovery.ParseProbeMatches(Encoding.UTF8.GetBytes("not xml")));
        Assert.Empty(NetworkDiscovery.ParseProbeMatches(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e 'x'>]><x>&e;</x>")));
    }

    [Fact]
    public void A_computers_metadata_gives_its_name_and_workgroup()
    {
        Assert.Equal(("TESTBOX", "Workgroup: WORKGROUP"), NetworkDiscovery.ParseComputer(Encoding.UTF8.GetBytes(Metadata)));
        // A name that could not be a server's is not taken (the device host's friendly name has spaces).
        Assert.Null(NetworkDiscovery.ParseComputer(Encoding.UTF8.GetBytes(Metadata.Replace("TESTBOX/Workgroup:WORKGROUP", "bad name/x"))));
        Assert.True(NetworkDiscovery.IsHostName("nas.local"));
        Assert.False(NetworkDiscovery.IsHostName("..\\evil"));
        Assert.False(NetworkDiscovery.IsHostName(""));
    }

    [Fact]
    public void An_mDNS_answer_gives_the_SMB_service_its_host_and_address()
    {
        var host = Assert.Single(NetworkDiscovery.ParseMdnsAnswer(MdnsAnswer(IPAddress.Parse("192.168.0.50")), IPAddress.Parse("192.168.0.99")));
        Assert.Equal("NAS", host.Name);
        Assert.Equal("nas.local", host.Server);
        Assert.Equal(IPAddress.Parse("192.168.0.50"), host.Address);
        Assert.Equal("Bonjour", host.Source);
        // Cut short anywhere, or looping through its own name pointers: nothing, and no failure.
        var answer = MdnsAnswer(IPAddress.Parse("192.168.0.50"));
        for (int cut = 0; cut < answer.Length; cut++) NetworkDiscovery.ParseMdnsAnswer(answer.AsSpan(0, cut), IPAddress.Loopback);
        var loop = (byte[])answer.Clone();
        loop[12] = 0xC0;
        loop[13] = 12;
        NetworkDiscovery.ParseMdnsAnswer(loop, IPAddress.Loopback);
    }

    [Fact]
    public async Task Devices_that_answer_are_found_named_and_listed_once()
    {
        var ct = TestContext.Current.CancellationToken;
        // A computer's metadata, served over HTTP as WS-Discovery devices serve it.
        using var http = new TcpListener(IPAddress.Loopback, 0);
        http.Start();
        int httpPort = ((IPEndPoint)http.LocalEndpoint).Port;
        int metadataRequests = 0;
        var serving = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await http.AcceptTcpClientAsync(ct);
                Interlocked.Increment(ref metadataRequests);
                using var stream = client.GetStream();
                var buffer = new byte[16384];
                int read = 0;
                // The headers, then the body its length announces.
                while (true)
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
                    if (n == 0) break;
                    read += n;
                    string text = Encoding.UTF8.GetString(buffer, 0, read);
                    int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    if (end < 0) continue;
                    var length = System.Text.RegularExpressions.Regex.Match(text, "Content-Length: *([0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (!length.Success || read >= end + 4 + int.Parse(length.Groups[1].Value)) break;
                }
                byte[] body = Encoding.UTF8.GetBytes(Metadata);
                byte[] head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/soap+xml\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head, ct);
                await stream.WriteAsync(body, ct);
            }
        }, ct);

        // A WS-Discovery responder whose device names its metadata at the address it answers from, and an mDNS
        // responder for a NAS.
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var mdns = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var answering = Task.WhenAll(
            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var probe = await wsd.ReceiveAsync(ct);
                    if (!Encoding.UTF8.GetString(probe.Buffer).Contains("pub:Computer", StringComparison.Ordinal)) continue;
                    await wsd.SendAsync(Encoding.UTF8.GetBytes(ProbeMatches($"http://127.0.0.1:{httpPort}/1f7b8c3a/")), probe.RemoteEndPoint, ct);
                }
            }, ct),
            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    var query = await mdns.ReceiveAsync(ct);
                    await mdns.SendAsync(MdnsAnswer(IPAddress.Parse("10.1.2.3")), query.RemoteEndPoint, ct);
                }
            }, ct));

        var found = new List<NetworkHost>();
        await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, TimeSpan.FromSeconds(2), ct,
            (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)mdns.Client.LocalEndPoint!, [IPAddress.Loopback]);

        // Each once, though each answered both copies of the probe.
        Assert.Equal(2, found.Count);
        var computer = Assert.Single(found, h => h.Source == "WS-Discovery");
        Assert.Equal("TESTBOX", computer.Name);
        Assert.Equal("TESTBOX", computer.Server);
        Assert.Equal("Workgroup: WORKGROUP", computer.Detail);
        var nas = Assert.Single(found, h => h.Source == "Bonjour");
        Assert.Equal("nas.local", nas.Server);
        Assert.Equal(1, metadataRequests);
        http.Stop();
    }

    [Fact]
    public async Task A_device_that_names_another_address_is_not_followed_there()
    {
        var ct = TestContext.Current.CancellationToken;
        // The answer points its metadata at a different machine: FileCat never asks there, and lists the device by
        // the address it answered from.
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var answering = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var probe = await wsd.ReceiveAsync(ct);
                await wsd.SendAsync(Encoding.UTF8.GetBytes(ProbeMatches("http://203.0.113.9:5357/elsewhere/")), probe.RemoteEndPoint, ct);
            }
        }, ct);
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var found = new List<NetworkHost>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, TimeSpan.FromSeconds(1), ct,
            (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)silent.Client.LocalEndPoint!, [IPAddress.Loopback]);
        var host = Assert.Single(found);
        Assert.Equal("127.0.0.1", host.Server);
        // No wait on an HTTP request to the other address.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(3), clock.Elapsed.ToString());
    }
}
