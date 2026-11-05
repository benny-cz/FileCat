using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class ArchiveCreationMetadataTests
{
    private static readonly string Empty = Convert.ToHexString(SHA256.HashData([]));
    private sealed class Review(object issuer, ArchiveFileObservation stage, string target, byte[] witness)
        : ArchiveCreatePublicationReview(issuer, stage, target, ArchiveCreationBackend.LinuxExt4, witness);
    private static (ArchiveTransactionFacts Facts, Review Review) Fixture(string root, Guid? nonce = null)
    {
        var version = new ArchiveObjectVersion("stage-id", 0, Empty);
        var stage = new ArchiveFileObservation(version, "parent-id", "stage", new string('A', 64), ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.LinuxOriginUrl), new ArchiveTimestamp(0, 0), 0x180);
        return (new(nonce ?? Guid.NewGuid(), 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Verifying, Path.Combine(root, "result.tar"), "parent-id", null,
            Path.Combine(root, "stage"), "stage-id", verifiedStage: version), new(new object(), stage, "result.tar", [1, 0, 0, 0]));
    }

    [Fact]
    public void Scoped_private_creation_evidence_has_complete_checksum_exact_readback_and_EOF()
    {
        using var root = new TempDir(); var f = Fixture(root.Path); var metadata = new ArchiveCreationMetadata(f.Facts, f.Review);
        using var bytes = new MemoryStream(); metadata.Write(bytes, TestContext.Current.CancellationToken); byte[] encoded = bytes.ToArray();
        Assert.Equal("FCCMETA1"u8.ToArray(), encoded[..8]); Assert.Equal("FCCMEND1"u8.ToArray(), encoded[^8..]);
        Assert.Equal(encoded.Length, metadata.Length); Assert.Equal(Convert.ToHexString(SHA256.HashData(encoded)), metadata.Sha256);
        metadata.Readback(new MemoryStream(encoded, false), TestContext.Current.CancellationToken);
        for (int length = 0; length < encoded.Length; length++)
            Assert.ThrowsAny<IOException>(() => metadata.Readback(new MemoryStream(encoded, 0, length, false), TestContext.Current.CancellationToken));
        Assert.Throws<ArchiveWriteException>(() => metadata.Readback(new MemoryStream([.. encoded, 0]), TestContext.Current.CancellationToken));
        encoded[encoded.Length / 2] ^= 1; Assert.Throws<ArchiveWriteException>(() => metadata.Readback(new MemoryStream(encoded), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void A_different_nonce_target_or_native_owner_cannot_reuse_the_evidence_scope()
    {
        using var root = new TempDir(); var f = Fixture(root.Path); var metadata = new ArchiveCreationMetadata(f.Facts, f.Review);
        metadata.RequireScope(f.Facts); Assert.Throws<ArgumentException>(() => metadata.RequireScope(Fixture(root.Path).Facts));
        Assert.Throws<ArgumentException>(() => new ArchiveCreationMetadata(f.Facts, new Review(new object(), f.Review.Stage, "different.tar", [1])));
        Assert.Throws<ArchiveWriteException>(() => f.Review.RequireIssuer(new object()));
    }

    [Fact]
    public void Native_witness_buffers_refuse_before_growth_beyond_the_bound()
    {
        byte[] excessive = new byte[ArchiveCreationMetadata.MaximumWitnessBytes + 1]; long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => ArchiveCreationMetadata.EncodeWitness(w => w.Write(excessive))).Reason);
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256 << 10);
    }

    [Fact]
    public void Schema_four_cannot_carry_the_new_creation_role_and_is_read_only()
    {
        using var root = new TempDir(); var f = Fixture(root.Path); var node = f.Facts.ToJson(); node["v"] = 4;
        Assert.Equal(4, ArchiveTransactionFacts.Parse(node).RecordVersion);
        node["sidecars"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
        { ["role"] = (int)ArchiveSidecarRole.CreationMetadata, ["path"] = Path.Combine(root.Path, "metadata"), ["maximum"] = ArchiveCreationMetadata.MaximumBytes, ["identity"] = null, ["version"] = null, ["sources"] = null });
        Assert.Throws<ArgumentException>(() => ArchiveTransactionFacts.Parse(node));
    }

    [Fact]
    public void A_manual_recovery_hold_permits_only_the_conservative_review_record_and_never_restores_creation_authority()
    {
        using var root = new TempDir(); var stage = new ArchiveObjectVersion("stage", 0, Empty); Guid nonce = Guid.NewGuid();
        using var journal = JobJournal.Create(root.Dir("journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(root.Path, "result.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned retention", "owned", [], []));
        var facts = new ArchiveTransactionFacts(nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
            Path.Combine(root.Path, "result.tar"), "parent", null, Path.Combine(root.Path, "stage"), "stage"); journal.ArchiveTransaction(facts);
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Rebuilding); journal.ArchiveTransaction(facts);
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Finalizing); journal.ArchiveTransaction(facts);
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Verifying, finalizedStage: stage); journal.ArchiveTransaction(facts);
        facts = facts.AdvancePreparation(ArchiveTransactionPhase.Verifying, verifiedStage: stage); journal.ArchiveTransaction(facts);
        journal.RequireRecovery(); Assert.Throws<InvalidOperationException>(() => journal.CurrentArchiveTransaction(nonce));
        var reviewed = journal.RecordArchiveCreationReview(nonce, true); Assert.Equal(ArchiveTransactionPhase.NeedsReview, reviewed.Phase);
        Assert.Contains(facts.StagePath, reviewed.UnresolvedArtifacts); Assert.Contains(facts.ArchivePath, reviewed.UnresolvedArtifacts);
        Assert.Equal(reviewed.Sequence, journal.RecordArchiveCreationReview(nonce, true).Sequence);
        Assert.Throws<InvalidOperationException>(() => journal.RequireCurrentArchiveTransaction(reviewed)); Assert.True(journal.NeedsRecovery);
    }
}
