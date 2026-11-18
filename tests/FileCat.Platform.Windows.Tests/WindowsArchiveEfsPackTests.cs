using System.IO.Compression;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsArchiveEfsPackTests
{
    [Theory]
    [InlineData("file", true)]
    [InlineData("nested file", true)]
    [InlineData("folder default", true)]
    [InlineData("file", false)]
    [InlineData("nested file", false)]
    [InlineData("folder default", false)]
    public void Pack_refuses_EFS_before_loading_an_input_or_creating_a_stage(string selection, bool native)
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-efs-pack-").FullName;
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, "selected")).FullName;
            string source = Path.Join(folder, "protected.txt"), target = Path.Join(root, "output.zip");
            File.WriteAllText(source, "original protected content");
            string encrypted = selection == "folder default" ? folder : source;
            File.Encrypt(encrypted);
            bool direct = selection == "file";
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
            Assert.Equal("original protected content", File.ReadAllText(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

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

    private static void RequireFixture()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }
}
