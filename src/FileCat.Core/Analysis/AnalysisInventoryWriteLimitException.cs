namespace FileCat.Core.Analysis;

/// <summary>The complete inventory cannot fit a requested finite cap; no partial inventory was published.</summary>
internal sealed class AnalysisInventoryWriteLimitException(long maximumBytes)
    : IOException("The complete inventory exceeds the configured byte limit. Existing inventories were kept.")
{
    internal long MaximumBytes { get; } = maximumBytes;
}
