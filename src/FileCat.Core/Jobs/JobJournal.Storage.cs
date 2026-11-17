using System.Collections.Immutable;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Jobs;

public sealed partial class JobJournal
{
    private StorageTransformJournalWriter? _storageJournal;
    private IDisposable? _storageWriterCharge;
    internal StorageTransformJournalWriter Storage => _storageJournal ?? throw new InvalidOperationException("This is not a storage journal.");
    internal StorageJournalPrefix? StoragePrefix => _storageJournal?.Prefix;

    internal static StorageJournalHeader CaptureStorageHeader(JobRequest request, Guid id, DateTime created)
    {
        if (request.Kind != JobKind.StorageTransform || request.StorageTransform is not { } payload) throw new ArgumentException("Missing storage approval.");
        var paths = StoragePaths.Capture(request.Sources);
        if (payload.Roots.IsDefault || payload.Roots.Length != paths.Length) throw new ArgumentException("Missing storage root mappings.");
        var header = new StorageJournalHeader(id, created, payload.Action, payload.Scope, payload.ReviewRevision,
            paths.Select((path, i) => new StorageJournalRoot(i, payload.Roots[i].PinIndex, path.Path, payload.Roots[i].Observation)).ToImmutableArray())
            { EfsConsent = payload.EfsConsent };
        StorageTransformJournalCollector.ValidateHeader(header);
        _ = StorageTransformJournalWriter.Encode("storage-begin", 1, id, header, StorageJournalPolicy.VersionFor(header)); // strict 256 KiB preflight, no omitted roots
        return header;
    }

    private static JobJournal CreateStorage(string directory, Job job, Func<string, string, CancellationToken, StorageJournalFile>? create)
    {
        var header = CaptureStorageHeader(job.Request, job.Id, job.CreatedUtc);
        StorageJournalFile? owned = null;
        try
        {
            Directory.CreateDirectory(directory);
            string leaf = StorageJournalPolicy.FileName(job.CreatedUtc, job.Id), path = System.IO.Path.Combine(directory, leaf);
            if (create is not null) owned = create(directory, leaf, job.Token);
            else
            {
                // Portable qualification fakes retain the old test route. App's Windows composition supplies the
                // private native creator; this fallback does not qualify a product storage adapter.
                var charge = StorageNativeAllowance.RequireCurrent().ClaimScratch();
                try { owned = new(new(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.None), charge); }
                catch { charge.Dispose(); throw; }
            }
            var stream = owned.Stream;
            var journal = new JobJournal(path, stream) { _storageWriterCharge = owned };
            try
            {
                journal._storageJournal = new(stream, () => stream.Flush(flushToDisk: true), header);
                job.AttachStorageWriter(journal._storageJournal);
                return journal;
            }
            catch { journal.Dispose(); throw; }
        }
        catch { owned?.Dispose(); throw; }
    }

    internal void FinishStorage(StorageResultSnapshot result)
    {
        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Storage.Finish(result);
            _finished = Storage.Collector.Ended;
        }
    }
}
