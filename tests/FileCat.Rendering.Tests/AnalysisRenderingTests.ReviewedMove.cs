using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public void NativeReviewedMoveSharingAllowsOtherHandlesToChangeTheStreamInventory()
    {
        OrdinaryPreparation();
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new PreparationFixture();
        string file = Path.GetFullPath(Path.Combine(fixture.Root, "move-proof.bin"));
        File.WriteAllText(file, "approved primary bytes");
        File.WriteAllText(file + ":existing", "approved named bytes");
        using (var primary = Open(file, NtFile.Delete, NtFile.ShareRead))
        using (var named = Open(file + ":existing", 0, NtFile.ShareRead | NtFile.ShareDelete))
        {
            // The source handle grants DELETE to the approved mover and excludes writers of the primary
            // stream. The named handle excludes writers of that stream but must share DELETE with the mover.
            Assert.ThrowsAny<IOException>(() => File.Open(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
            Assert.ThrowsAny<IOException>(() => File.Open(file + ":existing", FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete).Dispose());
            File.Delete(file + ":existing");
            using var added = new FileStream(file + ":new", FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            added.Write("unreviewed bytes"u8);
        }
        Assert.Equal("approved primary bytes", File.ReadAllText(file));
        Assert.Throws<FileNotFoundException>(() => File.ReadAllText(file + ":existing"));
        Assert.Equal("unreviewed bytes", File.ReadAllText(file + ":new"));

        static SafeFileHandle Open(string path, uint access, uint share)
        {
            int status = NtFile.TryOpen(null, @"\??\" + path, access | NtFile.ReadData | NtFile.ReadAttributes | NtFile.Synchronize,
                share, NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint | 0x00400000, out var handle);
            if (status >= 0) return handle;
            handle.Dispose();
            throw NtFile.Error(status, "Open the reviewed move test binding");
        }
    }
}
