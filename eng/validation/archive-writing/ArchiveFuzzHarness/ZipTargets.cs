using System.IO.Compression;
using System.Text;
using FileCat.Core.Archives;

// Test-only AW2 task 5 target: the worker ZIP profile through the real plan, the real transform and assessment worker roles
// (in-process) and the real fresh verifier, with the runtime's ZipArchive as the independent oracle.

/// <summary>
/// Input: [level][mode][rest].
/// Mode 0 (FZ05): [count][name, payload]… become a new ZIP; the fresh verifier accepts it and the runtime reads every payload.
/// Mode 1 (FZ06): [count][name, payload]… make a source with the runtime's writer, which is assessed by the real worker; then
///   [shape][payload] deletes, renames, replaces or adds one member (or attempts a forbidden change that must be refused), and
///   the runtime reads exactly the expected members.
/// Mode 2 (FZ07): [payload][position, xor…] mutates a valid stage; whatever the fresh verifier accepts reads as the payload.
/// Mode 3 (assessment): [bytes] are assessed as a ZIP: a typed refusal, or members whose order, names, lengths and SHA-256 equal
///   the runtime's reading of the same bytes.
/// </summary>
static class ZipArchives
{
    private const ushort Version = ArchiveWorkerProtocol.SecretVersion;

    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        var reader = new FuzzReader(data);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Zip, compressionLevel: reader.Byte() % 4);
        int mode = reader.Byte() % 4;
        try
        {
            switch (mode)
            {
                case 0:
                {
                    var members = Members(ref reader, 8);
                    var inputs = members.Select((m, i) => Input(m.Payload, i + 1)).ToArray();
                    var plan = ArchiveZipTransformPlan.Create(profile, null, inputs, [],
                        members.Select((m, i) => new ArchiveTransformAddition(m.Name, ArchiveMemberType.File, i + 1)).ToArray(), CancellationToken.None);
                    byte[] stage = WriterRun.RebuildAndVerify(ArchiveTransformWire.Prepare(plan, CancellationToken.None), WriterRun.Throwing,
                        id => new MemoryStream(members[id - 1].Payload, false));
                    Same(members.Select(m => (m.Name, m.Payload)).ToArray(), Read(stage), "created ZIP");
                    return true;
                }
                case 1:
                {
                    var members = Members(ref reader, 6); byte shape = reader.Byte(); byte[] payload = reader.Bytes(reader.UInt16() % 4097);
                    if (members.Length == 0) return false;
                    byte[] original = Runtime(members);
                    var source = Assess(profile, original);
                    var target = members[shape / 8 % members.Length];
                    var key = new ArchiveMemberKey(source.CatalogRevision, target.Name, ArchiveMemberType.File,
                        members.Take(Array.IndexOf(members, target)).Count(m => m.Name == target.Name));
                    var input = Input(payload, 1);
                    var expected = members.Select(m => (m.Name, m.Payload)).ToList(); int index = Array.IndexOf(members, target);
                    ArchiveZipTransformPlan plan;
                    try
                    {
                        switch (shape % 8)
                        {
                            case 0: plan = ArchiveZipTransformPlan.Create(profile, source, [], [new(ArchiveChangeKind.Delete, key)], [], CancellationToken.None); expected.RemoveAt(index); break;
                            case 1: plan = ArchiveZipTransformPlan.Create(profile, source, [], [new(ArchiveChangeKind.Rename, key, "renamed/" + target.Name)], [], CancellationToken.None);
                                expected[index] = ("renamed/" + target.Name, target.Payload); break;
                            case 2: plan = ArchiveZipTransformPlan.Create(profile, source, [input], [new(ArchiveChangeKind.Replace, key, inputId: 1)], [], CancellationToken.None);
                                expected[index] = (target.Name, payload); break;
                            case 3: plan = ArchiveZipTransformPlan.Create(profile, source, [input], [], [new("added/payload", ArchiveMemberType.File, 1)], CancellationToken.None);
                                expected.Add(("added/payload", payload)); break;
                            // Stale revisions, unknown occurrences and unused grants are refused before any worker exists.
                            case 4: plan = ArchiveZipTransformPlan.Create(profile, source, [], [new(ArchiveChangeKind.Delete,
                                new ArchiveMemberKey(WorkerFixtures.Sha(payload), target.Name, ArchiveMemberType.File, key.Occurrence))], [], CancellationToken.None); break;
                            case 5: plan = ArchiveZipTransformPlan.Create(profile, source, [], [new(ArchiveChangeKind.Delete, new ArchiveMemberKey(key.CatalogRevision, key.RawPath, key.Type, key.Occurrence + 64))], [], CancellationToken.None); break;
                            case 6: plan = ArchiveZipTransformPlan.Create(profile, source, [input], [], [], CancellationToken.None); break;
                            default: plan = ArchiveZipTransformPlan.Create(profile, source, [], [new(ArchiveChangeKind.Rename, key, "../" + target.Name)], [], CancellationToken.None); break;
                        }
                    }
                    catch (ArchiveWriteException) { return false; }
                    if (shape % 8 >= 4) throw new InvalidOperationException("ZIP oracle: a stale, unknown, unused or unsafe change was planned.");
                    byte[] stage = WriterRun.RebuildAndVerify(ArchiveTransformWire.Prepare(plan, CancellationToken.None), () => new MemoryStream(original, false),
                        _ => new MemoryStream(payload, false));
                    Same(expected.ToArray(), Read(stage), "updated ZIP");
                    return true;
                }
                case 2:
                {
                    byte[] payload = reader.Bytes(reader.UInt16() % 8193);
                    var plan = ArchiveZipTransformPlan.Create(profile, null, [Input(payload, 1)], [], [new("payload", ArchiveMemberType.File, 1)], CancellationToken.None);
                    var invocation = ArchiveTransformWire.Prepare(plan, CancellationToken.None);
                    byte[] mutated = WriterRun.RebuildAndVerify(invocation, WriterRun.Throwing, _ => new MemoryStream(payload, false)); int changes = 0;
                    while (!reader.End && changes++ < 8)
                    {
                        int position = reader.UInt16(); byte value = reader.Byte();
                        if (position % 4096 == 4095) { Array.Resize(ref mutated, Math.Max(0, mutated.Length - 1 - value % 16)); continue; }
                        if (position % 4096 == 4094) { mutated = [.. mutated, value]; continue; }
                        if (mutated.Length != 0) mutated[position % mutated.Length] ^= value == 0 ? (byte)1 : value;
                    }
                    try
                    {
                        new ArchiveVerificationClient((nonce, _) => new InProcessWorker(nonce, verification: true, Version), WorkerFixtures.Queue, Version)
                            .VerifyAsync(WorkerFixtures.Version(mutated), invocation.Expected, () => new MemoryStream(mutated, false), CancellationToken.None)
                            .GetAwaiter().GetResult();
                    }
                    catch (ArchiveWriteException) { return false; }
                    Same([("payload", payload)], Read(mutated), "verified mutated ZIP");
                    return true;
                }
                default:
                {
                    byte[] bytes = reader.Bytes(1 << 20);
                    var assessed = Assess(profile, bytes);
                    var expected = assessed.Manifest.Members.Select(m => (m.Name, m.Length, m.Sha256)).ToArray();
                    (string, long, string)[] actual;
                    try
                    {
                        using var archive = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
                        actual = archive.Entries.Select(e => { byte[] content = Content(e); return (e.FullName, (long)content.Length, WorkerFixtures.Sha(content)); }).ToArray();
                    }
                    catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException)
                    { throw new InvalidOperationException("ZIP oracle: the runtime's reader rejects an admitted ZIP.", error); }
                    if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("ZIP oracle: the admitted members differ from the runtime's reading.");
                    return true;
                }
            }
        }
        catch (ArgumentException) { return false; } // Request constructors validate their own arguments.
    }

    private sealed record Member(string Name, byte[] Payload);

    private static Member[] Members(ref FuzzReader reader, int maximum)
    {
        int count = reader.Byte() % (maximum + 1); var result = new Member[count];
        for (int i = 0; i < count; i++)
        {
            // Mostly valid relative names: a short stem from the fuzz bytes, kept printable, with a fixed folder sometimes.
            string stem = new(reader.Text(24).Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or 'é' or '暗').ToArray());
            string name = (i % 3 == 2 ? "folder/" : "") + (stem.Length == 0 ? "member" + i : stem) + ".bin";
            result[i] = new(name, reader.Bytes(reader.UInt16() % 2049));
        }
        return result;
    }

    private static ArchiveTransformInput Input(byte[] payload, int id) =>
        new(id, payload.Length, WorkerFixtures.Sha(payload), ArchiveTimestamp.FromUnixSeconds(1700000000), 0x1a4, localOffsetMinutes: 60);

    private static byte[] Runtime(Member[] members)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var member in members)
            {
                var entry = archive.CreateEntry(member.Name, member.Payload.Length % 2 == 0 ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
                entry.LastWriteTime = new DateTimeOffset(2021, 6, 7, 8, 9, 10, TimeSpan.Zero);
                using var output = entry.Open(); output.Write(member.Payload);
            }
        return stream.ToArray();
    }

    private static ArchiveAssessment Assess(ArchiveWriteProfile profile, byte[] bytes) =>
        new ArchiveAssessmentClient((nonce, _) => new InProcessWorker(nonce, verification: false, Version), WorkerFixtures.Queue, Version)
            .AssessAsync(new(profile, bytes.Length, WorkerFixtures.Sha(bytes)), () => new MemoryStream(bytes, false), CancellationToken.None)
            .GetAwaiter().GetResult();

    private static (string Name, byte[] Payload)[] Read(byte[] zip)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip, false), ZipArchiveMode.Read);
            return archive.Entries.Select(e => (e.FullName, Content(e))).ToArray();
        }
        catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException)
        { throw new InvalidOperationException("ZIP oracle: the runtime's reader rejects a verified ZIP.", error); }
    }

    private static byte[] Content(ZipArchiveEntry entry)
    { using var stream = entry.Open(); using var copy = new MemoryStream(); stream.CopyTo(copy); return copy.ToArray(); }

    private static void Same((string Name, byte[] Payload)[] expected, (string Name, byte[] Payload)[] actual, string what)
    {
        if (expected.Length != actual.Length || expected.Zip(actual).Any(p => p.First.Name != p.Second.Name || !p.First.Payload.AsSpan().SequenceEqual(p.Second.Payload)))
            throw new InvalidOperationException("ZIP oracle: the " + what + " reads as other members or bytes.");
    }

    /// <summary>One valid request per mode, plus the runtime's and an Info-ZIP-style archive for assessment.</summary>
    internal static IEnumerable<(string Name, byte[] Seed)> Seeds()
    {
        byte[] text = "a ZIP payload, a ZIP payload, a ZIP payload"u8.ToArray();
        static byte[] Entry(string name, byte[] payload) => [(byte)name.Length, .. Encoding.UTF8.GetBytes(name), (byte)payload.Length, (byte)(payload.Length >> 8), .. payload];
        yield return ("create.bin", [0, 0, 2, .. Entry("first", text), .. Entry("second", [])]);
        yield return ("create-store.bin", [3, 0, 1, .. Entry("stored", text)]);
        for (byte shape = 0; shape < 8; shape++)
            yield return ($"update-{shape}.bin", [0, 1, 3, .. Entry("one", text), .. Entry("two", [1, 2, 3]), .. Entry("three", text), shape, 4, 0, .. "four"u8]);
        yield return ("mutate.bin", [0, 2, (byte)text.Length, 0, .. text, 40, 0, 1]);
        using var runtime = new MemoryStream();
        using (var archive = new ZipArchive(runtime, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.Comment = "comment";
            using (var output = archive.CreateEntry("folder/a.txt").Open()) output.Write(text);
            archive.CreateEntry("folder/");
        }
        yield return ("assess-runtime.bin", [0, 3, .. runtime.ToArray()]);
    }
}
