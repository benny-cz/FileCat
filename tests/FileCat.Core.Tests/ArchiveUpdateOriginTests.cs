using System.Security.Cryptography;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveUpdateOriginTests
{
    private static readonly string Empty = Convert.ToHexString(SHA256.HashData([]));
    private static ArchiveFileObservation Source(string id, ArchiveOriginSnapshot mark) => new(new(id, 0, Empty), "parent", id, Empty, mark, null, null);
    private static ArchiveOriginSnapshot Zone(int zone) => ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier,
        ArchiveWritePolicy.Utf8.GetBytes($"[ZoneTransfer]\r\nZoneId={zone}\r\nHostUrl=https://original.invalid/{zone}\r\n"));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Update_keeps_the_original_URL_and_raises_only_the_declared_zone_after_full_evidence_binding(bool marked)
    {
        var original = Source("original", marked ? Zone(3) : ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier));
        var inputs = new[] { Source("first", Zone(4)), Source("last", Zone(2)) };
        var result = Review(original, inputs, original, inputs);
        Assert.Equal(marked ? ArchiveWindowsOriginZone.Parse(original.Origin).RaiseTo(4).Digest : ArchiveWindowsOriginZone.ZoneOnly(4).Digest, result.Digest);
    }

    [Theory]
    [InlineData("original-version")] [InlineData("original-mark")] [InlineData("input-version")]
    [InlineData("input-mark")] [InlineData("order")] [InlineData("missing")] [InlineData("extra")]
    [InlineData("late-invalid-mark")] [InlineData("checksum")]
    public void The_maximum_zone_does_not_short_circuit_later_capture_or_checksum_validation(string mutation)
    {
        var original = Source("original", Zone(3)); var inputs = new[] { Source("first", Zone(4)), Source("last", Zone(2)) };
        var expectedOriginal = original; var expected = inputs.ToArray();
        switch (mutation)
        {
            case "original-version": expectedOriginal = Source("other", original.Origin); break;
            case "original-mark": expectedOriginal = Source("original", Zone(2)); break;
            case "input-version": expected[1] = Source("other", expected[1].Origin); break;
            case "input-mark": expected[1] = Source("last", Zone(1)); break;
            case "order": Array.Reverse(expected); break;
            case "missing": expected = expected[..1]; break;
            case "extra": expected = [.. expected, Source("extra", Zone(1))]; break;
            case "late-invalid-mark": inputs[1] = expected[1] = Source("last", ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "invalid"u8)); break;
        }
        Assert.Throws<ArchiveWriteException>(() => Review(original, inputs, expectedOriginal, expected, mutation == "checksum"));
    }

    [Theory]
    [InlineData(ArchiveOriginFormat.MacQuarantine, false, false)]
    [InlineData(ArchiveOriginFormat.MacQuarantine, false, true)]
    [InlineData(ArchiveOriginFormat.MacQuarantine, true, false)]
    [InlineData(ArchiveOriginFormat.MacQuarantine, true, true)]
    [InlineData(ArchiveOriginFormat.LinuxOriginUrl, false, false)]
    [InlineData(ArchiveOriginFormat.LinuxOriginUrl, false, true)]
    [InlineData(ArchiveOriginFormat.LinuxOriginUrl, true, false)]
    [InlineData(ArchiveOriginFormat.LinuxOriginUrl, true, true)]
    public void Unix_update_retains_one_exact_mark_from_the_original_or_inputs(ArchiveOriginFormat format, bool oldMarked, bool inputMarked)
    {
        var mark = ArchiveOriginSnapshot.Present(format, [0, 255, 9]);
        var absent = ArchiveOriginSnapshot.Absent(format);
        var original = Source("original", oldMarked ? mark : absent);
        var input = Source("input", inputMarked ? mark : absent);
        var result = ReviewUnix(original, [input], original, [input]);
        Assert.Equal(oldMarked || inputMarked ? mark.Digest : absent.Digest, result.Digest);
    }

    [Theory]
    [InlineData(ArchiveOriginFormat.MacQuarantine)] [InlineData(ArchiveOriginFormat.LinuxOriginUrl)]
    public void Unix_update_binds_directories_and_refuses_conflicting_or_unavailable_marks(ArchiveOriginFormat format)
    {
        var mark = ArchiveOriginSnapshot.Present(format, []);
        var different = ArchiveOriginSnapshot.Present(format, [1]);
        var absent = ArchiveOriginSnapshot.Absent(format);
        var original = Source("original", mark);
        var input = Source("input", absent);
        ArchiveDirectoryObservation Directory(ArchiveOriginSnapshot origin) => new("folder", "parent", "folder", Empty, origin, new(0, 0), 0x1c0);
        var directory = Directory(mark);
        Assert.Equal(mark.Digest, ReviewUnix(original, [input], original, [input], [directory], [directory]).Digest);
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
            ReviewUnix(original, [input], original, [input], [directory], [Directory(absent)])).Reason);
        Assert.Equal(ArchiveWriteReason.PublicationUnavailable, Assert.Throws<ArchiveWriteException>(() =>
            ReviewUnix(original, [Source("input", different)], original, [Source("input", different)], [directory], [directory])).Reason);
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
            ReviewUnix(original, [input], original, [input], evidenceInputOrigin:
                ArchiveOriginSnapshot.Unavailable(format, ArchiveOriginFailure.AccessDenied))).Reason);
        Assert.Equal(ArchiveWriteReason.NeedsReview, Assert.Throws<ArchiveWriteException>(() =>
            ReviewUnix(original, [input], original, [input], [Directory(different)], [Directory(different)], corrupt: true)).Reason);
    }

    private static ArchiveOriginSnapshot ReviewUnix(ArchiveFileObservation original, ArchiveFileObservation[] inputs,
        ArchiveFileObservation expectedOriginal, ArchiveFileObservation[] expected,
        ArchiveDirectoryObservation[]? directories = null, ArchiveDirectoryObservation[]? expectedDirectories = null,
        bool corrupt = false, ArchiveOriginSnapshot? evidenceInputOrigin = null)
    {
        var scope = new ArchiveOriginEvidenceScope(Guid.NewGuid(), new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Update,
            inputs.Length + (directories?.Length ?? 0) + 1);
        using var stream = new MemoryStream(); using var writer = new ArchiveOriginEvidenceWriter(stream, scope);
        writer.Append(new(ArchiveOriginSourceKind.OriginalArchive, original.Version, original.Origin));
        foreach (var input in inputs) writer.Append(new(ArchiveOriginSourceKind.File, input.Version, evidenceInputOrigin ?? input.Origin));
        foreach (var directory in directories ?? []) writer.Append(new(ArchiveOriginSourceKind.Directory, directory.Version, directory.Origin));
        var info = writer.Complete(); byte[] bytes = stream.ToArray(); if (corrupt) bytes[^1] ^= 1;
        using var view = new MemoryStream(bytes, false);
        try { return ArchiveUpdateOrigin.ReviewUnixExact(view, info, scope, expectedOriginal, expected,
            TestContext.Current.CancellationToken, expectedDirectories); }
        finally { Assert.False(view.CanRead); }
    }

    private static ArchiveOriginSnapshot Review(ArchiveFileObservation original, ArchiveFileObservation[] inputs,
        ArchiveFileObservation expectedOriginal, ArchiveFileObservation[] expected, bool corrupt = false)
    {
        var scope = new ArchiveOriginEvidenceScope(Guid.NewGuid(), new(ArchiveWriteFormat.Tar), ArchiveOriginOperation.Update, inputs.Length + 1);
        using var stream = new MemoryStream(); using var writer = new ArchiveOriginEvidenceWriter(stream, scope);
        writer.Append(new(ArchiveOriginSourceKind.OriginalArchive, original.Version, original.Origin));
        foreach (var input in inputs) writer.Append(new(ArchiveOriginSourceKind.File, input.Version, input.Origin));
        var info = writer.Complete(); byte[] bytes = stream.ToArray(); if (corrupt) bytes[^1] ^= 1;
        using var view = new MemoryStream(bytes, false);
        try { return ArchiveUpdateOrigin.ReviewWindows(view, info, scope, expectedOriginal, expected, TestContext.Current.CancellationToken); }
        finally { Assert.False(view.CanRead); }
    }
}
