using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsCompressionNativeTests
{
    private static SafeFileHandle Open(SafeFileHandle? parent, string name, uint rights, uint share, bool directory)
    {
        int status = NtFile.TryOpen(parent, name, rights | NtFile.ReadAttributes | NtFile.Synchronize, share, NtFile.Open,
            NtFile.OpenReparsePoint | (directory ? NtFile.DirectoryFile : NtFile.NonDirectoryFile), out var file, caseSensitive: true);
        if (status >= 0) return file;
        file.Dispose(); throw NtFile.Error(status, "Open the disposable compression fixture");
    }
    private static void Check(WindowsCompression.CallResult call, ushort expected)
    {
        Assert.True(call.NativeSucceeded, $"Native SET error {call.NativeError}; postcondition {call.After}");
        Assert.Equal(0, call.After.Error); Assert.Equal(expected, call.After.Native);
    }
    private static ushort Format(string path, bool directory = false)
    {
        using var file = Open(null, @"\??\" + path, 0, NtFile.ShareAll, directory);
        var state = WindowsCompression.Query(file);
        Assert.Equal(0, state.Error); Assert.NotNull(state.Native);
        return state.Native.Value;
    }
    private static string Hash(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(input));
    }

    [Fact]
    public void Same_mutation_handle_with_live_metadata_pin_preserves_named_stream_states_and_payload_hashes()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-compression-abi-").FullName;
        try
        {
            string path = Path.Join(root, "main.bin"), clear = path + ":clear:$DATA", compressed = path + ":compressed:$DATA";
            byte[] bytes = new byte[1024 * 1024]; Random.Shared.NextBytes(bytes.AsSpan(0, 8192));
            foreach (string stream in new[] { path, clear, compressed }) File.WriteAllBytes(stream, bytes);
            using (var setup = Open(null, @"\??\" + compressed, NtFile.ReadData | NtFile.WriteData, NtFile.ShareAll, false))
                Check(WindowsCompression.InvokeAdmitted(setup, StorageAction.Compress), WindowsCompression.Lznt1);
            string mainHash = Hash(path), clearHash = Hash(clear), compressedHash = Hash(compressed);
            using var parent = Open(null, @"\??\" + root, 0, NtFile.ShareAll, true);
            using var pin = Open(parent, "main.bin", 0, NtFile.ShareAll, false);
            Assert.Equal(WindowsCompression.None, WindowsCompression.Query(pin).Native);
            using (var mutation = Open(parent, "main.bin", NtFile.ReadData | NtFile.WriteData, NtFile.ShareRead, false))
            {
                Assert.Equal(ProtectedHexFile.Identity(pin), ProtectedHexFile.Identity(mutation));
                Check(WindowsCompression.InvokeAdmitted(mutation, StorageAction.Compress), WindowsCompression.Lznt1);
                Assert.Equal(WindowsCompression.None, Format(clear)); Assert.Equal(WindowsCompression.Lznt1, Format(compressed));
                Check(WindowsCompression.InvokeAdmitted(mutation, StorageAction.RemoveCompression), WindowsCompression.None);
                Assert.Equal(WindowsCompression.None, Format(clear)); Assert.Equal(WindowsCompression.Lznt1, Format(compressed));
            }
            Assert.Equal(mainHash, Hash(path)); Assert.Equal(clearHash, Hash(clear)); Assert.Equal(compressedHash, Hash(compressed));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Directory_mutation_and_bounded_enumeration_use_the_same_handle_and_preserve_the_ReadOnly_marker()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-compression-directory-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "folder")).FullName;
            File.WriteAllText(Path.Join(folder, "existing"), "existing payload");
            Directory.CreateDirectory(Path.Join(folder, "child"));
            File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.ReadOnly);
            using var parent = Open(null, @"\??\" + root, 0, NtFile.ShareAll, true);
            using var pin = Open(parent, "folder", 0, NtFile.ShareAll, true);
            using (var mutation = Open(parent, "folder", NtFile.ListDirectory | NtFile.WriteData, NtFile.ShareRead, true))
            {
                Assert.Equal(ProtectedHexFile.Identity(pin), ProtectedHexFile.Identity(mutation));
                Check(WindowsCompression.InvokeAdmitted(mutation, StorageAction.Compress), WindowsCompression.Lznt1);
                Assert.NotEqual(0u, NtFile.GetBasic(mutation).FileAttributes & (uint)FileAttributes.ReadOnly);
                Assert.Equal(new[] { "child", "existing" }, NtFile.List(mutation, 4, TestContext.Current.CancellationToken).Select(e => e.Name).Order(StringComparer.Ordinal));
                using var child = Open(mutation, "existing", NtFile.ReadData | NtFile.WriteData, NtFile.ShareRead, false);
                Assert.Equal(WindowsCompression.None, WindowsCompression.Query(child).Native); // setting a default did not convert existing contents
                Check(WindowsCompression.InvokeAdmitted(child, StorageAction.Compress), WindowsCompression.Lznt1);
                Check(WindowsCompression.InvokeAdmitted(mutation, StorageAction.RemoveCompression), WindowsCompression.None);
                Assert.Equal(2, NtFile.List(mutation, 4, TestContext.Current.CancellationToken).Count);
                Assert.NotEqual(0u, NtFile.GetBasic(mutation).FileAttributes & (uint)FileAttributes.ReadOnly);
            }
            Assert.Equal("existing payload", File.ReadAllText(Path.Join(folder, "existing")));
        }
        finally
        {
            // The assertion above requires preservation by the product primitive; fixture cleanup then removes its marker.
            string folder = Path.Join(root, "folder");
            if (Directory.Exists(folder)) File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.ReadOnly);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Folder_default_only_changes_new_entries_and_same_volume_moves_keep_their_state()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-compression-inheritance-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "compressed")).FullName;
            string peer = Directory.CreateDirectory(Path.Join(root, "clear")).FullName;
            string existing = Path.Join(folder, "existing.bin");
            string fromClear = Path.Join(peer, "moved-in.bin");
            byte[] payload = new byte[128 * 1024];
            payload[0] = 23;
            File.WriteAllBytes(existing, payload);
            File.WriteAllBytes(fromClear, payload);

            using (var change = Open(null, @"\??\" + folder, NtFile.ListDirectory | NtFile.WriteData, NtFile.ShareRead, true))
                Check(WindowsCompression.InvokeAdmitted(change, StorageAction.Compress), WindowsCompression.Lznt1);
            Assert.Equal(WindowsCompression.None, Format(existing));
            string child = Directory.CreateDirectory(Path.Join(folder, "child")).FullName;
            Assert.Equal(WindowsCompression.Lznt1, Format(child, directory: true));
            string created = Path.Join(child, "created.bin");
            File.WriteAllBytes(created, payload);
            Assert.Equal(WindowsCompression.Lznt1, Format(created));

            string movedIn = Path.Join(folder, Path.GetFileName(fromClear)), movedOut = Path.Join(peer, "moved-out.bin");
            File.Move(fromClear, movedIn);
            File.Move(created, movedOut);
            Assert.Equal(WindowsCompression.None, Format(movedIn));
            Assert.Equal(WindowsCompression.Lznt1, Format(movedOut));
            Assert.Equal(payload, File.ReadAllBytes(movedIn));
            Assert.Equal(payload, File.ReadAllBytes(movedOut));

            using (var change = Open(null, @"\??\" + folder, NtFile.ListDirectory | NtFile.WriteData, NtFile.ShareRead, true))
                Check(WindowsCompression.InvokeAdmitted(change, StorageAction.RemoveCompression), WindowsCompression.None);
            Assert.Equal(WindowsCompression.Lznt1, Format(child, directory: true));
            string createdAfterRemoval = Path.Join(folder, "clear-now.bin");
            File.WriteAllBytes(createdAfterRemoval, payload);
            Assert.Equal(WindowsCompression.None, Format(createdAfterRemoval));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Metadata_only_handle_cannot_SET_but_still_returns_a_postcondition_after_native_failure()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Disposable Windows NTFS fixture.");
        string path = Path.GetTempFileName();
        try
        {
            using var pin = Open(null, @"\??\" + path, 0, NtFile.ShareAll, false);
            var result = WindowsCompression.InvokeAdmitted(pin, StorageAction.Compress);
            Assert.False(result.NativeSucceeded); Assert.NotEqual(0, result.NativeError);
            Assert.Equal(WindowsCompression.None, result.After.Native); Assert.Equal(0, result.After.Error);
            Assert.Throws<ArgumentOutOfRangeException>(() => WindowsCompression.InvokeAdmitted(pin, StorageAction.EncryptEfs));
        }
        finally { File.Delete(path); }
    }
}
