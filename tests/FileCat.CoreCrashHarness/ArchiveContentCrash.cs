using System.Formats.Tar;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

// Test-only operation wrappers. The driver kills this actual parent externally at a flushed checkpoint.
internal static class ArchiveContentCrash
{
    internal static readonly string[] Points = ["snapshot-reserved", "snapshot-created-unrecorded", "snapshot-identified", "snapshot-created",
        "transaction-opened-unrecorded", "transaction-opened", "snapshot-sealed", "snapshot-recorded", "private-ready", "ready",
        "commit-intent", "native-committed", "committed-recorded", "rollback-before", "rollback-after", "rollback-recorded"];

    internal static async Task<int> Run(string root, string point, string windowsAssembly, string probe)
    {
        if (!OperatingSystem.IsWindows() || !Path.IsPathFullyQualified(root) || !Directory.Exists(root) || !Points.Contains(point) ||
            !Path.IsPathFullyQualified(probe) || !File.Exists(probe)) return 64;
        string path = Path.Combine(root, "original.tar"), inputPath = Path.Combine(root, "input");
        using (var stream = File.Create(path))
        using (var writer = new TarWriter(stream, TarEntryFormat.Pax))
        using (var before = new MemoryStream("original bytes"u8.ToArray()))
            writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "old") { DataStream = before, ModificationTime = DateTimeOffset.UnixEpoch });
        File.WriteAllBytes(path + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://original.invalid/\r\n"u8.ToArray());
        File.WriteAllBytes(inputPath, "replacement bytes"u8.ToArray());
        File.WriteAllBytes(inputPath + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=4\r\n"u8.ToArray());
        File.SetLastWriteTimeUtc(inputPath, DateTime.UnixEpoch.AddSeconds(1700000000));
        using var store = ArchiveCrashNativeStore.Open(root, windowsAssembly);
        var source = (IArchiveContentUpdateSource)store.OpenSource("original.tar"); using var sourceLease = (IDisposable)source;
        var input = store.OpenSource("input"); using var inputLease = (IDisposable)input;
        var baseline = source.Observe(CancellationToken.None); var inputState = input.Observe(CancellationToken.None);
        File.WriteAllText(Path.Combine(root, "baseline.json"), JsonSerializer.Serialize(new { baseline.Version, baseline.MetadataDigest,
            Origin = Convert.ToBase64String(baseline.Origin.Bytes), baseline.ParentIdentity }));
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(path, null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned native content crash", "owned", [], []));
        var children = new List<ArchiveCreationCrash.Worker>();
        var assessment = await new ArchiveAssessmentClient((nonce, _) => Start(nonce, "archive-assess"))
            .AssessAsync(new(new(ArchiveWriteFormat.Tar), baseline.Version.Length, baseline.Version.Sha256),
                () => source.OpenReadView(baseline, CancellationToken.None), CancellationToken.None);
        var plan = ArchiveTarTransformPlan.Create(new(ArchiveWriteFormat.Tar), assessment, [inputState.Input(3)],
            [new(ArchiveChangeKind.Replace, new(assessment.CatalogRevision, "old", ArchiveMemberType.File, 0), inputId: 3)], [], CancellationToken.None);
        using var prepared = await ArchiveJournaledTarPreparation.PrepareAsync(journal, path, plan, new(source, baseline),
            new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, inputState) }, store.Reserve, Create,
            new((nonce, _) => Start(nonce, "archive-transform")), new((nonce, _) => Start(nonce, "archive-verify")), CancellationToken.None);
        if (children.Count != 3 || children.Any(child => !child.Reaped || child.ExitCode != 0) || children.Select(child => child.Id).Distinct().Count() != 3)
            throw new IOException("All three distinct actual worker roles must be reaped before native update crash qualification.");
        try
        {
            using var result = ArchiveContentUpdate.Publish(journal, prepared, new Source(source, point),
                new Dictionary<int, ArchiveRetainedBinding> { [3] = new(input, inputState) }, Create, TimeSpan.FromMinutes(2), CancellationToken.None);
            WaitIf("committed-recorded", point);
        }
        catch (InjectedRollback) when (point == "rollback-recorded")
        {
            if (Current()?.Phase != ArchiveContentUpdatePhase.RolledBackWithRetainedArtifacts) throw;
            WaitIf("rollback-recorded", point);
        }
        return 65;

        ArchiveCreationCrash.Worker Start(Guid nonce, string role)
        { var child = ArchiveCreationCrash.Worker.Start(probe, nonce, role); children.Add(child); return child; }
        ArchiveContentUpdateFacts? Current() => JournalRecovery.ReadArchiveTransactions(journal.Path).ContentUpdates.SingleOrDefault();
        Stage Create(ArchiveArtifactReservation reservation)
        {
            bool snapshot = Current()?.SnapshotPath == reservation.Path;
            if (snapshot) WaitIf("snapshot-reserved", point);
            var native = (IArchiveContentUpdateStage)store.Create(reservation);
            if (snapshot) WaitIf("snapshot-created-unrecorded", point);
            return new(native, snapshot, point, Current);
        }
    }
    private static void WaitIf(string checkpoint, string point)
    {
        if (point != checkpoint) return;
        Console.WriteLine("archive-content-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
        throw new InvalidOperationException("The driver must terminate this actual process externally.");
    }
    private sealed class InjectedRollback : IOException;
    private sealed class Source(IArchiveContentUpdateSource native, string point) : IArchiveContentUpdateSource
    {
        public ArchiveContentUpdateRecipe Recipe => native.Recipe;
        public string ObjectIdentity => native.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct) => native.Observe(ct);
        public void Review(ArchiveFileObservation expected, CancellationToken ct) => native.Review(expected, ct);
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => native.OpenReadView(expected, ct);
        public ArchiveArtifactReservation ReserveContentSnapshot(Guid nonce, ArchiveSpaceEstimate space, CancellationToken ct) => native.ReserveContentSnapshot(nonce, space, ct);
        public IArchiveContentTransaction BeginContentTransaction(ArchiveFileObservation reviewed, Guid nonce, TimeSpan timeout, CancellationToken ct)
        {
            WaitIf("snapshot-created", point);
            var transaction = native.BeginContentTransaction(reviewed, nonce, timeout, ct);
            WaitIf("transaction-opened-unrecorded", point); return new Transaction(transaction, point);
        }
    }
    private sealed class Transaction(IArchiveContentTransaction native, string point) : IArchiveContentTransaction
    {
        public Guid TransactionId => native.TransactionId;
        public ArchiveFileObservation RetainOriginal(IArchiveContentUpdateStage snapshot, CancellationToken ct)
        {
            WaitIf("transaction-opened", point);
            var result = native.RetainOriginal(((Stage)snapshot).Native, ct); WaitIf("snapshot-sealed", point); return result;
        }
        public ArchiveContentUpdateReview PrepareOutput(IArchiveContentUpdateStage output, ArchiveVerifiedNativeStage verified, ArchiveOriginSnapshot origin, CancellationToken ct)
        {
            WaitIf("snapshot-recorded", point);
            var review = native.PrepareOutput(((Stage)output).Native, verified, origin, ct);
            WaitIf("private-ready", point);
            if (point.StartsWith("rollback-", StringComparison.Ordinal)) throw new InjectedRollback();
            return review;
        }
        public ArchiveFileObservation Commit(ArchiveContentUpdateReview review, CancellationToken ct)
        { WaitIf("commit-intent", point); var result = native.Commit(review, ct); WaitIf("native-committed", point); return result; }
        public ArchiveFileObservation ReviewPublished(ArchiveContentUpdateReview review, CancellationToken ct) => native.ReviewPublished(review, ct);
        public void Rollback() { WaitIf("rollback-before", point); native.Rollback(); WaitIf("rollback-after", point); }
        public void Dispose() => native.Dispose();
    }
    private sealed class Stage(IArchiveContentUpdateStage native, bool snapshot, string point, Func<ArchiveContentUpdateFacts?> current) : IArchiveContentUpdateStage
    {
        internal IArchiveContentUpdateStage Native => native;
        public string ObjectIdentity => native.ObjectIdentity;
        public ArchiveFileObservation Observe(CancellationToken ct) => native.Observe(ct);
        public void Review(ArchiveFileObservation expected, CancellationToken ct)
        { native.Review(expected, ct); if (current()?.Phase == ArchiveContentUpdatePhase.Ready) WaitIf("ready", point); }
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => native.OpenReadView(expected, ct);
        public string ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct) => native.ReviewCreation(reservation, ct);
        public string ReviewContentSnapshotCreation(ArchiveArtifactReservation reservation, CancellationToken ct)
        { string policy = native.ReviewContentSnapshotCreation(reservation, ct); if (snapshot) WaitIf("snapshot-identified", point); return policy; }
        public Stream BeginRebuild(long maximum, CancellationToken ct) => native.BeginRebuild(maximum, ct);
        public ArchiveFileObservation SealForVerification(CancellationToken ct) => native.SealForVerification(ct);
        public Stream BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct) => native.BeginEvidence(reservation, ct);
        public ArchiveFileObservation SealEvidence(CancellationToken ct) => native.SealEvidence(ct);
        public void StopWrites() => native.StopWrites();
        public void Dispose() => native.Dispose();
    }
}
