using System.Buffers.Binary;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed partial class ArchiveCapturedInputsTests
{
    [Fact]
    public void Bound_directories_keep_exact_metadata_and_provenance_without_worker_file_grants()
    {
        using var captured = new ArchiveCapturedInputs();
        captured.Capture(11, new Source(11).Open, Ct); captured.CaptureDirectory(2, _ => Folder(2, mode: 0x1d9), Ct);
        captured.CaptureDirectory(3, _ => Folder(3, mode: 0x1c0), Ct); captured.Seal(Ct);
        var plan = captured.CreatePlan(new(ArchiveWriteFormat.Tar), null, [],
            [captured.DirectoryAddition("tree/", 2), captured.DirectoryAddition("empty/", 3), new("tree/file", ArchiveMemberType.File, 11)], Ct);
        Assert.Same(captured.Directories, plan.Directories); ArchiveCapturedInputs.RequirePlanCost(plan, captured.Bindings);
        Assert.Equal(0x1d9, BinaryPrimitives.ReadInt32LittleEndian(plan.Expected.Members[0].Metadata[2..]));
        Assert.Equal(36, plan.Expected.Members[0].Metadata.Length);
        var invocation = ArchiveTransformWire.Prepare(plan, Ct);
        Assert.Single(invocation.Request.Inputs); Assert.Single(invocation.CapturedInputIds);
        Assert.Equal(11, invocation.CapturedInputIds[1]);
        Assert.All(invocation.Request.Additions.Take(2), a => { Assert.Null(a.InputId); Assert.Null(a.CapturedDirectory); });
        using var bytes = new MemoryStream(); var nonce = Guid.NewGuid();
        ArchiveTransformWire.WriteRequest(new(new(nonce, ArchiveWritePolicy.MaxWorkerWireBytes), bytes, true), invocation.Request, Ct);
        bytes.Position = 0;
        var decoded = ArchiveTransformWire.ReadRequest(new(new(nonce, ArchiveWritePolicy.MaxWorkerWireBytes), bytes, false), Ct);
        Assert.Equal(new ArchiveTimestamp(1700000000, 123456789), decoded.Additions[0].DirectoryMetadata!.Modified);
        Assert.Equal(0x1d9, decoded.Additions[0].DirectoryMetadata!.Mode);
        var workerPlan = ArchiveTarTransformPlan.Create(decoded.Profile, null, decoded.Inputs.Values.ToArray(), [], decoded.Additions, Ct);
        Assert.Equal(plan.Expected.Digest, workerPlan.Expected.Digest); Assert.Empty(workerPlan.Directories);
    }

    [Fact]
    public void Directory_scope_cannot_be_dropped_retargeted_or_replaced_with_equal_unbound_metadata()
    {
        using var first = new ArchiveCapturedInputs(); using var other = new ArchiveCapturedInputs();
        first.CaptureDirectory(1, _ => Folder(1), Ct); other.CaptureDirectory(1, _ => Folder(1), Ct); first.Seal(Ct); other.Seal(Ct);
        var bound = first.DirectoryAddition("folder/", 1);
        Assert.Throws<ArchiveWriteException>(() => other.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], [bound], Ct));
        Assert.Throws<ArchiveWriteException>(() => first.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], [new("folder/", bound.DirectoryMetadata!)], Ct));
        Assert.Throws<ArchiveWriteException>(() => ArchiveTarTransformPlan.Create(null, [], [], [bound], Ct));
        var plan = first.CreatePlan(new(ArchiveWriteFormat.Tar), null, [], [bound], Ct);
        first.Dispose(); Assert.Throws<ObjectDisposedException>(() => ArchiveCapturedInputs.RequirePlanCost(plan, first.Bindings));
    }

    [Fact]
    public void Directory_origins_participate_in_creation_and_update_union_with_exact_source_kinds()
    {
        var file = new Source(1).Observation(); var folder = Folder(2, origin: ArchiveWindowsOriginZone.ZoneOnly(4));
        var profile = new ArchiveWriteProfile(ArchiveWriteFormat.Tar);
        foreach (bool update in new[] { false, true })
        {
            var scope = new ArchiveOriginEvidenceScope(Guid.NewGuid(), profile, update ? ArchiveOriginOperation.Update : ArchiveOriginOperation.Create, update ? 3 : 2);
            foreach (bool wrongKind in new[] { false, true })
            {
                using var bytes = new MemoryStream(); using var writer = new ArchiveOriginEvidenceWriter(bytes, scope, Ct);
                if (update) writer.Append(new(ArchiveOriginSourceKind.OriginalArchive, file.Version, file.Origin));
                writer.Append(new(ArchiveOriginSourceKind.File, file.Version, file.Origin));
                writer.Append(new(wrongKind ? ArchiveOriginSourceKind.File : ArchiveOriginSourceKind.Directory, folder.Version, folder.Origin));
                var evidence = writer.Complete(); bytes.Position = 0;
                ArchiveOriginSnapshot Review() => update ? ArchiveUpdateOrigin.ReviewWindows(bytes, evidence, scope, file, [file], Ct, [folder])
                    : ArchiveCreationOrigin.Review(bytes, evidence, scope, [file], file.Origin.Format, Ct, [folder]);
                if (wrongKind) Assert.Throws<ArchiveWriteException>(() => Review());
                else Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(4).Digest, Review().Digest);
            }
        }
    }
}
