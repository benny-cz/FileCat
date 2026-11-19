using System.IO.Compression;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsArchiveEfsPackTests
{
    [Theory]
    [InlineData("file", true)]
    [InlineData("nested file", true)]
    [InlineData("folder default", true)]
    [InlineData("file", false)]
    [InlineData("nested file", false)]
    [InlineData("folder default", false)]
    [InlineData("sparse file", true)]
    [InlineData("sparse file", false)]
    public void Pack_refuses_EFS_before_loading_an_input_or_creating_a_stage(string selection, bool native)
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-efs-pack-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "selected")).FullName;
            string source = Path.Join(folder, "protected.txt"), target = Path.Join(root, "output.zip");
            File.WriteAllText(source, "original protected content");
            if (selection == "sparse file") MakeSparse(source);
            string encrypted = selection == "folder default" ? folder : source;
            File.Encrypt(encrypted);
            if (selection == "sparse file") Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.SparseFile));
            byte[] expected = File.ReadAllBytes(source);
            bool direct = selection is "file" or "sparse file";
            var plan = new ArchivePlan(target, null,
                [new(direct ? ArchiveChangeKind.AddFile : ArchiveChangeKind.AddFolder, "selected", direct ? source : folder)],
                Profile: native ? new ArchiveWriteProfile(ArchiveWriteFormat.Zip) : null);
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "EFS pack fixture", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);
            var files = new WindowsFileOperations();
            var problem = Assert.Throws<ArchiveWriteException>(() =>
            {
                if (native)
                {
                    var backend = WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Zip, Path.Join(root, "unused-worker.exe"));
                    backend.Execute(new(job, plan, null, files, journal));
                }
                else new ZipUpdateExecutor(job, files, journal).Execute();
            });
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, problem.Reason);
            Assert.Equal(ArchiveOuterEfs.PackUnavailable, problem.Message);
            Assert.Empty(Directory.EnumerateFiles(root));
            Assert.True(File.GetAttributes(encrypted).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(expected, File.ReadAllBytes(source));
            Assert.DoesNotContain(job.Issues, issue => issue.Outcome == StepOutcome.Committed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Sparse_input_packing_preserves_logical_bytes_and_discloses_extraction_space(bool native, bool move)
    {
        RequireFixture();
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
        if (native) Assert.SkipUnless(!string.IsNullOrWhiteSpace(assets), "Requires the prepared native archive application worker.");
        string root = Directory.CreateTempSubdirectory("filecat-sparse-pack-").FullName;
        try
        {
            string source = Path.Join(root, "input.bin"), ordinary = Path.Join(root, "ordinary.bin"), target = Path.Join(root, "output.zip");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            MakeSparse(source);
            byte[] expected = File.ReadAllBytes(source);
            byte[] ordinaryBytes = [5, 6, 7, 8];
            File.WriteAllBytes(ordinary, ordinaryBytes);
            var plan = new ArchivePlan(target, null,
                [new(ArchiveChangeKind.AddFile, "input.bin", source), new(ArchiveChangeKind.AddFile, "ordinary.bin", ordinary)],
                Profile: native ? new ArchiveWriteProfile(ArchiveWriteFormat.Zip) : null) { MoveSources = move };
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File), ItemRef.ForFileSystemPath(ordinary, EntryKind.File)] },
                "Sparse packing fixture", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);
            var files = new WindowsFileOperations();
            if (native)
            {
                ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
                var backend = WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Zip, Path.Join(assets!, "FileCat.exe"));
                backend.Execute(new(job, plan, null, files, journal));
            }
            else new ZipUpdateExecutor(job, files, journal).Execute();

            var notice = Assert.Single(job.Issues, issue => issue.Message.Contains("Extracting them may use more disk space", StringComparison.Ordinal));
            Assert.Equal(StepOutcome.Committed, notice.Outcome);
            Assert.Equal(target, notice.TargetPath);
            Assert.Equal(!move, File.Exists(source));
            Assert.Equal(!move, File.Exists(ordinary));
            if (!move) Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.SparseFile));
            using var zip = ZipFile.OpenRead(target);
            Assert.Equal(2, zip.Entries.Count);
            Check("input.bin", expected);
            Check("ordinary.bin", ordinaryBytes);

            void Check(string member, byte[] bytes)
            {
                var entry = zip.GetEntry(member)!;
                Assert.Equal(bytes.LongLength, entry.Length);
                using var input = entry.Open();
                using var decoded = new MemoryStream();
                input.CopyTo(decoded);
                Assert.Equal(bytes, decoded.ToArray());
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void MakeSparse(string path)
    {
        using var file = File.OpenHandle(path, access: FileAccess.ReadWrite);
        Assert.True(SetSparse(file, 0x000900c4 /* FSCTL_SET_SPARSE */, 0, 0, 0, 0, out _, 0));
        RandomAccess.SetLength(file, 4 * 1024 * 1024);
        RandomAccess.Write(file, [0x6d], 2 * 1024 * 1024);
        var layout = WindowsStorageTransforms.ReadSparseLayout(file, TestContext.Current.CancellationToken);
        Assert.NotNull(layout);
        Assert.True(layout.Ranges.Sum(range => range.Length) < layout.Length / 2);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetSparse(SafeFileHandle file, uint operation, nint input, uint inputBytes,
        nint output, uint outputBytes, out uint written, nint overlapped);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_pack_and_move_stop_when_the_actual_source_is_encrypted_after_initial_capture(bool move)
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-efs-legacy-pack-drift-").FullName;
        try
        {
            string source = Path.Join(root, "input.bin"), target = Path.Join(root, "output.zip");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            const string mark = "[ZoneTransfer]\r\nZoneId=3\r\n";
            File.WriteAllText(source + ":Zone.Identifier", mark);
            var plan = new ArchivePlan(target, null, [new(ArchiveChangeKind.AddFile, "selected", source)]) { MoveSources = move };
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)] }, "EFS packing drift fixture", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);
            var files = new EncryptDuringPack(source, beforeMark: move);
            var error = Assert.Throws<ArchiveWriteException>(() => new ZipUpdateExecutor(job, files, journal).Execute());
            Assert.Equal(ArchiveOuterEfs.PackUnavailable, error.Message);
            Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(source));
            Assert.Equal(mark, File.ReadAllText(source + ":Zone.Identifier"));
            Assert.Equal([source], Directory.GetFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class EncryptDuringPack(string source, bool beforeMark) : WindowsFileOperations
    {
        public override IDisposable HoldRegularFile(string path, string? root = null)
        {
            var held = base.HoldRegularFile(path, root);
            try { if (!beforeMark && path == source) File.Encrypt(source); return held; }
            catch { held.Dispose(); throw; }
        }

        public override string? ReadArchiveSourceOriginMark(string path, string? identity, CancellationToken ct)
        {
            if (beforeMark && path == source) File.Encrypt(source);
            return base.ReadArchiveSourceOriginMark(path, identity, ct);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Legacy_packing_holds_clear_inputs_through_worker_reads(bool move)
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-efs-legacy-pack-held-").FullName;
        try
        {
            string source = Path.Join(root, "input.bin"), target = Path.Join(root, "output.zip");
            byte[] payload = [1, 2, 3, 4];
            File.WriteAllBytes(source, payload);
            if (move) File.WriteAllText(source + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
            var plan = new ArchivePlan(target, null, [new(ArchiveChangeKind.AddFile, "selected", source)]) { MoveSources = move };
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan,
                Sources = [ItemRef.ForFileSystemPath(source, EntryKind.File)] }, "Held EFS packing fixture", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);
            var files = new EncryptHeldPackingInput(source);
            new ZipUpdateExecutor(job, files, journal).Execute();

            Assert.NotEmpty(files.Attempts);
            Assert.All(files.Attempts, AssertBlockedEncryption);
            Assert.Equal(!move, File.Exists(source));
            if (move) Assert.Contains("ZoneId=3", File.ReadAllText(target + ":Zone.Identifier"));
            else
            {
                File.Encrypt(source);
                Assert.True(File.GetAttributes(source).HasFlag(FileAttributes.Encrypted));
            }

            using var zip = ZipFile.OpenRead(target);
            using var entry = Assert.Single(zip.Entries).Open();
            using var content = new MemoryStream();
            entry.CopyTo(content);
            Assert.Equal(payload, content.ToArray());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class EncryptHeldPackingInput(string source) : WindowsFileOperations
    {
        internal List<Exception?> Attempts { get; } = [];

        public override IDisposable? HoldArchivePackingInput(SafeFileHandle file)
        {
            var held = base.HoldArchivePackingInput(file);
            Attempts.Add(Record.Exception(() => File.Encrypt(source)));
            return held;
        }
    }

    [Fact]
    public void A_retained_packing_view_holds_the_source_clear_until_release()
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-efs-pack-held-").FullName;
        try
        {
            string file = Path.Join(root, "input.bin");
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            var ct = TestContext.Current.CancellationToken;
            using var directory = WindowsArchiveDirectory.Open(root);
            using (var packing = directory.OpenPackingSource("input.bin"))
            {
                var source = (IArchiveRetainedFile)packing;
                var observed = source.Observe(ct);
                using var view = source.OpenReadView(observed, ct);
                AssertBlockedEncryption(Record.Exception(() => File.Encrypt(file)));
                byte[] bytes = new byte[4];
                view.ReadExactly(bytes);
                Assert.Equal(new byte[] { 1, 2, 3, 4 }, bytes);
            }
            File.Encrypt(file);
            Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.Encrypted));
            using var readOnly = directory.OpenSource("input.bin");
            Assert.Equal(1, readOnly.ReadByte());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AssertBlockedEncryption(Exception? error)
    {
        const int efsInTransaction = unchecked((int)0x80071AAF);
        const int sharingViolation = unchecked((int)0x80070020);
        const int transactionConflict = unchecked((int)0x80071A90);
        Assert.True(error is IOException { HResult: efsInTransaction or sharingViolation or transactionConflict }, error?.ToString());
    }

    private static void RequireFixture() => WindowsStorageAbiTests.RequireNativeEfsOutputFixture();
}
