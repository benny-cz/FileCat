using System.Reflection;
using System.Security.Cryptography;
using FileCat.Archives;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.LZMA;
using SharpCompress.Compressors.LZMA.Utilities;

namespace FileCat.Core.Tests;

public sealed class ArchivePasswordPolicyTests
{
    private const string PublicPassword = "FileCat-Public-Policy-Vector-42829304";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Empty_or_invalid_new_passwords_refuse_before_spending_the_kdf_budget(int format, bool invalidUnicode)
    {
        string password = invalidUnicode ? new string((char)0xd800, 1) : string.Empty;
        var budget = new ArchiveCryptoWorkBudget(0, 0);
        Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() =>
        {
            using IDisposable secret = format == 0 ? new Rar5EncryptionSession(password, false, budget, Ct) : new SevenZipEncryptionSession(password, false, budget, Ct);
        }).Reason);
        Assert.Equal(0, budget.Rounds); Assert.Equal(0, budget.HashBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_password_cannot_silently_leave_an_empty_archive_unprotected(bool sevenZip)
    {
        using var stage = new MemoryStream();
        if (sevenZip)
        {
            using var secret = new SevenZipEncryptionSession(PublicPassword, false, new(), Ct);
            Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => SevenZipArchiveWriter.Create(stage, [],
                new Dictionary<int, SevenZipInputDescriptor>(), _ => throw new InvalidOperationException(), new(SevenZipCodec.Copy), Ct, encryption: secret)).Reason);
        }
        else
        {
            using var secret = new Rar5EncryptionSession(PublicPassword, false, new(), Ct);
            Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(() => Rar5ArchiveWriter.Create(stage, [],
                new Dictionary<int, Rar5InputDescriptor>(), _ => throw new InvalidOperationException(), Rar5Compression.Store, Ct, secret)).Reason);
        }
        Assert.Equal(0, stage.Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Archive_sessions_reject_reuse_and_disposed_read_sessions_clear_their_password_bytes(bool sevenZip)
    {
        if (sevenZip)
        {
            using var secret = new SevenZipEncryptionSession(PublicPassword, false, new(), Ct); secret.ClaimArchive();
            Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(secret.ClaimArchive).Reason);
            using var output = new MemoryStream();
            var a = secret.CreateCipher(output, false, 0, Ct); var b = secret.CreateCipher(output, false, 0, Ct);
            using (a.Cipher) using (b.Cipher)
            {
                Assert.Equal(a.Properties.AsSpan(2, 16).ToArray(), b.Properties.AsSpan(2, 16).ToArray());
                Assert.False(a.Properties.AsSpan(18).SequenceEqual(b.Properties.AsSpan(18)));
            }
            var read = new SevenZipReadPasswordSession(PublicPassword, new(), Ct); byte[] bytes = PasswordBytes(read);
            Assert.Contains(bytes, b => b != 0); read.Dispose(); Assert.Equal(-1, bytes.AsSpan().IndexOfAnyExcept((byte)0));
        }
        else
        {
            using var secret = new Rar5EncryptionSession(PublicPassword, false, new(), Ct); secret.ClaimArchive();
            Assert.Equal(ArchiveWriteReason.InvalidRequest, Assert.Throws<ArchiveWriteException>(secret.ClaimArchive).Reason);
            using var output = new MemoryStream(); var a = secret.CreateCipher(output, false, 0, Ct); var b = secret.CreateCipher(output, false, 0, Ct);
            using (a.Cipher) using (b.Cipher) Assert.False(a.Iv.AsSpan().SequenceEqual(b.Iv));
            var read = new Rar5ReadPasswordSession(PublicPassword, new(), Ct); byte[] bytes = PasswordBytes(read);
            Assert.Contains(bytes, b => b != 0); read.Dispose(); Assert.Equal(-1, bytes.AsSpan().IndexOfAnyExcept((byte)0));
        }
    }

    [Fact]
    public void Legacy_7z_direct_key_without_iv_uses_a_zero_iv_and_the_job_broker()
    {
        byte[] raw = new byte[32]; new Random(42829304).NextBytes(raw);
        byte[] password = ArchivePasswordEncoding.Encode(PublicPassword, true), key = new byte[32]; password.AsSpan(0, 32).CopyTo(key);
        using var aes = Aes.Create(); aes.Key = key;
        byte[] encoded = aes.EncryptCbc(raw, new byte[16], PaddingMode.None);
        using var secret = new SevenZipReadPasswordSession(PublicPassword, new(), Ct);
        using var decoded = new AesDecoderStream(new MemoryStream(encoded, false), [63], new Broker(secret.CreateDecryptor), raw.Length);
        using var output = new MemoryStream(); byte[] one = new byte[1]; int read;
        while ((read = decoded.Read(one, 0, 1)) != 0) output.Write(one, 0, read);
        Assert.Equal(raw, output.ToArray()); CryptographicOperations.ZeroMemory(password); CryptographicOperations.ZeroMemory(key);
    }

    private static byte[] PasswordBytes(object session) => (byte[])session.GetType().GetField("_password", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
    private sealed class Broker(SevenZipAesDecryptorFactory factory) : IPasswordProvider
    {
        public SevenZipAesDecryptorFactory? ArchiveDecryptorFactory => factory;
        public string? CryptoGetTextPassword() => throw new InvalidOperationException("Synthetic test must use the bounded broker.");
    }
}
