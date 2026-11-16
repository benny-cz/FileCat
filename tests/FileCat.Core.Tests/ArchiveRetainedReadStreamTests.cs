using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class ArchiveRetainedReadStreamTests
{
    [Fact]
    public void Short_reads_use_an_independent_bounded_cursor_and_disposal_never_closes_the_owner()
    {
        byte[] data = [0, 1, 2, 3]; bool readable = true; int calls = 0;
        using var first = View(); using var second = View();
        Assert.False(first.CanWrite); Assert.Equal(0, first.ReadByte()); Assert.Equal(0, second.Position);
        first.Dispose(); Assert.True(readable); Assert.Equal(0, second.ReadByte());
        Assert.Equal(4, second.Seek(0, SeekOrigin.End)); Assert.Equal(-1, second.ReadByte());
        int before = calls; Assert.Throws<IOException>(() => second.Seek(long.MaxValue, SeekOrigin.Current)); Assert.Equal(before, calls);
        Assert.Throws<NotSupportedException>(() => second.WriteByte(1)); Assert.Throws<NotSupportedException>(() => second.SetLength(1));
        readable = false; Assert.False(second.CanRead); Assert.Throws<ArchiveWriteException>(() => second.ReadByte());
        ArchiveRetainedReadStream View() => new(data.Length, (offset, target) =>
        { calls++; int count = Math.Min(1, target.Length); data.AsSpan((int)offset, count).CopyTo(target); return count; }, () => readable);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void An_invalid_or_premature_native_read_is_not_silently_accepted(int returned)
    {
        using var view = new ArchiveRetainedReadStream(1, (_, _) => returned, () => true);
        Assert.Equal(ArchiveWriteReason.SourceChanged, Assert.Throws<ArchiveWriteException>(() => view.ReadByte()).Reason);
    }
}
