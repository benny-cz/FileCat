using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using FileCat.Core.HiddenData;

namespace FileCat.Platform.Windows.Tests;

public sealed class HiddenInventoryParserTests
{
    private static byte[] StreamRecord(string name, long size = 0)
    {
        byte[] raw = Encoding.Unicode.GetBytes(name), record = new byte[24 + raw.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(4), (uint)raw.Length);
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(8), size); raw.CopyTo(record, 24); return record;
    }
    [Fact]
    public void Default_stream_is_excluded_but_a_named_stream_called_DATA_is_retained()
    {
        Assert.Empty(WindowsHiddenData.ParseStreamInventory(StreamRecord("::$DATA")).Items);
        Assert.Equal("$DATA", Assert.Single(WindowsHiddenData.ParseStreamInventory(StreamRecord(":$DATA:$DATA")).Items).Name);
        Assert.Equal(HiddenInventoryState.Failed, WindowsHiddenData.ParseStreamInventory(StreamRecord(":bad:$INDEX_ALLOCATION")).State);
        byte[] broken = StreamRecord(":valid:$DATA"); broken[4] = 255;
        Assert.Equal(HiddenInventoryState.Failed, WindowsHiddenData.ParseStreamInventory(broken).State);
    }
    [Fact]
    public void Empty_default_stream_name_is_complete_only_as_a_single_record()
    {
        var emptyDefault = WindowsHiddenData.ParseStreamInventory(StreamRecord(""));
        Assert.Equal(HiddenInventoryState.Complete, emptyDefault.State);
        Assert.Empty(emptyDefault.Items);
        Assert.Equal(HiddenInventoryState.Failed, WindowsHiddenData.ParseStreamInventory(StreamRecord("", -1)).State);

        byte[] unnamed = StreamRecord("");
        byte[] named = StreamRecord(":side:$DATA", 7);
        byte[] chain = new byte[unnamed.Length + named.Length];
        unnamed.CopyTo(chain, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chain, (uint)unnamed.Length);
        named.CopyTo(chain, unnamed.Length);
        Assert.Equal(HiddenInventoryState.Failed, WindowsHiddenData.ParseStreamInventory(chain).State);
    }
    [Fact]
    public void Ea_parser_preserves_raw_flags_and_native_bytes_and_refuses_bad_bounds_and_progress()
    {
        byte[] record = new byte[12]; record[4] = 0x81; record[5] = 1; record[6] = 2; record[8] = 0xff;
        var item = Assert.Single(WindowsHiddenData.ParseEaInventory(record).Items);
        Assert.Equal("ÿ", item.Name); Assert.Equal(0x81, item.NativeFlags); Assert.Equal(HiddenMetadataRole.RequiredMetadata, item.Role);
        Assert.Equal(HiddenInventoryState.Failed, WindowsHiddenData.ParseEaInventory(record.AsSpan(0, 11)).State);
        record[0] = 4;
        Assert.Equal(HiddenInventoryState.Failed, WindowsHiddenData.ParseEaInventory(record).State);
    }

    [Fact]
    public void Combined_inventory_keeps_the_bounded_EA_prefix_and_its_partial_state()
    {
        var streams = new WindowsHiddenData.InventoryPart(Enumerable.Range(0, 1022)
            .Select(i => new HiddenItem($"s{i}", HiddenKind.Stream, 0)).ToImmutableArray(), HiddenInventoryState.Complete);
        var eas = new WindowsHiddenData.InventoryPart(Enumerable.Range(0, 3)
            .Select(i => new HiddenItem($"ea{i}", HiddenKind.NtfsAttribute, 0)).ToImmutableArray(), HiddenInventoryState.Complete, Acquired: true);
        var (items, boundedEa) = WindowsHiddenData.MergeInventoryParts(streams, eas);
        Assert.Equal(HiddenInventory.MaxEntries, items.Length);
        Assert.Equal(["ea0", "ea1"], boundedEa.Items.Select(item => item.Name));
        Assert.Equal(HiddenInventoryState.Partial, boundedEa.State);
        Assert.True(boundedEa.Acquired);
        Assert.Contains("combined 1,024-entry", boundedEa.Problem, StringComparison.Ordinal);

        var failed = WindowsHiddenData.MergeInventoryParts(streams, eas with
        { State = HiddenInventoryState.Failed, Problem = "Native query failed." }).Ea;
        Assert.Equal(HiddenInventoryState.Failed, failed.State);
        Assert.Contains("Native query failed.", failed.Problem, StringComparison.Ordinal);
    }
}
