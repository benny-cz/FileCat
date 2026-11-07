using System.Buffers.Binary;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisPathStatisticsTests
{
    [Theory]
    [InlineData("too-many-files")]
    [InlineData("too-many-unknown")]
    [InlineData("known-with-no-known-files")]
    [InlineData("short")]
    [InlineData("trailing")]
    public void DerivedReferenceStatisticsRejectImpossibleOrTruncatedCounts(string corruption)
    {
        var value = new AnalysisPathStatistics(); value.Add(AnalysisMeasurement.Known(1)); value.Add(AnalysisMeasurement.Missing(MeasurementState.Failed));
        byte[] bytes = new byte[AnalysisPathStatistics.EncodedBytes]; AnalysisPathStatistics.Write(bytes, value.Export());
        switch (corruption)
        {
            case "too-many-files": BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), 3); break;
            case "too-many-unknown": BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), 3); break;
            case "known-with-no-known-files": BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), 2); break;
            case "short": bytes = bytes[..^1]; break;
            case "trailing": bytes = [.. bytes, 0]; break;
        }
        Assert.Throws<InvalidDataException>(() => AnalysisPathStatistics.Read(bytes, 2));
    }
}
