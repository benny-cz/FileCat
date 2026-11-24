using FileCat.Core.Edit;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// I654's pattern in the edit cleanup: the reason a temporary edit file was kept can be the system's own message, which
/// ends with a period, and FileCat's sentence added another ("…used by another process..").
/// </summary>
public sealed class EditPrivateCleanupMessageTests
{
    [Fact]
    public void A_file_kept_because_another_program_holds_it_gives_the_systems_reason_with_one_period()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Only Windows refuses to delete a file that another handle holds without delete sharing.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-edit-cleanup-tests", Guid.NewGuid().ToString("N"));
        string path = Path.Combine(Directory.CreateDirectory(root).FullName, "edit-copy.tmp");
        File.WriteAllText(path, "draft");
        try
        {
            string? kept;
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                kept = EditPrivateCleanup.DeleteOwnedFile(new PortableFileOperations(), path, identity: null, inFileCatData: true);
            Assert.NotNull(kept);
            string prefix = "FileCat kept its temporary edit file " + path + ": ";
            Assert.StartsWith(prefix, kept, StringComparison.Ordinal);
            // The system's reason, its own final period given way to the one that ends FileCat's sentence.
            Assert.True(kept.Length > prefix.Length + 1, kept);
            Assert.EndsWith(".", kept, StringComparison.Ordinal);
            Assert.DoesNotContain("..", kept, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
