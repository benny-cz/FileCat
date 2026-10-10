using System.Text.Json;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

/// <summary>Unknown userspace/network backing cannot establish an independent recovery source disk.</summary>
public sealed class NetworkSourceTopologyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("nbd0", false, false)]
    [InlineData("nbd0", false, true)]
    [InlineData("nbd0", true, false)]
    [InlineData("nbd0", true, true)]
    [InlineData("nbd17", false, false)]
    [InlineData("nbd17", false, true)]
    [InlineData("nbd17", true, false)]
    [InlineData("nbd17", true, true)]
    [InlineData("rbd0", false, false)]
    [InlineData("rbd0", false, true)]
    [InlineData("rbd0", true, false)]
    [InlineData("rbd0", true, true)]
    [InlineData("rbd23", false, false)]
    [InlineData("rbd23", false, true)]
    [InlineData("rbd23", true, false)]
    [InlineData("rbd23", true, true)]
    public void Unresolved_network_backing_stays_unknown_for_sources_and_writes(string device, bool written, bool mapped)
    {
        using var dir = new TempDir();
        string sys = dir.Dir("sys");
        Directory.CreateDirectory(Path.Combine(sys, "class", "block", device, "slaves"));
        Directory.CreateDirectory(Path.Combine(sys, "class", "block", "sda", "slaves"));
        Directory.CreateDirectory(Path.Combine(sys, "class", "block", "dm-0", "slaves", device));
        var mounts = UnixDisks.ParseMountInfo(["30 22 8:0 / /owned rw - ext4 /dev/sda rw"]);
        var topology = new UnixDisks.LinuxTopology(sys, mounts, p => p, _ => false);
        string selected = mapped ? "dm-0" : device;
        var actual = topology.BlockDisks(selected, written);
        var local = topology.FolderDisks("/owned/state");
        bool? shares = actual is null || local is null ? null : actual.Intersect(local).Any();
        output.WriteLine("NETWORK_SOURCE_TOPOLOGY " + JsonSerializer.Serialize(new
        {
            device, written, mapped, selected, actual, local, shares,
            noDeviceOpened = true, ownedSyntheticSysfsAndMounts = true,
            noClaimOfActualBackingOrPhysicalDataLoss = true,
        }));
        Assert.Equal(["sda"], local);
        Assert.Null(actual);
        Assert.Null(shares);
        if (!written) Assert.Null(topology.DeviceDisks("/dev/" + selected));
    }
}
