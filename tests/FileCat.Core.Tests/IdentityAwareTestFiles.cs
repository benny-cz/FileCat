using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

/// <summary>
/// System.IO test operations with actual stable entry identities on all three test platforms.
/// PortableFileOperations deliberately leaves Windows identity to its production adapter.
/// Container transfer fixtures need that capability; content bytes are never an identity.
/// </summary>
internal partial class IdentityAwareTestFiles : PortableFileOperations
{
    // Offers replacement as the Windows adapter does on a fixed drive. Its bound replacement is still Portable's refusal,
    // so what a test sees for a replaced item is the refusal a destination that changed after review gets.
    public override bool CanReplaceReviewed(string destination) => true;

    public override unsafe string? GetFileIdentity(string path)
    {
        if (!OperatingSystem.IsWindows()) return base.GetFileIdentity(path);
        string full = Path.GetFullPath(path);
        if (!full.StartsWith(@"\\?\", StringComparison.Ordinal))
            full = full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
        // Query the entry itself, including directories/reparse points, with all sharing enabled.
        using var handle = OpenIdentity(full, 0x80, 7, 0, 3, 0x02000000 | 0x00200000, 0);
        if (handle.IsInvalid || !ReadIdentity(handle, 18, out IdentityInfo info, (uint)sizeof(IdentityInfo))) return null;
        return new FileIdentity(info.Volume, Convert.ToHexString(new ReadOnlySpan<byte>(info.Id, 16))).ToString();
    }

    public unsafe string? GetHandleIdentity(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsWindows()) return UnixFiles.Stat(handle)?.Identity.ToString();
        if (!ReadIdentity(handle, 18, out IdentityInfo info, (uint)sizeof(IdentityInfo))) return null;
        return new FileIdentity(info.Volume, Convert.ToHexString(new ReadOnlySpan<byte>(info.Id, 16))).ToString();
    }

    private unsafe struct IdentityInfo
    {
        public ulong Volume;
        public fixed byte Id[16];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle OpenIdentity(string path, uint access, uint sharing, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool ReadIdentity(SafeFileHandle handle, int kind, out IdentityInfo info, uint size);
}

public sealed class IdentityAwareTestFilesTests
{
    [Theory]
    [InlineData("same-bytes")]
    [InlineData("rename")]
    [InlineData("hard-link")]
    [InlineData("directory")]
    [InlineData("missing")]
    public void Owned_fixture_identity_tracks_the_entry_instead_of_content_or_path(string route)
    {
        using var root = new TempDir();
        var files = new IdentityAwareTestFiles();
        string source = root.File("source ž.txt", "same complete fixture bytes");
        string other = Path.Combine(root.Path, "other.txt");
        string original = Assert.IsType<string>(files.GetFileIdentity(source));
        Assert.NotEmpty(original);
        Assert.Equal(original, files.GetFileIdentity(source));
        switch (route)
        {
            case "same-bytes":
                File.Copy(source, other);
                Assert.NotEqual(original, files.GetFileIdentity(other));
                break;
            case "rename":
                File.Move(source, other);
                Assert.Null(files.GetFileIdentity(source));
                Assert.Equal(original, files.GetFileIdentity(other));
                File.WriteAllText(source, "same complete fixture bytes");
                Assert.NotEqual(original, files.GetFileIdentity(source));
                break;
            case "hard-link":
                files.CreateLink(other, source, LinkKind.Hard, false);
                Assert.Equal(original, files.GetFileIdentity(other));
                break;
            case "directory":
                string directory = root.Dir("folder ž");
                string id = Assert.IsType<string>(files.GetFileIdentity(directory));
                Assert.NotEmpty(id);
                Assert.NotEqual(original, id);
                Assert.Equal(id, files.GetFileIdentity(directory));
                break;
            default:
                Assert.Null(files.GetFileIdentity(other));
                break;
        }
    }
}
