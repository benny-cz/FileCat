using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsSparseTransformTests
{
    [Fact]
    public void Dense_NTFS_volume_admits_and_removes_a_small_compressed_file()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_NTFS_DENSE_ROOT");
        Assert.SkipUnless(OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(root),
            "Requires the disposable dense NTFS volume.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        string directory = Directory.CreateDirectory(Path.Join(root!, "filecat-dense-preflight-" + Guid.NewGuid().ToString("N"))).FullName;
        string evidence = Directory.CreateDirectory(Path.Join(root!,
            "filecat-dense-preflight-evidence-" + Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Join(directory, "small.bin");
        try
        {
            using (var data = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                Assert.True(WindowsCompression.InvokeAdmitted(data.SafeFileHandle, StorageAction.Compress).NativeSucceeded);
                WriteLargeDenseBytes(data, 1L << 20, TestContext.Current.CancellationToken);
                Assert.Equal(WindowsCompression.Lznt1, WindowsCompression.Query(data.SafeFileHandle).Native);
            }
            PrivateDenseEvidence(evidence);
            var jobs = CreateManager(path, evidence);
            using (var review = jobs.PrepareStorageReview([ItemRef.ForFileSystemPath(path, EntryKind.File)],
                StorageAction.RemoveCompression, TestContext.Current.CancellationToken))
            {
                var observation = Assert.Single(review.Roots).Observation;
                Assert.True(observation.Ineligible is null,
                    $"The prepared dense volume refused removal: {observation.Ineligible?.Code}: {observation.Ineligible?.Message}");
                Assert.Equal(StorageObservationState.Enabled, observation.Compression);
            }
            RunJob(jobs, path, StorageAction.RemoveCompression, changed: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
            Directory.Delete(evidence, recursive: true);
        }
    }

    [Fact]
    public void Reviewed_removal_above_the_enable_limit_preserves_dense_NTFS_streams()
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_NTFS_DENSE_ROOT");
        Assert.SkipUnless(OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(root),
            "Requires a separately prepared disposable 76 GiB NTFS fixture.");
        Assert.True(WindowsStorageTransforms.SupportsHost(WindowsStorageAbiTests.QualifyNativeArm64Jobs));
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
        var drive = new DriveInfo(root!);
        Assert.Equal(drive.RootDirectory.FullName, Path.GetFullPath(root!), ignoreCase: true);
        Assert.Equal(DriveType.Fixed, drive.DriveType);
        Assert.Equal("NTFS", drive.DriveFormat);
        Assert.Equal("FCATDENSE", drive.VolumeLabel);
        Assert.InRange(drive.TotalSize, 74L << 30, 78L << 30);
        const long length = 30_000_000_001L;
        // NTFS reserves the compressed stream's logical EOF in addition to its physical allocation.
        // The removal call can need space for the 30 GB uncompressed result before reclaiming it.
        Assert.True(drive.AvailableFreeSpace >= length + (35L << 30));
        Assert.True(GetDiskFreeSpace(drive.RootDirectory.FullName, out uint sectors, out uint bytes, out _, out _));
        Assert.Equal(4096u, checked(sectors * bytes));

        string directory = Directory.CreateDirectory(Path.Join(root, "filecat-dense-" + Guid.NewGuid().ToString("N"))).FullName;
        string evidence = Directory.CreateDirectory(Path.Join(root!,
            "filecat-dense-evidence-" + Guid.NewGuid().ToString("N"))).FullName;
        string path = Path.Join(directory, "dense.bin");
        var ct = TestContext.Current.CancellationToken;
        byte[] namedPayload = "dense fixture named stream"u8.ToArray();
        JobManager? jobs = null;
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
            WindowsCompression.Format namedCompression;
            using (var originalNamed = new FileStream(path + NamedStream, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read))
            {
                originalNamed.Write(namedPayload);
                originalNamed.Flush(flushToDisk: true);
                Assert.Equal(identity, ProtectedHexFile.Identity(originalNamed.SafeFileHandle));
                namedCompression = WindowsCompression.Query(originalNamed.SafeFileHandle);
                Assert.True(namedCompression.State is StorageObservationState.Enabled or StorageObservationState.Disabled,
                    $"The original named stream compression could not be observed: {namedCompression.Error}.");
            }
            TestContext.Current.TestOutputHelper!.WriteLine($"before eof={length} id={identity} sha256={expected} named compression={namedCompression.Native}");

            PrivateDenseEvidence(evidence);
            jobs = CreateManager(path, evidence);
            const StorageAction action = StorageAction.RemoveCompression;
            TestContext.Current.TestOutputHelper!.WriteLine($"dense removal submitted: {DateTimeOffset.UtcNow:O}");
            var job = Submit(jobs, path, action, TimeSpan.FromMinutes(45));
            TestContext.Current.TestOutputHelper!.WriteLine($"dense removal finished: {DateTimeOffset.UtcNow:O} state={job.State}");
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
            Assert.Equal(namedCompression, WindowsCompression.Query(named.SafeFileHandle));
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
            if (jobs?.HasActiveWork == true)
            {
                // A timed-out native call can still hold the stream. The enclosing
                // disposable-VHDX controller owns teardown after the test host exits.
                TestContext.Current.TestOutputHelper!.WriteLine("dense removal still active; deferring fixture cleanup to VHDX teardown");
            }
            else
            {
                Directory.Delete(directory, recursive: true);
                Directory.Delete(evidence, recursive: true);
            }
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
        var volume = new DriveInfo(Path.GetPathRoot(data.Name)!);
        for (long offset = 0; offset < length; offset += buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            int count = (int)Math.Min(buffer.Length, length - offset);
            BinaryPrimitives.WriteInt64LittleEndian(buffer, offset);
            try
            {
                data.Write(buffer.AsSpan(0, count));
            }
            catch (IOException error)
            {
                WriteDenseProgress(data, volume, $"write failed: {error.Message}");
                throw;
            }
            digest.AppendData(buffer, 0, count);
            if (data.Position % (512L << 20) == 0)
            {
                data.Flush(flushToDisk: true);
                WriteDenseProgress(data, volume, "flushed");
            }
        }
        data.Flush(flushToDisk: true);
        WriteDenseProgress(data, volume, "seed complete");
        return Convert.ToHexString(digest.GetHashAndReset());
    }

    private static unsafe void WriteDenseProgress(FileStream data, DriveInfo volume, string stage)
    {
        long allocation = GetInfo(data.SafeFileHandle, 1, out StandardInfo standard, (uint)sizeof(StandardInfo))
            ? standard.AllocationSize : -1;
        string message = $"dense {stage}: position={data.Position} eof={data.Length} allocation={allocation} available={volume.AvailableFreeSpace}";
        TestContext.Current.TestOutputHelper!.WriteLine(message);
        string? progressPath = Environment.GetEnvironmentVariable("FILECAT_TEST_NTFS_DENSE_PROGRESS");
        if (!string.IsNullOrWhiteSpace(progressPath))
        {
            // A cancelled hosted job may have no TRX. Keep best-effort phase evidence
            // on the runner's host volume without making the storage test depend on it.
            try { File.AppendAllText(progressPath, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}"); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void PrivateDenseEvidence(string evidence)
    {
        string journal = Directory.CreateDirectory(Path.Join(evidence, "journal")).FullName;
        using var identity = WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? throw new InvalidOperationException("The fixture identity is unavailable.");
        var security = new DirectorySecurity();
        security.SetSecurityDescriptorSddlForm($"O:{sid}D:P(A;OICI;FA;;;{sid})(A;OICI;FA;;;SY)",
            AccessControlSections.Owner | AccessControlSections.Access);
        new DirectoryInfo(journal).SetAccessControl(security);
    }
}
