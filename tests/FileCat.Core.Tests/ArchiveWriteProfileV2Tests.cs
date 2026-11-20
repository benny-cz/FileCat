using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveWriteProfileV2Tests
{
    private static readonly Guid Nonce = new("23af5afc-4407-49e2-a838-428293110001");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));
    private static ArchiveWriteManifest Manifest(ArchiveWriteProfile profile) => new(profile,
        [new("é.bin", ArchiveMemberType.File, 0, 3, Sha([1, 2, 3]), [1, 2])], [1]);
    private static ArchiveTransactionFacts Facts(ArchiveWriteProfile profile, int sequence = 1,
        ArchiveTransactionPhase phase = ArchiveTransactionPhase.Reserved) => new(Nonce, sequence, profile, phase,
        Path.GetFullPath("profile-contract-archive"), "parent", null, Path.GetFullPath(".profile-contract-stage"), null, stageMaximumLength: 4096);
    private static byte[] Encode(ArchiveWriteProfile profile)
    { using var output = new MemoryStream(); using var writer = new BinaryWriter(output); ArchiveWriteProfileEncoding.Write(writer, profile); return output.ToArray(); }
    private static ArchiveWriteProfile[] AllProfiles() =>
        [.. from mode in Enum.GetValues<ArchiveEncryptionMode>() from codec in Enum.GetValues<ArchiveSevenZipCodec>()
            from header in Enum.GetValues<ArchiveSevenZipHeaderCompression>() select ArchiveWriteProfile.ForSevenZip(new(codec, headerCompression: header), encryption: Encryption(mode)),
         .. from mode in Enum.GetValues<ArchiveEncryptionMode>() from method in Enum.GetValues<ArchiveRar5Compression>()
            select ArchiveWriteProfile.ForRar5(method, Encryption(mode))];
    private static ArchiveEncryptionOptions Encryption(ArchiveEncryptionMode mode) => mode == ArchiveEncryptionMode.None ? ArchiveEncryptionOptions.None : new(mode);
    public static IEnumerable<object[]> Profiles() => Enumerable.Range(0, 60).Select(index => new object[] { index });

    [Theory]
    [MemberData(nameof(Profiles))]
    public void Every_current_codec_header_method_and_password_mode_round_trips_all_public_contracts(int index)
    {
        var profile = AllProfiles()[index]; byte[] encoding = Encode(profile); Assert.Equal(52, encoding.Length);
        Assert.Equal(profile, ArchiveWriteProfileEncoding.Decode(encoding));
        Assert.Equal(profile, ArchiveWriteProfileEncoding.FromJson(ArchiveWriteProfileEncoding.ToJson(profile)));
        Assert.Equal(profile, JsonSerializer.Deserialize<ArchiveWriteProfile>(JsonSerializer.Serialize(profile)));
        var request = new ArchiveAssessmentRequest(profile, 1, Sha([]));
        byte[] requestBytes = ArchiveAssessmentWire.Request(request); Assert.Equal(94, requestBytes.Length);
        Assert.Equal(request, ArchiveAssessmentWire.ReadRequest(requestBytes));
        var manifest = Manifest(profile); var assessment = new ArchiveAssessment(manifest, Sha([4]), 1, Sha([]));
        var envelope = ArchiveAssessmentWire.ReadCatalog(ArchiveAssessmentWire.Catalog(assessment), request, new());
        ArchiveAssessmentWire.VerifyComplete(envelope, manifest);
        using var sidecar = new MemoryStream(); var info = ArchiveManifestSidecar.Write(sidecar, Nonce, ArchiveManifestPurpose.ExpectedOutput, manifest, Ct);
        sidecar.Position = 0;
        using (var reader = ArchiveManifestSidecar.OpenRead(sidecar, info, Nonce, ArchiveManifestPurpose.ExpectedOutput, profile, Ct))
        { Assert.False(reader.IsComplete); Assert.NotNull(reader.ReadNext()); Assert.Null(reader.ReadNext()); Assert.True(reader.IsComplete); Assert.Equal(manifest.Digest, reader.VerifiedManifestDigest); }
        var scope = new ArchiveOriginEvidenceScope(Nonce, profile, ArchiveOriginOperation.Create, 0);
        using var origin = new MemoryStream(); ArchiveOriginEvidenceInfo originInfo;
        using (var writer = new ArchiveOriginEvidenceWriter(origin, scope, Ct)) originInfo = writer.Complete();
        Assert.Equal(88, originInfo.Length); Assert.Equal(originInfo.Length, ArchiveOriginEvidenceWriter.EncodedLength(scope, [], Ct));
        origin.Position = 0; using (var reader = new ArchiveOriginEvidenceReader(origin, originInfo, scope, Ct))
        { Assert.False(reader.IsComplete); Assert.Null(reader.ReadNext()); Assert.True(reader.IsComplete); }
        var facts = Facts(profile); Assert.Equal(profile, ArchiveTransactionFacts.Parse(facts.ToJson()).Profile);
        var creation = Creation(profile); using var creationBytes = new MemoryStream(); creation.Write(creationBytes, Ct);
        Assert.Equal(encoding, creationBytes.ToArray().AsSpan(28, encoding.Length).ToArray());
        creationBytes.Position = 0; creation.Readback(creationBytes, Ct);
        if (profile.Format == ArchiveWriteFormat.Rar5) Assert.Equal(profile.CompressionLevel, (int)ArchiveWriterProfileAdapter.Rar5(profile));
        else
        {
            var options = ArchiveWriterProfileAdapter.SevenZip(profile); Assert.Equal((int)profile.SevenZip!.Codec, (int)options.Codec);
            Assert.Equal(profile.DictionaryBytes, options.DictionaryBytes); Assert.Equal((int)profile.SevenZip.HeaderCompression, (int)ArchiveWriterProfileAdapter.SevenZipHeader(profile));
        }
        Export(index, profile, encoding, requestBytes, sidecar.ToArray(), origin.ToArray(), manifest);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Independent_fixed_vectors_define_the_complete_v2_option_order(int vector)
    {
        var profile = vector switch
        {
            0 => ArchiveWriteProfile.ForRar5(), 1 => ArchiveWriteProfile.ForSevenZip(),
            _ => ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Ppmd7, ArchiveSevenZipFilter.Delta, 12, 4 << 20, 256, ArchiveSevenZipHeaderCompression.Raw),
                3, encryption: new(ArchiveEncryptionMode.DataAndNames, 20)),
        };
        int[] expected = vector switch
        {
            0 => [9, 2, 3, 131072, 0, 0, 0, 0, 0, 0, 0, 0, 0],
            1 => [5, 2, 0, 8388608, 2, 0, 0, 0, 1, 1, 0, 0, 0],
            _ => [5, 2, 3, 8388608, 6, 1, 12, 4194304, 256, 0, 2, 20, 0],
        };
        byte[] golden = new byte[52]; for (int i = 0; i < expected.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(golden.AsSpan(i * 4), expected[i]);
        Assert.Equal(golden, Encode(profile)); Assert.Equal(profile, ArchiveWriteProfileEncoding.Decode(golden));
        if (profile.SevenZip is { } seven)
        {
            var mapped = ArchiveWriterProfileAdapter.SevenZip(profile); Assert.Equal(seven.ModelOrder, mapped.ModelOrder);
            Assert.Equal(seven.ModelMemoryBytes, mapped.ModelMemoryBytes); Assert.Equal(seven.DeltaDistance, mapped.DeltaDistance);
            Assert.Equal((int)seven.Filter, (int)mapped.Filter);
        }
    }

    public static IEnumerable<object[]> ChangedFields() => Enumerable.Range(0, 12).Select(index => new object[] { index });
    private static (ArchiveWriteProfile Before, ArchiveWriteProfile After) Changed(int index)
    {
        var initial = ArchiveWriteProfile.ForSevenZip();
        return index switch
        {
            0 => (initial, ArchiveWriteProfile.ForRar5()),
            1 => (new(ArchiveWriteFormat.SevenZip), initial),
            2 => (initial, ArchiveWriteProfile.ForSevenZip(compressionLevel: 1)),
            3 => (initial, ArchiveWriteProfile.ForSevenZip(dictionaryBytes: 4 << 20)),
            4 => (initial, ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Lzma))),
            5 => (initial, ArchiveWriteProfile.ForSevenZip(new(filter: ArchiveSevenZipFilter.X86))),
            6 => (ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Ppmd7, modelOrder: 6, modelMemoryBytes: 8 << 20)),
                ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Ppmd7, modelOrder: 7, modelMemoryBytes: 8 << 20))),
            7 => (ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Ppmd7, modelOrder: 6, modelMemoryBytes: 8 << 20)),
                ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Ppmd7, modelOrder: 6, modelMemoryBytes: 16 << 20))),
            8 => (ArchiveWriteProfile.ForSevenZip(new(filter: ArchiveSevenZipFilter.Delta)),
                ArchiveWriteProfile.ForSevenZip(new(filter: ArchiveSevenZipFilter.Delta, deltaDistance: 2))),
            9 => (initial, ArchiveWriteProfile.ForSevenZip(new(headerCompression: ArchiveSevenZipHeaderCompression.Raw))),
            10 => (ArchiveWriteProfile.ForSevenZip(encryption: new(ArchiveEncryptionMode.Data)),
                ArchiveWriteProfile.ForSevenZip(encryption: new(ArchiveEncryptionMode.DataAndNames))),
            _ => (ArchiveWriteProfile.ForSevenZip(encryption: new(ArchiveEncryptionMode.Data, 19)),
                ArchiveWriteProfile.ForSevenZip(encryption: new(ArchiveEncryptionMode.Data, 20))),
        };
    }
    [Theory]
    [MemberData(nameof(ChangedFields))]
    public void Every_meaningful_option_binds_digests_sidecars_journal_scope_and_fresh_worker_secret(int index)
    {
        var (before, after) = Changed(index); var a = Manifest(before); var b = Manifest(after);
        Assert.NotEqual(a.Digest, b.Digest); Assert.NotEqual(a.ContentDigest, b.ContentDigest); Assert.NotEqual(a.MetadataDigest, b.MetadataDigest);
        string requestA = Sha(ArchiveAssessmentWire.Request(new(before, 1, Sha([])))), requestB = Sha(ArchiveAssessmentWire.Request(new(after, 1, Sha([]))));
        Assert.NotEqual(requestA, requestB);
        using var secretWire = new MemoryStream(); using var grant = new ArchiveWorkerSecretGrant("Public profile binding vector");
        ArchiveWorkerSecretWire.WriteGrant(new(Nonce, 1 << 20, 2), secretWire, ArchiveWorkerRole.Verify, requestA, grant, Ct);
        secretWire.Position = 0;
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveWorkerSecretWire.ReadGrant(new(Nonce, 1 << 20, 2), secretWire, ArchiveWorkerRole.Verify, requestB, Ct)).Reason);
        using var sidecar = new MemoryStream(); var info = ArchiveManifestSidecar.Write(sidecar, Nonce, ArchiveManifestPurpose.ExpectedOutput, a, Ct); sidecar.Position = 0;
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveManifestSidecar.OpenRead(sidecar, info, Nonce, ArchiveManifestPurpose.ExpectedOutput, after, Ct)).Reason);
        using var origin = new MemoryStream(); ArchiveOriginEvidenceInfo originInfo;
        using (var writer = new ArchiveOriginEvidenceWriter(origin, new(Nonce, before, ArchiveOriginOperation.Create, 0), Ct)) originInfo = writer.Complete();
        origin.Position = 0; Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
            new ArchiveOriginEvidenceReader(origin, originInfo, new(Nonce, after, ArchiveOriginOperation.Create, 0), Ct)).Reason);
        Assert.Throws<ArgumentException>(() => Facts(after, 2, ArchiveTransactionPhase.NeedsReview).ValidateAfter(Facts(before)));
        var creation = Creation(before); Assert.Throws<ArgumentException>(() => creation.RequireScope(CreationFacts(after)));
    }

    [Fact]
    public void Legacy_profile_bytes_and_json_keys_stay_unchanged_and_cannot_accept_omitted_new_options()
    {
        for (int format = 0; format <= 8; format++)
        {
            var profile = new ArchiveWriteProfile((ArchiveWriteFormat)format); byte[] expected = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(expected, format); BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(4), 1);
            BinaryPrimitives.WriteInt32LittleEndian(expected.AsSpan(12), 8 << 20); Assert.Equal(expected, Encode(profile));
            Assert.Equal(4, ArchiveWriteProfileEncoding.ToJson(profile).Count);
        }
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.Rar5));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.SevenZip, sevenZip: new()));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.Tar, encryption: new(ArchiveEncryptionMode.Data)));
    }

    [Fact]
    public void Every_truncated_extension_unknown_version_reserved_field_and_json_omission_refuses()
    {
        byte[] valid = Encode(ArchiveWriteProfile.ForSevenZip());
        for (int length = 0; length < valid.Length; length++) Assert.Throws<ArchiveWriteException>(() => ArchiveWriteProfileEncoding.Decode(valid.AsSpan(0, length)));
        Assert.Throws<ArchiveWriteException>(() => ArchiveWriteProfileEncoding.Decode([.. valid, 0]));
        foreach (int offset in new[] { 0, 4, 16, 20, 36, 40, 44, 48 })
        { byte[] invalid = (byte[])valid.Clone(); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(offset), int.MaxValue); Assert.Throws<ArchiveWriteException>(() => ArchiveWriteProfileEncoding.Decode(invalid)); }
        for (int index = 4; index <= 9; index++)
        { byte[] invalid = Encode(ArchiveWriteProfile.ForRar5()); BinaryPrimitives.WriteInt32LittleEndian(invalid.AsSpan(index * 4), 1); Assert.Throws<ArchiveWriteException>(() => ArchiveWriteProfileEncoding.Decode(invalid)); }
        var profile = ArchiveWriteProfile.ForSevenZip(); var json = ArchiveWriteProfileEncoding.ToJson(profile);
        foreach (string key in json.Select(pair => pair.Key).ToArray())
        {
            var missing = json.DeepClone().AsObject(); missing.Remove(key);
            var error = Record.Exception(() => ArchiveWriteProfileEncoding.FromJson(missing));
            Assert.True(error is ArgumentException or ArchiveWriteException, error?.ToString() ?? "Missing option was accepted.");
        }
        var extra = json.DeepClone().AsObject(); extra["password"] = "forbidden-unknown-field";
        Assert.Throws<ArgumentException>(() => ArchiveWriteProfileEncoding.FromJson(extra));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Password_consumers_never_discard_an_unused_grant_and_clear_it_on_validation_or_cancellation(bool rar)
    {
        var plain = rar ? ArchiveWriteProfile.ForRar5() : ArchiveWriteProfile.ForSevenZip();
        using var unused = new ArchiveWorkerSecretGrant("Public unused grant");
        Assert.Throws<ArchiveWriteException>(() => Consume(plain, unused, Ct)); Assert.Throws<ObjectDisposedException>(() => unused.UseOnce(value => value.Length));
        var encrypted = rar ? ArchiveWriteProfile.ForRar5(encryption: new(ArchiveEncryptionMode.Data)) : ArchiveWriteProfile.ForSevenZip(encryption: new(ArchiveEncryptionMode.Data));
        Assert.Throws<ArchiveWriteException>(() => Consume(encrypted, null, Ct));
        using var canceled = new ArchiveWorkerSecretGrant("Public canceled grant"); using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => Consume(encrypted, canceled, stop.Token)); Assert.Throws<ObjectDisposedException>(() => canceled.UseOnce(value => value.Length));
        void Consume(ArchiveWriteProfile profile, ArchiveWorkerSecretGrant? grant, CancellationToken ct)
        { using IDisposable? value = rar ? ArchiveWriterProfileAdapter.Rar5Encryption(profile, grant, new(), ct) : ArchiveWriterProfileAdapter.SevenZipEncryption(profile, grant, new(), ct); }
    }

    [Fact]
    public void Unsupported_or_ineffective_options_are_controlled_refusals_without_enabling_a_backend()
    {
        Assert.Throws<ArchiveWriteException>(() => ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Copy), 1));
        Assert.Throws<ArchiveWriteException>(() => ArchiveWriteProfile.ForSevenZip(new(ArchiveSevenZipCodec.Ppmd7), dictionaryBytes: 4 << 20));
        Assert.Throws<ArchiveWriteException>(() => ArchiveWriteProfile.ForSevenZip(dictionaryBytes: 3 << 20));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSevenZipOptions(modelOrder: 6, modelMemoryBytes: 8 << 20));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSevenZipOptions(ArchiveSevenZipCodec.Ppmd7, modelOrder: 6));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveSevenZipOptions(deltaDistance: 2));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveEncryptionOptions(ArchiveEncryptionMode.Data, 18));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveEncryptionOptions(ArchiveEncryptionMode.None, 19));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveRar5Options(1));
        Assert.Throws<ArchiveWriteException>(() => new ArchiveWriteProfile(ArchiveWriteFormat.Rar5, 2, 3, 8 << 20, rar5: new()));
        Assert.Throws<ArchiveWriteException>(() => ArchiveTarTransformPlan.Create(ArchiveWriteProfile.ForRar5(), null, [], [], [], Ct));
        Assert.Throws<ArchiveWriteException>(() => ArchiveTarTransformPlan.Create(ArchiveWriteProfile.ForSevenZip(), null, [], [], [], Ct));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Request_downgrade_truncation_and_catalog_option_substitution_never_reuse_a_profile(bool rar)
    {
        var profile = rar ? ArchiveWriteProfile.ForRar5() : ArchiveWriteProfile.ForSevenZip();
        var request = new ArchiveAssessmentRequest(profile, 1, Sha([])); byte[] bytes = ArchiveAssessmentWire.Request(request);
        for (int length = 0; length < bytes.Length; length++)
            Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => ArchiveAssessmentWire.ReadRequest(bytes.AsSpan(0, length))).Reason);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(6), 1);
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => ArchiveAssessmentWire.ReadRequest(bytes)).Reason);
        var manifest = Manifest(profile); byte[] catalog = ArchiveAssessmentWire.Catalog(new(manifest, Sha([4]), 1, Sha([])));
        BinaryPrimitives.WriteInt32LittleEndian(catalog.AsSpan(10), 1); // another valid compression level/method
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, Assert.Throws<ArchiveWriteException>(() => ArchiveAssessmentWire.ReadCatalog(catalog, request, new())).Reason);
    }

    private static ArchiveTransactionFacts CreationFacts(ArchiveWriteProfile profile) => new(Nonce, 1, profile,
        ArchiveTransactionPhase.Verifying, Path.GetFullPath("profile-contract-archive"), "parent", null,
        Path.GetFullPath(".profile-contract-stage"), "stage", new("stage", 0, Sha([])));
    private sealed class Review(ArchiveFileObservation stage) : ArchiveCreatePublicationReview(new object(), stage,
        "profile-contract-archive", ArchiveCreationBackend.LinuxExt4, [1, 2, 3]);
    private static ArchiveCreationMetadata Creation(ArchiveWriteProfile profile)
    {
        var stage = new ArchiveFileObservation(new("stage", 0, Sha([])), "parent", ".profile-contract-stage", new string('A', 64),
            ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.LinuxOriginUrl), new(0, 0), 0x180);
        return new(CreationFacts(profile), new Review(stage));
    }

    private static void Export(int index, ArchiveWriteProfile profile, byte[] encoding, byte[] request, byte[] manifestSidecar, byte[] origin, ArchiveWriteManifest manifest)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_PROFILE_V2_EXPORT_ROOT") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, index.ToString("D2")); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "profile.bin"), encoding); File.WriteAllBytes(Path.Combine(directory, "request.bin"), request);
        File.WriteAllBytes(Path.Combine(directory, "manifest.bin"), manifestSidecar); File.WriteAllBytes(Path.Combine(directory, "origin.bin"), origin);
        File.WriteAllText(Path.Combine(directory, "profile.json"), ArchiveWriteProfileEncoding.ToJson(profile).ToJsonString());
        File.WriteAllText(Path.Combine(directory, "digests.json"), JsonSerializer.Serialize(new { manifest.Digest, manifest.ContentDigest, manifest.MetadataDigest }));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(root)!, "runtime-profile.json"), JsonSerializer.Serialize(new
        {
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
        }));
    }
}
