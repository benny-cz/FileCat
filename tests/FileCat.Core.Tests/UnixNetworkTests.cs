using FileCat.Core.Network;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>D-54 on Linux and macOS: the system's SMB tools read right, and a real Samba server where CI runs one.</summary>
public sealed class UnixNetworkTests(ITestOutputHelper output)
{
    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Entries = [];
        public readonly List<string> Issues = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) { lock (Entries) Entries.AddRange(entries.ToArray()); }
        public void ReportIssue(string message) => Issues.Add(message);
    }

    [Fact]
    public void Automatic_network_helpers_are_selected_only_from_OS_tool_directories()
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix SMB helper selection."); return; }
        string[] directories = ["/usr/bin", "/bin", "/usr/sbin", "/sbin"];
        foreach (string name in new[] { "gio", "smbclient", "smbutil", "mount" })
        {
            string? selected = SmbTools.FindSystemTool(name);
            if (selected is not null)
                Assert.Contains(Path.GetDirectoryName(selected), directories);
        }
        Assert.Null(SmbTools.FindSystemTool("../smbclient"));
    }

    [Fact]
    public void Each_tools_share_list_is_read_as_the_tool_writes_it()
    {
        // smbclient -L //server -g
        var smbclient = SmbTools.ParseSmbclient("""
            Disk|public|Public files
            Disk|Private Stuff|
            IPC|IPC$|IPC Service (Samba 4.19.5-Ubuntu)
            Printer|laser|HP LaserJet
            Disk|backup$|
            Workgroup|WORKGROUP|NAS
            """);
        Assert.Equal(["public", "Private Stuff", "backup$"], smbclient.Select(s => s.Name));
        Assert.Equal("Public files", smbclient[0].Comment);
        Assert.True(smbclient[2].IsHidden);

        // gio list smb://server/
        Assert.Equal(["public", "Private Stuff"], SmbTools.ParseGioList("public\nPrivate Stuff\nIPC$\n\n").Select(s => s.Name));

        // smbutil view //server (macOS)
        var smbutil = SmbTools.ParseSmbutil("""
            Share                                           Type    Comments
            -------------------------------
            public                                          Disk    Public files
            Private Stuff                                   Disk
            IPC$                                            Pipe    IPC Service (Samba 4.19.5)

            3 shares listed
            """);
        Assert.Equal(["public", "Private Stuff"], smbutil.Select(s => s.Name));
        Assert.Equal("Public files", smbutil[0].Comment);
        Assert.Null(smbutil[1].Comment);
    }

    [Fact]
    public void A_near_budget_smbclient_reply_does_not_allocate_a_field_per_separator()
    {
        string reply = "Disk|public|Public files\n" + new string('|', 999_900) + "\nDisk|backup|Backups\n";
        _ = SmbTools.ParseSmbclient("Disk|warmup|\n");
        long before = GC.GetAllocatedBytesForCurrentThread();

        var shares = SmbTools.ParseSmbclient(reply);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"SMB_SHARE_PARSE_ALLOCATED_BYTES={allocated}; INPUT_CHARACTERS={reply.Length}");
        Assert.Equal(["public", "backup"], shares.Select(share => share.Name));
        Assert.Equal("Public files", shares[0].Comment);
        Assert.Equal("Backups", shares[1].Comment);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Theory]
    [InlineData("gio")]
    [InlineData("smbclient")]
    [InlineData("smbutil")]
    public void Share_lists_refuse_more_than_4096_valid_rows(string format)
    {
        string row = format switch
        {
            "gio" => "public\n",
            "smbclient" => "Disk|public|Files\n",
            _ => "public Disk Files\n"
        };
        string allowed = string.Concat(Enumerable.Repeat(row, 4096));
        Assert.Equal(4096, ParseShareList(format, allowed).Count);
        var error = Assert.Throws<IOException>(() => ParseShareList(format, allowed + row));
        Assert.Contains("4096", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("gio", "abc\n", 250_000)]
    [InlineData("smbclient", "Disk|x|\n", 125_000)]
    [InlineData("smbutil", "x Disk\n", 140_000)]
    public void Near_budget_share_lists_stop_before_materializing_every_row(string format, string row, int count)
    {
        string reply = string.Concat(Enumerable.Repeat(row, count));
        long before = GC.GetAllocatedBytesForCurrentThread();

        Exception? error = Record.Exception(() => ParseShareList(format, reply));

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"SMB_SHARE_LIMIT_FORMAT={format}; ALLOCATED_BYTES={allocated}; INPUT_CHARACTERS={reply.Length}");
        Assert.IsType<IOException>(error);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    private static List<SmbShare> ParseShareList(string format, string reply) => format switch
    {
        "gio" => SmbTools.ParseGioList(reply),
        "smbclient" => SmbTools.ParseSmbclient(reply),
        "smbutil" => SmbTools.ParseSmbutil(reply),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    [Fact]
    public void Near_budget_smbutil_non_disk_rows_do_not_allocate_per_line()
    {
        string reply = string.Concat(Enumerable.Repeat("x Pipe\n", 140_000));
        _ = SmbTools.ParseSmbutil("warmup Pipe\n");
        long before = GC.GetAllocatedBytesForCurrentThread();

        var shares = SmbTools.ParseSmbutil(reply);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"SMBUTIL_NON_DISK_ALLOCATED_BYTES={allocated}; INPUT_CHARACTERS={reply.Length}");
        Assert.Empty(shares);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Fact]
    public void Smbutil_type_column_uses_the_first_recognized_type_after_a_name()
    {
        var shares = SmbTools.ParseSmbutil(
            "Disk room Pipe Ignore later Disk\nDisk room Disk A place\nx\tDisk\tTabbed\n leading Disk Skip\nmissing Unknown Not a share\n");
        Assert.Equal(["Disk room", "x"], shares.Select(share => share.Name));
        Assert.Equal(["A place", "Tabbed"], shares.Select(share => share.Comment));
    }

    [Fact]
    public void Mounted_shares_are_recognized_in_each_systems_form()
    {
        Assert.Equal(("nas", "public"), SmbTools.ParseUnc("//nas/public"));
        Assert.Equal(("nas.local", "Private Stuff"), SmbTools.ParseUnc("//user@nas.local/Private%20Stuff"));
        Assert.Null(SmbTools.ParseUnc("/dev/sda1"));
        Assert.Equal(("nas", "public"), SmbTools.ParseGvfsName("smb-share:server=nas,share=public,user=marek"));
        Assert.Null(SmbTools.ParseGvfsName("sftp:host=nas"));
        Assert.Equal(("nas.local", "public", "/Volumes/public"),
            SmbTools.ParseMacMount("//GUEST:@nas.local/public on /Volumes/public (smbfs, nodev, nosuid, mounted by marek)"));
        Assert.Null(SmbTools.ParseMacMount("/dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)"));
    }

    [Fact]
    public void Tool_failures_become_what_the_panel_says()
    {
        Assert.IsType<SmbSignInRequiredException>(SmbTools.Failure("nas", "session setup failed: NT_STATUS_ACCESS_DENIED"));
        Assert.IsType<SmbSignInRequiredException>(SmbTools.Failure("nas", "gio: smb://nas/: Password required for share public on nas"));
        Assert.IsType<DirectoryNotFoundException>(SmbTools.Failure("nas", "do_connect: Connection to nas failed (Error NT_STATUS_HOST_UNREACHABLE)"));
        Assert.IsType<DirectoryNotFoundException>(SmbTools.Failure("nas", "Connection to nas failed (Error 55)"));
        Assert.IsType<IOException>(SmbTools.Failure("nas", "Connection to nas\nother failed\nlast detail"));
        var other = Assert.IsType<IOException>(SmbTools.Failure("nas", "line one\nprotocol negotiation failed: NT_STATUS_CONNECTION_RESET"));
        Assert.Contains("NT_STATUS_CONNECTION_RESET", other.Message);
    }

    [Fact]
    public void Near_budget_tool_failure_does_not_materialize_every_diagnostic_line()
    {
        string reply = string.Concat(Enumerable.Repeat("junk\n", 200_000)) + "final detail";
        _ = SmbTools.Failure("nas", "warmup\nlast");
        long before = GC.GetAllocatedBytesForCurrentThread();

        Exception failure = SmbTools.Failure("nas", reply);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"SMB_FAILURE_ALLOCATED_BYTES={allocated}; INPUT_CHARACTERS={reply.Length}");
        Assert.Equal("nas: final detail", Assert.IsType<IOException>(failure).Message);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Fact]
    public void Repeated_connection_prefixes_do_not_make_failure_classification_quadratic()
    {
        string reply = string.Concat(Enumerable.Repeat("Connection to ", 6_000)) + "\nfinal detail";
        _ = SmbTools.Failure("nas", "warmup\nlast");
        var watch = System.Diagnostics.Stopwatch.StartNew();

        Exception failure = SmbTools.Failure("nas", reply);

        watch.Stop();
        output.WriteLine($"SMB_FAILURE_CLASSIFY_MILLISECONDS={watch.ElapsedMilliseconds}; INPUT_CHARACTERS={reply.Length}");
        Assert.Equal("nas: final detail", Assert.IsType<IOException>(failure).Message);
        // A shared macOS CI runner paused this 84 KB linear scan for 583 ms.
        // Keep a generous bound that still catches the old repeated-prefix
        // quadratic walk without treating scheduler contention as a regression.
        Assert.InRange(watch.ElapsedMilliseconds, 0, 3_000);
    }

    [Fact]
    public void Network_addresses_read_and_lead_up_as_the_desktops_write_them()
    {
        var provider = new UnixNetworkProvider();
        Assert.True(provider.TryParse("Network", null, out var root));
        Assert.Equal(UnixNetworkProvider.Root, root);
        Assert.True(provider.TryParse("smb://nas/", null, out var server));
        Assert.Equal("smb://nas", server!.Path);
        Assert.True(provider.TryParse("smb://nas/public/docs/", null, out var deep));
        Assert.Equal("smb://nas/public/docs", deep!.Path);
        Assert.False(provider.TryParse("smb://../evil", null, out _));
        Assert.False(provider.TryParse("/home", null, out _));

        Assert.Equal("Network", provider.GetDisplayName(UnixNetworkProvider.Root));
        Assert.Equal("nas", provider.GetDisplayName(server));
        Assert.Null(provider.GetParent(UnixNetworkProvider.Root));
        Assert.Equal(UnixNetworkProvider.Root, provider.GetParent(server));
        Assert.Equal("smb://nas", provider.GetParent(deep)!.Path);
        Assert.Equal("nas", provider.GetNameInParent(server));

        // A server row opens its shares; a share row is a location prepared first (mounted), then shown as a folder.
        var host = new EntryData("NAS", EntryKind.Server) { Tag = new UnixNetworkTag(new NetworkHost("NAS", "nas.local", null, "Bonjour"), null) };
        Assert.Equal("smb://nas.local", provider.GetChildLocation(UnixNetworkProvider.Root, host)!.Path);
        var share = new EntryData("Private Stuff", EntryKind.Share) { Tag = new UnixNetworkTag(null, new SmbShare("Private Stuff", null)) };
        var shareLocation = provider.GetChildLocation(server, share)!;
        Assert.Equal("smb://nas/Private%20Stuff", shareLocation.Path);
        Assert.Null(provider.PrepareAsync(server, CancellationToken.None));
        Assert.Null(provider.PrepareAsync(UnixNetworkProvider.Root, CancellationToken.None));
    }

    /// <summary>
    /// A real Samba server (CI's Linux job starts one with a guest share "filecat-public" holding hello.txt): its shares
    /// are listed, and, where gvfs runs, the share opens as the folder gvfs mounts it at.
    /// </summary>
    [Fact]
    public async Task A_Samba_servers_shares_are_listed_and_a_guest_share_opens_as_a_folder()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("FILECAT_TEST_SAMBA") != "1")
        {
            Assert.Skip("Needs the Samba server CI's Linux job starts (FILECAT_TEST_SAMBA=1).");
            return;
        }
        var ct = TestContext.Current.CancellationToken;
        var provider = new UnixNetworkProvider();
        var sink = new Sink();
        await provider.EnumerateAsync(new Location(Schemes.Network, "smb://localhost"), sink, ct);
        Assert.Contains(sink.Entries, e => e.Name == "filecat-public" && e.Kind == EntryKind.Share);
        Assert.DoesNotContain(sink.Entries, e => e.Name == "IPC$");

        if (Environment.GetEnvironmentVariable("FILECAT_TEST_GVFS") != "1") return;
        var folder = await provider.PrepareAsync(new Location(Schemes.Network, "smb://localhost/filecat-public"), ct)!;
        Assert.True(folder.IsFileSystem, folder.ToString());
        Assert.Equal("hello", File.ReadAllText(Path.Combine(folder.Path, "hello.txt")).Trim());
        // Mounted now: found again without mounting, and its server is among those already reached.
        Assert.Equal(folder.Path, SmbTools.FindMount("localhost", "filecat-public"));
    }
}
