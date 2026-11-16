using FileCat.Core.Archives;
using FileCat.Core.Jobs;

internal static class ArchiveSidecarCrash
{
    internal static int Run(string root, string role, string point, string windowsAssembly)
    {
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || role is not ("manifest" or "origin") ||
            point is not ("reserved" or "identified" or "partial" or "sealed" or "recorded")) return 64;
        using var store = ArchiveCrashNativeStore.Open(root, windowsAssembly); Guid nonce = Guid.NewGuid();
        var manifest = ArchiveTarTransformPlan.Create(null, [], [], [], CancellationToken.None).Expected;
        var originScope = new ArchiveOriginEvidenceScope(nonce, manifest.Profile, ArchiveOriginOperation.Create, 0);
        long length = role == "manifest" ? ArchiveManifestSidecar.EncodedLength(manifest, CancellationToken.None) : 52;
        var stageName = store.Reserve(nonce, ArchiveArtifactKind.Stage, 1024);
        var sidecarName = store.Reserve(nonce, role == "manifest" ? ArchiveArtifactKind.Manifest : ArchiveArtifactKind.Provenance, length);
        var sidecarRole = role == "manifest" ? ArchiveSidecarRole.ExpectedOutput : ArchiveSidecarRole.OriginEvidence;
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: manifest.Profile) }, "owned sidecar crash", "owned", [], []));
        var initial = new ArchiveTransactionFacts(nonce, 1, manifest.Profile, ArchiveTransactionPhase.Reserved, Path.Combine(root, "result.tar"),
            stageName.ParentIdentity, null, stageName.Path, null, stageMaximumLength: 1024,
            sidecars: [new(sidecarRole, sidecarName.Path, length, originSources: role == "origin" ? 0 : null)]);
        using var stage = ArchiveJournaledStage.Create(journal, initial, stageName, store.Create, CancellationToken.None);
        var current = journal.CurrentArchiveTransaction(nonce);
        using var evidence = role == "manifest"
            ? ArchiveJournaledSidecar.WriteManifest(journal, current, sidecarName, Create, ArchiveManifestPurpose.ExpectedOutput, manifest, CancellationToken.None)
            : ArchiveJournaledSidecar.WriteOrigin(journal, current, sidecarName, Create, originScope, [], CancellationToken.None);
        Wait(point);
        return 65;
        CrashArtifact Create(ArchiveArtifactReservation name)
        {
            if (point == "reserved") Wait(point);
            return new(store.Create(name), point);
        }
    }
    private static void Wait(string point)
    {
        Console.WriteLine("archive-sidecar-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
        throw new InvalidOperationException("The test must terminate this actual process externally.");
    }

    private sealed class CrashArtifact(IArchiveRetainedStage native, string point) : IArchiveRetainedStage
    {
        public string ObjectIdentity => native.ObjectIdentity;
        public string ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct) => native.ReviewCreation(reservation, ct);
        public ArchiveFileObservation Observe(CancellationToken ct) => native.Observe(ct);
        public void Review(ArchiveFileObservation expected, CancellationToken ct) => native.Review(expected, ct);
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => native.OpenReadView(expected, ct);
        public Stream BeginRebuild(long exactMaximumLength, CancellationToken ct) => native.BeginRebuild(exactMaximumLength, ct);
        public ArchiveFileObservation SealForVerification(CancellationToken ct) => native.SealForVerification(ct);
        public Stream BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct)
        {
            if (point == "identified") Wait(point);
            return new PartialStream(native.BeginEvidence(reservation, ct), point == "partial");
        }
        public ArchiveFileObservation SealEvidence(CancellationToken ct)
        {
            var observation = native.SealEvidence(ct); if (point == "sealed") Wait(point); return observation;
        }
        public void StopWrites() => native.StopWrites();
        public void Dispose() => native.Dispose();
    }
    private sealed class PartialStream(Stream native, bool interrupt) : Stream
    {
        public override bool CanRead => native.CanRead; public override bool CanSeek => native.CanSeek; public override bool CanWrite => native.CanWrite;
        public override long Length => native.Length; public override long Position { get => native.Position; set => native.Position = value; }
        public override void Flush() => native.Flush(); public override int Read(byte[] buffer, int offset, int count) => native.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => native.Seek(offset, origin); public override void SetLength(long value) => native.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (interrupt && !buffer.IsEmpty) { native.Write(buffer[..1]); Wait("partial"); }
            native.Write(buffer);
        }
        // This test-only view does not own the native artifact, exactly like the codec's borrowed output.
    }
}
