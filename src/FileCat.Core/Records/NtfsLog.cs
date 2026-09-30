using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.Records;

/// <summary>What $LogFile's restart area says: the log's geometry, where its NTFS client would start a restart, and how LSNs map to offsets.</summary>
public sealed record NtfsLogRestart(uint SystemPageSize, uint LogPageSize, short MajorVersion, short MinorVersion, ulong CurrentLsn,
    int SequenceNumberBits, long FileSize, int PageDataOffset, int RecordHeaderLength, bool Clean, ulong OldestLsn, ulong ClientRestartLsn)
{
    /// <summary>The byte offset in $LogFile an LSN names (its low bits are the offset in 8-byte units).</summary>
    public long OffsetOf(ulong lsn) => SequenceNumberBits is > 3 and < 64 ? (long)((lsn << SequenceNumberBits) >> (SequenceNumberBits - 3)) : -1;
}

/// <summary>One NTFS operation in $LogFile: what to redo and undo, and where (an attribute's clusters, a record, an offset in it).</summary>
public sealed record NtfsLogRecord(ulong Lsn, ulong PreviousLsn, ulong UndoNextLsn, uint TransactionId, ushort RedoOperation, ushort UndoOperation,
    ushort TargetAttribute, ushort RecordOffset, ushort AttributeOffset, ushort ClusterBlockOffset, long TargetVcn, long[] Lcns, byte[] Redo, byte[] Undo);

/// <summary>
/// NTFS's transaction log ($LogFile, MFT record 2), read as the Log File Service writes it (D-56): two restart pages,
/// then pages of log records ("RCRD"), each protected by an update sequence and each record naming its own offset by its
/// LSN, so records are found by where they say they are, those that span pages are gathered, and stale bytes are passed
/// over. NTFS's client data says, for each metadata change, the operation to redo and to undo, the attribute's clusters,
/// and the bytes before and after: a changed time in $STANDARD_INFORMATION can be read back from it.
/// </summary>
public static class NtfsLog
{
    public const int LogRecordHeaderLength = 0x30;
    private const int NtfsHeaderLength = 0x20;

    // The operations NTFS logs (NTFS_LOG_OPERATION), with what each does in words.
    private static readonly (string Name, string Words)[] Operations =
    [
        ("Noop", "nothing"),
        ("CompensationLogRecord", "an undo completed"),
        ("InitializeFileRecordSegment", "record made"),
        ("DeallocateFileRecordSegment", "record freed (deleted)"),
        ("WriteEndOfFileRecordSegment", "record's end rewritten"),
        ("CreateAttribute", "attribute added"),
        ("DeleteAttribute", "attribute removed"),
        ("UpdateResidentValue", "value changed"),
        ("UpdateNonresidentValue", "data written"),
        ("UpdateMappingPairs", "clusters remapped"),
        ("DeleteDirtyClusters", "dirty clusters dropped"),
        ("SetNewAttributeSizes", "sizes set"),
        ("AddIndexEntryRoot", "index entry added (in the record)"),
        ("DeleteIndexEntryRoot", "index entry removed (in the record)"),
        ("AddIndexEntryAllocation", "index entry added"),
        ("DeleteIndexEntryAllocation", "index entry removed"),
        ("WriteEndOfIndexBuffer", "index block's end rewritten"),
        ("SetIndexEntryVcnRoot", "index link set (in the record)"),
        ("SetIndexEntryVcnAllocation", "index link set"),
        ("UpdateFileNameRoot", "name's copy of times and sizes updated (in the record)"),
        ("UpdateFileNameAllocation", "name's copy of times and sizes updated"),
        ("SetBitsInNonresidentBitMap", "bits set in a bitmap"),
        ("ClearBitsInNonresidentBitMap", "bits cleared in a bitmap"),
        ("HotFix", "cluster replaced"),
        ("EndTopLevelAction", "end of an action"),
        ("PrepareTransaction", "transaction prepared"),
        ("CommitTransaction", "transaction committed"),
        ("ForgetTransaction", "transaction forgotten"),
        ("OpenNonresidentAttribute", "attribute opened"),
        ("OpenAttributeTableDump", "open attributes (checkpoint)"),
        ("AttributeNamesDump", "attribute names (checkpoint)"),
        ("DirtyPageTableDump", "dirty pages (checkpoint)"),
        ("TransactionTableDump", "transactions (checkpoint)"),
        ("UpdateRecordDataRoot", "index data changed (in the record)"),
        ("UpdateRecordDataAllocation", "index data changed"),
        ("UpdateRelativeDataIndex", "relative data changed"),
        ("UpdateRelativeDataAllocation", "relative data changed"),
        ("ZeroEndOfFileRecord", "record's end zeroed"),
    ];

    public const ushort InitializeFileRecordSegment = 2, DeallocateFileRecordSegment = 3, CreateAttribute = 5, DeleteAttribute = 6,
        UpdateResidentValue = 7, SetNewAttributeSizes = 11, AddIndexEntryRoot = 12, DeleteIndexEntryRoot = 13, AddIndexEntryAllocation = 14,
        DeleteIndexEntryAllocation = 15, UpdateFileNameRoot = 19, UpdateFileNameAllocation = 20;

    /// <summary>The sequence number of the record an InitializeFileRecordSegment made (its new record's header), or null.</summary>
    public static ushort? MadeSequence(byte[] redo) =>
        redo.Length >= 0x12 && redo.AsSpan(0, 4).SequenceEqual("FILE"u8) ? BinaryPrimitives.ReadUInt16LittleEndian(redo.AsSpan(0x10)) : null;

    /// <summary>
    /// Where an item's own history starts among the operations the log holds on its MFT record. Records are reused, so
    /// the operations of the items a record held before are on it too: the item's history starts where the record was
    /// made with the item's own <paramref name="sequence"/> number. Without that creation in the log, the operations are
    /// the item's only when the log shows no reuse of the record and reaches the record's own latest change
    /// (<paramref name="reachesLatest"/>; the record comes from the file system's cache, the log from the disk, which NTFS
    /// writes a moment later): otherwise they may be an earlier item's, and none are taken for this one's (release issue
    /// I20). Returns the first LSN of the item's history (0: all of them), or null when none are the item's.
    /// </summary>
    public static ulong? OwnHistoryStart(IEnumerable<NtfsLogRecord> onRecord, ushort sequence, bool reachesLatest)
    {
        ulong made = 0;
        NtfsLogRecord? lastReuse = null;
        foreach (var r in onRecord)
        {
            if (r.RedoOperation == InitializeFileRecordSegment && MadeSequence(r.Redo) == sequence) made = Math.Max(made, r.Lsn);
            if (r.RedoOperation is InitializeFileRecordSegment or DeallocateFileRecordSegment && (lastReuse is null || r.Lsn > lastReuse.Lsn)) lastReuse = r;
        }
        if (made > 0) return made;
        // The newest reuse is a creation whose new record cannot be read: it is taken as this item's.
        if (lastReuse is { RedoOperation: InitializeFileRecordSegment } newest && MadeSequence(newest.Redo) is null) return newest.Lsn;
        return lastReuse is null && reachesLatest ? 0 : null;
    }

    public static string OperationName(ushort operation) => operation < Operations.Length ? Operations[operation].Name : $"operation {operation}";

    public static string OperationWords(ushort operation) => operation < Operations.Length ? Operations[operation].Words : $"operation {operation}";

    /// <summary>The restart area of the newer of the two restart pages (the one with the higher current LSN), or null.</summary>
    public static NtfsLogRestart? ReadRestart(byte[] log)
    {
        var first = Restart(log, 0, 4096);
        // The second restart page sits one system page in (where the first says, or 4 KiB when the first is damaged).
        int secondAt = (int)(first?.SystemPageSize ?? 4096);
        var second = Restart(log, secondAt, secondAt);
        if (first is null) return second;
        return second is not null && second.CurrentLsn > first.CurrentLsn ? second : first;
    }

    private static NtfsLogRestart? Restart(byte[] log, int at, int pageSize)
    {
        if (at < 0 || at + 64 > log.Length || !log.AsSpan(at, 4).SequenceEqual("RSTR"u8)) return null;
        uint systemPage = BinaryPrimitives.ReadUInt32LittleEndian(log.AsSpan(at + 0x10));
        if (systemPage is < 512 or > 65536 || at + systemPage > log.Length) systemPage = (uint)Math.Min(pageSize, log.Length - at);
        var page = log.AsSpan(at, (int)systemPage).ToArray();
        NtfsIndexFixups.Apply(page);
        uint logPage = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(0x14));
        int area = BinaryPrimitives.ReadUInt16LittleEndian(page.AsSpan(0x18));
        short minor = BinaryPrimitives.ReadInt16LittleEndian(page.AsSpan(0x1A));
        short major = BinaryPrimitives.ReadInt16LittleEndian(page.AsSpan(0x1C));
        if (logPage is < 512 or > 65536 || area + 0x30 > page.Length) return null;
        var r = page.AsSpan(area);
        ulong current = BinaryPrimitives.ReadUInt64LittleEndian(r);
        int clients = BinaryPrimitives.ReadUInt16LittleEndian(r[8..]);
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(r[0x0E..]);
        int seqBits = (int)BinaryPrimitives.ReadUInt32LittleEndian(r[0x10..]);
        int clientArray = BinaryPrimitives.ReadUInt16LittleEndian(r[0x16..]);
        long fileSize = BinaryPrimitives.ReadInt64LittleEndian(r[0x18..]);
        int headerLength = BinaryPrimitives.ReadUInt16LittleEndian(r[0x24..]);
        int dataOffset = BinaryPrimitives.ReadUInt16LittleEndian(r[0x26..]);
        ulong oldest = 0, clientRestart = 0;
        if (clients > 0 && clientArray + 0x20 <= r.Length)
        {
            oldest = BinaryPrimitives.ReadUInt64LittleEndian(r[clientArray..]);
            clientRestart = BinaryPrimitives.ReadUInt64LittleEndian(r[(clientArray + 8)..]);
        }
        if (seqBits is < 4 or > 63 || dataOffset is < 0x28 or > 0x1000 || headerLength is < LogRecordHeaderLength or > 0x100) return null;
        return new NtfsLogRestart(systemPage, logPage, major, minor, current, seqBits, fileSize, dataOffset, headerLength, (flags & 2) != 0, oldest, clientRestart);
    }

    /// <summary>
    /// Every log record the pages still hold, oldest LSN first. A record is taken where its own LSN says it is (the two
    /// "tail" pages right after the restart pages hold copies of the newest page: records there fill in what the home
    /// page does not have yet); one that runs past its page continues in the next page's data area.
    /// </summary>
    public static List<NtfsLogRecord> ReadRecords(byte[] log, NtfsLogRestart restart, CancellationToken ct)
    {
        int pageSize = (int)restart.LogPageSize;
        long start = 2L * restart.SystemPageSize;
        long end = Math.Min(log.Length, restart.FileSize > 0 ? restart.FileSize : log.Length);
        var pages = new Dictionary<long, byte[]>();
        byte[]? Page(long offset)
        {
            if (offset < start || offset + pageSize > end) return null;
            if (pages.TryGetValue(offset, out var cached)) return cached;
            if (!log.AsSpan((int)offset, 4).SequenceEqual("RCRD"u8)) return pages[offset] = null!;
            var copy = log.AsSpan((int)offset, pageSize).ToArray();
            NtfsIndexFixups.Apply(copy);
            if (pages.Count > 4096) pages.Clear();
            return pages[offset] = copy;
        }
        // The log's own area begins after two tail pages (LFS 1.1 and later).
        long firstLogPage = restart.MajorVersion >= 1 && restart.MinorVersion >= 1 || restart.MajorVersion >= 2 ? start + 2L * pageSize : start;
        var found = new Dictionary<ulong, NtfsLogRecord>();
        for (long pageAt = start; pageAt + pageSize <= end; pageAt += pageSize)
        {
            ct.ThrowIfCancellationRequested();
            var page = Page(pageAt);
            if (page is null) continue;
            bool tail = pageAt < firstLogPage;
            for (int q = restart.PageDataOffset; q + LogRecordHeaderLength <= pageSize; q += 8)
            {
                ulong lsn = BinaryPrimitives.ReadUInt64LittleEndian(page.AsSpan(q));
                if (lsn == 0) continue;
                long home = restart.OffsetOf(lsn);
                // Here, or (a tail copy) at this place in its home page.
                if (tail ? home < firstLogPage || home % pageSize != q : home != pageAt + q) continue;
                if (found.ContainsKey(lsn)) continue;
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(q + 0x18));
                uint type = BinaryPrimitives.ReadUInt32LittleEndian(page.AsSpan(q + 0x20));
                if (length is 0 or > 1 << 16 || type is not (1 or 2)) continue;
                var data = Gather(page, q, LogRecordHeaderLength + (int)length, pageAt, tail ? -1 : pageSize, restart.PageDataOffset, firstLogPage, end, Page);
                if (data is null) continue;
                if (Parse(data, type) is { } record) found[lsn] = record;
                q += LogRecordHeaderLength + (int)((length + 7) & ~7u) - 8;
            }
        }
        return [.. found.Values.OrderBy(r => r.Lsn)];
    }

    /// <summary>A record's bytes: from its page, and from the data areas of the pages after it when it runs past (a tail copy may not).</summary>
    private static byte[]? Gather(byte[] page, int q, int total, long pageAt, int pageSize, int dataOffset, long firstLogPage, long end, Func<long, byte[]?> pageOf)
    {
        var data = new byte[total];
        int have = Math.Min(total, page.Length - q);
        page.AsSpan(q, have).CopyTo(data);
        if (have == total) return data;
        if (pageSize < 0) return null;
        long next = pageAt;
        for (int guard = 0; have < total && guard < 64; guard++)
        {
            next += pageSize;
            if (next + pageSize > end) next = firstLogPage; // the log wraps
            var more = pageOf(next);
            if (more is null) return null;
            int take = Math.Min(total - have, pageSize - dataOffset);
            more.AsSpan(dataOffset, take).CopyTo(data.AsSpan(have));
            have += take;
        }
        return have == total ? data : null;
    }

    private static NtfsLogRecord? Parse(byte[] data, uint type)
    {
        ulong lsn = BinaryPrimitives.ReadUInt64LittleEndian(data);
        ulong previous = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(8));
        ulong undoNext = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(0x10));
        uint transaction = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x24));
        var client = data.AsSpan(LogRecordHeaderLength);
        if (type != 1 || client.Length < NtfsHeaderLength)
            return new NtfsLogRecord(lsn, previous, undoNext, transaction, 0, 0, 0, 0, 0, 0, 0, [], [], []);
        ushort redo = BinaryPrimitives.ReadUInt16LittleEndian(client);
        ushort undo = BinaryPrimitives.ReadUInt16LittleEndian(client[2..]);
        int redoOffset = BinaryPrimitives.ReadUInt16LittleEndian(client[4..]), redoLength = BinaryPrimitives.ReadUInt16LittleEndian(client[6..]);
        int undoOffset = BinaryPrimitives.ReadUInt16LittleEndian(client[8..]), undoLength = BinaryPrimitives.ReadUInt16LittleEndian(client[10..]);
        ushort target = BinaryPrimitives.ReadUInt16LittleEndian(client[12..]);
        int lcnCount = BinaryPrimitives.ReadUInt16LittleEndian(client[14..]);
        ushort recordOffset = BinaryPrimitives.ReadUInt16LittleEndian(client[16..]);
        ushort attributeOffset = BinaryPrimitives.ReadUInt16LittleEndian(client[18..]);
        ushort clusterBlock = BinaryPrimitives.ReadUInt16LittleEndian(client[20..]);
        long vcn = BinaryPrimitives.ReadInt64LittleEndian(client[24..]);
        if (redo > 64 || undo > 64 || lcnCount > 256 || NtfsHeaderLength + lcnCount * 8 > client.Length) return null;
        var lcns = new long[lcnCount];
        for (int i = 0; i < lcnCount; i++) lcns[i] = BinaryPrimitives.ReadInt64LittleEndian(client[(NtfsHeaderLength + i * 8)..]);
        return new NtfsLogRecord(lsn, previous, undoNext, transaction, redo, undo, target, recordOffset, attributeOffset, clusterBlock, vcn, lcns,
            Cut(client, redoOffset, redoLength), Cut(client, undoOffset, undoLength));
    }

    private static byte[] Cut(ReadOnlySpan<byte> data, int offset, int length) =>
        length > 0 && offset + length <= data.Length ? data.Slice(offset, length).ToArray() : [];

    /// <summary>
    /// The MFT record a record's change lands in: its first cluster found in $MFT's runs, then the 512-byte block within
    /// that cluster. Null when the change is not in $MFT (an index block, a bitmap, a file's data).
    /// </summary>
    public static long? MftRecordOf(NtfsLogRecord record, IReadOnlyList<NtfsRun> mftRuns, int clusterSize, int recordSize) =>
        ByteInRuns(record, mftRuns, clusterSize) is { } at && recordSize > 0 ? at / recordSize : null;

    /// <summary>Where in an attribute a record's change lands (its first cluster found in the attribute's runs), or null.</summary>
    public static long? ByteInRuns(NtfsLogRecord record, IReadOnlyList<NtfsRun> runs, int clusterSize)
    {
        if (record.Lcns.Length == 0 || clusterSize <= 0) return null;
        long lcn = record.Lcns[0];
        foreach (var run in runs)
            if (!run.IsSparse && lcn >= run.Lcn && lcn < run.Lcn + run.Length)
                return (run.Vcn + (lcn - run.Lcn)) * clusterSize + record.ClusterBlockOffset * 512L;
        return null;
    }

    /// <summary>An index entry (in a redo or undo image): the item it names by MFT reference, and the name its $FILE_NAME key holds.</summary>
    public static (long Record, ushort Sequence, string Name)? IndexEntry(ReadOnlySpan<byte> entry)
    {
        if (entry.Length < 16 + 66) return null;
        ulong reference = BinaryPrimitives.ReadUInt64LittleEndian(entry);
        int keyLength = BinaryPrimitives.ReadUInt16LittleEndian(entry[10..]);
        if (keyLength < 66 || 16 + keyLength > entry.Length) return null;
        var key = entry.Slice(16, keyLength);
        int nameLength = key[64];
        if (66 + nameLength * 2 > key.Length) return null;
        return ((long)(reference & 0xFFFF_FFFF_FFFF), (ushort)(reference >> 48), Encoding.Unicode.GetString(key.Slice(66, nameLength * 2)));
    }

    /// <summary>
    /// The times a change to $STANDARD_INFORMATION's value moved, from its undo (before) and redo (after) images:
    /// <paramref name="valueStart"/> is where the value starts in the attribute (0x18 for a resident one without a name).
    /// </summary>
    public static List<(string Field, long Before, long After)> StandardInformationTimes(NtfsLogRecord record, int valueStart = 0x18) =>
        Times(record, valueStart, 0, ["Created", "Modified", "MFT changed", "Accessed"]);

    /// <summary>The same for a $FILE_NAME value, whose times follow the parent reference.</summary>
    public static List<(string Field, long Before, long After)> FileNameTimes(NtfsLogRecord record, int valueStart = 0x18) =>
        Times(record, valueStart, 8, ["Created", "Modified", "MFT changed", "Accessed"]);

    private static List<(string, long, long)> Times(NtfsLogRecord record, int valueStart, int first, string[] names)
    {
        var moved = new List<(string, long, long)>();
        int at = record.AttributeOffset - valueStart; // where the change starts in the value
        for (int i = 0; i < names.Length; i++)
        {
            int field = first + i * 8;
            int inRedo = field - at;
            if (inRedo < 0 || inRedo + 8 > record.Redo.Length) continue;
            long after = BinaryPrimitives.ReadInt64LittleEndian(record.Redo.AsSpan(inRedo));
            long before = inRedo + 8 <= record.Undo.Length ? BinaryPrimitives.ReadInt64LittleEndian(record.Undo.AsSpan(inRedo)) : 0;
            if (after != before) moved.Add((names[i], before, after));
        }
        return moved;
    }
}
