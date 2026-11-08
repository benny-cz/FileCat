using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchivePasswordCommandTests
{
    [Theory, InlineData(false, false), InlineData(false, true), InlineData(true, false), InlineData(true, true)]
    public async Task Assessment_reports_password_required_without_changing_the_strict_reader_refusal(bool sevenZip, bool hide)
    {
        var ct = TestContext.Current.CancellationToken; const string password = "Public command password é 🔐";
        byte[] payload = "public synthetic payload"u8.ToArray(); string digest = Convert.ToHexString(SHA256.HashData(payload));
        using var stage = new MemoryStream();
        var protectedProfile = sevenZip ? ArchiveWriteProfile.ForSevenZip(encryption: new(hide ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data))
            : ArchiveWriteProfile.ForRar5(encryption: new(hide ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data));
        using var grant = new ArchiveWorkerSecretGrant(password);
        if (sevenZip)
        {
            using var secret = ArchiveWriterProfileAdapter.SevenZipEncryption(protectedProfile, grant, new(), ct);
            SevenZipArchiveWriter.Create(stage, [new("private-name", ArchiveMemberType.File, new(1700000000, 0), 0x1a4, 1)],
                new Dictionary<int, SevenZipInputDescriptor> { [1] = new(1, payload.Length, digest) },
                _ => new MemoryStream(payload, false), ArchiveWriterProfileAdapter.SevenZip(protectedProfile), ct,
                ArchiveWriterProfileAdapter.SevenZipHeader(protectedProfile), secret);
        }
        else
        {
            using var secret = ArchiveWriterProfileAdapter.Rar5Encryption(protectedProfile, grant, new(), ct);
            Rar5ArchiveWriter.Create(stage, [new("private-name", ArchiveMemberType.File, new(1700000000, 0), 0x1a4, 1)],
                new Dictionary<int, Rar5InputDescriptor> { [1] = new(1, payload.Length, digest) },
                _ => new MemoryStream(payload, false), ArchiveWriterProfileAdapter.Rar5(protectedProfile), ct, secret);
        }
        byte[] bytes = stage.ToArray(); string sha = Convert.ToHexString(SHA256.HashData(bytes));
        var plainProfile = sevenZip ? ArchiveWriteProfile.ForSevenZip() : ArchiveWriteProfile.ForRar5();
        var client = new ArchiveAssessmentClient((nonce, _) => ArchiveAssessmentClientTests.Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(new(plainProfile, bytes.Length, sha), () => new MemoryStream(bytes, false), ct));
        Assert.Equal(ArchiveWriteReason.PasswordRequired, error.Reason);
        Assert.DoesNotContain("private-name", error.Message); Assert.DoesNotContain(password, error.Message);
    }
}
