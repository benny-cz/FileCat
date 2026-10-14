using System.Security.Cryptography;
using System.Text;
using FileCat.Remote.Ftp;

namespace FileCat.Remote.Tests;

public sealed class CertificateTrustInputBudgetTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fc-certificate-budget-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private static readonly string Pin = new('A', 64);
    private static readonly CertificateInfo Certificate = new("subject", "issuer", DateTime.UtcNow,
        DateTime.UtcNow.AddDays(1), Pin, ["self-signed"]);

    private static void WriteLargePins(string path)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false));
        writer.WriteLine("server.example:21 " + Pin);
        writer.Write('#');
        char[] padding = new char[64 * 1024];
        Array.Fill(padding, 'x');
        for (int i = 0; i < 128; i++) writer.Write(padding);
        writer.WriteLine();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Oversized_certificate_pin_file_is_not_materialized_or_trusted(bool linked)
    {
        string target = Path.Combine(_root, "large-pins");
        string path = linked ? Path.Combine(_root, "pins-link") : target;
        WriteLargePins(target);
        if (linked)
        {
            try { File.CreateSymbolicLink(path, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Assert.Skip("This account cannot create a file symbolic link: " + ex.Message);
            }
        }
        Assert.Equal(CertificateStatus.Unknown,
            new CertificateTrust(path).Check("server.example", 21, Certificate).Status);
    }

    [Fact]
    public void Remember_does_not_replace_an_oversized_certificate_pin_file()
    {
        string path = Path.Combine(_root, "pins");
        WriteLargePins(path);
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        new CertificateTrust(path).Remember("new.example", 21, Pin);
        Assert.Equal(before, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
}
