using System.Collections.Immutable;
using System.Runtime.InteropServices;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Tests;

internal static partial class AnalysisObservationCaptureFixture
{
    internal static async Task VerifyNative(string parent, Func<string, IAnalysisBackend> prepare, Func<AnalysisPrivateDirectory> create, CancellationToken cancellation)
    {
        string sourcePath = Path.Combine(parent, "source"); Directory.CreateDirectory(sourcePath); Directory.CreateDirectory(Path.Combine(sourcePath, "folder"));
        string original = Path.Combine(sourcePath, "original.bin"), alias = Path.Combine(sourcePath, "alias.bin");
        byte[] payload = Enumerable.Range(0, 1031).Select(n => (byte)(n % 251)).ToArray(); File.WriteAllBytes(original, payload);
        Assert.True(OperatingSystem.IsWindows() ? WindowsLink(alias, original, IntPtr.Zero) : UnixLink(original, alias) == 0);
        File.WriteAllBytes(Path.Combine(sourcePath, "folder", "empty.txt"), []);
        File.WriteAllBytes(Path.Combine(sourcePath, "café-😀.txt"), [1, 2, 3]);
        using var io = new DeviceIoScheduler(); string sourceDevice = "capture-source-" + Guid.NewGuid().ToString("N"), scratchDevice = "capture-scratch-" + Guid.NewGuid().ToString("N");
        IAnalysisBackend? source = null; AnalysisObservationWriter? writer = null;
        AnalysisDataset? dataset = null; AnalysisDatasetLease? reader = null;
        try
        {
            source = await AnalysisIo.Run(io, sourceDevice, _ => prepare(sourcePath), cancellation);
            var now = DateTimeOffset.UtcNow; var start = source.RootEntry.ObservedUtc ?? now;
            var header = new AnalysisIndexedGraphHeader(new(Guid.NewGuid(), 1, 1, "native-capture-fixture", start, now) { TimeZoneId = "UTC" },
                [source.Root with { Id = "r", Kind = source.RootEntry.Kind, ObservedObjectIdentity = source.RootEntry.Identity }],
                new() { BasisIds = [source.Basis], Limits = AnalysisLimits.EffectivePolicy }, AnalysisPhase.Scanning, NamespaceCoverage.Partial);
            writer = await AnalysisObservationWriter.CreateAsync(io, scratchDevice, create, header.Identity, header.Roots, header.Policy, cancellation);
            ulong root = (await writer.AppendBatchAsync([new(source.RootEntry, 0, "r", source.Basis)], cancellation))[0];
            var pending = new Stack<(AnalysisEntry Entry, ulong Node)>(); pending.Push((source.RootEntry, root));
            var capturedNames = new Dictionary<string, ulong>(StringComparer.Ordinal);
            while (pending.TryPop(out var directory))
            {
                var cursor = await AnalysisIo.Run(io, sourceDevice, _ => source.Children(directory.Entry, cancellation).GetEnumerator(), cancellation);
                await using var cursorOwner = AnalysisIo.OwnCursor(io, sourceDevice, cursor);
                bool finished = false;
                while (!finished)
                {
                    var entries = await AnalysisIo.Run(io, sourceDevice, token =>
                    {
                        source.ValidateReadScope(directory.Entry); var batch = new List<AnalysisEntry>(); int bytes = 0;
                        while (batch.Count < 16 && bytes < 64 * 1024)
                        {
                            token.ThrowIfCancellationRequested(); if (!cursor.MoveNext()) { finished = true; break; }
                            batch.Add(cursor.Current); bytes += cursor.Current.RawName.Length + 1024;
                        }
                        source.ValidateReadScope(directory.Entry); return batch;
                    }, cancellation);
                    if (entries.Count != 0)
                    {
                        var ids = await writer.AppendBatchAsync(entries.Select(entry => new AnalysisObservationItem(entry, directory.Node, "r", source.Basis)).ToImmutableArray(), cancellation);
                        for (int n = 0; n < entries.Count; n++)
                        {
                            var entry = entries[n]; ulong node = ids[n]; capturedNames.Add(entry.Name, node);
                            if (entry.Kind == AnalysisKind.Directory && entry.MetadataState == MeasurementState.Known && entry.IncludedByPolicy && entry.Cloud != true) pending.Push((entry, node));
                        }
                    }
                }
            }
            // Finalization must use captured values after the held source has actually closed.
            await AnalysisIo.ReleaseAsync(io, source); source = null;
            dataset = await writer.CompleteAsync(header.Identity with { Revision = 2, ObservedUtc = DateTimeOffset.UtcNow },
                AnalysisPhase.Finished, NamespaceCoverage.CompleteUnderPolicy, cancellation);
            reader = await dataset.AcquireAsync(cancellation);
            await dataset.DisposeAsync();
            Assert.Equal(6UL, reader.Descriptor.Paths); Assert.Equal(3UL, reader.Descriptor.Objects); Assert.Equal(3UL, reader.Descriptor.Facts);
            var paths = await reader.ReadPathsAsync(0, cancellation: cancellation); Assert.False(paths.HasMore);
            var first = paths.Rows.Single(n => n.Id == capturedNames["original.bin"]); var second = paths.Rows.Single(n => n.Id == capturedNames["alias.bin"]);
            Assert.Equal(first.ObjectId, second.ObjectId); Assert.Equal(2UL, (await reader.ReadAliasesAsync(first.ObjectId!, 0, cancellation: cancellation)).Total);
            var objects = await reader.ReadObjectsAsync(0, cancellation: cancellation); var obj = objects.Rows.Single(o => o.Id == first.ObjectId);
            Assert.Equal((UInt128)payload.Length, obj.LogicalLength.Value); Assert.Equal((UInt128)2, obj.LinkCount.Value); Assert.NotNull(obj.VerifiedIdentity);
            Assert.Equal("café-😀.txt", paths.Rows.Single(n => n.Id == capturedNames["café-😀.txt"]).Name);
            var empty = paths.Rows.Single(n => n.Id == capturedNames["empty.txt"]);
            Assert.Equal(AnalysisMeasurement.Known(0), objects.Rows.Single(o => o.Id == empty.ObjectId).LogicalLength);
            Assert.Equal(payload, File.ReadAllBytes(original)); Assert.Equal(payload, File.ReadAllBytes(alias));
        }
        finally
        {
            if (reader is not null) await reader.DisposeAsync();
            if (dataset is not null) await dataset.DisposeAsync();
            if (writer is not null) await writer.DisposeAsync();
            if (source is not null) await AnalysisIo.ReleaseAsync(io, source);
        }
        Assert.Empty(Directory.GetFiles(Path.Combine(parent, "scratch")));
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WindowsLink(string link, string existing, IntPtr security);
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int UnixLink(string existing, string link);
}
