using System.Text.Json;
using FileCat.Recovery.Unix;

namespace FileCat.Core.Tests;

/// <summary>Controlled diskutil replies exercise production Mac topology interpretation without opening a device.</summary>
public sealed class MacIncompleteTopologyTests(ITestOutputHelper output)
{
    private const string Local = "<plist><dict><key>ParentWholeDisk</key><string>disk4</string></dict></plist>";
    private const string Physical = "<plist><dict><key>BusProtocol</key><string>PCI-Express</string></dict></plist>";
    private const string Image = "<plist><dict><key>BusProtocol</key><string>Disk Image</string></dict></plist>";

    [Theory]
    [InlineData("missing-store", false)]
    [InlineData("missing-store", true)]
    [InlineData("not-a-dictionary", false)]
    [InlineData("not-a-dictionary", true)]
    [InlineData("empty-identifier", false)]
    [InlineData("empty-identifier", true)]
    [InlineData("wrong-store-type", false)]
    [InlineData("wrong-store-type", true)]
    [InlineData("wrong-array-type", false)]
    [InlineData("wrong-array-type", true)]
    public void Incomplete_APFS_members_never_establish_a_different_disk(string kind, bool written)
    {
        string invalid = kind switch
        {
            "missing-store" => "<dict/>",
            "not-a-dictionary" => "<string>unavailable</string>",
            "empty-identifier" => "<dict><key>APFSPhysicalStore</key><string/></dict>",
            "wrong-store-type" => "<dict><key>APFSPhysicalStore</key><integer>5</integer></dict>",
            _ => "",
        };
        string stores = kind == "wrong-array-type" ? "<string>unavailable</string>" :
            "<array><dict><key>APFSPhysicalStore</key><string>disk4s1</string></dict>" + invalid + "</array>";
        string reply = "<plist><dict><key>ParentWholeDisk</key><string>disk2</string>" +
            "<key>APFSPhysicalStores</key>" + stores + "</dict></plist>";
        var queries = new List<string>();
        var actual = UnixDisks.MacWholeDisks("owned-mount", written, name =>
        {
            queries.Add(name);
            return name == "owned-mount" ? reply : Physical;
        }, _ => throw new InvalidOperationException("No disk image should be resolved."));
        Say("apfs-" + kind, written, actual, queries);
        Assert.Null(actual);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing-bus")]
    [InlineData("empty-bus")]
    [InlineData("wrong-bus-type")]
    [InlineData("not-a-dictionary")]
    public void An_unavailable_whole_disk_reply_is_unknown_when_classifying_writes(string kind)
    {
        string reply = kind switch
        {
            "empty" => "",
            "missing-bus" => "<plist><dict/></plist>",
            "empty-bus" => "<plist><dict><key>BusProtocol</key><string/><key>VirtualOrPhysical</key><string>Unknown</string></dict></plist>",
            "wrong-bus-type" => "<plist><dict><key>BusProtocol</key><integer>1</integer></dict></plist>",
            _ => "<plist><array/></plist>",
        };
        var queries = new List<string>();
        var actual = UnixDisks.MacWholeDisks("owned-mount", true, name =>
        {
            queries.Add(name);
            return name == "owned-mount" ? Local : reply;
        }, _ => throw new InvalidOperationException("No disk image should be resolved."));
        Say("whole-" + kind, true, actual, queries);
        Assert.Null(actual);
    }

    [Fact]
    public void A_known_physical_write_remains_classified()
    {
        var actual = UnixDisks.MacWholeDisks("owned-mount", true, name => name == "owned-mount" ? Local : Physical,
            _ => throw new InvalidOperationException("Not a disk image."));
        Say("physical-write", true, actual, []);
        Assert.Equal(["disk4"], actual);
    }

    [Fact]
    public void A_source_keeps_its_known_disk_without_expanding_an_image_write()
    {
        var actual = UnixDisks.MacWholeDisks("owned-source", false, name =>
        {
            Assert.Equal("owned-source", name);
            return Local;
        }, _ => throw new InvalidOperationException("Source classification does not expand image writes."));
        Say("source", false, actual, []);
        Assert.Equal(["disk4"], actual);
    }

    [Fact]
    public void Every_complete_APFS_member_and_image_backing_is_retained()
    {
        const string reply = "<plist><dict><key>APFSPhysicalStores</key><array>" +
            "<dict><key>APFSPhysicalStore</key><string>disk4s1</string></dict>" +
            "<dict><key>APFSPhysicalStore</key><string>disk5s2</string></dict></array></dict></plist>";
        var actual = UnixDisks.MacWholeDisks("owned-mount", true, name => name == "owned-mount" ? reply : Image,
            disk => disk == "disk4" ? ["disk0"] : ["disk1"]);
        Say("complete-images", true, actual, []);
        Assert.Equal(["disk4", "disk0", "disk5", "disk1"], actual);
    }

    [Fact]
    public void An_unresolved_image_backing_stays_unknown()
    {
        var actual = UnixDisks.MacWholeDisks("owned-mount", true, name => name == "owned-mount" ? Local : Image, _ => null);
        Say("unknown-image", true, actual, []);
        Assert.Null(actual);
    }

    private void Say(string kind, bool written, IReadOnlyList<string>? actual, List<string> queries) =>
        output.WriteLine("MAC_INCOMPLETE_TOPOLOGY " + JsonSerializer.Serialize(new
        {
            kind, written, actual, queries, noDeviceOpened = true, controlledQueryReplies = true,
            noClaimOfActualDiskutilFailureOrPhysicalDataLoss = true,
        }));
}
