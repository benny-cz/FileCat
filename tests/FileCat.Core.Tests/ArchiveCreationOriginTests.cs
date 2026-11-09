using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveCreationOriginTests
{
    private static readonly string Empty = Convert.ToHexString(SHA256.HashData([]));
    private static ArchiveFileObservation Input(string id, ArchiveOriginSnapshot origin) =>
        new(new(id, 0, Empty), "parent", id, Empty, origin, new(0, 0), 0x180);
    private static ArchiveOriginSnapshot Zone(int zone) => ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier,
        ArchiveWritePolicy.Utf8.GetBytes($"[ZoneTransfer]\r\nZoneId={zone}\r\nHostUrl=https://example.test/{zone}\r\n"));
    private static (byte[] Bytes, ArchiveOriginEvidenceInfo Info, ArchiveOriginEvidenceScope Scope) Evidence(params ArchiveFileObservation[] inputs)
    {
        var scope = new ArchiveOriginEvidenceScope(Guid.NewGuid(), new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Create, inputs.Length);
        using var stream = new MemoryStream(); using var writer = new ArchiveOriginEvidenceWriter(stream, scope);
        foreach (var input in inputs) writer.Append(new(ArchiveOriginSourceKind.File, input.Version, input.Origin));
        var info = writer.Complete(); return (stream.ToArray(), info, scope);
    }
    private static (byte[] Bytes, ArchiveOriginEvidenceInfo Info, ArchiveOriginEvidenceScope Scope) Evidence(
        ArchiveFileObservation[] files, ArchiveDirectoryObservation[] directories)
    {
        var scope = new ArchiveOriginEvidenceScope(Guid.NewGuid(), new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Create,
            files.Length + directories.Length);
        using var stream = new MemoryStream(); using var writer = new ArchiveOriginEvidenceWriter(stream, scope);
        foreach (var file in files) writer.Append(new(ArchiveOriginSourceKind.File, file.Version, file.Origin));
        foreach (var directory in directories) writer.Append(new(ArchiveOriginSourceKind.Directory, directory.Version, directory.Origin));
        var info = writer.Complete(); return (stream.ToArray(), info, scope);
    }
    private static ArchiveOriginSnapshot Review((byte[] Bytes, ArchiveOriginEvidenceInfo Info, ArchiveOriginEvidenceScope Scope) evidence,
        ArchiveFileObservation[] captured, ArchiveOriginFormat format = ArchiveOriginFormat.WindowsZoneIdentifier) =>
        ArchiveCreationOrigin.Review(new MemoryStream(evidence.Bytes, false), evidence.Info, evidence.Scope, captured, format, TestContext.Current.CancellationToken);

    [Fact]
    public void Creation_derives_the_highest_zone_without_fabricating_a_source_URL_and_preserves_all_raw_evidence()
    {
        var inputs = new[] { Input("first", Zone(4)), Input("second", Zone(3)), Input("third", Zone(0)) }; var evidence = Evidence(inputs);
        var required = Review(evidence, inputs);
        Assert.Equal(ArchiveWindowsOriginZone.ZoneOnly(4).Digest, required.Digest);
        Assert.DoesNotContain("https:", ArchiveWritePolicy.Utf8.GetString(required.Bytes));
        Assert.True(evidence.Bytes.AsSpan().IndexOf("https://example.test/3"u8) >= 0);
    }

    [Theory]
    [InlineData("different-version")] [InlineData("different-mark")] [InlineData("reordered")]
    [InlineData("missing")] [InlineData("extra")] [InlineData("checksum")]
    public void Every_record_remains_bound_to_the_exact_capture_after_the_highest_zone(string failure)
    {
        var inputs = new[] { Input("first", Zone(4)), Input("second", Zone(3)) }; var evidence = Evidence(inputs);
        var captured = inputs.ToArray();
        if (failure == "different-version") captured[1] = Input("different", Zone(3));
        if (failure == "different-mark") captured[1] = Input("second", Zone(2));
        if (failure == "reordered") Array.Reverse(captured);
        if (failure == "missing") captured = captured[..1];
        if (failure == "extra") captured = [.. captured, Input("extra", Zone(1))];
        if (failure == "checksum") evidence.Bytes[^1] ^= 1;
        Assert.Throws<ArchiveWriteException>(() => Review(evidence, captured));
    }

    [Fact]
    public void An_unsupported_later_declaration_is_not_ignored_after_maximum_classification()
    {
        var inputs = new[] { Input("first", Zone(4)), Input("second", ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "invalid"u8)) };
        Assert.Throws<ArchiveWriteException>(() => Review(Evidence(inputs), inputs));
    }

    [Theory]
    [InlineData(ArchiveOriginFormat.WindowsZoneIdentifier)] [InlineData(ArchiveOriginFormat.LinuxOriginUrl)] [InlineData(ArchiveOriginFormat.MacQuarantine)]
    public void Typed_absence_is_preserved_and_never_materialized_as_an_empty_mark(ArchiveOriginFormat format)
    {
        var inputs = new[] { Input("first", ArchiveOriginSnapshot.Absent(format)) };
        var required = Review(Evidence(inputs), inputs, format); Assert.Equal(format, required.Format); Assert.Equal(ArchiveOriginState.Absent, required.State);
    }

    [Theory]
    [InlineData(ArchiveOriginFormat.LinuxOriginUrl)] [InlineData(ArchiveOriginFormat.MacQuarantine)]
    public void Unix_creation_retains_one_exact_opaque_mark_among_marked_and_unmarked_inputs(ArchiveOriginFormat format)
    {
        foreach (byte[] bytes in new byte[][] { [], [0, 255, 1, 0] })
        {
            var mark = ArchiveOriginSnapshot.Present(format, bytes);
            var inputs = new[] { Input("first", ArchiveOriginSnapshot.Absent(format)), Input("second", mark), Input("third", mark) };
            var required = Review(Evidence(inputs), inputs, format);
            Assert.Equal(ArchiveOriginState.Present, required.State);
            Assert.Equal(mark.Digest, required.Digest);
            Assert.Equal(bytes, required.Bytes.ToArray());
            ArchiveCreationOrigin.RequireSupported(required);
        }
    }

    [Theory]
    [InlineData(ArchiveOriginFormat.LinuxOriginUrl)] [InlineData(ArchiveOriginFormat.MacQuarantine)]
    public void Unix_creation_refuses_conflicting_marks_and_stale_evidence(ArchiveOriginFormat format)
    {
        var first = ArchiveOriginSnapshot.Present(format, [0, 1]);
        var inputs = new[] { Input("first", first), Input("second", ArchiveOriginSnapshot.Absent(format)),
            Input("third", ArchiveOriginSnapshot.Present(format, [0, 2])) };
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable,
            Assert.Throws<ArchiveWriteException>(() => Review(Evidence(inputs), inputs, format)).Reason);
        var stale = inputs.ToArray(); stale[1] = Input("other", ArchiveOriginSnapshot.Absent(format));
        Assert.Equal(ArchiveWriteReason.NeedsReview,
            Assert.Throws<ArchiveWriteException>(() => Review(Evidence(inputs), stale, format)).Reason);
    }

    [Theory]
    [InlineData(ArchiveOriginFormat.LinuxOriginUrl)] [InlineData(ArchiveOriginFormat.MacQuarantine)]
    public void Unix_creation_binds_directory_marks_to_the_same_exact_policy(ArchiveOriginFormat format)
    {
        var mark = ArchiveOriginSnapshot.Present(format, [0, 255, 1]);
        var files = new[] { Input("file", ArchiveOriginSnapshot.Absent(format)) };
        ArchiveDirectoryObservation Directory(ArchiveOriginSnapshot origin) => new("tree", "parent", "tree", Empty, origin, new(0, 0), 0x1c0);
        var directories = new[] { Directory(mark) }; var evidence = Evidence(files, directories);
        ArchiveOriginSnapshot ReviewDirectories(ArchiveFileObservation[] capturedFiles, ArchiveDirectoryObservation[] capturedDirectories) =>
            ArchiveCreationOrigin.Review(new MemoryStream(evidence.Bytes, false), evidence.Info, evidence.Scope,
                capturedFiles, format, TestContext.Current.CancellationToken, capturedDirectories);
        Assert.Equal(mark.Digest, ReviewDirectories(files, directories).Digest);
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
            ReviewDirectories(files, [Directory(ArchiveOriginSnapshot.Absent(format))])).Reason);
        files = [Input("file", ArchiveOriginSnapshot.Present(format, [0, 1]))];
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
            ReviewDirectories(files, directories)).Reason);
        var conflicting = Evidence(files, directories);
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() =>
            ArchiveCreationOrigin.Review(new MemoryStream(conflicting.Bytes, false), conflicting.Info, conflicting.Scope,
                files, format, TestContext.Current.CancellationToken, directories)).Reason);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void The_owned_evidence_view_is_closed_after_success_or_refusal(bool mismatch)
    {
        var inputs = new[] { Input("first", Zone(4)) }; var evidence = Evidence(inputs);
        using var view = new MemoryStream(evidence.Bytes, false);
        var captured = mismatch ? new[] { Input("other", Zone(4)) } : inputs;
        if (mismatch) Assert.Throws<ArchiveWriteException>(() => ArchiveCreationOrigin.Review(view, evidence.Info, evidence.Scope, captured,
            ArchiveOriginFormat.WindowsZoneIdentifier, TestContext.Current.CancellationToken));
        else ArchiveCreationOrigin.Review(view, evidence.Info, evidence.Scope, captured, ArchiveOriginFormat.WindowsZoneIdentifier, TestContext.Current.CancellationToken);
        Assert.False(view.CanRead);
    }
}
