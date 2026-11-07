using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Archives.Rar;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class Rar5WorkerPipelineTests
{
    private const string Password = "Public-RAR-Transform-暗号-42829316";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(ReadOnlySpan<byte> data) => Convert.ToHexString(SHA256.HashData(data));
    private static ArchiveWriteProfile Profile(int mode, int method = 3) => ArchiveWriteProfile.ForRar5((ArchiveRar5Compression)method,
        mode == 0 ? null : new((ArchiveEncryptionMode)mode));
    private static ArchiveWorkerSecretGrant? Secret(int mode) => mode == 0 ? null : new(Password);
    private static ArchiveTransformInput Input(int id, byte[] bytes) => new(id, bytes.Length, Sha(bytes), new ArchiveTimestamp(1700000000, 123456789));
    private static byte[] Data(int seed, int length)
    { byte[] bytes = new byte[length]; new Random(seed).NextBytes(bytes); for (int i = length / 2; i < length; i++) bytes[i] = (byte)(i % 13); return bytes; }
    public static IEnumerable<object[]> Modes() => from method in Enumerable.Range(0, 6) from mode in Enumerable.Range(0, 3) select new object[] { method, mode };

    [Theory] [MemberData(nameof(Modes))]
    public async Task Complete_RAR_jobs_cross_real_role_bound_processes_and_fresh_verification(int method, int mode)
    {
        var profile = Profile(mode, method); byte[] payload = Data(42829317, 70001), replacement = Data(42829318, 131089);
        var children = new List<Child>(); IArchiveWorkerProcess Launch(Guid nonce, string role) { var child = Child.Start(nonce, role); children.Add(child); return child; }
        var first = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, null, [Input(37, payload)], [],
            [new("keep.bin", ArchiveMemberType.File, 37), new("replace.bin", ArchiveMemberType.File, 37), new("delete.bin", ArchiveMemberType.File, 37), new("folder/", ArchiveMemberType.Directory)], Ct), Ct);
        Assert.Equal(3, first.CapturedInputIds.Count); Assert.Equal(3, first.Request.Inputs.Count);
        using var source = new MemoryStream(); int opens = 0;
        var created = await new ArchiveTransformClient((nonce, _) => Launch(nonce, "archive-transform-v2"), protocolVersion: 2)
            .RebuildAsync(first, source, () => throw new InvalidOperationException(), id => { Assert.Equal(37, id); opens++; return new MemoryStream(payload, false); }, Ct, Secret(mode));
        Assert.Equal(3, opens); Assert.InRange(created.Length, 24, first.Request.OutputLengthBound);
        byte[] original = source.ToArray();
        var assessment = await new ArchiveAssessmentClient((nonce, _) => Launch(nonce, "archive-assess-v2"), protocolVersion: 2)
            .AssessAsync(new(profile, original.Length, Sha(original)), () => new MemoryStream(original, false), Ct, Secret(mode));
        Assert.Equal(first.Expected.Digest, assessment.Manifest.Digest);
        ArchiveMemberKey Key(int i) { var member = assessment.Manifest.Members[i]; return new(assessment.CatalogRevision, member.Name, member.Type, member.Occurrence); }
        var update = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, assessment, [Input(42, replacement)],
            [new(ArchiveChangeKind.Rename, Key(0), "renamed-界.bin"), new(ArchiveChangeKind.Replace, Key(1), inputId: 42), new(ArchiveChangeKind.Delete, Key(2))],
            [new("new-folder/", ArchiveMemberType.Directory), new("new-界.bin", ArchiveMemberType.File, 42)], Ct), Ct);
        Assert.Equal(2, update.CapturedInputIds.Count); using var stage = new MemoryStream(); int sourceOpens = 0; opens = 0;
        var receipt = await new ArchiveTransformClient((nonce, _) => Launch(nonce, "archive-transform-v2"), protocolVersion: 2)
            .RebuildAsync(update, stage, () => { sourceOpens++; return new MemoryStream(original, false); },
                id => { Assert.Equal(42, id); opens++; return new MemoryStream(replacement, false); }, Ct, Secret(mode));
        Assert.Equal(2, sourceOpens); Assert.Equal(2, opens); Assert.InRange(receipt.Length, 24, update.Request.OutputLengthBound);
        byte[] rebuilt = stage.ToArray();
        var verified = await new ArchiveVerificationClient((nonce, _) => Launch(nonce, "archive-verify-v2"), protocolVersion: 2)
            .VerifyAsync(new("sealed-parent-stage", rebuilt.Length, Sha(rebuilt)), update.Expected, () => new MemoryStream(rebuilt, false), Ct, Secret(mode));
        Assert.Equal(update.Expected.Digest, verified.ManifestDigest); Assert.Equal(4, children.Count);
        foreach (var child in children) { Assert.True(child.Reaped); Assert.True(child.Disposed); Assert.Equal(0u, child.ExitCode); }
        Export($"method-{method}-mode-{mode}", original, rebuilt, assessment.Manifest, verified.ObservedManifest);
    }

    public static IEnumerable<object[]> RoleDamage() => from role in Enumerable.Range(1, 3) from binding in new[] { false, true } select new object[] { role, binding };
    private static string RoleName(int role) => role switch { 1 => "archive-assess-v2", 2 => "archive-transform-v2", _ => "archive-verify-v2" };
    private static string Request(ArchiveWorkerProtocol send, Stream input, int role)
    {
        var records = new ArchiveWorkerRecords(send, input, writing: true); var profile = Profile(1);
        if (role == 2)
        {
            var request = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, null, [Input(1, [1])], [], [new("f", ArchiveMemberType.File, 1)], Ct), Ct).Request;
            ArchiveTransformWire.WriteRequest(records, request, Ct); return ArchiveTransformWire.RequestDigest(request, Ct);
        }
        var assessment = new ArchiveAssessmentRequest(profile, 24, Sha(new byte[24]));
        records.Write(ArchiveControlRecord.AssessmentRequest, ArchiveAssessmentWire.Request(assessment)); return ArchiveAssessmentWire.RequestDigest(assessment);
    }
    private static async Task Raw(int role, Action<Guid, Child> action)
    {
        Child? child = null;
        await ArchiveWorkerSession.RunAsync((nonce, _) => child = Child.Start(nonce, RoleName(role)), new ArchiveWorkerQueue(),
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1),
            (nonce, process, progress) => { progress(); action(nonce, (Child)process); progress(); return 0; }, Ct).WaitAsync(TimeSpan.FromMinutes(2), Ct);
        Assert.True(child!.Disposed); Assert.True(child.Reaped); Assert.Equal(0u, child.ExitCode);
    }
    [Theory] [MemberData(nameof(RoleDamage))]
    public Task Real_endpoints_refuse_wrong_role_or_request_bindings_before_any_proxy_IO(int role, bool binding)
        => Raw(role, (nonce, child) =>
        {
            var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, 2); var send = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
            Assert.Equal(ArchiveWorkerMessage.Hello, receive.Read(child.Output).Kind); string digest = Request(send, child.Input, role);
            using var grant = new ArchiveWorkerSecretGrant(Password);
            ArchiveWorkerSecretWire.WriteGrant(send, child.Input, binding ? (ArchiveWorkerRole)role : role == 1 ? ArchiveWorkerRole.Verify : ArchiveWorkerRole.Assess,
                binding ? Sha([99]) : digest, grant, Ct);
            var result = receive.Read(child.Output); Assert.Equal(ArchiveWorkerMessage.Refused, result.Kind);
            Assert.Equal((int)ArchiveWriteReason.ProtocolViolation, BinaryPrimitives.ReadInt32LittleEndian(result.Payload)); Assert.Equal(-1, child.Output.ReadByte());
        });

    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public Task A_partly_received_runtime_secret_cancels_without_proxy_IO_or_receipt(int role)
        => Raw(role, (nonce, child) =>
        {
            var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, 2); var send = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
            Assert.Equal(ArchiveWorkerMessage.Hello, receive.Read(child.Output).Kind); string digest = Request(send, child.Input, role);
            using var packet = new MemoryStream(); using var grant = new ArchiveWorkerSecretGrant(Password);
            ArchiveWorkerSecretWire.WriteGrant(send, packet, (ArchiveWorkerRole)role, digest, grant, Ct);
            byte[] partial = packet.ToArray(); child.Input.Write(partial, 0, ArchiveWorkerProtocol.HeaderBytes + ArchiveWorkerSecretWire.PrefixBytes + 6);
            child.Input.Flush(); child.RequestCancellation(); Assert.Equal(-1, child.Output.ReadByte());
        });

    public static IEnumerable<object[]> GrantDamage() => from role in Enumerable.Range(1, 3) from extra in new[] { false, true } select new object[] { role, extra };
    [Theory] [MemberData(nameof(GrantDamage))]
    public async Task Missing_or_unexpected_passwords_refuse_before_launch_and_clear_supplied_grants(int role, bool extra)
    {
        int mode = extra ? 0 : 1; var profile = Profile(mode); using var grant = extra ? new ArchiveWorkerSecretGrant(Password) : null;
        using var stage = new MemoryStream(); int launches = 0; IArchiveWorkerProcess Launch(Guid _, TimeSpan __) { launches++; throw new InvalidOperationException(); }
        var invocation = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, null, [Input(1, [1])], [], [new("f", ArchiveMemberType.File, 1)], Ct), Ct);
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(async () =>
        {
            if (role == 1) await new ArchiveAssessmentClient(Launch, protocolVersion: 2).AssessAsync(new(profile, 24, Sha(new byte[24])), () => throw new InvalidOperationException(), Ct, grant);
            else if (role == 2) await new ArchiveTransformClient(Launch, protocolVersion: 2).RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), Ct, grant);
            else await new ArchiveVerificationClient(Launch, protocolVersion: 2).VerifyAsync(new("stage", 24, Sha(new byte[24])), invocation.Expected, () => throw new InvalidOperationException(), Ct, grant);
        });
        Assert.Equal(ArchiveWriteReason.InvalidRequest, error.Reason); Assert.Equal(0, launches); Assert.Equal(0, stage.Length);
        if (grant is not null) Assert.Throws<ObjectDisposedException>(() => grant.UseOnce(value => value.Length));
    }

    [Theory] [MemberData(nameof(GrantDamage))]
    public async Task Encrypted_jobs_cannot_downgrade_to_a_protocol_one_endpoint(int role, bool hidden)
    {
        int mode = hidden ? 2 : 1; var profile = Profile(mode); using var grant = new ArchiveWorkerSecretGrant(Password);
        using var stage = new MemoryStream(); int launches = 0; IArchiveWorkerProcess Launch(Guid _, TimeSpan __) { launches++; throw new InvalidOperationException(); }
        var invocation = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, null, [Input(1, [1])], [], [new("f", ArchiveMemberType.File, 1)], Ct), Ct);
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(async () =>
        {
            if (role == 1) await new ArchiveAssessmentClient(Launch).AssessAsync(new(profile, 24, Sha(new byte[24])), () => throw new InvalidOperationException(), Ct, grant);
            else if (role == 2) await new ArchiveTransformClient(Launch).RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), Ct, grant);
            else await new ArchiveVerificationClient(Launch).VerifyAsync(new("stage", 24, Sha(new byte[24])), invocation.Expected, () => throw new InvalidOperationException(), Ct, grant);
        });
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, error.Reason); Assert.Equal(0, launches); Assert.Equal(0, stage.Length);
        Assert.Throws<ObjectDisposedException>(() => grant.UseOnce(value => value.Length));
    }

    [Theory] [MemberData(nameof(GrantDamage))]
    public async Task Wrong_passwords_refuse_real_jobs_and_never_publish_a_transform_receipt(int role, bool hidden)
    {
        int mode = hidden ? 2 : 1; var profile = Profile(mode); byte[] source = Fixture(mode); var readRequest = new ArchiveAssessmentRequest(profile, source.Length, Sha(source));
        using var read = new Rar5ReadPasswordSession(Password, new(), Ct); using var memory = new MemoryStream(source, false);
        var assessment = Rar5SemanticCatalog.Assessment(StrictRar5Catalog.Read(memory, Ct, read), profile, Ct);
        var invocation = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, assessment, [], [], [], Ct), Ct);
        Child? child = null; using var stage = new MemoryStream(); using var grant = new ArchiveWorkerSecretGrant("Wrong public synthetic password");
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(async () =>
        {
            if (role == 1) await new ArchiveAssessmentClient((nonce, _) => child = Child.Start(nonce, RoleName(role)), protocolVersion: 2).AssessAsync(readRequest, () => new MemoryStream(source, false), Ct, grant);
            else if (role == 2) await new ArchiveTransformClient((nonce, _) => child = Child.Start(nonce, RoleName(role)), protocolVersion: 2).RebuildAsync(invocation, stage, () => new MemoryStream(source, false), _ => throw new InvalidOperationException(), Ct, grant);
            else await new ArchiveVerificationClient((nonce, _) => child = Child.Start(nonce, RoleName(role)), protocolVersion: 2).VerifyAsync(new("sealed", source.Length, Sha(source)), assessment.Manifest, () => new MemoryStream(source, false), Ct, grant);
        });
        Assert.Equal(ArchiveWriteReason.InvalidArchive, error.Reason); Assert.Equal(0, stage.Length); Assert.True(child!.Reaped); Assert.True(child.Disposed);
        Assert.Throws<ObjectDisposedException>(() => grant.UseOnce(value => value.Length));
    }

    [Theory] [InlineData(1)] [InlineData(2)]
    public async Task A_declared_password_mode_cannot_verify_an_archive_with_unencrypted_contents(int mode)
    {
        byte[] original = Fixture(0); var profile = Profile(mode); Child? child = null;
        var error = await Assert.ThrowsAsync<ArchiveWriteException>(() => new ArchiveAssessmentClient((nonce, _) => child = Child.Start(nonce, "archive-assess-v2"), protocolVersion: 2)
            .AssessAsync(new(profile, original.Length, Sha(original)), () => new MemoryStream(original, false), Ct, Secret(mode)));
        Assert.Equal(ArchiveWriteReason.UnsupportedFeature, error.Reason); Assert.True(child!.Reaped);
    }

    [Fact]
    public async Task Unencrypted_RAR_remains_usable_on_the_explicit_protocol_one_endpoints()
    {
        var profile = Profile(0); byte[] bytes = [1, 2, 3];
        var invocation = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, null, [Input(5, bytes)], [], [new("f", ArchiveMemberType.File, 5)], Ct), Ct);
        using var stage = new MemoryStream(); await new ArchiveTransformClient((nonce, _) => Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => new MemoryStream(bytes, false), Ct);
        byte[] output = stage.ToArray(); var verified = await new ArchiveVerificationClient((nonce, _) => Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("sealed", output.Length, Sha(output)), invocation.Expected, () => new MemoryStream(output, false), Ct);
        Assert.Equal(invocation.Expected.Digest, verified.ManifestDigest);
    }

    [Fact]
    public void Canonical_request_bindings_survive_wire_roundtrip_and_change_with_public_fields()
    {
        var plan = ArchiveRar5TransformPlan.Create(Profile(1), null, [Input(1, [1])], [], [new("f", ArchiveMemberType.File, 1)], Ct);
        var request = ArchiveTransformWire.Prepare(plan, Ct).Request; string digest = ArchiveTransformWire.RequestDigest(request, Ct);
        Guid nonce = Guid.NewGuid(); using var packet = new MemoryStream();
        ArchiveTransformWire.WriteRequest(new(new(nonce, 1 << 20, 2), packet, writing: true), request, Ct); packet.Position = 0;
        var read = ArchiveTransformWire.ReadRequest(new(new(nonce, 1 << 20, 2), packet, writing: false), Ct);
        Assert.Equal(digest, ArchiveTransformWire.RequestDigest(read, Ct));
        var changed = new ArchiveTransformRequest(request.Source, request.Inputs.ToDictionary(), request.Edits.ToArray(), request.Additions.ToArray(), request.OutputLengthBound + 1, request.Profile);
        Assert.NotEqual(digest, ArchiveTransformWire.RequestDigest(changed, Ct));
        var renamed = new ArchiveTransformRequest(request.Source, request.Inputs.ToDictionary(), [], [new("g", ArchiveMemberType.File, 1)], request.OutputLengthBound, request.Profile);
        Assert.NotEqual(digest, ArchiveTransformWire.RequestDigest(renamed, Ct));
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Incompressible_inputs_fit_the_audited_parent_ceiling_through_two_real_encoding_passes(int method)
    {
        byte[] bytes = new byte[131089]; new Random(42829320).NextBytes(bytes); var profile = Profile(0, method);
        var invocation = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, null, [Input(1, bytes)], [], [new("noise", ArchiveMemberType.File, 1)], Ct), Ct);
        Assert.InRange(invocation.Request.OutputLengthBound, bytes.Length, 2 * bytes.Length);
        using var stage = new MemoryStream(); await new ArchiveTransformClient((nonce, _) => Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => new MemoryStream(bytes, false), Ct);
        Assert.InRange(stage.Length, bytes.Length, invocation.Request.OutputLengthBound); byte[] output = stage.ToArray();
        var verified = await new ArchiveVerificationClient((nonce, _) => Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("sealed", output.Length, Sha(output)), invocation.Expected, () => new MemoryStream(output, false), Ct);
        Assert.Equal(invocation.Expected.Digest, verified.ManifestDigest);
    }

    [Fact]
    public async Task A_maximum_Unicode_name_crosses_fragmented_requests_and_encrypted_headers_exactly()
    {
        string name = new string('界', ArchiveWritePolicy.MaxRar5NameBytes / 3) + "a"; var profile = Profile(2); byte[] bytes = [1];
        Assert.Equal(ArchiveWritePolicy.MaxRar5NameBytes, ArchiveWritePolicy.Utf8.GetByteCount(name));
        var invocation = ArchiveTransformWire.Prepare(ArchiveRar5TransformPlan.Create(profile, null, [Input(1, bytes)], [], [new(name, ArchiveMemberType.File, 1)], Ct), Ct);
        using var stage = new MemoryStream(); await new ArchiveTransformClient((nonce, _) => Child.Start(nonce, "archive-transform-v2"), protocolVersion: 2)
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => new MemoryStream(bytes, false), Ct, Secret(2));
        byte[] output = stage.ToArray(); Assert.InRange(output.Length, 1, invocation.Request.OutputLengthBound);
        var verified = await new ArchiveVerificationClient((nonce, _) => Child.Start(nonce, "archive-verify-v2"), protocolVersion: 2)
            .VerifyAsync(new("sealed", output.Length, Sha(output)), invocation.Expected, () => new MemoryStream(output, false), Ct, Secret(2));
        Assert.Equal(invocation.Expected.Digest, verified.ManifestDigest);
    }

    [Fact]
    public void Retaining_only_a_directory_does_not_reserve_the_deleted_payloads_physical_size()
    {
        var profile = Profile(0); string revision = Sha([1]);
        var directory = new ArchiveRar5Metadata(ArchiveMemberType.Directory, 0, 0x41ed, 1, 0);
        var file = new ArchiveRar5Metadata(ArchiveMemberType.File, 2, 0x81a4, 1, 3UL << 7, hasCrc32: true);
        var manifest = new ArchiveWriteManifest(profile, [new("d/", ArchiveMemberType.Directory, 0, 0, Sha([]), directory.Encode()),
            new("deleted", ArchiveMemberType.File, 0, ArchiveWritePolicy.MaxPayloadBytes, revision, file.Encode())], new ArchiveRar5Features().Encode());
        var source = new ArchiveAssessment(manifest, revision, ArchiveWritePolicy.MaxPhysicalBytes, revision);
        var plan = ArchiveRar5TransformPlan.Create(profile, source, [], [new(ArchiveChangeKind.Delete, new(revision, "deleted", ArchiveMemberType.File, 0))], [], Ct);
        Assert.InRange(ArchiveTransformWire.Prepare(plan, Ct).Request.OutputLengthBound, 24, 4096);
    }

    [Fact]
    public void The_global_IO_ceiling_allows_two_captured_passes_but_still_requires_actual_grant_lengths()
    {
        var grants = new List<ArchiveInputGrant> { new(0, ArchiveWritePolicy.MaxPhysicalBytes) };
        for (int id = 1; id <= 8; id++) grants.Add(new(id, ArchiveWritePolicy.MaxPayloadBytes));
        long maximum = 4 * ArchiveWritePolicy.MaxPhysicalBytes + 2 * ArchiveWritePolicy.MaxLogicalBytes + (16 << 20);
        using var stage = new MemoryStream(); var proxy = new ArchiveStageProxy(stage, 0, 0); int opens = 0;
        using var authority = new ArchiveIoAuthority(grants, _ => { opens++; throw new InvalidOperationException(); }, proxy, maximum);
        Assert.Equal(0, opens);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveIoAuthority(grants, _ => throw new InvalidOperationException(), proxy, maximum + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchiveIoAuthority([new(1, 1)], _ => throw new InvalidOperationException(), proxy, maximum));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void RAR_name_byte_limits_are_checked_by_parent_planning_before_worker_or_capture_access(bool rename)
    {
        var profile = Profile(0); string name = new string('界', ArchiveWritePolicy.MaxRar5NameBytes / 3) + "ab";
        ArchiveAssessment? source = null; ArchiveTransformEdit[] edits = []; ArchiveTransformAddition[] additions = []; ArchiveTransformInput[] inputs = [];
        if (rename)
        {
            byte[] bytes = Fixture(0); using var memory = new MemoryStream(bytes, false);
            source = Rar5SemanticCatalog.Assessment(StrictRar5Catalog.Read(memory, Ct), profile, Ct);
            edits = [new(ArchiveChangeKind.Rename, new(source.CatalogRevision, "f", ArchiveMemberType.File, 0), name)];
        }
        else { additions = [new(name, ArchiveMemberType.File, 1)]; inputs = [Input(1, [1])]; }
        Assert.Equal(ArchiveWriteReason.BudgetExceeded, Assert.Throws<ArchiveWriteException>(() => ArchiveRar5TransformPlan.Create(profile, source, inputs, edits, additions, Ct)).Reason);
    }

    private static byte[] Fixture(int mode)
    {
        var profile = Profile(mode); byte[] bytes = Data(42829319, 777); using var stage = new MemoryStream();
        using var key = ArchiveWriterProfileAdapter.Rar5Encryption(profile, Secret(mode), new(), Ct);
        Rar5RewriteEngine.Rebuild(null, null, profile, stage, [], [new("f", ArchiveMemberType.File, 1)], [Input(1, bytes)], _ => new MemoryStream(bytes, false), Ct, encryption: key); return stage.ToArray();
    }
    private static void Export(string name, byte[] original, byte[] rebuilt, ArchiveWriteManifest source, ArchiveWriteManifest output)
    {
        if (Environment.GetEnvironmentVariable("FILECAT_RAR_WORKER_EXPORT_ROOT") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, name); Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "source.rar"), original); File.WriteAllBytes(Path.Combine(directory, "rebuilt.rar"), rebuilt);
        void Manifest(string which, ArchiveWriteManifest manifest) => File.WriteAllText(Path.Combine(directory, which + ".json"), JsonSerializer.Serialize(new
        { password = Password, synthetic_public_vector = true, digest = manifest.Digest, features = Convert.ToHexString(manifest.Features), members = manifest.Members.Select(member => new
            { name = member.Name, type = (int)member.Type, length = member.Length, sha256 = member.Sha256, metadata = Convert.ToHexString(member.Metadata) }) }));
        Manifest("source", source); Manifest("rebuilt", output);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(root)!, "runtime-rar-worker.json"), JsonSerializer.Serialize(new
        { runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, runtime_directory = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() }));
    }
}
