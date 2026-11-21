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

    [Theory]
    [InlineData(0u, FactState.Unsupported, FactState.Unsupported)]
    [InlineData(0x200u, FactState.Failed, FactState.Unsupported)]
    [InlineData(0x400u, FactState.Unsupported, FactState.Failed)]
    [InlineData(0x600u, FactState.Failed, FactState.Failed)]
    public void Unreturned_statx_slots_cannot_report_overflow(uint mask, FactState sizeState, FactState allocationState)
    {
        byte[] bytes = new byte[256];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, mask);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), ulong.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48), ulong.MaxValue);
        var summary = UnixFileRecords.ParseLinuxDetails("/synthetic", bytes).Summary;
        Assert.Equal(sizeState, summary.LogicalSize.State);
        Assert.Equal(allocationState, summary.Allocated.State);
        if (sizeState == FactState.Unsupported) Assert.Contains("mask does not include", summary.LogicalSize.Reason);
        if (allocationState == FactState.Unsupported) Assert.Contains("mask does not include", summary.Allocated.Reason);
    }

    [Fact]
    public void Linux_restrictions_describe_only_supported_flags_for_the_held_kind()
    {
        byte[] bytes = new byte[256];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0x81A4);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 0x10_0070);

        ObservedFact<string> Restrictions(ulong mask)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(56), mask);
            return UnixFileRecords.ParseLinuxDetails("/synthetic", bytes).Summary.Restrictions;
        }

        Assert.Equal(FactState.Unsupported, Restrictions(0).State);
        Assert.Equal("Immutable flag set.", Restrictions(0x10).Value);
        Assert.Contains("Append-only flag set.", Restrictions(0x20).Value);
        Assert.Contains("Backup tools may skip this item.", Restrictions(0x40).Value);
        Assert.Contains("fs-verity enabled.", Restrictions(0x10_0000).Value);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28), 0x41FF);
        Assert.Equal(FactState.Unsupported, Restrictions(0x10_0000).State);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 0);
        Assert.Equal("No returned restriction flag is set.", Restrictions(0x70).Value);
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

    [Theory]
    [InlineData(FileObjectKind.Directory, FactState.NotApplicable, 0u, true)]
    [InlineData(FileObjectKind.Directory, FactState.Known, FileSystem.ReparseLinks.MountPointTag, false)]
    [InlineData(FileObjectKind.File, FactState.Known, FileSystem.ReparseLinks.WslSymbolicLinkTag, false)]
    [InlineData(FileObjectKind.Directory, FactState.Failed, 0u, false)]
    [InlineData(FileObjectKind.SymbolicLink, FactState.NotApplicable, 0u, false)]
    public void Windows_target_navigation_requires_a_confirmed_ordinary_entry(FileObjectKind kind, FactState state, uint tag, bool allowed)
    {
        var observed = new FileDetailsSnapshot(@"C:\target", DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = kind,
            ReparseTag = new(state, tag, FactScope.Entry, "fixture"),
        };
        Assert.Equal(allowed, FileDetailsTargets.IsOrdinaryNavigationTarget(observed, windows: true));
    }

    [Fact]
    public void Unix_target_resolution_distinguishes_a_missing_parent_from_an_absent_final_entry()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "Requires Unix native pathname resolution.");
        using var root = new TempDir();
        var ct = TestContext.Current.CancellationToken;
        string canonical = Assert.IsType<string>(FileSystem.UnixFiles.RealPath(root.Path));
        Assert.Equal((Path.Combine(canonical, "missing"), false),
            FileDetailsTargets.ResolveUnixNavigationPath(Path.Combine(root.Path, "missing"), ct));
        Assert.Throws<DirectoryNotFoundException>(() =>
            FileDetailsTargets.ResolveUnixNavigationPath(Path.Combine(root.Path, "missing", "child"), ct));
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
