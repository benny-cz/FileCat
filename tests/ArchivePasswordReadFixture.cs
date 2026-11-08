using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Tests;

internal static class ArchivePasswordReadFixture
{
    // Public synthetic vector, never a user password or a machine credential.
    internal const string Password = " Public browsing é 🔐 ";
    internal static byte[] Create(string path, bool rar, bool hidden, int length = 4097, bool empty = false)
    {
        byte[] bytes = new byte[length]; new Random(42829308).NextBytes(bytes);
        string digest = Convert.ToHexString(SHA256.HashData(bytes));
        var mode = hidden ? ArchiveEncryptionMode.DataAndNames : ArchiveEncryptionMode.Data;
        using var stage = File.Create(path);
        if (rar)
        {
            var profile = ArchiveWriteProfile.ForRar5(ArchiveRar5Compression.Normal, new(mode));
            using var secret = ArchiveWriterProfileAdapter.Rar5Encryption(profile, new(Password), new(), default);
            Rar5ArchiveWriter.Create(stage, empty ? [] : [new("folder/private-界.bin", ArchiveMemberType.File, new(1700000000, 0), 0x1a4, 0)],
                empty ? [] : new Dictionary<int, Rar5InputDescriptor> { [0] = new(0, length, digest) },
                _ => new MemoryStream(bytes, false), ArchiveWriterProfileAdapter.Rar5(profile), default, secret);
        }
        else
        {
            var profile = ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Lzma2), encryption: new(mode));
            using var secret = ArchiveWriterProfileAdapter.SevenZipEncryption(profile, new(Password), new(), default);
            SevenZipArchiveWriter.Create(stage, empty ? [] : [new("folder/private-界.bin", ArchiveMemberType.File, new(1700000000, 0), 0x1a4, 0)],
                empty ? [] : new Dictionary<int, SevenZipInputDescriptor> { [0] = new(0, length, digest) },
                _ => new MemoryStream(bytes, false), ArchiveWriterProfileAdapter.SevenZip(profile), default,
                ArchiveWriterProfileAdapter.SevenZipHeader(profile), secret);
        }
        return bytes;
    }
}
