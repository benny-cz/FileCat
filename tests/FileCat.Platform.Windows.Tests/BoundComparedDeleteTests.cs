using System.Security.Cryptography;

namespace FileCat.Platform.Windows.Tests;

public sealed class BoundComparedDeleteTests
{
    [Fact]
    public void Bound_delete_refuses_same_metadata_substitute_and_removes_unchanged_file()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows handle-bound deletion required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-compare-delete-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var files = new WindowsFileOperations();
            string target = Path.Combine(root, "target.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            byte[] b = Enumerable.Repeat((byte)'B', 4096).ToArray();
            File.WriteAllBytes(target, a);
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            string digest = Convert.ToHexString(SHA256.HashData(a));
            DateTime stamp = File.GetLastWriteTimeUtc(target);
            File.Move(target, Path.Combine(root, "retired-A.bin"));
            File.WriteAllBytes(target, b);
            File.SetLastWriteTimeUtc(target, stamp);
            var refused = files.DeleteReviewedFile(target, identity, digest, a.Length, TestContext.Current.CancellationToken);
            Assert.NotNull(refused);
            Assert.False(refused.Deleted);
            Assert.True(refused.Changed);
            Assert.Equal(b, File.ReadAllBytes(target));

            identity = Assert.IsType<string>(files.GetFileIdentity(target));
            var accepted = files.DeleteReviewedFile(target, identity, Convert.ToHexString(SHA256.HashData(b)), b.Length,
                TestContext.Current.CancellationToken);
            Assert.NotNull(accepted);
            Assert.True(accepted.Deleted, accepted.Reason);
            Assert.False(File.Exists(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void The_verified_file_cannot_be_swapped_in_the_final_delete_window()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows(), "Windows handle-bound deletion required.");
        string root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "filecat-bound-delete-window-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var files = new WindowsFileOperations();
            string target = Path.Combine(root, "target.bin");
            byte[] a = Enumerable.Repeat((byte)'A', 4096).ToArray();
            File.WriteAllBytes(target, a);
            string identity = Assert.IsType<string>(files.GetFileIdentity(target));
            bool swapBlocked = false;
            var result = files.DeleteReviewedFileCore(target, identity, Convert.ToHexString(SHA256.HashData(a)), a.Length,
                TestContext.Current.CancellationToken, beforeDelete: () =>
                {
                    try { File.Move(target, Path.Combine(root, "swapped.bin")); }
                    catch (IOException) { swapBlocked = true; }
                });
            Assert.True(swapBlocked);
            Assert.NotNull(result);
            Assert.True(result.Deleted, result.Reason);
            Assert.False(File.Exists(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
