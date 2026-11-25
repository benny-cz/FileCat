using System.Net;
using System.Net.Sockets;

namespace FileCat.Remote.Tests;

/// <summary>
/// The owned loopback sshd the Remote tests start must be the server that answers on its port (I679: main's CI once ran
/// a test against a port this fixture's sshd had lost to another fixture's, and the connection was refused).
/// </summary>
public sealed class TestSshdTests
{
    [Fact]
    public void A_port_another_server_holds_is_given_up_for_one_this_sshd_binds()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("The owned test sshd runs on Linux and macOS."); return; }
        // Another fixture's server already listens on the port this one is about to use: a connect there succeeds.
        using var other = new TcpListener(IPAddress.Loopback, 0);
        other.Start();
        int taken = ((IPEndPoint)other.LocalEndpoint).Port;
        TestSshd.FirstPortForTests.Value = taken;
        try
        {
            using var sshd = TestSshd.TryStart();
            if (sshd is null) { Assert.Skip("This owned OpenSSH case requires a user-mode sshd; it is unavailable here."); return; }
            Assert.NotEqual(taken, sshd.Port);
            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, sshd.Port);
            client.ReceiveTimeout = 5000;
            using var reader = new StreamReader(client.GetStream());
            Assert.StartsWith("SSH-2.0-", reader.ReadLine()); // this sshd answers on its port
        }
        finally { TestSshd.FirstPortForTests.Value = null; }
    }
}
