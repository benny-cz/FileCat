using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using FileCat.Core.Diagnostics;
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
    private readonly Func<int, IReadOnlyList<string>?> _readWindowsFileDrop;
    private readonly Func<IReadOnlyList<WindowsClipboardFileDrop.VirtualDescriptor>?> _readVirtualDescriptors;
    private readonly Func<uint> _clipboardSequence;
    private long _writeRevision;
    private bool _writePending;
    private Publication? _publication;

    internal FileClipboard(Func<int, IReadOnlyList<string>?>? readWindowsFileDrop = null,
        Func<uint>? clipboardSequence = null,
        Func<IReadOnlyList<WindowsClipboardFileDrop.VirtualDescriptor>?>? readVirtualDescriptors = null)
    {
        _readWindowsFileDrop = readWindowsFileDrop ?? WindowsClipboardFileDrop.TryRead;
        _clipboardSequence = clipboardSequence ?? WindowsClipboardFileDrop.Sequence;
        _readVirtualDescriptors = readVirtualDescriptors ??
            (readWindowsFileDrop is not null || clipboardSequence is not null
                ? () => null : WindowsClipboardFileDrop.TryReadVirtualDescriptors);
    }

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
        long WriteRevision,
        uint? WindowsSequence,
        ClipboardVirtualStaging.Prepared? Staging = null);

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

    public async Task<Snapshot> ReadAsync(IClipboard clipboard, string? tempRoot = null)
    {
        if (_writePending) throw new InvalidOperationException("Copy or Cut is still preparing the clipboard. Paste again when it finishes.");
        long revision = _writeRevision;
        uint windowsSequence = _clipboardSequence();
        var publication = _publication;
        var owner = await CurrentOwnerAsync(clipboard);
        if (publication is not null && !ReferenceEquals(owner, publication.Transfer))
        {
            // A later same-path publication cannot revive an obsolete Cut, including after text/image data.
            if (ReferenceEquals(_publication, publication)) _publication = null;
            publication = null;
        }
        if (publication is { Cut: true, State: not MoveState.Available })
        {
            // A completed move may have removed every source. Native backends can no longer resolve
            // those file URLs, so explain this exact owned publication before attempting conversion.
            // Recheck ownership first: a newer same-path external Copy must never inherit this refusal.
            var guard = new Snapshot([], null, owner, publication, revision,
                windowsSequence == 0 ? null : windowsSequence);
            if (!await IsCurrentAsync(clipboard, guard)) throw new ChangedException();
            throw new InvalidOperationException(MoveUnavailableReason(publication)!);
        }

        var items = new List<ItemRef>();
        var resolvedPaths = new List<string>();
        ClipboardVirtualStaging.Prepared? staging = null;
        var virtualDescriptors = publication is null && windowsSequence != 0
            ? _readVirtualDescriptors() : null;
        int advertisedFiles;
        int resolvedFiles;
        bool useWindowsDrop;
        string? text = null;
        // Some Windows backends keep the shell clipboard open for the lifetime of this data
        // object. Release it before opening CF_HDROP through Win32 below.
        try
        {
            using (var data = await clipboard.TryGetDataAsync())
            {
                var files = data is null ? null : await data.TryGetFilesAsync();
                advertisedFiles = data?.GetItems(DataFormat.File).Count() ?? 0;
                resolvedFiles = files?.Length ?? 0;
                useWindowsDrop = data is not null && advertisedFiles != resolvedFiles;
                if (files is { Length: > 0 })
                {
                    bool hasVirtual = files.Any(file => file.TryGetLocalPath() is not { } path || !Path.IsPathFullyQualified(path));
                    if (hasVirtual)
                    {
                        try
                        {
                            if (publication is not null || files.Any(file => file.TryGetLocalPath() is { } path && Path.IsPathFullyQualified(path))
                                || advertisedFiles != files.Length || tempRoot is null || virtualDescriptors is null)
                                throw new InvalidOperationException("The clipboard includes an item without a local file-system path. No items were pasted; copy the complete selection to a folder first.");
                            staging = await ClipboardVirtualStaging.StageAsync(files, virtualDescriptors, tempRoot);
                            foreach (var path in staging.Paths)
                                items.Add(ItemRef.ForFileSystemPath(path, EntryKind.File));
                            useWindowsDrop = false;
                        }
                        finally
                        {
                            // Avalonia's virtual Shell files hold one indexed OLE stream each. End every
                            // source-side asynchronous operation once its bytes have been consumed.
                            foreach (var file in files) file.Dispose();
                        }
                    }
                    else
                    {
                        foreach (var file in files)
                        {
                            var path = file.TryGetLocalPath()!;
                            resolvedPaths.Add(path);
                            if (!useWindowsDrop)
                                items.Add(ItemRef.ForFileSystemPath(path,
                                    file is IStorageFolder ? EntryKind.Directory : EntryKind.File));
                        }
                    }
                }
                if (!useWindowsDrop && items.Count == 0 && data is not null)
                    text = await data.TryGetTextAsync();
            }
            IReadOnlyList<string>? windowsPaths = null;
            if (useWindowsDrop)
            {
                // The fallback is exclusively for external Copy. A retained in-process Cut must be read
                // through its exact publication; a native path list cannot authorize a destructive Move.
                if (publication is null && windowsSequence != 0 && advertisedFiles > 0)
                    windowsPaths = _readWindowsFileDrop(advertisedFiles);
                bool completeNativeManifest = windowsPaths is { Count: > 0 }
                    && (windowsPaths.Count == advertisedFiles
                        || (windowsPaths.Count + 1 == advertisedFiles
                            && resolvedFiles == windowsPaths.Count))
                    && resolvedPaths.All(path => windowsPaths.Contains(path, StringComparer.Ordinal));
                if (!completeNativeManifest)
                {
                    AppLog.Warn($"Clipboard file conversion incomplete: advertised={advertisedFiles}, resolved={resolvedFiles}, native={windowsPaths?.Count ?? -1}, owned={publication is not null}, nativeSequenceAvailable={windowsSequence != 0}.");
                    throw new InvalidOperationException("Some clipboard files could not be read. Nothing was pasted; copy the complete selection again.");
                }
                if (_clipboardSequence() != windowsSequence) throw new ChangedException();
            }
            if (windowsPaths is not null)
            {
                foreach (var path in windowsPaths)
                {
                    if (!Path.IsPathFullyQualified(path))
                        throw new InvalidOperationException("The clipboard includes an item without a local file-system path. No items were pasted; copy the complete selection to a folder first.");
                    items.Add(ItemRef.ForFileSystemPath(path,
                        Directory.Exists(path) ? EntryKind.Directory : EntryKind.File));
                }
            }
            // Bind every external Windows file import to the native clipboard generation. The shell
            // outcome report must never be sent to a later same-path Cut or Copy publication.
            var snapshot = new Snapshot(items, text, owner, publication, revision,
                publication is null && items.Count > 0 && windowsSequence != 0 ? windowsSequence : null,
                staging);
            if (!await IsCurrentAsync(clipboard, snapshot)) throw new ChangedException();
            if (publication is not null &&
                (items.Count != publication.Paths.Count || !publication.Paths.SetEquals(items.Select(i => NormalizePath(i.FileSystemPath!)))))
                throw new ChangedException();
            return snapshot;
        }
        catch
        {
            staging?.Dispose();
            throw;
        }
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
            (snapshot.WindowsSequence is null || _clipboardSequence() == snapshot.WindowsSequence.Value) &&
            (snapshot.Publication is null || ReferenceEquals(snapshot.Publication, _publication));
    }

    /// <summary>Called synchronously at mutation admission; a double Paste cannot move the same publication twice.</summary>
    public bool TryBeginMove(Publication publication, out string? reason)
    {
        reason = MoveUnavailableReason(publication);
        if (reason is not null) return false;
        publication.State = MoveState.Running;
        return true;
    }

    private static string? MoveUnavailableReason(Publication publication) => publication.State switch
    {
        MoveState.Running => "These cut items are already being moved. Open Operations (Ctrl+J) to follow that move.",
        MoveState.Completed => "These cut items have already been moved. Select and Cut or Copy the items again for another transfer.",
        MoveState.ReviewRequired => $"The previous move fully completed {publication.CompletedRoots} of {publication.Paths.Count} items. Open Operations (Ctrl+J) to review or retry it, or select the remaining originals and Cut again.",
        _ => null,
    };

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
