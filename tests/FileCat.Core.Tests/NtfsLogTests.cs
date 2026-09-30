using System.Buffers.Binary;
using System.Text;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>D-56: NTFS's $LogFile and $Secure read from bytes laid out as NTFS writes them (update sequences included).</summary>
public sealed class NtfsLogTests
{
    private const int Page = 4096;
    private const int SequenceBits = 30;
    private const int DataOffset = 0x40;

    /// <summary>An LSN naming this byte offset of the log (its low bits are the offset in 8-byte units).</summary>
    private static ulong Lsn(long offset, ulong sequence = 5) => (sequence << (64 - SequenceBits)) | (ulong)(offset >> 3);

    /// <summary>Protects a multi-sector structure as NTFS does: each sector's last two bytes saved in the array and replaced by the check value.</summary>
    private static void Protect(Span<byte> block, int usaOffset, ushort check)
    {
        int sectors = block.Length / 512;
        BinaryPrimitives.WriteUInt16LittleEndian(block[4..], (ushort)usaOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(block[6..], (ushort)(sectors + 1));
        BinaryPrimitives.WriteUInt16LittleEndian(block[usaOffset..], check);
        for (int i = 1; i <= sectors; i++)
        {
            int end = i * 512 - 2;
            block.Slice(end, 2).CopyTo(block[(usaOffset + i * 2)..]);
            BinaryPrimitives.WriteUInt16LittleEndian(block[end..], check);
        }
    }

    private static void RestartPage(Span<byte> page, ulong current, long fileSize)
    {
        "RSTR"u8.CopyTo(page);
        BinaryPrimitives.WriteUInt32LittleEndian(page[0x10..], Page);
        BinaryPrimitives.WriteUInt32LittleEndian(page[0x14..], Page);
        BinaryPrimitives.WriteUInt16LittleEndian(page[0x18..], 0x30);
        BinaryPrimitives.WriteInt16LittleEndian(page[0x1A..], 0);
        BinaryPrimitives.WriteInt16LittleEndian(page[0x1C..], 2);
        var area = page[0x30..];
        BinaryPrimitives.WriteUInt64LittleEndian(area, current);
        BinaryPrimitives.WriteUInt16LittleEndian(area[8..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x0E..], 2); // shut down cleanly
        BinaryPrimitives.WriteUInt32LittleEndian(area[0x10..], SequenceBits);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x16..], 0x30);
        BinaryPrimitives.WriteInt64LittleEndian(area[0x18..], fileSize);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x24..], 0x30);
        BinaryPrimitives.WriteUInt16LittleEndian(area[0x26..], DataOffset);
        var client = area[0x30..];
        BinaryPrimitives.WriteUInt64LittleEndian(client, Lsn(4 * Page + DataOffset));
        BinaryPrimitives.WriteUInt64LittleEndian(client[8..], Lsn(4 * Page + DataOffset));
        Encoding.Unicode.GetBytes("NTFS").CopyTo(client[0x20..]);
        Protect(page, 0x1E, 0x0101);
    }

    /// <summary>A log record: the LFS header, then NTFS's (operations, where, the clusters), then the redo and undo images.</summary>
    private static byte[] Record(ulong lsn, ushort redoOperation, ushort undoOperation, byte[] redo, byte[] undo, ushort recordOffset, ushort attributeOffset,
        ushort clusterBlock, long vcn, long lcn)
    {
        int ntfs = 0x20 + 8;
        var client = new byte[ntfs + redo.Length + undo.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(client, redoOperation);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(2), undoOperation);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(4), (ushort)ntfs);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(6), (ushort)redo.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(8), (ushort)(ntfs + redo.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(10), (ushort)undo.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(12), 0x18);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(14), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(16), recordOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(18), attributeOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(client.AsSpan(20), clusterBlock);
        BinaryPrimitives.WriteInt64LittleEndian(client.AsSpan(24), vcn);
        BinaryPrimitives.WriteInt64LittleEndian(client.AsSpan(32), lcn);
        redo.CopyTo(client, ntfs);
        undo.CopyTo(client, ntfs + redo.Length);
        var record = new byte[0x30 + client.Length];
        BinaryPrimitives.WriteUInt64LittleEndian(record, lsn);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x18), (uint)client.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x20), 1); // a client record
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(0x24), 42); // transaction
        client.CopyTo(record, 0x30);
        return record;
    }

    private static long Ft(int year, int month, int day) => new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc).ToFileTimeUtc();

    /// <summary>
    /// Two restart pages, two tail pages, and three log pages: an $STANDARD_INFORMATION time change, a record so long it
    /// continues in the next page (across sector ends the update sequence protects), and a record freed there.
    /// </summary>
    private static (byte[] Log, byte[] Long) Log()
    {
        const int pages = 7;
        var log = new byte[pages * Page];
        RestartPage(log.AsSpan(0, Page), Lsn(5 * Page + 0x4A0), pages * Page);
        RestartPage(log.AsSpan(Page, Page), Lsn(4 * Page + DataOffset), pages * Page); // an older copy
        var a = log.AsSpan(4 * Page, Page);
        var b = log.AsSpan(5 * Page, Page);
        "RCRD"u8.CopyTo(a);
        "RCRD"u8.CopyTo(b);
        var first = Record(Lsn(4 * Page + DataOffset), NtfsLog.UpdateResidentValue, NtfsLog.UpdateResidentValue,
            BitConverter.GetBytes(Ft(2019, 5, 1)), BitConverter.GetBytes(Ft(2026, 9, 30)), recordOffset: 0x38, attributeOffset: 0x18, clusterBlock: 2, vcn: 100, lcn: 5100);
        first.CopyTo(a[DataOffset..]);
        int at = DataOffset + (first.Length + 7) / 8 * 8;
        var longImage = Enumerable.Range(0, 4960).Select(i => (byte)(i % 251)).ToArray();
        var second = Record(Lsn(4 * Page + at), NtfsLog.CreateAttribute, NtfsLog.DeleteAttribute, longImage, [], 0x98, 0, 2, 100, 5100);
        int inFirst = Page - at;
        second.AsSpan(0, inFirst).CopyTo(a[at..]);
        second.AsSpan(inFirst).CopyTo(b[DataOffset..]);
        int third = DataOffset + (second.Length - inFirst + 7) / 8 * 8;
        Record(Lsn(5 * Page + third), NtfsLog.DeallocateFileRecordSegment, 0, [], [], 0, 0, 2, 100, 5100).CopyTo(b[third..]);
        Assert.Equal(0x4A0, third);
        Protect(a, 0x28, 0x0707);
        Protect(b, 0x28, 0x0808);
        return (log, longImage);
    }

    [Fact]
    public void The_restart_area_says_the_logs_shape_and_how_LSNs_name_offsets()
    {
        var (log, _) = Log();
        var restart = NtfsLog.ReadRestart(log);
        Assert.NotNull(restart);
        Assert.Equal((4096u, 4096u, (short)2, SequenceBits, DataOffset, true), (restart.SystemPageSize, restart.LogPageSize, restart.MajorVersion, restart.SequenceNumberBits, restart.PageDataOffset, restart.Clean));
        Assert.Equal(Lsn(5 * Page + 0x4A0), restart.CurrentLsn); // the newer of the two pages
        Assert.Equal(5 * Page + 0x4A0, restart.OffsetOf(restart.CurrentLsn));
    }

    [Fact]
    public void Records_are_found_where_their_LSNs_say_and_one_that_runs_on_is_gathered_from_the_next_page()
    {
        var (log, longImage) = Log();
        var records = NtfsLog.ReadRecords(log, NtfsLog.ReadRestart(log)!, TestContext.Current.CancellationToken);
        Assert.Equal(3, records.Count);
        var (time, created, freed) = (records[0], records[1], records[2]);
        Assert.Equal((NtfsLog.UpdateResidentValue, 0x38, 0x18, 2, 100L, 5100L), (time.RedoOperation, (int)time.RecordOffset, (int)time.AttributeOffset, (int)time.ClusterBlockOffset, time.TargetVcn, time.Lcns.Single()));
        Assert.Equal(42u, time.TransactionId);
        // The long image crossed a page and several sector ends: the update sequences put its bytes back.
        Assert.Equal(NtfsLog.CreateAttribute, created.RedoOperation);
        Assert.Equal(longImage, created.Redo);
        Assert.Equal(NtfsLog.DeallocateFileRecordSegment, freed.RedoOperation);
        Assert.Equal("InitializeFileRecordSegment", NtfsLog.OperationName(NtfsLog.InitializeFileRecordSegment));
    }

    [Fact]
    public void A_change_lands_in_the_MFT_record_its_clusters_name_and_its_times_read_from_the_images()
    {
        var (log, _) = Log();
        var time = NtfsLog.ReadRecords(log, NtfsLog.ReadRestart(log)!, TestContext.Current.CancellationToken)[0];
        // $MFT's cluster 100 is at LCN 5100; 1 KiB records in 4 KiB clusters: block 2 (of 512 bytes) is the cluster's second record.
        Assert.Equal(401, NtfsLog.MftRecordOf(time, [new NtfsRun(0, 5000, 1000)], 4096, 1024));
        Assert.Null(NtfsLog.MftRecordOf(time, [new NtfsRun(0, 9000, 1000)], 4096, 1024)); // not in $MFT
        var moved = Assert.Single(NtfsLog.StandardInformationTimes(time));
        Assert.Equal(("Created", Ft(2026, 9, 30), Ft(2019, 5, 1)), moved);
    }

    [Fact]
    public void An_index_entry_in_an_image_names_its_item_by_reference_and_name()
    {
        var entry = new byte[16 + 66 + 14];
        BinaryPrimitives.WriteUInt64LittleEndian(entry, 401UL | (7UL << 48));
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(8), (ushort)entry.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(10), 66 + 14);
        entry[16 + 64] = 7;
        Encoding.Unicode.GetBytes("doc.txt").CopyTo(entry, 16 + 66);
        Assert.Equal((401L, (ushort)7, "doc.txt"), NtfsLog.IndexEntry(entry));
        Assert.Null(NtfsLog.IndexEntry(entry.AsSpan(0, 40)));
    }

    [Fact]
    public void Secure_finds_a_descriptor_by_its_ID_and_hashes_as_NTFS_does()
    {
        // Each 32-bit word added to the hash rotated left by 3: 0 → 1, then 1 rotated is 8, plus 2 is 10.
        Assert.Equal(10u, NtfsSecure.Hash([1, 0, 0, 0, 2, 0, 0, 0]));
        // $SII's index root: its header, the node header, one entry keyed by the ID holding the $SDS header, then the last entry.
        var root = new byte[16 + 16 + 40 + 16];
        var node = root.AsSpan(16);
        BinaryPrimitives.WriteUInt32LittleEndian(node, 16);
        BinaryPrimitives.WriteUInt32LittleEndian(node[4..], 16 + 40 + 16);
        var entry = node[16..];
        BinaryPrimitives.WriteUInt16LittleEndian(entry, 20);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], 20);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], 40);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[10..], 4);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[16..], 18556);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[20..], 0x9081F32F);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[24..], 18556);
        BinaryPrimitives.WriteInt64LittleEndian(entry[28..], 0xD92DC0);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[36..], 244);
        var last = node[(16 + 40)..];
        BinaryPrimitives.WriteUInt16LittleEndian(last[8..], 16);
        BinaryPrimitives.WriteUInt16LittleEndian(last[12..], 2);
        Assert.Equal(new SecureEntry(0x9081F32F, 18556, 0xD92DC0, 244), Assert.Single(NtfsSecure.ReadSiiRoot(root)));
    }

    /// <summary>An operation on one MFT record; a creation carries the new record's header with its sequence number.</summary>
    private static NtfsLogRecord OnRecord(ulong lsn, ushort operation, ushort? madeSequence = null)
    {
        var redo = Array.Empty<byte>();
        if (madeSequence is { } sequence)
        {
            redo = new byte[0x38];
            "FILE"u8.CopyTo(redo);
            BinaryPrimitives.WriteUInt16LittleEndian(redo.AsSpan(0x10), sequence);
        }
        return new NtfsLogRecord(lsn, 0, 0, 1, operation, 0, 1, 0, 0, 0, 0, [], redo, []);
    }

    [Fact]
    public void An_items_history_starts_where_its_record_was_made_with_its_own_sequence_number()
    {
        // Sequence 5 held the record, was deleted, and the record was made again for this item (sequence 6).
        NtfsLogRecord[] onRecord =
        [
            OnRecord(100, NtfsLog.InitializeFileRecordSegment, 5), OnRecord(110, NtfsLog.UpdateResidentValue),
            OnRecord(120, NtfsLog.DeallocateFileRecordSegment), OnRecord(130, NtfsLog.InitializeFileRecordSegment, 6),
            OnRecord(140, NtfsLog.UpdateResidentValue),
        ];
        Assert.Equal(130UL, NtfsLog.OwnHistoryStart(onRecord, 6, reachesLatest: true));
        Assert.Equal(130UL, NtfsLog.OwnHistoryStart(onRecord, 6, reachesLatest: false)); // its making is there: what follows is its own
        Assert.Equal((ushort)6, NtfsLog.MadeSequence(onRecord[3].Redo));
    }

    [Fact]
    public void An_earlier_items_operations_are_never_taken_for_the_items_own()
    {
        // Release issue I20: the log on disk still ends with the record's earlier item (sequence 5), made, its time set
        // back, and deleted; this item's own making (sequence 6) is not on disk yet. None of it is this item's.
        NtfsLogRecord[] onRecord =
        [
            OnRecord(100, NtfsLog.InitializeFileRecordSegment, 5), OnRecord(110, NtfsLog.UpdateResidentValue),
            OnRecord(120, NtfsLog.DeallocateFileRecordSegment),
        ];
        Assert.Null(NtfsLog.OwnHistoryStart(onRecord, 6, reachesLatest: false));
        Assert.Null(NtfsLog.OwnHistoryStart(onRecord, 6, reachesLatest: true));
        // The same when only the earlier item's making and changes are there, not its deletion.
        Assert.Null(NtfsLog.OwnHistoryStart(onRecord[..2], 6, reachesLatest: true));
    }

    [Fact]
    public void Without_any_reuse_in_the_log_the_operations_are_the_items_only_when_the_log_reaches_its_latest_change()
    {
        NtfsLogRecord[] onRecord = [OnRecord(100, NtfsLog.UpdateResidentValue), OnRecord(110, NtfsLog.UpdateResidentValue)];
        Assert.Equal(0UL, NtfsLog.OwnHistoryStart(onRecord, 6, reachesLatest: true));
        // Changes newer than the log on disk may include a reuse since: nothing is attributed.
        Assert.Null(NtfsLog.OwnHistoryStart(onRecord, 6, reachesLatest: false));
    }

    [Fact]
    public void The_newest_making_whose_record_cannot_be_read_is_taken_as_the_items()
    {
        NtfsLogRecord[] onRecord =
        [
            OnRecord(100, NtfsLog.InitializeFileRecordSegment, 5), OnRecord(120, NtfsLog.DeallocateFileRecordSegment),
            OnRecord(130, NtfsLog.InitializeFileRecordSegment), // its image did not survive
            OnRecord(140, NtfsLog.UpdateResidentValue),
        ];
        Assert.Equal(130UL, NtfsLog.OwnHistoryStart(onRecord, 6, reachesLatest: true));
        Assert.Null(NtfsLog.MadeSequence([]));
    }
}
