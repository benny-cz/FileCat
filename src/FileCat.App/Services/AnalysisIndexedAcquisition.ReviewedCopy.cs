using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Platform.Windows;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.App.Services;

internal sealed record AnalysisReviewedCopyRow(ulong PathId, string DisplayPath, string? Name, long Bytes, int Streams, string? Refusal);

internal sealed partial class AnalysisIndexedAcquisition
{
    internal async Task<ReviewedCopy> PrepareReviewedCopyAsync(SessionIdentity identity, ImmutableHashSet<ulong> paths,
        string folder, CancellationToken cancellation)
    {
        if (paths.Count is 0 or > ReviewedAnalysisCopyOperation.MaximumEntries)
            throw new ArgumentException($"Review between 1 and {ReviewedAnalysisCopyOperation.MaximumEntries} captured entries.");
        AnalysisExportDestination.ValidateFolder(folder);
        if (AnalysisProtectedObjects.IsSystemManagedLocation(folder))
            throw new NotSupportedException("Choose an output folder outside system locations.");
        AnalysisSession session;
        IDisposable work;
        string[] locations;
        lock (_gate)
        {
            if (!CanReviewContentReads) throw new NotSupportedException("Reviewed copy requires a finished live analysis on local NTFS.");
            session = _session!;
            locations = (string[])_runtimeLocations.Clone();
            work = _imports.Enter();
        }
        using (work)
        {
            var resources = new ReviewedCopyResources(_services, folder);
            bool transferred = false;
            try
            {
                var protectedIds = await _services.AnalysisProtection.CaptureDirectoryIdentitiesAsync(cancellation).ConfigureAwait(false);
                resources.Folder = await AnalysisIo.Run(_services.Io, resources.OutputDevice,
                    _ => new WindowsAnalysisPrivateDirectory.ReviewedOutputFolder(folder, protectedIds), cancellation).ConfigureAwait(false);
                var rows = ImmutableArray.CreateBuilder<AnalysisReviewedCopyRow>(paths.Count);
                foreach (ulong id in paths.Order())
                {
                    cancellation.ThrowIfCancellationRequested();
                    lock (_gate) RequirePrimaryReadRuntime(session);
                    string display = $"Captured entry {id:N0}";
                    try
                    {
                        var item = await PreparePrimaryReadTargetAsync(session, identity, id, cancellation).ConfigureAwait(false);
                        string location = Path.TrimEndingDirectorySeparator(Path.GetFullPath(PrimaryRuntimeLocation(item.Root, locations)));
                        string canonical = item.Root.CanonicalLocator;
                        if (!canonical.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                            !location.Equals(Path.TrimEndingDirectorySeparator(canonical[4..]), StringComparison.OrdinalIgnoreCase))
                            throw new NotSupportedException("Choose a source with a directly bound local NTFS path.");
                        string path = item.Root.Kind == AnalysisKind.Directory ? Path.Join(location, item.RelativePath) : location;
                        display = AnalysisNames.Display(path);
                        if (AnalysisProtectedObjects.IsSystemManagedLocation(path))
                            throw new NotSupportedException("The source is within a protected system location.");
                        var reference = new ItemRef(Location.FileSystem(Path.GetDirectoryName(path)!), item.Target.Name,
                            EntryKind.File, item.Target.Bytes);
                        if (!path.Equals(reference.FileSystemPath, StringComparison.Ordinal))
                            throw new NotSupportedException("The captured source spelling cannot be used as an exact job locator.");
                        string device = PathUtil.GetDeviceKey(location);
                        var held = await AnalysisIo.Run(_services.Io, new AnalysisIoDevices(device, resources.OutputDevice),
                            token => HoldCopySource(location, device, reference, item, resources.Folder!, protectedIds, token),
                            cancellation).ConfigureAwait(false);
                        resources.Sources.Add(id, held);
                        rows.Add(new(id, display, reference.Name, held.Binding.TotalBytes, held.Binding.Streams.Length, null));
                    }
                    catch (AnalysisExportTargetExistsException)
                    {
                        rows.Add(new(id, display, null, 0, 0, "A file with this output name already exists and will be kept."));
                    }
                    catch (Exception error) when (IsCopyReviewRefusal(error) && !AnalysisCloseUnconfirmedException.IsPresent(error))
                    {
                        rows.Add(new(id, display, null, 0, 0, AnalysisNames.Display(error.Message, 320)));
                    }
                }
                cancellation.ThrowIfCancellationRequested();
                lock (_gate) RequirePrimaryReadRuntime(session);
                var review = new ReviewedCopy(this, session, identity, resources, rows.MoveToImmutable());
                transferred = true;
                return review;
            }
            finally
            {
                if (!transferred) await resources.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static bool IsCopyReviewRefusal(Exception error) =>
        error is IOException or NotSupportedException or UnauthorizedAccessException or ArgumentException;

    private static ReviewedCopySource HoldCopySource(string location, string device, ItemRef reference,
        AnalysisPrimaryReadItem item, WindowsAnalysisPrivateDirectory.ReviewedOutputFolder destination,
        IReadOnlySet<string> protectedIds, CancellationToken cancellation)
    {
        destination.RequireNewCopyName(reference.Name);
        var source = OpenPrimarySource(location, item.Root);
        try { return new(device, reference, source, source.ReviewCopy(item.Target, protectedIds, cancellation)); }
        catch { AnalysisIo.CloseOnWorker(source); throw; }
    }

    internal sealed class ReviewedCopy : IAsyncDisposable
    {
        private readonly AnalysisIndexedAcquisition _runtime;
        private readonly AnalysisSession _session;
        private readonly SessionIdentity _identity;
        private ReviewedCopyResources? _resources;

        internal ReviewedCopy(AnalysisIndexedAcquisition runtime, AnalysisSession session, SessionIdentity identity,
            ReviewedCopyResources resources, ImmutableArray<AnalysisReviewedCopyRow> rows)
        {
            _runtime = runtime;
            _session = session;
            _identity = identity;
            _resources = resources;
            Destination = resources.OutputPath;
            Rows = rows;
        }

        internal string Destination { get; }
        internal ImmutableArray<AnalysisReviewedCopyRow> Rows { get; }

        internal async Task<Job> SubmitAsync(ImmutableHashSet<ulong> included, CancellationToken cancellation)
        {
            var resources = Interlocked.Exchange(ref _resources, null) ?? throw new InvalidOperationException("This copy review has ended.");
            bool transferred = false;
            try
            {
                if (included.Count == 0 || !included.IsSubsetOf(resources.Sources.Keys))
                    throw new ArgumentException("Include only files eligible in this review.");
                foreach (ulong id in resources.Sources.Keys.Except(included).ToArray())
                {
                    var skipped = resources.Sources[id];
                    resources.Sources.Remove(id);
                    await resources.ReleaseSourceAsync(skipped).ConfigureAwait(false);
                }
                cancellation.ThrowIfCancellationRequested();
                var eligible = included.Order().Select(id => resources.Sources[id]).ToImmutableArray();
                var plan = new ReviewedAnalysisCopyOperation(Location.FileSystem(resources.OutputPath), eligible
                    .Select(item => new ReviewedAnalysisCopyEntry(item.Reference, item.Binding.TotalBytes)).ToImmutableArray(),
                    (index, token) => resources.CopyAsync(eligible[index], token), resources);
                await RequireCurrentRevisionAsync(cancellation).ConfigureAwait(false);
                await resources.VerifyProtectionAsync(eligible, cancellation).ConfigureAwait(false);
                lock (_runtime._gate) _runtime.RequirePrimaryReadRuntime(_session);
                transferred = true;
                return await _runtime._services.Jobs.SubmitReviewedAnalysisCopyAsync(plan, cancellation).ConfigureAwait(false);
            }
            finally
            {
                if (!transferred) await resources.DisposeAsync().ConfigureAwait(false);
            }
        }

        private async Task RequireCurrentRevisionAsync(CancellationToken cancellation)
        {
            lock (_runtime._gate) _runtime.RequirePrimaryReadRuntime(_session);
            if (_session.DatasetDescriptor is not null)
            {
                await using var dataset = await _session.AcquireDatasetAsync(cancellation).ConfigureAwait(false);
                if (dataset?.Descriptor.Identity != _identity) throw new AnalysisSourceChangedException();
            }
            else
            {
                using var memory = _session.AcquireSnapshot();
                if (memory.Snapshot.Identity != _identity) throw new AnalysisSourceChangedException();
            }
        }

        public ValueTask DisposeAsync() =>
            Interlocked.Exchange(ref _resources, null)?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    internal sealed record ReviewedCopySource(string Device, ItemRef Reference, WindowsAnalysisBackend Source,
        WindowsAnalysisBackend.ReviewedNtfsCopy Binding);

    internal sealed class ReviewedCopyResources(AppServices services, string outputPath) : IAsyncDisposable
    {
        internal string OutputPath { get; } = outputPath;
        internal string OutputDevice { get; } = PathUtil.GetDeviceKey(outputPath);
        internal Dictionary<ulong, ReviewedCopySource> Sources { get; } = [];
        internal WindowsAnalysisPrivateDirectory.ReviewedOutputFolder? Folder { get; set; }

        internal async Task VerifyProtectionAsync(ImmutableArray<ReviewedCopySource> included, CancellationToken cancellation)
        {
            var ids = await services.AnalysisProtection.CaptureDirectoryIdentitiesAsync(cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(services.Io, OutputDevice, _ => { Folder!.VerifyProtection(ids); return true; }, cancellation).ConfigureAwait(false);
            foreach (var item in included)
                await AnalysisIo.Run(services.Io, item.Device, _ => { item.Binding.VerifySourceProtection(ids); return true; }, cancellation).ConfigureAwait(false);
        }

        internal async Task CopyAsync(ReviewedCopySource item, CancellationToken cancellation)
        {
            var ids = await services.AnalysisProtection.CaptureDirectoryIdentitiesAsync(cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(services.Io, new AnalysisIoDevices(item.Device, OutputDevice),
                token => item.Binding.CopyTo(Folder!, item.Reference.Name, ids, token), cancellation).ConfigureAwait(false);
        }

        internal async Task ReleaseSourceAsync(ReviewedCopySource item)
        {
            try { await AnalysisIo.ReleaseScratchAsync(services.Io, item.Device, item.Binding).ConfigureAwait(false); }
            finally { await AnalysisIo.ReleaseAsync(services.Io, item.Source).ConfigureAwait(false); }
        }

        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            foreach (var source in Sources.Values)
                try { await ReleaseSourceAsync(source).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            Sources.Clear();
            if (Folder is { } folder)
            {
                Folder = null;
                try { await AnalysisIo.ReleaseScratchAsync(services.Io, OutputDevice, folder).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            }
            if (failures.Count > 0) throw AnalysisCloseUnconfirmedException.From(new AggregateException(failures));
        }
    }
}
