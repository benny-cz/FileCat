using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

/// <summary>V09/I106: incomplete block dependencies cannot prove that a write goes to a different disk.</summary>
public sealed class IncompleteLinuxTopologyTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Incomplete_block_dependencies_are_unknown(bool written, bool directoryReplacedByFile)
    {
        string sys = _dir.Dir("sys");
        string device = Path.Combine(sys, "class", "block", "dm-0");
        Directory.CreateDirectory(device);
        if (directoryReplacedByFile) File.WriteAllText(Path.Combine(device, "slaves"), "incomplete");
        var topology = new UnixDisks.LinuxTopology(sys, [], p => p, _ => false);

        Assert.Null(topology.BlockDisks("dm-0", written));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_folder_with_incomplete_block_dependencies_is_unknown(bool directoryReplacedByFile)
    {
        string sys = _dir.Dir("sys");
        string device = Path.Combine(sys, "class", "block", "dm-0");
        Directory.CreateDirectory(device);
        if (directoryReplacedByFile) File.WriteAllText(Path.Combine(device, "slaves"), "incomplete");
        var mounts = UnixDisks.ParseMountInfo(["30 22 253:0 / /data rw - ext4 /dev/dm-0 rw"]);
        var topology = new UnixDisks.LinuxTopology(sys, mounts, p => p, _ => false);

        Assert.Null(topology.FolderDisks("/data/out"));
    }

    [Fact]
    public void Known_empty_leaves_and_complete_mapped_dependencies_still_resolve()
    {
        string sys = _dir.Dir("sys");
        Directory.CreateDirectory(Path.Combine(sys, "class", "block", "sda", "slaves"));
        Directory.CreateDirectory(Path.Combine(sys, "class", "block", "dm-0", "slaves", "sda"));
        var topology = new UnixDisks.LinuxTopology(sys, [], p => p, _ => false);

        foreach (bool written in new[] { false, true })
        {
            Assert.Equal(["sda"], topology.BlockDisks("sda", written));
            Assert.Equal(["sda"], topology.BlockDisks("dm-0", written));
        }
    }

    [Fact]
    public void Disappearing_dependencies_do_not_reclassify_a_mapped_device_as_a_leaf()
    {
        string sys = _dir.Dir("sys");
        Directory.CreateDirectory(Path.Combine(sys, "class", "block", "sda", "slaves"));
        string dependencies = Path.Combine(sys, "class", "block", "dm-0", "slaves");
        Directory.CreateDirectory(Path.Combine(dependencies, "sda"));
        var mounts = UnixDisks.ParseMountInfo(["30 22 253:0 / /data rw - ext4 /dev/dm-0 rw"]);
        var topology = new UnixDisks.LinuxTopology(sys, mounts, p => p, _ => false);
        Assert.Equal(["sda"], topology.FolderDisks("/data/out"));

        Directory.Delete(Path.Combine(dependencies, "sda"));
        Directory.Delete(dependencies);

        Assert.Null(topology.FolderDisks("/data/out"));
        Assert.Null(topology.DeviceDisks("/dev/dm-0"));
    }
}
