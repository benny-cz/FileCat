using FileCat.Core.Permissions;

namespace FileCat.Remote.Tests;

// G-REMOTE: an ancestor whose server ACL lets another account add and delete entries while its mode shows no group or
// other write: a ZFS dataset with native NFSv4 ACLs on FreeBSD, exported over NFSv4 and mounted on the Ubuntu host that
// runs the qualified OpenSSH stack. SFTP shows only mode bits, so FileCat admits the review there; that ancestor is the
// residual the review discloses. FILECAT_TEST_SFTP_NFS4_ANCESTOR names the folder on the mount; the harness proves the
// other account's write separately and passes its numeric ID in FILECAT_TEST_SFTP_NFS4_OTHER_UID.
public sealed partial class SftpPermissionAdmissionTests
{
    [Fact]
    public void An_ancestor_writable_only_through_a_server_ACL_is_admitted_as_the_disclosed_residual()
    {
        string? ancestor = Environment.GetEnvironmentVariable("FILECAT_TEST_SFTP_NFS4_ANCESTOR");
        uint other = 0;
        if (ancestor is null || !Directory.Exists(ancestor) || !uint.TryParse(Environment.GetEnvironmentVariable("FILECAT_TEST_SFTP_NFS4_OTHER_UID"), out other))
            Assert.Skip("Requires the FreeBSD ZFS NFSv4-ACL export mounted below the qualified OpenSSH stack.");
        using var fixture = new Fixture();
        string name = "witness-" + Guid.NewGuid().ToString("N")[..8];
        string leaf = Path.Combine(ancestor, name);
        File.WriteAllText(leaf, "witness");
        try
        {
            // The mode alone looks private: no group or other write, owned by the signed-in account.
            const UnixFileMode shared = UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;
            Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(ancestor) & shared);
            using var review = fixture.Review(ancestor, name);
            Assert.Equal(leaf, review.Evidence.Path);
            var chain = review.Evidence.Ancestors.Append(review.Evidence.StartAttributes)
                .Select(attributes => $"{attributes.UserId}:{attributes.GroupId}:{attributes.Permissions:X4}").ToArray();
            TestContext.Current.SendDiagnosticMessage($"Admitted with ancestors {string.Join(", ", chain)}; account {other} writes the parent through its NFSv4 ACL.");
            Assert.All(review.Evidence.Ancestors, attributes => Assert.Equal(0u, (attributes.Permissions ?? 0) & 0x12));
            Assert.DoesNotContain(review.Evidence.Ancestors, attributes => attributes.UserId == other);
            review.Revalidate(Cancellation);
        }
        finally { File.Delete(leaf); }
    }
}
