using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveRar5PreparationPreflightTests
{
    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Password_and_worker_version_refusals_precede_reservation_creation_launch_and_journal_intent(int failure)
    {
        var ct = TestContext.Current.CancellationToken; using var temp = new TempDir(); string target = Path.Combine(temp.Path, "output.rar");
        var profile = ArchiveWriteProfile.ForRar5(encryption: failure == 1 ? null : new(ArchiveEncryptionMode.Data));
        string digest = Convert.ToHexString(SHA256.HashData([1]));
        var plan = ArchiveRar5TransformPlan.Create(profile, null, [new(1, 1, digest, DateTimeOffset.UnixEpoch)], [], [new("f", ArchiveMemberType.File, 1)], ct);
        using var password = failure == 0 ? null : new ArchiveJobPassword("Public preflight password"); if (failure == 2) password!.Dispose();
        int reserves = 0, creates = 0, launches = 0;
        IArchiveWorkerProcess Launch(Guid _, TimeSpan __) { launches++; throw new InvalidOperationException(); }
        using var journal = JobJournal.Create(Path.Combine(temp.Path, "journals"), new Job(new JobRequest
        { Kind = JobKind.ArchiveUpdate, Archive = new(target, null, [], Profile: profile) }, "owned RAR preflight", "owned", [], []));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => ArchiveJournaledContainerPreparation.PrepareAsync<UnixArchiveFile>(journal, target, plan, null,
            new Dictionary<int, ArchiveRetainedBinding>(), (_, _, _) => { reserves++; throw new InvalidOperationException(); },
            _ => { creates++; throw new InvalidOperationException(); }, new(Launch, protocolVersion: failure == 3 ? (ushort)1 : (ushort)2),
            new(Launch, protocolVersion: failure == 4 ? (ushort)1 : (ushort)2), ct, password: password));
        if (failure == 2) Assert.IsType<ObjectDisposedException>(error);
        else Assert.Equal(failure >= 3 ? ArchiveWriteReason.UnsupportedFeature : ArchiveWriteReason.InvalidRequest, Assert.IsType<ArchiveWriteException>(error).Reason);
        Assert.Equal(0, reserves); Assert.Equal(0, creates); Assert.Equal(0, launches); Assert.False(journal.NeedsRecovery);
        Assert.DoesNotContain(JobJournal.ReadAll(journal.Path), record => record.Type == "archive-transaction");
    }
}
