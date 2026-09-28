using FileCat.Core.Content;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class HexPatchOverlayTests
{
    [Fact]
    public void Sparse_multi_terabyte_offsets_keep_length_and_undo_exactly()
    {
        const long length = 3L * 1024 * 1024 * 1024 * 1024 + 19;
        using var overlay = new HexPatchOverlay(new SparseSource(length));
        long first = 7, far = length - 3;
        overlay.Write(first, [0xA1, 0xB2]);
        overlay.Write(far, [0xC3, 0xD4]);
        Assert.Equal(length, overlay.Length);
        Assert.Equal(4, overlay.DirtyBytes);
        var ranges = overlay.SnapshotRanges();
        Assert.Equal([first, far], ranges.Select(r => r.Offset));
        Span<byte> bytes = stackalloc byte[2];
        Assert.Equal(2, overlay.Read(far, bytes));
        Assert.Equal(new byte[] { 0xC3, 0xD4 }, bytes.ToArray());
        Assert.True(overlay.Undo());
        Assert.Equal(2, overlay.DirtyBytes);
        Assert.True(overlay.Redo());
        Assert.Equal(4, overlay.DirtyBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => overlay.Write(length, [1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => overlay.Write(-1, [1]));
    }

    [Fact]
    public void Overlapping_edits_restore_previous_overlay_then_baseline()
    {
        using var overlay = new HexPatchOverlay(new SparseSource(100));
        overlay.Write(10, [1, 2, 3]);
        overlay.Write(11, [8, 9]);
        Assert.Equal(3, overlay.DirtyBytes);
        Assert.True(overlay.Undo());
        Span<byte> bytes = stackalloc byte[3];
        Assert.Equal(3, overlay.Read(10, bytes));
        Assert.Equal(new byte[] { 1, 2, 3 }, bytes.ToArray());
        Assert.True(overlay.Undo());
        Assert.Empty(overlay.SnapshotRanges());
        Assert.True(overlay.Redo());
        Assert.Equal(3, overlay.DirtyBytes);
    }

    [Fact]
    public void Changes_report_their_range_and_undo_says_where_it_changed()
    {
        using var overlay = new HexPatchOverlay(new SparseSource(100));
        var seen = new List<HexOverlayChange>();
        overlay.Changed += seen.Add;
        overlay.Write(40, [7, 8]);
        Assert.True(overlay.Undo(out var undone));
        Assert.Equal(40, undone.Offset);
        Assert.Equal(new byte[] { 0, 0 }, undone.Bytes);
        Assert.True(overlay.Redo(out var redone));
        Assert.Equal(new byte[] { 7, 8 }, redone.Bytes);
        Assert.Equal(3, seen.Count);
        Assert.Equal(new byte[] { 7, 8 }, seen[0].Bytes);
        overlay.AcceptSave();
        Assert.Null(seen[^1].Bytes); // save-state only
        Assert.False(overlay.Undo(out _));
    }

    [Fact]
    public void Paged_reader_patches_cached_pages_and_discards_loads_started_before_an_edit()
    {
        var content = new byte[PagedReader.PageSize * 2];
        var gate = new ManualResetEventSlim(true);
        var source = new GatedSource(content, gate);
        using var reader = new PagedReader(source);
        var buffer = new byte[4];
        Assert.Equal(4, reader.Read(0, buffer)); // page 0 cached
        content[1] = 0x11;
        reader.Overwrite(1, [0x11]);
        Assert.True(reader.TryRead(0, buffer, out int n));
        Assert.Equal(4, n);
        Assert.Equal(0x11, buffer[1]); // patched in place, no reload

        // A background load of page 1 blocks; an edit lands meanwhile; the stale page must not be cached.
        gate.Reset();
        var loaded = new ManualResetEventSlim();
        reader.PageLoaded += loaded.Set;
        Assert.False(reader.TryRead(PagedReader.PageSize, buffer, out _));
        Assert.True(SpinWait.SpinUntil(() => source.Waiting, 5000));
        content[PagedReader.PageSize] = 0x22;
        reader.Overwrite(PagedReader.PageSize, [0x22]);
        source.ServeStale = true; // the blocked read returns what it saw before the edit
        gate.Set();
        Assert.True(loaded.Wait(5000, TestContext.Current.CancellationToken));
        source.ServeStale = false;
        if (reader.TryRead(PagedReader.PageSize, buffer, out _)) Assert.Equal(0x22, buffer[0]);
        Assert.Equal(1, reader.Read(PagedReader.PageSize, buffer.AsSpan(0, 1)));
        Assert.Equal(0x22, buffer[0]);
    }

    private sealed class GatedSource(byte[] content, ManualResetEventSlim gate) : IContentSource
    {
        public volatile bool Waiting;
        public volatile bool ServeStale;
        public string DisplayName => "gated";
        public long Length => content.Length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public int Read(long offset, Span<byte> buffer)
        {
            var snapshot = content.ToArray();
            Waiting = !gate.IsSet;
            gate.Wait();
            Waiting = false;
            var from = ServeStale ? snapshot : content;
            int n = (int)Math.Min(buffer.Length, from.Length - offset);
            from.AsSpan((int)offset, n).CopyTo(buffer);
            return n;
        }
        public ContentRevision? GetRevision() => new(content.Length, 0);
        public void Dispose() { }
    }

    private sealed class SparseSource(long length) : IContentSource
    {
        public string DisplayName => "synthetic";
        public long Length => length;
        public bool CanSeek => true;
        public string? LocalPath => null;
        public int Read(long offset, Span<byte> buffer)
        {
            int n = (int)Math.Min(buffer.Length, Math.Max(0, length - offset));
            buffer[..n].Clear();
            return n;
        }
        public ContentRevision? GetRevision() => new(length, 0);
        public void Dispose() { }
    }
}
