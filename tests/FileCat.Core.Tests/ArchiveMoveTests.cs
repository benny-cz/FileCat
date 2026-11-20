using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

public sealed class ArchiveMoveTests : ArchiveMoveAcceptance
{
    protected override IFileSystemOperations NativeFiles()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Actual Windows adapter acceptance runs in FileCat.Platform.Windows.Tests.");
        return new UnixFileOperations();
    }
}
