using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Pipelines;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

// Test-only FZ07/FZ08 targets. Both call the real public parent conversations (assessment, fresh verification) through the
// real ArchiveWorkerSession; only the process boundary is replaced by an in-memory stand-in.

/// <summary>Small deterministic TARs and their real in-process assessments: the frozen expectations of FZ07/FZ08.</summary>
static class WorkerFixtures
{
    internal static readonly ArchiveWriteProfile Tar = new(ArchiveWriteFormat.Tar);
    internal static readonly byte[][] Archives = [Build(0), Build(1), Build(2)];
    private static ArchiveAssessment[]? assessments;
    internal static ArchiveAssessment Assessment(int index) => (assessments ??= Archives.Select(Assess).ToArray())[index];
    internal static ArchiveWriteManifest Manifest(int index) => Assessment(index).Manifest;
    internal static string Sha(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static ArchiveObjectVersion Version(byte[] bytes) => new("fuzz-stage", bytes.Length, Sha(bytes));
    internal static readonly ArchiveWorkerQueue Queue = new();

    private static byte[] Build(int shape)
    {
        using var output = new MemoryStream();
        using (var writer = new TarWriter(output, TarEntryFormat.Ustar, leaveOpen: true))
        {
            var time = DateTimeOffset.FromUnixTimeSeconds(1700000000);
            void File(string name, byte[] data) => writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name)
                { DataStream = new MemoryStream(data), ModificationTime = time, Mode = (UnixFileMode)0x1a4 });
            void Folder(string name) => writer.WriteEntry(new UstarTarEntry(TarEntryType.Directory, name) { ModificationTime = time, Mode = (UnixFileMode)0x1ed });
            switch (shape)
            {
                case 0: File("a.txt", "alpha"u8.ToArray()); break;
                case 1: Folder("d/"); File("d/b.bin", Enumerable.Range(0, 700).Select(i => (byte)(i * 7)).ToArray()); File("e", []); break;
                default: Folder("x/"); Folder("x/y/"); File("x/y/z.txt", "zeta"u8.ToArray()); File("x/w.txt", "omega omega"u8.ToArray()); break;
            }
        }
        return output.ToArray();
    }

    private static ArchiveAssessment Assess(byte[] archive)
    {
        var client = new ArchiveAssessmentClient((nonce, _) => new InProcessWorker(nonce, verification: false, ArchiveWorkerProtocol.SecretVersion),
            Queue, ArchiveWorkerProtocol.SecretVersion);
        return client.AssessAsync(new(Tar, archive.Length, Sha(archive)), () => new MemoryStream(archive, false), CancellationToken.None)
            .GetAwaiter().GetResult();
    }
}

/// <summary>The real worker role (assessment, fresh verification or transform) on a thread, connected by in-memory pipes.</summary>
sealed class InProcessWorker : IArchiveWorkerProcess
{
    private readonly Task<int> run;
    public Stream Input { get; }
    public Stream Output { get; }
    public InProcessWorker(Guid nonce, bool verification, ushort version)
        : this(nonce, version, verification ? ArchiveAssessmentWorker.RunVerificationAfterIsolation : ArchiveAssessmentWorker.RunAfterIsolation) { }
    internal static InProcessWorker Transform(Guid nonce, ushort version) => new(nonce, version, ArchiveTransformWorker.RunAfterIsolation);
    private InProcessWorker(Guid nonce, ushort version, Func<Stream, Stream, Guid, CancellationToken, ushort, int> role)
    {
        var toWorker = new Pipe(); var fromWorker = new Pipe();
        Input = toWorker.Writer.AsStream(); Output = fromWorker.Reader.AsStream();
        Stream workerInput = toWorker.Reader.AsStream(), workerOutput = fromWorker.Writer.AsStream();
        run = Task.Run(() =>
        {
            try { return role(workerInput, workerOutput, nonce, CancellationToken.None, version); }
            finally { workerOutput.Dispose(); workerInput.Dispose(); }
        });
    }
    public bool HasExited => run.IsCompleted;
    public uint ExitCode => run.IsCompletedSuccessfully ? unchecked((uint)run.Result) : 70u;
    public TimeSpan TotalProcessorTime => TimeSpan.Zero;
    public bool ErrorLimitExceeded => false;
    public void Kill() { Input.Dispose(); }
    public async ValueTask DisposeAsync()
    {
        Input.Dispose();
        try { await run.ConfigureAwait(false); } catch { /* The role's own failure is observed through ExitCode. */ }
        Output.Dispose();
    }
}

/// <summary>A worker whose complete output is a fixed script; everything the parent sends is discarded.</summary>
sealed class ScriptedWorker(byte[] output) : IArchiveWorkerProcess
{
    public Stream Input { get; } = Stream.Null;
    public Stream Output { get; } = new MemoryStream(output, writable: false);
    public bool HasExited => true;
    public uint ExitCode => 0;
    public TimeSpan TotalProcessorTime => TimeSpan.Zero;
    public bool ErrorLimitExceeded => false;
    public void Kill() { }
    public ValueTask DisposeAsync() => default;
}

/// <summary>
/// FZ08: hostile worker output against the real parent assessment and fresh-verification conversations.
/// Input: [mode][role][frames…]. mode bit0 = start with the raw script (no correct hello), bit1 = append the remainder
/// unframed, bit2 = protocol 1 instead of 2. Frame spec: [kind][length u16 LE][payload], encoded with the real framer
/// (correct header, nonce and sequence) so mutations reach the record and catalog logic behind the framing checks.
/// </summary>
static class WorkerFrames
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return false;
        byte mode = data[0]; int role = data[1] % 2; byte[] script = data[2..].ToArray();
        ushort version = (mode & 4) != 0 ? ArchiveWorkerProtocol.Version : ArchiveWorkerProtocol.SecretVersion;
        byte[] source = WorkerFixtures.Archives[data[1] / 2 % WorkerFixtures.Archives.Length];
        IArchiveWorkerProcess Launch(Guid nonce, TimeSpan cpu) => new ScriptedWorker(Encode(nonce, version, mode, role == 1, script));
        try
        {
            if (role == 0)
            {
                var result = new ArchiveAssessmentClient(Launch, WorkerFixtures.Queue, version)
                    .AssessAsync(new(WorkerFixtures.Tar, source.Length, WorkerFixtures.Sha(source)), () => new MemoryStream(source, false), CancellationToken.None)
                    .GetAwaiter().GetResult();
                // A parent may accept a worker's claims only for the exact request it sent.
                if (result.SourceLength != source.Length || result.SourceSha256 != WorkerFixtures.Sha(source) || result.Manifest.Profile.Format != ArchiveWriteFormat.Tar)
                    throw new InvalidOperationException("FZ08 oracle: an assessment receipt was accepted for another request.");
                return true;
            }
            int index = Array.IndexOf(WorkerFixtures.Archives, source);
            var verified = new ArchiveVerificationClient(Launch, WorkerFixtures.Queue, version)
                .VerifyAsync(WorkerFixtures.Version(source), WorkerFixtures.Manifest(index), () => new MemoryStream(source, false), CancellationToken.None)
                .GetAwaiter().GetResult();
            if (verified.ManifestDigest != WorkerFixtures.Manifest(index).Digest)
                throw new InvalidOperationException("FZ08 oracle: a verification receipt was accepted for another manifest.");
            return true;
        }
        catch (ArchiveWriteException) { return false; }
    }

    internal static byte[] Encode(Guid nonce, ushort version, byte mode, bool verification, byte[] script)
    {
        using var output = new MemoryStream();
        if ((mode & 1) != 0) { output.Write(script); return output.ToArray(); }
        var framer = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes, version);
        framer.Write(output, ArchiveWorkerMessage.Hello, ArchiveAssessmentWire.HelloFor(verification, version));
        int offset = 0;
        while (offset + 3 <= script.Length)
        {
            var kind = (ArchiveWorkerMessage)script[offset]; int length = BinaryPrimitives.ReadUInt16LittleEndian(script.AsSpan(offset + 1));
            offset += 3; length = Math.Min(length, script.Length - offset);
            try { framer.Write(output, kind, script.AsSpan(offset, length)); }
            catch (ArchiveWriteException) { break; } // The real framer refuses this frame; stop the script here.
            offset += length;
        }
        if ((mode & 2) != 0 && offset < script.Length) output.Write(script.AsSpan(offset));
        return output.ToArray();
    }

    /// <summary>Real worker transcripts for the seed corpus: the frames a correct worker sends for each fixture and role.</summary>
    internal static IEnumerable<(string Name, byte[] Seed)> Seeds()
    {
        for (int index = 0; index < WorkerFixtures.Archives.Length; index++)
            for (int role = 0; role < 2; role++)
            {
                byte[] source = WorkerFixtures.Archives[index]; List<(ArchiveWorkerMessage Kind, byte[] Payload)> frames = [];
                IArchiveWorkerProcess Launch(Guid nonce, TimeSpan cpu) => new RecordingWorker(new InProcessWorker(nonce, role == 1, ArchiveWorkerProtocol.SecretVersion), nonce, frames);
                if (role == 0)
                    new ArchiveAssessmentClient(Launch, WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
                        .AssessAsync(new(WorkerFixtures.Tar, source.Length, WorkerFixtures.Sha(source)), () => new MemoryStream(source, false), CancellationToken.None).GetAwaiter().GetResult();
                else
                    new ArchiveVerificationClient(Launch, WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
                        .VerifyAsync(WorkerFixtures.Version(source), WorkerFixtures.Manifest(index), () => new MemoryStream(source, false), CancellationToken.None).GetAwaiter().GetResult();
                using var seed = new MemoryStream(); seed.WriteByte(0); seed.WriteByte((byte)(index * 2 + role));
                foreach (var (kind, payload) in frames.Skip(1)) // The encoder supplies the hello.
                {
                    if (payload.Length > ushort.MaxValue) break;
                    seed.WriteByte((byte)kind); Span<byte> length = stackalloc byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(length, (ushort)payload.Length);
                    seed.Write(length); seed.Write(payload);
                }
                yield return ($"worker-{(role == 0 ? "assess" : "verify")}-{index}.bin", seed.ToArray());
            }
    }

    private sealed class RecordingWorker(InProcessWorker inner, Guid nonce, List<(ArchiveWorkerMessage, byte[])> frames) : IArchiveWorkerProcess
    {
        public Stream Input => inner.Input;
        public Stream Output { get; } = new Tap(inner.Output, nonce, frames);
        public bool HasExited => inner.HasExited;
        public uint ExitCode => inner.ExitCode;
        public TimeSpan TotalProcessorTime => inner.TotalProcessorTime;
        public bool ErrorLimitExceeded => inner.ErrorLimitExceeded;
        public void Kill() => inner.Kill();
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    /// <summary>Passes the worker's bytes through and parses a copy with a separate real reader to record each frame.</summary>
    private sealed class Tap(Stream inner, Guid nonce, List<(ArchiveWorkerMessage, byte[])> frames) : Stream
    {
        private readonly MemoryStream copy = new(); private readonly ArchiveWorkerProtocol reader = new(nonce, ArchiveWritePolicy.MaxWorkerWireBytes, ArchiveWorkerProtocol.SecretVersion);
        private long parsed;
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int read = inner.Read(buffer); copy.Write(buffer[..read]);
            while (true)
            {
                copy.Position = parsed;
                if (copy.Length - parsed < ArchiveWorkerProtocol.HeaderBytes) break;
                Span<byte> header = stackalloc byte[ArchiveWorkerProtocol.HeaderBytes]; copy.ReadExactly(header);
                long length = BinaryPrimitives.ReadUInt32LittleEndian(header[28..]);
                if (copy.Length - parsed < ArchiveWorkerProtocol.HeaderBytes + length) break;
                copy.Position = parsed; var frame = reader.Read(copy); frames.Add((frame.Kind, frame.Payload.ToArray()));
                parsed = copy.Position;
            }
            copy.Position = copy.Length; return read;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException(); public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}

/// <summary>
/// FZ07: hostile staged output against the real fresh verifier and its expected manifest. Input: [expectation][stage bytes].
/// Oracle: whatever the verifier accepts, an independent decoder (System.Formats.Tar) must read as exactly the expected
/// members, types, lengths and contents. A self-consistent checksum or our own writer's provenance never suffices.
/// </summary>
static class VerifyOutput
{
    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        if (data.Length < 1) return false;
        int index = data[0] % WorkerFixtures.Archives.Length; byte[] stage = data[1..].ToArray(); var expected = WorkerFixtures.Manifest(index);
        try
        {
            new ArchiveVerificationClient((nonce, _) => new InProcessWorker(nonce, verification: true, ArchiveWorkerProtocol.SecretVersion),
                    WorkerFixtures.Queue, ArchiveWorkerProtocol.SecretVersion)
                .VerifyAsync(WorkerFixtures.Version(stage), expected, () => new MemoryStream(stage, false), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ArchiveWriteException) { return false; }
        Independent(stage, index);
        return true;
    }

    private static readonly List<Member>?[] Baselines = new List<Member>?[WorkerFixtures.Archives.Length];
    internal readonly record struct Member(string Name, TarEntryType Type, long Length, string Sha, UnixFileMode Mode, int Uid, int Gid,
        string User, string Group, DateTimeOffset Modified);

    // The independent decoding of the accepted stage must equal the independent decoding of the unmutated fixture: names,
    // types, lengths, contents and every metadata field the TAR manifest binds (mode, owner IDs and names, time).
    private static void Independent(byte[] stage, int index)
    {
        var baseline = Baselines[index] ??= Decode(WorkerFixtures.Archives[index]);
        List<Member> actual;
        try { actual = Decode(stage); }
        catch (Exception error) when (error is InvalidDataException or FormatException or EndOfStreamException or ArgumentException or OverflowException)
        { throw new InvalidOperationException("FZ07 oracle: the fresh verifier accepted output an independent TAR decoder rejects.", error); }
        if (!actual.SequenceEqual(baseline))
            throw new InvalidOperationException("FZ07 oracle: the fresh verifier accepted output whose independent members or metadata differ from the expectation.");
    }

    internal static List<Member> Decode(byte[] archive)
    {
        var members = new List<Member>();
        using var reader = new TarReader(new MemoryStream(archive, false));
        while (reader.GetNextEntry(copyData: true) is { } entry)
        {
            if (members.Count > 4096) throw new InvalidOperationException("FZ07 oracle: verified output has more members than any expectation.");
            byte[] bytes = entry.DataStream is null ? [] : ReadAll(entry.DataStream);
            string user = entry is PosixTarEntry posix ? posix.UserName : "", group = entry is PosixTarEntry posixGroup ? posixGroup.GroupName : "";
            members.Add(new(entry.Name.TrimEnd('/'), entry.EntryType == TarEntryType.V7RegularFile ? TarEntryType.RegularFile : entry.EntryType,
                bytes.Length, WorkerFixtures.Sha(bytes), entry.Mode, entry.Uid, entry.Gid, user, group, entry.ModificationTime));
        }
        return members;
    }

    private static byte[] ReadAll(Stream stream) { using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); }
}
