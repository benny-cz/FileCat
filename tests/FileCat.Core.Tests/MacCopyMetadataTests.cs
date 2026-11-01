using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class MacCopyMetadataTests
{
    [Fact]
    public void Copy_preserves_resource_fork_and_other_xattrs_on_mac()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS resource forks only.");
        using var dir = new TempDir();
        string source = Path.Combine(dir.Path, "source.txt");
        string destination = Path.Combine(dir.Path, "destination.txt");
        byte[] data = Encoding.UTF8.GetBytes("data fork remains distinct");
        byte[] fork = new byte[128 * 1024];
        for (int i = 0; i < fork.Length; i++) fork[i] = (byte)(i * 17 + 3);
        File.WriteAllBytes(source, data);
        File.WriteAllBytes(Path.Join(source, "..namedfork", "rsrc"), fork);
        Assert.True(Xattr.Set(source, "org.filecat.copy-test", "separate metadata"));

        new UnixFileOperations().CopyFile(source, destination, new FileCopyOptions(), null,
            TestContext.Current.CancellationToken);

        Assert.Equal(data, File.ReadAllBytes(destination));
        Assert.Equal(fork, File.ReadAllBytes(Path.Join(destination, "..namedfork", "rsrc")));
        Assert.Equal("separate metadata", Xattr.Get(destination, "org.filecat.copy-test"));
        Assert.Equal(data, File.ReadAllBytes(source));
    }
}
