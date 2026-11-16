using System.Security.Cryptography;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows.Tests;

// Risk R21 for the save-copy stage (EditSessionStore.PublishSaveCopy) on Windows: a copy that fails after writing part of
// the stage is removed through a handle bound to the identity read as the copy ended. The destination is never touched.
public sealed class WindowsEditSaveCopyStageCleanupTests
{
    [Fact]
    public void A_partial_stage_is_removed_through_a_handle_bound_to_its_identity()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows handle-bound deletion.");
        if (Environment.IsPrivilegedProcess) Assert.Skip("Edit sessions refuse an elevated process.");
        string root = Directory.CreateTempSubdirectory("filecat-save-copy-stage-").FullName;
        try
        {
            string snapshot = Directory.CreateDirectory(Path.Combine(root, "snapshot")).FullName;
            string folder = Directory.CreateDirectory(Path.Combine(root, "destination")).FullName;
            string destination = Path.Combine(folder, "notes.txt"); File.WriteAllText(destination, "previous destination");
            byte[] bytes = "the complete edit"u8.ToArray(); string source = Path.Combine(snapshot, "notes.txt"); File.WriteAllBytes(source, bytes);
            var files = new PartialCopy();
            using (var copy = new EditCommitCopy(source, Convert.ToHexString(SHA256.HashData(bytes)), 0, bytes.Length, files))
            {
                var error = Assert.Throws<IOException>(() => EditSessionStore.PublishSaveCopy(copy, destination, files, () => { }));
                Assert.Equal("Controlled partial copy.", error.Message);
            }
            Assert.Equal("previous destination", File.ReadAllText(destination));
            Assert.Empty(Directory.GetFiles(folder, ".filecat-edit-copy-*.tmp"));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }
    }

    private sealed class PartialCopy : WindowsFileOperations
    {
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        { File.WriteAllText(destination, "partial stage"); throw new IOException("Controlled partial copy."); }
    }
}
