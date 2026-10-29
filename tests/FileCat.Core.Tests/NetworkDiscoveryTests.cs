using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Diagnostics;
using FileCat.Core.Network;

namespace FileCat.Core.Tests;

[CollectionDefinition("Network discovery allocation", DisableParallelization = true)]
public sealed class NetworkDiscoveryAllocationCollection;

/// <summary>D-54: finding the local network's computers and file servers (WS-Discovery and multicast DNS).</summary>
[Collection("Network discovery allocation")]
public sealed class NetworkDiscoveryTests
{
    private const string Endpoint = "urn:uuid:1f7b8c3a-0000-4000-8000-00155d000001";

    [Fact]
    public void Compressed_name_amplification_is_rejected_with_bounded_allocation()
    {
        byte[] bytes = AmplifiedMdnsAnswer();
        long before = GC.GetAllocatedBytesForCurrentThread();
        var found = NetworkDiscovery.ParseMdnsAnswer(bytes, IPAddress.Loopback);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Empty(found);
        Assert.True(allocated < 4 * 1024 * 1024, $"mDNS parse allocated {allocated:N0} bytes.");
    }

    [Fact]
    public async Task Loopback_discovery_ignores_amplified_answer_and_reports_valid_SMB_service()
    {
        var ct = TestContext.Current.CancellationToken;
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var mdns = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var cutoff = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cutoff.CancelAfter(TimeSpan.FromSeconds(2));
        var responder = Task.Run(async () =>
        {
            var query = await mdns.ReceiveAsync(ct);
            await mdns.SendAsync(AmplifiedMdnsAnswer(), query.RemoteEndPoint, ct);
            await mdns.SendAsync(MdnsAnswer(IPAddress.Parse("10.1.2.3")), query.RemoteEndPoint, ct);
        }, ct);
        var found = new List<NetworkHost>();
        long before = GC.GetTotalAllocatedBytes(precise: true);
        var watch = Stopwatch.StartNew();
        await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, cutoff.Token, ct,
            (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)mdns.Client.LocalEndPoint!, [IPAddress.Loopback]);
        watch.Stop();
        await responder.WaitAsync(TimeSpan.FromSeconds(5), ct);
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        var nas = Assert.Single(found, h => h.Source == "Bonjour");
        Assert.Equal("nas.local", nas.Server);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Discovery took {watch.Elapsed}.");
        Assert.True(allocated < 64L * 1024 * 1024, $"Discovery allocated {allocated:N0} bytes.");
        TestContext.Current.TestOutputHelper?.WriteLine($"mDNS socket callback: duration={watch.Elapsed}, process-allocated={allocated:N0} bytes, hosts={found.Count}");
    }

    private static byte[] AmplifiedMdnsAnswer()
    {
        var bytes = new List<byte>(26_000);
        bytes.AddRange(new byte[12]);
        bytes[4] = 0x0B; bytes[5] = 0xB9; // 3001 questions
        for (int i = 0; i < 4000; i++) { bytes.Add(1); bytes.Add((byte)'a'); }
        bytes.Add(0); bytes.AddRange(new byte[4]);
        for (int i = 0; i < 3000; i++) { bytes.Add(0xC0); bytes.Add(0x0C); bytes.AddRange(new byte[4]); }
        return [.. bytes];
    }

    [Fact]
    public async Task Owned_loopback_probe_flood_has_a_finite_endpoint_budget()
    {
        var ct = TestContext.Current.CancellationToken;
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var mdns = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var cutoff = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cutoff.CancelAfter(TimeSpan.FromSeconds(2));
        using var metadata = new TcpListener(IPAddress.Loopback, 0);
        metadata.Start(512);
        int metadataPort = ((IPEndPoint)metadata.LocalEndpoint).Port;
        int metadataRequests = 0;
        using var httpStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var http = Task.Run(async () =>
        {
            while (!httpStop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await metadata.AcceptTcpClientAsync(httpStop.Token); }
                catch (OperationCanceledException) { break; }
                using (client)
                {
                    Interlocked.Increment(ref metadataRequests);
                    byte[] answer = Encoding.ASCII.GetBytes("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    try { await client.GetStream().WriteAsync(answer, httpStop.Token); }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
                }
            }
        }, ct);
        var responder = Task.Run(async () =>
        {
            var probe = await wsd.ReceiveAsync(ct);
            string template = ProbeMatches($"http://127.0.0.1:{metadataPort}/metadata/");
            int start = template.IndexOf("<wsd:ProbeMatch>", StringComparison.Ordinal);
            int end = template.IndexOf("</wsd:ProbeMatch>", start, StringComparison.Ordinal) + "</wsd:ProbeMatch>".Length;
            string match = template[start..end];
            // macOS limits a local UDP datagram below the protocol's 65,507-byte payload maximum.
            // Keep each synthetic reply portable while preserving the same MaxHostsPerProbe + 1
            // distinct endpoints across the complete response flood.
            const int endpointsPerReply = 10;
            const int portableUdpPayloadBudget = 8 * 1024;
            for (int from = 0; from <= NetworkDiscovery.MaxHostsPerProbe; from += endpointsPerReply)
            {
                int count = Math.Min(endpointsPerReply, NetworkDiscovery.MaxHostsPerProbe + 1 - from);
                var matches = new StringBuilder();
                for (int i = from; i < from + count; i++)
                    matches.Append(match.Replace(Endpoint, $"urn:uuid:{i:00000000}-0000-4000-8000-000000000000", StringComparison.Ordinal));
                byte[] reply = Encoding.UTF8.GetBytes(template[..start] + matches + template[end..]);
                Assert.True(reply.Length < portableUdpPayloadBudget);
                Assert.Equal(count, NetworkDiscovery.ParseProbeMatches(reply).Count);
                await wsd.SendAsync(reply, probe.RemoteEndPoint, ct);
                // Yield to the receiver between the greater number of portable-sized datagrams so the
                // loopback socket queue, rather than the discovery budget, cannot become the test limit.
                await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
            }
        }, ct);
        var found = new List<NetworkHost>();
        long before = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, cutoff.Token, ct,
            (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)mdns.Client.LocalEndPoint!, [IPAddress.Loopback]);
        await responder.WaitAsync(TimeSpan.FromSeconds(5), ct);
        httpStop.Cancel();
        await http.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.InRange(found.Count, 1, 1); // one replying address, even with many endpoint identities
        Assert.Equal(NetworkDiscovery.MaxHostsPerProbe, metadataRequests);
        TestContext.Current.TestOutputHelper?.WriteLine($"WS-Discovery: cap={NetworkDiscovery.MaxHostsPerProbe}, replies={NetworkDiscovery.MaxHostsPerProbe + 1}, metadata-requests={metadataRequests}, working-set-before={before}, process-peak={System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64}");
    }

    [Fact]
    public async Task Owned_loopback_mdns_distinct_names_have_a_finite_retention_budget()
    {
        var ct = TestContext.Current.CancellationToken;
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var mdns = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var cutoff = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cutoff.CancelAfter(TimeSpan.FromSeconds(2));
        var responder = Task.Run(async () =>
        {
            var query = await mdns.ReceiveAsync(ct);
            for (int i = 0; i <= NetworkDiscovery.MaxHostsPerProbe; i++)
            {
                byte[] reply = MdnsAnswer(IPAddress.Parse($"10.0.{i / 256}.{i % 256}"), i.ToString("X3"));
                await mdns.SendAsync(reply, query.RemoteEndPoint, ct);
            }
        }, ct);
        var found = new List<NetworkHost>();
        long before = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, cutoff.Token, ct,
            (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)mdns.Client.LocalEndPoint!, [IPAddress.Loopback]);
        await responder.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.InRange(found.Count, 1, NetworkDiscovery.MaxHostsPerProbe);
        TestContext.Current.TestOutputHelper?.WriteLine($"mDNS: cap={NetworkDiscovery.MaxHostsPerProbe}, replies={NetworkDiscovery.MaxHostsPerProbe + 1}, retained={found.Count}, working-set-before={before}, process-peak={System.Diagnostics.Process.GetCurrentProcess().PeakWorkingSet64}");
    }

    internal static string ProbeMatches(string xaddrs) => $"""
        <?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:wsd="http://schemas.xmlsoap.org/ws/2005/04/discovery" xmlns:wsdp="http://schemas.xmlsoap.org/ws/2006/02/devprof" xmlns:pub="http://schemas.microsoft.com/windows/pub/2005/07"><soap:Header><wsa:To>http://schemas.xmlsoap.org/ws/2004/08/addressing/role/anonymous</wsa:To><wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches</wsa:Action><wsa:MessageID>urn:uuid:5d2c1a4e-1111-4222-8333-444455556666</wsa:MessageID><wsa:RelatesTo>urn:uuid:0e9e8f7a-1111-4222-8333-444455556666</wsa:RelatesTo><wsd:AppSequence InstanceId="3" MessageNumber="1"></wsd:AppSequence></soap:Header><soap:Body><wsd:ProbeMatches><wsd:ProbeMatch><wsa:EndpointReference><wsa:Address>{Endpoint}</wsa:Address></wsa:EndpointReference><wsd:Types>wsdp:Device pub:Computer</wsd:Types><wsd:XAddrs>{xaddrs}</wsd:XAddrs><wsd:MetadataVersion>2</wsd:MetadataVersion></wsd:ProbeMatch></wsd:ProbeMatches></soap:Body></soap:Envelope>
        """;

    /// <summary>What Windows answers a WS-Transfer Get with (the device host first, then the computer it runs on).</summary>
    internal const string Metadata = """
        <?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope" xmlns:wsa="http://schemas.xmlsoap.org/ws/2004/08/addressing" xmlns:wsx="http://schemas.xmlsoap.org/ws/2004/09/mex" xmlns:wsdp="http://schemas.xmlsoap.org/ws/2006/02/devprof" xmlns:pub="http://schemas.microsoft.com/windows/pub/2005/07"><soap:Header><wsa:Action>http://schemas.xmlsoap.org/ws/2004/09/transfer/GetResponse</wsa:Action></soap:Header><soap:Body><wsx:Metadata><wsx:MetadataSection Dialect="http://schemas.xmlsoap.org/ws/2006/02/devprof/ThisDevice"><wsdp:ThisDevice><wsdp:FriendlyName>Microsoft Publication Service Device Host</wsdp:FriendlyName><wsdp:FirmwareVersion>1.0</wsdp:FirmwareVersion></wsdp:ThisDevice></wsx:MetadataSection><wsx:MetadataSection Dialect="http://schemas.xmlsoap.org/ws/2006/02/devprof/Relationship"><wsdp:Relationship Type="http://schemas.xmlsoap.org/ws/2006/02/devprof/host"><wsdp:Host><wsa:EndpointReference><wsa:Address>urn:uuid:1f7b8c3a-0000-4000-8000-00155d000001</wsa:Address></wsa:EndpointReference><wsdp:Types>pub:Computer</wsdp:Types><pub:Computer>TESTBOX/Workgroup:WORKGROUP</pub:Computer></wsdp:Host></wsdp:Relationship></wsx:MetadataSection></wsx:Metadata></soap:Body></soap:Envelope>
        """;

    /// <summary>An mDNS answer: a PTR to "NAS._smb._tcp.local", its SRV naming nas.local, and nas.local's address.</summary>
    internal static byte[] MdnsAnswer(IPAddress address, string code = "NAS")
    {
        if (code.Length != 3) throw new ArgumentException("The synthetic DNS name must be three ASCII characters.", nameof(code));
        string lower = code.ToLowerInvariant();
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
        Record(12, [3, (byte)code[0], (byte)code[1], (byte)code[2], (byte)(0xC0 | service >> 8), (byte)service]);
        int instance = bytes.Count;
        bytes.AddRange(new byte[] { 3, (byte)code[0], (byte)code[1], (byte)code[2], (byte)(0xC0 | service >> 8), (byte)service });
        var srv = new List<byte> { 0, 0, 0, 0, 0x01, 0xBD };
        srv.AddRange(new byte[] { 3, (byte)lower[0], (byte)lower[1], (byte)lower[2], 5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0 });
        Record(33, srv);
        Name(lower, "local");
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

    [Theory]
    [InlineData(0)]
    [InlineData(1200)]
    public async Task A_name_that_arrives_after_the_search_window_is_still_used(int probeDelayMilliseconds)
    {
        // I23/I117: close the probe window after the device receives the metadata request and before it replies.
        // A one-second timer could expire before any probe was handled on a busy ARM64 runner, testing no name lookup.
        var ct = TestContext.Current.CancellationToken;
        using var cutoff = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var http = new TcpListener(IPAddress.Loopback, 0);
        http.Start();
        int httpPort = ((IPEndPoint)http.LocalEndpoint).Port;
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var silentMdns = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        wsd.Client.ReceiveTimeout = 10_000;
        Exception? deviceError = null;
        bool repliedAfterCutoff = false;
        // This dedicated device thread also closes the window; a delayed pool continuation cannot postpone the reply
        // until the metadata client's own timeout. The second control delays the probe beyond the former 1 s timer.
        var device = new Thread(() =>
        {
            try
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                wsd.Receive(ref from);
                Thread.Sleep(probeDelayMilliseconds);
                wsd.Send(Encoding.UTF8.GetBytes(ProbeMatches($"http://127.0.0.1:{httpPort}/1f7b8c3a/")), from);
                using var client = http.AcceptTcpClient();
                using var stream = client.GetStream();
                stream.ReadTimeout = 10_000;
                var buffer = new byte[16384];
                int read = 0;
                while (!Encoding.UTF8.GetString(buffer, 0, read).Contains("</soap:Envelope>", StringComparison.Ordinal))
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n == 0) throw new IOException("The metadata request ended before its envelope.");
                    read += n;
                }
                cutoff.Cancel();
                repliedAfterCutoff = cutoff.IsCancellationRequested;
                byte[] body = Encoding.UTF8.GetBytes(Metadata);
                stream.Write(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/soap+xml\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"));
                stream.Write(body);
            }
            catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException) { deviceError = ex; }
        }) { IsBackground = true };
        device.Start();

        var found = new List<NetworkHost>();
        try
        {
            await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, cutoff.Token, ct,
                (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)silentMdns.Client.LocalEndPoint!, [IPAddress.Loopback])
                .WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(device.Join(TimeSpan.FromSeconds(5)), "The owned device did not finish.");
            Assert.Null(deviceError);
            Assert.True(repliedAfterCutoff, "The name was not sent after the probe window closed.");
            var host = Assert.Single(found);
            Assert.Equal("TESTBOX", host.Name);
            Assert.Equal("Workgroup: WORKGROUP", host.Detail);
        }
        finally
        {
            cutoff.Cancel();
            http.Stop();
            wsd.Dispose();
            Assert.True(device.Join(TimeSpan.FromSeconds(10)), "The owned device survived cleanup.");
        }
    }

    [Fact]
    public async Task A_device_whose_metadata_redirects_elsewhere_is_not_followed_there()
    {
        // Release plan B05: a device's metadata is asked of its own address; a redirect from there to another address (a
        // service on this computer, say) would make FileCat send a request wherever any device on the network chose.
        var ct = TestContext.Current.CancellationToken;
        using var elsewhere = new TcpListener(IPAddress.Loopback, 0);
        elsewhere.Start();
        int other = ((IPEndPoint)elsewhere.LocalEndpoint).Port;
        using var metadata = new TcpListener(IPAddress.Loopback, 0);
        metadata.Start();
        int port = ((IPEndPoint)metadata.LocalEndpoint).Port;
        var serving = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await metadata.AcceptTcpClientAsync(ct);
                var stream = client.GetStream();
                var buffer = new byte[64 * 1024];
                int read = 0;
                while (read < buffer.Length && !Encoding.ASCII.GetString(buffer, 0, read).Contains("</soap:Envelope>", StringComparison.Ordinal))
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(read), ct);
                    if (n == 0) break;
                    read += n;
                }
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{other}/redirected\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
            }
        }, ct);
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var answering = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var probe = await wsd.ReceiveAsync(ct);
                await wsd.SendAsync(Encoding.UTF8.GetBytes(ProbeMatches($"http://127.0.0.1:{port}/metadata/")), probe.RemoteEndPoint, ct);
            }
        }, ct);
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var found = new List<NetworkHost>();
        await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, TimeSpan.FromSeconds(2), ct,
            (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)silent.Client.LocalEndPoint!, [IPAddress.Loopback]);
        Assert.Equal("127.0.0.1", Assert.Single(found).Server);
        Assert.False(elsewhere.Pending(), "Discovery followed the device's redirect to another address.");
    }

    [Fact]
    public async Task A_device_that_names_another_address_is_not_followed_there()
    {
        var ct = TestContext.Current.CancellationToken;
        // The answer points its metadata at a different address, where something listens: FileCat never connects
        // there, and lists the device by the address it answered from. (Once judged by how long discovery took, which a
        // busy CI runner stretched past its bound.)
        using var elsewhere = new TcpListener(IPAddress.IPv6Loopback, 0);
        elsewhere.Start();
        int port = ((IPEndPoint)elsewhere.LocalEndpoint).Port;
        using var wsd = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var answering = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var probe = await wsd.ReceiveAsync(ct);
                await wsd.SendAsync(Encoding.UTF8.GetBytes(ProbeMatches($"http://[::1]:{port}/elsewhere/")), probe.RemoteEndPoint, ct);
            }
        }, ct);
        using var silent = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var found = new List<NetworkHost>();
        await NetworkDiscovery.DiscoverAsync(h => { lock (found) found.Add(h); }, TimeSpan.FromSeconds(1), ct,
            (IPEndPoint)wsd.Client.LocalEndPoint!, (IPEndPoint)silent.Client.LocalEndPoint!, [IPAddress.Loopback]);
        var host = Assert.Single(found);
        Assert.Equal("127.0.0.1", host.Server);
        Assert.False(elsewhere.Pending(), "Discovery connected to the address the device named instead of its own.");
    }
}
