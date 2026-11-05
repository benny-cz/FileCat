using FileCat.Core.Archives;
using FileCat.Core.Jobs;

// Test-only actual process-crash checkpoints; never an application worker route.
internal static class ArchiveStageCrash
{
    internal static int Run(string root, string point, string windowsAssembly)
    {
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || point is not ("reserved" or "created" or "recorded")) return 64;
        using var store = ArchiveCrashNativeStore.Open(root, windowsAssembly);
        var reservation = store.Reserve(Guid.NewGuid(), ArchiveArtifactKind.Stage, 1024);
        using (var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned crash reservation", "owned", [], [])))
        {
            var reserved = new ArchiveTransactionFacts(reservation.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
                Path.Combine(root, "result.tar"), reservation.ParentIdentity, null, reservation.Path, null, stageMaximumLength: 1024);
            using var stage = ArchiveJournaledStage.Create<IArchiveRetainedStage>(journal, reserved, reservation, r =>
            {
                if (point == "reserved") WaitForExternalKill(point);
                var file = store.Create(r);
                if (point == "created") WaitForExternalKill(point);
                return file;
            }, CancellationToken.None);
            WaitForExternalKill(point);
        }
        return 65;
    }

    private static void WaitForExternalKill(string point)
    {
        Console.WriteLine("archive-stage-checkpoint:" + point); Console.Out.Flush();
        Console.ReadLine();
        throw new InvalidOperationException("The crash test must terminate this process externally.");
    }
}
