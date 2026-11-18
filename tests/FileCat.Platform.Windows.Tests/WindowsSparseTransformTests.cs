using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>Disposable NTFS behavior fixtures. These operations do not exercise FileCat release admission.</summary>
[Collection("Windows storage job evidence")]
public sealed unsafe partial class WindowsSparseTransformTests
{
    private const int Length = 4 * 1024 * 1024;
    private const string NamedStream = ":fixture";
    private const uint SetSparse = 0x000900C4;
    private const uint QueryAllocatedRanges = 0x000940CF;

    [Fact]
    public void Reviewed_hex_save_as_keeps_sparse_ranges_in_an_approved_clear_copy()
    {
        RequireFixture();
        Assert.SkipUnless(Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1", "Requires the explicit EFS fixture opt-in.");
        Run("main-holes", (path, before) =>
        {
            File.Encrypt(path);
            byte[] expected = File.ReadAllBytes(path);
            expected[64 * 1024] = 0x6d;
            string destination = Path.Join(Path.GetDirectoryName(path)!, "copy.bin");
            using var source = new ProtectedHexFile(path);
            using var overlay = new HexPatchOverlay(source);
            overlay.Write(64 * 1024, [0x6d]);
            using var review = HexSaveAs.Prepare(source, overlay, destination);
            Assert.True(review.NeedsEfsConsent);
            review.ApproveEfsExport();
            var result = HexSaveAs.CreateNew(review, TestContext.Current.CancellationToken);
            Assert.True(result.Sparse);
            Assert.InRange(result.BytesWritten, 1, Length / 2);
            using var copy = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read);
            var observed = ObserveStream(copy);
            Assert.Equal(Length, observed.Length);
            Assert.Equal(before.Main.Ranges, observed.Ranges);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)), observed.Digest);
            Assert.False(observed.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Encrypted));
        });
    }

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Efs_native_actions_preserve_identity_and_logical_bytes_in_bounded_sparse_fixtures(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var encrypted = Report("encrypt", TryEfs(() => File.Encrypt(path)), path, first);
            Assert.True(encrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, encrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, encrypted.Named.Allocation);
            var decrypted = Report("decrypt", TryEfs(() => File.Decrypt(path)), path, first);
            Assert.False(decrypted.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, decrypted.Main.Allocation);
            Assert.Equal(first.Named.Allocation, decrypted.Named.Allocation);
        });
    }

    [Theory]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Compression_native_actions_preserve_identity_and_logical_bytes_in_bounded_sparse_fixtures(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var compressed = Report("compress-main", InvokeCompression(path, StorageAction.Compress), path, first);
            Assert.Equal(WindowsCompression.Lznt1, compressed.Main.Compression);
            Assert.Equal(first.Named.Compression, compressed.Named.Compression);
            Assert.Equal(first.Named.Allocation, compressed.Named.Allocation);
            var decompressed = Report("remove-main-compression", InvokeCompression(path, StorageAction.RemoveCompression), path, first);
            Assert.Equal(WindowsCompression.None, decompressed.Main.Compression);
            Assert.Equal(first.Main.Allocation, decompressed.Main.Allocation);
            Assert.Equal(first.Named.Compression, decompressed.Named.Compression);
            Assert.Equal(first.Named.Allocation, decompressed.Named.Allocation);
        });
    }

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Reviewed_compression_jobs_preserve_sparse_streams_and_do_not_call_native_for_noops(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var jobs = CreateManager(path);
            RunJob(jobs, path, StorageAction.Compress, changed: true);
            var compressed = Report("reviewed compress", "changed", path, first);
            Assert.Equal(WindowsCompression.Lznt1, compressed.Main.Compression);
            Assert.Equal(first.Named.Allocation, compressed.Named.Allocation);
            Assert.Equal(first.Named.Compression, compressed.Named.Compression);
            RunJob(jobs, path, StorageAction.Compress, changed: false);

            RunJob(jobs, path, StorageAction.RemoveCompression, changed: true);
            var decompressed = Report("reviewed decompress", "changed", path, first);
            Assert.Equal(WindowsCompression.None, decompressed.Main.Compression);
            Assert.Equal(first.Main.Allocation, decompressed.Main.Allocation);
            Assert.Equal(first.Named.Allocation, decompressed.Named.Allocation);
            Assert.Equal(first.Named.Compression, decompressed.Named.Compression);
            RunJob(jobs, path, StorageAction.RemoveCompression, changed: false);
        });
    }

    [Theory]
    [InlineData("all-hole")]
    [InlineData("main-holes")]
    [InlineData("named-holes")]
    [InlineData("both-holes")]
    [InlineData("sparse-no-holes")]
    public void Reviewed_efs_binding_preserves_sparse_main_and_named_layouts(string shape)
    {
        RequireFixture();
        Run(shape, (path, first) =>
        {
            var encrypted = ApplyReviewedEfs(path, StorageAction.EncryptEfs);
            Assert.Equal(StorageEffect.Changed, encrypted.Effect);
            Assert.True(encrypted.NativeCallAttempted);
            var enabled = Report("reviewed encrypt", "changed", path, first);
            Assert.True(enabled.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, enabled.Main.Allocation);
            Assert.Equal(first.Named.Allocation, enabled.Named.Allocation);
            var encryptNoop = ApplyReviewedEfs(path, StorageAction.EncryptEfs);
            Assert.Equal(StorageEffect.AlreadySatisfied, encryptNoop.Effect);
            Assert.False(encryptNoop.NativeCallAttempted);

            var decrypted = ApplyReviewedEfs(path, StorageAction.DecryptEfs);
            Assert.Equal(StorageEffect.Changed, decrypted.Effect);
            Assert.True(decrypted.NativeCallAttempted);
            var disabled = Report("reviewed decrypt", "changed", path, first);
            Assert.False(disabled.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Allocation, disabled.Main.Allocation);
            Assert.Equal(first.Named.Allocation, disabled.Named.Allocation);
            var decryptNoop = ApplyReviewedEfs(path, StorageAction.DecryptEfs);
            Assert.Equal(StorageEffect.AlreadySatisfied, decryptNoop.Effect);
            Assert.False(decryptNoop.NativeCallAttempted);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Named_sparse_layout_growth_before_or_during_the_call_is_never_reported_as_success(bool duringCall)
    {
        RequireFixture();
        Run("named-holes", (path, first) =>
        {
            void GrowNamedStream()
            {
                using var named = new FileStream(path + NamedStream, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                named.Position = 1024 * 1024;
                named.WriteByte(0x5A);
                named.Flush(flushToDisk: true);
            }
            var outcome = ApplyReviewedEfs(path, StorageAction.EncryptEfs,
                afterPrepare: duringCall ? null : GrowNamedStream, afterCall: duringCall ? GrowNamedStream : null);
            Assert.Equal(duringCall ? StorageEffect.Uncertain : StorageEffect.NotProcessed, outcome.Effect);
            Assert.Equal(duringCall ? "efs-sparse-postcondition-changed" : "efs-sparse-preparation-changed", outcome.Problem?.Code);
            Assert.Equal(duringCall, outcome.NativeCallAttempted);
            var after = Observe(path);
            Assert.Equal(duringCall, after.Attributes.HasFlag(FileAttributes.Encrypted));
            Assert.Equal(first.Main.Digest, after.Main.Digest);
            Assert.NotEqual(first.Named.Digest, after.Named.Digest);
        });
    }

    private static StorageMutationResult ApplyReviewedEfs(string path, StorageAction action, Action? afterPrepare = null,
        Action? afterCall = null)
    {
        var ct = TestContext.Current.CancellationToken;
        var adapter = new WindowsStorageTransforms();
        var target = StoragePaths.Capture([ItemRef.ForFileSystemPath(path, EntryKind.File)])[0];
        using var reserve = StorageReviewResources.ReservePins(1);
        using var native = StorageReviewResources.EnterNative(ct);
        IStorageReviewLease pin;
        using (StorageReviewResources.EnterRootInspection(target))
            pin = adapter.InspectEfsRoot(target, action, ct);
        using (pin)
        {
            var consent = StorageEfsConsent.Capture(Guid.NewGuid(), action, StorageScope.SelectedItems,
                [pin.Observation.Identity], new(true, action == StorageAction.EncryptEfs));
            using var held = adapter.OpenEfsPathForQualification(pin, consent, ct, afterCall is null ? null : (boundPath, observedAction) =>
            {
                Assert.Equal(StorageAction.EncryptEfs, observedAction);
                File.Encrypt(boundPath);
                afterCall();
                return new(true, 0);
            });
            Assert.Null(held.PrepareMutation(action, ct).Ineligible);
            afterPrepare?.Invoke();
            var result = held.ApplyAdmitted(action);
            TestContext.Current.TestOutputHelper!.WriteLine($"reviewed {action} -> {result.Effect} {result.Problem}");
            return result;
        }
    }

    [Fact]
    public void A_sparse_range_overflow_refuses_before_a_native_call_while_a_noop_requires_no_inventory()
    {
        RequireFixture();
        const int stride = 128 * 1024;
        string root = Directory.CreateTempSubdirectory("filecat-sparse-fragmented-").FullName;
        string path = Path.Join(root, "fragmented.bin");
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(Control(stream.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0));
                stream.SetLength((long)(WindowsStorageTransforms.SparseRangeLimit + 1) * stride);
                for (int i = 0; i <= WindowsStorageTransforms.SparseRangeLimit; i++)
                {
                    TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
                    stream.Position = (long)i * stride;
                    stream.WriteByte(0x39);
                }
                stream.Flush(flushToDisk: true);
            }
            var jobs = CreateManager(path);
            RunJob(jobs, path, StorageAction.RemoveCompression, changed: false);
            var refused = Submit(jobs, path, StorageAction.Compress);
            Assert.Equal(JobState.Failed, refused.State);
            Assert.Equal(1, refused.StorageResult!.Counts.Failed);
            Assert.Contains(refused.StorageResult.Reasons, r => r.Problem.Code == "sparse-range-limit");
            AssertCallEvidence(jobs, refused, changed: false);
            using var held = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Assert.Equal(WindowsCompression.None, WindowsCompression.Query(held).Native);
            Assert.True(File.GetAttributes(held).HasFlag(FileAttributes.SparseFile));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static JobManager CreateManager(string path) =>
        new(new PortableFileOperations(), new ProviderRegistry(), Directory.CreateDirectory(
            Path.Join(Path.GetDirectoryName(path)!, "journal")).FullName, ArchiveJobBackends.Empty,
            new WindowsStorageTransforms(), WindowsStorageReviewEvidence.CreateJournal, WindowsStorageReviewEvidence.Backend);

    private static Job Submit(JobManager jobs, string path, StorageAction action)
    {
        var ct = TestContext.Current.CancellationToken;
        using var review = jobs.PrepareStorageReview([ItemRef.ForFileSystemPath(path, EntryKind.File)], action, ct);
        var selection = review.Approve(action, StorageScope.SelectedItems);
        using var finished = new ManualResetEventSlim();
        void OnFinished(Job _) => finished.Set();
        jobs.JobFinished += OnFinished;
        try
        {
            var job = jobs.SubmitStorage(review, selection, _ => { });
            Assert.True(finished.Wait(TimeSpan.FromSeconds(45), ct));
            return job;
        }
        finally { jobs.JobFinished -= OnFinished; }
    }

    private static void RunJob(JobManager jobs, string path, StorageAction action, bool changed)
    {
        var job = Submit(jobs, path, action);
        Assert.True(job.State == JobState.Completed, $"{job.State}: " + string.Join(", ",
            job.StorageResult!.Reasons.Select(r => r.Problem.Message).Concat(job.Issues.Select(i => i.Message))));
        Assert.Equal(changed ? 1 : 0, job.StorageResult!.Counts.Changed);
        Assert.Equal(changed ? 0 : 1, job.StorageResult.Counts.AlreadySatisfied);
        AssertCallEvidence(jobs, job, changed);
    }

    private static void AssertCallEvidence(JobManager jobs, Job job, bool changed)
    {
        var report = jobs.ReadStorageReview(StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), TestContext.Current.CancellationToken);
        Assert.False(report.ReviewPending);
        Assert.True(report.Current.History.Ended);
        Assert.Equal(changed, report.Current.History.LastCall is not null);
    }

    private static void RequireFixture()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost &&
            Environment.GetEnvironmentVariable("FILECAT_TEST_SPARSE_TRANSFORMS") == "1",
            "Requires an explicitly prepared disposable NTFS volume in the Windows 11 VM.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }

    private static void Run(string shape, Action<string, Observation> transform)
    {
        string directory = Directory.CreateTempSubdirectory("filecat-sparse-transforms-").FullName;
        string path = Path.Join(directory, "fixture.bin");
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(directory)!);
            Assert.Equal("NTFS", drive.DriveFormat);
            File.WriteAllBytes(path, []);
            WriteStream(path, shape != "named-holes", shape is "all-hole" or "main-holes" or "both-holes",
                allHole: shape == "all-hole", seed: 17);
            WriteStream(path + NamedStream, shape is "named-holes" or "both-holes", shape is "named-holes" or "both-holes",
                allHole: false, seed: 29);
            var first = Observe(path);
            if (shape != "named-holes") Assert.True(first.Attributes.HasFlag(FileAttributes.SparseFile));
            Assert.Equal(Length, first.Main.Length);
            Assert.Equal(Length, first.Named.Length);
            if (shape == "all-hole") Assert.Empty(first.Main.Ranges);
            if (shape is "main-holes" or "both-holes") Assert.True(first.Main.Ranges.Sum(range => range.Length) < Length / 2);
            if (shape is "named-holes" or "both-holes") Assert.True(first.Named.Ranges.Sum(range => range.Length) < Length / 2);
            if (shape == "sparse-no-holes") Assert.Equal(Length, first.Main.Ranges.Sum(range => range.Length));
            Assert.True(GetDiskFreeSpace(drive.RootDirectory.FullName, out uint sectors, out uint bytes, out _, out _));
            TestContext.Current.TestOutputHelper!.WriteLine($"{shape} NTFS {RuntimeInformation.ProcessArchitecture} allocationUnit={checked(sectors * bytes)}");
            Write("before", "observed", first);

            transform(path, first);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void WriteStream(string streamPath, bool sparse, bool holes, bool allHole, int seed)
    {
        using var stream = new FileStream(streamPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (sparse) Assert.True(Control(stream.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0),
            $"Setting the fixture sparse failed with {Marshal.GetLastPInvokeError()}.");
        stream.SetLength(Length);
        if (holes)
        {
            if (!allHole)
            {
                stream.Position = 64 * 1024;
                stream.WriteByte((byte)seed);
                stream.Position = 2 * 1024 * 1024;
                stream.WriteByte((byte)(seed + 1));
            }
        }
        else
        {
            byte[] block = new byte[64 * 1024];
            new Random(seed).NextBytes(block);
            while (stream.Position < Length) stream.Write(block);
        }
        stream.Flush(flushToDisk: true);
    }

    private static string TryEfs(Action call)
    {
        try { call(); return "success"; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return $"failure 0x{error.HResult:X8} {error.Message}"; }
    }

    private static string InvokeCompression(string path, StorageAction action)
    {
        using var file = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var result = WindowsCompression.InvokeAdmitted(file, action);
        return $"success={result.NativeSucceeded} error={result.NativeError} format={result.After.Native}";
    }

    private static Observation Report(string phase, string result, string path, Observation first)
    {
        var observed = Observe(path);
        Write(phase, result, observed);
        Assert.Equal(first.Identity, observed.Identity);
        Assert.Equal(first.Main.Digest, observed.Main.Digest);
        Assert.Equal(first.Named.Digest, observed.Named.Digest);
        Assert.Equal(first.Main.Length, observed.Main.Length);
        Assert.Equal(first.Named.Length, observed.Named.Length);
        Assert.Equal(first.Main.Ranges, observed.Main.Ranges);
        Assert.Equal(first.Named.Ranges, observed.Named.Ranges);
        Assert.Equal(first.Main.Attributes & FileAttributes.SparseFile, observed.Main.Attributes & FileAttributes.SparseFile);
        Assert.Equal(first.Named.Attributes & FileAttributes.SparseFile, observed.Named.Attributes & FileAttributes.SparseFile);
        return observed;
    }

    private static void Write(string phase, string result, Observation observation)
    {
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"{phase} {result} attributes={observation.Attributes} id={observation.Identity}");
        WriteStreamObservation("main", observation.Main);
        WriteStreamObservation("named", observation.Named);
    }

    private static void WriteStreamObservation(string name, StreamObservation observation) =>
        TestContext.Current.TestOutputHelper!.WriteLine(
            $"  {name} eof={observation.Length} allocation={observation.Allocation} attributes={observation.Attributes} compression={observation.Compression} " +
            $"ranges={string.Join(",", observation.Ranges.Select(r => $"{r.Offset}+{r.Length}"))} sha256={observation.Digest}");

    private static Observation Observe(string path)
    {
        using var main = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var named = new FileStream(path + NamedStream, FileMode.Open, FileAccess.Read, FileShare.Read);
        var identity = ProtectedHexFile.Identity(main.SafeFileHandle);
        Assert.Equal(identity, ProtectedHexFile.Identity(named.SafeFileHandle));
        return new(identity, File.GetAttributes(main.SafeFileHandle), ObserveStream(main), ObserveStream(named));
    }

    private static StreamObservation ObserveStream(FileStream stream)
    {
        var input = new Range(0, stream.Length);
        var output = stackalloc Range[32];
        uint rangeSize = (uint)sizeof(Range);
        Assert.True(Control(stream.SafeFileHandle, QueryAllocatedRanges, &input, rangeSize, output, 32 * rangeSize, out uint bytes, 0),
            $"Allocated-range query failed with {Marshal.GetLastPInvokeError()}.");
        Assert.True(bytes <= 32 * sizeof(Range) && bytes % sizeof(Range) == 0);
        var ranges = new ReadOnlySpan<Range>(output, (int)(bytes / sizeof(Range))).ToArray();
        long last = 0;
        foreach (var range in ranges)
        {
            Assert.True(range.Offset >= last && range.Length > 0 && checked(range.Offset + range.Length) <= input.Length);
            last = range.Offset + range.Length;
        }
        Assert.True(GetInfo(stream.SafeFileHandle, 1, out StandardInfo standard, (uint)sizeof(StandardInfo)));
        Assert.Equal(stream.Length, standard.EndOfFile);
        return new(stream.Length, standard.AllocationSize, File.GetAttributes(stream.SafeFileHandle), WindowsCompression.Query(stream.SafeFileHandle).Native,
            ranges, Convert.ToHexString(SHA256.HashData(stream)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Range(long Offset, long Length);

    [StructLayout(LayoutKind.Sequential)]
    private struct StandardInfo
    {
        public long AllocationSize, EndOfFile;
        public uint NumberOfLinks;
        public byte DeletePending, Directory;
    }

    private sealed record StreamObservation(long Length, long Allocation, FileAttributes Attributes, ushort? Compression, Range[] Ranges, string Digest);
    private sealed record Observation(FileIdentity Identity, FileAttributes Attributes, StreamObservation Main, StreamObservation Named);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Control(SafeFileHandle file, uint code, void* input, uint inputBytes, void* output,
        uint outputBytes, out uint returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetInfo(SafeFileHandle file, int informationClass, out StandardInfo result, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector,
        out uint freeClusters, out uint totalClusters);
}
