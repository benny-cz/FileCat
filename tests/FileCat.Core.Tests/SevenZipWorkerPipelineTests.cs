using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class SevenZipWorkerPipelineTests
{
    private const string Password = "Public-7z-worker-暗号-42829324";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Sha(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value));
    private static ArchiveWriteProfile Profile(int codec, int mode, int filter = 0) => ArchiveWriteProfile.ForSevenZip(
        new((ArchiveSevenZipCodec)codec, (ArchiveSevenZipFilter)filter, codec == 6 ? 4 : 0, codec == 6 ? 1 << 20 : 0),
        codec == 0 ? 0 : 1, codec is 1 or 2 ? 1 << 20 : 8 << 20, mode == 0 ? null : new((ArchiveEncryptionMode)mode));
    private static ArchiveWorkerSecretGrant? Secret(int mode) => mode == 0 ? null : new(Password);
    private static ArchiveTransformInput Input(int id, byte[] data) => new(id, data.Length, Sha(data), new ArchiveTimestamp(1700000000, 123456700));
    private static byte[] Data(int seed, int length)
    { byte[] bytes = new byte[length]; new Random(seed).NextBytes(bytes); for (int i = 0; i + 5 < length; i += 32) { bytes[i] = 0xe8; bytes[i + 4] = 0; } return bytes; }
    public static IEnumerable<object[]> Modes() => (from codec in Enumerable.Range(0, 7) from mode in Enumerable.Range(0, 3) select new object[] { codec, mode, 0 })
        .Concat(from codec in new[] { 4, 5 } from mode in Enumerable.Range(0, 3) select new object[] { codec, mode, 12 });

    [Theory] [MemberData(nameof(Modes))]
    public async Task Complete_sevenzip_jobs_cross_bound_processes_and_fresh_verification(int codec, int mode, int filter)
    {
        var profile = Profile(codec, mode, filter); byte[] data = Data(42829324, 70001), replacement = Data(42829325, 131089);
        var children = new List<Child>(); IArchiveWorkerProcess Launch(Guid nonce, string role) { var child = Child.Start(nonce, role); children.Add(child); return child; }
        var first = ArchiveTransformWire.Prepare(ArchiveSevenZipTransformPlan.Create(profile, null, [Input(37, data), Input(38, [])], [],
            [new("keep.bin", ArchiveMemberType.File, 37), new("replace.bin", ArchiveMemberType.File, 37), new("delete.bin", ArchiveMemberType.File, 37),
                new("folder/", new ArchiveTransformDirectoryMetadata(new(1, 100))), new("empty.bin", ArchiveMemberType.File, 38)], Ct), Ct);
        Assert.Equal(4, first.CapturedInputIds.Count); using var createdStage = new MemoryStream(); int opens = 0;
        var created = await new ArchiveTransformClient((nonce, _) => Launch(nonce, "archive-transform-v2"), protocolVersion: 2)
            .RebuildAsync(first, createdStage, () => throw new InvalidOperationException(), id => { opens++; return new MemoryStream(id == 37 ? data : [], false); }, Ct, Secret(mode));
        Assert.Equal(3, opens); Assert.InRange(created.Length, 32, first.Request.OutputLengthBound); byte[] original = createdStage.ToArray();
        var assessment = await new ArchiveAssessmentClient((nonce, _) => Launch(nonce, "archive-assess-v2"), protocolVersion: 2)
            .AssessAsync(new(profile, original.Length, Sha(original)), () => new MemoryStream(original, false), Ct, Secret(mode));
        Assert.Equal(first.Expected.Digest, assessment.Manifest.Digest);
        ArchiveMemberKey Key(int index) { var m = assessment.Manifest.Members[index]; return new(assessment.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        var update = ArchiveTransformWire.Prepare(ArchiveSevenZipTransformPlan.Create(profile, assessment, [Input(42, replacement)],
            [new(ArchiveChangeKind.Rename, Key(0), "renamed-界.bin"), new(ArchiveChangeKind.Replace, Key(1), inputId: 42), new(ArchiveChangeKind.Delete, Key(2))],
            [new("new-folder/", new ArchiveTransformDirectoryMetadata(new(2, 200))), new("new-界.bin", ArchiveMemberType.File, 42)], Ct), Ct);
        Assert.Equal(2, update.CapturedInputIds.Count); using var stage = new MemoryStream(); int sourceOpens = 0; opens = 0;
        var receipt = await new ArchiveTransformClient((nonce, _) => Launch(nonce, "archive-transform-v2"), protocolVersion: 2)
            .RebuildAsync(update, stage, () => { sourceOpens++; return new MemoryStream(original, false); },
                id => { Assert.Equal(42, id); opens++; return new MemoryStream(replacement, false); }, Ct, Secret(mode));
        Assert.Equal(2, sourceOpens); Assert.Equal(2, opens); Assert.InRange(receipt.Length, 32, update.Request.OutputLengthBound); byte[] rebuilt = stage.ToArray();
        var verified = await new ArchiveVerificationClient((nonce, _) => Launch(nonce, "archive-verify-v2"), protocolVersion: 2)
            .VerifyAsync(new("sealed-7z-parent-stage", rebuilt.Length, Sha(rebuilt)), update.Expected, () => new MemoryStream(rebuilt, false), Ct, Secret(mode));
        Assert.Equal(update.Expected.Digest, verified.ManifestDigest); Assert.Equal(4, children.Count);
        Assert.All(children, child => { Assert.True(child.Reaped); Assert.True(child.Disposed); Assert.Equal(0u, child.ExitCode); });
        if (Environment.GetEnvironmentVariable("FILECAT_7Z_WORKER_EXPORT_ROOT") is { } root)
        {
            string path = Path.Combine(root, $"codec-{codec}-filter-{filter}-mode-{mode}"); Directory.CreateDirectory(path);
            Export("created", original, first.Expected); Export("rebuilt", rebuilt, verified.ObservedManifest);
            void Export(string name, byte[] bytes, ArchiveWriteManifest manifest)
            {
                File.WriteAllBytes(Path.Combine(path, name + ".7z"), bytes); File.WriteAllText(Path.Combine(path, name + ".json"), JsonSerializer.Serialize(new
                { synthetic_public_vector = true, password = Password, digest = manifest.Digest, features = Convert.ToHexString(manifest.Features),
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                    members = manifest.Members.Select(m => new { name = m.Name, type = (int)m.Type, length = m.Length, sha256 = m.Sha256, metadata = Convert.ToHexString(m.Metadata) }) }));
            }
        }
    }

    [Theory] [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public Task Windows_preflight_covers_real_sevenzip_worker_roles_and_passwords(int mode)
        => Complete_sevenzip_jobs_cross_bound_processes_and_fresh_verification(5, mode, 12);

    public static IEnumerable<object[]> RoleDamage() => from role in Enumerable.Range(1, 3) from binding in new[] { false, true } select new object[] { role, binding };
    [Theory] [MemberData(nameof(RoleDamage))]
    public async Task Wrong_role_or_request_bound_secrets_refuse_before_any_proxy_IO(int role, bool binding)
    {
        Child? child = null; string endpoint = role switch { 1 => "archive-assess-v2", 2 => "archive-transform-v2", _ => "archive-verify-v2" };
        await ArchiveWorkerSession.RunAsync((nonce, _) => child = Child.Start(nonce, endpoint), new ArchiveWorkerQueue(), TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), (nonce, process, progress) =>
            {
                progress(); var receive = new ArchiveWorkerProtocol(nonce, 1 << 20, 2); var send = new ArchiveWorkerProtocol(nonce, 1 << 20, 2);
                Assert.Equal(ArchiveWorkerMessage.Hello, receive.Read(process.Output).Kind); var records = new ArchiveWorkerRecords(send, process.Input, true); string digest;
                if (role == 2)
                {
                    var request = ArchiveTransformWire.Prepare(ArchiveSevenZipTransformPlan.Create(Profile(0, 1), null, [Input(1, [1])], [], [new("f", ArchiveMemberType.File, 1)], Ct), Ct).Request;
                    ArchiveTransformWire.WriteRequest(records, request, Ct); digest = ArchiveTransformWire.RequestDigest(request, Ct);
                }
                else
                { var request = new ArchiveAssessmentRequest(Profile(0, 1), 32, Sha(new byte[32])); records.Write(ArchiveControlRecord.AssessmentRequest, ArchiveAssessmentWire.Request(request)); digest = ArchiveAssessmentWire.RequestDigest(request); }
                using var grant = new ArchiveWorkerSecretGrant(Password);
                ArchiveWorkerSecretWire.WriteGrant(send, process.Input, binding ? (ArchiveWorkerRole)role : role == 1 ? ArchiveWorkerRole.Verify : ArchiveWorkerRole.Assess,
                    binding ? Sha([99]) : digest, grant, Ct);
                var result = receive.Read(process.Output); Assert.Equal(ArchiveWorkerMessage.Refused, result.Kind);
                Assert.Equal((int)ArchiveWriteReason.ProtocolViolation, BinaryPrimitives.ReadInt32LittleEndian(result.Payload)); Assert.Equal(-1, process.Output.ReadByte()); progress(); return 0;
            }, Ct).WaitAsync(TimeSpan.FromMinutes(2), Ct);
        Assert.True(child!.Reaped); Assert.True(child.Disposed); Assert.Equal(0u, child.ExitCode);
    }

    [Fact]
    public async Task Unencrypted_sevenzip_also_works_on_the_explicit_protocol_one_endpoints()
    {
        var profile = Profile(0, 0); byte[] data = [1, 2, 3];
        var invocation = ArchiveTransformWire.Prepare(ArchiveSevenZipTransformPlan.Create(profile, null, [Input(5, data)], [], [new("f", ArchiveMemberType.File, 5)], Ct), Ct);
        using var stage = new MemoryStream();
        await new ArchiveTransformClient((nonce, _) => Child.Start(nonce, "archive-transform")).RebuildAsync(invocation, stage,
            () => throw new InvalidOperationException(), _ => new MemoryStream(data, false), Ct);
        byte[] output = stage.ToArray(); var verified = await new ArchiveVerificationClient((nonce, _) => Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("sealed", output.Length, Sha(output)), invocation.Expected, () => new MemoryStream(output, false), Ct);
        Assert.Equal(invocation.Expected.Digest, verified.ManifestDigest);
    }
}
