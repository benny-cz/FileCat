using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.HiddenData;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>D-55: what streams and attributes say, and the real ones of each system.</summary>
public sealed partial class HiddenDataTests
{
    private static string Summary(string name, HiddenKind kind, byte[] value) =>
        HiddenDataDecoder.Describe(new HiddenItem(name, kind, value.Length), value).Summary;

    [Fact]
    public void Linux_inventory_uses_the_qualified_kernel_route_and_preserves_permission_outcomes()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires Linux x64 xattr syscalls.");
        string? route = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_XATTR_INVENTORY");
        if (route is not ("modern" or "legacy" or "refused")) Assert.Skip("Requires a prepared kernel xattr inventory route.");
        using var directory = new TempDir();
        string file = directory.File("attribute.bin");
        const string name = "user.filecat.fixture";
        byte[] value = "private attribute value"u8.ToArray();
        Assert.Equal(0, SetAttribute(file, name, value));
        var adapter = new UnixHiddenData();
        var ct = TestContext.Current.CancellationToken;
        if (route == "refused")
        {
            var error = Assert.Throws<IOException>(() => adapter.ReadInventory(file, new(), ct));
            Assert.Equal(38, error.HResult); // The actual lookup was refused, while the syscall capability probe succeeded.
            return;
        }

        var readable = adapter.ReadInventory(file, new(), ct);
        Assert.True(readable.BoundToObject);
        Assert.Equal(FileSystem.UnixFiles.Stat(file)?.Identity, readable.Identity);
        Assert.StartsWith(route == "modern" ? "listxattrat / getxattrat" : "flistxattr / fgetxattr", readable.Source);
        Assert.True(readable.IsComplete);
        Assert.False(readable.ValueBearingQueryIssued);
        Assert.Equal(value.Length, Assert.Single(readable.Items, item => item.Name == name).Size);

        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(file, UnixFileMode.None);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => { using var ignored = File.OpenRead(file); });
            if (route == "legacy")
            {
                Assert.Throws<UnauthorizedAccessException>(() => adapter.ReadInventory(file, new(), ct));
                return;
            }

            var unreadable = adapter.ReadInventory(file, new(), ct);
            Assert.True(unreadable.BoundToObject);
            Assert.Equal(readable.Identity, unreadable.Identity);
            Assert.Equal(HiddenInventoryState.Partial, Assert.Single(unreadable.Categories).State);
            Assert.StartsWith("listxattrat / getxattrat", unreadable.Source);
            Assert.Equal(-1, Assert.Single(unreadable.Items, item => item.Name == name).Size);
            Assert.False(unreadable.ValueBearingQueryIssued);
        }
        finally { if (OperatingSystem.IsLinux()) File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    [Fact]
    public void Reviewed_linux_attribute_actions_refuse_a_replacement_inode()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires the Linux descriptor-bound xattr adapter.");
        using var root = new TempDir();
        string file = root.File("value.bin");
        const string name = "user.filecat.report";
        if (SetAttribute(file, name, "original attribute value"u8.ToArray()) != 0)
            Assert.Skip("This filesystem cannot create the test xattr.");
        var adapter = new UnixHiddenData();
        var provider = new HiddenDataProvider(adapter);
        var ct = TestContext.Current.CancellationToken;
        var original = adapter.ReadInventory(file, new(), ct).Identity!.Value;
        var item = new ItemRef(HiddenDataProvider.Of(file), name, EntryKind.File)
            { Ordinal = HiddenIdentities.Ordinal(HiddenKind.Attribute), SupplementarySourceIdentity = original };
        var report = provider.ReadValueReport(item, original, ct);
        Assert.True(report.CanPublish);
        Assert.Equal(original, Assert.Single(report.Origin.Contributors).Identity);
        Assert.Contains("original attribute value", report.Text);

        string previous = Path.Combine(root.Path, "previous.bin");
        File.Move(file, previous);
        File.WriteAllText(file, "replacement main bytes");
        Assert.Equal(0, SetAttribute(file, name, "replacement attribute"u8.ToArray()));
        Assert.Contains("different file", Assert.Throws<IOException>(() => provider.ReadValueReport(item, original, ct)).Message);
        Assert.Contains("different file", Assert.Throws<IOException>(() => provider.OpenContent(item)).Message);
        Assert.Contains("different file", Assert.Throws<IOException>(() =>
            ((IUnprotectedContentOutput)provider).OpenUnprotectedOutput(item, ContentOutputKind.Extraction)).Message);
        Assert.Contains("original attribute value", report.Text);
        Assert.DoesNotContain("replacement attribute", report.Text);

        Assert.Contains("different file", Assert.Throws<IOException>(() => provider.DeleteReviewedValue(item, original, ct)).Message);
        var replacement = adapter.ReadInventory(file, new(), ct);
        Assert.Contains(replacement.Items, attr => attr.Name == name);
        provider.DeleteReviewedValue(item, replacement.Identity!.Value, ct);
        Assert.DoesNotContain(adapter.ReadInventory(file, new(), ct).Items, attr => attr.Name == name);
        Assert.Contains(adapter.ReadInventory(previous, new(), ct).Items, attr => attr.Name == name);
        Assert.Equal("replacement main bytes", File.ReadAllText(file));
    }

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

        // Finder stores its color index after a newline; the name shown to a person excludes that suffix.
        byte[] tagList = BinaryPlistOfStrings("Evidence\n4", "Review\n0");
        var (tagSummary, tagDetails) = HiddenDataDecoder.Describe(
            new HiddenItem("com.apple.metadata:_kMDItemUserTags", HiddenKind.Attribute, tagList.Length), tagList);
        Assert.Equal("Finder tags: Evidence, Review", tagSummary);
        Assert.Equal(["Evidence", "Review"], tagDetails);
        Assert.DoesNotContain('\n', tagSummary);

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
        Assert.Equal("Header declares LZFSE, in the resource fork, 1 MiB uncompressed", Summary("com.apple.decmpfs", HiddenKind.Attribute, decmpfs));

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
        if (OperatingSystem.IsMacOS())
        {
            // The legacy path inventory is observational. The new route refuses values/actions until a safe
            // held-object adapter is qualified, rather than reopening a mutable path or ..namedfork traversal.
            var inventory = hidden.ReadInventory(file, new(), TestContext.Current.CancellationToken);
            Assert.False(inventory.BoundToObject); Assert.Contains("Compression backing: not inventoried", inventory.Notice);
            Assert.Equal(UnixHiddenData.MacValueRefusal, inventory.ValueRefusal);
            var ordinary = Assert.Single(items, i => i.Name == "org.filecat.payload");
            Assert.Equal(UnixHiddenData.MacValueRefusal, Assert.Throws<NotSupportedException>(() => hidden.Read(file, ordinary, 64)).Message);
            Assert.Throws<NotSupportedException>(() => hidden.Open(file, ordinary));
            Assert.Throws<NotSupportedException>(() => hidden.Delete(file, ordinary));
            Assert.Equal("contents", File.ReadAllText(file));
            return;
        }
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

    /// <summary>
    /// I651 and I615 on macOS: a Finder tag is read and named through a held folder, while a file's tag is listed with its
    /// size and its value refused (47f7b0630). The refusal is said on the file's row and by every value action, instead
    /// of "Not read — F3 to view" followed by silence.
    /// </summary>
    [Fact]
    public async Task Mac_Finder_tag_is_named_for_a_folder_and_a_files_value_refusal_is_explained()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires Darwin extended attributes.");
        const string tags = "com.apple.metadata:_kMDItemUserTags";
        using var dir = new TempDir();
        string folder = Directory.CreateDirectory(Path.Combine(dir.Path, "tagged folder")).FullName;
        string file = dir.File("case notes.txt", "case notes with one Finder tag\n");
        byte[] blue = BinaryPlistOfStrings("Blue\n4");
        Assert.Equal(51, blue.Length);
        Assert.Equal(0, SetAttribute(folder, tags, blue));
        Assert.Equal(0, SetAttribute(file, tags, blue));
        var ct = TestContext.Current.CancellationToken;
        var hidden = new UnixHiddenData();

        var held = hidden.ReadInventory(folder, new(), ct);
        Assert.True(held.CanAct);
        Assert.Null(held.ValueRefusal);
        var folderTag = Assert.Single(held.Items, item => item.Name == tags);
        Assert.Equal("Finder tag: Blue", HiddenDataDecoder.Describe(folderTag, hidden.Read(folder, folderTag, 65536)).Summary);

        var listed = hidden.ReadInventory(file, new(), ct);
        Assert.False(listed.CanAct);
        Assert.Equal(UnixHiddenData.MacValueRefusal, listed.ValueRefusal);
        Assert.Equal(UnixHiddenData.MacValueRefusal, listed.ActionRefusalText);
        Assert.Equal(UnixHiddenData.MacValueRefusalSummary, listed.ValueRefusalSummary);
        Assert.Contains(UnixHiddenData.MacValueRefusal, listed.Notice);
        var fileTag = Assert.Single(listed.Items, item => item.Name == tags);
        Assert.Equal(blue.Length, fileTag.Size);
        Assert.Equal(UnixHiddenData.MacValueRefusal, Assert.Throws<NotSupportedException>(() => hidden.Read(file, fileTag, 64)).Message);

        var provider = new HiddenDataProvider(hidden);
        var sink = new RowSink();
        await provider.EnumerateAsync(HiddenDataProvider.Of(file), sink, ct);
        Assert.Same(listed.ValueRefusal, sink.Inventory?.ValueRefusal);
        var row = Assert.Single(sink.Rows);
        Assert.True((row.Flags & EntryFlags.Unavailable) != 0);
        Assert.Equal(UnixHiddenData.MacValueRefusalSummary, Assert.IsType<HiddenEntryTag>(row.Tag).Summary);
        Assert.Throws<NotSupportedException>(() => provider.OpenContent(provider.GetItemRef(HiddenDataProvider.Of(file), row)));
    }

    private sealed class RowSink : IEnumerationSink
    {
        public List<EntryData> Rows { get; } = [];
        public HiddenInventory? Inventory { get; private set; }
        public void ReportSupplementaryInventory(HiddenInventory inventory) => Inventory = inventory;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Rows.AddRange(entries.ToArray());
        public void ReportIssue(string message) { }
    }

    [Fact]
    public void A_Darwin_resource_fork_without_ordinary_attributes_has_its_own_inventory_category()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Requires a Darwin resource fork.");
        using var root = new TempDir();
        string path = root.File("resource-only.bin");
        byte[] resource = "standalone resource fork"u8.ToArray();
        File.WriteAllBytes(Path.Join(path, "..namedfork", "rsrc"), resource);

        int previousPolicy = MacMaterializationPolicy.Get();
        var inventory = new UnixHiddenData().ReadInventory(path, new(ShowCompressionBacking: false), TestContext.Current.CancellationToken);
        Assert.Equal(previousPolicy, MacMaterializationPolicy.Get());
        var fork = Assert.Single(inventory.Items);
        Assert.Equal(HiddenKind.ResourceFork, fork.Kind);
        Assert.Equal("com.apple.ResourceFork", fork.Name);
        Assert.Equal(resource.Length, fork.Size);
        Assert.Equal(HiddenMetadataRole.Unqualified, fork.Role);
        Assert.NotNull(HiddenMetadataRoles.DeleteRefusal(fork));
        Assert.Equal(0, Assert.Single(inventory.Categories, category => category.Kind == HiddenKind.Attribute).Count);
        Assert.Equal(1, Assert.Single(inventory.Categories, category => category.Kind == HiddenKind.ResourceFork).Count);
        Assert.Equal("1+", inventory.CountText);
        Assert.False(inventory.BoundToObject);
        Assert.False(inventory.ValueBearingQueryIssued);
        var results = new FileCat.Core.Search.ResultSet("fork", "fork", "test");
        new FileCat.Core.Search.SearchSession(new FileCat.Core.Search.SearchQuery { Roots = [root.Path], CarriesHiddenData = new UnixHiddenData() }, results)
            .Run(TestContext.Current.CancellationToken);
        Assert.Contains(results.Snapshot(), result => result.Item.Name == "resource-only.bin");
    }

    [Fact]
    public void A_real_Darwin_compressed_file_discloses_that_the_ordinary_view_omits_backing()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Prepared Darwin compression fixture.");
        string? path = Environment.GetEnvironmentVariable("FILECAT_MAC_COMPRESSED_XATTR_TARGET");
        if (string.IsNullOrWhiteSpace(path)) Assert.Skip("Set FILECAT_MAC_COMPRESSED_XATTR_TARGET to an owned compressed file without ordinary attributes.");
        const string compression = "com.apple.decmpfs", fork = "com.apple.ResourceFork";
        const int noFollow = 1, showCompression = 0x20;
        Assert.Equal(-1, MacAttributeLength(path, compression, 0, 0, 0, noFollow));
        Assert.Equal(-1, MacAttributeLength(path, fork, 0, 0, 0, noFollow));
        Assert.True(MacAttributeLength(path, compression, 0, 0, 0, noFollow | showCompression) >= 16);
        Assert.True(MacAttributeLength(path, fork, 0, 0, 0, noFollow | showCompression) > 0);

        var ct = TestContext.Current.CancellationToken;
        var adapter = new UnixHiddenData();
        var fallback = adapter.ReadInventory(path, new(ShowCompressionBacking: true), ct);
        Assert.Empty(fallback.Items);
        Assert.False(fallback.BoundToObject);
        Assert.False(fallback.ValueBearingQueryIssued);
        Assert.Null(fallback.ExactCount);
        Assert.All(fallback.Categories, category => Assert.Equal(HiddenInventoryState.Partial, category.State));
        Assert.Contains("0 ordinary attributes and 0 resource forks", fallback.Notice);
        Assert.Contains("Compression backing: not inventoried.", fallback.Notice);

        var ordinary = adapter.ReadInventory(path, new(ShowCompressionBacking: false), ct);
        Assert.Empty(ordinary.Items);
        Assert.Contains("Compression backing: not requested.", ordinary.Notice);
    }

    private static int SetAttribute(string path, string name, byte[] value) =>
        OperatingSystem.IsMacOS() ? MacSet(path, name, value, value.Length, 0, 1) : LinuxSet(path, name, value, value.Length, 0);

    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSet(string path, string name, byte[] value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "getxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial nint MacAttributeLength(string path, string name, nint value, nint size, uint position, int options);

    [LibraryImport("libc", EntryPoint = "lsetxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSet(string path, string name, byte[] value, nint size, int flags);
}
