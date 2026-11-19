using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Records;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsStorageAbiTests
{
    [Fact]
    public void Ntfs_compression_changes_held_streams_and_preserves_their_bytes()
    {
        string directory = CreateFixture();
        string path = Path.Join(directory, "compression.txt");
        byte[] mainBytes = Enumerable.Repeat((byte)0x31, 128 * 1024).ToArray();
        byte[] streamBytes = Enumerable.Repeat((byte)0x62, 96 * 1024).ToArray();
        try
        {
            File.WriteAllBytes(path, mainBytes);
            File.WriteAllBytes(path + ":fixture", streamBytes);
            using var main = File.OpenHandle(path, access: FileAccess.ReadWrite, share: FileShare.ReadWrite);
            using var stream = File.OpenHandle(path + ":fixture", access: FileAccess.ReadWrite, share: FileShare.ReadWrite);
            var identity = ProtectedHexFile.Identity(main);
            Assert.Equal(identity, ProtectedHexFile.Identity(stream));
            Assert.Equal(WindowsCompression.None, WindowsCompression.Query(main).Native);
            Assert.Equal(WindowsCompression.None, WindowsCompression.Query(stream).Native);

            Set(main, StorageAction.Compress, WindowsCompression.Lznt1);
            Assert.Equal(WindowsCompression.None, WindowsCompression.Query(stream).Native);
            Set(stream, StorageAction.Compress, WindowsCompression.Lznt1);
            AssertBytes(main, mainBytes);
            AssertBytes(stream, streamBytes);

            Set(main, StorageAction.RemoveCompression, WindowsCompression.None);
            Assert.Equal(WindowsCompression.Lznt1, WindowsCompression.Query(stream).Native);
            Set(stream, StorageAction.RemoveCompression, WindowsCompression.None);
            Assert.Equal(identity, ProtectedHexFile.Identity(main));
            Assert.Equal(identity, ProtectedHexFile.Identity(stream));
            AssertBytes(main, mainBytes);
            AssertBytes(stream, streamBytes);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public void Native_Efs_round_trip_preserves_identity_and_streams_in_the_details_route()
    {
        string directory = CreateFixture();
        string path = Path.Join(directory, "efs.txt");
        byte[] main = [1, 3, 5, 7, 9];
        byte[] stream = [2, 4, 6, 8];
        var ct = TestContext.Current.CancellationToken;
        try
        {
            File.WriteAllBytes(path, main);
            File.WriteAllBytes(path + ":fixture", stream);
            var records = new WindowsFileRecords();
            var before = records.ReadSummary(path, ct);
            Assert.Equal(FactState.Known, before.Identity.State);
            Assert.Equal(FactState.Known, before.FileEncryption.State);
            Assert.False(before.FileEncryption.Value);

            File.Encrypt(path);
            var encrypted = records.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(before.Identity, encrypted.Summary.Identity);
            Assert.Equal(FactState.Known, encrypted.Summary.FileEncryption.State);
            Assert.True(encrypted.Summary.FileEncryption.Value);
            Assert.Equal(ReportContentKind.MetadataOnly, encrypted.ContentKind);
            Assert.Equal(main, File.ReadAllBytes(path));
            Assert.Equal(stream, File.ReadAllBytes(path + ":fixture"));

            File.Decrypt(path);
            var after = records.ReadSummary(path, ct);
            Assert.Equal(before.Identity, after.Identity);
            Assert.Equal(FactState.Known, after.FileEncryption.State);
            Assert.False(after.FileEncryption.Value);
            Assert.Equal(main, File.ReadAllBytes(path));
            Assert.Equal(stream, File.ReadAllBytes(path + ":fixture"));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static string CreateFixture()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_STORAGE_ABI_ROOT");
        if (!OperatingSystem.IsWindows() || string.IsNullOrWhiteSpace(root))
            Assert.Skip("Requires a prepared disposable Windows NTFS storage ABI fixture.");
        Assert.True(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000));
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        string architecture = RuntimeInformation.ProcessArchitecture.ToString();
        Assert.Equal(Environment.GetEnvironmentVariable("FILECAT_TEST_WINDOWS_STORAGE_ABI_ARCHITECTURE"), architecture);
        Assert.Equal(RuntimeInformation.OSArchitecture, RuntimeInformation.ProcessArchitecture);
        Assert.True(Path.IsPathFullyQualified(root));
        Assert.True(Directory.Exists(root));
        var drive = new DriveInfo(Path.GetPathRoot(root)!);
        Assert.Equal(DriveType.Fixed, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        TestContext.Current.TestOutputHelper?.WriteLine($"Native {architecture}, {RuntimeInformation.FrameworkDescription}, {RuntimeInformation.OSDescription}, medium integrity.");
        return Directory.CreateDirectory(Path.Join(root, Guid.NewGuid().ToString("N"))).FullName;
    }

    private static void Set(SafeFileHandle stream, StorageAction action, ushort format)
    {
        var result = WindowsCompression.InvokeAdmitted(stream, action);
        Assert.True(result.NativeSucceeded, $"{action} failed with Windows error {result.NativeError}.");
        Assert.Equal(format, result.After.Native);
    }

    private static void AssertBytes(SafeFileHandle stream, byte[] expected)
    {
        Assert.Equal(expected.LongLength, RandomAccess.GetLength(stream));
        byte[] actual = new byte[expected.Length];
        int offset = 0;
        while (offset < actual.Length)
        {
            int count = RandomAccess.Read(stream, actual.AsSpan(offset), offset);
            Assert.True(count > 0);
            offset += count;
        }
        Assert.Equal(expected, actual);
    }
}
