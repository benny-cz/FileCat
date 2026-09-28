using System.Security.Cryptography;
using System.Text;
using FileCat.Remote.Sftp;

namespace FileCat.Remote.Tests;

public sealed class HostKeyTrustTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "filecat-remote-tests", Guid.NewGuid().ToString("N"));

    public HostKeyTrustTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    private static readonly byte[] Ed = FakeSftpServer.KeyBlob("ssh-ed25519", 1);
    private static readonly byte[] EdOther = FakeSftpServer.KeyBlob("ssh-ed25519", 2);
    private static readonly byte[] Rsa = FakeSftpServer.KeyBlob("ssh-rsa", 3);

    private string Seed(params string[] lines)
    {
        string file = Path.Combine(_dir, "seed_known_hosts");
        File.WriteAllLines(file, lines);
        return file;
    }

    private static string Line(string hosts, byte[] key, string? marker = null) =>
        (marker is null ? "" : marker + " ") + $"{hosts} {HostKeyTrust.KeyTypeOf(key)} {Convert.ToBase64String(key)} comment";

    [Fact]
    public void Fingerprints_and_key_types_match_OpenSSH()
    {
        Assert.Equal("ssh-ed25519", HostKeyTrust.KeyTypeOf(Ed));
        Assert.Equal("SHA256:" + Convert.ToBase64String(SHA256.HashData(Ed)).TrimEnd('='), HostKeyTrust.Fingerprint(Ed));
        Assert.DoesNotContain("=", HostKeyTrust.Fingerprint(Ed));
    }

    [Fact]
    public void OpenSSH_known_hosts_seed_trust_including_hashed_ported_and_wildcard_hosts()
    {
        var salt = RandomNumberGenerator.GetBytes(20);
        string hashed = $"|1|{Convert.ToBase64String(salt)}|{Convert.ToBase64String(HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes("[hashed.example]:2222")))}";
        var trust = new HostKeyTrust(Path.Combine(_dir, "own"), [Seed(
            "# comment", "",
            Line("plain.example,10.0.0.1", Ed),
            Line(hashed, Ed),
            Line("*.corp.example,!evil.corp.example", Ed),
            Line("changed.example", EdOther),
            Line("revoked.example", Ed, "@revoked"),
            Line("typed.example", Rsa),
            "garbage line")]);
        Assert.Equal(HostKeyStatus.Trusted, trust.Check("plain.example", 22, Ed).Status);
        Assert.Equal(HostKeyStatus.Trusted, trust.Check("10.0.0.1", 22, Ed).Status);
        Assert.Equal(HostKeyStatus.Unknown, trust.Check("plain.example", 2222, Ed).Status);
        Assert.Equal(HostKeyStatus.Trusted, trust.Check("hashed.example", 2222, Ed).Status);
        Assert.Equal(HostKeyStatus.Trusted, trust.Check("build.corp.example", 22, Ed).Status);
        Assert.Equal(HostKeyStatus.Unknown, trust.Check("evil.corp.example", 22, Ed).Status);
        var changed = trust.Check("changed.example", 22, Ed);
        Assert.Equal(HostKeyStatus.Changed, changed.Status);
        Assert.Equal(HostKeyTrust.Fingerprint(EdOther), changed.KnownFingerprint);
        Assert.Equal(HostKeyStatus.Revoked, trust.Check("revoked.example", 22, Ed).Status);
        var otherType = trust.Check("typed.example", 22, Ed);
        Assert.Equal(HostKeyStatus.Unknown, otherType.Status);
        Assert.True(otherType.OtherTypesKnown);
    }

    [Fact]
    public void Remembering_replaces_only_in_FileCats_own_file_which_then_takes_precedence()
    {
        string seed = Seed(Line("server.example", EdOther));
        string own = Path.Combine(_dir, "own", "known_hosts");
        var trust = new HostKeyTrust(own, [seed]);
        Assert.Equal(HostKeyStatus.Changed, trust.Check("server.example", 22, Ed).Status);
        trust.Remember("server.example", 22, Ed);
        Assert.Equal(HostKeyStatus.Trusted, trust.Check("server.example", 22, Ed).Status);
        // OpenSSH's file is never edited.
        Assert.Single(File.ReadAllLines(seed));
        trust.Remember("server.example", 22, Ed);
        trust.Remember("server.example", 2200, Ed);
        Assert.Equal(2, File.ReadAllLines(own).Length);
        Assert.Contains(File.ReadAllLines(own), l => l.StartsWith("[server.example]:2200 ssh-ed25519 ", StringComparison.Ordinal));
    }
}
