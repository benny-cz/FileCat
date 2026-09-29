using FileCat.Core.Network;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

/// <summary>D-54 on Linux and macOS: the system's SMB tools read right, and a real Samba server where CI runs one.</summary>
public sealed class UnixNetworkTests
{
    private sealed class Sink : IEnumerationSink
    {
        public readonly List<EntryData> Entries = [];
        public readonly List<string> Issues = [];
        public void AddBatch(ReadOnlySpan<EntryData> entries) { lock (Entries) Entries.AddRange(entries.ToArray()); }
        public void ReportIssue(string message) => Issues.Add(message);
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
        var other = Assert.IsType<IOException>(SmbTools.Failure("nas", "line one\nprotocol negotiation failed: NT_STATUS_CONNECTION_RESET"));
        Assert.Contains("NT_STATUS_CONNECTION_RESET", other.Message);
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
