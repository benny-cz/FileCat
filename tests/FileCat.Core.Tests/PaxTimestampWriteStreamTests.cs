using System.Globalization;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

namespace FileCat.Core.Tests;

public sealed class PaxTimestampWriteStreamTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(65536)]
    public void Fragmented_encoder_records_rewrite_only_mtime_without_losing_framing_or_payload(int chunk)
    {
        byte[] input = ArchiveTimestampTests.WithTimes("1700000000.1234567", "-0.000000001", "0.999999999");
        using var output = new MemoryStream(); using var adapter = new PaxTimestampWriteStream(output, TestContext.Current.CancellationToken);
        adapter.BeginMember(-0.000000001m, 6);
        int bodyEnd = input.Length - 1024;
        for (int offset = 0; offset < bodyEnd; offset += chunk) adapter.Write(input.AsSpan(offset, Math.Min(chunk, bodyEnd - offset)));
        adapter.CompleteMember(); adapter.BeginEndMarkers(true); adapter.Write(input.AsSpan(bodyEnd)); adapter.CompleteArchive();
        Assert.True(output.CanWrite); Assert.False(adapter.CanSeek); output.Position = 0;
        var catalog = StrictTarCatalog.Read(output, TestContext.Current.CancellationToken); var member = Assert.Single(catalog.Members);
        Assert.Equal(-0.000000001m, member.Modified); Assert.Equal(-0.000000001m, member.Accessed); Assert.Equal(0.999999999m, member.Changed);
        output.Position = 0; StrictTarCatalog.CompareDecoder(output, catalog, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("checksum")]
    [InlineData("oversize")]
    [InlineData("binary-size")]
    public void Unexpected_extension_header_poisons_before_body_allocation_or_destination_write(string field)
    {
        byte[] header = ArchiveTimestampTests.WithTimes("0", "0", "0")[..512];
        switch (field)
        {
            case "type": header[156] = (byte)'g'; break;
            case "oversize": ArchiveStrictTarTests.WriteOctal(header.AsSpan(124, 12), ArchiveWritePolicy.MaxEntryMetadataBytes + 1L); break;
            case "binary-size": header[124] = 0x80; break;
        }
        ArchiveStrictTarTests.Checksum(header);
        if (field == "checksum") header[148] ^= 1;
        using var output = new MemoryStream(); using var adapter = new PaxTimestampWriteStream(output, TestContext.Current.CancellationToken);
        adapter.BeginMember(0, 6); long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<ArchiveWriteException>(() => adapter.Write(header));
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 256 << 10);
        Assert.Equal(0, output.Length); Assert.False(adapter.CanWrite);
        Assert.Throws<ArchiveWriteException>(() => adapter.Write([0]));
        Assert.Throws<ArchiveWriteException>(() => adapter.BeginMember(0, 0));
    }

    [Fact]
    public void Truncated_entries_or_extra_member_bytes_cannot_be_completed()
    {
        byte[] bytes = ArchiveTimestampTests.WithTimes("0", "0", "0");
        using var output = new MemoryStream(); using var adapter = new PaxTimestampWriteStream(output, TestContext.Current.CancellationToken);
        adapter.BeginMember(0, 6); adapter.Write(bytes.AsSpan(0, bytes.Length - 1025));
        Assert.Throws<ArchiveWriteException>(() => adapter.CompleteMember()); Assert.False(adapter.CanWrite);
    }
}
