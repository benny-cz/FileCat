using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public static class AnalysisLimits
{
    public static System.Collections.Immutable.ImmutableSortedDictionary<string, ulong> EffectivePolicy { get; } =
        new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            ["roots"] = Roots, ["observations"] = Observations, ["depth"] = Depth, ["rawNameBytes"] = RawNameBytes,
            ["locatorBytes"] = LocatorBytes, ["batchRecords"] = BatchRecords, ["batchBytes"] = BatchBytes, ["pendingBytes"] = PendingBytes,
            ["memoryBytes"] = MemoryBytes, ["scratchBytes"] = ScratchBytes, ["profileScratchBytes"] = ProfileScratchBytes,
            ["nativeHandles"] = NativeHandles, ["ioRequests"] = IoRequests, ["activeScans"] = ActiveScans,
            ["issues"] = Issues, ["components"] = Components
        }.ToImmutableSortedDictionary(StringComparer.Ordinal);
    public const int Roots = 32, Sessions = 8, ActiveScans = 2, IoRequests = 8, NativeHandles = 128;
    public const int NavigationHistory = 128;
    public const int Observations = 10_000_000, Depth = 256, RawNameBytes = 64 * 1024, LocatorBytes = 256 * 1024;
    public const int BatchRecords = 512, BatchBytes = 256 * 1024, PendingBytes = 8 * 1024 * 1024;
    public const long MemoryBytes = 256L * 1024 * 1024, ScratchBytes = 4L * 1024 * 1024 * 1024;
    public const long ProfileScratchBytes = 8L * 1024 * 1024 * 1024, InventoryBytes = 8L * 1024 * 1024 * 1024;
    public const int ChartLeaves = 5000, Issues = 10_000, Components = 256, QueryBytes = 8192, QueryTerms = 128, QueryDepth = 16;
    public const int RecordBytes = 1024 * 1024, JsonDepth = 16, RecordProperties = 128, InventoryRecords = 50_000_000;
    public const int ReviewPaths = 100_000, ReviewLocatorBytes = 32 * 1024 * 1024;
}
