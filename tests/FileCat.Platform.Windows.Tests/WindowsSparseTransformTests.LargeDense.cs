using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsSparseTransformTests
{
    [Fact]
    public void Reviewed_removal_above_the_enable_limit_preserves_dense_NTFS_streams()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_NTFS_DENSE_ROOT");
        Assert.SkipUnless(OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(root),
            "Requires a separately prepared disposable 36 GiB NTFS fixture.");
        Assert.True(WindowsStorageTransforms.SupportsHost(WindowsStorageAbiTests.QualifyNativeArm64Jobs));
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        var drive = new DriveInfo(root!);
        Assert.Equal(drive.RootDirectory.FullName, Path.GetFullPath(root!), ignoreCase: true);
        Assert.Equal(DriveType.Fixed, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal("FCATDENSE", drive.VolumeLabel);
        Assert.InRange(drive.TotalSize, 35L << 30, 36L << 30);
        const long length = 30_000_000_001L;
        Assert.True(drive.AvailableFreeSpace >= length + (2L << 30));
        Assert.True(GetDiskFreeSpace(drive.RootDirectory.FullName, out uint sectors, out uint bytes, out _, out _));
        Assert.Equal(4096u, checked(sectors * bytes));

        string directory = Directory.CreateDirectory(Path.Join(root, "filecat-dense-" + Guid.NewGuid().ToString("N"))).FullName;
        string evidence = Directory.CreateTempSubdirectory("filecat-dense-evidence-").FullName;
        string path = Path.Join(directory, "dense.bin");
        var ct = TestContext.Current.CancellationToken;
        byte[] namedPayload = "dense fixture named stream"u8.ToArray();
        try
        {
            string expected;
            FileIdentity identity;
            using (var data = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                var enabled = WindowsCompression.InvokeAdmitted(data.SafeFileHandle, StorageAction.Compress);
                Assert.True(enabled.NativeSucceeded, $"The empty test stream could not be compressed: {enabled.NativeError}.");
                identity = ProtectedHexFile.Identity(data.SafeFileHandle);
                expected = WriteLargeDenseBytes(data, length, ct);
                Assert.Equal(length, data.Length);
                Assert.Equal(WindowsCompression.Lznt1, WindowsCompression.Query(data.SafeFileHandle).Native);
                Assert.False(File.GetAttributes(data.SafeFileHandle).HasFlag(FileAttributes.SparseFile));
            }
            File.WriteAllBytes(path + NamedStream, namedPayload);
            TestContext.Current.TestOutputHelper!.WriteLine($"before eof={length} id={identity} sha256={expected}");

            var jobs = CreateManager(path, evidence);
            const StorageAction action = StorageAction.RemoveCompression;
            var job = Submit(jobs, path, action, TimeSpan.FromMinutes(12));
            Assert.True(job.State == JobState.Completed,
                $"{job.State}: {JobIssue.Summarize(job.Issues)} " +
                string.Join(", ", job.StorageResult!.Reasons.Select(reason => reason.Problem.Message)));
            Assert.Equal(1, job.StorageResult!.Counts.Changed);
            var report = jobs.ReadStorageReview(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), ct);
            Assert.True(report.Current.History.Ended);
            Assert.False(report.ReviewPending);
            var call = Assert.IsType<StorageHistoryCall>(report.Current.History.LastCall);
            Assert.Equal(StorageCallDisposition.Observed, call.Disposition);
            var result = Assert.IsType<StorageMutationResult>(call.Result);
            Assert.True(result.NativeCallAttempted);
            Assert.Equal(StorageEffect.Changed, result.Effect);
            Assert.Equal(StorageObservationState.Enabled, call.Before.Compression);
            Assert.Equal(StorageObservationState.Disabled, result.After?.Compression);

            using var main = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var named = new FileStream(path + NamedStream, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(identity, ProtectedHexFile.Identity(main.SafeFileHandle));
            Assert.Equal(identity, ProtectedHexFile.Identity(named.SafeFileHandle));
            Assert.Equal(WindowsCompression.None, WindowsCompression.Query(main.SafeFileHandle).Native);
            Assert.False(File.GetAttributes(main.SafeFileHandle).HasFlag(FileAttributes.SparseFile));
            Assert.Equal(length, main.Length);
            string after = Convert.ToHexString(SHA256.HashData(main));
            Assert.Equal(expected, after);
            Assert.Equal(namedPayload.Length, named.Length);
            byte[] reread = new byte[namedPayload.Length];
            named.ReadExactly(reread);
            Assert.Equal(namedPayload, reread);
            TestContext.Current.TestOutputHelper!.WriteLine($"after eof={main.Length} id={identity} sha256={after}; original named stream verified");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(evidence, recursive: true);
        }
    }

    private static string WriteLargeDenseBytes(FileStream data, long length, CancellationToken ct)
    {
        byte[] buffer = new byte[4 * 1024 * 1024];
        Span<byte> pattern = buffer.AsSpan(0, 1024);
        new Random(167).NextBytes(pattern);
        for (int index = pattern.Length; index < buffer.Length; index += pattern.Length)
            pattern.CopyTo(buffer.AsSpan(index));
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (long offset = 0; offset < length; offset += buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, length - offset);
            BinaryPrimitives.WriteInt64LittleEndian(buffer, offset);
            data.Write(buffer.AsSpan(0, count));
            digest.AppendData(buffer, 0, count);
        }
        data.Flush(flushToDisk: true);
        return Convert.ToHexString(digest.GetHashAndReset());
    }
}
