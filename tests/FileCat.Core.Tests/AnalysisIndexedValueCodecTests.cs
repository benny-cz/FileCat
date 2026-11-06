using System.Buffers.Binary;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisIndexedValueCodecTests
{
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-11-06T04:05:06+05:30");
    private static AnalysisObject Object => new("object", "scope/identity", AnalysisMeasurement.Known(UInt128.MaxValue), AnalysisMeasurement.Known(2), Time, "observed-version", 1000);
    private static StorageFact Fact => new("fact", "object", null, "component", "basis", AnalysisMeasurement.Known(UInt128.MaxValue), "native-method", Time, ["included-component"], false);

    [Theory]
    [InlineData(MeasurementState.Known)]
    [InlineData(MeasurementState.Pending)]
    [InlineData(MeasurementState.Unsupported)]
    [InlineData(MeasurementState.Excluded)]
    [InlineData(MeasurementState.Failed)]
    [InlineData(MeasurementState.Changed)]
    public void ExactUnsignedValuesAndMissingStatesStaySeparateInObjectsAndFacts(MeasurementState state)
    {
        var measure = state == MeasurementState.Known ? AnalysisMeasurement.Known(UInt128.MaxValue) : AnalysisMeasurement.Missing(state);
        var value = Object with { LogicalLength = measure }; var decoded = AnalysisIndexedValueCodec.DecodeObject(AnalysisIndexedValueCodec.EncodeObject(value));
        Assert.Equal(value, decoded); Assert.Equal(value.ModifiedUtc!.Value.Offset, decoded.ModifiedUtc!.Value.Offset);
        var fact = Fact with { Measurement = measure }; var actual = AnalysisIndexedValueCodec.DecodeFact(AnalysisIndexedValueCodec.EncodeFact(fact));
        Assert.Equal(fact with { Includes = [] }, actual with { Includes = [] }); Assert.Equal(fact.Includes, actual.Includes);
        Assert.Equal(fact.ObservedUtc.Offset, actual.ObservedUtc.Offset);
        var issue = new AnalysisIssue("captured-boundary", ulong.MaxValue, state) { RootId = "root" };
        Assert.Equal(issue, AnalysisIndexedValueCodec.DecodeIssue(AnalysisIndexedValueCodec.EncodeIssue(issue)));
    }

    [Fact]
    public void KnownZeroRemainsKnownAndDoesNotBecomeUnsupportedOrPending()
    {
        var expected = Object with { LogicalLength = AnalysisMeasurement.Known(0), LinkCount = AnalysisMeasurement.Missing(MeasurementState.Unsupported), ModifiedUtc = null, VersionEvidence = null };
        Assert.Equal(expected, AnalysisIndexedValueCodec.DecodeObject(AnalysisIndexedValueCodec.EncodeObject(expected)));
        var directory = Fact with { ObjectId = null, DirectoryId = ulong.MaxValue, Includes = [] };
        Assert.Equal(directory, AnalysisIndexedValueCodec.DecodeFact(AnalysisIndexedValueCodec.EncodeFact(directory)));
        var issue = new AnalysisIssue("scope-only") { RootId = "root" };
        Assert.Equal(issue, AnalysisIndexedValueCodec.DecodeIssue(AnalysisIndexedValueCodec.EncodeIssue(issue)));
    }

    [Fact]
    public void EveryTruncationWrongKindAndTrailingBytesFailClosed()
    {
        byte[] obj = AnalysisIndexedValueCodec.EncodeObject(Object), fact = AnalysisIndexedValueCodec.EncodeFact(Fact), issue = AnalysisIndexedValueCodec.EncodeIssue(new("issue", 1));
        for (int n = 0; n < obj.Length; n++) Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeObject(obj.AsSpan(0, n)));
        for (int n = 0; n < fact.Length; n++) Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeFact(fact.AsSpan(0, n)));
        for (int n = 0; n < issue.Length; n++) Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeIssue(issue.AsSpan(0, n)));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeObject(fact));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeFact(issue));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeIssue(obj));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeObject([.. obj, 0]));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeFact([.. fact, 0]));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeIssue([.. issue, 0]));
        BinaryPrimitives.WriteInt32LittleEndian(obj.AsSpan(4), int.MaxValue);
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeObject(obj));
    }

    [Fact]
    public void AliasesAreNotSilentlyDiscardedAndContradictoryOwnersMeasurementsAndReferencesAreRefused()
    {
        foreach (var value in new[] { Object with { ObservedAliases = [1] }, Object with { ObservedAliases = default }, Object with { LogicalLength = default },
            Object with { LinkCount = AnalysisMeasurement.Missing((MeasurementState)255) }, Object with { ModifiedPrecisionTicks = 0 }, Object with { Id = "bad\0id" }, Object with { VerifiedIdentity = "bad\ud800" } })
            Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.EncodeObject(value));
        foreach (var value in new[] { Fact with { DirectoryId = 1 }, Fact with { ObjectId = null }, Fact with { ObjectId = null, DirectoryId = 0 },
            Fact with { Includes = ["duplicate", "duplicate"] }, Fact with { Includes = default }, Fact with { Measurement = default }, Fact with { Method = "" } })
            Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.EncodeFact(value));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.EncodeIssue(new("issue", 0)));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.EncodeIssue(new("issue", 1, (MeasurementState)255)));
    }

    [Fact]
    public void InclusionCountAndTextBoundsAreAdmittedBeforeAllocation()
    {
        string[] names = Enumerable.Range(0, AnalysisLimits.Components).Select(i => "component-" + i).ToArray();
        var value = Fact with { Includes = [.. names] }; var decoded = AnalysisIndexedValueCodec.DecodeFact(AnalysisIndexedValueCodec.EncodeFact(value));
        Assert.Equal(names, decoded.Includes);
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.EncodeFact(value with { Includes = [.. names, "one-more"] }));
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.EncodeObject(Object with { VerifiedIdentity = new string('x', 4097) }));
        byte[] issue = AnalysisIndexedValueCodec.EncodeIssue(new("issue", null)); issue[4] = 0xff; issue[5] = 0xff; issue[6] = 0xff; issue[7] = 0x7f;
        Assert.Throws<InvalidDataException>(() => AnalysisIndexedValueCodec.DecodeIssue(issue));
    }
}
