using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using SharpCompress.Compressors.LZMA;

namespace FileCat.Core.Tests;

/// <summary>Public synthetic vectors only. These values are neither user secrets nor VM login credentials.</summary>
public sealed class ArchivePasswordPrimitiveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Password_encoding_preserves_spaces_unicode_and_decomposed_characters(bool sevenZip)
    {
        const string value = " public vector 界 🚀 e\u0301 ";
        byte[] actual = ArchivePasswordEncoding.Encode(value, sevenZip);
        Assert.Equal((sevenZip ? Encoding.Unicode : Encoding.UTF8).GetBytes(value), actual);
        CryptographicOperations.ZeroMemory(actual);
        Assert.Empty(ArchivePasswordEncoding.Encode([], sevenZip));
        Assert.Throws<ArchiveWriteException>(() => ArchivePasswordEncoding.Encode(new string('\ud800', 1), sevenZip));
    }

    [Fact]
    public void Password_encoded_size_is_checked_without_truncation()
    {
        Assert.Equal(1024, ArchivePasswordEncoding.Encode(new string('a', 1024), false).Length);
        Assert.Throws<ArchiveWriteException>(() => ArchivePasswordEncoding.Encode(new string('a', 513), true));
        Assert.Throws<ArchiveWriteException>(() => ArchivePasswordEncoding.Encode(new string('界', 400), false));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(15)]
    public void Rar_keys_match_independent_framework_pbkdf2_iteration_counts(int power)
    {
        byte[] password = Encoding.UTF8.GetBytes("public RAR5 vector 界"), salt = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray();
        using var keys = Rar5EncryptionPrimitives.Derive(password, salt, power, new(), Ct);
        int rounds = 1 << power;
        Assert.Equal(Rfc(rounds), keys.EncryptionKey.ToArray());
        Assert.Equal(Rfc(rounds + 16), keys.ChecksumKey.ToArray());
        byte[] last = Rfc(rounds + 32), check = new byte[12]; last.AsSpan(0, 8).CopyTo(check);
        for (int index = 8; index < last.Length; index++) check[index & 7] ^= last[index];
        SHA256.HashData(check.AsSpan(0, 8)).AsSpan(0, 4).CopyTo(check.AsSpan(8));
        Assert.Equal(check, keys.PasswordCheck.ToArray()); Assert.Equal(Rar5PasswordCheck.Match, keys.Check(check));
        using var other = Rar5EncryptionPrimitives.Derive("public other vector"u8, salt, power, new(), Ct);
        Assert.Equal(Rar5PasswordCheck.IncorrectPassword, keys.Check(other.PasswordCheck));
        check[11] ^= 1; Assert.Equal(Rar5PasswordCheck.CorruptCheckData, keys.Check(check));
        Assert.Equal(Rar5PasswordCheck.CorruptCheckData, keys.Check(check.AsSpan(0, 11)));
        CryptographicOperations.ZeroMemory(password); CryptographicOperations.ZeroMemory(last);

        byte[] Rfc(int count) => Rfc2898DeriveBytes.Pbkdf2(password, salt, count, HashAlgorithmName.SHA256, 32);
    }

    [Fact]
    public void Rar_crc_protection_matches_the_separate_hmac_and_folding_definition()
    {
        using var keys = Rar5EncryptionPrimitives.Derive("public CRC vector"u8, new byte[16], 4, new(), Ct);
        const uint crc = 0xcbf43926;
        Span<byte> raw = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(raw, crc);
        byte[] mac = HMACSHA256.HashData(keys.ChecksumKey, raw);
        byte[] folded = new byte[4];
        for (int offset = 0; offset < 32; offset += 4)
            for (int index = 0; index < 4; index++) folded[index] ^= mac[offset + index];
        Assert.Equal(BinaryPrimitives.ReadUInt32LittleEndian(folded), keys.ProtectCrc32(crc));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(12, 16)]
    [InlineData(19, 16)]
    public void Sevenzip_keys_match_the_existing_independent_decoder_kdf(int power, int saltBytes)
    {
        byte[] password = ArchivePasswordEncoding.Encode("public 7z vector 界", true), salt = Enumerable.Range(0, saltBytes).Select(x => (byte)x).ToArray();
        using var key = SevenZipEncryptionPrimitives.Derive(password, salt, power, new(), Ct);
        // Existing decoder implementation uses separate incremental TransformBlock/counter-carry code. Do not add
        // a production reflection route; this private test invokes only its pure legacy KDF for differential proof.
        object decoder = RuntimeHelpers.GetUninitializedObject(typeof(AesDecoderStream));
        MethodInfo method = typeof(AesDecoderStream).GetMethod("InitKey", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var reference = (byte[])method.Invoke(decoder, [power, salt, password])!;
        Assert.Equal(reference, key.Key.ToArray());
        CryptographicOperations.ZeroMemory(reference); CryptographicOperations.ZeroMemory(password);
    }

    [Fact]
    public void Sevenzip_legacy_direct_key_has_its_own_explicit_semantics()
    {
        byte[] salt = Enumerable.Range(0, 16).Select(x => (byte)x).ToArray(), password = Encoding.Unicode.GetBytes("public direct-key vector");
        using var key = SevenZipEncryptionPrimitives.Derive(password, salt, 63, new(), Ct);
        Assert.Equal(salt.Concat(password).Take(32).ToArray(), key.Key.ToArray());
        using var shortKey = SevenZipEncryptionPrimitives.Derive([0x61, 0], [], 63, new(), Ct);
        Assert.Equal(new byte[] { 0x61, 0 }.Concat(new byte[30]).ToArray(), shortKey.Key.ToArray());
    }

    [Fact]
    public void Shared_kdf_work_is_cumulative_and_refusal_does_not_reset_counters()
    {
        var budget = new ArchiveCryptoWorkBudget(95, long.MaxValue);
        using var first = Rar5EncryptionPrimitives.Derive("public vector"u8, new byte[16], 4, budget, Ct);
        Assert.Equal(48, budget.Rounds); long hashed = budget.HashBytes;
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => Rar5EncryptionPrimitives.Derive("public vector"u8, new byte[16], 4, budget, Ct)).Reason);
        Assert.Equal(48, budget.Rounds); Assert.Equal(hashed, budget.HashBytes);
        using var second = SevenZipEncryptionPrimitives.Derive([0x61, 0], [], 4, budget, Ct);
        Assert.Equal(64, budget.Rounds);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => SevenZipEncryptionPrimitives.Derive([0x61, 0], [], 4, new(100, 1), Ct)).Reason);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25)]
    [InlineData(62)]
    public void Excessive_or_invalid_kdf_parameters_refuse_before_derivation(int power)
    {
        var budget = new ArchiveCryptoWorkBudget();
        Assert.Throws<ArchiveWriteException>(() => Rar5EncryptionPrimitives.Derive([], new byte[16], power, budget, Ct));
        Assert.Throws<ArchiveWriteException>(() => SevenZipEncryptionPrimitives.Derive([], [], power, budget, Ct));
        Assert.Equal(0, budget.Rounds); Assert.Equal(0, budget.HashBytes);
    }

    [Fact]
    public void Owned_key_material_is_zeroed_and_inaccessible_after_disposal()
    {
        var rar = Rar5EncryptionPrimitives.Derive("public disposal vector"u8, new byte[16], 4, new(), Ct);
        var aes = rar.EncryptionKey; var checksum = rar.ChecksumKey; var check = rar.PasswordCheck;
        Assert.True(aes.IndexOfAnyExcept((byte)0) >= 0); rar.Dispose(); rar.Dispose();
        Assert.Equal(-1, aes.IndexOfAnyExcept((byte)0)); Assert.Equal(-1, checksum.IndexOfAnyExcept((byte)0)); Assert.Equal(-1, check.IndexOfAnyExcept((byte)0));
        Assert.Throws<ObjectDisposedException>(() => { _ = rar.EncryptionKey.Length; });
        var seven = SevenZipEncryptionPrimitives.Derive([0x61, 0], [], 4, new(), Ct); var key = seven.Key;
        seven.Dispose(); seven.Dispose(); Assert.Equal(-1, key.IndexOfAnyExcept((byte)0));
        Assert.Throws<ObjectDisposedException>(() => { _ = seven.Key.Length; });
    }

    [Fact]
    public void Cancelled_kdf_does_not_reserve_or_derive_work()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var budget = new ArchiveCryptoWorkBudget();
        Assert.Throws<OperationCanceledException>(() => Rar5EncryptionPrimitives.Derive([], new byte[16], 15, budget, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => SevenZipEncryptionPrimitives.Derive([], [], 19, budget, cancellation.Token));
        Assert.Equal(0, budget.Rounds); Assert.Equal(0, budget.HashBytes);
    }
}
