using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using FileCat.Archives;
using FileCat.Core.Archives;
using SharpCompress.Readers;
using SevenZipReader = SharpCompress.Archives.SevenZip.SevenZipArchive;

// Test-only FZ05/FZ06/FZ07/FZ13 targets for the 7z and RAR5 writers: the real plan, the real transform and assessment worker
// roles (in-process, with single-use password grants for protected profiles) and the real fresh verifier. Each oracle is the
// format's separate browse reader: the vendored 7z archive reader, and FileCat's own RAR reader.

/// <summary>
/// Input: [options…][mode][rest]. 7z options: [codec][filter][flags][level][model]; RAR5 options: [level][flags].
/// Mode 0 (FZ05/FZ13 create): [count][name, payload]… become a new archive; the fresh verifier accepts it and the oracle reads
///   exactly those files.
/// Mode 1 (FZ06 update): the same members make a source with the product writer, which the real worker assesses; then
///   [shape][payload] deletes, renames, replaces or adds one member, or attempts a stale, unknown, unused or unsafe change that
///   the plan must refuse; the oracle reads exactly the expected files.
/// Mode 2 (FZ07): [payload][position, xor…] mutates a valid stage; whatever the fresh verifier accepts reads as the payload.
/// </summary>
static class ContainerWriters
{
    private const ushort Version = ArchiveWorkerProtocol.SecretVersion;
    internal const string Password = "Public-writer-fuzz-暗号-42829331";
    private static Func<string, IDisposable>? rarSession;
    private static MethodInfo? rarOpen;

    internal static bool Exercise(ReadOnlySpan<byte> data, bool rar)
    {
        var reader = new FuzzReader(data);
        try
        {
            var profile = rar ? Rar5Profile(ref reader) : SevenZipProfile(ref reader);
            bool protectedProfile = profile.Encryption.Mode != ArchiveEncryptionMode.None;
            string? password = protectedProfile ? Password : null;
            int mode = reader.Byte() % 3;
            switch (mode)
            {
                case 0:
                {
                    var members = Members(ref reader, protectedProfile ? 2 : 8);
                    byte[] stage = Create(profile, members, password);
                    Same(members, Read(stage, rar, password), "created archive");
                    return true;
                }
                case 1:
                {
                    var members = Members(ref reader, protectedProfile ? 2 : 5); byte shape = reader.Byte(); byte[] payload = reader.Bytes(reader.UInt16() % 2049);
                    if (members.Length == 0) return false;
                    byte[] original = Create(profile, members, password);
                    var source = Assess(profile, original, password);
                    var files = source.Manifest.Members.Where(m => m.Type == ArchiveMemberType.File).ToArray();
                    var target = files[shape / 8 % files.Length];
                    var key = new ArchiveMemberKey(source.CatalogRevision, target.Name, target.Type, target.Occurrence);
                    var input = Input(payload, 1);
                    var expected = members.Select(m => (m.Name, m.Payload)).ToList(); int index = expected.FindIndex(m => m.Name == target.Name);
                    if (index < 0) throw new InvalidOperationException("Writer oracle: the assessed source lists a file the request never added.");
                    ArchiveContainerTransformPlan plan;
                    try
                    {
                        ArchiveTransformEdit[] edits = []; ArchiveTransformInput[] inputs = []; ArchiveTransformAddition[] additions = [];
                        switch (shape % 8)
                        {
                            case 0: edits = [new(ArchiveChangeKind.Delete, key)]; expected.RemoveAt(index); break;
                            case 1: edits = [new(ArchiveChangeKind.Rename, key, "renamed/" + target.Name)]; expected[index] = ("renamed/" + target.Name, expected[index].Payload); break;
                            case 2: inputs = [input]; edits = [new(ArchiveChangeKind.Replace, key, inputId: 1)]; expected[index] = (target.Name, payload); break;
                            case 3: inputs = [input]; additions = [new("added/payload", ArchiveMemberType.File, 1)]; expected.Add(("added/payload", payload)); break;
                            // Stale revisions, unknown occurrences, unused grants and unsafe names are refused before any worker exists.
                            case 4: edits = [new(ArchiveChangeKind.Delete, new ArchiveMemberKey(WorkerFixtures.Sha(payload), key.RawPath, key.Type, key.Occurrence))]; break;
                            case 5: edits = [new(ArchiveChangeKind.Delete, new ArchiveMemberKey(key.CatalogRevision, key.RawPath, key.Type, key.Occurrence + 64))]; break;
                            case 6: inputs = [input]; break;
                            default: edits = [new(ArchiveChangeKind.Rename, key, "../" + target.Name)]; break;
                        }
                        plan = rar ? ArchiveRar5TransformPlan.Create(profile, source, inputs, edits, additions, CancellationToken.None)
                            : ArchiveSevenZipTransformPlan.Create(profile, source, inputs, edits, additions, CancellationToken.None);
                    }
                    catch (ArchiveWriteException) { return false; }
                    if (shape % 8 >= 4) throw new InvalidOperationException("Writer oracle: a stale, unknown, unused or unsafe change was planned.");
                    byte[] stage = WriterRun.RebuildAndVerify(ArchiveTransformWire.Prepare(plan, CancellationToken.None), () => new MemoryStream(original, false),
                        _ => new MemoryStream(payload, false), password: password);
                    Same([.. expected], Read(stage, rar, password), "updated archive");
                    return true;
                }
                default:
                {
                    byte[] payload = reader.Bytes(reader.UInt16() % 8193);
                    var plan = Plan(profile, [new("payload", payload)]);
                    var invocation = ArchiveTransformWire.Prepare(plan, CancellationToken.None);
                    byte[] mutated = WriterRun.RebuildAndVerify(invocation, WriterRun.Throwing, _ => new MemoryStream(payload, false), password: password); int changes = 0;
                    while (!reader.End && changes++ < 8)
                    {
                        int position = reader.UInt16(); byte value = reader.Byte();
                        if (position % 4096 == 4095) { Array.Resize(ref mutated, Math.Max(0, mutated.Length - 1 - value % 16)); continue; }
                        if (position % 4096 == 4094) { mutated = [.. mutated, value]; continue; }
                        if (mutated.Length != 0) mutated[position % mutated.Length] ^= value == 0 ? (byte)1 : value;
                    }
                    try
                    {
                        using var grant = password is null ? null : new ArchiveWorkerSecretGrant(password);
                        new ArchiveVerificationClient((nonce, _) => new InProcessWorker(nonce, verification: true, Version), WorkerFixtures.Queue, Version)
                            .VerifyAsync(WorkerFixtures.Version(mutated), invocation.Expected, () => new MemoryStream(mutated, false), CancellationToken.None, grant)
                            .GetAwaiter().GetResult();
                    }
                    catch (ArchiveWriteException) { return false; }
                    Same([("payload", payload)], Read(mutated, rar, password), "verified mutated archive");
                    return true;
                }
            }
        }
        catch (ArchiveWriteException) { return false; }
        catch (ArgumentException) { return false; } // Public option and request constructors validate their own arguments.
    }

    /// <summary>Encoder memory stays within the fuzz unit's managed heap: 1-2 MiB LZMA dictionaries, 64 KiB-4 MiB PPMd models.</summary>
    private static ArchiveWriteProfile SevenZipProfile(ref FuzzReader reader)
    {
        byte codecByte = reader.Byte(), filterByte = reader.Byte(), flags = reader.Byte(), levelByte = reader.Byte(), modelByte = reader.Byte();
        var codec = (ArchiveSevenZipCodec)(codecByte % 7);
        var filter = (ArchiveSevenZipFilter)(filterByte % 13);
        int level = codec == ArchiveSevenZipCodec.Copy ? 0 : levelByte % 4;
        int dictionary = codec is ArchiveSevenZipCodec.Lzma or ArchiveSevenZipCodec.Lzma2 ? 1 << (20 + (flags >> 4 & 1)) : 8 << 20;
        int order = codec == ArchiveSevenZipCodec.Ppmd7 ? 2 + modelByte % 31 : 0;
        int memory = codec == ArchiveSevenZipCodec.Ppmd7 ? 1 << (16 + modelByte / 32 % 7) : 0;
        int delta = filter == ArchiveSevenZipFilter.Delta ? 1 + modelByte : 1;
        var header = (ArchiveSevenZipHeaderCompression)(flags & 1);
        return ArchiveWriteProfile.ForSevenZip(new(codec, filter, order, memory, delta, header), level, dictionary, Protection(flags >> 1));
    }

    private static ArchiveWriteProfile Rar5Profile(ref FuzzReader reader)
    {
        byte level = reader.Byte(), flags = reader.Byte();
        return ArchiveWriteProfile.ForRar5((ArchiveRar5Compression)(level % 6), Protection(flags));
    }

    // A quarter of the requests are protected: each one pays the minimum 2^19-round key derivation per worker role and oracle.
    private static ArchiveEncryptionOptions? Protection(int bits) => (bits & 7) switch
    {
        1 => new(ArchiveEncryptionMode.Data, 19),
        2 => new(ArchiveEncryptionMode.DataAndNames, 19),
        _ => null,
    };

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
        new(id, payload.Length, WorkerFixtures.Sha(payload), ArchiveTimestamp.FromUnixSeconds(1700000000), 0x1a4);

    private static ArchiveContainerTransformPlan Plan(ArchiveWriteProfile profile, Member[] members)
    {
        var inputs = members.Select((m, i) => Input(m.Payload, i + 1)).ToArray();
        var additions = members.Select((m, i) => new ArchiveTransformAddition(m.Name, ArchiveMemberType.File, i + 1)).ToArray();
        return profile.Format == ArchiveWriteFormat.Rar5 ? ArchiveRar5TransformPlan.Create(profile, null, inputs, [], additions, CancellationToken.None)
            : ArchiveSevenZipTransformPlan.Create(profile, null, inputs, [], additions, CancellationToken.None);
    }

    private static byte[] Create(ArchiveWriteProfile profile, Member[] members, string? password) =>
        WriterRun.RebuildAndVerify(ArchiveTransformWire.Prepare(Plan(profile, members), CancellationToken.None), WriterRun.Throwing,
            id => new MemoryStream(members[id - 1].Payload, false), password: password);

    private static ArchiveAssessment Assess(ArchiveWriteProfile profile, byte[] bytes, string? password)
    {
        using var grant = password is null ? null : new ArchiveWorkerSecretGrant(password);
        return new ArchiveAssessmentClient((nonce, _) => new InProcessWorker(nonce, verification: false, Version), WorkerFixtures.Queue, Version)
            .AssessAsync(new(profile, bytes.Length, WorkerFixtures.Sha(bytes)), () => new MemoryStream(bytes, false), CancellationToken.None, grant)
            .GetAwaiter().GetResult();
    }

    /// <summary>The oracle's files in archive order; directories are not compared. A codec the oracle lacks skips the check.</summary>
    private static (string Name, byte[] Payload)[]? Read(byte[] bytes, bool rar, string? password)
    {
        try { return rar ? ReadRar(bytes, password) : ReadSevenZip(bytes, password); }
        catch (NotSupportedException) when (!rar) { Interlocked.Increment(ref Stats.OracleSkipped); return null; } // The browse reader may lack a writer-qualified filter or codec.
        catch (Exception error) when (error is not InvalidOperationException)
        { throw new InvalidOperationException("Writer oracle: the independent reader rejects a verified archive.", error); }
    }

    private static (string Name, byte[] Payload)[] ReadSevenZip(byte[] bytes, string? password)
    {
        using var archive = SevenZipReader.OpenArchive(new MemoryStream(bytes, false), new ReaderOptions { Password = password, LeaveStreamOpen = false });
        var files = new List<(string, byte[])>();
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory) continue;
            using var content = entry.OpenEntryStream(); using var copy = new MemoryStream(); content.CopyTo(copy);
            files.Add(((entry.Key ?? "").Replace('\\', '/'), copy.ToArray()));
        }
        return [.. files];
    }

    private static (string Name, byte[] Payload)[] ReadRar(byte[] bytes, string? password)
    {
        var assembly = typeof(IMemberReader).Assembly;
        var sessionType = assembly.GetType("FileCat.Archives.Rar.Rar5ReadPasswordSession", throwOnError: true)!;
        rarOpen ??= assembly.GetType("FileCat.Archives.Rar.RarReader", throwOnError: true)!.GetMethod("Open", BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(List<Stream>), typeof(bool), typeof(CancellationToken), typeof(IReadOnlyList<string>), sessionType])
            ?? throw new InvalidOperationException("The RAR browse reader's stream entry changed.");
        rarSession ??= Format.BindPassword(assembly, seven: false, maximumRounds: 1L << 26);
        using var session = password is null ? null : rarSession(password);
        IMemberReader reader;
        try { reader = (IMemberReader)rarOpen.Invoke(null, [new List<Stream> { new MemoryStream(bytes, false) }, false, CancellationToken.None, null, session])!; }
        catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
        using (reader)
        {
            var files = new List<(string, byte[])>();
            foreach (var member in reader.List(warning => throw new InvalidDataException("The RAR browse reader warned: " + warning), CancellationToken.None).ToArray())
            {
                if (member.Kind != MemberKind.File) continue;
                using var content = reader.Open(member.Index, CancellationToken.None); using var copy = new MemoryStream(); content.CopyTo(copy);
                files.Add((member.Path.Replace('\\', '/'), copy.ToArray()));
            }
            return [.. files];
        }
    }

    private static void Same(IReadOnlyList<Member> expected, (string Name, byte[] Payload)[]? actual, string what) =>
        Same(expected.Select(m => (m.Name, m.Payload)).ToArray(), actual, what);

    private static void Same((string Name, byte[] Payload)[] expected, (string Name, byte[] Payload)[]? actual, string what)
    {
        if (actual is null) return;
        if (expected.Length != actual.Length || expected.Zip(actual).Any(p => p.First.Name != p.Second.Name || !p.First.Payload.AsSpan().SequenceEqual(p.Second.Payload)))
            throw new InvalidOperationException("Writer oracle: the " + what + " reads as other members or bytes.");
    }

    /// <summary>Every codec, a filter sample, both header modes and both protections for create, each update shape, a mutation.</summary>
    internal static IEnumerable<(string Name, byte[] Seed)> Seeds(bool rar)
    {
        byte[] text = "a container payload, a container payload, a container payload"u8.ToArray();
        static byte[] Entry(string name, byte[] payload) => [(byte)name.Length, .. Encoding.UTF8.GetBytes(name), (byte)payload.Length, (byte)(payload.Length >> 8), .. payload];
        byte[][] options = rar
            ? [[0, 0], [1, 0], [2, 0], [3, 0], [4, 0], [5, 0], [3, 1], [5, 2]]
            : [[0, 0, 0, 0, 0], [1, 0, 0, 1, 0], [2, 0, 1, 2, 0], [3, 0, 0, 3, 0], [4, 0, 0, 1, 0], [5, 0, 0, 2, 0], [6, 0, 0, 1, 37],
               [2, 2, 0, 1, 0], [1, 1, 0, 1, 3], [2, 12, 0, 1, 0], [2, 0, 2, 1, 0], [2, 0, 5, 1, 0]];
        for (int i = 0; i < options.Length; i++)
            yield return ($"create-{i:00}.bin", [.. options[i], 0, 2, .. Entry("first", text), .. Entry("second", [])]);
        for (byte shape = 0; shape < 8; shape++)
            yield return ($"update-{shape}.bin", [.. options[1], 1, 3, .. Entry("one", text), .. Entry("two", [1, 2, 3]), .. Entry("three", text), shape, 4, 0, .. "four"u8]);
        yield return ("mutate.bin", [.. options[1], 2, (byte)text.Length, 0, .. text, 40, 0, 1]);
        yield return ("mutate-protected.bin", [.. options[^1], 2, (byte)text.Length, 0, .. text, 41, 0, 2]);
    }
}
