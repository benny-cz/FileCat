using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsArchiveEfsPackTests
{
    [Theory]
    [InlineData("file")]
    [InlineData("nested file")]
    [InlineData("folder default")]
    public void Native_pack_refuses_EFS_before_starting_the_worker_or_creating_a_stage(string selection)
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
                Profile: new ArchiveWriteProfile(ArchiveWriteFormat.Zip));
            var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "EFS pack fixture", "", [], []);
            using var journal = JobJournal.Create(Path.Join(root, "journals"), job);
            var backend = WindowsArchiveJobBackend.Create(ArchiveWriteFormat.Zip, Path.Join(root, "unused-worker.exe"));
            var problem = Assert.Throws<ArchiveWriteException>(() => backend.Execute(new(job, plan, null, new WindowsFileOperations(), journal)));
            Assert.Equal(ArchiveWriteReason.PublicationUnavailable, problem.Reason);
            Assert.Equal(ArchiveOuterEfs.PackUnavailable, problem.Message);
            Assert.Empty(Directory.EnumerateFiles(root));
            Assert.True(File.GetAttributes(encrypted).HasFlag(FileAttributes.Encrypted));
            Assert.Equal("original protected content", File.ReadAllText(source));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void A_retained_packing_view_refuses_a_new_EFS_state_before_returning_payload()
    {
        RequireFixture();
        string root = Directory.CreateTempSubdirectory("filecat-efs-pack-held-").FullName;
        try
        {
            string file = Path.Join(root, "input.bin");
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            var ct = TestContext.Current.CancellationToken;
            using var directory = WindowsArchiveDirectory.Open(root);
            using var packing = directory.OpenPackingSource("input.bin");
            var source = (IArchiveRetainedFile)packing;
            var observed = source.Observe(ct);
            using var view = source.OpenReadView(observed, ct);
            File.Encrypt(file);
            byte[] untouched = [0xe1, 0xe2];
            var error = Assert.Throws<ArchiveWriteException>(() => view.Read(untouched));
            Assert.Equal(ArchiveOuterEfs.PackUnavailable, error.Message);
            Assert.Equal(new byte[] { 0xe1, 0xe2 }, untouched);
            using var readOnly = directory.OpenSource("input.bin");
            Assert.Equal(1, readOnly.ReadByte());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void RequireFixture()
    {
        Assert.SkipUnless(WindowsStorageTransforms.IsQualifiedHost && Environment.GetEnvironmentVariable("FILECAT_TEST_EFS") == "1",
            "Requires a disposable normal-user Windows VM with EFS enabled.");
        Assert.False(WindowsTokenAuthority.IsHighIntegrity);
    }
}
