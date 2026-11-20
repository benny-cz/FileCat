using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Tests;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed partial class UnixArchiveStorageTests
{
    // AW7 through the production Unix backend and the manager's own target review, as the interface uses them: one file into
    // a new compressed file, the review of that file for its one update, the replacement and a fresh review of the result.
    // The v14d AT18 run found that the review refused every existing compressed file; only the scale lane covered it then.
    [Theory]
    [InlineData(ArchiveWriteFormat.Gzip)] [InlineData(ArchiveWriteFormat.Bzip2)] [InlineData(ArchiveWriteFormat.Zstd)]
    public async Task A_compressed_file_is_created_reviewed_and_its_contents_replaced_through_the_real_manager(ArchiveWriteFormat format)
    {
        Native(); using var temp = new TempDir(); string root = Canonical(temp.Path); var ct = TestContext.Current.CancellationToken;
        File.SetUnixFileMode(root, (UnixFileMode)0x1c0);
        string first = Path.Combine(root, "notes.txt"), second = Path.Combine(root, "newer.txt");
        File.WriteAllText(first, "first contents\n"); File.WriteAllText(second, string.Concat(Enumerable.Repeat("second contents ", 5000)));
        foreach (string file in new[] { first, second }) File.SetLastWriteTimeUtc(file, DateTime.UnixEpoch.AddSeconds(1700000000));
        var backend = UnixArchiveJobBackend.Create(format, (nonce, _) => Child.Start(nonce, "archive-assess-v2"),
            (nonce, _) => Child.Start(nonce, "archive-transform-v2"), (nonce, _) => Child.Start(nonce, "archive-verify-v2"));
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.Combine(root, "journals"), new([backend]));
        manager.DecisionRequested += pending => Assert.Fail("No decision is expected: " + pending.Request.Message);
        var profile = new ArchiveWriteProfile(format); string target = Path.Combine(root, "notes.txt" + ArchiveStandalone.Suffix(format));

        await ArchiveNativeJobCases.Completed(manager.SubmitArchive(new(target, null,
            [new(ArchiveChangeKind.AddFile, ArchiveStandalone.MemberName, first)], Profile: profile), null));
        var reviewed = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
        Assert.Equal(Hash(File.ReadAllBytes(first)), Assert.Single(reviewed.Assessment.Manifest.Members).Sha256, ignoreCase: true);

        await ArchiveNativeJobCases.Completed(manager.SubmitArchive(reviewed.CreatePlan(
            [new(ArchiveChangeKind.Replace, ArchiveStandalone.MemberName, second)]), null));
        var replaced = await manager.ReviewArchiveUpdateAsync(target, profile, null, ct);
        Assert.Equal(Hash(File.ReadAllBytes(second)), Assert.Single(replaced.Assessment.Manifest.Members).Sha256, ignoreCase: true);
        Assert.Equal(reviewed.Assessment.Manifest.Features.ToArray(), replaced.Assessment.Manifest.Features.ToArray());
        var refused = Assert.Throws<ArchiveWriteException>(() =>
            replaced.ReviewMemberChanges([new(ArchiveChangeKind.Delete, ArchiveStandalone.MemberName)], ct));
        Assert.Equal(ArchiveStandalone.MemberChangesUnavailable, refused.Message);
        Assert.Equal(new[] { "journals", "newer.txt", "notes.txt", Path.GetFileName(target) },
            Directory.GetFileSystemEntries(root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }
}
