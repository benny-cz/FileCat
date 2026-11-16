using System.IO.Compression;
using FileCat.Core.Archives;

// Test-only AW7 target: standalone gzip, bzip2 and Zstandard streams through the real plan, the real transform and
// assessment worker roles (in-process) and the real fresh verifier.

/// <summary>
/// FZ02 and FZ05–FZ07 for standalone streams. Input: [format][level][mode][rest].
/// Mode 0 (FZ05): [payload] is compressed into a new stream; the fresh verifier accepts it and decoding equals the payload.
/// Mode 1 (FZ06): [first][shape][second] creates a stream, assesses it with the real worker, then either replaces the payload
///   (wrapper features kept, decoding equals the second payload) or attempts a forbidden change that must be refused.
/// Mode 2 (FZ07): [payload][position, xor…] mutates a valid stage; whatever the fresh verifier accepts decodes exactly to the payload.
/// Mode 3 (FZ02): [bytes] are assessed as a standalone stream: a typed refusal, or one synthetic member whose length and
///   SHA-256 equal an independent decoding by the reader the application uses for these files.
/// </summary>
static class StandaloneStreams
{
    private static readonly ArchiveWriteFormat[] Formats = [ArchiveWriteFormat.Gzip, ArchiveWriteFormat.Bzip2, ArchiveWriteFormat.Zstd];
    private const ushort Version = ArchiveWorkerProtocol.SecretVersion;

    internal static bool Exercise(ReadOnlySpan<byte> data)
    {
        var reader = new FuzzReader(data);
        var format = Formats[reader.Byte() % Formats.Length]; int level = reader.Byte() % 4;
        var profile = new ArchiveWriteProfile(format, compressionLevel: format == ArchiveWriteFormat.Bzip2 ? 0 : level);
        int mode = reader.Byte() % 4;
        try
        {
            switch (mode)
            {
                case 0:
                {
                    byte[] payload = reader.Bytes(reader.UInt16());
                    byte[] stage = Create(profile, payload, out _);
                    Same(payload, Decode(format, stage), "created stream");
                    return true;
                }
                case 1:
                {
                    byte[] first = reader.Bytes(reader.UInt16() % 4097); byte shape = reader.Byte(); byte[] second = reader.Bytes(reader.UInt16() % 4097);
                    byte[] original = Create(profile, first, out _);
                    var source = Assess(profile, original);
                    var key = new ArchiveMemberKey(source.CatalogRevision, ArchiveStandalone.MemberName, ArchiveMemberType.File, 0);
                    var input = Input(second, 3);
                    if (shape % 8 != 0)
                    {
                        // Every other change shape is refused by the plan before any worker or object exists.
                        try
                        {
                            _ = (shape % 8) switch
                            {
                                1 => ArchiveStandaloneTransformPlan.Create(profile, source, [input], [new(ArchiveChangeKind.Rename, key, "renamed")], [], CancellationToken.None),
                                2 => ArchiveStandaloneTransformPlan.Create(profile, source, [input], [new(ArchiveChangeKind.Delete, key)], [], CancellationToken.None),
                                3 => ArchiveStandaloneTransformPlan.Create(profile, source, [input], [], [new(ArchiveStandalone.MemberName, ArchiveMemberType.File, 3)], CancellationToken.None),
                                4 => ArchiveStandaloneTransformPlan.Create(profile, null, [input, Input(first, 4)], [],
                                    [new(ArchiveStandalone.MemberName, ArchiveMemberType.File, 3), new("second", ArchiveMemberType.File, 4)], CancellationToken.None),
                                5 => ArchiveStandaloneTransformPlan.Create(profile, source, [input],
                                    [new(ArchiveChangeKind.Replace, new ArchiveMemberKey(WorkerFixtures.Sha(second), ArchiveStandalone.MemberName, ArchiveMemberType.File, 0), inputId: 3)], [], CancellationToken.None),
                                6 => ArchiveStandaloneTransformPlan.Create(profile, null, [input], [],
                                    [new(ArchiveStandalone.MemberName, new ArchiveTransformDirectoryMetadata(DateTimeOffset.UnixEpoch))], CancellationToken.None),
                                _ => ArchiveStandaloneTransformPlan.Create(profile, null, [input], [], [new("payload.txt", ArchiveMemberType.File, 3)], CancellationToken.None),
                            };
                        }
                        catch (ArchiveWriteException) { return false; }
                        throw new InvalidOperationException("Standalone oracle: a change other than one replacement of the one payload was planned.");
                    }
                    var plan = ArchiveStandaloneTransformPlan.Create(profile, source, [input], [new(ArchiveChangeKind.Replace, key, inputId: 3)], [], CancellationToken.None);
                    if (!plan.Expected.Features.SequenceEqual(source.Manifest.Features))
                        throw new InvalidOperationException("Standalone oracle: a replacement changed the wrapper features.");
                    var invocation = ArchiveTransformWire.Prepare(plan, CancellationToken.None);
                    byte[] replaced = WriterRun.RebuildAndVerify(invocation, () => new MemoryStream(original, false), _ => new MemoryStream(second, false));
                    Same(second, Decode(format, replaced), "replaced stream");
                    return true;
                }
                case 2:
                {
                    byte[] payload = reader.Bytes(reader.UInt16() % 8193);
                    byte[] stage = Create(profile, payload, out var invocation);
                    byte[] mutated = stage.ToArray(); int changes = 0;
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
                    // Accepted: the bytes must still be exactly the expected payload, whatever the self-consistent trailer says.
                    Same(payload, Decode(format, mutated), "verified mutated stream");
                    return true;
                }
                default:
                {
                    byte[] bytes = reader.Bytes(1 << 20);
                    var assessed = Assess(profile, bytes);
                    var member = assessed.Manifest.Members.Count == 1 ? assessed.Manifest.Members[0]
                        : throw new InvalidOperationException("Standalone oracle: an assessment described more than one payload.");
                    if (member.Name != ArchiveStandalone.MemberName || member.Type != ArchiveMemberType.File || member.Occurrence != 0)
                        throw new InvalidOperationException("Standalone oracle: the payload is not the synthetic member.");
                    byte[] decoded = Decode(format, bytes);
                    if (decoded.Length != member.Length || WorkerFixtures.Sha(decoded) != member.Sha256)
                        throw new InvalidOperationException("Standalone oracle: the admitted payload differs from the application's reader.");
                    return true;
                }
            }
        }
        catch (ArgumentException) { return false; } // Request constructors validate their own arguments.
    }

    private static ArchiveTransformInput Input(byte[] payload, int id) =>
        new(id, payload.Length, WorkerFixtures.Sha(payload), ArchiveTimestamp.FromUnixSeconds(1700000000), 0x1a4);

    private static byte[] Create(ArchiveWriteProfile profile, byte[] payload, out ArchiveTransformInvocation invocation)
    {
        invocation = ArchiveTransformWire.Prepare(ArchiveStandaloneTransformPlan.Create(profile, null, [Input(payload, 1)], [],
            [new(ArchiveStandalone.MemberName, ArchiveMemberType.File, 1)], CancellationToken.None), CancellationToken.None);
        return WriterRun.RebuildAndVerify(invocation, WriterRun.Throwing, _ => new MemoryStream(payload, false));
    }

    private static ArchiveAssessment Assess(ArchiveWriteProfile profile, byte[] bytes) =>
        new ArchiveAssessmentClient((nonce, _) => new InProcessWorker(nonce, verification: false, Version), WorkerFixtures.Queue, Version)
            .AssessAsync(new(profile, bytes.Length, WorkerFixtures.Sha(bytes)), () => new MemoryStream(bytes, false), CancellationToken.None)
            .GetAwaiter().GetResult();

    /// <summary>The runtime's zlib for gzip; for bzip2 and Zstandard, the decoders FileCat's own reader uses for these files.</summary>
    private static byte[] Decode(ArchiveWriteFormat format, byte[] stream)
    {
        try
        {
            using var source = new MemoryStream(stream, false);
            using Stream decoder = format switch
            {
                ArchiveWriteFormat.Gzip => new GZipStream(source, CompressionMode.Decompress),
                ArchiveWriteFormat.Bzip2 => SharpCompress.Compressors.BZip2.BZip2Stream.Create(source, SharpCompress.Compressors.CompressionMode.Decompress, false, false, true),
                _ => new SharpCompress.Compressors.ZStandard.DecompressionStream(source, 0, false, false),
            };
            using var output = new MemoryStream(); decoder.CopyTo(output); return output.ToArray();
        }
        catch (Exception error) when (error is not InvalidOperationException)
        { throw new InvalidOperationException("Standalone oracle: an independent decoder rejects an admitted stream.", error); }
    }

    private static void Same(byte[] expected, byte[] actual, string what)
    {
        if (!expected.AsSpan().SequenceEqual(actual)) throw new InvalidOperationException("Standalone oracle: the " + what + " decodes to other bytes.");
    }

    /// <summary>One valid request per mode and format.</summary>
    internal static IEnumerable<(string Name, byte[] Seed)> Seeds()
    {
        byte[] text = "a standalone payload, a standalone payload"u8.ToArray();
        for (byte format = 0; format < 3; format++)
        {
            yield return ($"create-{format}.bin", [format, 2, 0, (byte)text.Length, 0, .. text]);
            yield return ($"replace-{format}.bin", [format, 1, 1, 5, 0, .. "first"u8, 0, 6, 0, .. "second"u8]);
            yield return ($"refuse-{format}.bin", [format, 1, 1, 5, 0, .. "first"u8, 3, 6, 0, .. "second"u8]);
            yield return ($"mutate-{format}.bin", [format, 2, 2, (byte)text.Length, 0, .. text, 12, 0, 1]);
            yield return ($"empty-{format}.bin", [format, 0, 0, 0, 0]);
        }
        using var gzip = new MemoryStream();
        using (var writer = new GZipStream(gzip, CompressionLevel.Optimal, leaveOpen: true)) writer.Write(text);
        byte[] foreign = gzip.ToArray(); foreign[4] = foreign[5] = foreign[6] = foreign[7] = 0; foreign[8] = 0; foreign[9] = 3;
        yield return ("assess-gzip.bin", [0, 0, 3, .. foreign]);
    }
}
