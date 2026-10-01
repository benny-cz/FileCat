using System.Buffers.Binary;
using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// Windows' Recycle Bin records ($I files), which FileCat's read-only view of the bin parses: both formats, every way a
/// record can be wrong, and damaged records by the thousand. A record is untrusted input (another user's program can
/// write one into a bin), so a wrong one is refused with a reason and never trusted in part.
/// </summary>
public sealed class RecycleBinRecordsTests
{
    private static readonly DateTime Deleted = new(2026, 10, 1, 18, 30, 15, DateTimeKind.Utc);

    internal static byte[] Version2(string path, long size, DateTime deletedUtc)
    {
        var data = new byte[28 + (path.Length + 1) * 2];
        BinaryPrimitives.WriteInt64LittleEndian(data, 2);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedUtc.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(24), path.Length + 1);
        Encoding.Unicode.GetBytes(path).CopyTo(data, 28);
        return data;
    }

    internal static byte[] Version1(string path, long size, DateTime deletedUtc)
    {
        var data = new byte[24 + 520];
        BinaryPrimitives.WriteInt64LittleEndian(data, 1);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(8), size);
        BinaryPrimitives.WriteInt64LittleEndian(data.AsSpan(16), deletedUtc.ToFileTimeUtc());
        Encoding.Unicode.GetBytes(path).CopyTo(data, 24);
        return data;
    }

    [Fact]
    public void Both_formats_give_the_path_size_and_time()
    {
        string path = @"C:\Users\someone\Documents\žluťoučký report.docx";
        var v2 = RecycleBinRecords.Parse(Version2(path, 12_345, Deleted), out var p2);
        Assert.Null(p2);
        Assert.Equal(new RecycleBinRecord(path, 12_345, Deleted, 2), v2);
        var v1 = RecycleBinRecords.Parse(Version1(path, 12_345, Deleted), out var p1);
        Assert.Null(p1);
        Assert.Equal(new RecycleBinRecord(path, 12_345, Deleted, 1), v1);
        // Version 1's path field is fixed: what follows its end is not part of it.
        var dirty = Version1(@"D:\a.txt", 1, Deleted);
        Encoding.Unicode.GetBytes("garbage").CopyTo(dirty, 24 + 20);
        Assert.Equal(@"D:\a.txt", RecycleBinRecords.Parse(dirty, out _)!.OriginalPath);
        // A share's path, as a redirected folder's bin records it.
        Assert.Equal(@"\\server\share\x.txt", RecycleBinRecords.Parse(Version2(@"\\server\share\x.txt", 0, Deleted), out _)!.OriginalPath);
    }

    [Fact]
    public void A_record_that_does_not_hold_together_is_refused_with_a_reason()
    {
        byte[] Changed(byte[] data, Action<byte[]> change)
        {
            var copy = (byte[])data.Clone();
            change(copy);
            return copy;
        }
        var good = Version2(@"C:\x\file.txt", 10, Deleted);
        var cases = new Dictionary<string, byte[]>
        {
            ["empty"] = [],
            ["too short"] = good[..20],
            ["version 0"] = Changed(good, d => BinaryPrimitives.WriteInt64LittleEndian(d, 0)),
            ["version 3"] = Changed(good, d => BinaryPrimitives.WriteInt64LittleEndian(d, 3)),
            ["version -1"] = Changed(good, d => BinaryPrimitives.WriteInt64LittleEndian(d, -1)),
            ["length 0"] = Changed(good, d => BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(24), 0)),
            ["length 1"] = Changed(good, d => BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(24), 1)),
            ["length past the end"] = Changed(good, d => BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(24), 1000)),
            ["length negative"] = Changed(good, d => BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(24), -5)),
            ["length huge"] = Changed(good, d => BinaryPrimitives.WriteInt32LittleEndian(d.AsSpan(24), int.MaxValue)),
            ["version 1, short"] = Version1(@"C:\a", 1, Deleted)[..300],
            ["negative size"] = Changed(good, d => BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(8), -1)),
            ["no time"] = Changed(good, d => BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16), 0)),
            ["a time before 1601"] = Changed(good, d => BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16), -100)),
            ["a time past 9999"] = Changed(good, d => BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16), long.MaxValue)),
            ["a relative path"] = Version2(@"x\file.txt", 1, Deleted),
            ["a drive alone"] = Version2("C:", 1, Deleted),
            ["a server alone"] = Version2(@"\\", 1, Deleted),
            ["an empty path"] = Changed(Version2(@"C:\a", 1, Deleted), d => d.AsSpan(28).Clear()),
        };
        foreach (var (what, data) in cases)
        {
            Assert.Null(RecycleBinRecords.Parse(data, out var problem));
            Assert.False(string.IsNullOrEmpty(problem), what);
        }
    }

    [Fact]
    public void Damaged_records_are_refused_or_read_never_crashed_on()
    {
        int rounds = int.TryParse(Environment.GetEnvironmentVariable("FILECAT_RECYCLE_FUZZ_ROUNDS"), out var r) ? r : 20_000;
        var originals = new[] { Version2(@"C:\Users\a\Documents\report.docx", 4096, Deleted), Version1(@"D:\photos\2024\x.jpg", 1 << 20, Deleted) };
        int read = 0;
        for (int round = 0; round < rounds; round++)
        {
            var random = new Random(round);
            var data = (byte[])originals[round % 2].Clone();
            for (int f = 1 + random.Next(8); f > 0; f--) data[random.Next(data.Length)] = (byte)random.Next(256);
            if (random.Next(8) == 0) data = data[..random.Next(data.Length + 1)];
            if (random.Next(16) == 0) data = [.. data, .. Enumerable.Range(0, random.Next(64)).Select(_ => (byte)random.Next(256))];
            var record = RecycleBinRecords.Parse(data, out var problem);
            Assert.True(record is not null ^ problem is not null, $"round {round}: an answer and a reason at once, or neither");
            if (record is null) continue;
            read++;
            Assert.True(record.Size >= 0 && record.Version is 1 or 2, $"round {round}");
            Assert.Contains('\\', record.OriginalPath);
            Assert.DoesNotContain('\0', record.OriginalPath);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{rounds:N0} damaged records: {read:N0} still read, the rest refused");
    }

    [Fact]
    public void A_records_item_is_stored_under_the_same_name_with_R()
    {
        Assert.Equal("$RAB12CD.txt", RecycleBinRecords.DataName("$IAB12CD.txt"));
        Assert.Equal("$Rabc", RecycleBinRecords.DataName("$iabc"));
        Assert.Null(RecycleBinRecords.DataName("$I"));
        Assert.Null(RecycleBinRecords.DataName("readme.txt"));
    }
}
