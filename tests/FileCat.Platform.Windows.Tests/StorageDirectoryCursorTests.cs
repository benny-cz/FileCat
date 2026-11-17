using System.Buffers.Binary;
using System.Text;

namespace FileCat.Platform.Windows.Tests;

public sealed class StorageDirectoryCursorTests
{
    private static byte[] Entry(string name, uint attributes = 0)
    {
        byte[] text = Encoding.Unicode.GetBytes(name), bytes = new byte[88 + text.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), attributes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), (uint)text.Length);
        bytes[72] = 17; text.CopyTo(bytes, 88); return bytes;
    }
    [Fact]
    public void Exact_names_native_IDs_kinds_and_tags_are_detached_and_case_siblings_stay_distinct()
    {
        byte[] a = Entry("a"), b = Entry("A", (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(68), 0xA000000C);
        byte[] batch = new byte[96 + b.Length]; a.CopyTo(batch, 0); b.CopyTo(batch, 96);
        BinaryPrimitives.WriteUInt32LittleEndian(batch, 96);
        var result = WindowsStorageDirectoryCursor.Parse(batch);
        batch.AsSpan().Clear();
        Assert.Equal(new[] { "a", "A" }, result.Select(e => e.Name)); Assert.False(result[0].IsDirectory); Assert.True(result[1].IsDirectory);
        Assert.Equal("11000000000000000000000000000000", result[0].ObjectId); Assert.Equal(0xA000000Cu, result[1].ReparseTag);
    }
    [Fact]
    public void Bounded_parser_refuses_offsets_truncation_invalid_UTF16_and_non_component_stream_names()
    {
        foreach (string name in new[] { "x:y", @"a\b", "a/b", "x\0y" }) Assert.Throws<IOException>(() => WindowsStorageDirectoryCursor.Parse(Entry(name)));
        var malformed = Entry("x"); malformed[88] = 0; malformed[89] = 0xD8;
        Assert.Throws<IOException>(() => WindowsStorageDirectoryCursor.Parse(malformed));
        var offset = Entry("x"); BinaryPrimitives.WriteUInt32LittleEndian(offset, uint.MaxValue);
        Assert.Throws<IOException>(() => WindowsStorageDirectoryCursor.Parse(offset));
        Assert.Throws<IOException>(() => WindowsStorageDirectoryCursor.Parse(new byte[87]));
        Assert.Throws<IOException>(() => WindowsStorageDirectoryCursor.Parse(new byte[65537]));
        var truncated = Entry("x"); BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(60), 4);
        Assert.Throws<IOException>(() => WindowsStorageDirectoryCursor.Parse(truncated));
        Assert.Empty(WindowsStorageDirectoryCursor.Parse(Entry(".")));
        var noId = Entry("unknown"); noId.AsSpan(72, 16).Clear();
        Assert.Null(Assert.Single(WindowsStorageDirectoryCursor.Parse(noId)).ObjectId);
    }
}
