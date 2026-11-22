using FileCat.Core.Permissions;
using FileCat.Core.State;
using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

// G-FTP, a live server whose LIST runs an ACL-aware ls: FreeBSD's ftpd (the freebsd-ftpd port, whose LIST runs /bin/ls -lA)
// serving a ZFS dataset, where ls marks entries that carry NFSv4 ACL entries with '+'. FILECAT_TEST_FTP_LIVE_LIST is
// "port|user|folder" on loopback, with the fixture's password; the folder holds acl-file.txt (0644) and acl-folder (0755),
// each with a named-user ACL entry, beside plain-file.txt and plain-folder with the same modes and no entry.
public sealed class FtpLiveListingTests : IDisposable
{
    private readonly string _state = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-ftp-live", Guid.NewGuid().ToString("N")[..8])).FullName;

    [Fact]
    public void A_live_LIST_access_rule_marker_leaves_access_incomplete_beside_the_mode()
    {
        string[] setting = Environment.GetEnvironmentVariable("FILECAT_TEST_FTP_LIVE_LIST")?.Split('|') ?? [];
        if (setting.Length != 3 || !int.TryParse(setting[0], out int port))
            throw Xunit.Sdk.SkipException.ForSkip("Requires a live FTP server whose LIST marks access rules (FreeBSD ftpd on ZFS).");
        string user = setting[1], folder = setting[2];
        var profile = new RemoteProfile
        {
            Name = "live", Host = "127.0.0.1", Port = port, User = user, Protocol = RemoteProtocols.Ftp, Temporary = true,
        };
        var interaction = new FtpInteraction { AllowPlain = true };
        using var connections = new SftpConnections(id => id == profile.Id ? profile : null,
            new ProtocolConnector(new SshNetConnector(), new FluentFtpConnector()), new HostKeyTrust(Path.Combine(_state, "known_hosts")),
            new SessionSecretStore(), interaction, new CertificateTrust(Path.Combine(_state, "trusted_certificates")));
        var provider = new SftpProvider(connections, () => [profile], value => value);
        var location = SftpProvider.At(profile, folder);
        var authority = new PermissionAuthority(PermissionAuthorityKind.RemoteSession, profile.Id, 0);
        (string Name, PermissionObjectKind Kind, bool Marked, ushort Mode)[] expected =
        [
            ("acl-file.txt", PermissionObjectKind.File, true, 0x1a4),
            ("acl-folder", PermissionObjectKind.Directory, true, 0x1ed),
            ("plain-file.txt", PermissionObjectKind.File, false, 0x1a4),
            ("plain-folder", PermissionObjectKind.Directory, false, 0x1ed),
        ];
        var subjects = expected.Select(item => new PermissionSubject(location, item.Name, item.Name, authority, item.Kind,
            PermissionLinkPolicy.NoFollow)).ToArray();
        var result = provider.InspectFtpPermissions(location, subjects, TestContext.Current.CancellationToken);
        for (int i = 0; i < expected.Length; i++)
        {
            var (name, _, marked, mode) = expected[i];
            var snapshot = result[i];
            TestContext.Current.SendDiagnosticMessage(
                $"{name}: mode {snapshot.Mode.Status} {snapshot.Mode.Value:x} from {snapshot.Mode.Source}; access {snapshot.Access.Status}: {snapshot.Access.Reason}");
            Assert.True(snapshot.Mode.Value.HasValue, snapshot.BindingProblem ?? snapshot.Mode.Reason);
            Assert.Equal("Unix LIST", snapshot.Mode.Source);
            Assert.Equal(mode, snapshot.Mode.Value);
            if (marked)
            {
                Assert.Equal(PermissionReadStatus.Incomplete, snapshot.Access.Status);
                Assert.Equal("The server listing marks access rules beyond the mode that FTP does not describe.", snapshot.Access.Reason);
            }
            else Assert.NotEqual(PermissionReadStatus.Incomplete, snapshot.Access.Status);
            Assert.DoesNotContain(snapshot.Capabilities, c => c.Action == PermissionAction.Change && c.Availability == PermissionAvailability.Available);
        }
        Assert.Equal(1, interaction.PlainQuestions);
    }

    public void Dispose()
    {
        try { Directory.Delete(_state, true); } catch (IOException) { }
    }
}
