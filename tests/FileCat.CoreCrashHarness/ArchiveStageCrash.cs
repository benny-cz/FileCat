using System.Reflection;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

// Test-only actual process-crash checkpoints; never an application worker route.
internal static class ArchiveStageCrash
{
    internal static int Run(string root, string point, string windowsAssembly)
    {
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || point is not ("reserved" or "created" or "recorded")) return 64;
        IDisposable directory;
        ArchiveArtifactReservation reservation;
        Func<ArchiveArtifactReservation, IArchiveRetainedStage> create;
        if (OperatingSystem.IsWindows())
        {
            if (!Path.IsPathFullyQualified(windowsAssembly)) return 64;
            var assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(windowsAssembly);
            var type = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsArchiveDirectory", true)!;
            var kind = assembly.GetType("FileCat.Platform.Windows.Archives.WindowsArchiveArtifactKind", true)!;
            directory = (IDisposable)type.GetMethod("Open")!.Invoke(null, [root])!;
            reservation = (ArchiveArtifactReservation)type.GetMethod("ReserveArtifact", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(directory, [Guid.NewGuid(), Enum.ToObject(kind, 0), 1024L])!;
            var method = type.GetMethod("CreateArtifact", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, [typeof(ArchiveArtifactReservation)], modifiers: null) ?? throw new MissingMethodException(type.FullName, "CreateArtifact");
            create = r => (IArchiveRetainedStage)method.Invoke(directory, [r])!;
        }
        else
        {
            var native = UnixArchiveDirectory.Open(root); directory = native;
            reservation = native.ReserveArtifact(Guid.NewGuid(), UnixArchiveArtifactKind.Stage, 1024);
            create = r => native.CreateArtifact(r);
        }
        using (directory)
        using (var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(Path.Combine(root, "result.tar"), null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned crash reservation", "owned", [], [])))
        {
            var reserved = new ArchiveTransactionFacts(reservation.Nonce, 1, new(ArchiveWriteFormat.Tar), ArchiveTransactionPhase.Reserved,
                Path.Combine(root, "result.tar"), reservation.ParentIdentity, null, reservation.Path, null, stageMaximumLength: 1024);
            using var stage = ArchiveJournaledStage.Create<IArchiveRetainedStage>(journal, reserved, reservation, r =>
            {
                if (point == "reserved") WaitForExternalKill(point);
                var file = create(r);
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
