using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.HiddenData;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>D-55: what streams and attributes say, and the real ones of each system.</summary>
public sealed partial class HiddenDataTests
{
    private static string Summary(string name, HiddenKind kind, byte[] value) =>
        HiddenDataDecoder.Describe(new HiddenItem(name, kind, value.Length), value).Summary;

    [Fact]
    public void Download_marks_say_where_a_file_came_from()
    {
        var motw = Encoding.UTF8.GetBytes("[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://example.com/page\r\nHostUrl=https://example.com/tool.exe\r\n");
        var (summary, details) = HiddenDataDecoder.Describe(new HiddenItem("Zone.Identifier", HiddenKind.Stream, motw.Length), motw);
        Assert.Equal("Mark of the Web: from the Internet · https://example.com/tool.exe", summary);
        Assert.Contains("Linked from: https://example.com/page", details);

        // macOS: "flags;hex seconds;agent;event".
        var quarantine = Encoding.UTF8.GetBytes("0083;66f1c2a3;Safari;7F3D2C1B-0000-4000-8000-000000000001");
        var (qSummary, qDetails) = HiddenDataDecoder.Describe(new HiddenItem("com.apple.quarantine", HiddenKind.Attribute, quarantine.Length), quarantine);
        Assert.StartsWith("Quarantined: downloaded by Safari on ", qSummary);
        Assert.Contains("Flags: 0x0083", qDetails);

        Assert.Equal("Downloaded from https://example.com/a.iso", Summary("user.xdg.origin.url", HiddenKind.Attribute, Encoding.UTF8.GetBytes("https://example.com/a.iso")));
    }

    [Fact]
    public void Linux_labels_capabilities_and_ACLs_read_as_their_tools_write_them()
    {
        Assert.Equal("SELinux label system_u:object_r:bin_t:s0", Summary("security.selinux", HiddenKind.Attribute, Encoding.UTF8.GetBytes("system_u:object_r:bin_t:s0\0")));
        // setcap cap_net_raw,cap_net_admin+ep: revision 2 with the effective flag, bits 12 and 13 permitted.
        var caps = new byte[20];
        BitConverter.TryWriteBytes(caps.AsSpan(0), 0x02000001u);
        BitConverter.TryWriteBytes(caps.AsSpan(4), (1u << 12) | (1u << 13));
        Assert.Equal("Capabilities: cap_net_admin,cap_net_raw=ep", Summary("security.capability", HiddenKind.Attribute, caps));
        // Revision 3 names the namespace's root.
        var caps3 = new byte[24];
        caps.CopyTo(caps3, 0);
        BitConverter.TryWriteBytes(caps3.AsSpan(0), 0x03000000u);
        BitConverter.TryWriteBytes(caps3.AsSpan(20), 1000u);
        Assert.Equal("Capabilities: cap_net_admin,cap_net_raw=p [rootid=1000]", Summary("security.capability", HiddenKind.Attribute, caps3));

        // user::rw-,user:1000:r--,group::r--,mask::r--,other::---
        var acl = new List<byte>(BitConverter.GetBytes(2u));
        void Entry(ushort tag, ushort perm, uint id)
        {
            acl.AddRange(BitConverter.GetBytes(tag));
            acl.AddRange(BitConverter.GetBytes(perm));
            acl.AddRange(BitConverter.GetBytes(id));
        }
        Entry(0x01, 6, uint.MaxValue);
        Entry(0x02, 4, 1000);
        Entry(0x04, 4, uint.MaxValue);
        Entry(0x10, 4, uint.MaxValue);
        Entry(0x20, 0, uint.MaxValue);
        Assert.Equal("Access list: user::rw-,user:1000:r--,group::r--,mask::r--,other::---", Summary("system.posix_acl_access", HiddenKind.Attribute, [.. acl]));
    }

    [Fact]
    public void macOS_attributes_decode_their_binary_forms()
    {
        // kMDItemWhereFroms: a binary property list holding an array of two strings.
        byte[] whereFroms = BinaryPlistOfStrings("https://example.com/app.dmg", "https://example.com/");
        var (froms, fromDetails) = HiddenDataDecoder.Describe(new HiddenItem("com.apple.metadata:kMDItemWhereFroms", HiddenKind.Attribute, whereFroms.Length), whereFroms);
        Assert.Equal("Downloaded from https://example.com/app.dmg", froms);
        Assert.Equal(2, fromDetails.Count);

        // FinderInfo: type 'TEXT', creator 'ttxt', invisible and a color label.
        var finder = new byte[32];
        "TEXTttxt"u8.CopyTo(finder);
        finder[8] = 0x40;
        finder[9] = 0x04;
        Assert.Equal("Finder information: type 'TEXT', creator 'ttxt', invisible, color label 2", Summary("com.apple.FinderInfo", HiddenKind.Attribute, finder));
        Assert.Equal("Finder information (empty)", Summary("com.apple.FinderInfo", HiddenKind.Attribute, new byte[32]));

        // decmpfs: 'fpmc', LZFSE in the resource fork, 1 MiB uncompressed.
        var decmpfs = new byte[16];
        "fpmc"u8.CopyTo(decmpfs);
        BitConverter.TryWriteBytes(decmpfs.AsSpan(4), 12u);
        BitConverter.TryWriteBytes(decmpfs.AsSpan(8), 1048576ul);
        Assert.Equal("Compressed by the file system (LZFSE, in the resource fork), 1 MiB uncompressed", Summary("com.apple.decmpfs", HiddenKind.Attribute, decmpfs));

        // Undocumented: provenance's identifier after a 3-byte header.
        Assert.Contains("id 0807060504030201", Summary("com.apple.provenance", HiddenKind.Attribute, [1, 2, 0, 1, 2, 3, 4, 5, 6, 7, 8]));
    }

    [Fact]
    public void NTFS_attributes_that_WSL_and_the_kernel_keep_are_read()
    {
        Assert.Equal("WSL: owner uid 1000", Summary("$LXUID", HiddenKind.NtfsAttribute, BitConverter.GetBytes(1000u)));
        Assert.Equal("WSL: mode 100644 (-rw-r--r--)", Summary("$LXMOD", HiddenKind.NtfsAttribute, BitConverter.GetBytes(0x81A4u)));
        var lxattrb = new byte[56];
        BitConverter.TryWriteBytes(lxattrb.AsSpan(4), 0x41EDu); // drwxr-xr-x
        BitConverter.TryWriteBytes(lxattrb.AsSpan(8), 1000u);
        BitConverter.TryWriteBytes(lxattrb.AsSpan(12), 100u);
        Assert.Equal("WSL 1: mode 40755 (drwxr-xr-x), uid 1000, gid 100", Summary("LXATTRB", HiddenKind.NtfsAttribute, lxattrb));
        Assert.StartsWith("Kept by the Windows kernel (PURGE.ESBCACHE)", Summary("$KERNEL.PURGE.ESBCACHE", HiddenKind.NtfsAttribute, new byte[80]));
        Assert.Equal("Code Integrity: signed by the catalog Microsoft-Windows-Client.cat",
            Summary("$CI.CATALOGHINT", HiddenKind.NtfsAttribute, [1, 0, 34, 0, .. Encoding.ASCII.GetBytes("Microsoft-Windows-Client.cat")]));
        Assert.Contains("SmartScreen checked it (Anaheim)", Summary("SmartScreen", HiddenKind.Stream, Encoding.UTF8.GetBytes("Anaheim")));
    }

    [Fact]
    public void Anything_unknown_or_damaged_is_shown_not_hidden_and_never_fails()
    {
        Assert.Equal("a note someone left", Summary("hidden-note", HiddenKind.Stream, Encoding.UTF8.GetBytes("a note someone left\nline two")));
        Assert.Equal("3 bytes of binary data", Summary("blob", HiddenKind.Stream, [0, 1, 2]));
        Assert.Equal("Empty", Summary("nothing", HiddenKind.Stream, []));
        // Every decoder, fed every cut of its input and random bytes: an answer every time.
        var random = new Random(55);
        foreach (var (name, kind) in new[]
                 {
                     ("Zone.Identifier", HiddenKind.Stream), ("AFP_AfpInfo", HiddenKind.Stream), ("security.capability", HiddenKind.Attribute),
                     ("system.posix_acl_access", HiddenKind.Attribute), ("com.apple.quarantine", HiddenKind.Attribute),
                     ("com.apple.metadata:kMDItemWhereFroms", HiddenKind.Attribute), ("com.apple.FinderInfo", HiddenKind.Attribute),
                     ("com.apple.decmpfs", HiddenKind.Attribute), ("com.apple.macl", HiddenKind.Attribute), ("com.apple.provenance", HiddenKind.Attribute),
                     ("com.apple.lastuseddate#PS", HiddenKind.Attribute), ("LXATTRB", HiddenKind.NtfsAttribute), ("$LXMOD", HiddenKind.NtfsAttribute),
                     ("$CI.CATALOGHINT", HiddenKind.NtfsAttribute),
                 })
        {
            for (int length = 0; length < 96; length++)
            {
                var bytes = new byte[length];
                random.NextBytes(bytes);
                Assert.False(string.IsNullOrEmpty(HiddenDataDecoder.Describe(new HiddenItem(name, kind, length), bytes).Summary), $"{name} {length}");
            }
        }
        var plist = BinaryPlistOfStrings("x");
        for (int cut = 0; cut <= plist.Length; cut++)
            HiddenDataDecoder.Describe(new HiddenItem("com.apple.metadata:kMDItemWhereFroms", HiddenKind.Attribute, cut), plist.AsSpan(0, cut));
    }

    /// <summary>A minimal bplist00: an array of ASCII strings.</summary>
    private static byte[] BinaryPlistOfStrings(params string[] strings)
    {
        var body = new List<byte>("bplist00"u8.ToArray());
        var offsets = new List<int> { body.Count };
        body.Add((byte)(0xA0 | strings.Length));
        for (int i = 0; i < strings.Length; i++) body.Add((byte)(i + 1));
        foreach (string s in strings)
        {
            offsets.Add(body.Count);
            if (s.Length < 15) body.Add((byte)(0x50 | s.Length));
            else
            {
                body.Add(0x5F);
                body.Add(0x10);
                body.Add((byte)s.Length);
            }
            body.AddRange(Encoding.ASCII.GetBytes(s));
        }
        int table = body.Count;
        foreach (int offset in offsets) body.Add((byte)offset);
        var trailer = new byte[32];
        trailer[6] = 1;
        trailer[7] = 1;
        trailer[15] = (byte)offsets.Count;
        trailer[31] = (byte)table;
        body.AddRange(trailer);
        return [.. body];
    }

    // ---- The real thing on each system ------------------------------------------------------------------------

    [Fact]
    public void A_files_own_streams_and_attributes_are_listed_read_and_deleted()
    {
        using var dir = new TempDir();
        string file = Path.Combine(dir.Path, "tool.bin");
        File.WriteAllText(file, "contents");
        IHiddenData hidden;
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows' streams and NTFS attributes are tested with the Windows platform (WindowsHiddenDataTests).");
            return;
        }
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            hidden = new UnixHiddenData();
            string name = OperatingSystem.IsMacOS() ? "com.apple.quarantine" : "user.xdg.origin.url";
            byte[] value = Encoding.UTF8.GetBytes(OperatingSystem.IsMacOS() ? "0083;66f1c2a3;Safari;" : "https://example.com/tool.bin");
            if (SetAttribute(file, name, value) != 0)
            {
                Assert.Skip($"This file system keeps no extended attributes here (errno {Marshal.GetLastPInvokeError()}).");
                return;
            }
            Assert.Equal(0, SetAttribute(file, OperatingSystem.IsMacOS() ? "org.filecat.payload" : "user.filecat.payload", new byte[3000]));
            // A resource fork, written as a Mac program writes one.
            if (OperatingSystem.IsMacOS()) File.WriteAllBytes(Path.Join(file, "..namedfork", "rsrc"), new byte[5000]);
        }
        else
        {
            Assert.Skip("No streams or attributes here.");
            return;
        }

        var items = hidden.List(file);
        var mark = Assert.Single(items, i => i.Name is "Zone.Identifier" or "com.apple.quarantine" or "user.xdg.origin.url");
        var (summary, _) = HiddenDataDecoder.Describe(mark, hidden.Read(file, mark, 65536));
        Assert.Matches("Mark of the Web|Quarantined|Downloaded from", summary);
        var payload = Assert.Single(items, i => i.Name.EndsWith("payload", StringComparison.Ordinal));
        Assert.Equal(3000, payload.Size);
        using (var stream = hidden.Open(file, payload)) Assert.Equal(payload.Size, stream.Length);
        if (OperatingSystem.IsMacOS())
        {
            var fork = Assert.Single(items, i => i.Kind == HiddenKind.ResourceFork);
            Assert.Equal(5000, fork.Size);
            using var stream = hidden.Open(file, fork);
            Assert.Equal(5000, stream.Length);
        }

        hidden.Delete(file, payload);
        Assert.DoesNotContain(hidden.List(file), i => i.Name == payload.Name);
        // The file itself is untouched.
        Assert.Equal("contents", File.ReadAllText(file));
    }

    [Fact]
    public void Find_finds_files_carrying_attributes_besides_their_download_mark()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            Assert.Skip("Windows' streams are searched in WindowsHiddenDataTests.");
            return;
        }
        using var dir = new TempDir();
        string payloadName = OperatingSystem.IsMacOS() ? "org.filecat.payload" : "user.filecat.payload";
        string markName = OperatingSystem.IsMacOS() ? "com.apple.quarantine" : "user.xdg.origin.url";
        string carrier = dir.File("carrier.bin"), downloaded = dir.File("downloaded.bin");
        dir.File("plain.bin");
        if (SetAttribute(carrier, payloadName, "payload"u8.ToArray()) != 0)
        {
            Assert.Skip("This file system keeps no extended attributes here.");
            return;
        }
        SetAttribute(downloaded, markName, Encoding.UTF8.GetBytes(OperatingSystem.IsMacOS() ? "0083;66f1c2a3;Safari;" : "https://example.com/d.bin"));
        var set = new FileCat.Core.Search.ResultSet("t", "t", "t");
        new FileCat.Core.Search.SearchSession(new FileCat.Core.Search.SearchQuery { Roots = [dir.Path], CarriesHiddenData = new UnixHiddenData() }, set)
            .Run(TestContext.Current.CancellationToken);
        Assert.Equal(["carrier.bin"], set.Snapshot().Select(s => s.Item.Name));
    }

    private static int SetAttribute(string path, string name, byte[] value) =>
        OperatingSystem.IsMacOS() ? MacSet(path, name, value, value.Length, 0, 1) : LinuxSet(path, name, value, value.Length, 0);

    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(string path, string name, byte[] value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "lsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSet(string path, string name, byte[] value, nint size, int flags);
}
