namespace SharpCompress.Compressors.LZMA.Utilities;

internal interface IPasswordProvider
{
    string? CryptoGetTextPassword();
    SevenZipAesDecryptorFactory? ArchiveDecryptorFactory => null;
}
