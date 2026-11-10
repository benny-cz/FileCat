using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

// Test-only actual creation process for the Unix private stage folder. It runs the real plan, Unix job scope, preparation,
// new-file publication and exact cleanup; only the journal stream is wrapped so the process can stop immediately before or
// after one durable record. The test kills this process externally at that point. Production code has no crash hooks.
//   point: before|after:T:<ArchiveTransactionPhase> | before|after:C:<ArchiveCreationCleanupPhase>[:reviewedCount] | end
internal static class ArchiveUnixCreationCrash
{
    internal static async Task<int> Run(string root, string point, string probe)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return 64;
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || !Path.IsPathFullyQualified(probe) || !File.Exists(probe)) return 64;
        var stop = Point.Parse(point); var ct = CancellationToken.None; var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        string target = Path.Combine(root, "created.tar");
        var workers = new List<ArchiveCreationCrash.Worker>();
        ArchiveCreationCrash.Worker Start(Guid nonce, string role) { var worker = ArchiveCreationCrash.Worker.Start(probe, nonce, role); lock (workers) workers.Add(worker); return worker; }
        var transform = new ArchiveTransformClient((nonce, _) => Start(nonce, "archive-transform-v2"), protocolVersion: 2);
        var verifier = new ArchiveVerificationClient((nonce, _) => Start(nonce, "archive-verify-v2"), protocolVersion: 2);
        var request = new ArchivePlan(target, null, [new(ArchiveChangeKind.AddFile, "kept.txt", Path.Combine(root, "source.txt"))], Profile: profile);
        var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = request }, "owned unix creation crash", "owned", [], []);
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), job);
        CheckpointStream.Install(journal, stop);
        var planning = new ArchiveJobPlanning(request, ct);
        using var scope = new UnixArchiveJobScope(request, ct, retainArtifacts: false);
        using var selection = planning.CaptureInputs(scope.OpenInputTree, ct) ?? throw new InvalidOperationException("No captured input.");
        var review = planning.Review(null, selection, selection.Inputs, ct);
        var plan = review.CreatePlan(false, true, ct, true);
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, request.ArchivePath, plan, null,
            selection.Inputs.Bindings, scope.Reserve, scope.Create, transform, verifier, ct);
        var published = scope.Publish(journal, prepared, selection.Inputs.Bindings, ct);
        scope.CleanupPublication(journal, published, ct);
        if (stop.Kind == "end") CheckpointStream.Wait(point);
        Console.WriteLine("archive-unix-creation-unreached:" + point);
        return 65;
    }

    internal readonly record struct Point(string Kind, string Record, string? Phase, int Reviewed)
    {
        internal static Point Parse(string text)
        {
            if (text == "end") return new("end", "", null, -1);
            string[] parts = text.Split(':');
            if (parts.Length is < 3 or > 4 || parts[0] is not ("before" or "after") || parts[1] is not ("T" or "C") ||
                (parts[1] == "T" ? !Enum.TryParse<ArchiveTransactionPhase>(parts[2], out _) : !Enum.TryParse<ArchiveCreationCleanupPhase>(parts[2], out _)) ||
                parts.Length == 4 && parts[1] != "C")
                throw new ArgumentException("Use before|after:T:Phase, before|after:C:Phase[:reviewed] or end.");
            return new(parts[0], parts[1], parts[2], parts.Length == 4 ? int.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture) : -1);
        }
        internal bool Matches(string kind, string record, string phase, int reviewed) =>
            Kind == kind && Record == record && Phase == phase && (Reviewed < 0 || Reviewed == reviewed);
    }

    /// <summary>Forwards every journal call to the real stream and stops around one transaction or cleanup record.</summary>
    private sealed class CheckpointStream : FileStream
    {
        private readonly FileStream inner; private readonly Point stop; private (string Record, string Phase, int Reviewed)? pending;
        private CheckpointStream(FileStream inner, Point stop) : base(inner.SafeFileHandle, FileAccess.Write, 1, false)
        { this.inner = inner; this.stop = stop; }
        internal static void Install(JobJournal journal, Point stop)
        {
            var field = typeof(JobJournal).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("JobJournal._stream");
            field.SetValue(journal, new CheckpointStream((FileStream)field.GetValue(journal)!, stop));
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var record = Record(buffer);
            if (record is { } r && stop.Matches("before", r.Record, r.Phase, r.Reviewed)) Wait($"before:{r.Record}:{r.Phase}:{r.Reviewed}");
            inner.Write(buffer); pending = record;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Flush(bool flushToDisk)
        {
            inner.Flush(flushToDisk);
            if (flushToDisk && pending is { } r && stop.Matches("after", r.Record, r.Phase, r.Reviewed)) Wait($"after:{r.Record}:{r.Phase}:{r.Reviewed}");
            if (flushToDisk) pending = null;
        }
        public override void Flush() => Flush(false);
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
        private static (string Record, string Phase, int Reviewed)? Record(ReadOnlySpan<byte> line)
        {
            string text = Encoding.UTF8.GetString(line); int space = text.IndexOf(' ');
            if (space < 0 || JsonNode.Parse(text[(space + 1)..]) is not JsonObject node) return null;
            return node["t"]?.GetValue<string>() switch
            {
                "archive-transaction" => ("T", node["phase"]!.GetValue<string>(), -1),
                ArchiveCreationCleanupFacts.RecordType => ("C", node["phase"]!.GetValue<string>(), node["reviewed"]!.GetValue<int>()),
                _ => null,
            };
        }
        internal static void Wait(string point)
        {
            Console.WriteLine("archive-unix-creation-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
            throw new InvalidOperationException("The test must terminate this actual process externally.");
        }
    }
}
