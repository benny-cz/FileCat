using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using FileCat.Core.Archives;

// Test-only writer-side targets. Each builds a real in-memory transform plan, runs the real transform worker role and the
// real fresh verifier (in-process, through the real parent conversations), then checks the result independently.

/// <summary>Bounded reader of fuzz bytes; every read is clamped, so any input decodes to some request.</summary>
ref struct FuzzReader(ReadOnlySpan<byte> data)
{
    private readonly ReadOnlySpan<byte> data = data;
    private int offset;
    internal readonly bool End => offset >= data.Length;
    internal byte Byte() => offset < data.Length ? data[offset++] : (byte)0;
    internal ushort UInt16() => (ushort)(Byte() | Byte() << 8);
    internal int Int32() => Byte() | Byte() << 8 | Byte() << 16 | Byte() << 24;
    internal byte[] Bytes(int count) { count = Math.Clamp(count, 0, data.Length - offset); var value = data.Slice(offset, count).ToArray(); offset += count; return value; }
    internal string Text(int maximum)
    {
        int length = Math.Min(Byte(), maximum);
        // Mostly printable names keep mutations near valid paths; raw bytes still reach invalid UTF-8 and control cases.
        return Encoding.UTF8.GetString(Bytes(length));
    }
}

static class WriterRun
{
    internal static readonly ArchiveWriteFormat[] Formats = [ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd];
    private const ushort Version = ArchiveWorkerProtocol.SecretVersion;

    /// <summary>Rebuild through the real transform role, then require the real fresh verifier to accept the stage.</summary>
    internal static byte[] RebuildAndVerify(ArchiveTransformInvocation invocation, Func<Stream> original, Func<int, Stream> input, Func<Stream, Stream>? wrapStage = null)
    {
        using var stage = new MemoryStream();
        using (var target = wrapStage?.Invoke(stage) ?? new NonClosing(stage))
            new ArchiveTransformClient((nonce, _) => InProcessWorker.Transform(nonce, Version), WorkerFixtures.Queue, Version)
                .RebuildAsync(invocation, target, original, input, CancellationToken.None).GetAwaiter().GetResult();
        byte[] bytes = stage.ToArray();
        try
        {
            var verified = new ArchiveVerificationClient((nonce, _) => new InProcessWorker(nonce, verification: true, Version), WorkerFixtures.Queue, Version)
                .VerifyAsync(WorkerFixtures.Version(bytes), invocation.Expected, () => new MemoryStream(bytes, false), CancellationToken.None).GetAwaiter().GetResult();
            if (verified.ManifestDigest != invocation.Expected.Digest) throw new InvalidOperationException("Writer oracle: verification returned another manifest.");
        }
        catch (ArchiveWriteException error)
        { throw new InvalidOperationException("Writer oracle: the fresh verifier rejected a stage the real writer completed: " + error.Reason, error); }
        return bytes;
    }

    /// <summary>The plain TAR bytes of a stage, decompressed by the BCL where an independent decoder exists.</summary>
    internal static byte[]? IndependentTar(ArchiveWriteFormat format, byte[] stage)
    {
        if (format == ArchiveWriteFormat.Tar) return stage;
        if (format != ArchiveWriteFormat.TarGzip) return null; // No independent BCL decoder for BZip2/Zstandard here.
        using var gzip = new GZipStream(new MemoryStream(stage, false), CompressionMode.Decompress);
        using var copy = new MemoryStream(); gzip.CopyTo(copy); return copy.ToArray();
    }

    /// <summary>Independent decoding must equal the plan's expected members: names, types, lengths and contents.</summary>
    internal static List<VerifyOutput.Member>? Check(ArchiveTransformInvocation invocation, byte[] stage)
    {
        byte[]? tar = IndependentTar(invocation.Expected.Profile.Format, stage);
        if (tar is null) return null;
        List<VerifyOutput.Member> actual;
        try { actual = VerifyOutput.Decode(tar); }
        catch (Exception error) when (error is InvalidDataException or FormatException or EndOfStreamException or ArgumentException or OverflowException)
        { throw new InvalidOperationException("Writer oracle: an independent TAR decoder rejects verified writer output.", error); }
        var expected = invocation.Expected.Members.Select(m => (m.Name.TrimEnd('/'), m.Type, m.Length, m.Type == ArchiveMemberType.File ? m.Sha256 : WorkerFixtures.Sha([]))).ToArray();
        var observed = actual.Select(m => (m.Name, m.Type == TarEntryType.Directory ? ArchiveMemberType.Directory : ArchiveMemberType.File, m.Length, m.Sha)).ToArray();
        if (!observed.SequenceEqual(expected)) throw new InvalidOperationException("Writer oracle: independent members differ from the plan's expectation.");
        return actual;
    }

    internal static Stream Throwing() => throw new InvalidOperationException("No archive grant exists for this transform.");

    /// <summary>The stage remains readable after the client disposes its writer.</summary>
    internal sealed class NonClosing(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin); public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
    }
}

/// <summary>
/// FZ05: arbitrary create requests. Input: [format][count] then per member [type][name][length u16][payload][mode u16][time i32].
/// Oracle: the real plan refuses or the real writer's stage passes the fresh verifier and an independent decoder agrees.
/// </summary>
static class CreateInputs
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        var reader = new FuzzReader(data);
        var format = WriterRun.Formats[reader.Byte() % WriterRun.Formats.Length]; int count = reader.Byte() % 9;
        var payloads = new Dictionary<int, byte[]>(); var inputs = new List<ArchiveTransformInput>(); var additions = new List<ArchiveTransformAddition>();
        try
        {
            for (int i = 0; i < count && !reader.End; i++)
            {
                bool folder = (reader.Byte() & 1) == 1; string name = reader.Text(160);
                byte[] payload = reader.Bytes(reader.UInt16() % 4097); int mode = reader.UInt16() & 0xfff; var time = ArchiveTimestamp.FromUnixSeconds(reader.Int32() & 0x7fffffff);
                if (folder) { additions.Add(new(name, new ArchiveTransformDirectoryMetadata(time, mode))); continue; }
                int id = i + 1; payloads[id] = payload;
                inputs.Add(new(id, payload.Length, WorkerFixtures.Sha(payload), time, mode)); additions.Add(new(name, ArchiveMemberType.File, id));
            }
            var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(new ArchiveWriteProfile(format), null, inputs, [], additions, CancellationToken.None));
            byte[] stage = WriterRun.RebuildAndVerify(invocation, WriterRun.Throwing, id => new MemoryStream(payloads[id], false));
            WriterRun.Check(invocation, stage);
            return true;
        }
        catch (ArchiveWriteException) { return false; }
        catch (ArgumentException) { return false; } // Public request constructors validate names, modes and times.
    }
}

/// <summary>
/// FZ06: a fixture source plus arbitrary delete/replace/rename edits and additions. Input: [fixture][edits][additions] with
/// per edit [kind][member][new name][payload]. Oracle: refusal, or a verified stage whose independent decoding equals the
/// plan, where every member no edit names is preserved byte for byte with its metadata.
/// </summary>
static class UpdateChanges
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        var reader = new FuzzReader(data);
        int index = reader.Byte() % WorkerFixtures.Archives.Length; var source = WorkerFixtures.Assessment(index); byte[] original = WorkerFixtures.Archives[index];
        var members = source.Manifest.Members; var payloads = new Dictionary<int, byte[]>(); var inputs = new List<ArchiveTransformInput>();
        var edits = new List<ArchiveTransformEdit>(); var additions = new List<ArchiveTransformAddition>(); var touched = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            int editCount = reader.Byte() % 5, additionCount = reader.Byte() % 4, nextId = 1;
            for (int i = 0; i < editCount && !reader.End; i++)
            {
                byte kind = reader.Byte(); var member = members[reader.Byte() % members.Count];
                var key = new ArchiveMemberKey(source.CatalogRevision, member.Name, member.Type, member.Occurrence); touched.Add(member.Name.TrimEnd('/'));
                switch (kind % 3)
                {
                    case 0: edits.Add(new(ArchiveChangeKind.Delete, key)); break;
                    case 1:
                        byte[] payload = reader.Bytes(reader.UInt16() % 2049); int id = nextId++; payloads[id] = payload;
                        inputs.Add(new(id, payload.Length, WorkerFixtures.Sha(payload), DateTimeOffset.FromUnixTimeSeconds(1700000100)));
                        edits.Add(new(ArchiveChangeKind.Replace, key, inputId: id)); break;
                    default: edits.Add(new(ArchiveChangeKind.Rename, key, reader.Text(96))); break;
                }
            }
            for (int i = 0; i < additionCount && !reader.End; i++)
            {
                string name = reader.Text(96);
                if ((reader.Byte() & 1) == 1) { additions.Add(new(name, new ArchiveTransformDirectoryMetadata(ArchiveTimestamp.FromUnixSeconds(1700000200)))); continue; }
                byte[] payload = reader.Bytes(reader.UInt16() % 2049); int id = nextId++; payloads[id] = payload;
                inputs.Add(new(id, payload.Length, WorkerFixtures.Sha(payload), DateTimeOffset.FromUnixTimeSeconds(1700000300)));
                additions.Add(new(name, ArchiveMemberType.File, id));
            }
            var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(source, inputs, edits, additions, CancellationToken.None));
            byte[] stage = WriterRun.RebuildAndVerify(invocation, () => new MemoryStream(original, false), id => new MemoryStream(payloads[id], false));
            var actual = WriterRun.Check(invocation, stage)!;
            // Members no edit names keep their relative order, bytes and every TAR metadata field.
            var before = VerifyOutput.Decode(original).Where(m => !touched.Contains(m.Name)).ToArray();
            var kept = actual.Where(m => before.Any(b => b.Name == m.Name)).ToArray();
            if (!kept.SequenceEqual(before)) throw new InvalidOperationException("FZ06 oracle: a member no edit named changed, moved or lost metadata.");
            return true;
        }
        catch (ArchiveWriteException) { return false; }
        catch (ArgumentException) { return false; }
    }
}

/// <summary>
/// FZ11: short, truncated, throwing and nonseekable streams at the real parent boundaries. Input: [scenario][fault program].
/// Oracle: a faulted conversation is refused with a typed reason, never reported as success with incomplete data; a stage
/// write or flush failure never yields a transform receipt; success always equals the unfaulted result.
/// </summary>
static class StreamFaults
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1) return false;
        byte scenario = data[0]; var program = data[1..].ToArray(); int index = scenario / 4 % WorkerFixtures.Archives.Length;
        byte[] archive = WorkerFixtures.Archives[index];
        try
        {
            switch (scenario % 4)
            {
                case 0: // Assessment source.
                {
                    var faults = new List<FaultStream>();
                    var result = new ArchiveAssessmentClient((nonce, _) => new InProcessWorker(nonce, verification: false, ArchiveWorkerProtocol.SecretVersion),
                            WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
                        .AssessAsync(new(WorkerFixtures.Tar, archive.Length, WorkerFixtures.Sha(archive)),
                            () => { var stream = new FaultStream(new MemoryStream(archive, false), program, writable: false); faults.Add(stream); return stream; }, CancellationToken.None)
                        .GetAwaiter().GetResult();
                    if (result.Manifest.Digest != WorkerFixtures.Manifest(index).Digest)
                        throw new InvalidOperationException("FZ11 oracle: a faulted source produced a different accepted assessment.");
                    return true;
                }
                case 1: // Fresh verification source.
                {
                    var verified = new ArchiveVerificationClient((nonce, _) => new InProcessWorker(nonce, verification: true, ArchiveWorkerProtocol.SecretVersion),
                            WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
                        .VerifyAsync(WorkerFixtures.Version(archive), WorkerFixtures.Manifest(index),
                            () => new FaultStream(new MemoryStream(archive, false), program, writable: false), CancellationToken.None).GetAwaiter().GetResult();
                    if (verified.ManifestDigest != WorkerFixtures.Manifest(index).Digest) throw new InvalidOperationException("FZ11 oracle: a faulted stage verified as another manifest.");
                    return true;
                }
                default: // Transform input grants (2) or the stage writer (3).
                {
                    byte[] payload = Enumerable.Range(0, 3000).Select(i => (byte)(i * 31 + index)).ToArray();
                    var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(WorkerFixtures.Assessment(index),
                        [new(1, payload.Length, WorkerFixtures.Sha(payload), DateTimeOffset.FromUnixTimeSeconds(1700000400))], [],
                        [new("fault/added.bin", ArchiveMemberType.File, 1)], CancellationToken.None));
                    FaultStream? stageFault = null;
                    byte[] stage = WriterRun.RebuildAndVerify(invocation, () => new MemoryStream(archive, false),
                        _ => scenario % 4 == 2 ? new FaultStream(new MemoryStream(payload, false), program, writable: false) : new MemoryStream(payload, false),
                        scenario % 4 == 3 ? inner => stageFault = new FaultStream(inner, program, writable: true) : null);
                    if (stageFault is { Faulted: true }) throw new InvalidOperationException("FZ11 oracle: a stage write or flush failure still produced a receipt.");
                    WriterRun.Check(invocation, stage);
                    return true;
                }
            }
        }
        catch (ArchiveWriteException) { return false; }
    }
}

/// <summary>A stream that follows a fault program: short operations, early end, IOException and refused seeking.</summary>
sealed class FaultStream(Stream inner, byte[] program, bool writable) : Stream
{
    private int step;
    internal bool Faulted { get; private set; }
    private byte Next() => program.Length == 0 ? (byte)0 : program[step++ % program.Length];
    // A parent-owned stage must stay readable and seekable; only its writes, flushes and length changes fail.
    private bool Seekable => writable || program.Length == 0 || (program[0] & 0x80) == 0;
    public override bool CanRead => true; public override bool CanSeek => Seekable; public override bool CanWrite => writable;
    public override long Length => Seekable ? inner.Length : throw new NotSupportedException();
    public override long Position { get => Seekable ? inner.Position : throw new NotSupportedException(); set { if (!Seekable) throw new NotSupportedException(); inner.Position = value; } }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (writable) return inner.Read(buffer);
        byte op = Next();
        switch (op % 16)
        {
            case 0: Faulted = true; throw new IOException("Injected read failure.");
            case 1: if (inner.Position < inner.Length) Faulted = true; return 0; // Early end.
            case 2 or 3 or 4: return inner.Read(buffer[..Math.Min(buffer.Length, 1 + op % 7)]); // Short read.
            default: return inner.Read(buffer);
        }
    }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (Next() % 16 == 0) { Faulted = true; throw new IOException("Injected write failure."); }
        inner.Write(buffer);
    }
    public override void Flush() { if (writable && Next() % 32 == 1) { Faulted = true; throw new IOException("Injected flush failure."); } inner.Flush(); }
    public override long Seek(long offset, SeekOrigin origin) => Seekable ? inner.Seek(offset, origin) : throw new NotSupportedException();
    public override void SetLength(long value) { if (writable && Next() % 32 == 2) { Faulted = true; throw new IOException("Injected length failure."); } inner.SetLength(value); }
    protected override void Dispose(bool disposing) { if (disposing && !writable) inner.Dispose(); base.Dispose(disposing); }
}

/// <summary>Valid seed encodings for the writer-side targets, so mutation starts from complete requests.</summary>
static class WriterSeeds
{
    private static void Name(MemoryStream seed, string name) { byte[] bytes = Encoding.UTF8.GetBytes(name); seed.WriteByte((byte)bytes.Length); seed.Write(bytes); }
    private static void UInt16(MemoryStream seed, int value) { seed.WriteByte((byte)value); seed.WriteByte((byte)(value >> 8)); }
    private static void Int32(MemoryStream seed, int value) { for (int i = 0; i < 4; i++) seed.WriteByte((byte)(value >> (8 * i))); }

    internal static IEnumerable<(string Name, byte[] Seed)> Create()
    {
        for (int format = 0; format < WriterRun.Formats.Length; format++)
        {
            using var seed = new MemoryStream(); seed.WriteByte((byte)format); seed.WriteByte(3);
            seed.WriteByte(1); Name(seed, "docs/"); UInt16(seed, 0); UInt16(seed, 0x1ed); Int32(seed, 1700000000);
            seed.WriteByte(0); Name(seed, "docs/readme.txt"); byte[] text = "readme text"u8.ToArray(); UInt16(seed, text.Length); seed.Write(text); UInt16(seed, 0x1a4); Int32(seed, 1700000001);
            seed.WriteByte(0); Name(seed, "é/暗号.bin"); byte[] binary = Enumerable.Range(0, 300).Select(i => (byte)(i * 13)).ToArray(); UInt16(seed, binary.Length); seed.Write(binary); UInt16(seed, 0x180); Int32(seed, 1700000002);
            yield return ($"create-{format}.bin", seed.ToArray());
        }
    }

    internal static IEnumerable<(string Name, byte[] Seed)> Update()
    {
        for (int fixture = 0; fixture < WorkerFixtures.Archives.Length; fixture++)
        {
            using var seed = new MemoryStream(); seed.WriteByte((byte)fixture); seed.WriteByte(3); seed.WriteByte(2);
            seed.WriteByte(0); seed.WriteByte(0);                                   // Delete the first member.
            seed.WriteByte(1); seed.WriteByte(1); byte[] replacement = "replacement"u8.ToArray(); UInt16(seed, replacement.Length); seed.Write(replacement);
            seed.WriteByte(2); seed.WriteByte(2); Name(seed, "renamed-member");      // Rename another.
            Name(seed, "added/"); seed.WriteByte(1);                                 // Folder addition.
            Name(seed, "added/file.txt"); seed.WriteByte(0); byte[] added = "added"u8.ToArray(); UInt16(seed, added.Length); seed.Write(added);
            yield return ($"update-{fixture}.bin", seed.ToArray());
        }
    }

    internal static IEnumerable<(string Name, byte[] Seed)> Faults()
    {
        for (int scenario = 0; scenario < 4; scenario++)
        {
            yield return ($"faults-{scenario}-clean.bin", [(byte)scenario, 5, 6, 7, 8]);
            yield return ($"faults-{scenario}-short.bin", [(byte)(scenario + 4), 2, 3, 4, 5]);
            yield return ($"faults-{scenario}-error.bin", [(byte)(scenario + 8), 5, 5, 0]);
        }
    }
}
