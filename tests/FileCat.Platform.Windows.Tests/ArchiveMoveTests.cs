using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows.Tests;

public sealed class ArchiveMoveTests : ArchiveMoveAcceptance
{
    protected override IFileSystemOperations NativeFiles()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Actual Windows archive move adapter lane.");
        return new WindowsFileOperations();
    }
}
