using System.Buffers.Binary;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Records;
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
    public void Full_reparse_buffer_uses_declared_payload_boundary_for_typed_report_text()
    {
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\C:\target");
        byte[] display = Encoding.Unicode.GetBytes(@"C:\target");
        byte[] buffer = new byte[8 + 12 + substitute.Length + display.Length + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, ReparseLinks.SymbolicLinkTag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(buffer.Length - 8 - 4));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6), 7); // Reserved is ignored by the protocol.
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + 2), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + 4), (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + 6), (ushort)display.Length);
        substitute.CopyTo(buffer, 8 + 12);
        display.CopyTo(buffer, 8 + 12 + substitute.Length);

        var parsed = ReparsePayloadReader.Parse(buffer);
        Assert.Equal(ReparsePayloadState.Known, parsed.State);
        Assert.Equal(new StoredLinkTarget(@"\??\C:\target", @"C:\target", false), parsed.Link);
        Assert.Equal("C:\\target", RecordText.ReparseTarget(parsed));
        Assert.Equal(buffer.Length - 8 - 4, parsed.RawData.Length);

        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(buffer.Length - 8 - 6));
        Assert.Equal(ReparsePayloadState.Malformed, ReparsePayloadReader.Parse(buffer).State);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(buffer.Length - 8 + 1));
        Assert.Equal(ReparsePayloadState.Malformed, ReparsePayloadReader.Parse(buffer).State);
        Assert.Equal(ReparsePayloadState.Malformed, ReparsePayloadReader.Parse(buffer.AsSpan(0, 7)).State);
    }

    /// <summary>A complete reparse buffer: tag, ReparseDataLength, reserved, the link header and both names.</summary>
    private static byte[] LinkBuffer(uint tag, string substitute, string print, int padding = 0)
    {
        byte[] sub = Encoding.Unicode.GetBytes(substitute), shown = Encoding.Unicode.GetBytes(print);
        int header = tag == ReparseLinks.SymbolicLinkTag ? 12 : 8;
        byte[] buffer = new byte[8 + header + sub.Length + shown.Length + padding];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4), (ushort)(buffer.Length - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + 2), (ushort)sub.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + 4), (ushort)sub.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(8 + 6), (ushort)shown.Length);
        sub.CopyTo(buffer, 8 + header);
        shown.CopyTo(buffer, 8 + header + sub.Length);
        return buffer;
    }

    [Theory]
    [InlineData(ReparseLinks.SymbolicLinkTag, 0)]
    [InlineData(ReparseLinks.SymbolicLinkTag, 4)]
    [InlineData(ReparseLinks.MountPointTag, 0)]
    [InlineData(ReparseLinks.MountPointTag, 4)]
    public void Odd_or_out_of_range_name_offsets_inside_a_full_length_buffer_are_refused(uint tag, int field)
    {
        byte[] buffer = LinkBuffer(tag, @"\??\C:\target", @"C:\target", padding: 6);
        Assert.Equal(new StoredLinkTarget(@"\??\C:\target", @"C:\target", false), ReparsePayloadReader.Parse(buffer).Link);
        int names = buffer.Length - 8 - (tag == ReparseLinks.SymbolicLinkTag ? 12 : 8);
        int length = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8 + field + 2));
        // Odd, one name-unit past the end, at the end with a non-empty name, beyond the payload and at the 16-bit limit.
        foreach (int offset in new[] { 1, 3, names - length + 1, names - length + 2, names, names + 2, ushort.MaxValue - 1, ushort.MaxValue })
        {
            byte[] damaged = buffer.ToArray();
            BinaryPrimitives.WriteUInt16LittleEndian(damaged.AsSpan(8 + field), (ushort)offset);
            var parsed = ReparsePayloadReader.Parse(damaged);
            Assert.True(parsed.State == ReparsePayloadState.Malformed, $"offset {offset}");
            Assert.Null(parsed.Link);
            Assert.Null(RecordText.ReparseTarget(parsed));
            Assert.Equal(buffer.Length - 8, parsed.RawData.Length); // bounded raw evidence, not a decoded name
            Assert.Null(ReparsePresentation.ParseLink(tag, damaged.AsSpan(8)));
        }
        // A declared name length that runs past the full buffer is refused the same way.
        byte[] overlong = buffer.ToArray();
        int start = BinaryPrimitives.ReadUInt16LittleEndian(overlong.AsSpan(8 + field));
        BinaryPrimitives.WriteUInt16LittleEndian(overlong.AsSpan(8 + field + 2), (ushort)(names - start + 2));
        Assert.Equal(ReparsePayloadState.Malformed, ReparsePayloadReader.Parse(overlong).State);
    }

    [Fact]
    public void Reparse_payloads_above_the_sixteen_KiB_cap_are_refused_without_retaining_their_bytes()
    {
        const int Cap = 16 * 1024;
        byte[] atCap = new byte[Cap];
        LinkBuffer(ReparseLinks.SymbolicLinkTag, @"\??\C:\target", @"C:\target").CopyTo(atCap, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(atCap.AsSpan(4), Cap - 8);
        var accepted = ReparsePayloadReader.Parse(atCap);
        Assert.Equal(ReparsePayloadState.Known, accepted.State);
        Assert.Equal(Cap - 8, accepted.RawData.Length);
        Assert.Equal(@"\??\C:\target", accepted.Link?.SubstituteName);
        Assert.NotNull(ReparsePresentation.ParseLink(ReparseLinks.SymbolicLinkTag, atCap.AsSpan(8)));

        byte[] over = new byte[Cap + 1];
        atCap.CopyTo(over, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(over.AsSpan(4), Cap + 1 - 8);
        foreach (var refused in new[] { ReparsePayloadReader.Parse(over), ReparsePayloadReader.ParseData(ReparseLinks.SymbolicLinkTag, over.AsSpan(8)) })
        {
            Assert.Equal(ReparsePayloadState.Malformed, refused.State);
            Assert.True(refused.RawData.IsEmpty);
            Assert.Null(refused.Link);
        }
        Assert.Null(ReparsePresentation.ParseLink(ReparseLinks.SymbolicLinkTag, over.AsSpan(8)));
        // A buffer larger than the native maximum is refused even when its declared data length would fit.
        BinaryPrimitives.WriteUInt16LittleEndian(over.AsSpan(4), Cap - 8);
        Assert.Equal(ReparsePayloadState.Malformed, ReparsePayloadReader.Parse(over).State);
        byte[] vendor = new byte[Cap + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(vendor, 0x20000001);
        Assert.Equal(ReparsePayloadState.Malformed, ReparsePayloadReader.Parse(vendor).State);
        foreach (uint tag in new[] { ReparseLinks.MountPointTag, 0x80000017u, ReparseLinks.AppExecutionAliasTag, ReparseLinks.WslSymbolicLinkTag, 0x9000F01Au })
        {
            var data = ReparsePayloadReader.ParseData(tag, new byte[Cap - 8 + 1]);
            Assert.Equal(ReparsePayloadState.Malformed, data.State);
            Assert.True(data.RawData.IsEmpty);
        }
    }

    [Theory]
    [InlineData(0x80000023u, "Unix-domain socket")]
    [InlineData(0x80000024u, "WSL FIFO")]
    [InlineData(0x80000025u, "WSL character device")]
    [InlineData(0x80000026u, "WSL block device")]
    public void Wsl_special_file_tags_are_special_objects_never_links(uint tag, string label)
    {
        var description = ReparsePresentation.Describe(tag);
        Assert.Equal(ReparseKind.Special, description.Kind);
        Assert.Equal(label, description.Label);
        Assert.False(description.HasLinkPresentation);
        Assert.False(description.IsNameSurrogate);
        Assert.Null(ReparsePresentation.ParseLink(tag, LinkBuffer(ReparseLinks.SymbolicLinkTag, "target", "target").AsSpan(8)));
        Assert.Equal(ReparsePayloadState.Unsupported, ReparsePayloadReader.ParseData(tag, [1, 2, 3, 4]).State);
        Assert.Null(RecordText.ReparseTarget(tag, [1, 2, 3, 4]));
        if (!OperatingSystem.IsWindows()) return;
        var entry = new EntryData("special", EntryKind.File) { Attributes = (uint)FileAttributes.ReparsePoint, NativeReparseTag = tag, Flags = EntryFlags.Link };
        var origin = Location.FileSystem(Path.GetTempPath());
        Assert.Equal(label, EntryPresentation.Kind(entry, origin));
        Assert.False(EntryPresentation.IsLink(entry, origin));
    }

    [Fact]
    public void Every_cloud_subtag_is_cloud_managed_and_lookalike_tags_are_not()
    {
        for (uint subtag = 0; subtag <= 0xF; subtag++)
        {
            uint tag = 0x9000001A | subtag << 12;
            Assert.True(ReparsePresentation.IsCloudTag(tag), $"0x{tag:X8}");
            var description = ReparsePresentation.Describe(tag);
            Assert.Equal(ReparseKind.Cloud, description.Kind);
            Assert.Equal("Cloud-managed item", description.Label);
            Assert.False(description.HasLinkPresentation);
            Assert.Null(RecordText.ReparseTarget(tag, [1, 0, 0, 0]));
        }
        // Directory bit clear, a bit outside the subtag nibble, another low tag, and the name-surrogate bit.
        foreach (uint lookalike in new[] { 0x8000F01Au, 0x9001001Au, 0x9000F01Bu, 0xB000F01Au, 0x1000F01Au })
        {
            Assert.False(ReparsePresentation.IsCloudTag(lookalike), $"0x{lookalike:X8}");
            Assert.NotEqual(ReparseKind.Cloud, ReparsePresentation.Describe(lookalike).Kind);
        }
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
