using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.Jobs;

// Test-only actual update process for the Unix two-directory exchange. It runs the real reviewed plan, Unix job scope,
// preparation and coordinator; only the journal stream is wrapped so the process can stop immediately before or after one
// durable record. The test kills this process externally at that point. Production code has no crash hooks.
internal static class ArchiveUnixExchangeCrash
{
    internal static async Task<int> Run(string root, string point, string probe)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return 64;
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || !Path.IsPathFullyQualified(probe) || !File.Exists(probe)) return 64;
        var stop = CheckpointPoint.Parse(point);
        var ct = CancellationToken.None; var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        string target = Path.Combine(root, "original.tar"), added = Path.Combine(root, "added.bin");
        var workers = new List<ArchiveCreationCrash.Worker>();
        ArchiveCreationCrash.Worker Start(Guid nonce, string role) { var worker = ArchiveCreationCrash.Worker.Start(probe, nonce, role); lock (workers) workers.Add(worker); return worker; }
        var assessor = new ArchiveAssessmentClient((nonce, _) => Start(nonce, "archive-assess-v2"), protocolVersion: 2);
        var transform = new ArchiveTransformClient((nonce, _) => Start(nonce, "archive-transform-v2"), protocolVersion: 2);
        var verifier = new ArchiveVerificationClient((nonce, _) => Start(nonce, "archive-verify-v2"), protocolVersion: 2);
        var reviewed = await new ArchiveTargetReviewer(UnixArchiveTargetReadLease.Open, assessor).ReviewAsync(target, profile, null, ct);
        var request = reviewed.CreatePlan([new(ArchiveChangeKind.AddFile, "added.bin", added)]);
        var job = new Job(new JobRequest { Kind = JobKind.ArchiveUpdate, Archive = request }, "owned unix exchange crash", "owned", [], []);
        using var journal = JobJournal.Create(Path.Combine(root, "journals"), job);
        CheckpointStream.Install(journal, stop);
        var planning = new ArchiveJobPlanning(request, ct);
        using var scope = new UnixArchiveJobScope(request, ct, retainArtifacts: false);
        var original = scope.Original ?? throw new InvalidOperationException("The fixture archive was not opened.");
        var source = await assessor.AssessAsync(new(profile, original.Reviewed.Version.Length, original.Reviewed.Version.Sha256),
            () => original.File.OpenReadView(original.Reviewed, ct), ct);
        using var selection = planning.CaptureInputs(scope.OpenInputTree, ct) ?? throw new InvalidOperationException("No captured input.");
        var review = planning.Review(source, selection, selection.Inputs, ct);
        var plan = review.CreatePlan(false, true, ct, true);
        using var prepared = await ArchiveJournaledContainerPreparation.PrepareAsync(journal, request.ArchivePath, plan, original,
            selection.Inputs.Bindings, scope.Reserve, scope.Create, transform, verifier, ct);
        var published = scope.Publish(journal, prepared, selection.Inputs.Bindings, ct);
        scope.CleanupPublication(journal, published, ct);
        if (stop.Kind == "end") CheckpointStream.Wait(point);
        Console.WriteLine("archive-unix-exchange-unreached:" + point);
        return 65;
    }

    internal readonly record struct CheckpointPoint(string Kind, ArchiveExchangeUpdatePhase? Phase, int Cleaned)
    {
        internal static CheckpointPoint Parse(string text)
        {
            if (text == "end") return new("end", null, 0);
            string[] parts = text.Split(':');
            if (parts.Length is < 2 or > 3 || parts[0] is not ("before" or "after") ||
                !Enum.TryParse<ArchiveExchangeUpdatePhase>(parts[1], out var phase) || phase.ToString() != parts[1])
                throw new ArgumentException("Use before|after:Phase[:cleanedCount] or end.");
            return new(parts[0], phase, parts.Length == 3 ? int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) : -1);
        }
        internal bool Matches(string kind, ArchiveExchangeUpdatePhase phase, int cleaned) =>
            Kind == kind && Phase == phase && (Cleaned < 0 || Cleaned == cleaned);
    }

    /// <summary>Forwards every journal call to the real stream and stops around one exchange record.</summary>
    private sealed class CheckpointStream : FileStream
    {
        private readonly FileStream inner; private readonly CheckpointPoint stop; private (ArchiveExchangeUpdatePhase Phase, int Cleaned)? pending;
        private CheckpointStream(FileStream inner, CheckpointPoint stop) : base(inner.SafeFileHandle, FileAccess.Write, 1, false)
        { this.inner = inner; this.stop = stop; }
        internal static void Install(JobJournal journal, CheckpointPoint stop)
        {
            var field = typeof(JobJournal).GetField("_stream", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException("JobJournal._stream");
            field.SetValue(journal, new CheckpointStream((FileStream)field.GetValue(journal)!, stop));
        }
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var record = Exchange(buffer);
            if (record is { } r && stop.Matches("before", r.Phase, r.Cleaned)) Wait($"before:{r.Phase}:{r.Cleaned}");
            inner.Write(buffer); pending = record;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Flush(bool flushToDisk)
        {
            inner.Flush(flushToDisk);
            if (flushToDisk && pending is { } r && stop.Matches("after", r.Phase, r.Cleaned)) Wait($"after:{r.Phase}:{r.Cleaned}");
            if (flushToDisk) pending = null;
        }
        public override void Flush() => Flush(false);
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); }
        private static (ArchiveExchangeUpdatePhase Phase, int Cleaned)? Exchange(ReadOnlySpan<byte> line)
        {
            string text = Encoding.UTF8.GetString(line); int space = text.IndexOf(' ');
            if (space < 0 || JsonNode.Parse(text[(space + 1)..]) is not JsonObject node || node["t"]?.GetValue<string>() != "archive-exchange-update") return null;
            return (Enum.Parse<ArchiveExchangeUpdatePhase>(node["phase"]!.GetValue<string>()), node["cleaned"]!.GetValue<int>());
        }
        internal static void Wait(string point)
        {
            Console.WriteLine("archive-unix-exchange-checkpoint:" + point); Console.Out.Flush(); Console.ReadLine();
            throw new InvalidOperationException("The test must terminate this actual process externally.");
        }
    }
}
