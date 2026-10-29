using System.Buffers.Binary;
using System.ComponentModel;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// $Extend's view indexes in a file's record (D-56, as administrator on NTFS): the item's entries in $ObjId's $O (object
/// IDs) and $Reparse's $R (reparse points), found through $Extend's own $I30 index and read from the volume, compared
/// with what Windows reports for the item; and the entries for its record left behind in the indexes' unused space.
/// </summary>
public sealed unsafe partial class WindowsFileRecords
{
    private const int ErrorFileNotFound = 2, ErrorObjectIdNotFound = 4312;

    /// <summary>How much of a view index's blocks is read at most: a larger index is scanned this far, and says so.</summary>
    internal int ViewIndexReadLimit { get; init; } = 64 << 20;

    /// <summary>One view index as read: where it is, how much of its blocks was read, and what it holds about the item.</summary>
    private sealed class ViewIndexRead(string file, string name, string section)
    {
        public string File => file;
        public string Name => name;
        /// <summary>The report section that shows it (named in warnings).</summary>
        public string Section => section;
        public long Record = -1;
        /// <summary>Its $INDEX_ALLOCATION's size, and how much of it was read.</summary>
        public long Size, Read;
        public NtfsViewIndex? Index;
        /// <summary>Why it was not read at all, or null.</summary>
        public string? Failed;
        public readonly List<string> Problems = [];
        /// <summary>Whether the read limit stopped the scan before the index's end.</summary>
        public bool Truncated;
    }

    private sealed partial class Reader
    {
        /// <summary>The volume's clusters (NTFS_VOLUME_DATA_BUFFER's TotalClusters), or 0 when not known.</summary>
        private long _volumeClusters;
        private ViewIndexRead? _objectIds, _reparsePoints;

        /// <summary>See <see cref="WindowsFileRecords.ViewIndexReadLimit"/>.</summary>
        internal int ViewIndexLimit { get; init; } = 64 << 20;

        private static long VolumeClusters(SafeFileHandle volume)
        {
            var data = new byte[128];
            return Ioctl(volume, FsctlGetNtfsVolumeData, [], data, out int got) == 0 && got >= 24 ? Math.Max(0, BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(16))) : 0;
        }

        /// <summary>
        /// Whether a run read from disk lies on the volume: within its clusters when their count is known, else at a byte
        /// offset a read can ask for. A run that does not is damage, and is not read.
        /// </summary>
        private bool OnVolume(NtfsRun run)
        {
            if (run.IsSparse) return true;
            if (_volumeClusters > 0) return run.Lcn < _volumeClusters && run.Length <= _volumeClusters - run.Lcn;
            return run.Lcn <= long.MaxValue / Math.Max(1, _clusterSize) - run.Length;
        }

        /// <summary>$ObjId and $Reparse, found by name in the $I30 index of $Extend (record 11), and their indexes read for this item's entries.</summary>
        private void ReadViewIndexes(SafeFileHandle volume)
        {
            if (_record is null || _clusterSize <= 0) return;
            _objectIds = new ViewIndexRead("$ObjId", "$O", "Object ID index");
            _reparsePoints = new ViewIndexRead("$Reparse", "$R", "Reparse index");
            long mine = _record.Number;
            Guid? objectId = ReportedObjectId().Ids?.ObjectId;
            List<IndexName> names;
            try
            {
                var extend = MftRecord(volume, 11);
                if (extend is null)
                {
                    _objectIds.Failed = _reparsePoints.Failed = "$Extend's record (11) could not be read.";
                    return;
                }
                names = ReadIndex(volume, extend).Names;
            }
            catch (IOException ex)
            {
                _objectIds.Failed = _reparsePoints.Failed = "$Extend could not be read: " + ex.Message;
                return;
            }
            // Its own entries, and in $O also any entry for its object ID (which link tracking moves between records).
            Find(_objectIds, ViewIndexKind.ObjectIds, e => e.Record == mine || objectId is { } id && e.ObjectId == id);
            Find(_reparsePoints, ViewIndexKind.ReparsePoints, e => e.Record == mine);

            void Find(ViewIndexRead read, ViewIndexKind kind, Func<ViewIndexEntry, bool> keep)
            {
                var entry = names.FirstOrDefault(n => !n.InSlack && string.Equals(n.Name.Name, read.File, StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    read.Failed = $"$Extend's index ({names.Count(n => !n.InSlack):N0} names) has no {read.File}, so its {read.Name} index was not found.";
                    return;
                }
                try { ReadViewIndex(volume, read, entry, kind, keep); }
                catch (IOException ex) { read.Failed = $"{read.File} could not be read: {ex.Message}"; }
            }
        }

        /// <summary>
        /// A view index: its root, then its blocks, 1 MiB at a time and at most <see cref="ViewIndexLimit"/> bytes, each
        /// with what the index's bitmap says of it.
        /// </summary>
        private void ReadViewIndex(SafeFileHandle volume, ViewIndexRead read, IndexName entry, ViewIndexKind kind, Func<ViewIndexEntry, bool> keep)
        {
            read.Record = entry.Record;
            var record = MftRecord(volume, entry.Record);
            if (record is null)
            {
                read.Failed = $"{read.File}'s record ({entry.Record:N0}) could not be read.";
                return;
            }
            if (record.Sequence != entry.Sequence)
                read.Problems.Add($"Its record has sequence number {record.Sequence}, not the {entry.Sequence} that $Extend's index names: it was reused, or one of them is damaged.");
            foreach (var problem in record.Problems) read.Problems.Add("Its MFT record: " + problem);
            var root = record.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeTypes.IndexRoot && a.Name == read.Name);
            if (root?.Value is not { } rootValue)
            {
                read.Failed = $"{read.File} (record {entry.Record:N0}) has no {read.Name} index root.";
                return;
            }
            var index = new NtfsViewIndex(kind, keep);
            read.Index = index;
            index.ReadRoot(rootValue);
            var pieces = record.Attributes.Where(a => a.Type == NtfsAttributeTypes.IndexAllocation && a.Name == read.Name && !a.Resident).ToList();
            if (pieces.Count == 0) return;
            read.Size = Math.Max(0, pieces.Max(a => a.Size));
            int blockSize = index.BlockSize;
            if (blockSize == 0) return;
            if (read.Size % blockSize != 0)
                read.Problems.Add($"Its blocks take {read.Size:N0} bytes, not a whole number of {blockSize:N0}-byte blocks; the last {read.Size % blockSize:N0} bytes were not read.");
            read.Truncated = read.Size / blockSize * blockSize > ViewIndexLimit / blockSize * blockSize;
            var runs = pieces.SelectMany(a => a.Runs).OrderBy(r => r.Vcn).ToList();
            var outside = runs.Where(r => !OnVolume(r)).ToList();
            if (outside.Count > 0)
                read.Problems.Add($"{outside.Count:N0} of the runs of its blocks point past the end of the volume (the first to cluster {outside[0].Lcn:N0}); they were not read.");
            runs = runs.Where(OnVolume).ToList();
            byte[]? bitmap = IndexBitmap(volume, record, read);
            long limit = Math.Min(read.Size, ViewIndexLimit) / blockSize * blockSize;
            int block = 0;
            for (long offset = 0; offset < limit;)
            {
                ct.ThrowIfCancellationRequested();
                int length = (int)Math.Min(1 << 20, limit - offset);
                byte[]? chunk;
                string? why = null;
                try { chunk = ReadRange(volume, runs, offset, length); }
                catch (IOException ex)
                {
                    chunk = null;
                    why = ex.Message;
                }
                if (chunk is null)
                {
                    read.Problems.Add($"Its blocks {block:N0} to {block + length / blockSize - 1:N0} could not be read from the volume" + (why is null ? " (no run holds them)." : ": " + why));
                    block += length / blockSize;
                }
                else
                    for (int at = 0; at + blockSize <= length; at += blockSize, block++)
                    {
                        bool used = bitmap is null || block / 8 < bitmap.Length && (bitmap[block / 8] >> (block % 8) & 1) != 0;
                        index.ReadBlock(chunk.AsSpan(at, blockSize).ToArray(), block, used);
                    }
                offset += length;
                read.Read = offset;
            }
        }

        /// <summary>An index's $BITMAP (a bit per block, set when it is in use), kept in its record or read from its clusters; null: none read.</summary>
        private byte[]? IndexBitmap(SafeFileHandle volume, NtfsRecord record, ViewIndexRead read)
        {
            var pieces = record.Attributes.Where(a => a.Type == NtfsAttributeTypes.Bitmap && a.Name == read.Name).ToList();
            if (pieces.Count == 0)
            {
                read.Problems.Add($"It has no {read.Name} bitmap: every block was taken to be in use.");
                return null;
            }
            if (pieces[0].Resident) return pieces[0].Value;
            var runs = pieces.Where(a => !a.Resident).SelectMany(a => a.Runs).OrderBy(r => r.Vcn).ToList();
            if (runs.All(OnVolume)) return ReadRuns(volume, runs, pieces.Max(a => a.Size), 1 << 20);
            read.Problems.Add($"Its {read.Name} bitmap's runs point past the end of the volume: every block was taken to be in use.");
            return null;
        }

        /// <summary>
        /// The object ID and birth IDs Windows reports for the item (FSCTL_GET_OBJECT_ID): null IDs with no error when it
        /// has none, with the error when Windows did not say.
        /// </summary>
        private ((Guid ObjectId, Guid BirthVolume, Guid BirthObject, Guid Domain)? Ids, int Error) ReportedObjectId()
        {
            var output = new byte[64];
            int error = Ioctl(_item, FsctlGetObjectId, [], output, out int got);
            if (error is ErrorFileNotFound or ErrorObjectIdNotFound) return (null, 0);
            if (error != 0 || got < 16) return (null, error != 0 ? error : ErrorInvalidParameter);
            Guid At(int offset) => got >= offset + 16 ? new Guid(output.AsSpan(offset, 16)) : Guid.Empty;
            return ((At(0), At(16), At(32), At(48)), 0);
        }

        /// <summary>The tag of the item's reparse point as Windows reports it (FSCTL_GET_REPARSE_POINT, else the attribute tag), null when it has none.</summary>
        private (uint? Tag, string Source) ReportedReparseTag()
        {
            if ((_attributes & 0x400) == 0) return (null, "its attributes");
            var output = new byte[16 * 1024];
            if (Ioctl(_item, FsctlGetReparsePoint, [], output, out int got) == 0 && got >= 8)
                return (BinaryPrimitives.ReadUInt32LittleEndian(output), "FSCTL_GET_REPARSE_POINT");
            return (_reparseTag != 0 ? _reparseTag : null, "its attribute tag");
        }

        private static string Id(Guid id) => id == Guid.Empty ? "none" : id.ToString("B");

        private string Whose(ViewIndexEntry e) =>
            e.Record != _record!.Number ? "another record, with this item's object ID"
            : e.Sequence == _record.Sequence ? "this item"
            : $"an earlier item in this record (sequence {e.Sequence})";

        /// <summary>What was read of an index: its record, its entries in use, its blocks, and whether the scan reached its end.</summary>
        private static string Extent(ViewIndexRead read, string what)
        {
            var index = read.Index!;
            string text = $"{read.File}, record {read.Record:N0} · {index.Live:N0} {what} in use";
            if (read.Size == 0) return text + " · all in its root (it has no blocks)";
            if (index.Blocks == 0) return text + " · its blocks were not read (see below)";
            text += $" · {index.Blocks:N0} blocks of {RecordText.Short(index.BlockSize)}, {index.BlocksInUse:N0} in use";
            return read.Truncated
                ? text + $" · only the first {RecordText.Short(read.Read)} of its {RecordText.Short(read.Size)} was read: the rest was not searched"
                : text + " · all read";
        }

        private InspectionSection? ObjectIdIndexSection()
        {
            if (_objectIds is not { } read || _record is null) return null;
            string title = $"{read.Section} ($ObjId:$O)";
            var lines = Wrap("NTFS lists every object ID on the volume in $ObjId's $O index, with the record of the item that has it and the IDs it was born with: link tracking finds a moved item through it. Read from the volume, and compared with what Windows reports for this item.");
            if (read.Failed is not null || read.Index is null) return new InspectionSection(title, []) { Lines = [.. lines, .. Wrap(read.Failed ?? "It was not read.")] };
            var fields = new List<(string, string)> { ("Index", Extent(read, "object IDs")) };
            var entries = read.Index.Entries;
            var live = entries.Where(e => !e.InSlack).ToList();
            var (reported, error) = ReportedObjectId();
            bool agrees = true;
            var entry = reported is { } r
                ? live.FirstOrDefault(e => e.ObjectId == r.ObjectId)
                : live.FirstOrDefault(e => e.Record == _record.Number && e.Sequence == _record.Sequence);
            if (entry is not null)
            {
                fields.Add(("Its entry", $"{Id(entry.ObjectId)} → record {entry.Record:N0}, sequence {entry.Sequence}"));
                fields.Add(("Birth volume", Id(entry.BirthVolume)));
                fields.Add(("Birth object", Id(entry.BirthObject)));
                fields.Add(("Domain", Id(entry.Domain)));
            }
            string compared;
            if (error != 0)
                compared = $"not compared: Windows did not report its object ID ({new Win32Exception(error).Message})";
            else if (reported is { } ids)
            {
                if (entry is null)
                {
                    agrees = read.Truncated;
                    compared = read.Truncated
                        ? $"not found in the part read: Windows reports object ID {Id(ids.ObjectId)}, and the scan stopped before the end of $O"
                        : $"MISSING: Windows reports object ID {Id(ids.ObjectId)}, but $O has no entry for it";
                }
                else
                {
                    var differences = new List<string>();
                    if (entry.Record != _record.Number || entry.Sequence != _record.Sequence)
                        differences.Add($"the entry names record {entry.Record:N0} (sequence {entry.Sequence}), not this item's {_record.Number:N0} (sequence {_record.Sequence})");
                    if (entry.BirthVolume != ids.BirthVolume) differences.Add($"Windows reports birth volume {Id(ids.BirthVolume)}");
                    if (entry.BirthObject != ids.BirthObject) differences.Add($"Windows reports birth object {Id(ids.BirthObject)}");
                    if (entry.Domain != ids.Domain) differences.Add($"Windows reports domain {Id(ids.Domain)}");
                    agrees = differences.Count == 0;
                    compared = agrees
                        ? "matches what Windows reports (FSCTL_GET_OBJECT_ID): the object ID, birth volume, birth object, and domain, for this item's record"
                        : "DIFFERENT from what Windows reports (FSCTL_GET_OBJECT_ID): " + string.Join("; ", differences);
                }
            }
            else if (entry is not null)
            {
                agrees = false;
                compared = "EXTRA: $O has an entry for this item, but Windows reports no object ID for it";
            }
            else compared = "matches: Windows reports no object ID for it, and no entry in $O names its record" + (read.Truncated ? " in the part read" : "");
            fields.Add(("Compared", compared));
            // An entry in use for this record other than its own: an earlier item's, which NTFS should have removed.
            foreach (var other in live.Where(e => e != entry && e.Record == _record.Number))
            {
                agrees = false;
                lines.AddRange(Wrap($"$O also has an entry in use for this record: {Id(other.ObjectId)}, for {Whose(other)}. NTFS removes an item's entry when its object ID goes; this one was left.", "⚠ "));
            }
            if (!agrees) _warnings.Add("$ObjId's $O index does not agree with the item's object ID (see Object ID index).");
            return ViewIndexSection(title, read, fields, lines, entries.Where(e => e.InSlack).ToList(),
                ["Object ID", "Record", "Birth volume", "Birth object", "Whose"],
                e => [Id(e.ObjectId), $"{e.Record:N0}/{e.Sequence}", Id(e.BirthVolume), Id(e.BirthObject), Whose(e)]);
        }

        private InspectionSection? ReparseIndexSection()
        {
            if (_reparsePoints is not { } read || _record is null) return null;
            string title = $"{read.Section} ($Reparse:$R)";
            var lines = Wrap("NTFS lists every reparse point on the volume in $Reparse's $R index, by tag and record, so that it can find them without reading every record. Read from the volume, and compared with what Windows reports for this item.");
            if (read.Failed is not null || read.Index is null) return new InspectionSection(title, []) { Lines = [.. lines, .. Wrap(read.Failed ?? "It was not read.")] };
            var fields = new List<(string, string)> { ("Index", Extent(read, "reparse points")) };
            var entries = read.Index.Entries;
            var live = entries.Where(e => !e.InSlack).ToList();
            var (tag, source) = ReportedReparseTag();
            bool agrees = true;
            var entry = live.FirstOrDefault(e => e.Record == _record.Number && e.Sequence == _record.Sequence);
            if (entry is not null) fields.Add(("Its entry", $"tag {RecordText.ReparseTag(entry.Tag)} → record {entry.Record:N0}, sequence {entry.Sequence}"));
            string compared;
            if (tag is { } reported)
            {
                if (entry is null)
                {
                    agrees = read.Truncated;
                    compared = read.Truncated
                        ? $"not found in the part read: Windows reports a reparse point (tag 0x{reported:X8}), and the scan stopped before the end of $R"
                        : $"MISSING: Windows reports a reparse point (tag 0x{reported:X8}), but $R has no entry for this item";
                }
                else
                {
                    agrees = entry.Tag == reported;
                    compared = agrees
                        ? $"matches what Windows reports ({source}): tag 0x{reported:X8}, for this item's record"
                        : $"DIFFERENT: $R says tag 0x{entry.Tag:X8}; Windows reports 0x{reported:X8} ({source})";
                }
            }
            else if (entry is not null)
            {
                agrees = false;
                compared = $"EXTRA: $R lists a reparse point (tag 0x{entry.Tag:X8}) for this item, which Windows says has none";
            }
            else compared = "matches: it has no reparse point, and no entry in $R names its record" + (read.Truncated ? " in the part read" : "");
            fields.Add(("Compared", compared));
            foreach (var other in live.Where(e => e != entry && e.Record == _record.Number))
            {
                agrees = false;
                lines.AddRange(Wrap($"$R also has an entry in use for this record: tag 0x{other.Tag:X8}, for {Whose(other)}. NTFS removes an entry with its reparse point; this one was left.", "⚠ "));
            }
            if (!agrees) _warnings.Add("$Reparse's $R index does not agree with the item's reparse point (see Reparse index).");
            return ViewIndexSection(title, read, fields, lines, entries.Where(e => e.InSlack).ToList(),
                ["Tag", "Record", "Whose"], e => [RecordText.ReparseTag(e.Tag), $"{e.Record:N0}/{e.Sequence}", Whose(e)]);
        }

        /// <summary>What both index sections show besides the item's entry: the problems met reading the index, and its entries left behind for this record.</summary>
        private InspectionSection ViewIndexSection(string title, ViewIndexRead read, List<(string, string)> fields, List<string> lines,
            List<ViewIndexEntry> left, string[] columns, Func<ViewIndexEntry, string[]> row)
        {
            var problems = read.Problems.Concat(read.Index!.Problems).ToList();
            foreach (var problem in problems) lines.AddRange(Wrap(problem, "• "));
            if (problems.Count > 0) _warnings.Add($"{read.File}'s {read.Name} index: {problems.Count:N0} problem{(problems.Count == 1 ? "" : "s")} reading it (see {read.Section}).");
            fields.Add(("Left behind", left.Count == 0
                ? "nothing for this record in the unused space of its nodes"
                : $"{left.Count:N0} entr{(left.Count == 1 ? "y" : "ies")} for this record in the unused space of its nodes (below)"));
            var children = new List<InspectionSection>();
            if (left.Count > 0)
                children.Add(new InspectionSection($"Left behind in {read.Name} for this record ({left.Count:N0})", [])
                {
                    Lines = Wrap($"NTFS moves and removes {read.Name} entries without clearing the space they used. These are not in use: an earlier item's in this record, or this item's from before its entry changed."),
                    Table = new InspectionTable(columns, left.Take(300).Select(row).ToList()) { More = left.Count > 300 ? $"{left.Count - 300:N0} more are not listed." : null },
                });
            return new InspectionSection(title, fields) { Lines = lines, Children = children };
        }
    }
}
