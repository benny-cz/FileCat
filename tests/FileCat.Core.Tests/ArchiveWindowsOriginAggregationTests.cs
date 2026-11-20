using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveWindowsOriginAggregationTests
{
    private static readonly Guid Nonce = Guid.Parse("259e2159-a8c6-4471-8506-ab5c937b74ab");
    private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static ArchiveOriginSnapshot Mark(int? zone, string url = "https://example.test/input") => zone is { } value
        ? ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, Encoding.UTF8.GetBytes($"[ZoneTransfer]\r\nZoneId={value}\r\nHostUrl={url}\r\n"))
        : ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
    private static ArchiveOriginEvidenceRecord Record(ArchiveOriginSourceKind kind, string identity, ArchiveOriginSnapshot origin) =>
        new(kind, new(identity, kind == ArchiveOriginSourceKind.Directory ? 0 : 1, Hash(kind == ArchiveOriginSourceKind.Directory ? [] : [1])), origin);
    private static ArchiveOriginEvidenceScope Scope(ArchiveOriginOperation operation, int count) => new(Nonce, new(ArchiveWriteFormat.Tar), operation, count);
    public static IEnumerable<object?[]> ZonePairs() => from left in new int?[] { null, 0, 1, 2, 3, 4 }
        from right in new int?[] { null, 0, 1, 2, 3, 4 } select new object?[] { left, right };

    [Theory]
    [MemberData(nameof(ZonePairs))]
    public void Creation_uses_every_input_and_emits_no_invented_url_while_retaining_both_raw_marks(int? first, int? second)
    {
        using var output = new MemoryStream(); var scope = Scope(ArchiveOriginOperation.Create, 2);
        var left = Record(ArchiveOriginSourceKind.File, "first", Mark(first, "https://a.test/one"));
        var right = Record(ArchiveOriginSourceKind.Directory, "second", Mark(second, "https://b.test/two"));
        ArchiveWindowsOriginAggregate result;
        using (var aggregate = new ArchiveWindowsOriginAggregation(output, scope, TestContext.Current.CancellationToken))
        { aggregate.Add(left); aggregate.Add(right); result = aggregate.Complete(); }
        int? expected = first is null && second is null ? null : Math.Max(first ?? 0, second ?? 0);
        Assert.Equal(expected, ArchiveWindowsOriginZone.Parse(result.RequiredOrigin).DeclaredZone);
        Assert.DoesNotContain("HostUrl", Encoding.UTF8.GetString(result.RequiredOrigin.Bytes));
        output.Position = 0;
        using (var evidence = new ArchiveOriginEvidenceReader(output, result.Evidence, scope, TestContext.Current.CancellationToken))
        {
            Assert.Equal(left.Origin.Digest, evidence.ReadNext()!.Origin.Digest);
            Assert.Equal(right.Origin.Digest, evidence.ReadNext()!.Origin.Digest);
            Assert.Null(evidence.ReadNext()); Assert.True(evidence.IsComplete);
        }
        output.Position = 0; var reviewed = ArchiveWindowsOriginAggregation.Review(output, result.Evidence, scope, TestContext.Current.CancellationToken);
        Assert.Equal(result.RequiredOrigin.Digest, reviewed.RequiredOrigin.Digest);
    }

    [Theory]
    [MemberData(nameof(ZonePairs))]
    public void Updates_preserve_original_url_and_raw_fields_while_never_lowering_either_declaration(int? originalZone, int? addedZone)
    {
        using var output = new MemoryStream(); var scope = Scope(ArchiveOriginOperation.Update, 2);
        var original = Record(ArchiveOriginSourceKind.OriginalArchive, "archive", Mark(originalZone, "https://a.test/original"));
        var added = Record(ArchiveOriginSourceKind.File, "input", Mark(addedZone, "https://b.test/input"));
        using var aggregate = new ArchiveWindowsOriginAggregation(output, scope, TestContext.Current.CancellationToken);
        aggregate.Add(original); aggregate.Add(added); var result = aggregate.Complete();
        int? maximum = originalZone is null && addedZone is null ? null : Math.Max(originalZone ?? 0, addedZone ?? 0);
        Assert.Equal(maximum, ArchiveWindowsOriginZone.Parse(result.RequiredOrigin).DeclaredZone);
        if (originalZone is not null) Assert.Equal(ArchiveWindowsOriginZone.Parse(original.Origin).RaiseTo(maximum!.Value).Bytes.ToArray(), result.RequiredOrigin.Bytes.ToArray());
        else Assert.DoesNotContain("HostUrl", Encoding.UTF8.GetString(result.RequiredOrigin.Bytes));
        output.Position = 0; Assert.Equal(result.RequiredOrigin.Digest,
            ArchiveWindowsOriginAggregation.Review(output, result.Evidence, scope, TestContext.Current.CancellationToken).RequiredOrigin.Digest);
    }

    [Theory]
    [InlineData("unavailable")][InlineData("unsupported")][InlineData("foreign")]
    public void A_high_zone_never_short_circuits_later_failures_or_produces_a_partial_result(string failure)
    {
        using var output = new MemoryStream(); using var aggregate = new ArchiveWindowsOriginAggregation(output,
            Scope(ArchiveOriginOperation.Create, 2), TestContext.Current.CancellationToken);
        aggregate.Add(Record(ArchiveOriginSourceKind.File, "first", Mark(4)));
        var invalid = failure switch
        {
            "unavailable" => ArchiveOriginSnapshot.Unavailable(ArchiveOriginFormat.WindowsZoneIdentifier, ArchiveOriginFailure.InUse),
            "foreign" => ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.LinuxOriginUrl),
            _ => ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, "[ZoneTransfer]\nZoneId=1000"u8),
        };
        Assert.Throws<ArchiveWriteException>(() => aggregate.Add(Record(ArchiveOriginSourceKind.File, "later", invalid)));
        Assert.Throws<ArchiveWriteException>(() => aggregate.Complete());
        Assert.Throws<ArchiveWriteException>(() => aggregate.Add(Record(ArchiveOriginSourceKind.File, "replacement", Mark(0))));
        Assert.True(output.CanWrite); Assert.True(output.Length > 48);
    }

    [Fact]
    public void Empty_creation_and_original_only_update_are_complete_without_fabricated_marks()
    {
        using var create = new MemoryStream(); using var fresh = new ArchiveWindowsOriginAggregation(create,
            Scope(ArchiveOriginOperation.Create, 0), TestContext.Current.CancellationToken);
        Assert.Equal(ArchiveOriginState.Absent, fresh.Complete().RequiredOrigin.State);
        using var update = new MemoryStream(); using var existing = new ArchiveWindowsOriginAggregation(update,
            Scope(ArchiveOriginOperation.Update, 1), TestContext.Current.CancellationToken);
        var original = Mark(3); existing.Add(Record(ArchiveOriginSourceKind.OriginalArchive, "archive", original));
        Assert.Same(original, existing.Complete().RequiredOrigin);
        Assert.Throws<ArchiveWriteException>(() => existing.Complete());
    }

    [Fact]
    public void Missing_reviewed_sources_and_changed_evidence_refuse_instead_of_returning_an_aggregate()
    {
        using var partial = new MemoryStream(); using var missing = new ArchiveWindowsOriginAggregation(partial,
            Scope(ArchiveOriginOperation.Create, 2), TestContext.Current.CancellationToken);
        missing.Add(Record(ArchiveOriginSourceKind.File, "one", Mark(4))); Assert.Throws<ArchiveWriteException>(() => missing.Complete());
        using var output = new MemoryStream(); var scope = Scope(ArchiveOriginOperation.Create, 1); ArchiveWindowsOriginAggregate result;
        using (var complete = new ArchiveWindowsOriginAggregation(output, scope, TestContext.Current.CancellationToken))
        { complete.Add(Record(ArchiveOriginSourceKind.File, "one", Mark(3))); result = complete.Complete(); }
        byte[] corrupt = output.ToArray(); corrupt[^5] ^= 1; using var input = new MemoryStream(corrupt);
        Assert.Throws<ArchiveWriteException>(() => ArchiveWindowsOriginAggregation.Review(input, result.Evidence, scope, TestContext.Current.CancellationToken));
    }
}
