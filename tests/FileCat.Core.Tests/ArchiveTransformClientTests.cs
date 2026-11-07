using System.Formats.Tar;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using Child = FileCat.Core.Tests.ArchiveAssessmentClientTests.Child;
using Scripted = FileCat.Core.Tests.ArchiveAssessmentClientTests.Scripted;

namespace FileCat.Core.Tests;

[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed class ArchiveTransformClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    private static ArchiveTransformInput Input(int id, byte[] data) => new(id, data.Length, Hash(data), DateTimeOffset.UnixEpoch.AddTicks(7));
    private static async Task Verify(ArchiveTransformInvocation invocation, byte[] data)
    {
        var result = await new ArchiveVerificationClient((nonce, _) => Child.Start(nonce, "archive-verify"))
            .VerifyAsync(new("parent-stage", data.Length, Hash(data)), invocation.Expected, () => new MemoryStream(data, writable: false), Ct);
        Assert.Equal(invocation.Expected.Digest, result.ManifestDigest);
    }

    [Fact]
    public async Task Creation_streams_parent_grants_in_a_disposable_transform_process_then_verifies_fresh()
    {
        byte[] payload = new byte[4 << 20]; new Random(1487).NextBytes(payload);
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null, [Input(37, payload)], [],
            [new("dir/", ArchiveMemberType.Directory), new("dir/one", ArchiveMemberType.File, 37), new("two", ArchiveMemberType.File, 37)], Ct), Ct);
        using var stage = new MemoryStream(); Child? process = null; int opens = 0;
        var receipt = await new ArchiveTransformClient((nonce, _) => process = Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException("No archive grant exists."), id =>
            { Assert.Equal(37, id); opens++; return new MemoryStream(payload, writable: false); }, Ct);
        Assert.Equal(2, opens); Assert.True(process!.Reaped); Assert.True(process.Disposed); Assert.Equal(stage.Length, receipt.Length);
        Assert.InRange(stage.Length, 1024, invocation.Request.OutputLengthBound);
        await Verify(invocation, stage.ToArray());
    }

    [Theory]
    [InlineData(TarEntryFormat.Pax)]
    [InlineData(TarEntryFormat.Ustar)]
    public async Task A_worker_rewrites_exact_duplicates_and_preserves_metadata_against_the_parent_expectation(TarEntryFormat format)
    {
        byte[] original = ArchiveStrictTarTests.Make(format, ("same", "first"), ("same", "second"), ("dir/", ""));
        var assessed = await new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce)).AssessAsync(new(new(ArchiveWriteFormat.Tar), original.Length, Hash(original)), () => new MemoryStream(original, writable: false), Ct);
        ArchiveMemberKey Key(int i) { var m = assessed.Manifest.Members[i]; return new(assessed.CatalogRevision, m.Name, m.Type, m.Occurrence); }
        byte[] replacement = "replacement"u8.ToArray();
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(assessed, [Input(9, replacement)],
            [new(ArchiveChangeKind.Delete, Key(0)), new(ArchiveChangeKind.Replace, Key(1), inputId: 9), new(ArchiveChangeKind.Rename, Key(2), "renamed/")],
            [new("added", ArchiveMemberType.File, 9)], Ct), Ct);
        using var stage = new MemoryStream(); Child? process = null;
        await new ArchiveTransformClient((nonce, _) => process = Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => new MemoryStream(original, writable: false), id => { Assert.Equal(9, id); return new MemoryStream(replacement, writable: false); }, Ct);
        Assert.True(process!.Reaped); Assert.Equal(new[] { "same", "renamed/", "added" }, invocation.Expected.Members.Select(m => m.Name));
        await Verify(invocation, stage.ToArray());
    }

    [Fact]
    public async Task A_maximum_length_unicode_name_crosses_fragmented_control_and_stage_frames_without_losing_bytes()
    {
        string name = new('界', ArchiveWritePolicy.MaxNameChars); byte[] payload = [1];
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null, [Input(1, payload)], [], [new(name, ArchiveMemberType.File, 1)], Ct), Ct);
        using var stage = new MemoryStream();
        await new ArchiveTransformClient((nonce, _) => Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => new MemoryStream(payload, writable: false), Ct);
        Assert.InRange(stage.Length, 1, invocation.Request.OutputLengthBound); await Verify(invocation, stage.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_creation_and_remove_all_still_finish_with_a_valid_TAR(bool update)
    {
        byte[] original = ArchiveStrictTarTests.Make(TarEntryFormat.Pax, ("file", "data")); ArchiveAssessment? source = null;
        if (update) source = await new ArchiveAssessmentClient((nonce, _) => Child.Start(nonce)).AssessAsync(new(new(ArchiveWriteFormat.Tar), original.Length, Hash(original)), () => new MemoryStream(original, writable: false), Ct);
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(source, [], source is null ? [] :
            [new(ArchiveChangeKind.Delete, new(source.CatalogRevision, "file", ArchiveMemberType.File, 0))], [], Ct), Ct);
        using var stage = new MemoryStream();
        await new ArchiveTransformClient((nonce, _) => Child.Start(nonce, "archive-transform")).RebuildAsync(invocation, stage,
            () => new MemoryStream(original, writable: false), _ => throw new InvalidOperationException(), Ct);
        Assert.Equal(1024, stage.Length); await Verify(invocation, stage.ToArray());
    }

    [Theory]
    [InlineData("archive-assess")]
    [InlineData("archive-verify")]
    public async Task Another_worker_role_is_rejected_before_any_input_or_stage_mutation(string role)
    {
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null, [], [], [], Ct), Ct); using var stage = new MemoryStream(); Child? child = null;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => new ArchiveTransformClient((nonce, _) => child = Child.Start(nonce, role))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), Ct));
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, failure.Reason); Assert.Equal(0, stage.Length); Assert.True(child!.Reaped);
    }

    [Fact]
    public async Task Changed_captured_payload_refuses_and_reaps_the_worker_without_a_result()
    {
        byte[] data = "correct"u8.ToArray(); var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null, [Input(1, data)], [], [new("file", ArchiveMemberType.File, 1)], Ct), Ct);
        using var stage = new MemoryStream(); Child? child = null;
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => new ArchiveTransformClient((nonce, _) => child = Child.Start(nonce, "archive-transform"))
            .RebuildAsync(invocation, stage, () => throw new InvalidOperationException(), _ => new MemoryStream("changed"u8.ToArray(), writable: false), Ct));
        Assert.Equal(ArchiveWriteReason.SourceChanged, failure.Reason); Assert.True(child!.Reaped); Assert.True(child.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsolicited_source_and_over_bound_stage_requests_are_refused_before_touching_streams(bool write)
    {
        var invocation = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(null, [], [], [], Ct), Ct); using var stage = new MemoryStream(); Scripted? child = null;
        var client = new ArchiveTransformClient((nonce, _) =>
        {
            using var output = new MemoryStream(); var wire = new ArchiveWorkerProtocol(nonce, ArchiveWritePolicy.MaxWorkerWireBytes); wire.Write(output, ArchiveWorkerMessage.Hello, ArchiveTransformWire.Hello);
            byte[] request = new byte[write ? 9 : 16];
            if (write) System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(request, 1024);
            wire.Write(output, write ? ArchiveWorkerMessage.Write : ArchiveWorkerMessage.Read, request); return child = new Scripted(output.ToArray());
        });
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.RebuildAsync(invocation, stage,
            () => throw new InvalidOperationException(), _ => throw new InvalidOperationException(), Ct));
        Assert.Equal(ArchiveWriteReason.ProtocolViolation, failure.Reason); Assert.Equal(0, stage.Length); Assert.True(child!.Disposed);
    }
}
