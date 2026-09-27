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
