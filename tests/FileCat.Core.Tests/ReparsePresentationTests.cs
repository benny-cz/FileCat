using System.Buffers.Binary;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class ReparsePresentationTests
{
    [Theory]
    [InlineData(0u, ReparseKind.Unavailable, false)]
    [InlineData(0xA000000Cu, ReparseKind.SymbolicLink, true)]
    [InlineData(0xA0000003u, ReparseKind.MountPoint, true)]
    [InlineData(0xA000001Du, ReparseKind.WslLink, true)]
    [InlineData(0x8000001Bu, ReparseKind.AppAlias, false)]
    [InlineData(0x80000017u, ReparseKind.Wof, false)]
    [InlineData(0x9000F01Au, ReparseKind.Cloud, false)]
    [InlineData(0x80000023u, ReparseKind.Special, false)]
    [InlineData(0x8000FFFFu, ReparseKind.Unknown, false)]
    [InlineData(0xA000FFFFu, ReparseKind.Unknown, false)]
    public void Taxonomy_preserves_tag_and_does_not_use_surrogate_bit_as_link_policy(uint tag, ReparseKind kind, bool link)
    {
        var description = ReparsePresentation.Describe(tag);
        Assert.Equal(tag, description.Tag);
        Assert.Equal(kind, description.Kind);
        Assert.Equal(link, description.HasLinkPresentation);
        Assert.Equal((tag & 0x20000000) != 0, description.IsNameSurrogate);
    }

    [Fact]
    public void Stored_names_and_relative_flag_remain_separate_and_malformed_data_is_refused()
    {
        byte[] sub = Encoding.Unicode.GetBytes("..\\target"), print = Encoding.Unicode.GetBytes("display");
        byte[] data = new byte[12 + sub.Length + print.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2), (ushort)sub.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), (ushort)sub.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), (ushort)print.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), 1);
        sub.CopyTo(data, 12);
        print.CopyTo(data, 12 + sub.Length);
        Assert.Equal(new StoredLinkTarget("..\\target", "display", true), ReparsePresentation.ParseLink(ReparseLinks.SymbolicLinkTag, data));
        for (int length = 0; length < data.Length; length++)
            Assert.Null(ReparsePresentation.ParseLink(ReparseLinks.SymbolicLinkTag, data.AsSpan(0, length)));
        data[2] |= 1; // odd byte length, never silently round down
        Assert.Null(ReparsePresentation.ParseLink(ReparseLinks.SymbolicLinkTag, data));
        data[2] &= 0xFE;
        data[12] = 0; data[13] = 0xD8; // unpaired UTF-16 surrogate
        Assert.Null(ReparsePresentation.ParseLink(ReparseLinks.SymbolicLinkTag, data));
        Assert.Null(ReparsePresentation.ParseLink(0xA000FFFF, data));
    }

    [Fact]
    public void Opaque_vendor_data_and_unsupported_versions_remain_evidence_without_executable_targets()
    {
        byte[] vendor = new byte[28];
        BinaryPrimitives.WriteUInt32LittleEndian(vendor, 0x20000001);
        BinaryPrimitives.WriteUInt16LittleEndian(vendor.AsSpan(4), 4);
        Guid id = Guid.NewGuid(); id.TryWriteBytes(vendor.AsSpan(8));
        var parsed = ReparsePayloadReader.Parse(vendor);
        Assert.Equal(id, parsed.Vendor);
        Assert.Equal(ReparsePayloadState.Unsupported, parsed.State);
        Assert.Equal(4, parsed.RawData.Length);
        for (int length = 0; length < vendor.Length; length++)
            Assert.Equal(ReparsePayloadState.Malformed, ReparsePayloadReader.Parse(vendor.AsSpan(0, length)).State);
        byte[] wof = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(wof, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(wof.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(wof.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(wof.AsSpan(12), 3);
        Assert.Equal(new WofBacking(2, 3), ReparsePayloadReader.ParseData(0x80000017, wof).Wof);
        wof[8] = 99;
        Assert.Equal(ReparsePayloadState.Unsupported, ReparsePayloadReader.ParseData(0x80000017, wof).State);
        byte[] alias = new byte[4 + Encoding.Unicode.GetByteCount("package\0app\0program\0type\0")];
        BinaryPrimitives.WriteUInt32LittleEndian(alias, 3);
        Encoding.Unicode.GetBytes("package\0app\0program\0type\0").CopyTo(alias, 4);
        Assert.Equal("program", ReparsePayloadReader.ParseData(ReparseLinks.AppExecutionAliasTag, alias).Alias?.Executable);
        Assert.Null(ReparsePayloadReader.ParseData(ReparseLinks.AppExecutionAliasTag, alias).Link);
        alias[0] = 4;
        Assert.Equal(ReparsePayloadState.Unsupported, ReparsePayloadReader.ParseData(ReparseLinks.AppExecutionAliasTag, alias).State);
    }

    [Fact]
    public void Tag_survives_spill_mapped_scan_update_and_view_without_using_provider_payload()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "filecat-tags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            using var store = new EntryStore(scratch, 1);
            for (int i = 0; i < 512; i++) store.Append(new EntryData("item" + i, EntryKind.Directory)
                { Attributes = (uint)FileAttributes.ReparsePoint, Flags = EntryFlags.Link, NativeReparseTag = 0x9000F01A });
            Assert.True(store.IsSpilled);
            Assert.Equal(512 * 48 + 2 * Enumerable.Range(0, 512).Sum(i => ("item" + i).Length), store.SpillBytes);
            using var mapped = store.OpenSpillReader(store.Count)!;
            Assert.Equal(0x9000F01Au, mapped.Get(300).NativeReparseTag);
            var row = store[300]; row.NativeReparseTag = ReparseLinks.SymbolicLinkTag;
            store.Update(300, row);
            Assert.Equal(ReparseLinks.SymbolicLinkTag, store[300].NativeReparseTag);
            var hidden = store[301]; hidden.Flags |= EntryFlags.Hidden;
            store.Update(301, hidden);
            using var scan = new EntryStore.Scan(store, store.Count);
            Assert.Equal(ReparseLinks.SymbolicLinkTag, scan.Materialize(300).NativeReparseTag);
            Assert.Equal(EntryFlags.Link, scan.Materialize(300).Flags);
            Assert.Null(scan.Materialize(300).Tag);

            var ct = TestContext.Current.CancellationToken;
            using var order = ExternalViewBuilder.Sort(store, store.Count, new(Descending: true), null, scratch, ct);
            using var view = ExternalViewBuilder.BuildView(store, store.Count, order, null, showHidden: false, scratch, ct);
            Assert.Equal(Enumerable.Range(0, 512).Reverse().Where(index => index != 301), view.Enumerate());
            Assert.Equal(-1, view.GetVisibleIndex(301));
            var visible = scan.Materialize(view.GetStoreIndex(view.GetVisibleIndex(300)));
            Assert.Equal("item300", visible.Name);
            Assert.Equal(ReparseLinks.SymbolicLinkTag, visible.NativeReparseTag);
        }
        finally { Directory.Delete(scratch, true); }
    }
}
