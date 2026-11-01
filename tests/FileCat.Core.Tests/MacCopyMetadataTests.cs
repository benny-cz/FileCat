using System.Diagnostics;
using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class MacCopyMetadataTests
{
    [Fact]
    public void Created_folder_can_receive_its_source_acl_and_xattrs_after_children()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS directory metadata APIs only.");
        using var dir = new TempDir();
        string source = dir.Dir("source");
        string destination = dir.Dir("destination");
        File.WriteAllText(Path.Combine(source, "child.txt"), "child");
        File.Copy(Path.Combine(source, "child.txt"), Path.Combine(destination, "child.txt"));
        Assert.True(Xattr.Set(source, "org.filecat.folder-copy-test", "folder metadata"));
        var chmod = new ProcessStartInfo("/bin/chmod") { RedirectStandardError = true, UseShellExecute = false };
        chmod.ArgumentList.Add("+a");
        chmod.ArgumentList.Add("everyone deny add_file");
        chmod.ArgumentList.Add(source);
        using (var process = Process.Start(chmod)!)
        {
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }

        var sourceMetadata = MacExtendedAttributes.ProbeDirectory(source);
        var destinationMetadata = MacExtendedAttributes.ProbeDirectory(destination);
        Assert.True(sourceMetadata.HasMetadata);
        Assert.False(destinationMetadata.HasMetadata);
        MacExtendedAttributes.CopyDirectory(source, destination, sourceMetadata.Identity,
            destinationMetadata.Identity, TestContext.Current.CancellationToken);

        Assert.Equal("folder metadata", Xattr.Get(destination, "org.filecat.folder-copy-test"));
        Assert.Equal(File.ReadAllText(Path.Combine(source, "child.txt")),
            File.ReadAllText(Path.Combine(destination, "child.txt")));
        Assert.Equal(MacExtendedAttributes.ReadDirectoryAclText(source),
            MacExtendedAttributes.ReadDirectoryAclText(destination));
    }

    [Fact]
    public void Copy_of_plain_file_still_succeeds_without_an_acl_or_fork()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS metadata APIs only.");
        using var dir = new TempDir();
        string source = dir.File("plain.txt", "ordinary file");
        string destination = Path.Combine(dir.Path, "copy.txt");

        new UnixFileOperations().CopyFile(source, destination, new FileCopyOptions(), null,
            TestContext.Current.CancellationToken);

        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(destination));
        using var sourceHandle = File.OpenRead(source);
        using var destinationHandle = File.OpenRead(destination);
        Assert.Equal(
            MacExtendedAttributes.ReadAclText(sourceHandle.SafeFileHandle.DangerousGetHandle().ToInt32()),
            MacExtendedAttributes.ReadAclText(destinationHandle.SafeFileHandle.DangerousGetHandle().ToInt32()));
    }

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
        var chmod = new ProcessStartInfo("/bin/chmod") { RedirectStandardError = true, UseShellExecute = false };
        chmod.ArgumentList.Add("+a");
        chmod.ArgumentList.Add("everyone deny write");
        chmod.ArgumentList.Add(source);
        using (var process = Process.Start(chmod)!)
        {
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }
        string sourceAcl;
        using (var sourceHandle = File.OpenRead(source))
            sourceAcl = MacExtendedAttributes.ReadAclText(sourceHandle.SafeFileHandle.DangerousGetHandle().ToInt32());
        Assert.Contains("deny", sourceAcl, StringComparison.OrdinalIgnoreCase);

        new UnixFileOperations().CopyFile(source, destination, new FileCopyOptions(), null,
            TestContext.Current.CancellationToken);

        Assert.Equal(data, File.ReadAllBytes(destination));
        Assert.Equal(fork, File.ReadAllBytes(Path.Join(destination, "..namedfork", "rsrc")));
        Assert.Equal("separate metadata", Xattr.Get(destination, "org.filecat.copy-test"));
        using (var destinationHandle = File.OpenRead(destination))
            Assert.Equal(sourceAcl, MacExtendedAttributes.ReadAclText(destinationHandle.SafeFileHandle.DangerousGetHandle().ToInt32()));
        Assert.Equal(data, File.ReadAllBytes(source));
    }
}
