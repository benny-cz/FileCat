using System.Buffers.Binary;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed class FileDetailsTests
{
    [Fact]
    public void Unsupported_or_unreturned_fields_never_become_known_zero_or_off()
    {
        byte[] statx = new byte[256];
        var summary = UnixFileRecords.ParseLinuxDetails("/synthetic", statx).Summary;
        Assert.Equal(FileObjectKind.Unknown, summary.Kind);
        Assert.Equal(FactState.Unsupported, summary.Allocated.State);
        Assert.Equal(FactState.Unsupported, summary.Compression.State);
        Assert.Equal(FactState.Unsupported, summary.FileEncryption.State);
        Assert.DoesNotContain("Off", summary.Compression.Describe());
        Assert.DoesNotContain("0", summary.Allocated.Describe());
        Assert.Throws<InvalidDataException>(() => UnixFileRecords.ParseLinuxDetails("/synthetic", new byte[255]));
    }

    [Fact]
    public void Statx_masks_allocation_units_kinds_and_attribute_support_are_independent()
    {
        byte[] bytes = new byte[256];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x7FF);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0x81A4);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), 20_000);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 4 | 0x800);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), 4);
        var result = UnixFileRecords.ParseLinuxDetails("/synthetic", bytes);
        Assert.Equal(FileObjectKind.File, result.Summary.Kind);
        Assert.Equal(3584, result.Summary.Allocated.Value);
        Assert.Equal(20_000, result.Summary.LogicalSize.Value);
        Assert.Equal(3u, result.Summary.LinkCount.Value);
        Assert.True(result.Summary.Compression.Value);
        Assert.Equal(FactState.Unsupported, result.Summary.FileEncryption.State);
        Assert.Equal(ReportContentKind.MetadataOnly, result.ContentKind);
        Assert.Contains("Not acquired:", result.ToText());
        Assert.Contains("Names for this file", result.ToText());
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0x41FF);
        Assert.Contains("Native link count", UnixFileRecords.ParseLinuxDetails("/synthetic", bytes).ToText());
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), ulong.MaxValue);
        Assert.Equal(FactState.Failed, UnixFileRecords.ParseLinuxDetails("/synthetic", bytes).Summary.Allocated.State);
    }

    [Fact]
    public void Mac_returned_masks_also_require_complete_packed_slots()
    {
        byte[] bytes = new byte[256];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 140);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 0x80000000 | 2 | 8 | 0x40000 | 0x2000000);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(28), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(120), 2);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(132), 4096);
        var complete = UnixFileRecords.ParseMacDetails("/synthetic", bytes).Summary;
        Assert.Equal(FactState.Known, complete.Compression.State);
        Assert.False(complete.Compression.Value);
        Assert.Equal(4096, complete.Allocated.Value);
        Assert.Equal(FactScope.AllForks, complete.Allocated.Scope);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 110); // advertised flag field, truncated slot
        var shortResult = UnixFileRecords.ParseMacDetails("/synthetic", bytes).Summary;
        Assert.Equal(FactState.Failed, shortResult.Compression.State);
        Assert.Equal(FactState.Failed, shortResult.Allocated.State);
        Assert.Equal(FactState.Known, shortResult.Kind == FileObjectKind.File ? FactState.Known : FactState.Failed);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 257);
        Assert.Throws<InvalidDataException>(() => UnixFileRecords.ParseMacDetails("/synthetic", bytes));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0);
        Assert.Throws<InvalidDataException>(() => UnixFileRecords.ParseMacDetails("/synthetic", bytes));
    }

    [Fact]
    public void Target_navigation_only_accepts_qualified_filesystem_names()
    {
        var summary = new FileDetailsSnapshot(OperatingSystem.IsWindows() ? @"C:\folder\link" : "/folder/link", DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = FileObjectKind.SymbolicLink,
            ReparseTag = ObservedFact<uint>.Known(FileSystem.ReparseLinks.SymbolicLinkTag, FactScope.Entry, "fixture"),
            Target = ObservedFact<FileSystem.StoredLinkTarget>.Known(new("../target", "display must not be used", true), FactScope.Entry, "fixture"),
        };
        Assert.True(FileDetailsTargets.TryResolve(summary, false, out string relative, out bool network));
        Assert.EndsWith("target", relative);
        Assert.False(network);
        if (!OperatingSystem.IsWindows()) return;
        foreach (string invalid in new[] { @"\Device\HarddiskVolume1\file", @"\\.\pipe\name", @"\\?\GLOBALROOT\Device\file", @"\\server\pipe\name", @"C:\file:ads" })
        {
            var item = summary with { Target = summary.Target with { Value = new(invalid, "C:\\spoofed", false) } };
            Assert.False(FileDetailsTargets.TryResolve(item, true, out _, out _), invalid);
        }
        var unc = summary with { Target = summary.Target with { Value = new(@"\??\UNC\server\share\file", "display", false) } };
        Assert.True(FileDetailsTargets.TryResolve(unc, true, out string target, out network));
        Assert.Equal(@"\\server\share\file", target);
        Assert.True(network);
    }

    [Fact]
    public void Historical_ext_flags_do_not_promise_implemented_behavior()
    {
        string ext = UnixFileRecords.LsattrText(7, "ext4");
        Assert.Contains("behavior not implemented by this filesystem", ext);
        Assert.DoesNotContain("secure deletion", ext);
        Assert.DoesNotContain("undeletable", ext);
        Assert.Contains("Behavior not qualified", UnixFileRecords.LsattrText(7));
    }
}
