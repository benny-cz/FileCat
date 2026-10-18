using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>
/// File-transfer intent belongs to one clipboard publication, never to remembered path membership.
/// Avalonia's in-process clipboard API returns the exact currently owned transfer on supported desktop
/// backends. Other applications, an application restart and backends without that API safely import Copy.
/// All formats are read from one short-lived data-transfer object; ownership is checked around its reads.
/// </summary>
internal sealed class FileClipboard
{
    private readonly SemaphoreSlim _writes = new(1, 1);
    private long _writeRevision;
    private bool _writePending;
    private Publication? _publication;

    internal enum MoveState { Available, Running, Completed, ReviewRequired }

    internal sealed class Publication(IAsyncDataTransfer transfer, IReadOnlyList<string> paths, bool cut)
    {
        public IAsyncDataTransfer Transfer { get; } = transfer;
        // FileCat resource identities are ordinal (PI-10), including distinct-case Windows directories.
        // Conservative, case-insensitive containment checks must never authorize a destructive operation.
        public HashSet<string> Paths { get; } = paths.Select(NormalizePath).ToHashSet(StringComparer.Ordinal);
        public bool Cut { get; } = cut;
        public MoveState State { get; set; }
        public int CompletedRoots { get; set; }
    }

    internal sealed record Snapshot(
        IReadOnlyList<ItemRef> Files,
        string? Text,
        IAsyncDataTransfer? Owner,
        Publication? Publication,
        long WriteRevision);

    internal sealed class ChangedException() : InvalidOperationException("The clipboard changed while Paste was reading it. Paste again to use its current contents.");

    /// <summary>A newer Copy/Cut invocation supersedes older preparation, before storage conversion awaits.</summary>
    public long BeginWrite()
    {
        _writePending = true;
        return ++_writeRevision;
    }

    public bool IsLatestWrite(long revision) => revision == _writeRevision;

    public void EndWrite(long revision)
    {
        if (IsLatestWrite(revision)) _writePending = false;
    }

    /// <summary>
    /// Serializing native writes prevents a slow older SetDataAsync from replacing a newer publication.
    /// A null result means this request was superseded before publication; false means Copy fallback.
    /// </summary>
    public async Task<bool?> PublishAsync(IClipboard clipboard, long revision, IReadOnlyList<IStorageItem> files, IReadOnlyList<string> paths, bool cut)
    {
        await _writes.WaitAsync();
        try
        {
            if (!IsLatestWrite(revision)) return null;
            var transfer = new DataTransfer();
            foreach (var file in files) transfer.Add(DataTransferItem.CreateFile(file));
            // SetDataAsync transfers disposal responsibility to the clipboard, including on replacement.
            await clipboard.SetDataAsync(transfer);
            if (!IsLatestWrite(revision)) return null;
            bool owned = ReferenceEquals(await CurrentOwnerAsync(clipboard), transfer);
            if (!IsLatestWrite(revision)) return null;
            // Once the user has been told this publication is Copy, later backend behavior cannot upgrade it.
            _publication = new Publication(transfer, paths, cut && owned);
            return !cut || owned;
        }
        finally
        {
            _writes.Release();
        }
    }

    public async Task<Snapshot> ReadAsync(IClipboard clipboard)
    {
        if (_writePending) throw new InvalidOperationException("Copy or Cut is still preparing the clipboard. Paste again when it finishes.");
        long revision = _writeRevision;
        var publication = _publication;
        var owner = await CurrentOwnerAsync(clipboard);
        if (publication is not null && !ReferenceEquals(owner, publication.Transfer))
        {
            // A later same-path publication cannot revive an obsolete Cut, including after text/image data.
            if (ReferenceEquals(_publication, publication)) _publication = null;
            publication = null;
        }

        using var data = await clipboard.TryGetDataAsync();
        var files = data is null ? null : await data.TryGetFilesAsync();
        if (data is not null && data.GetItems(DataFormat.File).Count() != (files?.Length ?? 0))
            throw new InvalidOperationException("Some clipboard files could not be read. Nothing was pasted; copy the complete selection again.");
        var items = new List<ItemRef>();
        if (files is { Length: > 0 })
        {
            foreach (var file in files)
            {
                if (file.TryGetLocalPath() is not { } path || !Path.IsPathFullyQualified(path))
                    throw new InvalidOperationException("The clipboard includes an item without a local file-system path. No items were pasted; copy the complete selection to a folder first.");
                items.Add(ItemRef.ForFileSystemPath(path, file is IStorageFolder ? EntryKind.Directory : EntryKind.File));
            }
        }
        var text = items.Count == 0 && data is not null ? await data.TryGetTextAsync() : null;
        var snapshot = new Snapshot(items, text, owner, publication, revision);
        if (!await IsCurrentAsync(clipboard, snapshot)) throw new ChangedException();
        if (publication is not null &&
            (items.Count != publication.Paths.Count || !publication.Paths.SetEquals(items.Select(i => NormalizePath(i.FileSystemPath!)))))
            throw new ChangedException();
        return snapshot;
    }

    /// <summary>
    /// Recheck after other admission awaits. External data is a Copy snapshot: Avalonia does not expose a
    /// universal external generation counter, so replacing one external clipboard with another never upgrades it to Move.
    /// </summary>
    public async Task<bool> IsCurrentAsync(IClipboard clipboard, Snapshot snapshot)
    {
        if (_writePending || snapshot.WriteRevision != _writeRevision) return false;
        var owner = await CurrentOwnerAsync(clipboard);
        return !_writePending && snapshot.WriteRevision == _writeRevision && ReferenceEquals(owner, snapshot.Owner) &&
            (snapshot.Publication is null || ReferenceEquals(snapshot.Publication, _publication));
    }

    /// <summary>Called synchronously at mutation admission; a double Paste cannot move the same publication twice.</summary>
    public bool TryBeginMove(Publication publication, out string? reason)
    {
        reason = publication.State switch
        {
            MoveState.Running => "These cut items are already being moved. Open Operations (Ctrl+J) to follow that move.",
            MoveState.Completed => "These cut items have already been moved. Select and Cut or Copy the items again for another transfer.",
            MoveState.ReviewRequired => $"The previous move fully completed {publication.CompletedRoots} of {publication.Paths.Count} items. Open Operations (Ctrl+J) to review or retry it, or select the remaining originals and Cut again.",
            _ => null,
        };
        if (reason is not null) return false;
        publication.State = MoveState.Running;
        return true;
    }

    public void SubmissionFailed(Publication publication) => publication.State = MoveState.Available;

    /// <summary>
    /// A failed folder move may already have moved descendants even when no whole root completed.
    /// Never automatically replay the original clipboard after failure/cancel/partial completion.
    /// This updates only this publication, never the clipboard or a newer Cut/Copy.
    /// </summary>
    public void CompleteMove(Publication publication, Job job)
    {
        publication.CompletedRoots = job.CompletedRootCount;
        publication.State = job.State == JobState.Completed && job.CompletedRootCount == publication.Paths.Count
            ? MoveState.Completed : MoveState.ReviewRequired;
    }

    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static async Task<IAsyncDataTransfer?> CurrentOwnerAsync(IClipboard clipboard)
    {
        try { return await clipboard.TryGetInProcessDataAsync(); }
        catch (NotSupportedException) { return null; }
    }
}
