using System.Text;
using FileCat.Core.Archives;

/// <summary>
/// FZ04: member keys with corrupted catalog revision, name, type or occurrence against the pinned fixture catalog.
/// Input: [fixture][edit count] then per edit [kind][corruption][member][payload/name]. Oracle: a plan whose key does not name
/// exactly one member of the pinned catalog (same revision, raw name, type and occurrence) is refused; an accepted plan is
/// written, verified and independently decoded, and every member no edit names is preserved.
/// </summary>
static class MemberKeys
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        var reader = new FuzzReader(data);
        int index = reader.Byte() % WorkerFixtures.Archives.Length; var source = WorkerFixtures.Assessment(index); byte[] original = WorkerFixtures.Archives[index];
        var members = source.Manifest.Members; var edits = new List<ArchiveTransformEdit>(); var inputs = new List<ArchiveTransformInput>();
        var payloads = new Dictionary<int, byte[]>(); var touched = new HashSet<string>(StringComparer.Ordinal); bool forged = false;
        int count = 1 + reader.Byte() % 3;
        for (int i = 0; i < count && !reader.End; i++)
        {
            byte kind = reader.Byte(), corruption = reader.Byte(); var member = members[reader.Byte() % members.Count];
            string revision = source.CatalogRevision, name = member.Name; var type = member.Type; int occurrence = member.Occurrence;
            switch (corruption % 6)
            {
                case 1: revision = WorkerFixtures.Assessment((index + 1) % WorkerFixtures.Archives.Length).CatalogRevision; break; // Stale catalog.
                case 2: revision = Convert.ToHexString(Encoding.UTF8.GetBytes(reader.Text(32))).PadRight(64, '0')[..64]; break;
                case 3: name = reader.Text(64); break;
                case 4: type = type == ArchiveMemberType.File ? ArchiveMemberType.Directory : ArchiveMemberType.File; break;
                case 5: occurrence = reader.Byte() % 4; break;
            }
            bool exists = revision == source.CatalogRevision && members.Any(m => m.Name == name && m.Type == type && m.Occurrence == occurrence);
            forged |= !exists; touched.Add(name.TrimEnd('/'));
            ArchiveMemberKey key;
            try { key = new(revision, name, type, occurrence); }
            catch (Exception error) when (error is ArgumentException or ArchiveWriteException) { return false; } // Malformed tags are refused.
            switch (kind % 3)
            {
                case 0: edits.Add(new(ArchiveChangeKind.Delete, key)); break;
                case 1:
                    byte[] payload = reader.Bytes(reader.Byte()); int id = inputs.Count + 1; payloads[id] = payload;
                    inputs.Add(new(id, payload.Length, WorkerFixtures.Sha(payload), DateTimeOffset.FromUnixTimeSeconds(1700000500)));
                    edits.Add(new(ArchiveChangeKind.Replace, key, inputId: id)); break;
                default: edits.Add(new(ArchiveChangeKind.Rename, key, "renamed-" + i + (type == ArchiveMemberType.Directory ? "/" : ""))); break;
            }
        }
        ArchiveTransformInvocation invocation;
        try { invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(source, inputs, edits, [], CancellationToken.None)); }
        catch (Exception error) when (error is ArchiveWriteException or ArgumentException) { return false; }
        if (forged) throw new InvalidOperationException("FZ04 oracle: a plan accepted a member key that names no member of the pinned catalog.");
        try
        {
            byte[] stage = WriterRun.RebuildAndVerify(invocation, () => new MemoryStream(original, false), id => new MemoryStream(payloads[id], false));
            var actual = WriterRun.Check(invocation, stage)!;
            var before = VerifyOutput.Decode(original).Where(m => !touched.Contains(m.Name)).ToArray();
            if (!actual.Where(m => before.Any(b => b.Name == m.Name)).SequenceEqual(before))
                throw new InvalidOperationException("FZ04 oracle: a member no key named changed, moved or lost metadata.");
            return true;
        }
        catch (ArchiveWriteException) { return false; }
    }
}

/// <summary>
/// FZ12: repeated assessment, verification and transform conversations in one process, each cancelled at an arbitrary read.
/// Input: up to eight [operation][cancel at read k, 0 = never]. Oracle: each operation either succeeds with the exact
/// expected result or ends cancelled/refused with a typed outcome; the worker queue is free after every operation; a final
/// clean operation still succeeds, so no cancellation leaves poisoned shared state.
/// </summary>
static class RepeatedCancellation
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        var reader = new FuzzReader(data); int operations = 0; bool cancelledAny = false;
        while (!reader.End && operations++ < 8)
        {
            byte operation = reader.Byte(); int cancelAt = reader.Byte() % 24; int index = operation / 3 % WorkerFixtures.Archives.Length;
            byte[] archive = WorkerFixtures.Archives[index];
            using var cts = new CancellationTokenSource(); int reads = 0;
            Stream Source(byte[] bytes) => new CancelAtRead(new MemoryStream(bytes, false), () => { if (++reads == cancelAt) cts.Cancel(); });
            try
            {
                switch (operation % 3)
                {
                    case 0:
                    {
                        var result = new ArchiveAssessmentClient((nonce, _) => new InProcessWorker(nonce, verification: false, ArchiveWorkerProtocol.SecretVersion),
                                WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
                            .AssessAsync(new(WorkerFixtures.Tar, archive.Length, WorkerFixtures.Sha(archive)), () => Source(archive), cts.Token).GetAwaiter().GetResult();
                        if (result.Manifest.Digest != WorkerFixtures.Manifest(index).Digest) throw new InvalidOperationException("FZ12 oracle: assessment result changed.");
                        break;
                    }
                    case 1:
                    {
                        var verified = new ArchiveVerificationClient((nonce, _) => new InProcessWorker(nonce, verification: true, ArchiveWorkerProtocol.SecretVersion),
                                WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
                            .VerifyAsync(WorkerFixtures.Version(archive), WorkerFixtures.Manifest(index), () => Source(archive), cts.Token).GetAwaiter().GetResult();
                        if (verified.ManifestDigest != WorkerFixtures.Manifest(index).Digest) throw new InvalidOperationException("FZ12 oracle: verification result changed.");
                        break;
                    }
                    default:
                    {
                        byte[] payload = Enumerable.Range(0, 5000).Select(i => (byte)(i ^ index)).ToArray();
                        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(WorkerFixtures.Assessment(index),
                            [new(1, payload.Length, WorkerFixtures.Sha(payload), DateTimeOffset.FromUnixTimeSeconds(1700000600))], [],
                            [new("repeat/added.bin", ArchiveMemberType.File, 1)], CancellationToken.None));
                        using var stage = new MemoryStream();
                        new ArchiveTransformClient((nonce, _) => InProcessWorker.Transform(nonce, ArchiveWorkerProtocol.SecretVersion), WorkerFixtures.Queue,
                                ArchiveWorkerProtocol.SecretVersion)
                            .RebuildAsync(invocation, new WriterRun.NonClosing(stage), () => Source(archive), _ => Source(payload), cts.Token).GetAwaiter().GetResult();
                        break;
                    }
                }
                if (cts.IsCancellationRequested) throw new InvalidOperationException("FZ12 oracle: a cancelled conversation reported success.");
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested) { cancelledAny = true; }
            catch (ArchiveWriteException) when (cts.IsCancellationRequested) { cancelledAny = true; }
            // The queue reservation lasts through actual worker exit; it must be free again now.
            var entered = WorkerFixtures.Queue.EnterAsync(CancellationToken.None).AsTask();
            if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("FZ12 oracle: the worker queue stayed reserved after an operation.");
            entered.Result.Dispose();
        }
        // No shared state is poisoned: a clean assessment still returns the exact fixture catalog.
        var clean = new ArchiveAssessmentClient((nonce, _) => new InProcessWorker(nonce, verification: false, ArchiveWorkerProtocol.SecretVersion),
                WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
            .AssessAsync(new(WorkerFixtures.Tar, WorkerFixtures.Archives[0].Length, WorkerFixtures.Sha(WorkerFixtures.Archives[0])),
                () => new MemoryStream(WorkerFixtures.Archives[0], false), CancellationToken.None).GetAwaiter().GetResult();
        if (clean.Manifest.Digest != WorkerFixtures.Manifest(0).Digest) throw new InvalidOperationException("FZ12 oracle: shared state changed a clean result.");
        return !cancelledAny;
    }

    private sealed class CancelAtRead(Stream inner, Action read) : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) { read(); return inner.Read(buffer); }
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void Flush() { } public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}

static class StatefulSeeds
{
    internal static IEnumerable<(string Name, byte[] Seed)> Keys()
    {
        for (int fixture = 0; fixture < WorkerFixtures.Archives.Length; fixture++)
            for (int corruption = 0; corruption < 6; corruption++)
                yield return ($"keys-{fixture}-{corruption}.bin", [(byte)fixture, 0, 2, (byte)corruption, 0, 0, 5, 104, 101, 108, 108, 111]);
    }
    internal static IEnumerable<(string Name, byte[] Seed)> Repeats()
    {
        yield return ("repeat-clean.bin", [0, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0]);
        yield return ("repeat-cancel-early.bin", [0, 1, 1, 1, 2, 1, 0, 0]);
        yield return ("repeat-cancel-late.bin", [0, 6, 1, 9, 2, 12, 5, 0]);
    }
}
