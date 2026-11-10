using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveTarBzip2PipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ArchiveWriteProfile Profile = new(ArchiveWriteFormat.TarBzip2);
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static byte[] Encode(byte[] b, byte block = 9)
    { using var output = new MemoryStream(); using (var encoder = new StrictBzip2WriteStream(output, block, Ct)) encoder.Write(b); return output.ToArray(); }
    private static byte[] Decode(byte[] b)
    { using var source = new MemoryStream(b, false); using var decoder = new StrictBzip2ReadStream(source, Ct); using var output = new MemoryStream(); decoder.CopyTo(output); Assert.True(source.CanRead); return output.ToArray(); }

    [Fact]
    public void Fuzzed_move_to_front_index_is_a_controlled_invalid_archive_refusal()
    {
        // Ubuntu FZ02 bzip2-short-v6, crash artifact SHA-256 0B8498E3...; 229 bytes.
        byte[] encoded = Convert.FromBase64String(
            "QlpoOTFBWSZTWXUgvkAAACd/gH/v//////////////////+gAIoZMgaaZGp6ZTTRjRNMEaMmCMmAEaMBGARpowRgBPUPQBBgABNMEaMmCMmAEaMBGARpowRgBPUPQBBjAJgACYAgAAAAAAAAAAAAAAAAAAAAEAAACwEI6ePoEIaIRGKMkSj8BCYkrhT8Sjj5FKpWW9W59JgHMvYMWTPn6GnT1NVq5ewYsmbRq2buHORa1exZtW7l34+bVu5d7y731kJaaDExQVkmU1l1IL4CAAAnf4B///////////////8l1/KF/w==");
        Assert.Equal("0B8498E331CF04004C2FBBE1DF1AA281D2B61EC82299B7179FE29AA4A37DA01F", Hash(encoded));
        using var source = new MemoryStream(encoded, false);
        Assert.Equal(ArchiveWriteReason.InvalidArchive,
            Assert.Throws<ArchiveWriteException>(() => new StrictBzip2ReadStream(source, Ct)).Reason);
        Assert.True(source.CanRead);
    }
    private static Task<ArchiveAssessment> Assess(byte[] b) => new ArchiveAssessmentClient((n, _) => Child.Start(n))
        .AssessAsync(new(Profile, b.Length, Hash(b)), () => new MemoryStream(b, false), Ct);
    private static ArchiveTransformInput Input(byte[] b) => new(7, b.Length, Hash(b), new ArchiveTimestamp(1700000000, 123456789), 0x1ed);
    private static async Task<byte[]> Transform(ArchiveTarTransformPlan plan, byte[]? original, byte[]? payload = null)
    {
        var invocation = ArchiveTransformWire.Prepare(plan, Ct); using var stage = new MemoryStream(); Child? transform = null, verifier = null;
        var receipt = await new ArchiveTransformClient((n, _) => transform = Child.Start(n, "archive-transform"))
            .RebuildAsync(invocation, stage, () => new MemoryStream(original!, false), _ => new MemoryStream(payload!, false), Ct);
        byte[] result = stage.ToArray(); Assert.Equal(result.Length, receipt.Length); Assert.InRange(stage.Length, 14, invocation.Request.OutputLengthBound);
        var verified = await new ArchiveVerificationClient((n, _) => verifier = Child.Start(n, "archive-verify"))
            .VerifyAsync(new("owned-parent-stage", result.Length, Hash(result)), plan.Expected, () => new MemoryStream(result, false), Ct);
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest); Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped); Assert.NotEqual(transform.ProcessId, verifier.ProcessId);
        return result;
    }
    [Theory]
    [InlineData((byte)1, 0)] [InlineData((byte)5, 0)] [InlineData((byte)9, 0)]
    [InlineData((byte)1, 200000)] [InlineData((byte)5, 200000)] [InlineData((byte)9, 1100000)]
    public void Complete_streams_and_multiple_blocks_roundtrip_without_owning_the_stage(byte block, int size)
    {
        byte[] bytes = new byte[size]; new Random(9137).NextBytes(bytes); byte[] encoded = Encode(bytes, block);
        Assert.Equal((byte)('0' + block), encoded[3]); Assert.Equal(bytes, Decode(encoded));
        using var fragmented = new Fragmented(encoded); using var decoder = new StrictBzip2ReadStream(fragmented, Ct);
        using var output = new MemoryStream(); decoder.CopyTo(output); Assert.Equal(bytes, output.ToArray());
    }
    [Theory]
    [InlineData("header")] [InlineData("block-size")] [InlineData("block-crc")] [InlineData("combined-crc")]
    [InlineData("truncated")] [InlineData("concatenated")] [InlineData("trailing")] [InlineData("padding")]
    public void Incomplete_corrupt_or_non_single_stream_framing_refuses(string change)
    {
        // Independently generated with Python bz2.compress(b'hello'); its final byte has seven padding bits.
        byte[] b = Convert.FromHexString("425A68393141592653591931653D00000081000244A000219A68334D07338BB9229C28480C98B29E80");
        switch (change)
        {
            case "header": b[0] = 0; break; case "block-size": b[3] = (byte)'0'; break;
            case "block-crc": b[10] ^= 1; break; case "combined-crc": b[^2] ^= 0x80; break;
            case "truncated": b = b[..^1]; break; case "concatenated": b = [.. b, .. b]; break;
            case "trailing": b = [.. b, 0]; break; case "padding": b[^1] |= 1; break;
        }
        Assert.Equal(ArchiveWriteReason.InvalidArchive, Assert.Throws<ArchiveWriteException>(() => Decode(b)).Reason);
    }
    [Fact]
    public void Every_truncation_refuses_even_when_the_codec_could_return_partial_payload()
    {
        byte[] b = Encode("complete checksummed input"u8.ToArray());
        for (int end = 0; end < b.Length; end++) Assert.Throws<ArchiveWriteException>(() => Decode(b[..end]));
    }
    [Fact]
    public void Expansion_policy_applies_to_a_stream_written_by_FileCat()
    {
        using var stage = new MemoryStream(); byte[] chunk = new byte[1 << 20];
        using (var encoder = new StrictBzip2WriteStream(stage, 9, Ct)) { for (int i = 0; i < 65; i++) encoder.Write(chunk); }
        stage.Position = 0; using var decoder = new StrictBzip2ReadStream(stage, Ct);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => decoder.CopyTo(Stream.Null)).Reason);
    }
    [Fact]
    public void Aborted_encoding_cannot_forward_bytes_or_finalize_during_disposal()
    {
        using var stage = new MemoryStream(); var encoder = new StrictBzip2WriteStream(stage, 1, Ct); encoder.Write(new byte[1000]);
        encoder.Abort(); long length = stage.Length; Assert.Throws<IOException>(() => encoder.WriteByte(1)); encoder.Dispose();
        Assert.Equal(length, stage.Length); Assert.True(stage.CanWrite);
    }
    [Fact]
    public void A_healthy_finalizer_IO_failure_is_reported_and_never_retried_on_a_second_dispose()
    {
        using var stage = new FailingStage(); var encoder = new StrictBzip2WriteStream(stage, 9, Ct); encoder.Write("payload"u8);
        stage.Fail = true; long before = stage.Length; Assert.Throws<IOException>(encoder.Dispose); encoder.Dispose(); Assert.Equal(before, stage.Length);
    }
    [Theory]
    [InlineData((byte)0)] [InlineData((byte)10)] [InlineData((byte)255)]
    public void Unsupported_block_sizes_refuse_before_codec_creation(byte block)
    {
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveTarFeatures.Create(Profile, 2, bzip2BlockSize: block)).Reason);
        using var stage = new MemoryStream(); Assert.Throws<ArgumentException>(() => new StrictBzip2WriteStream(stage, block, Ct)); Assert.Empty(stage.ToArray());
    }
    [Fact]
    public void A_many_member_update_stays_within_four_physical_source_passes()
    {
        byte[] source = Encode(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, Enumerable.Range(0, 64).Select(i => ("file-" + i, "payload-" + i)).ToArray()), 1);
        using var counted = new CountedSource(source); using var stage = new MemoryStream();
        var result = Bzip2TarRewriteEngine.Rebuild(counted, Hash(source), Profile, stage, [], [], new Dictionary<int, TarInputDescriptor>(), _ => throw new InvalidOperationException(), Ct);
        Assert.Equal(64, result.Tar.Members.Count); Assert.InRange(counted.BytesRead, source.Length, 4L * source.Length);
        using var decoded = new MemoryStream(Decode(stage.ToArray())); Assert.Equal(64, StrictTarCatalog.Read(decoded, Ct).Members.Count);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Creation_and_empty_creation_use_distinct_transform_and_verifier_processes(bool empty)
    {
        byte[] bytes = "new BZip2 payload"u8.ToArray();
        var plan = ArchiveTarTransformPlan.Create(Profile, null, empty ? [] : [Input(bytes)], [], empty ? [] : [new("file", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, null, bytes); Assert.Equal((byte)'9', result[3]);
        Save("create-" + empty, result, plan.Expected);
    }
    [Fact]
    public async Task Incompressible_multiple_block_payloads_do_not_turn_codec_bytes_into_individual_IPC_frames()
    {
        byte[] payload = new byte[2 << 20]; new Random(9141).NextBytes(payload);
        var plan = ArchiveTarTransformPlan.Create(Profile, null, [Input(payload)], [], [new("file", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, null, payload); Assert.InRange(result.Length, payload.Length / 2, ArchiveTransformWire.OutputBound(plan.Expected, Ct));
    }
    [Fact]
    public async Task A_corrupt_assessment_refuses_through_the_worker_without_a_crash_or_reusable_channel()
    {
        byte[] source = Encode(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "before"))); source[10] ^= 1;
        Child? child = null; var client = new ArchiveAssessmentClient((n, _) => child = Child.Start(n));
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(new(Profile, source.Length, Hash(source)), () => new MemoryStream(source, false), Ct));
        Assert.Equal(ArchiveWriteReason.InvalidArchive, error.Reason); Assert.True(child!.Reaped); Assert.Equal(0u, child.ExitCode);
    }
    [Theory]
    [InlineData(TarEntryFormat.Ustar, (byte)1)] [InlineData(TarEntryFormat.Ustar, (byte)5)] [InlineData(TarEntryFormat.Ustar, (byte)9)]
    [InlineData(TarEntryFormat.Pax, (byte)1)] [InlineData(TarEntryFormat.Pax, (byte)5)] [InlineData(TarEntryFormat.Pax, (byte)9)]
    public async Task Updates_preserve_dialect_block_size_and_exact_duplicate_occurrences(TarEntryFormat dialect, byte block)
    {
        byte[] source = Encode(ArchiveStrictTarTests.Make(dialect, ("same", "first"), ("same", "second"), ("old", "before")), block);
        var assessed = await Assess(source); Assert.Equal(new byte[] { 3, dialect == TarEntryFormat.Ustar ? (byte)1 : (byte)2, block }, assessed.Manifest.Features);
        ArchiveMemberKey Key(int i) { var m = assessed.Manifest.Members[i]; return new(assessed.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        byte[] payload = "replacement"u8.ToArray(); var plan = ArchiveTarTransformPlan.Create(assessed, [Input(payload)],
            [new(ArchiveChangeKind.Delete, Key(0)), new(ArchiveChangeKind.Rename, Key(1), "renamed"), new(ArchiveChangeKind.Replace, Key(2), inputId: 7)],
            [new("added", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, source, payload); Assert.Equal(source[..4], result[..4]);
        Save("update-" + dialect + "-" + block, result, plan.Expected);
    }
    [Fact]
    public async Task Delete_all_retains_the_compression_block_size_and_verifies_a_valid_empty_TAR()
    {
        byte[] source = Encode(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "before")), 1); var assessed = await Assess(source);
        var plan = ArchiveTarTransformPlan.Create(assessed, [], [new(ArchiveChangeKind.Delete, new(assessed.CatalogRevision, "file", ArchiveMemberType.File, 0))], [], Ct);
        byte[] result = await Transform(plan, source); Assert.Equal(new byte[] { 3, 1, 1 }, plan.Expected.Features); Save("delete-all", result, plan.Expected);
    }
    [Theory]
    [InlineData("ustar")] [InlineData("pax")]
    public async Task Independent_inputs_are_assessed_and_updated_with_exact_metadata(string dialect)
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_BZIP2_INPUT_ORACLE_DIRECTORY");
        Assert.SkipUnless(root is not null && Path.IsPathFullyQualified(root) && Directory.Exists(root), "Owned independent BZip2 fixtures required.");
        byte[] source = File.ReadAllBytes(Path.Combine(root!, dialect + ".tar.bz2")); using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, dialect + ".json")));
        var assessed = await Assess(source); var golden = expected.RootElement.EnumerateArray().ToArray(); Assert.Equal(golden.Length, assessed.Manifest.Members.Count);
        for (int i = 0; i < golden.Length; i++) { var m = assessed.Manifest.Members[i]; Assert.Equal(golden[i].GetProperty("Name").GetString(), m.Name); Assert.Equal(golden[i].GetProperty("Sha256").GetString(), m.Sha256); Assert.Equal(golden[i].GetProperty("Metadata").GetString(), Convert.ToHexString(m.Metadata)); }
        ArchiveMemberKey Key(int i) { var m = assessed.Manifest.Members[i]; return new(assessed.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        byte[] payload = "external replacement"u8.ToArray(); var plan = ArchiveTarTransformPlan.Create(assessed, [Input(payload)],
            [new(ArchiveChangeKind.Delete, Key(0)), new(ArchiveChangeKind.Replace, Key(2), inputId: 7), new(ArchiveChangeKind.Rename, Key(3), "moved/")], [new("added", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, source, payload); Assert.Equal(source[..4], result[..4]); Save("external-" + dialect, result, plan.Expected);
    }
    [Fact]
    public async Task Fresh_verification_rejects_an_otherwise_valid_stream_with_a_different_block_size()
    {
        byte[] payload = "new data"u8.ToArray(); var plan = ArchiveTarTransformPlan.Create(Profile, null, [Input(payload)], [], [new("file", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, null, payload), changed = Encode(Decode(result), 1);
        var verifier = new ArchiveVerificationClient((n, _) => Child.Start(n, "archive-verify"));
        Assert.Equal(ArchiveWriteReason.VerificationFailed, (await Assert.ThrowsAsync<ArchiveWriteException>(() => verifier.VerifyAsync(new("owned", changed.Length, Hash(changed)), plan.Expected, () => new MemoryStream(changed, false), Ct))).Reason);
    }
    [Fact]
    public void Replay_has_two_passes_and_checks_the_reviewed_decoded_length()
    {
        byte[] tar = ArchiveStrictTarTests.Make(TarEntryFormat.Ustar, ("file", "payload")), b = Encode(tar, 1);
        using var source = new MemoryStream(b, false); using var replay = new SeekableBzip2TarSource(source, tar.Length, 1, Ct);
        replay.ReadExactly(new byte[512]); replay.Position = 0; replay.ReadExactly(new byte[512]); Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => replay.Position = 0).Reason);
        using var wrong = new SeekableBzip2TarSource(new MemoryStream(b, false), tar.Length - 1, 1, Ct); Assert.Throws<ArchiveWriteException>(() => wrong.Complete());
    }
    private sealed class Fragmented(byte[] b) : MemoryStream(b, false)
    { public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]); }
    private sealed class CountedSource(byte[] b) : Stream
    {
        private readonly MemoryStream source = new(b, false);
        internal long BytesRead;
        public override int Read(Span<byte> buffer) { int n = source.Read(buffer); BytesRead += n; return n; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int ReadByte() { int n = source.ReadByte(); if (n >= 0) BytesRead++; return n; }
        public override bool CanRead => source.CanRead; public override bool CanSeek => source.CanSeek; public override bool CanWrite => false;
        public override long Length => source.Length; public override long Position { get => source.Position; set => source.Position = value; }
        public override long Seek(long o, SeekOrigin s) => source.Seek(o, s); public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
    private sealed class FailingStage : MemoryStream
    {
        internal bool Fail;
        public override void WriteByte(byte b) { if (Fail) throw new IOException("Owned finalizer fault."); base.WriteByte(b); }
        public override void Write(byte[] b, int o, int c) { if (Fail) throw new IOException("Owned finalizer fault."); base.Write(b, o, c); }
        public override void Write(ReadOnlySpan<byte> b) { if (Fail) throw new IOException("Owned finalizer fault."); base.Write(b); }
    }
    private static void Save(string name, byte[] bytes, ArchiveWriteManifest manifest)
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_BZIP2_ORACLE_DIRECTORY"); if (string.IsNullOrEmpty(root)) return;
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new IOException("Owned oracle output required.");
        File.WriteAllBytes(Path.Combine(root, name + ".tar.bz2"), bytes); File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(new
        { Header = Convert.ToHexString(bytes[..4]), Members = manifest.Members.Select(m => new { m.Name, Type = (int)m.Type, m.Length, m.Sha256, Metadata = Convert.ToHexString(m.Metadata) }) }));
    }
}
