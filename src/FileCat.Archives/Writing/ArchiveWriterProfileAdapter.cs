using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

/// <summary>
/// Exact public intent to existing stream encoders. No admission, paths, publication or registration authority.
/// Workers must bind a received grant to their role and complete canonical request before passing it here.
/// </summary>
internal static class ArchiveWriterProfileAdapter
{
    internal static SevenZipCodecOptions SevenZip(ArchiveWriteProfile profile)
    {
        Require(profile, ArchiveWriteFormat.SevenZip); var options = profile.SevenZip!;
        var codec = options.Codec switch
        {
            ArchiveSevenZipCodec.Copy => SevenZipCodec.Copy, ArchiveSevenZipCodec.Lzma => SevenZipCodec.Lzma,
            ArchiveSevenZipCodec.Lzma2 => SevenZipCodec.Lzma2, ArchiveSevenZipCodec.Bzip2 => SevenZipCodec.Bzip2,
            ArchiveSevenZipCodec.Deflate => SevenZipCodec.Deflate, ArchiveSevenZipCodec.Zstandard => SevenZipCodec.Zstandard,
            ArchiveSevenZipCodec.Ppmd7 => SevenZipCodec.Ppmd7, _ => throw Invalid(),
        };
        var filter = options.Filter switch
        {
            ArchiveSevenZipFilter.None => SevenZipFilter.None, ArchiveSevenZipFilter.Delta => SevenZipFilter.Delta,
            ArchiveSevenZipFilter.X86 => SevenZipFilter.X86, ArchiveSevenZipFilter.Arm => SevenZipFilter.Arm,
            ArchiveSevenZipFilter.ArmThumb => SevenZipFilter.ArmThumb, ArchiveSevenZipFilter.Arm64 => SevenZipFilter.Arm64,
            ArchiveSevenZipFilter.Ppc => SevenZipFilter.Ppc, ArchiveSevenZipFilter.Ia64 => SevenZipFilter.Ia64,
            ArchiveSevenZipFilter.Sparc => SevenZipFilter.Sparc, ArchiveSevenZipFilter.RiscV => SevenZipFilter.RiscV,
            ArchiveSevenZipFilter.Swap2 => SevenZipFilter.Swap2, ArchiveSevenZipFilter.Swap4 => SevenZipFilter.Swap4,
            ArchiveSevenZipFilter.Bcj2 => SevenZipFilter.Bcj2, _ => throw Invalid(),
        };
        var result = new SevenZipCodecOptions(codec, profile.CompressionLevel, profile.DictionaryBytes, options.ModelOrder,
            options.ModelMemoryBytes, filter, options.DeltaDistance);
        SevenZipCodecEncoder.Validate(result, allowBcj2: true); return result;
    }
    internal static SevenZipHeaderMode SevenZipHeader(ArchiveWriteProfile profile)
    {
        Require(profile, ArchiveWriteFormat.SevenZip);
        return profile.SevenZip!.HeaderCompression switch
        { ArchiveSevenZipHeaderCompression.Raw => SevenZipHeaderMode.Raw, ArchiveSevenZipHeaderCompression.Lzma2 => SevenZipHeaderMode.Lzma2, _ => throw Invalid() };
    }
    internal static Rar5Compression Rar5(ArchiveWriteProfile profile)
    {
        Require(profile, ArchiveWriteFormat.Rar5);
        return profile.CompressionLevel switch
        { 0 => Rar5Compression.Store, 1 => Rar5Compression.Fastest, 2 => Rar5Compression.Fast, 3 => Rar5Compression.Normal,
            4 => Rar5Compression.Good, 5 => Rar5Compression.Best, _ => throw Invalid() };
    }
    internal static SevenZipEncryptionSession? SevenZipEncryption(ArchiveWriteProfile profile, ArchiveWorkerSecretGrant? grant,
        ArchiveCryptoWorkBudget budget, CancellationToken ct)
    {
        try
        {
            Require(profile, ArchiveWriteFormat.SevenZip); ArgumentNullException.ThrowIfNull(budget); ct.ThrowIfCancellationRequested();
            if (profile.Encryption.Mode == ArchiveEncryptionMode.None) { if (grant is not null) throw Invalid(); return null; }
            if (grant is null) throw Invalid();
            return grant.UseOnce(password => new SevenZipEncryptionSession(password,
                profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames, budget, ct, profile.Encryption.CountPower));
        }
        finally { grant?.Dispose(); }
    }
    internal static Rar5EncryptionSession? Rar5Encryption(ArchiveWriteProfile profile, ArchiveWorkerSecretGrant? grant,
        ArchiveCryptoWorkBudget budget, CancellationToken ct)
    {
        try
        {
            Require(profile, ArchiveWriteFormat.Rar5); ArgumentNullException.ThrowIfNull(budget); ct.ThrowIfCancellationRequested();
            if (profile.Encryption.Mode == ArchiveEncryptionMode.None) { if (grant is not null) throw Invalid(); return null; }
            if (grant is null) throw Invalid();
            return grant.UseOnce(password => new Rar5EncryptionSession(password,
                profile.Encryption.Mode == ArchiveEncryptionMode.DataAndNames, budget, ct, profile.Encryption.CountPower));
        }
        finally { grant?.Dispose(); }
    }
    private static void Require(ArchiveWriteProfile profile, ArchiveWriteFormat format)
    { ArgumentNullException.ThrowIfNull(profile); if (profile.Version != 2 || profile.Format != format) throw Invalid(); }
    private static ArchiveWriteException Invalid() => new(ArchiveWriteReason.InvalidRequest, "The encoder intent or password grant does not match this format and password mode.");
}
