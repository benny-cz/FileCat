using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchivePublicationCompatibilityTests
{
    [Theory]
    [InlineData(ArchiveWriteFormat.Zip)]
    [InlineData(ArchiveWriteFormat.Tar)]
    public void An_explicit_profile_cannot_enter_legacy_publication_or_create_a_legacy_transaction_intent(ArchiveWriteFormat format)
    {
        using var fixture = new TempDir();
        string archive = Path.Combine(fixture.Path, "owned.archive");
        var plan = new ArchivePlan(archive, null, [], Profile: new(format));
        var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = plan }, "owned publication guard", "owned", [], []);
        using var journal = JobJournal.Create(fixture.Path, job);
        long before = new FileInfo(journal.Path).Length;
        var failure = Assert.Throws<ArchiveWriteException>(() =>
            new LegacyZipPublicationTransaction(job, new PortableFileOperations(), journal, plan, archive, fixture.Path));
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, failure.Reason);
        Assert.Equal(before, new FileInfo(journal.Path).Length);
        Assert.Empty(Directory.EnumerateFiles(fixture.Path, ".filecat-zip-*"));
        Assert.False(File.Exists(archive));
    }
}
