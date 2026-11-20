using System.IO.Compression;
using System.Security.Cryptography;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

/// <summary>
/// AW7: standalone gzip, bzip2 and Zstandard streams through real assessment, transform and fresh-verification worker
/// processes. One synthetic member, exact payload bytes, preserved wrapper features and every refused shape.
/// </summary>
[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveStandalonePipelineTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Hash(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
    private static ArchiveWriteProfile Profile(ArchiveWriteFormat format, int level = 0) => new(format, compressionLevel: level);
    private static ArchiveTransformInput Input(byte[] b, int id = 7) => new(id, b.Length, Hash(b), new ArchiveTimestamp(1700000000, 0), 0x1a4);
    private static ArchiveTransformAddition Addition(int id = 7, string name = ArchiveStandalone.MemberName) => new(name, ArchiveMemberType.File, id);

    private static Task<ArchiveAssessment> Assess(ArchiveWriteProfile profile, byte[] b) => new ArchiveAssessmentClient((n, _) => Child.Start(n))
        .AssessAsync(new(profile, b.Length, Hash(b)), () => new MemoryStream(b, false), Ct);

    private static async Task<byte[]> Transform(ArchiveStandaloneTransformPlan plan, byte[]? original, byte[] payload)
    {
        var invocation = ArchiveTransformWire.Prepare(plan, Ct); using var stage = new MemoryStream(); Child? transform = null, verifier = null;
        var receipt = await new ArchiveTransformClient((n, _) => transform = Child.Start(n, "archive-transform"))
            .RebuildAsync(invocation, stage, () => new MemoryStream(original!, false), _ => new MemoryStream(payload, false), Ct);
        byte[] result = stage.ToArray(); Assert.Equal(result.Length, receipt.Length);
        Assert.InRange(result.Length, ArchiveTransformWire.MinimumOutputLength(plan.Expected.Profile.Format), invocation.Request.OutputLengthBound);
        var verified = await new ArchiveVerificationClient((n, _) => verifier = Child.Start(n, "archive-verify"))
            .VerifyAsync(new("owned-parent-stage", result.Length, Hash(result)), plan.Expected, () => new MemoryStream(result, false), Ct);
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest); Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped);
        return result;
    }

    private static byte[] Decode(ArchiveWriteFormat format, byte[] b)
    {
        using var source = new MemoryStream(b, false); using var output = new MemoryStream();
        using Stream decoder = format switch
        {
            ArchiveWriteFormat.Gzip => new StrictGzipReadStream(source, Ct),
            ArchiveWriteFormat.Bzip2 => new StrictBzip2ReadStream(source, Ct),
            _ => new StrictZstdReadStream(source, Ct),
        };
        decoder.CopyTo(output); return output.ToArray();
    }

    private static byte[] Payload(int length, int seed = 4711)
    {
        byte[] payload = new byte[length]; new Random(seed).NextBytes(payload);
        for (int i = 0; i < payload.Length / 3; i++) payload[i] = (byte)('a' + i % 7); // Partly compressible, partly not.
        return payload;
    }

    public static TheoryData<ArchiveWriteFormat, int> Creations => new()
    {
        { ArchiveWriteFormat.Gzip, 0 }, { ArchiveWriteFormat.Gzip, 1 }, { ArchiveWriteFormat.Gzip, 300_000 },
        { ArchiveWriteFormat.Bzip2, 0 }, { ArchiveWriteFormat.Bzip2, 1 }, { ArchiveWriteFormat.Bzip2, 300_000 },
        { ArchiveWriteFormat.Zstd, 0 }, { ArchiveWriteFormat.Zstd, 1 }, { ArchiveWriteFormat.Zstd, 300_000 },
    };

    [Theory, MemberData(nameof(Creations))]
    public async Task One_file_becomes_one_verified_stream_including_an_empty_file(ArchiveWriteFormat format, int length)
    {
        byte[] payload = Payload(length); var profile = Profile(format);
        var plan = ArchiveStandaloneTransformPlan.Create(profile, null, [Input(payload)], [], [Addition()], Ct);
        var member = Assert.Single(plan.Expected.Members);
        Assert.Equal((ArchiveStandalone.MemberName, ArchiveMemberType.File, 0, (long)length, Hash(payload)), (member.Name, member.Type, member.Occurrence, member.Length, member.Sha256));
        Assert.Equal(ArchiveStandalone.Features(profile), plan.Expected.Features.ToArray());
        byte[] created = await Transform(plan, null, payload);
        Assert.Equal(payload, Decode(format, created));
        // An independent decoder agrees where the runtime has one, so the stream is not only FileCat-readable.
        if (format == ArchiveWriteFormat.Gzip)
        {
            using var gzip = new GZipStream(new MemoryStream(created, false), CompressionMode.Decompress); using var plain = new MemoryStream();
            gzip.CopyTo(plain); Assert.Equal(payload, plain.ToArray());
            Assert.Equal(new byte[] { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0 }, created[..8]); // No name, no timestamp, no flags.
        }
        var assessed = await Assess(profile, created);
        Assert.Equal(plan.Expected.Digest, assessed.Manifest.Digest);
        Assert.Equal(Hash(created), assessed.CatalogRevision);
    }

    [Theory]
    [InlineData(ArchiveWriteFormat.Gzip)] [InlineData(ArchiveWriteFormat.Bzip2)] [InlineData(ArchiveWriteFormat.Zstd)]
    public async Task Replacing_the_payload_keeps_the_wrapper_features_and_changes_nothing_else(ArchiveWriteFormat format)
    {
        var profile = Profile(format); byte[] first = Payload(70_000, 1), second = Payload(123_457, 2);
        byte[] original = format switch
        {
            // A foreign gzip writer: maximum compression flag and a Unix operating-system byte.
            ArchiveWriteFormat.Gzip => Gzip(first, xfl: 2, os: 3),
            // A smaller block size than FileCat's own default.
            ArchiveWriteFormat.Bzip2 => Bzip2(first, blockSize: 1),
            _ => await Transform(ArchiveStandaloneTransformPlan.Create(profile, null, [Input(first)], [], [Addition()], Ct), null, first),
        };
        var source = await Assess(profile, original);
        var key = new ArchiveMemberKey(source.CatalogRevision, ArchiveStandalone.MemberName, ArchiveMemberType.File, 0);
        var plan = ArchiveStandaloneTransformPlan.Create(profile, source, [Input(second, 3)], [new(ArchiveChangeKind.Replace, key, inputId: 3)], [], Ct);
        Assert.Equal(source.Manifest.Features.ToArray(), plan.Expected.Features.ToArray());
        byte[] replaced = await Transform(plan, original, second);
        Assert.Equal(second, Decode(format, replaced));
        if (format == ArchiveWriteFormat.Gzip) { Assert.Equal(2, replaced[8]); Assert.Equal(3, replaced[9]); }
        if (format == ArchiveWriteFormat.Bzip2) Assert.Equal((byte)'1', replaced[3]);
        var reassessed = await Assess(profile, replaced);
        Assert.Equal(source.Manifest.Features.ToArray(), reassessed.Manifest.Features.ToArray());
    }

    public static TheoryData<string> Refusals => new() { "two-inputs", "other-name", "folder", "rename", "delete", "add-on-update", "replace-on-create", "stale-revision", "tar-profile" };

    [Theory, MemberData(nameof(Refusals))]
    public async Task Anything_but_one_file_or_one_replacement_is_refused_before_any_object_exists(string shape)
    {
        var profile = Profile(ArchiveWriteFormat.Gzip); byte[] payload = Payload(100);
        byte[] original = Gzip(Payload(50, 9));
        var source = await Assess(profile, original);
        var key = new ArchiveMemberKey(source.CatalogRevision, ArchiveStandalone.MemberName, ArchiveMemberType.File, 0);
        var error = Assert.Throws<ArchiveWriteException>(() => shape switch
        {
            "two-inputs" => ArchiveStandaloneTransformPlan.Create(profile, null, [Input(payload), Input(payload, 8)], [], [Addition(), Addition(8)], Ct),
            "other-name" => ArchiveStandaloneTransformPlan.Create(profile, null, [Input(payload)], [], [Addition(name: "report.txt")], Ct),
            "folder" => ArchiveStandaloneTransformPlan.Create(profile, null, [Input(payload)], [], [new(ArchiveStandalone.MemberName, new ArchiveTransformDirectoryMetadata(DateTimeOffset.UnixEpoch))], Ct),
            "rename" => ArchiveStandaloneTransformPlan.Create(profile, source, [Input(payload)], [new(ArchiveChangeKind.Rename, key, "renamed")], [], Ct),
            "delete" => ArchiveStandaloneTransformPlan.Create(profile, source, [Input(payload)], [new(ArchiveChangeKind.Delete, key)], [], Ct),
            "add-on-update" => ArchiveStandaloneTransformPlan.Create(profile, source, [Input(payload)], [], [Addition()], Ct),
            "replace-on-create" => ArchiveStandaloneTransformPlan.Create(profile, null, [Input(payload)], [new(ArchiveChangeKind.Replace, key, inputId: 7)], [], Ct),
            "stale-revision" => ArchiveStandaloneTransformPlan.Create(profile, source, [Input(payload)],
                [new(ArchiveChangeKind.Replace, new ArchiveMemberKey(Hash([1]), ArchiveStandalone.MemberName, ArchiveMemberType.File, 0), inputId: 7)], [], Ct),
            _ => ArchiveStandaloneTransformPlan.Create(new ArchiveWriteProfile(ArchiveWriteFormat.TarGzip), null, [Input(payload)], [], [Addition()], Ct),
        });
        Assert.Equal(shape == "tar-profile" ? ArchiveWriteReason.UnsupportedFeature : ArchiveWriteReason.InvalidRequest, error.Reason);
    }

    [Theory]
    [InlineData("name", ArchiveWriteReason.UnsupportedFeature)] [InlineData("mtime", ArchiveWriteReason.UnsupportedFeature)]
    [InlineData("xfl", ArchiveWriteReason.UnsupportedFeature)] [InlineData("concatenated", ArchiveWriteReason.InvalidArchive)]
    [InlineData("trailing", ArchiveWriteReason.InvalidArchive)] [InlineData("crc", ArchiveWriteReason.InvalidArchive)]
    [InlineData("isize", ArchiveWriteReason.InvalidArchive)]
    public async Task Gzip_files_with_stored_metadata_or_damage_stay_read_only(string change, ArchiveWriteReason reason)
    {
        byte[] b = Gzip("the payload"u8.ToArray());
        switch (change)
        {
            case "name": b = [.. b[..3], 8, .. b[4..10], .. "payload.txt\0"u8.ToArray(), .. b[10..]]; break; // FNAME, as the gzip tool writes.
            case "mtime": b[4] = 1; break;
            case "xfl": b[8] = 1; break;
            case "concatenated": b = [.. b, .. b]; break;
            case "trailing": b = [.. b, 0]; break;
            case "crc": b[^8] ^= 1; break;
            case "isize": b[^4] ^= 1; break;
        }
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => Assess(Profile(ArchiveWriteFormat.Gzip), b));
        Assert.Equal(reason, error.Reason);
    }

    [Fact]
    public async Task A_zstandard_frame_without_a_content_checksum_or_a_second_bzip2_stream_is_not_admitted()
    {
        byte[] noChecksum = [0x28, 0xB5, 0x2F, 0xFD, 0x20, 0x05, 0x29, 0x00, 0x00, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o'];
        await Assert.ThrowsAsync<ArchiveWriteException>(() => Assess(Profile(ArchiveWriteFormat.Zstd), noChecksum));
        byte[] stream = Bzip2("x"u8.ToArray(), 9);
        await Assert.ThrowsAsync<ArchiveWriteException>(() => Assess(Profile(ArchiveWriteFormat.Bzip2), [.. stream, .. stream]));
    }

    [Fact]
    public void Output_bounds_cover_each_encoder_and_refuse_beyond_the_physical_ceiling()
    {
        foreach (var format in new[] { ArchiveWriteFormat.Gzip, ArchiveWriteFormat.Bzip2, ArchiveWriteFormat.Zstd })
        {
            var profile = Profile(format);
            var tiny = ArchiveStandaloneTransformPlan.Create(profile, null, [Input([])], [], [Addition()], Ct);
            Assert.InRange(ArchiveTransformWire.Prepare(tiny, Ct).Request.OutputLengthBound, ArchiveTransformWire.MinimumOutputLength(format), 1L << 20);
            var huge = new ArchiveTransformInput(7, ArchiveWritePolicy.MaxPayloadBytes, Hash([]), new ArchiveTimestamp(0, 0), 0x1a4);
            long bound = ArchiveTransformWire.Prepare(ArchiveStandaloneTransformPlan.Create(profile, null, [huge], [], [Addition()], Ct), Ct).Request.OutputLengthBound;
            Assert.InRange(bound, ArchiveWritePolicy.MaxPayloadBytes, ArchiveWritePolicy.MaxPhysicalBytes);
        }
    }

    private static byte[] Gzip(byte[] payload, byte xfl = 0, byte os = 255)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true)) gzip.Write(payload);
        byte[] b = output.ToArray(); b[4] = b[5] = b[6] = b[7] = 0; b[8] = xfl; b[9] = os; return b;
    }

    private static byte[] Bzip2(byte[] payload, byte blockSize)
    {
        using var output = new MemoryStream();
        using (var bzip2 = new StrictBzip2WriteStream(output, blockSize, Ct)) bzip2.Write(payload);
        return output.ToArray();
    }
}
