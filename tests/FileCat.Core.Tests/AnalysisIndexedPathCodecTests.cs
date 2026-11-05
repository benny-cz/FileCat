using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisIndexedPathCodecTests
{
    private static readonly ImmutableArray<RootBinding> Roots = [new("r", "file", "/source", "/source", null, IdentityAssurance.Unknown, "source") { NameRules = AnalysisNameRules.Unix }];
    private static AnalysisNode Child => new(2, "r", 1, "café 😀.txt", AnalysisKind.File, "object-1", false, true, null)
    {
        ObservedIdentity = "context-scoped-observation",
        Times = new(DateTimeOffset.Parse("2026-11-06T01:02:03+01:30"), DateTimeOffset.Parse("2025-01-02T03:04:05Z"), 100,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"), 10, DateTimeOffset.Parse("2025-02-01T00:00:00Z"), 1000)
    };

    [Theory]
    [InlineData("utf8", false)]
    [InlineData("utf8", true)]
    [InlineData("utf16le", false)]
    [InlineData("utf16le", true)]
    [InlineData("unix-bytes", true)]
    public void ExactNamesFlagsAndObservationTimesSurvivePrivateRecordRoundTrip(string encoding, bool explicitRaw)
    {
        string name = encoding == "utf16le" ? "unpaired-\ud800-end. " : encoding == "unix-bytes" ? "\ufffda.txt" : Child.Name;
        byte[] raw = encoding == "unix-bytes" ? [0xff, (byte)'a', (byte)'.', (byte)'t', (byte)'x', (byte)'t'] : AnalysisRawNames.Encode(encoding, name);
        var node = Child with { Name = name, NameEncoding = encoding, RawName = explicitRaw ? raw.ToImmutableArray() : [] };
        var roots = Roots.SetItem(0, Roots[0] with { NameRules = encoding == "utf16le" ? AnalysisNameRules.Windows : AnalysisNameRules.Unix });
        byte[] bytes = AnalysisIndexedPathCodec.Encode(node, 0, 1, roots);
        var result = AnalysisIndexedPathCodec.Decode(bytes, 2, roots);
        Assert.Equal(1, result.Depth); Assert.Equal(0, result.RootIndex);
        Assert.Equal(node with { RawName = [] }, result.Node with { RawName = [] }); Assert.Equal(node.RawName, result.Node.RawName);
        Assert.Equal(node.Times!.ObservedUtc.Offset, result.Node.Times!.ObservedUtc.Offset);
        Assert.Equal(node.Times.ObservedUtc.Ticks, result.Node.Times.ObservedUtc.Ticks);
        Assert.Equal(AnalysisNames.DisplayName(node), AnalysisNames.DisplayName(result.Node));
    }

    [Fact]
    public void MaximumRawNamesRemainBoundedAndLossless()
    {
        foreach (string encoding in new[] { "utf8", "utf16le", "unix-bytes" })
        {
            byte[] raw = Enumerable.Repeat(encoding == "utf16le" ? (byte)0xd8 : (byte)'x', AnalysisLimits.RawNameBytes).ToArray();
            string name = AnalysisRawNames.Decode(encoding, raw); var node = Child with { Name = name, NameEncoding = encoding, RawName = raw.ToImmutableArray() };
            var bytes = AnalysisIndexedPathCodec.Encode(node, 0, 1, Roots);
            Assert.InRange(bytes.Length, 1, AnalysisScratchVector.MaximumRecordBytes);
            var result = AnalysisIndexedPathCodec.Decode(bytes, 2, Roots); Assert.Equal(name, result.Node.Name); Assert.Equal(raw, result.Node.RawName);
        }
    }

    [Fact]
    public void TruncationsTrailingDataWrongIdentityAndUnboundedRawLengthAreRejected()
    {
        byte[] bytes = AnalysisIndexedPathCodec.Encode(Child, 0, 1, Roots);
        for (int length = 0; length < bytes.Length; length++)
            Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Decode(bytes.AsSpan(0, length), 2, Roots));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Decode([.. bytes, 0], 2, Roots));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Decode(bytes, 3, Roots));
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(31), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Decode(bytes, 2, Roots));
    }

    [Theory]
    [InlineData(20, 32)]
    [InlineData(23, 255)]
    [InlineData(24, 255)]
    [InlineData(25, 3)]
    [InlineData(26, 3)]
    [InlineData(27, 3)]
    [InlineData(28, 2)]
    [InlineData(29, 0)]
    [InlineData(30, 2)]
    public void InvalidTypedTagsDoNotBecomePlausiblePathObservations(int offset, byte value)
    {
        byte[] bytes = AnalysisIndexedPathCodec.Encode(Child, 0, 1, Roots); bytes[offset] = value;
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Decode(bytes, 2, Roots));
    }

    [Fact]
    public void InvalidUnixBytesCannotBeRelabelledAsAnAbsentRawName()
    {
        var node = Child with { Name = "\ufffd", NameEncoding = "unix-bytes", RawName = [0xff] };
        byte[] bytes = AnalysisIndexedPathCodec.Encode(node, 0, 1, Roots); bytes[30] = 1;
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Decode(bytes, 2, Roots));
    }

    [Fact]
    public void BaseRecordRefusesInvalidRelationsAndCannotSilentlyDropIssueReferences()
    {
        var invalid = new[]
        {
            Child with { IssueCodes = ["captured-issue"] }, Child with { IssueCodes = default }, Child with { RawName = default },
            Child with { ParentId = 2 }, Child with { ParentId = ulong.MaxValue }, Child with { RootId = "another-root" },
            Child with { ObjectId = null }, Child with { Kind = AnalysisKind.Directory }, Child with { Name = "../escape" },
            Child with { IncludedByPolicy = false }, Child with { MetadataState = MeasurementState.Failed },
            Child with { ObservedIdentity = "unpaired-\ud800" }, Child with { Times = Child.Times! with { ModifiedPrecisionTicks = 0 } },
            Child with { Times = Child.Times! with { CreatedUtc = null } }
        };
        foreach (var node in invalid) Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Encode(node, 0, 1, Roots));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Encode(Child, 0, 0, Roots));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedPathCodec.Encode(Child, 0, AnalysisLimits.Depth + 1, Roots));
    }
}
