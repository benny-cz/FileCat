using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows.Archives;
using FileCat.Platform.Windows.Shell;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class ArchiveAppContainerTests
{
    // AW7 through the production Windows backend (AppContainer workers, NTFS transaction) and the manager's own target review,
    // as the interface uses them: one file into a new compressed file, the review for its one update, the replacement and a
    // fresh review. The v14d AT18 run found that the review refused every existing compressed file.
    [Theory]
    [InlineData(ArchiveWriteFormat.Gzip)] [InlineData(ArchiveWriteFormat.Bzip2)] [InlineData(ArchiveWriteFormat.Zstd)]
    public async Task A_compressed_file_is_created_reviewed_and_its_contents_replaced_through_the_real_manager(ArchiveWriteFormat format)
    {
        _ = Probe();
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TXF_NATIVE") != "1") Assert.Skip("Explicit native transactional job lane.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS"); Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!); string executable = Path.Combine(assets!, "FileCat.exe");
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-native-aw7-").FullName;
        try
        {
            string first = Path.Combine(root, "notes.txt"), second = Path.Combine(root, "newer.txt");
            File.WriteAllText(first, "first contents\n"); File.WriteAllText(second, string.Concat(Enumerable.Repeat("second contents ", 5000)));
            foreach (string file in new[] { first, second }) File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000));
            var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"),
                new([WindowsArchiveJobBackend.Create(format, executable)]));
            manager.DecisionRequested += pending => Assert.Fail("No decision is expected: " + pending.Request.Message);
            var profile = new ArchiveWriteProfile(format); string target = Path.Combine(root, "notes.txt" + ArchiveStandalone.Suffix(format));

            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null,
                [new(ArchiveChangeKind.AddFile, ArchiveStandalone.MemberName, first)], Profile: profile), null));
            var reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
            Assert.Equal(Hash(first), Assert.Single(reviewed.Assessment.Manifest.Members).Sha256, ignoreCase: true);

            await ArchiveNativeJobCases.Completed(manager.SubmitArchive(reviewed.CreatePlan(
                [new(ArchiveChangeKind.Replace, ArchiveStandalone.MemberName, second)]), null));
            var replaced = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
            Assert.Equal(Hash(second), Assert.Single(replaced.Assessment.Manifest.Members).Sha256, ignoreCase: true);
            Assert.Equal(reviewed.Assessment.Manifest.Features.ToArray(), replaced.Assessment.Manifest.Features.ToArray());
            var refused = Assert.Throws<ArchiveWriteException>(() =>
                replaced.ReviewMemberChanges([new(ArchiveChangeKind.Delete, ArchiveStandalone.MemberName)], ct));
            Assert.Equal(ArchiveStandalone.MemberChangesUnavailable, refused.Message);
            Assert.Equal(new[] { "journals", "newer.txt", "notes.txt", Path.GetFileName(target) },
                Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase));
        }
        finally { FileCat.Tests.TestCleanup.DeleteTree(root); }

        static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
