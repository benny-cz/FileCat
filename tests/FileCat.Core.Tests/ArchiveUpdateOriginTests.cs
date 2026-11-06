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
