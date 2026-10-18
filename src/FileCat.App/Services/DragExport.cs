using Avalonia.Platform.Storage;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Operations;
using FileCat.Core.Resources;

namespace FileCat.App.Services;

/// <summary>The complete, captured scope for one drag. No file payload is published until all items convert.</summary>
internal static class DragExport
{
    internal const int MaxItems = 5000;

    internal sealed class Prepared(IReadOnlyList<string> paths, IReadOnlyList<IStorageItem> items,
        IReadOnlyList<string>? staged, string tempRoot) : IDisposable
    {
        private bool _handedOff, _disposed;
        internal IReadOnlyList<string> Paths { get; } = paths;
        internal IReadOnlyList<IStorageItem> Items { get; } = items;
        internal bool IsStaged => staged is not null;

        /// <summary>A receiving application may read staged files after the drag returns; startup sweep retires them.</summary>
        internal void Handoff() => _handedOff = true;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handedOff) return;
            foreach (var item in Items) DisposeStorage(item);
            if (staged is not null) DragStaging.Discard(staged, tempRoot);
        }
    }

    internal static async Task<(Prepared? Transfer, string? Refusal)> PrepareAsync(
        IReadOnlyList<ItemRef> selection, ProviderRegistry providers, IFileSystemOperations fileOperations,
        string tempRoot, Func<string, bool, Task<IStorageItem?>> convert, CancellationToken cancellationToken)
    {
        int count = selection.Count;
        if (count == 0) return (null, null);
        if (count > MaxItems)
            return (null, Refuse(count, $"At most {MaxItems:N0} items can be dragged at once. Copy or move the complete selection with F5 or F6."));

        int local = selection.Count(item => item.FileSystemPath is not null);
        if (local is > 0 && local < count)
            return (null, Refuse(count, $"The selection has {local:N0} local and {count - local:N0} non-file-system items. Copy or extract the complete selection to a folder with F5 first."));

        IReadOnlyList<string>? staged = null;
        IReadOnlyList<string> paths;
        if (local == 0)
        {
            (IReadOnlyList<string>? Paths, string? Refusal) outcome;
            try
            {
                outcome = await Task.Run(() => DragStaging.Stage(selection, providers, fileOperations, tempRoot, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return (null, Refuse(count, "The selected items could not be staged: " + Formatters.SafeName(ErrorText.Describe(ex)) + " Copy them with F5 first."));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                if (outcome.Paths is not null) DragStaging.Discard(outcome.Paths, tempRoot);
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (outcome.Paths is null)
                return (null, Refuse(count, outcome.Refusal ?? "The selected items cannot be staged. Copy them with F5 first."));
            if (outcome.Paths.Count != count)
            {
                DragStaging.Discard(outcome.Paths, tempRoot);
                return (null, Refuse(count, "Staging returned an incomplete selection. Copy all items with F5 first."));
            }
            staged = outcome.Paths;
            paths = staged;
        }
        else paths = selection.Select(item => item.FileSystemPath!).ToArray();

        var items = new List<IStorageItem>(count);
        IStorageItem? currentStorage = null;
        void Discard()
        {
            if (currentStorage is not null) DisposeStorage(currentStorage);
            foreach (var item in items) DisposeStorage(item);
            if (staged is not null) DragStaging.Discard(staged, tempRoot);
        }
        try
        {
            for (int i = 0; i < paths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool folder = staged is null && selection[i].IsContainer;
                currentStorage = await convert(paths[i], folder);
                if (currentStorage is null || folder && currentStorage is not IStorageFolder || !folder && currentStorage is not IStorageFile ||
                    currentStorage.TryGetLocalPath() is not { } received ||
                    !string.Equals(PathUtil.NormalizeForCompare(received), PathUtil.NormalizeForCompare(paths[i]), PathUtil.SafetyComparison))
                {
                    Discard();
                    return (null, Refuse(count, $"The storage provider could not expose “{Formatters.SafeName(paths[i])}” at its requested path. Copy or extract the complete selection with F5 first."));
                }
                items.Add(currentStorage);
                currentStorage = null;
            }
            return (new Prepared(paths, items, staged, tempRoot), null);
        }
        catch (OperationCanceledException)
        {
            Discard();
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Discard();
            return (null, Refuse(count, "The storage provider could not prepare every item: " + Formatters.SafeName(ErrorText.Describe(ex)) + " Copy or extract them with F5 first."));
        }
    }

    private static string Refuse(int count, string reason) =>
        $"No items were dragged from the {count:N0} selected. {reason}";

    private static void DisposeStorage(IStorageItem item)
    {
        try { (item as IDisposable)?.Dispose(); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { }
    }
}
