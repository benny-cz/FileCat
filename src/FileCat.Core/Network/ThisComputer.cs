using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace FileCat.Core.Network;

/// <summary>
/// Whether a server named in a share's path is this computer. A share this computer serves itself is stored on its own
/// disks, so writing there writes to them: recovery's destination and own-file checks must not take it for another
/// computer's storage (release plan V09).
/// </summary>
public static class ThisComputer
{
    /// <summary>
    /// True for loopback names and addresses, this computer's names (alone or followed by a domain), and the addresses of
    /// its interfaces. Any other name is looked up for at most <paramref name="lookup"/>, so an alias that leads here counts;
    /// a name that does not answer in time is taken for another computer.
    /// </summary>
    public static bool Is(string server, TimeSpan? lookup = null)
    {
        string host = server.Trim();
        if (host.StartsWith('[') && host.EndsWith(']')) host = host[1..^1];
        if (host.Length == 0) return false;
        // Windows writes an IPv6 address in a share's path as "fe80--1s4.ipv6-literal.net".
        const string literal = ".ipv6-literal.net";
        if (host.EndsWith(literal, StringComparison.OrdinalIgnoreCase))
            host = host[..^literal.Length].Replace('-', ':').Replace('s', '%');
        if (IPAddress.TryParse(host, out var address)) return IsOwn(address);
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string own in Names())
            if (host.Equals(own, StringComparison.OrdinalIgnoreCase) || host.StartsWith(own + ".", StringComparison.OrdinalIgnoreCase)) return true;
        if (lookup is not { } wait || wait <= TimeSpan.Zero) return false;
        try
        {
            var addresses = Dns.GetHostAddressesAsync(host);
            return addresses.Wait(wait) && addresses.Result.Any(IsOwn);
        }
        catch (Exception ex) when (ex is AggregateException or SocketException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>A loopback or unspecified address, or one an interface of this computer has.</summary>
    public static bool IsOwn(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    var own = unicast.Address;
                    if (own.Equals(address)) return true;
                    // A scope-less link-local address names the same interface address as a scoped one.
                    if (own.AddressFamily == AddressFamily.InterNetworkV6 && address.AddressFamily == AddressFamily.InterNetworkV6 &&
                        address.ScopeId == 0 && new IPAddress(own.GetAddressBytes()).Equals(address)) return true;
                }
        }
        catch (NetworkInformationException)
        {
        }
        return false;
    }

    private static IEnumerable<string> Names()
    {
        yield return Environment.MachineName;
        string? dns = null;
        try { dns = Dns.GetHostName(); }
        catch (SocketException) { }
        if (!string.IsNullOrEmpty(dns)) yield return dns;
    }
}
