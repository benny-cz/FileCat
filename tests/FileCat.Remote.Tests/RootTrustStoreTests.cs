using FileCat.Remote.Ftp;
using FileCat.Remote.Sftp;
using static FileCat.Tests.ProcessAuthorityScopes;

namespace FileCat.Remote.Tests;

/// <summary>
/// I655: the host keys and certificates trusted in the profile are the ordinary account's to change, so FileCat running as
/// root, like an administrator window, neither trusts nor saves them. Each case runs with the decision assumed and, in a
/// test host started as root or elevated, with the process's own.
/// </summary>
public sealed class RootTrustStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("filecat-i655-trust-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Host_keys_in_the_profile_are_neither_trusted_nor_saved(bool assumed)
    {
        string file = Path.Combine(_root, "known_hosts");
        byte[] key = [0, 0, 0, 11, .. "ssh-ed25519"u8, 0, 0, 0, 4, 1, 2, 3, 4];
        var hosts = new HostKeyTrust(file, []);
        using (Ordinary())
        {
            hosts.Remember("owned.invalid", 22, key);
            Assert.Equal(HostKeyStatus.Trusted, hosts.Check("owned.invalid", 22, key).Status);
        }
        byte[] saved = File.ReadAllBytes(file);
        using (ElevatedOrRoot(assumed))
        {
            var check = hosts.Check("owned.invalid", 22, key);
            Assert.Equal(HostKeyStatus.Unavailable, check.Status);
            Assert.Equal($"FileCat does not read user-profile host-key trust while running {RunningAs}. {Advice}.", check.Problem);
            var error = Assert.Throws<IOException>(() => hosts.Remember("other.invalid", 22, key));
            Assert.Equal($"FileCat does not read or save user-profile host-key trust while running {RunningAs}. {Advice}.", error.Message);
        }
        Assert.Equal(saved, File.ReadAllBytes(file));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pinned_certificates_in_the_profile_are_neither_trusted_nor_saved(bool assumed)
    {
        string file = Path.Combine(_root, "trusted_certificates");
        var certificate = new CertificateInfo("owned", "owned", DateTime.UtcNow, DateTime.UtcNow.AddDays(1), new string('a', 64), ["self-signed"]);
        var trust = new CertificateTrust(file);
        using (Ordinary())
        {
            trust.Remember("owned.invalid", 21, certificate.Sha256);
            Assert.Equal(CertificateStatus.Pinned, trust.Check("owned.invalid", 21, certificate).Status);
        }
        byte[] saved = File.ReadAllBytes(file);
        using (ElevatedOrRoot(assumed))
        {
            Assert.Equal(CertificateStatus.Unknown, trust.Check("owned.invalid", 21, certificate).Status);
            var error = Assert.Throws<IOException>(() => trust.Remember("other.invalid", 21, certificate.Sha256));
            Assert.Equal($"FileCat does not save user-profile certificate trust while running {RunningAs}. {Advice}.", error.Message);
        }
        Assert.Equal(saved, File.ReadAllBytes(file));
    }
}
