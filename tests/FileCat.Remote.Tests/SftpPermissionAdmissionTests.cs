using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.Permissions;
using FileCat.Core.State;
using FileCat.Remote.Sftp;
using Renci.SshNet.Sftp;

namespace FileCat.Remote.Tests;

public sealed partial class SftpPermissionAdmissionTests
{
    private const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void The_banner_and_complete_versioned_subsystem_are_both_required()
    {
        var stock = SftpServerQualification.PortableOpenSsh96;
        Assert.Null(SftpServerQualification.Refusal("SSH-2.0-OpenSSH_9.6p1 Ubuntu", stock));
        Assert.Contains("Windows OpenSSH", SftpServerQualification.Refusal("SSH-2.0-OpenSSH_for_Windows_9.6", stock));
        Assert.Contains("awaiting", SftpServerQualification.Refusal("SSH-2.0-OpenSSH_10.5p1", stock));
        Assert.Contains("no identified", SftpServerQualification.Refusal("SSH-2.0-mod_sftp", stock));
        var altered = stock.ToDictionary();
        altered["fstatvfs@openssh.com"] = "1";
        Assert.Contains("subsystem differs", SftpServerQualification.Refusal("SSH-2.0-OpenSSH_9.6p1", altered));
        altered.Remove("lsetstat@openssh.com");
        Assert.Contains("lacks the version 1", SftpServerQualification.Refusal("SSH-2.0-OpenSSH_9.6p1", altered));
    }

    [Fact]
    public void Capture_uses_root_first_exact_paths_and_accepts_complete_mode_zero_with_unsigned_ids()
    {
        const uint user = 0xf1234567;
        var calls = new List<string>();
        var observed = SftpPermissionPathReview.CaptureEvidence("/area/home/mode zero", "/area/home", "reviewer", PermissionObjectKind.File,
            (path, _) =>
            {
                calls.Add(path);
                return path switch
                {
                    "/" or "/area" => Stat(0x41ed, 0),
                    "/area/home" => Stat(0x41c0, user),
                    "/area/home/mode zero" => Stat(0x8000, user),
                    _ => throw new InvalidOperationException()
                };
            }, (uid, gid, _) =>
            {
                Assert.Equal(user, uid);
                Assert.Equal(user, gid);
                return new SftpAccountNames("reviewer", "testers");
            }, Cancellation);
        Assert.Equal(new[] { "/", "/area", "/area/home", "/area/home/mode zero" }, calls);
        Assert.Equal(0x8000u, observed.Leaf.Permissions);
        Assert.Equal(user, observed.StartAttributes.UserId);
        Assert.Equal(3, observed.Ancestors.Length);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("missing tuple")]
    [InlineData("root leaf")]
    [InlineData("other owner")]
    [InlineData("wrong home")]
    [InlineData("wrong account")]
    public void Capture_refuses_missing_or_inconsistent_authority_facts(string refusal)
    {
        Assert.Throws<RefusedOperationException>(() => SftpPermissionPathReview.CaptureEvidence("/area/item", "/home", refusal == "root" ? "root" : "owner",
            PermissionObjectKind.File, (path, _) => path switch
            {
                "/" => Stat(0x41ed, 0),
                "/area" => Stat(0x41c0, refusal == "other owner" ? 2000u : 1000u),
                "/home" => Stat(0x41c0, refusal == "wrong home" ? 0u : 1000u),
                "/area/item" when refusal == "missing tuple" => Stat(0x8180, 1000) with { Flags = 0x7, ModifiedUtc = null },
                "/area/item" => Stat(0x8180, refusal == "root leaf" ? 0u : 1000u),
                _ => throw new InvalidOperationException()
            }, (_, _, _) => new SftpAccountNames(refusal == "wrong account" ? "someone-else" : "owner", null), Cancellation));
    }

    [Theory]
    [InlineData(0x41fd)]
    [InlineData(0x43ff)]
    [InlineData(0xa1ff)]
    public void A_shared_or_symbolic_ancestor_refuses_before_the_leaf_read(uint mode)
    {
        var reads = new List<string>();
        Assert.Throws<RefusedOperationException>(() => SftpPermissionPathReview.CaptureEvidence("/area/item", "/home", "owner",
            PermissionObjectKind.File, (path, _) =>
            {
                reads.Add(path);
                return path == "/" ? Stat(0x41ed, 0) : Stat(mode, 1000);
            }, (_, _, _) => throw new InvalidOperationException(), Cancellation));
        Assert.Equal(new[] { "/", "/area" }, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpenSsh_admits_owned_mode_zero_file_or_folder_on_the_captured_lease(bool directory)
    {
        using var fixture = new Fixture();
        string path = Path.Combine(fixture.Server.Files, "owned item");
        if (directory) Directory.CreateDirectory(path, OwnerDirectory);
        else File.WriteAllText(path, "witness");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            using var review = fixture.Review(fixture.Server.Files, "owned item", directory ? PermissionObjectKind.Directory : PermissionObjectKind.File);
            Assert.Equal(path, review.Evidence.Path);
            Assert.Equal(fixture.Server.Files, review.Evidence.StartDirectory);
            Assert.Equal((uint)(directory ? 0x4000 : 0x8000), review.Evidence.Leaf.Permissions);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(fixture.Server.HostKey)), review.Authority.HostKeySha256);
            Assert.Equal(fixture.Profile.User, review.Authority.User);
            review.Revalidate(Cancellation);
            Assert.Equal(UnixFileMode.None, File.GetUnixFileMode(path));
        }
        finally { if (directory) File.SetUnixFileMode(path, OwnerDirectory); }
    }

    [Theory]
    [InlineData("group-writable")]
    [InlineData("sticky")]
    [InlineData("symlink-ancestor")]
    [InlineData("symlink-leaf")]
    [InlineData("fifo")]
    public void OpenSsh_refuses_shared_ancestry_and_special_items(string refusal)
    {
        using var fixture = new Fixture();
        string parent = Directory.CreateDirectory(Path.Combine(fixture.Server.Files, "owned folder"), OwnerDirectory).FullName;
        string leaf = Path.Combine(parent, "witness");
        File.WriteAllText(leaf, "witness");
        string name = "witness";
        switch (refusal)
        {
            case "group-writable":
                File.SetUnixFileMode(parent, (UnixFileMode)0x1fd);
                break;
            case "sticky":
                File.SetUnixFileMode(parent, (UnixFileMode)0x3ff);
                break;
            case "symlink-ancestor":
                parent = Path.Combine(fixture.Server.Files, "link to folder");
                Directory.CreateSymbolicLink(parent, Path.GetDirectoryName(leaf)!);
                break;
            case "symlink-leaf":
                name = "link";
                File.CreateSymbolicLink(Path.Combine(parent, name), leaf);
                break;
            case "fifo":
                name = "pipe";
                var start = new ProcessStartInfo("/usr/bin/mkfifo") { UseShellExecute = false };
                start.ArgumentList.Add(Path.Combine(parent, name));
                using (var process = Process.Start(start)!)
                {
                    Assert.True(process.WaitForExit(5_000));
                    Assert.Equal(0, process.ExitCode);
                }
                break;
        }
        Assert.Throws<RefusedOperationException>(() => fixture.Review(parent, name));
        Assert.Equal("witness", File.ReadAllText(leaf));
    }

    [Theory]
    [InlineData("leaf")]
    [InlineData("ancestor")]
    [InlineData("replaced directory")]
    [InlineData("session")]
    [InlineData("profile")]
    public void Reviewed_OpenSsh_path_refuses_observable_changes(string changed)
    {
        using var fixture = new Fixture();
        string parent = Directory.CreateDirectory(Path.Combine(fixture.Server.Files, "parent"), OwnerDirectory).FullName;
        string leaf = Path.Combine(parent, "witness");
        File.WriteAllText(leaf, "witness");
        File.SetUnixFileMode(leaf, UnixFileMode.UserRead);
        using var review = fixture.Review(parent, "witness");
        switch (changed)
        {
            case "leaf":
                File.SetUnixFileMode(leaf, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                break;
            case "ancestor":
                File.SetUnixFileMode(parent, OwnerDirectory | UnixFileMode.GroupWrite);
                break;
            case "replaced directory":
                DateTime leafTime = File.GetLastWriteTimeUtc(leaf);
                DateTime parentTime = Directory.GetLastWriteTimeUtc(parent);
                Directory.Move(parent, Path.Combine(fixture.Server.Files, "original directory"));
                Directory.CreateDirectory(parent, OwnerDirectory);
                File.WriteAllText(leaf, "witness");
                File.SetUnixFileMode(leaf, UnixFileMode.UserRead);
                File.SetLastWriteTimeUtc(leaf, leafTime);
                Directory.SetLastWriteTimeUtc(parent, parentTime.AddMinutes(-20));
                break;
            case "session":
                fixture.Connections.Disconnect(fixture.Profile.Id);
                break;
            case "profile":
                fixture.Profile.InitialPath = "/other";
                break;
        }
        Assert.Throws<SftpPermissionPathConflictException>(() => review.Revalidate(Cancellation));
    }

    [Fact]
    public void Refused_stale_listings_release_their_leases()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Server.Files, "witness"), "witness");
        var subject = fixture.Subject(fixture.Server.Files, "witness") with { ListedSize = 1 };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        for (int attempt = 0; attempt < 6; attempt++)
            Assert.Throws<SftpPermissionPathConflictException>(() => fixture.Provider.PrepareSftpPermissionPathReview(subject, deadline.Token));
        using var current = fixture.Review(fixture.Server.Files, "witness");
        Assert.Equal(7ul, current.Evidence.Leaf.Size);
    }

    [Fact]
    public void OpenSsh_without_lsetstat_remains_inspectable_but_refuses_review()
    {
        using var fixture = new Fixture(disableLSetStat: true);
        File.WriteAllText(Path.Combine(fixture.Server.Files, "witness"), "witness");
        var subject = fixture.Subject(fixture.Server.Files, "witness");
        var result = Assert.Single(fixture.Provider.InspectSftpPermissions(subject.OriginalLocation, [subject], Cancellation));
        Assert.Equal(PermissionReadStatus.Present, result.Mode.Status);
        Assert.Contains("version 1 no-follow", result.Sftp!.ChangeRefusal);
        Assert.Throws<RefusedOperationException>(() => fixture.Review(fixture.Server.Files, "witness"));
    }

    [Fact]
    public void Windows_OpenSSH_provides_live_inspection_but_refuses_a_permission_review()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_SFTP_ROOT");
        string? port = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_SFTP_PORT");
        if (!OperatingSystem.IsWindows() || root is null || port is null)
            Assert.Skip("Requires a task-owned Windows OpenSSH server listening on loopback.");

        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.True(int.TryParse(port, out int portNumber));
        Assert.InRange(portNumber, 1, ushort.MaxValue);
        string path = Path.Combine(root, "witness.txt");
        byte[] before = File.ReadAllBytes(path);
        byte[] hostKey = Convert.FromBase64String(File.ReadAllText(Path.Combine(root, "host.pub")).Split(' ')[1]);
        var profile = new RemoteProfile
        {
            Name = "Windows OpenSSH fixture", Host = "127.0.0.1", Port = portNumber,
            User = Environment.UserName, Auth = RemoteAuth.Key, KeyFile = Path.Combine(root, "client")
        };
        var trust = new HostKeyTrust(Path.Combine(root, "known_hosts"), []);
        trust.Remember(profile.Host, profile.Port, hostKey);
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null, new SshNetConnector(),
            trust, new SessionSecretStore(), new FtpInteraction());
        var provider = new SftpProvider(connections, () => [profile], profile => profile);
        string parent = "/" + Path.TrimEndingDirectorySeparator(root).Replace('\\', '/');
        var location = SftpProvider.At(profile, parent);
        var subject = new PermissionSubject(location, "witness.txt", parent + "/witness.txt",
            new(PermissionAuthorityKind.RemoteSession, profile.Id, 0), PermissionObjectKind.File, PermissionLinkPolicy.NoFollow);

        var snapshot = Assert.Single(provider.InspectSftpPermissions(location, [subject], Cancellation));
        Assert.Equal(PermissionReadStatus.Present, snapshot.Mode.Status);
        Assert.Contains("Windows OpenSSH", snapshot.Sftp?.ChangeRefusal);
        var refusal = Assert.Throws<RefusedOperationException>(() => provider.PrepareSftpPermissionPathReview(subject, Cancellation));
        Assert.Contains("Windows OpenSSH", refusal.Message);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    private static RemoteSecurityAttributes Stat(uint mode, uint owner) =>
        new(RemoteSecurityObservation.ExactLStat, 0xf, 16, owner, owner, mode, DateTime.UnixEpoch);

    private sealed class Fixture : IDisposable
    {
        internal Fixture(bool disableLSetStat = false, bool dropGroupReply = false)
        {
            var root = Environment.GetEnvironmentVariable("FILECAT_TEST_SFTP_ADMISSION_ROOT");
            if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess ||
                root is null || !root.StartsWith("/run/user/", StringComparison.Ordinal) || !Directory.Exists(root))
                Assert.Skip("Requires the private ordinary-user OpenSSH admission fixture under /run/user.");
            var server = TestSshd.TryStart(disableLSetStat: disableLSetStat, privateReviewRoot: root, dropGroupReply: dropGroupReply);
            if (server is null) Assert.Skip("Requires the owned OpenSSH server.");
            Server = server;
            Profile = server.Profile;
            var trust = new HostKeyTrust(Path.Combine(Server.Directory, "known_hosts"), []);
            trust.Remember(Profile.Host, Profile.Port, Server.HostKey);
            Connections = new SftpConnections(id => id == Profile.Id ? Profile : null, Connector,
                trust, new SessionSecretStore(), new FtpInteraction());
            Provider = new(Connections, () => [Profile], profile => profile);
        }

        internal TestSshd Server { get; }
        internal RemoteProfile Profile { get; }
        internal SftpConnections Connections { get; }
        internal SftpProvider Provider { get; }
        internal TrackingConnector Connector { get; } = new();
        internal PermissionSubject Subject(string parent, string name, PermissionObjectKind kind = PermissionObjectKind.File) =>
            new(SftpProvider.At(Profile, parent), name, Path.Combine(parent, name),
                new(PermissionAuthorityKind.RemoteSession, Profile.Id, 0), kind, PermissionLinkPolicy.NoFollow);
        internal SftpPermissionPathReview Review(string parent, string name, PermissionObjectKind kind = PermissionObjectKind.File) =>
            Provider.PrepareSftpPermissionPathReview(Subject(parent, name, kind), Cancellation);
        public void Dispose()
        {
            Connections.Dispose();
            Server.Dispose();
        }
    }

    private sealed class TrackingConnector : ISftpConnector
    {
        private readonly SshNetConnector _inner = new();
        internal ISftpChannel? Last { get; private set; }
        internal int Connections { get; private set; }

        public ISftpChannel Connect(RemoteProfile profile, ConnectContext context, CancellationToken cancellation)
        {
            var channel = _inner.Connect(profile, context, cancellation);
            Connections++;
            return Last = channel;
        }
    }
}
