using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>
/// I662, a regression of I612: Linux File-system details say where a mount's content comes from, its root within the source
/// file system, as /proc/self/mountinfo gives it. The lines here are fixtures, so these run on every system;
/// <see cref="UnixFileDetailsNativeTests"/> reads the real mount table on Linux.
/// </summary>
public sealed class LinuxMountDetailsTests
{
    // As Ubuntu 24.04's kernel writes them; the snapd line is the mount the Ubuntu GUI harness found I662 on (row a4).
    private const string SnapdLine = "519 31 0:27 /snapd/ns /run/snapd/ns rw,nosuid,nodev,noexec,relatime - tmpfs tmpfs rw,size=807880k,mode=755,inode64";
    private const string SlashLine = "32 2 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw";
    private const string NamespaceLine = "5385 519 0:5 mnt:[4026532859] /run/snapd/ns/snap-store.mnt rw - nsfs nsfs rw";

    private static UnixFileRecords.LinuxMount Parse(string line) =>
        Assert.IsType<UnixFileRecords.LinuxMount>(UnixFileRecords.ParseLinuxMount(line.Split(' ')));

    private static FileDetailsReport Details(string path, UnixFileRecords.LinuxMount mount, bool mountRoot, bool full) =>
        UnixFileRecords.AddLinuxMount(FileDetailsReport.Metadata(new FileDetailsSnapshot(path, DateTimeOffset.UtcNow, Guid.NewGuid()),
            new InspectionSection("Native statx metadata", [])), mount, mountRoot, full, "mountinfo fixture");

    /// <summary>
    /// The values of the report's fields named <paramref name="name"/> (a name, at least two spaces, the value), as the
    /// harness reads the copied text; "Source" does not match "Source root".
    /// </summary>
    private static string[] Lines(string text, string name) => text.Split('\n').Select(line => line.TrimEnd('\r').TrimStart())
        .Where(line => line.StartsWith(name + "  ", StringComparison.Ordinal)).Select(line => line[name.Length..].Trim()).ToArray();

    [Fact]
    public void A_bind_mounts_root_shows_its_source_root_in_the_item_and_the_volume_section()
    {
        var mount = Parse(SnapdLine);
        Assert.Equal(new UnixFileRecords.LinuxMount("0:27", "/snapd/ns", "/run/snapd/ns", "tmpfs", "tmpfs",
            "rw,nosuid,nodev,noexec,relatime; rw,size=807880k,mode=755,inode64"), mount);
        var report = Details("/run/snapd/ns", mount, mountRoot: true, full: true);
        Assert.Equal("/snapd/ns", report.Summary.SourceRoot?.Value);
        Assert.Contains(("Source root", "/snapd/ns"), report.Summary.ToReport().Sections.Single(s => s.Title == "Item").Fields);
        Assert.Equal(new (string, string)[]
        {
            ("Mounted at", "/run/snapd/ns"), ("File system", "tmpfs"), ("Source", "tmpfs"), ("Root within source", "/snapd/ns"),
            ("Options", "rw,nosuid,nodev,noexec,relatime; rw,size=807880k,mode=755,inode64"), ("Observation", "mountinfo fixture"),
        }, report.Technical.Sections.Single(s => s.Title == "Volume").Fields);
        // Copy all of the full record: findmnt's FSROOT on one line each, after its name.
        string text = report.ToText();
        Assert.Equal(["/snapd/ns"], Lines(text, "Source root"));
        Assert.Equal(["/snapd/ns"], Lines(text, "Root within source"));

        // The summary names the source root too; the Volume section belongs to the full record.
        var summary = Details("/run/snapd/ns", mount, mountRoot: true, full: false);
        Assert.Equal(["/snapd/ns"], Lines(summary.ToText(), "Source root"));
        Assert.DoesNotContain(summary.Technical.Sections, s => s.Title == "Volume");
    }

    [Fact]
    public void An_item_inside_the_mount_has_the_root_within_source_but_is_not_itself_a_source_root()
    {
        var report = Details("/run/snapd/ns/snap.snap-store.fstab", Parse(SnapdLine), mountRoot: false, full: true);
        Assert.Null(report.Summary.SourceRoot);
        Assert.Empty(Lines(report.ToText(), "Source root"));
        Assert.Equal(["/snapd/ns"], Lines(report.ToText(), "Root within source"));
    }

    [Fact]
    public void A_mount_of_the_whole_source_shows_slash_and_no_invented_folder()
    {
        var mount = Parse(SlashLine); // with an optional field (shared:1) before the separator
        Assert.Equal(new UnixFileRecords.LinuxMount("8:2", "/", "/", "ext4", "/dev/sda2", "rw,relatime; rw"), mount);
        foreach (var (path, mountRoot) in new[] { ("/", true), ("/srv/data", false) })
        {
            var report = Details(path, mount, mountRoot, full: true);
            Assert.Null(report.Summary.SourceRoot);
            Assert.Empty(Lines(report.ToText(), "Source root"));
            Assert.Equal(["/"], Lines(report.ToText(), "Root within source"));
            Assert.Equal(["/"], Lines(report.ToText(), "Mounted at"));
        }
    }

    [Fact]
    public void A_namespace_files_mount_shows_the_name_the_kernel_gives_its_root()
    {
        var report = Details("/run/snapd/ns/snap-store.mnt", Parse(NamespaceLine), mountRoot: true, full: true);
        Assert.Equal(["mnt:[4026532859]"], Lines(report.ToText(), "Source root"));
        Assert.Equal(["mnt:[4026532859]"], Lines(report.ToText(), "Root within source"));
    }

    [Fact]
    public void Escaped_paths_are_decoded_and_each_stays_on_its_own_line()
    {
        // The kernel writes a space, tab, newline and backslash in a path as \040, \011, \012 and \134.
        var mount = Parse(@"77 32 8:2 /srv/a\040b\011c\012d\134e /mnt/bind\040here rw,relatime shared:9 master:2 - ext4 /dev/disk\040one rw,errors=remount-ro");
        Assert.Equal("/srv/a b\tc\nd\\e", mount.Root);
        Assert.Equal("/mnt/bind here", mount.MountPoint);
        Assert.Equal("/dev/disk one", mount.Source);
        var report = Details("/mnt/bind here", mount, mountRoot: true, full: true);
        Assert.Equal(mount.Root, report.Summary.SourceRoot?.Value); // the exact value, for anything that compares it
        // Shown, the tab and the newline are written out, so the root cannot break the report into lines of its own.
        char escape = (char)92;
        string shown = $"/srv/a b{escape}u0009c{escape}u000Ad{escape}e";
        string text = report.ToText();
        Assert.Equal([shown], Lines(text, "Source root"));
        Assert.Equal([shown], Lines(text, "Root within source"));
        Assert.Equal(["/mnt/bind here"], Lines(text, "Mounted at"));
        Assert.Equal(["/dev/disk one"], Lines(text, "Source"));
        Assert.DoesNotContain("c\nd", text, StringComparison.Ordinal);

        // mountinfo leaves other characters as they are; one that reorders text is written out too.
        var bidi = Parse($"78 32 8:2 /srv/x{(char)0x202E}y /mnt/x rw - ext4 /dev/sda2 rw");
        Assert.Equal([$"/srv/x{escape}u202Ey"], Lines(Details("/mnt/x", bidi, mountRoot: true, full: false).ToText(), "Source root"));
    }

    [Theory]
    [InlineData(@"/a\x41", @"/a\x41")] // not octal
    [InlineData(@"/a\08", @"/a\08")] // 8 is not an octal digit
    [InlineData(@"/a\04", @"/a\04")] // too short
    [InlineData(@"/a\400", @"/a\400")] // more than a byte
    [InlineData(@"/a\200", @"/a\200")] // not ASCII, which the kernel never escapes
    [InlineData(@"/a\134134", @"/a\134")] // an escaped backslash, then text: decoded once
    [InlineData(@"/a\040", "/a ")]
    public void Only_the_escapes_the_kernel_writes_are_decoded(string field, string expected) =>
        Assert.Equal(expected, UnixFileRecords.UnescapeMountField(field));

    [Theory]
    [InlineData("519 31 0:27 /snapd/ns /run/snapd/ns rw tmpfs tmpfs rw")] // no separator
    [InlineData("519 31 0:27 /snapd/ns - rw - tmpfs tmpfs rw")] // a separator before the mount options
    [InlineData("519 31 0:27 /snapd/ns /run/snapd/ns rw - tmpfs tmpfs")] // no super options
    [InlineData("519 31 0:27 /snapd/ns /run/snapd/ns rw - tmpfs tmpfs rw more")]
    [InlineData("519 31 0:27 /snapd/ns /run/snapd/ns rw -  tmpfs rw")] // no type
    public void A_line_that_is_not_a_mount_gives_none(string line) => Assert.Null(UnixFileRecords.ParseLinuxMount(line.Split(' ')));

    [Fact]
    public void Statx_says_whether_an_item_is_a_mounts_root_where_the_kernel_reports_it()
    {
        var mount = Parse(SnapdLine);
        const ulong MountRoot = 0x2000; // STATX_ATTR_MOUNT_ROOT
        // Reached through a symbolic link (on Ubuntu /var/run leads to /run), the item is still the mount's root.
        Assert.True(UnixFileRecords.IsLinuxMountRoot(MountRoot, MountRoot, "/var/run/snapd/ns", mount));
        Assert.False(UnixFileRecords.IsLinuxMountRoot(0, MountRoot, "/run/snapd/ns", mount));
        // Before Linux 5.8 statx does not say: the path is compared with the mount point.
        Assert.True(UnixFileRecords.IsLinuxMountRoot(0, 0, "/run/snapd/ns", mount));
        Assert.False(UnixFileRecords.IsLinuxMountRoot(MountRoot, 0, "/var/run/snapd/ns", mount));
    }
}
