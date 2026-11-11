using System.Security.Cryptography;
using FileCat.Core.Edit;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

// Risk R21 for the save-copy stage (EditSessionStore.PublishSaveCopy) on Unix: a copy that fails after writing part of
// the stage has the stage's identity read as the copy ends, so the partial stage is removed exactly where a complete one
// would be (a folder no other user can change) and kept and reported anywhere else. The destination is never touched.
public sealed class EditSaveCopyStageCleanupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_partial_stage_is_removed_in_a_private_folder_and_kept_in_a_shared_one(bool shared)
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Unix rule; Windows deletes through a handle bound to the identity (Platform.Windows tests).");
        using var temp = new TempDir();
        string snapshot = Directory.CreateDirectory(Path.Combine(temp.Path, "snapshot")).FullName;
        File.SetUnixFileMode(snapshot, (UnixFileMode)0x1c0);
        string folder = Directory.CreateDirectory(Path.Combine(temp.Path, "destination")).FullName;
        File.SetUnixFileMode(folder, (UnixFileMode)(shared ? 0x1ff : 0x1c0)); // 0777: anyone may rename its entries; 0700: only this user.
        string destination = Path.Combine(folder, "notes.txt"); File.WriteAllText(destination, "previous destination");
        byte[] bytes = "the complete edit"u8.ToArray(); string source = Path.Combine(snapshot, "notes.txt"); File.WriteAllBytes(source, bytes);
        var files = new PartialCopy();
        using var copy = new EditCommitCopy(source, Convert.ToHexString(SHA256.HashData(bytes)), 0, bytes.Length, files);

        var error = Assert.Throws<IOException>(() => EditSessionStore.PublishSaveCopy(copy, destination, files, () => { }));
        Assert.Equal("Controlled partial copy.", error.Message);
        Assert.Equal("previous destination", File.ReadAllText(destination));
        string[] left = Directory.GetFiles(folder, ".filecat-edit-copy-*.tmp");
        if (shared) Assert.Equal("partial stage", File.ReadAllText(Assert.Single(left)));
        else Assert.Empty(left);
    }

    private sealed class PartialCopy : PortableFileOperations
    {
        public override void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        { File.WriteAllText(destination, "partial stage"); throw new IOException("Controlled partial copy."); }
    }
}
