using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

public sealed class ArchiveTarGzipPipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static ArchiveTransformInput Input(int id, byte[] bytes) => new(id, bytes.Length, Hash(bytes), new ArchiveTimestamp(1700000000, 123456789), 0x1ed);
    private static byte[] Compress(byte[] tar, byte os = 255, CompressionLevel level = CompressionLevel.Optimal)
    { using var result = new MemoryStream(); using (var gzip = new StrictGzipWriteStream(result, level, os)) gzip.Write(tar); return result.ToArray(); }
    private static Task<ArchiveAssessment> Assess(byte[] bytes, ArchiveWriteProfile? profile = null) =>
        new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce)).AssessAsync(new(profile ?? new(ArchiveWriteFormat.TarGzip), bytes.Length, Hash(bytes)), () => new MemoryStream(bytes, false), Ct);
    private static async Task<byte[]> Transform(ArchiveTarTransformPlan plan, byte[]? original, params (int Id, byte[] Bytes)[] captured)
    {
        var invocation = ArchiveTransformWire.Prepare(plan, Ct); using var stage = new MemoryStream(); Child? transform = null, verifier = null;
        var receipt = await new ArchiveTransformClient((nonce, _) => transform = Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => new MemoryStream(original!, false), id => new MemoryStream(captured.Single(p => p.Id == id).Bytes, false), Ct);
        byte[] data = stage.ToArray(); Assert.Equal(data.Length, receipt.Length); Assert.InRange(stage.Length, 20, invocation.Request.OutputLengthBound);
        var verified = await new ArchiveVerificationClient((nonce, _) => verifier = Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("owned-parent-stage", data.Length, Hash(data)), plan.Expected, () => new MemoryStream(data, false), Ct);
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest); Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped); Assert.NotEqual(transform.ProcessId, verifier.ProcessId);
        using var outer = new GZipStream(new MemoryStream(data, false), CompressionMode.Decompress);
        using var reader = new TarReader(outer); int i = 0;
        while (reader.GetNextEntry() is { } member)
        {
            var expected = plan.Expected.Members[i++]; Assert.Equal(expected.Name, member.Name); Assert.Equal(expected.Length, member.Length);
            Assert.Equal(expected.Sha256, member.DataStream is { } payload ? Convert.ToHexString(SHA256.HashData(payload)) : Hash([]));
        }
        Assert.Equal(plan.Expected.Members.Count, i);
        return data;
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    public async Task Creation_and_fresh_verification_preserve_exact_time_and_finalize_the_whole_wrapper(int level, bool empty)
    {
        byte[] payload = new byte[2 << 20]; new Random(741).NextBytes(payload);
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.TarGzip, compressionLevel: level);
        var plan = ArchiveTarTransformPlan.Create(profile, null, empty ? [] : [Input(7, payload)], [], empty ? [] : [new("payload", ArchiveMemberType.File, 7)], Ct);
        byte[] data = await Transform(plan, null, empty ? [] : [(7, payload)]);
        Assert.Equal(new byte[] { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0, ArchiveTarFeatures.Flags(level), 255 }, data[..10]);
        Assert.Equal(empty ? (byte)1 : (byte)2, plan.Expected.Features[1]);
        Save("create-" + level + "-" + empty, data, plan.Expected);
    }

    [Theory]
    [InlineData(TarEntryFormat.Ustar, (byte)3, CompressionLevel.Fastest)]
    [InlineData(TarEntryFormat.Ustar, (byte)255, CompressionLevel.SmallestSize)]
    [InlineData(TarEntryFormat.Pax, (byte)7, CompressionLevel.Optimal)]
    [InlineData(TarEntryFormat.Pax, (byte)0, CompressionLevel.SmallestSize)]
    public async Task Updates_bind_physical_revisions_preserve_wrapper_and_target_exact_occurrences(TarEntryFormat dialect, byte os, CompressionLevel level)
    {
        byte[] source = Compress(ArchiveStrictTarTests.Make(dialect, ("same", "first"), ("same", "second"), ("remove", "bye"), ("dir/", "")), os, level);
        var assessed = await Assess(source); Assert.Equal(Hash(source), assessed.CatalogRevision);
        ArchiveMemberKey Key(int i) { var m = assessed.Manifest.Members[i]; return new(assessed.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        byte[] replacement = "replacement"u8.ToArray();
        var plan = ArchiveTarTransformPlan.Create(assessed, [Input(9, replacement)],
            [new(ArchiveChangeKind.Replace, Key(1), inputId: 9), new(ArchiveChangeKind.Delete, Key(2)), new(ArchiveChangeKind.Rename, Key(3), "renamed/")],
            [new("added", ArchiveMemberType.File, 9)], Ct);
        using (var direct = new MemoryStream())
        {
            var rebuilt = GzipTarRewriteEngine.Rebuild(new MemoryStream(source, false), Hash(source), plan.Expected.Profile, direct,
                plan.Edits.Select(e => new TarWriteEdit(e.Kind, e.Key, e.NewName, e.InputId)).ToArray(),
                plan.Additions.Select(a => new TarWriteAddition(a.Name, a.Type, a.InputId)).ToArray(),
                plan.Inputs.ToDictionary(p => p.Key, p => new TarInputDescriptor(p.Key, p.Value.Length, p.Value.Sha256, p.Value.ModifiedTimestamp, p.Value.Mode)),
                _ => new MemoryStream(replacement, false), Ct);
            Assert.Equal(plan.Expected.Digest, ArchiveAssessmentWorker.Manifest(plan.Expected.Profile, rebuilt.Tar.Format, rebuilt.Tar.Members, Ct, rebuilt.OperatingSystem, rebuilt.ExtraFlags).Digest);
        }
        byte[] output = await Transform(plan, source, (9, replacement));
        Assert.Equal(source[..10], output[..10]); Assert.Equal(new[] { "same", "same", "renamed/", "added" }, plan.Expected.Members.Select(m => m.Name));
        Assert.Equal(assessed.Manifest.Members[0].Metadata.ToArray(), plan.Expected.Members[0].Metadata.ToArray());
        Assert.Equal(assessed.Manifest.Members[1].Metadata.ToArray(), plan.Expected.Members[1].Metadata.ToArray());
        Save("update-" + dialect + "-" + os, output, plan.Expected);
    }

    [Fact]
    public async Task Removing_all_members_keeps_wrapper_fields_and_produces_a_complete_empty_TAR()
    {
        byte[] source = Compress(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("one", "value")), 3); var assessed = await Assess(source); var old = assessed.Manifest.Members[0];
        var plan = ArchiveTarTransformPlan.Create(assessed, [], [new(ArchiveChangeKind.Delete, new(assessed.CatalogRevision, old.Name, old.Type, old.Occurrence))], [], Ct);
        byte[] data = await Transform(plan, source); Assert.Equal(source[..10], data[..10]); Assert.Empty(plan.Expected.Members);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Fresh_verification_compares_dialect_OS_and_extra_flags_in_addition_to_member_bytes(int field)
    {
        byte[] bytes = Compress(ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("payload", "value")));
        var actual = await Assess(bytes); byte[] features = actual.Manifest.Features.ToArray();
        features[field] = field == 2 ? (byte)3 : field == 3 ? (byte)2 : (byte)1;
        var expected = new ArchiveWriteManifest(actual.Manifest.Profile, actual.Manifest.Members, features);
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => new ArchiveVerificationClient((nonce, _) => Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("held-stage", bytes.Length, Hash(bytes)), expected, () => new MemoryStream(bytes, false), Ct));
        Assert.Equal(ArchiveWriteReason.VerificationFailed, error.Reason);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("mtime")]
    [InlineData("reserved")]
    [InlineData("extra-flags")]
    [InlineData("crc")]
    [InlineData("size")]
    [InlineData("truncated")]
    [InlineData("concatenated")]
    [InlineData("trailing")]
    public async Task Strict_assessment_refuses_outer_metadata_damage_and_hidden_extra_streams(string mutation)
    {
        byte[] data = Compress(ArchiveStrictTarTests.Make(TarEntryFormat.Ustar, ("file", "payload")));
        switch (mutation)
        {
            case "name": data[3] = 8; break;
            case "mtime": data[4] = 1; break;
            case "reserved": data[3] = 128; break;
            case "extra-flags": data[8] = 1; break;
            case "crc": data[^8] ^= 1; break;
            case "size": data[^4] ^= 1; break;
            case "truncated": data = data[..^1]; break;
            case "concatenated": data = [.. data, .. data]; break;
            case "trailing": data = [.. data, 0]; break;
        }
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => Assess(data));
        Assert.True(failure.Reason is ArchiveWriteReason.UnsupportedFeature or ArchiveWriteReason.InvalidArchive);
    }

    [Fact]
    public void Replay_has_bounded_rewinds_and_never_accepts_a_guessed_decoded_length()
    {
        byte[] tar = ArchiveStrictTarTests.Make(TarEntryFormat.Ustar, ("file", "payload")), bytes = Compress(tar);
        using var source = new MemoryStream(bytes, false); using var replay = new SeekableGzipTarSource(source, tar.Length, 255, 0, Ct);
        replay.ReadExactly(new byte[512]); replay.Position = 0; replay.ReadExactly(new byte[512]);
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => replay.Position = 0).Reason);
        Assert.Throws<ArchiveWriteException>(() => replay.ReadByte());
        using var wrongLength = new SeekableGzipTarSource(new MemoryStream(bytes, false), tar.Length - 1, 255, 0, Ct);
        Assert.Throws<ArchiveWriteException>(() => wrongLength.Complete());
    }

    [Fact]
    public void A_many_member_update_reuses_its_catalog_and_reads_at_most_four_physical_source_passes()
    {
        byte[] before = Compress(ArchiveStrictTarTests.Make(TarEntryFormat.Pax,
            Enumerable.Range(0, 64).Select(i => ("file-" + i, "payload-" + i)).ToArray()));
        using var original = new CountedSource(new MemoryStream(before, false)); using var stage = new MemoryStream();
        var rebuilt = GzipTarRewriteEngine.Rebuild(original, Hash(before), new(ArchiveWriteFormat.TarGzip), stage, [], [],
            new Dictionary<int, TarInputDescriptor>(), _ => throw new InvalidOperationException("No captured input exists."), Ct);
        Assert.Equal(64, rebuilt.Tar.Members.Count); Assert.InRange(original.BytesRead, before.Length, 4L * before.Length);
        TarRewriteEngine.VerifyGzip(stage, rebuilt.Tar, Ct); Assert.True(original.CanRead);
    }

    private sealed class CountedSource(Stream source) : Stream
    {
        internal long BytesRead { get; private set; }
        public override bool CanRead => source.CanRead; public override bool CanSeek => source.CanSeek; public override bool CanWrite => false;
        public override long Length => source.Length; public override long Position { get => source.Position; set => source.Position = value; }
        public override int Read(byte[] b, int o, int c) => Read(b.AsSpan(o, c));
        public override int Read(Span<byte> b) { int n = source.Read(b); BytesRead += n; return n; }
        public override long Seek(long o, SeekOrigin s) => source.Seek(o, s);
        public override void Flush() => throw new NotSupportedException();
        public override void SetLength(long n) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) source.Dispose(); base.Dispose(disposing); }
    }

    [Fact]
    public void An_aborted_encoder_cannot_forward_more_output_or_append_a_trailer_during_disposal()
    {
        using var stage = new MemoryStream(); var gzip = new StrictGzipWriteStream(stage, CompressionLevel.Optimal);
        gzip.Write(new byte[1 << 20]); gzip.Abort(); long before = stage.Length;
        Assert.Throws<IOException>(() => gzip.WriteByte(1)); Assert.Throws<IOException>(() => gzip.Flush()); gzip.Dispose();
        Assert.Equal(before, stage.Length); Assert.True(stage.CanWrite);
    }

    [Theory]
    [InlineData("ustar")]
    [InlineData("pax")]
    public async Task Independently_written_inputs_are_fully_assessed_and_updated_without_losing_metadata(string dialect)
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_GZIP_INPUT_ORACLE_DIRECTORY");
        Assert.SkipUnless(root is not null && Path.IsPathFullyQualified(root) && Directory.Exists(root), "Opt in with an owned independent input-fixture directory.");
        byte[] source = File.ReadAllBytes(Path.Combine(root!, dialect + ".tar.gz"));
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(root!, dialect + ".json")));
        var assessed = await Assess(source); var golden = expected.RootElement.EnumerateArray().ToArray();
        Assert.Equal(golden.Length, assessed.Manifest.Members.Count);
        for (int i = 0; i < golden.Length; i++)
        {
            var actual = assessed.Manifest.Members[i]; Assert.Equal(golden[i].GetProperty("Name").GetString(), actual.Name);
            Assert.Equal(golden[i].GetProperty("Sha256").GetString(), actual.Sha256);
            Assert.Equal(golden[i].GetProperty("Metadata").GetString(), Convert.ToHexString(actual.Metadata));
        }
        ArchiveMemberKey Key(int i) { var m = assessed.Manifest.Members[i]; return new(assessed.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        byte[] payload = "replaced external payload"u8.ToArray();
        var plan = ArchiveTarTransformPlan.Create(assessed, [Input(9, payload)],
            [new(ArchiveChangeKind.Delete, Key(0)), new(ArchiveChangeKind.Replace, Key(2), inputId: 9), new(ArchiveChangeKind.Rename, Key(3), "moved/")],
            [new("added", ArchiveMemberType.File, 9)], Ct);
        byte[] result = await Transform(plan, source, (9, payload)); Assert.Equal(source[..10], result[..10]);
        Save("external-" + dialect, result, plan.Expected);
    }

    private static void Save(string name, byte[] bytes, ArchiveWriteManifest manifest)
    {
        string? root = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_GZIP_ORACLE_DIRECTORY"); if (string.IsNullOrEmpty(root)) return;
        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) throw new IOException("The owned oracle directory must already exist.");
        File.WriteAllBytes(Path.Combine(root, name + ".tar.gz"), bytes);
        File.WriteAllText(Path.Combine(root, name + ".json"), JsonSerializer.Serialize(new
        { Header = Convert.ToHexString(bytes[..10]), Members = manifest.Members.Select(m => new { m.Name, Type = (int)m.Type, m.Length, m.Sha256, Metadata = Convert.ToHexString(m.Metadata) }) }));
    }
}
