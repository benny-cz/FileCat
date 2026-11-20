using System.Buffers.Binary;
using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveTimestampTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Theory]
    [InlineData("1700000000.123456789", 1700000000L, 123456789)]
    [InlineData("-0.000000001", -1L, 999999999)]
    [InlineData("-1.123456789", -2L, 876543211)]
    [InlineData("-62135596800", -62135596800L, 0)]
    [InlineData("253402300799.999999999", 253402300799L, 999999999)]
    public void Nanosecond_instants_are_exact_and_tick_conversion_never_silently_rounds(string text, long seconds, int nanos)
    {
        decimal value = decimal.Parse(text, CultureInfo.InvariantCulture); var time = ArchiveTimestamp.FromUnixSeconds(value);
        Assert.Equal(new(seconds, nanos), time); Assert.Equal(value, time.UnixSeconds);
        if (nanos % 100 == 0) Assert.Equal(time, (ArchiveTimestamp)time.ToDateTimeOffset());
        else Assert.Throws<ArchiveWriteException>(() => time.ToDateTimeOffset());
    }

    [Theory]
    [InlineData("-62135596800.000000001")]
    [InlineData("253402300800")]
    [InlineData("0.0000000001")]
    [InlineData("-0.0000000001")]
    public void Unsupported_ranges_and_subnanosecond_values_refuse(string text) =>
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, Assert.Throws<ArchiveWriteException>(() => ArchiveTimestamp.FromUnixSeconds(decimal.Parse(text, CultureInfo.InvariantCulture))).Reason);

    [Fact]
    public void Every_tick_boundary_roundtrips_including_negative_epoch_remainders_and_offsets()
    {
        foreach (var value in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, DateTimeOffset.UnixEpoch.AddTicks(-1), DateTimeOffset.UnixEpoch.AddTicks(1), new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.FromHours(7)) })
            Assert.Equal(value.UtcTicks, ((ArchiveTimestamp)value).ToDateTimeOffset().UtcTicks);
    }

    [Theory]
    [InlineData("1700000000.123456789")]
    [InlineData("-0.000000001")]
    [InlineData("-62135596799.999999999")]
    [InlineData("253402300799.999999999")]
    public async Task Exact_captured_time_crosses_alias_wire_transform_and_fresh_verifier(string text)
    {
        var time = ArchiveTimestamp.FromUnixSeconds(decimal.Parse(text, CultureInfo.InvariantCulture)); byte[] payload = "timestamp input"u8.ToArray();
        var plan = ArchiveTarTransformPlan.Create(null, [new(37, payload.Length, Hash(payload), time)], [], [new("one", ArchiveMemberType.File, 37), new("two", ArchiveMemberType.File, 37)], Ct);
        Assert.All(plan.Expected.Members, member => Assert.Equal(2, BinaryPrimitives.ReadUInt16LittleEndian(member.Metadata)));
        using var stage = new MemoryStream(); var invocation = ArchiveTransformWire.Prepare(plan, Ct);
        Child? transform = null, verifier = null;
        await new ArchiveTransformClient((nonce, _) => transform = Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => new MemoryStream(payload, false), Ct);
        byte[] bytes = stage.ToArray();
        var verified = await new ArchiveVerificationClient((nonce, _) => verifier = Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("owned-stage", bytes.Length, Hash(bytes)), plan.Expected, () => new MemoryStream(bytes, false), Ct);
        Assert.Equal(plan.Expected.Digest, verified.ManifestDigest); Assert.True(transform!.Reaped); Assert.True(verifier!.Reaped);
        using var raw = new MemoryStream(bytes); var catalog = StrictTarCatalog.Read(raw, Ct);
        Assert.All(catalog.Members, member => Assert.Equal(time.UnixSeconds, member.Modified));
        Assert.Contains("mtime=" + text + "\n", Encoding.UTF8.GetString(bytes));
        SaveOracle("create-" + time.Seconds.ToString(CultureInfo.InvariantCulture) + "-" + time.Nanoseconds.ToString(CultureInfo.InvariantCulture), bytes,
            new[] { new { Name = "one", Modified = text, Accessed = (string?)null, Changed = (string?)null, Sha256 = Hash(payload) }, new { Name = "two", Modified = text, Accessed = (string?)null, Changed = (string?)null, Sha256 = Hash(payload) } });
    }

    [Theory]
    [InlineData("1700000000.123456789", "-0.000000001", "253402300799.999999999")]
    [InlineData("-0.000000001", "0.000000001", "-62135596800")]
    public async Task Independently_framed_PAX_times_survive_replacement_rename_and_fresh_verification(string modified, string accessed, string changed)
    {
        byte[] original = WithTimes(modified, accessed, changed); string hash = Hash(original);
        var assessment = await new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce))
            .AssessAsync(new(new(ArchiveWriteFormat.Tar), original.Length, hash), () => new MemoryStream(original, false), Ct);
        byte[] replacement = "after"u8.ToArray(); var key = new ArchiveMemberKey(hash, "payload", ArchiveMemberType.File, 0);
        foreach (var edit in new[] { new ArchiveTransformEdit(ArchiveChangeKind.Replace, key, inputId: 1), new ArchiveTransformEdit(ArchiveChangeKind.Rename, key, "renamed") })
        {
            var inputs = edit.Kind == ArchiveChangeKind.Replace ? new[] { new ArchiveTransformInput(1, replacement.Length, Hash(replacement), DateTimeOffset.UnixEpoch) } : [];
            var plan = ArchiveTarTransformPlan.Create(assessment, inputs, [edit], [], Ct);
            using var before = new MemoryStream(original, false); using var stage = new MemoryStream();
            var manifest = TarRewriteEngine.Rebuild(before, hash, stage, [new(edit.Kind, key, edit.NewName, edit.InputId)], [],
                inputs.ToDictionary(i => i.Id, i => new TarInputDescriptor(i.Id, i.Length, i.Sha256, i.ModifiedTimestamp)), _ => new MemoryStream(replacement, false), Ct);
            TarRewriteEngine.Verify(stage, manifest, Ct); byte[] bytes = stage.ToArray();
            var verified = await new ArchiveVerificationClient((nonce, _) => Child.Start(nonce, "archive-verify"))
                .VerifyAsync(new("owned-stage", bytes.Length, Hash(bytes)), plan.Expected, () => new MemoryStream(bytes, false), Ct);
            Assert.Equal(plan.Expected.Digest, verified.ManifestDigest);
            string encoded = Encoding.UTF8.GetString(bytes);
            Assert.Contains("mtime=" + modified + "\n", encoded); Assert.Contains("atime=" + accessed + "\n", encoded); Assert.Contains("ctime=" + changed + "\n", encoded);
            SaveOracle("update-" + Hash(original)[..12] + "-" + edit.Kind, bytes,
                new[] { new { Name = edit.NewName ?? "payload", Modified = modified, Accessed = (string?)accessed, Changed = (string?)changed, Sha256 = Hash(edit.Kind == ArchiveChangeKind.Replace ? replacement : "before"u8.ToArray()) } });
        }
    }

    private static void SaveOracle(string name, byte[] bytes, object expected)
    {
        string? directory = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_TIME_ORACLE_DIRECTORY");
        if (directory is null) return;
        Assert.True(Path.IsPathFullyQualified(directory)); Assert.True(Directory.Exists(directory));
        File.WriteAllBytes(Path.Combine(directory, name + ".tar"), bytes);
        File.WriteAllText(Path.Combine(directory, name + ".json"), System.Text.Json.JsonSerializer.Serialize(expected));
    }

    // Separate fixture generator: use the runtime only for the ordinary member header/payload, then frame exact
    // PAX records independently. This deliberately bypasses the production adapter and DateTimeOffset conversion.
    internal static byte[] WithTimes(string modified, string accessed, string changed)
    {
        byte[] template = ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("payload", "before"));
        int oldSize = Convert.ToInt32(Encoding.ASCII.GetString(template, 124, 11).Trim('\0', ' '), 8);
        int next = 512 + (oldSize + 511) / 512 * 512;
        string records = Record("path", "payload") + Record("mtime", modified) + Record("atime", accessed) + Record("ctime", changed);
        byte[] body = Encoding.UTF8.GetBytes(records); byte[] result = new byte[512 + (body.Length + 511) / 512 * 512 + template.Length - next];
        template.AsSpan(0, 512).CopyTo(result); body.CopyTo(result, 512); template.AsSpan(next).CopyTo(result.AsSpan(512 + (body.Length + 511) / 512 * 512));
        ArchiveStrictTarTests.WriteOctal(result.AsSpan(124, 12), body.Length); ArchiveStrictTarTests.Checksum(result.AsSpan(0, 512)); return result;
        static string Record(string key, string value)
        {
            string tail = " " + key + "=" + value + "\n";
            for (int n = tail.Length + 1; ; n++) if (n == n.ToString(CultureInfo.InvariantCulture).Length + tail.Length) return n.ToString(CultureInfo.InvariantCulture) + tail;
        }
    }
}
