using System.Runtime.InteropServices;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class LinuxHeldDirectoryTests
{
    [Fact]
    public void Archive_and_ACL_directory_opens_hold_the_real_object_and_refuse_links_and_regular_files()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native Linux directory/ACL opens.");
        using var temporary = new TempDir();
        string folder = Path.Combine(temporary.Path, "real"), link = Path.Combine(temporary.Path, "link"), file = Path.Combine(temporary.Path, "file");
        Directory.CreateDirectory(folder); Directory.CreateSymbolicLink(link, folder); File.WriteAllText(file, "owned");
        var identity = UnixFiles.Stat(folder)!.Value.Identity;
        using (var archive = UnixArchiveNative.PinDirectory(folder)) Assert.Equal(identity, UnixFiles.Stat(archive)!.Value.Identity);
        using (var acl = LinuxPosixAcl.OpenDirectory(folder)) Assert.Equal(identity, UnixFiles.Stat(acl)!.Value.Identity);
        Assert.ThrowsAny<IOException>(() => UnixArchiveNative.PinDirectory(link));
        Assert.ThrowsAny<IOException>(() => UnixArchiveNative.PinDirectory(file));
        Assert.ThrowsAny<IOException>(() => LinuxPosixAcl.OpenDirectory(link));
        Assert.ThrowsAny<IOException>(() => LinuxPosixAcl.OpenDirectory(file));
        Assert.Equal(identity, LinuxPosixAcl.ProbeDirectory(folder).Identity);
        Assert.ThrowsAny<IOException>(() => LinuxPosixAcl.ProbeDirectory(link));
    }

    [Theory]
    [InlineData(Architecture.X86)]
    [InlineData(Architecture.Arm)]
    [InlineData(Architecture.Wasm)]
    public void Unknown_file_open_ABIs_are_not_guessed(Architecture architecture) =>
        Assert.Throws<PlatformNotSupportedException>(() => LinuxOpenFlags.ForArchitecture(architecture));
}
