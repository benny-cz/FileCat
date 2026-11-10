using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

// Test-only actual update process whose preparation fails after Rebuilding (the transform cannot be launched), so the Unix
// job scope runs the exact discard of the unpublished update in its private stage folder. It runs the real review, plan,
// capture, journaled preparation and discard; only the journal stream is wrapped so the process can stop immediately
// before or after one durable record, and the test kills it externally there. Production code has no crash hooks.
//   point: before|after:T:<ArchiveTransactionPhase> | before|after:D:<ArchiveDiscardPhase>[:reviewedCount] | end
internal static class ArchiveUnixUpdateDiscardCrash
{
    internal static async Task<int> Run(string root, string point, string probe)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return 64;
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || !Path.IsPathFullyQualified(probe) || !File.Exists(probe)) return 64;
        var stop = ArchiveUnixCreationCrash.Point.Parse(point);
        if (stop.Record == "C") return 64; // An update has no creation cleanup records.
        var ct = CancellationToken.None; var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        string target = Path.Combine(root, "original.tar"), added = Path.Combine(root, "added.bin");
        var workers = new List<ArchiveCreationCrash.Worker>();
        ArchiveCreationCrash.Worker Start(Guid nonce, string role) { var worker = ArchiveCreationCrash.Worker.Start(probe, nonce, role); lock (workers) workers.Add(worker); return worker; }
        var assessor = new ArchiveAssessmentClient((nonce, _) => Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var transform = new ArchiveTransformClient((nonce, _) => throw new IOException("Injected transform launch failure."), protocolVersion: 2);
        var verifier = new ArchiveVerificationClient((nonce, _) => Start(nonce, "archive-verify-v2"), protocolVersion: 2);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var request = reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "added.bin", added)]);
        var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = request }, "owned unix update discard crash", "owned", [], []);
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), job);
        ArchiveUnixCreationCrash.CheckpointStream.Prefix = "archive-unix-update-discard-checkpoint:";
        ArchiveUnixCreationCrash.CheckpointStream.Install(journal, stop);
        var planning = new ArchiveJobPlanning(request, ct);
        using var scope = new UnixArchiveJobScope(request, ct, retainArtifacts: false);
        var original = scope.Original ?? throw new InvalidOperationException("The fixture archive was not opened.");
        var source = await assessor.AssessAsync(new(profile, original.Reviewed.Version.Length, original.Reviewed.Version.Sha256),
            () => original.File.OpenReadView(original.Reviewed, ct), ct);
        using var selection = planning.CaptureInputs(scope.OpenInputTree, ct) ?? throw new InvalidOperationException("No captured input.");
        var review = planning.Review(source, selection, selection.Inputs, ct);
        var plan = review.CreatePlan(false, true, ct, true);
        try
        {
            using var failed = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, request.ArchivePath, plan, original,
                selection.Inputs.Bindings, scope.Reserve, scope.Create, transform, verifier, ct);
        }
        catch (IOException) { scope.DiscardUnpublished(journal, ct); }
        if (stop.Kind == "end") ArchiveUnixCreationCrash.CheckpointStream.Wait(point);
        Console.WriteLine("archive-unix-update-discard-unreached:" + point);
        return 65;
    }
}
