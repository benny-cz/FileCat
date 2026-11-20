using System.Collections.Immutable;
using System.Security.Cryptography;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisComparisonFormat
{
    internal static async Task WriteIndexedAsync(Stream destination, Guid id,
        AnalysisIndexedGraphReader previous, AnalysisIndexedGraphReader current,
        AnalysisDatasetDescriptor before, AnalysisDatasetDescriptor now, AnalysisComparisonView view,
        IReadOnlyDictionary<string, string>? structuralRoots, string previousLabel,
        IReadOnlyDictionary<string, string>? previousRootDisplayIds, IReadOnlyDictionary<string, string>? currentRootDisplayIds,
        DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        if (!destination.CanWrite || id == Guid.Empty) throw new ArgumentException("A writable stream and generated comparison identity are required.");
        AnalysisComparisonView.Validate(view); Label(previousLabel);
        if (previous.Header.Identity != before.Identity || current.Header.Identity != now.Identity)
            throw Invalid("The comparison inputs differ from their retained descriptors.");
        var roots = WireRoots(before.Roots, now.Roots, structuralRoots);
        var beforeRoots = RootDisplayIds(before.Roots, previousRootDisplayIds);
        var nowRoots = RootDisplayIds(now.Roots, currentRootDisplayIds);
        Guid beforeId = Guid.NewGuid(), nowId = Guid.NewGuid();
        using var beforeCount = new Counter(cancellation); using var nowCount = new Counter(cancellation);
        await AnalysisInventoryFormat.WriteDatasetStreamAsync(previous, beforeId, beforeCount, io, devices, cancellation).ConfigureAwait(false);
        await AnalysisInventoryFormat.WriteDatasetStreamAsync(current, nowId, nowCount, io, devices, cancellation).ConfigureAwait(false);
        var taint = AnalysisPrivacy.Join(before.Taint, now.Taint);
        byte[] header = Header(id, beforeId, nowId, beforeCount.Bytes, nowCount.Bytes, DateTimeOffset.UtcNow,
            previousLabel, taint, roots, view, beforeRoots, nowRoots);
        if (beforeCount.Bytes > AnalysisLimits.InventoryBytes - header.Length - nowCount.Bytes - 256)
            throw Invalid("Saved comparison exceeds the private bundle size limit.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var writer = new HashWriter(destination, digest, cancellation);
        writer.Write(header);
        long start = writer.Bytes;
        await AnalysisInventoryFormat.WriteDatasetStreamAsync(previous, beforeId, writer, io, devices, cancellation).ConfigureAwait(false);
        if (writer.Bytes - start != beforeCount.Bytes) throw Invalid("The previous captured input changed during encoding.");
        start = writer.Bytes;
        await AnalysisInventoryFormat.WriteDatasetStreamAsync(current, nowId, writer, io, devices, cancellation).ConfigureAwait(false);
        if (writer.Bytes - start != nowCount.Bytes) throw Invalid("The current captured input changed during encoding.");
        cancellation.ThrowIfCancellationRequested();
        byte[] trailer = Encode(w => { w.WriteString("kind", "comparison-trailer"); w.WriteString("sha256", Convert.ToHexStringLower(digest.GetHashAndReset())); });
        await destination.WriteAsync(trailer, cancellation).ConfigureAwait(false);
    }
}
