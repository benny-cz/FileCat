using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsSparseTransformTests
{
    [Fact]
    public void Reviewed_efs_jobs_preserve_a_sparse_file_beyond_the_four_gib_boundary()
    {
        RequireFixture();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_LARGE_EFS") == "1",
            "Requires the explicit large sparse EFS fixture opt-in.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_LARGE_EFS_ROOT");
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(root) && Directory.Exists(root),
            "Requires a prepared disposable NTFS fixture directory.");

        const long length = (4L << 30) + (64L << 20);
        var drive = new DriveInfo(Path.GetPathRoot(root!)!);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal(DriveType.Fixed, drive.DriveType);
        Assert.True(drive.AvailableFreeSpace > 24L << 30,
            "Keep at least 24 GiB free before the native EFS large-file fixture.");
        Assert.True(GetDiskFreeSpace(drive.RootDirectory.FullName, out uint sectors, out uint bytes, out _, out _));
        Assert.Equal(4096u, checked(sectors * bytes));

        string directory = Directory.CreateDirectory(Path.Join(root!, "filecat-large-sparse-efs-" + Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Join(directory, "fixture.bin");
        try
        {
            using (var data = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                unsafe
                {
                    Assert.True(Control(data.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0),
                        "Setting the large fixture sparse failed.");
                }
                data.SetLength(length);
                WriteLargeSparseSentinel(data, 64 * 1024, 17);
                WriteLargeSparseSentinel(data, (2L << 30) + 4096, 29);
                WriteLargeSparseSentinel(data, length - 4096, 43);
                data.Flush(flushToDisk: true);
            }
            WriteStream(path + NamedStream, sparse: true, holes: true, allHole: false, seed: 59);

            var first = Observe(path);
            Assert.Equal(length, first.Main.Length);
            Assert.True(first.Main.Attributes.HasFlag(FileAttributes.SparseFile));
            Assert.True(first.Named.Attributes.HasFlag(FileAttributes.SparseFile));
            Assert.True(first.Main.Ranges.Sum(range => range.Length) < 8L << 20);
            Assert.True(first.Named.Ranges.Sum(range => range.Length) < Length / 2);
            Write("large sparse before", "observed", first);

            bool arm64 = WindowsStorageAbiTests.QualifyNativeArm64Jobs;
            var adapter = new WindowsStorageTransforms(efsSetupBaseDirectory: () => directory,
                qualifyNativeArm64: arm64);
            var candidate = adapter.CreateEfsJobCandidate((_, sid, consent, ct) =>
            {
                var setup = adapter.InspectEfsSetup(sid, consent, true, ct);
                Assert.Null(setup.Problem);
                return StorageEfsRecoveryApproval.DeclareArrangement(consent, setup,
                    Assert.Single(setup.Association!.CurrentUserKeys).CertificateSha1, acknowledged: true);
            });
            var jobs = CreateManager(path, adapter: candidate);
            var timeout = TimeSpan.FromMinutes(8);

            RunEfsJob(jobs, path, StorageAction.EncryptEfs, changed: true, timeout);
            var encrypted = Report("large sparse encrypt", "changed", path, first);
            Assert.True(encrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            VerifyLargeSparseHoles(path, length);
            RunEfsJob(jobs, path, StorageAction.EncryptEfs, changed: false, timeout);

            RunEfsJob(jobs, path, StorageAction.DecryptEfs, changed: true, timeout);
            var decrypted = Report("large sparse decrypt", "changed", path, first);
            Assert.False(decrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            VerifyLargeSparseHoles(path, length);
            RunEfsJob(jobs, path, StorageAction.DecryptEfs, changed: false, timeout);

            TestContext.Current.TestOutputHelper!.WriteLine(
                $"large sparse main allocation before={first.Main.Allocation} encrypted={encrypted.Main.Allocation} decrypted={decrypted.Main.Allocation}; " +
                $"named before={first.Named.Allocation} encrypted={encrypted.Named.Allocation} decrypted={decrypted.Named.Allocation}");
        }
        finally
        {
            Assert.StartsWith(Path.GetFullPath(root!) + Path.DirectorySeparatorChar,
                Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WriteLargeSparseSentinel(FileStream stream, long offset, int seed)
    {
        byte[] sentinel = new byte[4096];
        new Random(seed).NextBytes(sentinel);
        stream.Position = offset;
        stream.Write(sentinel);
    }

    private static void VerifyLargeSparseHoles(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Assert.Equal(length, stream.Length);
        byte[] zero = new byte[4096];
        byte[] actual = new byte[4096];
        foreach (long offset in new[] { 0L, 1L << 30, (3L << 30) + 4096, length - (1L << 20) })
        {
            stream.Position = offset;
            stream.ReadExactly(actual);
            Assert.Equal(zero, actual);
        }
    }
}
