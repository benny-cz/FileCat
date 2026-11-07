using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

// Actual externally killed parent, with two real (unconfined test) child roles reaped before publication.
// Test-only wrapper checkpoints observe the coordinator's durable facts; production code has no crash hooks.
internal static class ArchiveCreationCrash
{
    internal static async Task<int> Run(string root, string point, string windowsAssembly, string probe)
    {
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || !Path.IsPathFullyQualified(probe) || !File.Exists(probe) ||
            point is not ("metadata-reserved" or "metadata-identified" or "metadata-partial" or "metadata-sealed" or
                "metadata-recorded" or "ready" or "publishing" or "claimed" or "reconciling" or "committed-warning")) return 64;
        using var store = ArchiveCrashNativeStore.Open(root, windowsAssembly);
        string target = Path.Combine(root, "result.tar");
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), new Job(new JobRequest { Kind = JobKind.ArchiveUpdate,
            Archive = new(target, null, [], Profile: new(ArchiveWriteFormat.Tar)) }, "owned creation crash", "owned", [], []));
        var plan = ArchiveTarTransformPlan.Create(null, [], [], [], CancellationToken.None);
        var children = new List<Worker>();
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, target, plan, null,
            new Dictionary<int, ArchiveRetainedBinding>(), store.Reserve, Create,
            new((nonce, _) => Start(nonce, "archive-transform")), new((nonce, _) => Start(nonce, "archive-verify")), CancellationToken.None);
        if (children.Count != 2 || children.Any(c => !c.Reaped || c.ExitCode != 0) || children[0].Id == children[1].Id)
            throw new IOException("Both distinct actual worker roles must exit and be reaped before a publication crash.");
        await ArchiveNewFilePublication.PublishAsync(journal, prepared, new Dictionary<int, ArchiveRetainedBinding>(), Create, CancellationToken.None);
        Wait("committed-warning");
        return 65;

        Worker Start(Guid nonce, string role) { var worker = Worker.Start(probe, nonce, role); children.Add(worker); return worker; }
        CrashFile Create(ArchiveArtifactReservation reservation)
        {
            var facts = journal.CurrentArchiveTransaction(reservation.Nonce);
            bool metadata = facts.Sidecars.Any(s => s.Role == ArchiveSidecarRole.CreationMetadata && s.Path == reservation.Path);
            if (metadata && point == "metadata-reserved") Wait(point);
            return new(store.Create(reservation), metadata, point, () => journal.CurrentArchiveTransaction(reservation.Nonce));
        }
    }
    private static void Wait(string point)
    {
        Console.WriteLine("archive-creation-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
        throw new InvalidOperationException("The test must terminate this actual process externally.");
    }

    private sealed class CrashFile(IArchiveRetainedStage native, bool metadata, string point, Func<ArchiveTransactionFacts> current)
        : IArchiveCreatePublicationStage
    {
        public string ObjectIdentity => native.ObjectIdentity;
        public string ReviewCreation(ArchiveArtifactReservation reservation, CancellationToken ct) => native.ReviewCreation(reservation, ct);
        public ArchiveFileObservation Observe(CancellationToken ct) => native.Observe(ct);
        public void Review(ArchiveFileObservation expected, CancellationToken ct)
        {
            native.Review(expected, ct); var facts = current();
            if (point == "metadata-recorded" && facts.Phase == ArchiveTransactionPhase.Verifying &&
                facts.Sidecars.Any(s => s.Role == ArchiveSidecarRole.CreationMetadata && s.Version is not null)) Wait(point);
            if (point == "ready" && facts.Phase == ArchiveTransactionPhase.ReadyToPublish ||
                point == "reconciling" && facts.Phase == ArchiveTransactionPhase.Reconciling) Wait(point);
        }
        public Stream OpenReadView(ArchiveFileObservation expected, CancellationToken ct) => native.OpenReadView(expected, ct);
        public Stream BeginRebuild(long maximum, CancellationToken ct) => native.BeginRebuild(maximum, ct);
        public ArchiveFileObservation SealForVerification(CancellationToken ct) => native.SealForVerification(ct);
        public Stream BeginEvidence(ArchiveArtifactReservation reservation, CancellationToken ct)
        {
            if (metadata && point == "metadata-identified") Wait(point);
            var stream = native.BeginEvidence(reservation, ct);
            return metadata && point == "metadata-partial" ? new Partial(stream) : stream;
        }
        public ArchiveFileObservation SealEvidence(CancellationToken ct)
        { var result = native.SealEvidence(ct); if (metadata && point == "metadata-sealed") Wait(point); return result; }
        public void StopWrites() => native.StopWrites();
        public void Dispose() => native.Dispose();
        public ArchiveFileObservation ApplyCreationOrigin(ArchiveFileObservation sealedStage, ArchiveOriginSnapshot required, CancellationToken ct)
            => ((IArchiveCreatePublicationStage)native).ApplyCreationOrigin(sealedStage, required, ct);
        public ArchiveCreatePublicationReview ReviewCreate(ArchiveFileObservation verified, string target, CancellationToken ct)
            => ((IArchiveCreatePublicationStage)native).ReviewCreate(verified, target, ct);
        public void ClaimNewName(string target, ArchiveCreatePublicationReview review, CancellationToken ct)
        {
            if (point == "publishing") Wait(point);
            ((IArchiveCreatePublicationStage)native).ClaimNewName(target, review, ct);
            if (point == "claimed") Wait(point);
        }
        public ArchiveFileObservation ReviewPublished(string target, ArchiveCreatePublicationReview review, CancellationToken ct)
            => ((IArchiveCreatePublicationStage)native).ReviewPublished(target, review, ct);
    }
    private sealed class Partial(Stream output) : Stream
    {
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => output.Length; public override long Position { get => output.Position; set => throw new NotSupportedException(); }
        public override void Flush() => output.Flush();
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => Write(b.AsSpan(o, c));
        public override void Write(ReadOnlySpan<byte> bytes)
        { if (!bytes.IsEmpty) { output.Write(bytes[..1]); output.Flush(); Wait("metadata-partial"); } }
        // Borrowed view: the native artifact owns the output and its lifetime.
    }
    internal sealed class Worker(Process process, System.IO.Pipes.AnonymousPipeServerStream cancellation) : IArchiveWorkerProcess
    {
        private readonly Task<bool> stderr = Drain(process); // Controlled empty-TAR child, never a production launch.
        private uint exit; private TimeSpan cpu; private bool disposed;
        internal int Id { get; } = process.Id;
        internal bool Reaped { get; private set; }
        public Stream Input => process.StandardInput.BaseStream;
        public Stream Output => process.StandardOutput.BaseStream;
        public bool HasExited => disposed || process.HasExited;
        public uint ExitCode => disposed ? exit : (uint)process.ExitCode;
        public TimeSpan TotalProcessorTime
        {
            get
            {
                if (!disposed)
                {
                    try { cpu = process.TotalProcessorTime; }
                    // macOS can report exit between the liveness and CPU queries using either exception type.
                    catch (Exception ex) when ((ex is System.ComponentModel.Win32Exception or InvalidOperationException) && process.HasExited) { }
                }
                return cpu;
            }
        }
        public bool ErrorLimitExceeded { get; private set; }
        public void RequestCancellation() => cancellation.Dispose();
        public void Kill() { if (!disposed && !process.HasExited) process.Kill(entireProcessTree: true); }
        public async ValueTask DisposeAsync()
        {
            if (disposed) return; Kill(); await process.WaitForExitAsync(); exit = (uint)process.ExitCode;
            ErrorLimitExceeded = await stderr; Reaped = true; cancellation.Dispose(); process.Dispose(); disposed = true;
        }
        private static async Task<bool> Drain(Process child)
        {
            char[] buffer = new char[1024]; int total = 0;
            while (await child.StandardError.ReadAsync(buffer) is int count && count != 0)
            {
                total += count;
                if (total >= 64 * 1024) { if (!child.HasExited) child.Kill(entireProcessTree: true); return true; }
            }
            return total != 0;
        }
        internal static Worker Start(string probe, Guid nonce, string role)
        {
            string root = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory()).Parent!.Parent!.Parent!.FullName;
            var start = new ProcessStartInfo(Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in new[] { probe, role, nonce.ToString("D") }) start.ArgumentList.Add(value);
            var cancellation = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.Out, HandleInheritability.Inheritable);
            start.ArgumentList.Add(ArchiveWorkerCancellation.Argument); start.ArgumentList.Add(cancellation.GetClientHandleAsString());
            try
            {
                var process = Process.Start(start)!; cancellation.DisposeLocalCopyOfClientHandle(); return new(process, cancellation);
            }
            catch { cancellation.Dispose(); throw; }
        }
    }
}
