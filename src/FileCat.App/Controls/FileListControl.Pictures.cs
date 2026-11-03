using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Content;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Controls;

public sealed partial class FileListControl
{
    private const long MaxAutomaticThumbnailBytes = 64L * 1024 * 1024;
    // Keep a normal photo folder warm when the user scrolls back. Bound both the
    // number of answers and their decoded pixels so very large folders stay finite.
    private const int MaxCachedThumbnails = 512;
    private const long MaxCachedThumbnailBytes = 96L * 1024 * 1024;
    private readonly SemaphoreSlim _thumbnailSlots = new(2);
    private readonly Dictionary<ThumbnailKey, ThumbnailValue> _pictureCache = new();
    private readonly Dictionary<ThumbnailKey, ThumbnailRequest> _picturePending = new();
    private HashSet<ThumbnailKey> _pictureDemand = [];
    private int _pictureColumns = 1;
    private long _pictureGeneration;
    private long _pictureTouch;
    internal int CachedPictureThumbnailCount => _pictureCache.Values.Count(value => value.Bitmap is not null);
    internal int PendingPictureThumbnailCount => _picturePending.Count;
    internal long PictureThumbnailLoadCount { get; private set; }

    private readonly record struct ThumbnailKey(ItemRef Item, long Size, long Modified);
    private sealed record ThumbnailValue(WriteableBitmap? Bitmap, long Touch);
    private sealed class ThumbnailRequest
    {
        public readonly CancellationTokenSource Cancellation = new();
    }

    private double PictureTileHeight => Math.Min(Math.Max(156, 126 + (FontSize > 0 ? FontSize : 13) * 2),
        Math.Max(88, _layoutHeight > 0 ? _layoutHeight : Bounds.Height));
    private double PictureTileWidth => Math.Max(1, _horizontalViewport > 0 ? _horizontalViewport / _pictureColumns : Bounds.Width / _pictureColumns);

    private Rect PictureTileBounds(int row)
    {
        int relative = row - _topRow;
        double width = PictureTileWidth;
        return new Rect(relative % _pictureColumns * width, relative / _pictureColumns * PictureTileHeight,
            width, PictureTileHeight);
    }

    private void RenderPictureGrid(DrawingContext dc, Rect bounds, bool active)
    {
        var listing = _listing;
        if (listing is null) return;
        var demand = new HashSet<ThumbnailKey>();
        var visible = new HashSet<int>();
        int count = listing.VisibleCount;
        int first = Math.Max(0, _topRow);
        int last = Math.Min(count, first + VisibleRowCapacity + _pictureColumns);
        double width = _horizontalViewport > 0 ? _horizontalViewport : bounds.Width;
        using (dc.PushClip(new Rect(0, 0, width, bounds.Height)))
        {
            for (int row = first; row < last; row++)
            {
                int storeIndex = listing.GetStoreIndex(row);
                visible.Add(storeIndex);
                var entry = listing.GetVisible(row);
                var tile = PictureTileBounds(row).Deflate(new Thickness(4));
                bool marked = listing.IsMarked(storeIndex);
                bool focused = row == listing.FocusedIndex;
                dc.DrawRectangle(null, new Pen(_grid, 0.75), tile.Deflate(new Thickness(0.5)));
                if (marked) dc.FillRectangle(_markedBg, tile);
                if (focused && active) dc.FillRectangle(_focusBg, tile);
                if (marked) dc.FillRectangle(_markedText, new Rect(tile.X, tile.Y + 2, 4, tile.Height - 4));
                if (focused)
                    dc.DrawRectangle(null, active ? new Pen(_focusBorder, 1.5)
                        : new Pen(_focusInactive, 1, new DashStyle([2, 2], 0)), tile.Deflate(new Thickness(0.5)));

                double imageWidth = Math.Max(1, Math.Min(220, tile.Width - 20));
                var imageBox = new Rect(tile.Center.X - imageWidth / 2, tile.Y + 7,
                    imageWidth, Math.Max(1, tile.Height - 61));
                dc.FillRectangle(_header, imageBox);
                dc.DrawRectangle(null, new Pen(_grid, 1), imageBox);
                var item = listing.GetItemRef(storeIndex);
                WriteableBitmap? thumbnail = null;
                if (entry.Kind == EntryKind.File && PictureBrowseContext.IsPictureName(entry.Name)
                    && entry.Size <= MaxAutomaticThumbnailBytes && !entry.Has(EntryFlags.Offline))
                {
                    var key = new ThumbnailKey(item, entry.Size, entry.Modified);
                    // Demand tracks every visible picture, including cached ones. Otherwise
                    // cache trimming mistakes a rendered thumbnail for an off-screen entry.
                    demand.Add(key);
                    if (_pictureCache.TryGetValue(key, out var cached))
                    {
                        thumbnail = cached.Bitmap;
                        _pictureCache[key] = cached with { Touch = ++_pictureTouch };
                    }
                }
                if (thumbnail is not null)
                {
                    double scale = Math.Min(imageBox.Width / thumbnail.PixelSize.Width, imageBox.Height / thumbnail.PixelSize.Height);
                    double w = thumbnail.PixelSize.Width * scale, h = thumbnail.PixelSize.Height * scale;
                    var image = new Rect(imageBox.Center.X - w / 2, imageBox.Center.Y - h / 2, w, h);
                    dc.DrawImage(thumbnail, image);
                }
                else if (entry.Kind == EntryKind.Parent)
                {
                    var center = imageBox.Center;
                    var arrow = new Pen(_muted, 2.5);
                    dc.DrawLine(arrow, new Point(center.X, center.Y + 13), new Point(center.X, center.Y - 17));
                    dc.DrawLine(arrow, new Point(center.X, center.Y - 17), new Point(center.X - 12, center.Y - 5));
                    dc.DrawLine(arrow, new Point(center.X, center.Y - 17), new Point(center.X + 12, center.Y - 5));
                }
                else if (Tab?.Services.Icons.GetIcon(entry, Tab.Location) is { } icon)
                {
                    double size = Math.Min(46, Math.Min(imageBox.Width, imageBox.Height - 18));
                    dc.DrawImage(icon, new Rect(imageBox.Center.X - size / 2, imageBox.Center.Y - size / 2 - 8, size, size));
                }
                if (thumbnail is null)
                {
                    string extension = NameParts.GetExtension(entry.Name).ToUpperInvariant();
                    string badge = entry.Kind == EntryKind.Parent ? "UP" : entry.IsContainer ? "FOLDER"
                        : entry.Has(EntryFlags.Offline) ? "ONLINE ONLY"
                        : extension.Length is > 0 and <= 6 ? extension : "FILE";
                    using var label = MakeText(badge, _muted, _typeface, Math.Max(1, imageBox.Width - 8));
                    label.Draw(dc, new Point(imageBox.Center.X - label.Width / 2, imageBox.Bottom - label.Height - 4));
                }

                var nameBrush = marked ? _markedText : focused && active ? _focusText : entry.IsContainer ? _dir : _text;
                using var name = MakeText(Formatters.SafeName(entry.Name), nameBrush,
                    marked ? _boldTypeface : _typeface, Math.Max(1, tile.Width - 16));
                name.Draw(dc, new Point(tile.X + 8, tile.Bottom - 43));
                string detail = entry.Kind == EntryKind.Parent ? "Parent folder" : entry.IsContainer ? "Folder"
                    : entry.Has(EntryFlags.Offline) ? "Open to download"
                    : entry.Size >= 0 ? Formatters.SizeWithUnit(entry.Size) : "File";
                using var secondary = MakeText(detail, _muted, _typeface, Math.Max(1, tile.Width - 16));
                secondary.Draw(dc, new Point(tile.X + 8, tile.Bottom - 23));
            }
            RenderEmptyState(dc, listing, count, width, bounds.Height);
        }
        if (Tab is { } tab) tab.VisibleStoreIndices = visible;
        // Do not open providers or launch decoders while Avalonia is drawing. Only visible tiles demand work.
        long generation = _pictureGeneration;
        Dispatcher.UIThread.Post(() => SyncPictureDemand(demand, generation), DispatcherPriority.Background);
    }

    private void SyncPictureDemand(HashSet<ThumbnailKey> demand, long generation)
    {
        if (!PictureMode || !_viewEventsAttached || generation != _pictureGeneration || Tab is not { } tab) return;
        _pictureDemand = demand;
        foreach (var (key, request) in _picturePending.ToArray())
        {
            if (!demand.Contains(key))
            {
                request.Cancellation.Cancel();
                _picturePending.Remove(key);
            }
        }
        TrimPictureCache();
        foreach (var key in demand)
        {
            if (_pictureCache.ContainsKey(key) || _picturePending.ContainsKey(key)) continue;
            var request = new ThumbnailRequest();
            _picturePending.Add(key, request);
            _ = LoadPictureThumbnailAsync(tab, key, request, generation);
        }
    }

    private async Task LoadPictureThumbnailAsync(TabViewModel tab, ThumbnailKey key, ThumbnailRequest request, long generation)
    {
        WriteableBitmap? bitmap = null;
        bool entered = false;
        try
        {
            var ct = request.Cancellation.Token;
            await _thumbnailSlots.WaitAsync(ct);
            entered = true;
            ct.ThrowIfCancellationRequested();
            PictureThumbnailLoadCount++;
            var provider = tab.Services.Providers.For(key.Item.Parent);
            string deviceKey = provider.GetDeviceKey(key.Item.Parent);
            var source = await ViewerLauncher.ReopenSourceAsync(tab.Services, deviceKey,
                () => provider.OpenContent(key.Item), ct);
            if (source is null) return;
            PagedReader reader;
            try
            {
                if (source.Length > MaxAutomaticThumbnailBytes)
                {
                    source.Dispose();
                    return;
                }
                reader = new PagedReader(source, tab.Services.Io, deviceKey, maxPages: 16);
            }
            catch
            {
                source.Dispose();
                throw;
            }
            using (reader)
            {
                int side = Math.Clamp((int)Math.Ceiling(112 * (TopLevel.GetTopLevel(this)?.RenderScaling ?? 1)), 16, 256);
                bitmap = (await PictureDecoder.DecodeAsync(reader, tab.Services.Io, deviceKey, side, ct)).Bitmap;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException or TimeoutException or ObjectDisposedException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // A failed thumbnail retains its ordinary file icon; opening the file still reports the real error.
        }
        finally
        {
            if (entered) _thumbnailSlots.Release();
            var ready = bitmap;
            Dispatcher.UIThread.Post(() =>
            {
                if (_picturePending.TryGetValue(key, out var pending) && ReferenceEquals(pending, request))
                    _picturePending.Remove(key);
                bool current = generation == _pictureGeneration && PictureMode && ReferenceEquals(tab, Tab)
                    && !request.Cancellation.IsCancellationRequested && _pictureDemand.Contains(key);
                request.Cancellation.Dispose();
                if (!current) { ready?.Dispose(); return; }
                _pictureCache[key] = new ThumbnailValue(ready, ++_pictureTouch);
                TrimPictureCache();
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }
    }

    private void TrimPictureCache()
    {
        // A large viewport can exceed the ordinary cache budget. Never evict an
        // on-screen thumbnail: doing so starts an endless decode/redraw loop.
        long bytes = _pictureCache.Values.Sum(value => value.Bitmap is { } bitmap
            ? (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4 : 0);
        while (_pictureCache.Count > MaxCachedThumbnails || bytes > MaxCachedThumbnailBytes)
        {
            ThumbnailKey? oldestKey = null;
            long oldestTouch = long.MaxValue;
            foreach (var (key, value) in _pictureCache)
            {
                if (_pictureDemand.Contains(key) || value.Touch >= oldestTouch) continue;
                oldestKey = key;
                oldestTouch = value.Touch;
            }
            if (oldestKey is not { } evicted) break;
            _pictureCache.Remove(evicted, out var removed);
            if (removed?.Bitmap is { } bitmap)
                bytes -= (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
            removed?.Bitmap?.Dispose();
        }
    }

    private void ClearPictureThumbnails()
    {
        _pictureGeneration++;
        _pictureDemand.Clear();
        foreach (var request in _picturePending.Values) request.Cancellation.Cancel();
        _picturePending.Clear();
        foreach (var cached in _pictureCache.Values) cached.Bitmap?.Dispose();
        _pictureCache.Clear();
    }
}
