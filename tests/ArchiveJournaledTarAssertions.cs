using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Tests;

internal static class ArchiveJournaledTarAssertions
{
    internal static void BeforeCreate(JobJournal journal, ArchiveArtifactReservation reservation)
    {
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.False(File.Exists(reservation.Path));
        if (reservation.Kind == ArchiveArtifactKind.Stage) { Assert.Equal(reservation.Path, facts.StagePath); Assert.Null(facts.StageIdentity); }
        else
        {
            var sidecar = Assert.Single(facts.Sidecars, s => s.Path == reservation.Path);
            Assert.Null(sidecar.Identity); Assert.Null(sidecar.Version); Assert.Equal(reservation.MaximumLength, sidecar.MaximumLength);
        }
    }

    internal static void Phase(JobJournal journal, ArchiveTransactionPhase phase)
    {
        var facts = Assert.Single(JournalRecovery.ReadArchiveTransactions(journal.Path).Transactions);
        Assert.Equal(phase, facts.Phase); Assert.NotNull(facts.StageIdentity); Assert.NotNull(facts.Manifest); Assert.NotNull(facts.OriginEvidence);
        Assert.Equal(phase == ArchiveTransactionPhase.Verifying, facts.FinalizedStage is not null);
        Assert.Null(facts.VerifiedStage); Assert.Null(facts.Sidecars.Single(s => s.Role == ArchiveSidecarRole.VerifiedOutput).Version);
    }

    internal static void Complete<T>(ArchivePreparedTar<T> prepared, JobJournal journal, ArchiveTarTransformPlan plan, CancellationToken ct)
        where T : class, IArchiveRetainedStage
    {
        TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(new
        { archiveRuntime = Environment.Version.ToString(), runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory(), os = RuntimeInformation.OSDescription, process = Environment.ProcessId }));
        var facts = prepared.Facts; Assert.True(journal.NeedsRecovery); Assert.Equal(ArchiveTransactionPhase.Verifying, facts.Phase);
        Assert.Equal(facts.ParentIdentity, prepared.Space.ParentIdentity); Assert.True(prepared.Space.CapacityKnown);
        Assert.True(prepared.Space.Capacity!.AvailableBytes >= prepared.Space.Estimate.AdditionalBytes);
        Assert.Equal(facts.Original?.Length ?? 0, prepared.Space.Estimate.OriginalBytes);
        Assert.Equal(facts.StageMaximumLength, prepared.Space.Estimate.StageBytes);
        Assert.Equal(facts.Sidecars.Sum(s => s.MaximumLength), prepared.Space.Estimate.EvidenceBytes);
        Assert.Equal(0, prepared.Space.Estimate.KnownSnapshotBytes);
        Assert.Equal((64L << 20) + facts.StageMaximumLength + facts.Sidecars.Sum(s => s.MaximumLength), prepared.Space.Estimate.AdditionalBytes);
        TestContext.Current.TestOutputHelper?.WriteLine(JsonSerializer.Serialize(new { archiveSpace = new {
            parent = prepared.Space.ParentIdentity, volume = prepared.Space.Capacity.VolumeIdentity, available = prepared.Space.Capacity.AvailableBytes,
            additional = prepared.Space.Estimate.AdditionalBytes, footprint = prepared.Space.Estimate.TotalFootprintBytes,
            original = prepared.Space.Estimate.OriginalBytes, stage = prepared.Space.Estimate.StageBytes,
            evidence = prepared.Space.Estimate.EvidenceBytes, snapshots = prepared.Space.Estimate.KnownSnapshotBytes } }));
        Assert.Equal(prepared.Verified.Stage.Version, facts.VerifiedStage); Assert.Null(facts.SecuritySnapshotSha256); Assert.Null(facts.Published);
        Assert.Equal(facts.VerifiedStage, facts.FinalizedStage);
        Assert.Equal(plan.Source is null ? 3 : 4, prepared.Sidecars.Count); Assert.Equal(prepared.Sidecars.Count, facts.Sidecars.Count);
        Assert.All(facts.Sidecars, s => Assert.NotNull(s.Version)); Assert.All(prepared.Sidecars, s => Assert.False(s.File is Stream stream && stream.CanWrite));
        var history = JobJournal.ReadAll(journal.Path).Where(r => r.Type == "archive-transaction").Select(r => ArchiveTransactionFacts.Parse(r.Node)).ToArray();
        Assert.Null(history[0].StageIdentity); Assert.All(history[0].Sidecars, s => { Assert.Null(s.Identity); Assert.Null(s.Version); });
        Assert.Equal(facts.Sequence, history.Length); Assert.DoesNotContain(history, h => h.Phase == ArchiveTransactionPhase.ReadyToPublish);
        Assert.Contains(history, h => h.Phase == ArchiveTransactionPhase.Finalizing && h.FinalizedStage is null);
        Assert.Contains(history, h => h.Phase == ArchiveTransactionPhase.Verifying && h.FinalizedStage is not null && h.VerifiedStage is null);
        Assert.Throws<IOException>(() => journal.Finish(JobState.Completed, "verified unpublished preparation still requires review"));
        foreach (var evidence in prepared.Sidecars)
        {
            var sidecar = Assert.Single(facts.Sidecars, s => s.Identity == evidence.Observation.Version.Identity);
            Assert.Equal(sidecar.Version, evidence.Observation.Version); Assert.True(File.Exists(sidecar.Path));
            using var view = evidence.File.OpenReadView(evidence.Observation, ct);
            var version = evidence.Observation.Version;
            if (sidecar.Role == ArchiveSidecarRole.OriginEvidence)
            {
                using var reader = new ArchiveOriginEvidenceReader(view, new(version.Length, version.Sha256),
                    new(facts.Nonce, facts.Profile, plan.Source is null ? ArchiveOriginOperation.Create : ArchiveOriginOperation.Update, facts.OriginEvidenceSources!.Value), ct);
                if (prepared.Verified.Original is { } original) Match(reader.ReadNext(), original, ArchiveOriginSourceKind.OriginalArchive);
                foreach (var input in prepared.Verified.Inputs.OrderBy(p => p.Key)) Match(reader.ReadNext(), input.Value, ArchiveOriginSourceKind.File);
                Assert.Null(reader.ReadNext()); Assert.True(reader.IsComplete);
            }
            else
            {
                var expected = sidecar.Role == ArchiveSidecarRole.AssessedSource ? plan.Source!.Manifest : plan.Expected;
                using var reader = ArchiveManifestSidecar.OpenRead(view, new(version.Length, version.Sha256), facts.Nonce, (ArchiveManifestPurpose)sidecar.Role, facts.Profile, ct);
                while (reader.ReadNext() is not null) { }
                Assert.True(reader.IsComplete); Assert.Equal(expected.Digest, reader.VerifiedManifestDigest);
                Assert.Equal(expected.ContentDigest, reader.VerifiedContentDigest); Assert.Equal(expected.MetadataDigest, reader.VerifiedMetadataDigest);
            }
            evidence.File.Review(evidence.Observation, ct);
        }
        static void Match(ArchiveOriginEvidenceRecord? actual, ArchiveFileObservation expected, ArchiveOriginSourceKind kind)
        { Assert.NotNull(actual); Assert.Equal(kind, actual.Kind); Assert.Equal(expected.Version, actual.Source); Assert.Equal(expected.Origin.Digest, actual.Origin.Digest); }
    }
}
