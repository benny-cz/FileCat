using System.Net;
using FileCat.Core.Network;

namespace FileCat.Core.Tests;

/// <summary>
/// Whether a server named in a share's path is this computer (release plan V09, I09): a share it serves itself is stored on
/// its own disks, so recovery must not take it for another computer's storage.
/// </summary>
public sealed class ThisComputerTests
{
    [Fact]
    public void Loopback_this_computers_names_and_its_own_addresses_are_this_computer()
    {
        Assert.True(ThisComputer.Is("localhost"));
        Assert.True(ThisComputer.Is("LOCALHOST"));
        Assert.True(ThisComputer.Is("127.0.0.1"));
        Assert.True(ThisComputer.Is("127.10.20.30"));
        Assert.True(ThisComputer.Is("::1"));
        Assert.True(ThisComputer.Is("[::1]"));
        Assert.True(ThisComputer.Is("0--1.ipv6-literal.net")); // Windows' way of writing ::1 in a share's path
        Assert.True(ThisComputer.Is(Environment.MachineName));
        Assert.True(ThisComputer.Is(Environment.MachineName.ToLowerInvariant() + ".corp.example"));
        foreach (var address in NetworkDiscovery.LocalAddresses()) Assert.True(ThisComputer.Is(address.ToString()));
        Assert.True(ThisComputer.IsOwn(IPAddress.Parse("::ffff:127.0.0.1")));

        Assert.False(ThisComputer.Is("nas.example.invalid")); // no lookup asked for
        Assert.False(ThisComputer.Is("nas.example.invalid", TimeSpan.FromSeconds(2))); // a name that does not resolve
        Assert.False(ThisComputer.Is("192.0.2.10")); // a documentation address no interface has
        Assert.False(ThisComputer.Is(""));
    }
}
