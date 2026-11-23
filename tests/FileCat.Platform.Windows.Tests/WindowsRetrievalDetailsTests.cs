using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsRetrievalDetailsTests
{
    private static int Page(byte[] output, long first, int count, long? lcn = null)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(output, (uint)count);
        BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(8), first);
        for (int i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(16 + 16 * i), first + i + 1);
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(24 + 16 * i), lcn ?? first + i);
        }
        return 16 + 16 * count;
    }

    [Theory]
    [InlineData(0, FactState.Known)]
    [InlineData(1, FactState.Partial)]
    public void Exact_record_limit_is_distinct_from_truncation(int extra, FactState state)
    {
        int calls = 0;
        var result = WindowsFileRecords.ReadRetrievalPointers((cursor, output) =>
        {
            int left = WindowsFileRecords.RetrievalRecordLimit + extra - (int)cursor;
            int count = Math.Min(512, left);
            calls++;
            return (left > count ? 234 : 0, Page(output, cursor, count));
        }, TestContext.Current.CancellationToken);
        Assert.Equal(8, calls);
        Assert.Equal(state, result.State);
        Assert.Equal(WindowsFileRecords.RetrievalRecordLimit, result.Value!.Count);
    }

    [Theory]
    [InlineData("header only")]
    [InlineData("large count")]
    [InlineData("repeated cursor")]
    [InlineData("moved boundary")]
    [InlineData("overflow")]
    [InlineData("native failure")]
    public void A_failed_later_page_reports_only_the_validated_prefix(string fault)
    {
        int calls = 0;
        var result = WindowsFileRecords.ReadRetrievalPointers((cursor, output) =>
        {
            if (++calls == 1) return (234, Page(output, 0, 1, lcn: -1));
            Assert.Equal(1, cursor);
            return fault switch
            {
                "header only" => (234, Page(output, cursor, 0)),
                "large count" => (234, Page(output, cursor, 2) - 16),
                "repeated cursor" => RepeatEnd(output, cursor),
                "moved boundary" => (0, Page(output, 0, 1)),
                "overflow" => (0, Page(output, cursor, 1, lcn: long.MaxValue)),
                _ => (5, 0),
            };
        }, TestContext.Current.CancellationToken);
        Assert.Equal(FactState.Partial, result.State);
        Assert.Equal(new NtfsRun(0, -1, 1), Assert.Single(result.Value!));
        Assert.NotEmpty(result.Reason!);
        Assert.Equal(2, calls);

        static (int Error, int Returned) RepeatEnd(byte[] output, long cursor)
        {
            int length = Page(output, cursor, 1);
            BinaryPrimitives.WriteInt64LittleEndian(output.AsSpan(16), cursor);
            return (234, length);
        }
    }

    [Fact]
    public void Empty_success_requires_the_requested_starting_VCN()
    {
        var ct = TestContext.Current.CancellationToken;
        var valid = WindowsFileRecords.ReadRetrievalPointers((cursor, output) => (0, Page(output, cursor, 0)), ct);
        Assert.Equal(FactState.Known, valid.State);
        Assert.Empty(valid.Value!);

        var moved = WindowsFileRecords.ReadRetrievalPointers((_, output) => (0, Page(output, 7, 0)), ct);
        Assert.Equal(FactState.Failed, moved.State);
        Assert.Empty(moved.Value!);
        Assert.Contains("previous record boundary", moved.Reason);
    }

    [Fact]
    public void Each_query_advances_and_the_query_count_is_bounded_independently_of_records()
    {
        int calls = 0;
        var result = WindowsFileRecords.ReadRetrievalPointers((cursor, output) =>
        {
            Assert.Equal(calls++, cursor);
            return (234, Page(output, cursor, 1));
        }, TestContext.Current.CancellationToken);
        Assert.Equal(WindowsFileRecords.RetrievalQueryLimit, calls);
        Assert.Equal(FactState.Partial, result.State);
        Assert.Equal(calls, result.Value!.Count);
        Assert.Contains("query inspection limit", result.Reason);
    }

    [Fact]
    public void Cancellation_after_a_completed_page_prevents_the_next_query()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => WindowsFileRecords.ReadRetrievalPointers((cursor, output) =>
        {
            calls++;
            stop.Cancel();
            return (234, Page(output, cursor, 1));
        }, stop.Token));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_record_maps_a_native_NTFS_file_and_leaves_summary_and_directories_without_the_section(bool sparse)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_TEST_STORAGE_UI") == "1",
            "Disposable Windows NTFS file-details fixture.");
        string folder = Directory.CreateTempSubdirectory("filecat-ntfs-record-extents-").FullName;
        try
        {
            const int sparseRanges = 1025;
            Assert.Equal("NTFS", new DriveInfo(Path.GetPathRoot(folder)!).DriveFormat);
            string file = Path.Join(folder, "nonresident.bin");
            var reader = new WindowsFileRecords();
            var ct = TestContext.Current.CancellationToken;
            if (sparse)
            {
                const int stride = 128 * 1024;
                using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                Assert.True(DeviceIoControl(stream.SafeFileHandle, 0x000900C4 /* FSCTL_SET_SPARSE */, 0, 0, 0, 0, out _, 0));
                stream.SetLength((long)sparseRanges * stride);
                for (int i = 0; i < sparseRanges; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    stream.Position = (long)i * stride;
                    stream.WriteByte(0x47);
                }
                stream.Flush(flushToDisk: true);
            }
            else File.WriteAllBytes(file, Enumerable.Repeat((byte)0x47, 256 * 1024).ToArray());
            var summary = reader.ReadSummary(file, ct);
            Assert.DoesNotContain("extent records", summary.ToReport().ToText(), StringComparison.OrdinalIgnoreCase);
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            var extents = Assert.Single(full.Technical.Sections, section => section.Title == "Main-stream extent records");
            Assert.Contains(("Inventory", "Complete"), extents.Fields);
            Assert.NotEmpty(extents.Table!.Rows);
            if (sparse)
            {
                var count = extents.Fields.Single(field => field.Name == "Returned extent records").Value;
                Assert.True(int.Parse(count, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.CurrentCulture) > 1023);
                Assert.Contains(extents.Table.Rows, row => row[2] == "Unallocated or compressed");
                var ranges = Assert.Single(full.Technical.Sections, section => section.Title == "Main-stream allocated byte ranges");
                Assert.True(ranges.Fields.Contains(("Inventory", "Complete")), string.Join(" ", ranges.Lines));
                Assert.Contains(("Returned ranges", sparseRanges.ToString("N0")), ranges.Fields);
                Assert.Contains("These ranges can contain zero bytes.", ranges.Lines);
                Assert.DoesNotContain("allocated byte ranges", summary.ToReport().ToText(), StringComparison.OrdinalIgnoreCase);
            }
            Assert.Equal(summary.Identity, full.Summary.Identity);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.DoesNotContain(reader.ReadFull(folder, FileDetailsPolicy.MetadataOnly, ct).Technical.Sections,
                section => section.Title == "Main-stream extent records");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle file, uint code, nint input, uint inputBytes, nint output,
        uint outputBytes, out uint returned, nint overlapped);
}
