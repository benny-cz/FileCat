using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

public sealed partial class WindowsHardLinkDetailsTests
{
    private const int MoreData = unchecked((int)0x80000005);

    private static int Names(byte[] output, uint required, int count, string prefix = "ž-")
    {
        int position = 8;
        BinaryPrimitives.WriteUInt32LittleEndian(output, required);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), (uint)count);
        for (int i = 0; i < count; i++)
        {
            string name = prefix + i;
            int bytes = 20 + 2 * name.Length, aligned = (bytes + 7) & ~7;
            Span<byte> entry = output.AsSpan(position, aligned);
            BinaryPrimitives.WriteUInt32LittleEndian(entry, i + 1 == count ? 0u : (uint)aligned);
            BinaryPrimitives.WriteUInt64LittleEndian(entry[8..], (ulong)(84 + i));
            BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], (uint)name.Length);
            Encoding.Unicode.GetBytes(name, entry[20..]);
            position += aligned;
        }
        if (required == 0) BinaryPrimitives.WriteUInt32LittleEndian(output, (uint)position);
        return position;
    }

    [Fact]
    public void The_owned_query_grows_at_most_twice_and_retries_only_within_the_byte_budget()
    {
        int calls = 0;
        var result = WindowsFileRecords.ReadHardLinkNames(buffer =>
        {
            calls++;
            if (calls <= 2) return (MoreData, Names(buffer, (uint)(buffer.Length * 2), 1));
            return (0, Names(buffer, 0, 2));
        }, TestContext.Current.CancellationToken);
        Assert.Equal(3, calls);
        Assert.Equal(FactState.Known, result.State);
        Assert.Equal(new[] { "ž-0", "ž-1" }, result.Value!.Select(link => link.Leaf));

        calls = 0;
        var limit = WindowsFileRecords.ReadHardLinkNames(buffer =>
        {
            calls++;
            return (MoreData, Names(buffer, uint.MaxValue, 1));
        }, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        Assert.Equal(FactState.Partial, limit.State);
        Assert.Single(limit.Value!);
        Assert.Contains("1 MiB", limit.Reason);
    }

    [Theory]
    [InlineData(0, FactState.Known)]
    [InlineData(1, FactState.Partial)]
    public void A_complete_exact_name_cap_is_distinct_from_a_retained_prefix(int extra, FactState expected)
    {
        var result = WindowsFileRecords.ReadHardLinkNames(buffer => buffer.Length == 4096
            ? (MoreData, Names(buffer, 65_536, 1))
            : (0, Names(buffer, 0, WindowsFileRecords.HardLinkNameLimit + extra)), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.State);
        Assert.Equal(WindowsFileRecords.HardLinkNameLimit, result.Value!.Count);
    }

    [Theory]
    [InlineData("name overflow")]
    [InlineData("bad UTF-16")]
    [InlineData("escaping leaf")]
    public void Malformed_suffix_keeps_only_the_validated_names(string kind)
    {
        var result = WindowsFileRecords.ReadHardLinkNames(buffer =>
        {
            int bytes = Names(buffer, 0, 2);
            int next = 8 + (int)BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(8));
            if (kind == "name overflow") BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(next + 16), uint.MaxValue);
            if (kind == "bad UTF-16") BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(next + 20), 0xd800);
            if (kind == "escaping leaf") BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(next + 20), '\\');
            return (0, bytes);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(FactState.Partial, result.State);
        Assert.Equal("ž-0", Assert.Single(result.Value!).Leaf);
    }

    [Fact]
    public void Cancellation_before_a_resize_prevents_the_next_native_query()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        int calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => WindowsFileRecords.ReadHardLinkNames(buffer =>
        {
            calls++;
            stop.Cancel();
            return (MoreData, Names(buffer, 8192, 1));
        }, stop.Token));
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Full_NTFS_report_reads_actual_names_and_observed_volume_relative_paths(bool many)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_TEST_STORAGE_UI") == "1",
            "Disposable Windows NTFS hard-link fixture.");
        string root = Directory.CreateTempSubdirectory("filecat-hard-link-details-").FullName;
        try
        {
            string first = Directory.CreateDirectory(Path.Join(root, "first")).FullName;
            string second = Directory.CreateDirectory(Path.Join(root, "second")).FullName;
            string source = Path.Join(first, "original.bin"), alias = Path.Join(second, "čáp-界.bin");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            Assert.True(CreateLink(WindowsFileOperations.Long(alias), WindowsFileOperations.Long(source), 0));
            if (many)
                for (int i = 0; i < 64; i++)
                    Assert.True(CreateLink(WindowsFileOperations.Long(Path.Join(second, i + "-" + new string('x', 120))), WindowsFileOperations.Long(source), 0));
            var reader = new WindowsFileRecords();
            var ct = TestContext.Current.CancellationToken;
            Assert.DoesNotContain("Hard-link names", reader.ReadSummary(source, ct).ToReport().ToText());
            var full = reader.ReadFull(source, FileDetailsPolicy.MetadataOnly, ct);
            var links = Assert.Single(full.Technical.Sections, section => section.Title == "Hard-link names");
            Assert.True(links.Fields.Contains(("Inventory", "Complete")), string.Join(" ", links.Lines));
            Assert.Contains(("Returned names", many ? "66" : "2"), links.Fields);
            if (!many)
            {
                string relativeAlias = alias[(Path.GetPathRoot(alias)!.Length - 1)..];
                Assert.Contains(links.Table!.Rows, row => JsonSerializer.Deserialize<string>(row[0]) == "čáp-界.bin" &&
                    row[2] != "Unavailable" && JsonSerializer.Deserialize<string>(row[2]) == relativeAlias);
            }
            else Assert.Equal(40, links.Table!.Rows.Count);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateLink(string newName, string existing, nint attributes);
}
