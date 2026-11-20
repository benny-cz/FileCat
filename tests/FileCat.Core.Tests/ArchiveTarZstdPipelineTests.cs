using System.Buffers.Binary;
using System.Formats.Tar;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveTarZstdPipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly ArchiveWriteProfile Profile = new(ArchiveWriteFormat.TarZstd);
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static byte[] Encode(byte[] b, int level = 0)
    { using var output = new MemoryStream(); using (var encoder = new StrictZstdWriteStream(output, level, Ct)) encoder.Write(b); return output.ToArray(); }
    private static byte[] Decode(byte[] b)
    { using var source = new MemoryStream(b, false); using var decoder = new StrictZstdReadStream(source, Ct); using var output = new MemoryStream(); decoder.CopyTo(output); Assert.True(source.CanRead); return output.ToArray(); }
    private static Task<ArchiveAssessment> Assess(byte[] b) => new ArchiveAssessmentClient((n, _) => Child.Start(n))
        .AssessAsync(new(Profile, b.Length, Hash(b)), () => new MemoryStream(b, false), Ct);
    private static ArchiveTransformInput Input(byte[] b) => new(7, b.Length, Hash(b), new ArchiveTimestamp(1700000000, 123456789), 0x1ed);
    private static async Task<byte[]> Transform(ArchiveTarTransformPlan plan, byte[]? original, byte[]? payload = null)
    {
        var invocation = ArchiveTransformWire.Prepare(plan, Ct); using var stage = new CountedStage(); Child? transform = null, verifier = null;
        var receipt = await new ArchiveTransformClient((n, _) => transform = Child.Start(n, "archive-transform"))
            .RebuildAsync(invocation, stage, () => new MemoryStream(original!, false), _ => new MemoryStream(payload!, false), Ct);
        byte[] result = stage.ToArray(); Assert.Equal(result.Length, receipt.Length); Assert.InRange(stage.Length, 9, invocation.Request.OutputLengthBound);
        Assert.InRange(stage.Writes, 1, 8 + 4 * ((result.Length + ArchiveIoAuthority.MaximumReadBytes - 1) / ArchiveIoAuthority.MaximumReadBytes));
        var verified = await new ArchiveVerificationClient((n, _) => verifier = Child.Start(n, "archive-verify"))
            .VerifyAsync(new("owned-parent-stage", result.Length, Hash(result)), plan.Expected, () => new MemoryStream(result, false), Ct);
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest); Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped); Assert.NotEqual(transform.ProcessId, verifier.ProcessId);
        return result;
    }
    [Theory]
    [InlineData(0, 3)] [InlineData(1, 1)] [InlineData(2, 3)] [InlineData(3, 9)]
    public void Encoder_levels_are_effective_and_output_is_checksummed_and_bounded(int level, int effective)
    {
        byte[] payload = new byte[300000]; new Random(9137).NextBytes(payload); using var stage = new MemoryStream();
        using (var encoder = new StrictZstdWriteStream(stage, level, Ct)) { Assert.Equal(effective, encoder.EffectiveCompressionLevel); encoder.Write(payload); }
        byte[] encoded = stage.ToArray(); Assert.Equal(payload, Decode(encoded));
        using var source = new MemoryStream(encoded, false); using var decoded = new StrictZstdReadStream(source, Ct);
        Assert.True(decoded.Header.Checksum); Assert.Null(decoded.Header.ContentSize); Assert.InRange(decoded.Header.WindowSize, 1UL, 8UL << 20);
        using var fragmented = new Fragmented(encoded); using var fragmentedDecoder = new StrictZstdReadStream(fragmented, Ct);
        using var result = new MemoryStream(); fragmentedDecoder.CopyTo(result); Assert.Equal(payload, result.ToArray());
    }
    [Fact]
    public void Selected_levels_change_the_encoded_result_on_a_fixed_search_sensitive_corpus()
    {
        byte[] dictionary = new byte[4096], noise = new byte[31]; var random = new Random(9181); random.NextBytes(dictionary);
        using var data = new MemoryStream();
        for (int i = 0; i < 2048; i++)
        {
            data.Write(dictionary.AsSpan(i * 13 % 3000, 16 + i * 37 % 1000)); random.NextBytes(noise); data.Write(noise);
        }
        byte[] payload = data.ToArray(); var results = Enumerable.Range(0, 4).Select(level => Encode(payload, level)).ToArray();
        Assert.Equal(results[0], results[2]); Assert.Equal(3, results.Skip(1).Select(Hash).Distinct(StringComparer.Ordinal).Count());
        foreach (var result in results) Assert.Equal(payload, Decode(result));
    }
    [Theory]
    [InlineData(true, 0, 0)] [InlineData(true, 0, 5)] [InlineData(true, 1, 512)]
    [InlineData(true, 2, 512)] [InlineData(true, 3, 512)] [InlineData(false, 0, 512)]
    [InlineData(false, 1, 512)] [InlineData(false, 2, 512)] [InlineData(false, 3, 512)]
    public void Every_content_size_width_and_unknown_size_are_validated(bool single, int flag, int length)
    {
        byte[] payload = new byte[length]; new Random(9153).NextBytes(payload); byte[] frame = Raw(payload, single, flag, 7);
        Assert.Equal(payload, Decode(frame));
        using var source = new MemoryStream(frame, false); var header = ZstdFrameHeader.Read(source, Ct);
        Assert.Equal(single || flag != 0 ? (ulong?)length : null, header.ContentSize); Assert.False(header.Checksum);
    }
    [Theory]
    [InlineData("magic", ArchiveWriteReason.InvalidArchive)] [InlineData("reserved", ArchiveWriteReason.InvalidArchive)]
    [InlineData("unused", ArchiveWriteReason.UnsupportedFeature)] [InlineData("dictionary", ArchiveWriteReason.UnsupportedFeature)]
    [InlineData("skippable", ArchiveWriteReason.UnsupportedFeature)] [InlineData("window", ArchiveWriteReason.BudgetExceeded)]
    [InlineData("single-size", ArchiveWriteReason.BudgetExceeded)] [InlineData("block-type", ArchiveWriteReason.InvalidArchive)]
    [InlineData("block-size", ArchiveWriteReason.InvalidArchive)] [InlineData("checksum", ArchiveWriteReason.InvalidArchive)]
    [InlineData("concatenated", ArchiveWriteReason.InvalidArchive)] [InlineData("trailing", ArchiveWriteReason.InvalidArchive)]
    [InlineData("content-size", ArchiveWriteReason.InvalidArchive)]
    public void Unsupported_or_corrupt_frames_refuse_without_decoder_fallback(string change, ArchiveWriteReason reason)
    {
        byte[] b = Encode("hello"u8.ToArray());
        switch (change)
        {
            case "magic": b[0] = 0; break; case "reserved": b[4] |= 8; break; case "unused": b[4] |= 16; break;
            case "dictionary": b[4] |= 1; break; case "skippable": BinaryPrimitives.WriteUInt32LittleEndian(b, 0x184D2A50); break;
            case "window": b[5] = 0xf8; break;
            case "single-size": b = Raw([], true, 3); BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(5), ulong.MaxValue); break;
            case "block-type": b[6] = (byte)(b[6] | 6); break;
            case "block-size": b[6] = 9; b[7] = 0; b[8] = 16; break;
            case "checksum": b[^1] ^= 1; break; case "concatenated": b = [.. b, .. b]; break; case "trailing": b = [.. b, 0]; break;
            case "content-size": b = Raw("hello"u8.ToArray(), true, 0); b[5] = 6; break;
        }
        Assert.Equal(reason, Assert.Throws<ArchiveWriteException>(() => Decode(b)).Reason);
    }
    [Fact]
    public void Every_truncation_of_a_checksummed_frame_refuses()
    {
        byte[] source = Encode("a complete checksummed frame"u8.ToArray());
        for (int length = 0; length < source.Length; length++) Assert.Throws<ArchiveWriteException>(() => Decode(source[..length]));
    }
    [Fact]
    public void Empty_raw_compression_roundtrips_and_does_not_close_the_stage()
    { byte[] b = Encode([]); Assert.Empty(Decode(b)); }
    [Fact]
    public void FileCat_output_has_no_exemption_from_expansion_limits()
    {
        using var stage = new MemoryStream(); byte[] chunk = new byte[1 << 20];
        using (var encoder = new StrictZstdWriteStream(stage, 0, Ct)) for (int i = 0; i < 65; i++) encoder.Write(chunk);
        stage.Position = 0; using var decoder = new StrictZstdReadStream(stage, Ct);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => decoder.CopyTo(Stream.Null)).Reason);
    }
    [Fact]
    public void Abort_suppresses_trailers_and_healthy_finalizer_faults_are_not_hidden()
    {
        using var stage = new FailingStage(); var encoder = new StrictZstdWriteStream(stage, 0, Ct); encoder.Write("payload"u8);
        encoder.Abort(); long before = stage.Length; Assert.Throws<IOException>(() => encoder.WriteByte(1)); encoder.Dispose(); Assert.Equal(before, stage.Length);
        var healthy = new StrictZstdWriteStream(stage, 0, Ct); healthy.Write("payload"u8); stage.Fail = true;
        Assert.Throws<IOException>(healthy.Dispose); healthy.Dispose(); Assert.True(stage.CanWrite);
    }
    [Theory]
    [InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)] [InlineData(0, true)]
    public async Task Creation_levels_and_empty_creation_use_distinct_transform_and_verifier_processes(int level, bool empty)
    {
        byte[] payload = "new Zstandard payload"u8.ToArray(); var profile = new ArchiveWriteProfile(ArchiveWriteFormat.TarZstd, compressionLevel: level);
        var plan = ArchiveTarTransformPlan.Create(profile, null, empty ? [] : [Input(payload)], [], empty ? [] : [new("file", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, null, payload); Assert.Equal((byte)1, plan.Expected.Features[2]); Save("create-" + level + "-" + empty, result, plan.Expected);
    }
    [Theory]
    [InlineData(TarEntryFormat.Ustar, false)] [InlineData(TarEntryFormat.Ustar, true)]
    [InlineData(TarEntryFormat.Pax, false)] [InlineData(TarEntryFormat.Pax, true)]
    public async Task Updates_preserve_dialect_and_duplicate_scope_and_always_emit_a_checksum(TarEntryFormat dialect, bool checksum)
    {
        byte[] tar = ArchiveStrictTarTests.Make(dialect, ("same", "first"), ("same", "second"), ("old", "before"));
        byte[] source = checksum ? Encode(tar) : Raw(tar, false, 0, 47); var assessed = await Assess(source);
        Assert.Equal(checksum ? (byte)1 : (byte)0, assessed.Manifest.Features[2]);
        ArchiveMemberKey Key(int i) { var m = assessed.Manifest.Members[i]; return new(assessed.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        byte[] payload = "replacement"u8.ToArray(); var plan = ArchiveTarTransformPlan.Create(assessed, [Input(payload)],
            [new(ArchiveChangeKind.Delete, Key(0)), new(ArchiveChangeKind.Rename, Key(1), "renamed"), new(ArchiveChangeKind.Replace, Key(2), inputId: 7)], [new("added", ArchiveMemberType.File, 7)], Ct);
        Assert.Equal((byte)1, plan.Expected.Features[2]); byte[] result = await Transform(plan, source, payload); Save("update-" + dialect + "-" + checksum, result, plan.Expected);
    }
    [Fact]
    public async Task Removing_all_members_produces_a_valid_checksummed_empty_TAR()
    {
        byte[] source = Encode(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "before"))); var assessed = await Assess(source);
        var plan = ArchiveTarTransformPlan.Create(assessed, [], [new(ArchiveChangeKind.Delete, new(assessed.CatalogRevision, "file", ArchiveMemberType.File, 0))], [], Ct);
        byte[] result = await Transform(plan, source); Assert.Equal(new byte[] { 4, 1, 1 }, plan.Expected.Features); Save("delete-all", result, plan.Expected);
    }
    [Fact]
    public async Task Incompressible_multi_block_data_uses_bounded_IPC_writes()
    {
        byte[] payload = new byte[2 << 20]; new Random(9167).NextBytes(payload);
        var plan = ArchiveTarTransformPlan.Create(Profile, null, [Input(payload)], [], [new("file", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, null, payload); Assert.InRange(result.Length, payload.Length / 2, ArchiveTransformWire.OutputBound(plan.Expected, Ct));
    }
    [Theory]
    [InlineData("window", ArchiveWriteReason.BudgetExceeded)] [InlineData("checksum", ArchiveWriteReason.InvalidArchive)]
    public async Task Hostile_frames_refuse_through_the_worker_without_an_abnormal_exit(string change, ArchiveWriteReason reason)
    {
        byte[] source = Encode(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "before")));
        if (change == "window") source[5] = 0xf8; else source[^1] ^= 1;
        Child? child = null; var client = new ArchiveAssessmentClient((n, _) => child = Child.Start(n));
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(new(Profile, source.Length, Hash(source)), () => new MemoryStream(source, false), Ct));
        Assert.Equal(reason, error.Reason); Assert.True(child!.Reaped); Assert.Equal(0u, child.ExitCode);
    }
    [Fact]
    public async Task Fresh_verification_refuses_an_unchecked_frame_with_otherwise_correct_content()
    {
        byte[] payload = "new data"u8.ToArray(); var plan = ArchiveTarTransformPlan.Create(Profile, null, [Input(payload)], [], [new("file", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, null, payload), changed = Raw(Decode(result), true, 2);
        var verifier = new ArchiveVerificationClient((n, _) => Child.Start(n, "archive-verify"));
        Assert.Equal(ArchiveWriteReason.VerificationFailed, (await Assert.ThrowsAsync<ArchiveWriteException>(() => verifier.VerifyAsync(new("owned", changed.Length, Hash(changed)), plan.Expected, () => new MemoryStream(changed, false), Ct))).Reason);
    }
    [Fact]
    public void Catalog_reuse_stays_within_four_physical_passes_and_buffers_block_header_IO()
    {
        byte[] source = Encode(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, Enumerable.Range(0, 64).Select(i => ("file-" + i, "payload-" + i)).ToArray()));
        using var counted = new CountedSource(source); using var stage = new MemoryStream();
        var result = ZstdTarRewriteEngine.Rebuild(counted, Hash(source), Profile, stage, [], [], new Dictionary<int, TarInputDescriptor>(), _ => throw new InvalidOperationException(), Ct);
        Assert.Equal(64, result.Members.Count); Assert.InRange(counted.BytesRead, source.Length, 4L * source.Length);
        Assert.InRange(counted.Reads, 1, 4 * ((source.Length + ArchiveIoAuthority.MaximumReadBytes - 1) / ArchiveIoAuthority.MaximumReadBytes));
    }
    [Fact]
    public void Replay_checks_both_decoded_length_and_a_finite_rewind_count()
    {
        byte[] tar = ArchiveStrictTarTests.Make(TarEntryFormat.Ustar, ("file", "payload")), b = Encode(tar);
        using var source = new MemoryStream(b, false); var header = ZstdFrameHeader.Read(source, Ct);
        using var replay = new SeekableZstdTarSource(source, tar.Length, header, Ct); replay.ReadExactly(new byte[512]); replay.Position = 0; replay.ReadExactly(new byte[512]);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => replay.Position = 0).Reason);
        using var wrong = new SeekableZstdTarSource(new MemoryStream(b, false), tar.Length - 1, header, Ct); Assert.Throws<ArchiveWriteException>(() => wrong.Complete());
    }
    [Theory]
    [InlineData("ustar")] [InlineData("pax")]
    public async Task Independent_inputs_are_assessed_and_updated_with_exact_metadata(string dialect)
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_ZSTD_INPUT_ORACLE_DIRECTORY");
        Assert.SkipUnless(root is not null && Path.IsPathFullyQualified(root) && Directory.Exists(root), "Owned independent Zstandard fixtures required.");
        byte[] source = File.ReadAllBytes(Path.Combine(root!, dialect + ".tar.zst")); using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, dialect + ".json")));
        var assessed = await Assess(source); var golden = expected.RootElement.EnumerateArray().ToArray(); Assert.Equal(golden.Length, assessed.Manifest.Members.Count);
        for (int i = 0; i < golden.Length; i++) { var m = assessed.Manifest.Members[i]; Assert.Equal(golden[i].GetProperty("Name").GetString(), m.Name); Assert.Equal(golden[i].GetProperty("Sha256").GetString(), m.Sha256); Assert.Equal(golden[i].GetProperty("Metadata").GetString(), Convert.ToHexString(m.Metadata)); }
        ArchiveMemberKey Key(int i) { var m = assessed.Manifest.Members[i]; return new(assessed.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        byte[] payload = "external replacement"u8.ToArray(); var plan = ArchiveTarTransformPlan.Create(assessed, [Input(payload)],
            [new(ArchiveChangeKind.Delete, Key(0)), new(ArchiveChangeKind.Replace, Key(2), inputId: 7), new(ArchiveChangeKind.Rename, Key(3), "moved/")], [new("added", ArchiveMemberType.File, 7)], Ct);
        byte[] result = await Transform(plan, source, payload); Save("external-" + dialect, result, plan.Expected);
    }
    private static byte[] Raw(byte[] payload, bool single, int sizeFlag, byte window = 0)
    {
        using var stream = new MemoryStream(); stream.Write(new byte[] { 0x28, 0xb5, 0x2f, 0xfd, (byte)(sizeFlag << 6 | (single ? 32 : 0)) });
        if (!single) stream.WriteByte(window); int width = sizeFlag == 0 ? single ? 1 : 0 : 1 << sizeFlag;
        Span<byte> size = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(size, (ulong)(width == 2 ? payload.Length - 256 : payload.Length)); stream.Write(size[..width]);
        uint block = ((uint)payload.Length << 3) | 1; stream.WriteByte((byte)block); stream.WriteByte((byte)(block >> 8)); stream.WriteByte((byte)(block >> 16)); stream.Write(payload); return stream.ToArray();
    }
    private sealed class Fragmented(byte[] b) : MemoryStream(b, false)
    { public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, 1)]); }
    private sealed class CountedStage : MemoryStream
    { internal int Writes; public override void Write(ReadOnlySpan<byte> b) { Writes++; base.Write(b); } }
    private sealed class CountedSource(byte[] b) : Stream
    {
        private readonly MemoryStream source = new(b, false); internal long BytesRead; internal int Reads;
        public override int Read(Span<byte> b) { int n = source.Read(b); BytesRead += n; if (n != 0) Reads++; return n; }
        public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c)); public override int ReadByte() { int n = source.ReadByte(); if (n >= 0) { BytesRead++; Reads++; } return n; }
        public override bool CanRead => source.CanRead; public override bool CanSeek => source.CanSeek; public override bool CanWrite => false;
        public override long Length => source.Length; public override long Position { get => source.Position; set => source.Position = value; }
        public override long Seek(long o, SeekOrigin s) => source.Seek(o, s); public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }
    private sealed class FailingStage : MemoryStream
    { internal bool Fail; public override void Write(byte[] b, int o, int c) { if (Fail) throw new IOException("Owned finalizer fault."); base.Write(b, o, c); } public override void Write(ReadOnlySpan<byte> b) { if (Fail) throw new IOException("Owned finalizer fault."); base.Write(b); } }
    private static void Save(string name, byte[] bytes, ArchiveWriteManifest manifest)
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_ZSTD_ORACLE_DIRECTORY"); if (string.IsNullOrEmpty(root)) return;
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new IOException("Owned oracle output required.");
        File.WriteAllBytes(Path.Combine(root, name + ".tar.zst"), bytes); File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(new
        { Header = Convert.ToHexString(bytes[..6]), Members = manifest.Members.Select(m => new { m.Name, Type = (int)m.Type, m.Length, m.Sha256, Metadata = Convert.ToHexString(m.Metadata) }) }));
    }
}
