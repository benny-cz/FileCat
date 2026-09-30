using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// NTFS's transaction log and its store of security descriptors in a file's record (D-56, as administrator):
/// $LogFile's operations on this item's MFT record and on its names in its folder's index, with changed times read
/// from their before and after images; and $Secure's copy of its descriptor, checked against its hash, its mirror copy,
/// and what Windows reports.
/// </summary>
public sealed unsafe partial class WindowsFileRecords
{
    /// <summary>$Secure's copy of an item's descriptor, and how it compares with what Windows reports (null: not compared).</summary>
    private sealed record SecureCopy(SecureEntry Entry, bool HashMatches, bool? MirrorMatches, string? Sddl, string? LiveSddl,
        SecurityComparison? Comparison, int Descriptors);

    /// <param name="Differences">The parts that differ, in words: "owner", "group", "DACL flags" (protected,
    /// auto-inherited), "DACL entries", or "DACL order" when only their order does. Empty when none.</param>
    /// <param name="MarkedInherited">
    /// Entries Windows reports as inherited that the stored DACL does not mark so. A DACL not marked auto-inherited (set
    /// the pre-Windows 2000 way, as some profile folders are) carries no inheritance marks; GetSecurityInfo works them
    /// out by comparing its entries with the parent folder's inheritable ones, marks those inherited, and lists the
    /// others first. That is Windows' reading of the same DACL, not a difference.
    /// </param>
    internal sealed record SecurityComparison(IReadOnlyList<string> Differences, int MarkedInherited);

    private const byte InheritedAce = 0x10; // INHERITED_ACE, in an ACE header's flags (its second byte)

    /// <summary>
    /// Compares a descriptor as NTFS stores it with the one Windows reports for the item, both self-relative: owner,
    /// group, and DACL, as structures rather than SDDL text (see <see cref="SecurityComparison"/>).
    /// </summary>
    internal static SecurityComparison CompareSecurity(byte[] stored, byte[] live)
    {
        RawSecurityDescriptor a, b;
        try
        {
            a = new RawSecurityDescriptor(stored, 0);
            b = new RawSecurityDescriptor(live, 0);
        }
        catch (ArgumentException)
        {
            return new(stored.AsSpan().SequenceEqual(live) ? [] : ["the descriptor (one could not be parsed)"], 0);
        }
        var parts = new List<string>();
        if (a.Owner != b.Owner) parts.Add("owner");
        if (a.Group != b.Group) parts.Add("group");
        // Without the auto-inherited flag, Windows' marks and order are its own reading (see MarkedInherited).
        bool unmarked = (a.ControlFlags & ControlFlags.DiscretionaryAclAutoInherited) == 0;
        var daclFlags = ControlFlags.DiscretionaryAclPresent | ControlFlags.DiscretionaryAclProtected
            | ControlFlags.DiscretionaryAclAutoInheritRequired | (unmarked ? 0 : ControlFlags.DiscretionaryAclAutoInherited);
        if ((a.ControlFlags & daclFlags) != (b.ControlFlags & daclFlags)) parts.Add("DACL flags");
        var first = Entries(a.DiscretionaryAcl);
        var second = Entries(b.DiscretionaryAcl);
        int marked = 0;
        if (unmarked)
        {
            marked = Math.Max(0, second.Count(e => (e[1] & InheritedAce) != 0) - first.Count(e => (e[1] & InheritedAce) != 0));
            if (!Unmarked(first).Order().SequenceEqual(Unmarked(second).Order())) parts.Add("DACL entries");
        }
        else if (!first.Select(Convert.ToHexString).SequenceEqual(second.Select(Convert.ToHexString)))
            parts.Add(first.Select(Convert.ToHexString).Order().SequenceEqual(second.Select(Convert.ToHexString).Order()) ? "DACL order" : "DACL entries");
        return new(parts, parts.Contains("DACL entries") ? 0 : marked);

        static List<byte[]> Entries(RawAcl? acl)
        {
            var list = new List<byte[]>();
            if (acl is null) return list;
            foreach (GenericAce ace in acl)
            {
                var bytes = new byte[ace.BinaryLength];
                ace.GetBinaryForm(bytes, 0);
                list.Add(bytes);
            }
            return list;
        }

        static IEnumerable<string> Unmarked(List<byte[]> entries) => entries.Select(e =>
        {
            var copy = (byte[])e.Clone();
            copy[1] &= unchecked((byte)~InheritedAce);
            return Convert.ToHexString(copy);
        });
    }

    private sealed partial class Reader
    {
        /// <summary>$LogFile is 64 MiB unless made larger (chkdsk /L); read at most this much.</summary>
        private const int MaxLogBytes = 512 << 20;
        private const int MaxLogRows = 120;

        /// <summary>Reads again, this far apart, while the log on disk has not reached the record's latest change.</summary>
        private const int LogCatchUpReads = 3;
        private static readonly TimeSpan LogCatchUpWait = TimeSpan.FromSeconds(2);

        private NtfsLogRestart? _logRestart;
        private int _logCount;
        private ulong _logFirst, _logLast;
        private readonly List<(NtfsLogRecord Record, string What)> _logMine = [];
        private readonly List<RecordFinding> _logFindings = [];
        /// <summary>The items the record held before this one (records are reused), as far back as the log reaches.</summary>
        private readonly List<string> _logEarlier = [];
        private ulong _logMadeAt;
        /// <summary>Why none of the operations on the item's record are shown as its own (release issue I20), or null.</summary>
        private string? _logUnattributed;
        private string? _logProblem;
        private long _logBytes;
        private SecureCopy? _secure;
        private string? _secureProblem;

        private void ReadLogAndSecure(SafeFileHandle volume, CancellationToken token)
        {
            if (_record is null || _clusterSize <= 0) return;
            try { ReadLog(volume, token); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _logProblem = "$LogFile could not be read: " + ex.Message; }
            if (_record.Standard is { HasExtendedFields: true, SecurityId: > 0 } si)
            {
                try { ReadSecure(volume, si.SecurityId); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _secureProblem = "$Secure could not be read: " + ex.Message; }
            }
        }

        private int _recordSize;

        /// <summary>
        /// The MFT record size: NTFS_VOLUME_DATA_BUFFER's BytesPerFileRecordSegment, at offset 48 (44 is BytesPerCluster,
        /// which an earlier reading took by mistake: $LogFile's operations then landed in the wrong records).
        /// </summary>
        private int RecordSize(SafeFileHandle volume)
        {
            if (_recordSize > 0) return _recordSize;
            var data = new byte[128];
            int size = Ioctl(volume, FsctlGetNtfsVolumeData, [], data, out int got) == 0 && got >= 52 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(48)) : 1024;
            return _recordSize = size is < 256 or > 65536 ? 1024 : size;
        }

        private static IEnumerable<NtfsRun> RunsOf(NtfsRecord record, uint type, string name) =>
            record.Attributes.Where(a => a.Type == type && a.Name == name && !a.Resident).SelectMany(a => a.Runs).OrderBy(r => r.Vcn);

        private void ReadLog(SafeFileHandle volume, CancellationToken token)
        {
            var logFile = MftRecord(volume, 2);
            var pieces = logFile?.Attributes.Where(a => a.Type == NtfsAttributeTypes.Data && a.Name == "" && !a.Resident).ToList();
            if (logFile is null || pieces is not { Count: > 0 })
            {
                _logProblem = "$LogFile's data was not found in its record.";
                return;
            }
            long size = pieces.Max(a => a.Size);
            _logBytes = size;
            // The item's record comes from the file system's cache; $LogFile is read from the disk, where NTFS writes it a
            // moment later. Until the log on disk reaches the record's own latest change, its newest operations (the
            // record's reuse for this item among them) are missing, so the log is read again for a few seconds.
            NtfsLogRestart? restart;
            List<NtfsLogRecord> records;
            for (int attempt = 0; ; attempt++)
            {
                var log = ReadRuns(volume, RunsOf(logFile, NtfsAttributeTypes.Data, ""), size, (int)Math.Min(size, MaxLogBytes));
                token.ThrowIfCancellationRequested();
                restart = NtfsLog.ReadRestart(log);
                if (restart is null)
                {
                    _logProblem = "$LogFile's restart area could not be read.";
                    return;
                }
                records = NtfsLog.ReadRecords(log, restart, token);
                if (records.Count > 0 && records[^1].Lsn >= _record!.Lsn || attempt >= LogCatchUpReads) break;
                token.WaitHandle.WaitOne(LogCatchUpWait);
                token.ThrowIfCancellationRequested();
            }
            _logRestart = restart;
            _logCount = records.Count;
            if (records.Count == 0) return;
            _logFirst = records[0].Lsn;
            _logLast = records[^1].Lsn;

            // Where changes land: this item's MFT record (found through $MFT's own runs), and its folders' indexes.
            var mft = MftRecord(volume, 0);
            if (mft is null) return;
            var mftRuns = RunsOf(mft, NtfsAttributeTypes.Data, "").ToList();
            int recordSize = RecordSize(volume);
            long mine = _record!.Number;
            var folders = new Dictionary<long, (string Name, List<NtfsRun> IndexRuns)>();
            foreach (var parent in _record.Names.Select(n => n.ParentRecord).Distinct().Take(8))
            {
                if (MftRecord(volume, parent) is { } folder)
                    folders[parent] = (folder.Names.FirstOrDefault(n => !n.IsDosAlias)?.Name ?? $"record {parent:N0}", RunsOf(folder, NtfsAttributeTypes.IndexAllocation, "$I30").ToList());
            }
            var own = new List<NtfsLogRecord>();
            foreach (var record in records)
            {
                token.ThrowIfCancellationRequested();
                if (record.RedoOperation == 0 && record.UndoOperation == 0) continue;
                long? target = NtfsLog.MftRecordOf(record, mftRuns, _clusterSize, recordSize);
                if (target == mine)
                {
                    own.Add(record);
                    continue;
                }
                // Its names in its folder's index: in the folder's record (the index root) or in the index's blocks.
                string? folderName = null;
                bool inIndexRoot = record.RedoOperation is NtfsLog.AddIndexEntryRoot or NtfsLog.DeleteIndexEntryRoot or NtfsLog.UpdateFileNameRoot
                    || record.UndoOperation is NtfsLog.AddIndexEntryRoot or NtfsLog.DeleteIndexEntryRoot;
                if (target is { } t && inIndexRoot && folders.TryGetValue(t, out var holder)) folderName = holder.Name;
                else if (target is null)
                    folderName = folders.Values.FirstOrDefault(f => f.IndexRuns.Count > 0 && NtfsLog.ByteInRuns(record, f.IndexRuns, _clusterSize) is not null).Name;
                if (folderName is null) continue;
                if (IndexChange(record, mine, _record.Sequence, folderName) is { } what) _logMine.Add((record, what));
            }

            // A record is reused: what came before it was made for this item belongs to the items it held before, and
            // without its making in the log, the operations are this item's only where no reuse can hide (release issue I20).
            bool reachesLatest = _logLast >= _record.Lsn;
            ulong? start = NtfsLog.OwnHistoryStart(own, _record.Sequence, reachesLatest);
            _logMadeAt = start ?? 0;
            if (start is null && own.Count > 0)
                _logUnattributed = reachesLatest
                    ? "The log shows this item's record freed or made for other items, but not made for this one, so the operations on the record are not taken for this item's."
                    : $"The log on disk ends at LSN {_logLast:N0}, before the record's latest change (LSN {_record.Lsn:N0}): NTFS has not written its newest operations there yet, so the operations on the record are not taken for this item's (they may be an earlier item's). Read the record again (F5) in a moment.";
            ulong first = start ?? ulong.MaxValue;
            string? earlier = null;
            ulong earlierMade = 0;
            foreach (var record in own)
            {
                if (record.Lsn >= first)
                {
                    _logMine.Add((record, OwnChange(record)));
                    continue;
                }
                if (record.RedoOperation == NtfsLog.InitializeFileRecordSegment)
                {
                    earlier = MadeName(record.Redo) ?? "an item";
                    earlierMade = record.Lsn;
                }
                else if (record.RedoOperation == NtfsLog.DeallocateFileRecordSegment)
                {
                    _logEarlier.Add(earlier is null ? $"an item made before the log reaches, freed at LSN {record.Lsn:N0}" : $"“{earlier}”, made at LSN {earlierMade:N0} and freed at LSN {record.Lsn:N0}");
                    earlier = null;
                }
            }
            // Times set back, from the before and after images of this item's own $STANDARD_INFORMATION.
            foreach (var (record, _) in _logMine)
            {
                if (record.RedoOperation != NtfsLog.UpdateResidentValue || AttributeAt(record)?.Type != NtfsAttributeTypes.StandardInformation) continue;
                foreach (var (field, before, after) in NtfsLog.StandardInformationTimes(record))
                    // Set back by more than a minute: a program set it (NTFS moves times forward).
                    if (field is "Created" or "Modified" && before > 0 && after > 0 && after < before - TimeSpan.TicksPerMinute)
                        _logFindings.Add(new RecordFinding(true, $"$LogFile (LSN {record.Lsn:N0}) shows its {field.ToLowerInvariant()} time set back from {RecordText.TimeSeconds(before)} to {RecordText.TimeSeconds(after)}: a program set it, and the log kept the time it had."));
            }
        }

        private NtfsAttribute? AttributeAt(NtfsLogRecord record) =>
            _record!.Attributes.FirstOrDefault(a => a.InRecord == _record.Number && a.OffsetInRecord == record.RecordOffset);

        /// <summary>What an operation on this item's own MFT record did, in words; changed times are read from its images.</summary>
        private string OwnChange(NtfsLogRecord record)
        {
            ushort op = record.RedoOperation;
            var attribute = AttributeAt(record);
            string where = attribute is null ? "" : attribute.TypeName + (attribute.Name.Length > 0 ? ":" + attribute.Name : "");
            switch (op)
            {
                case 0:
                    return "nothing to redo; its undo: " + NtfsLog.OperationWords(record.UndoOperation);
                case NtfsLog.InitializeFileRecordSegment:
                    return "the record was made (the item created, or the record reused)" + MadeAs(record.Redo);
                case NtfsLog.DeallocateFileRecordSegment:
                    return "the record was freed (the item deleted)";
                case NtfsLog.CreateAttribute:
                    return "added " + AttributeIn(record.Redo);
                case NtfsLog.DeleteAttribute:
                    return "removed " + AttributeIn(record.Undo);
                case NtfsLog.UpdateResidentValue when attribute?.Type == NtfsAttributeTypes.StandardInformation:
                {
                    var parts = NtfsLog.StandardInformationTimes(record).Select(t => $"{t.Field} {RecordText.TimeSeconds(t.After)} (was {RecordText.TimeSeconds(t.Before)})").ToList();
                    parts.AddRange(StandardInformationFields(record));
                    return "$STANDARD_INFORMATION: " + (parts.Count > 0 ? string.Join("; ", parts) : "rewritten with the same values");
                }
                case NtfsLog.UpdateResidentValue when attribute is { Type: NtfsAttributeTypes.Data, Resident: true }:
                    return record.Redo.Length == 0
                        ? "its content (kept in the record) resized"
                        : $"its content (kept in the record) written: {record.Redo.Length:N0} bytes at offset {Math.Max(0, record.AttributeOffset - 0x18):N0}: {Preview(record.Redo)}";
                case NtfsLog.UpdateResidentValue when attribute?.Type == NtfsAttributeTypes.FileName:
                {
                    var times = NtfsLog.FileNameTimes(record);
                    return times.Count > 0
                        ? "$FILE_NAME: " + string.Join("; ", times.Select(t => $"{t.Field} {RecordText.TimeSeconds(t.After)} (was {RecordText.TimeSeconds(t.Before)})"))
                        : "$FILE_NAME changed";
                }
                case NtfsLog.SetNewAttributeSizes when record.Redo.Length >= 16:
                {
                    long dataSize = BinaryPrimitives.ReadInt64LittleEndian(record.Redo.AsSpan(8));
                    return $"{(where.Length > 0 ? where : "an attribute")}: size set to {RecordText.Bytes(dataSize)}";
                }
                default:
                    return NtfsLog.OperationWords(op) + (where.Length > 0 ? " in " + where : "");
            }
        }

        /// <summary>
        /// A change to a folder's index that names this item (its MFT reference, the sequence number included, so the
        /// names of the items the record held before are not taken for its own): a name added or removed.
        /// </summary>
        private static string? IndexChange(NtfsLogRecord record, long mine, ushort sequence, string folder)
        {
            bool Mine((long Record, ushort Sequence, string Name)? entry) => entry is { } e && e.Record == mine && e.Sequence == sequence;
            switch (record.RedoOperation)
            {
                case NtfsLog.AddIndexEntryRoot or NtfsLog.AddIndexEntryAllocation:
                    return NtfsLog.IndexEntry(record.Redo) is var added && Mine(added) ? $"its name “{added!.Value.Name}” added to the index of “{folder}”" : null;
                case NtfsLog.DeleteIndexEntryRoot or NtfsLog.DeleteIndexEntryAllocation:
                    return NtfsLog.IndexEntry(record.Undo) is var removed && Mine(removed) ? $"its name “{removed!.Value.Name}” removed from the index of “{folder}”" : null;
            }
            return null;
        }

        /// <summary>The fields other than times a change to $STANDARD_INFORMATION's value set: attributes, security ID, its latest USN.</summary>
        private static List<string> StandardInformationFields(NtfsLogRecord record)
        {
            var parts = new List<string>();
            int start = record.AttributeOffset - 0x18, end = start + record.Redo.Length;
            bool Covers(int field, int size) => start <= field && end >= field + size;
            if (Covers(0x20, 4))
            {
                uint after = BinaryPrimitives.ReadUInt32LittleEndian(record.Redo.AsSpan(0x20 - start));
                uint before = 0x20 - start + 4 <= record.Undo.Length ? BinaryPrimitives.ReadUInt32LittleEndian(record.Undo.AsSpan(0x20 - start)) : after;
                if (after != before) parts.Add($"attributes {RecordText.Attributes(after)} (were 0x{before:X8})");
            }
            if (Covers(0x34, 4))
            {
                uint after = BinaryPrimitives.ReadUInt32LittleEndian(record.Redo.AsSpan(0x34 - start));
                uint before = 0x34 - start + 4 <= record.Undo.Length ? BinaryPrimitives.ReadUInt32LittleEndian(record.Undo.AsSpan(0x34 - start)) : after;
                if (after != before) parts.Add($"security ID {after} (was {before})");
            }
            if (Covers(0x40, 8))
            {
                long after = BinaryPrimitives.ReadInt64LittleEndian(record.Redo.AsSpan(0x40 - start));
                if (after != 0) parts.Add($"latest USN {after:N0}");
            }
            return parts;
        }

        /// <summary>Bytes as text when they read as text (line ends shown), else their first bytes in hex.</summary>
        private static string Preview(byte[] data)
        {
            if (data.Length == 0) return "nothing";
            bool text = data.All(b => b is >= 0x20 and < 0x7F or (byte)'\r' or (byte)'\n' or (byte)'\t');
            if (text)
            {
                string s = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 80)).Replace("\r\n", "⏎").Replace("\n", "⏎").Replace("\r", "⏎").Replace("\t", "→");
                return "“" + s + (data.Length > 80 ? "…" : "") + "”";
            }
            return Convert.ToHexString(data, 0, Math.Min(data.Length, 24)) + (data.Length > 24 ? "…" : "");
        }

        private static string? MadeName(byte[] image)
        {
            if (image.Length < 48 || !image.AsSpan(0, 4).SequenceEqual("FILE"u8)) return null;
            try
            {
                var padded = new byte[Math.Max(image.Length, 1024)];
                image.CopyTo(padded, 0);
                return NtfsRecord.Parse(padded, 0, live: true).Names.FirstOrDefault(n => !n.IsDosAlias)?.Name;
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return null; }
        }

        private static string MadeAs(byte[] image)
        {
            if (image.Length < 48 || !image.AsSpan(0, 4).SequenceEqual("FILE"u8)) return "";
            try
            {
                var padded = new byte[Math.Max(image.Length, 1024)];
                image.CopyTo(padded, 0);
                var made = NtfsRecord.Parse(padded, 0, live: true);
                var name = made.Names.FirstOrDefault(n => !n.IsDosAlias);
                var parts = new List<string>();
                if (name is not null) parts.Add($"as “{name.Name}”");
                if (made.Standard is { } si && si.Created > 0) parts.Add("created " + RecordText.TimeSeconds(si.Created));
                return parts.Count > 0 ? ": " + string.Join(", ", parts) : "";
            }
            catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or InvalidDataException) { return ""; }
        }

        private static string AttributeIn(byte[] image)
        {
            if (image.Length < 16) return "an attribute";
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(image);
            int nameLength = image[9];
            int nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(10));
            string name = nameLength > 0 && nameOffset + nameLength * 2 <= image.Length ? System.Text.Encoding.Unicode.GetString(image, nameOffset, nameLength * 2) : "";
            return NtfsAttributeTypes.Name(type) + (name.Length > 0 ? ":" + name : "");
        }

        private InspectionSection? LogSection()
        {
            if (!_ntfs || !privileged || _record is null) return null;
            var lines = new List<string>();
            if (_logProblem is not null && _logRestart is null) return new InspectionSection("NTFS log ($LogFile)", []) { Lines = Wrap(_logProblem) };
            if (_logRestart is not { } restart) return null;
            var fields = new List<(string, string)>
            {
                ("Log", $"{RecordText.Bytes(_logBytes)}, Log File Service {restart.MajorVersion}.{restart.MinorVersion}, pages of {RecordText.Short(restart.LogPageSize)}{(restart.Clean ? ", shut down cleanly" : "")}"),
                ("Holds", _logCount == 0 ? "no operations it can read" : $"{_logCount:N0} operations, LSN {_logFirst:N0} to {_logLast:N0}"),
                ("Restart from", $"LSN {restart.ClientRestartLsn:N0} (the last checkpoint: what a restart would replay from)"),
                ("This item", _logMine.Count > 0
                    ? $"{_logMine.Count:N0} of them touched its record or its names" + (_logMadeAt > 0 ? $", since the record was made for it at LSN {_logMadeAt:N0}" : "")
                        + (_logUnattributed is not null ? " (only changes to its names: see below)" : "")
                    : _logUnattributed is not null
                        ? "none shown (see below)"
                        // Its record says where its last change is logged: older than the log reaches, the log has moved on.
                        : _record.Lsn > 0 && _record.Lsn < _logFirst
                            ? $"none of them: its last change (LSN {_record.Lsn:N0}, from its MFT record) is older than anything the log still holds"
                            : "none of them touched it"),
            };
            if (_logEarlier.Count > 0)
                fields.Add(("Before it", string.Join("; ", _logEarlier.TakeLast(4)) + (_logEarlier.Count > 4 ? $"; and {_logEarlier.Count - 4:N0} more" : "") + " (the record's earlier items)"));
            lines.AddRange(Wrap("NTFS writes every change to its metadata here before making it, with the bytes before and after; the log is circular, so on a busy drive it reaches back minutes to hours. An operation names where it changed a record by offset: the attribute there is named as the record is laid out now."));
            if (_logUnattributed is not null) lines.AddRange(Wrap(_logUnattributed));
            InspectionTable? table = null;
            if (_logMine.Count > 0)
            {
                var rows = _logMine.OrderByDescending(m => m.Record.Lsn).Take(MaxLogRows)
                    .Select(m => new[] { m.Record.Lsn.ToString("N0", CultureInfo.CurrentCulture), NtfsLog.OperationName(m.Record.RedoOperation), m.What }).ToList();
                table = new InspectionTable(["LSN", "Operation", "What it did"], rows) { More = _logMine.Count > MaxLogRows ? $"{_logMine.Count - MaxLogRows:N0} older ones are not listed." : null };
            }
            return new InspectionSection("NTFS log ($LogFile)", fields) { Table = table, Lines = lines };
        }

        // ---- $Secure ------------------------------------------------------------------------------------------------

        private void ReadSecure(SafeFileHandle volume, uint securityId)
        {
            var secure = MftRecord(volume, 9);
            if (secure is null)
            {
                _secureProblem = "$Secure's record could not be read.";
                return;
            }
            var entries = new List<SecureEntry>();
            var root = secure.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeTypes.IndexRoot && a.Name == "$SII");
            if (root?.Value is { } rootValue) entries.AddRange(NtfsSecure.ReadSiiRoot(rootValue));
            int blockSize = root?.Value is { } value ? NtfsIndex.BlockSize(value) : 0;
            var allocation = secure.Attributes.Where(a => a.Type == NtfsAttributeTypes.IndexAllocation && a.Name == "$SII" && !a.Resident).ToList();
            if (allocation.Count > 0 && blockSize is >= 512 and <= 65536)
            {
                var blocks = ReadRuns(volume, RunsOf(secure, NtfsAttributeTypes.IndexAllocation, "$SII"), allocation.Max(a => a.Size), 64 << 20);
                for (int at = 0; at + blockSize <= blocks.Length; at += blockSize)
                    entries.AddRange(NtfsSecure.ReadSiiBlock(blocks.AsSpan(at, blockSize).ToArray()));
            }
            var entry = entries.FirstOrDefault(e => e.SecurityId == securityId);
            if (entry is null)
            {
                _secureProblem = $"Security ID {securityId} is not in $Secure's $SII index ({entries.Count:N0} entries read).";
                return;
            }
            var sds = secure.Attributes.Where(a => a.Type == NtfsAttributeTypes.Data && a.Name == "$SDS" && !a.Resident).ToList();
            if (sds.Count == 0)
            {
                _secureProblem = "$Secure's $SDS stream was not found.";
                return;
            }
            var runs = RunsOf(secure, NtfsAttributeTypes.Data, "$SDS").ToList();
            long sdsSize = sds.Max(a => a.Size);
            byte[]? stored = ReadRange(volume, runs, entry.Offset, (int)entry.Length);
            if (stored is null || NtfsSecure.ReadHeader(stored) is not { } header || header.SecurityId != securityId)
            {
                _secureProblem = $"$SDS holds no descriptor for ID {securityId} where $SII says (offset {entry.Offset:N0}).";
                return;
            }
            var descriptor = stored.AsSpan(NtfsSecure.HeaderLength).ToArray();
            bool hashMatches = NtfsSecure.Hash(descriptor) == header.Hash;
            bool? mirror = entry.Offset + NtfsSecure.MirrorDistance + entry.Length <= sdsSize && ReadRange(volume, runs, entry.Offset + NtfsSecure.MirrorDistance, (int)entry.Length) is { } copy
                ? copy.AsSpan().SequenceEqual(stored) : null;
            byte[]? live = LiveDescriptor();
            _secure = new SecureCopy(header, hashMatches, mirror, Sddl(descriptor), live is null ? null : Sddl(live),
                live is null ? null : CompareSecurity(descriptor, live), entries.Select(e => e.SecurityId).Distinct().Count());
        }

        /// <summary>Some bytes of a non-resident attribute, from the clusters that hold them only.</summary>
        private byte[]? ReadRange(SafeFileHandle volume, List<NtfsRun> runs, long offset, int length)
        {
            if (length <= 0 || length > 1 << 20) return null;
            var result = new byte[length];
            int have = 0;
            while (have < length)
            {
                long at = offset + have;
                long vcn = at / _clusterSize;
                var run = runs.FirstOrDefault(r => vcn >= r.Vcn && vcn < r.Vcn + r.Length);
                if (run is null || run.IsSparse) return null;
                int inCluster = (int)(at % _clusterSize);
                int clusters = (int)Math.Min(run.Vcn + run.Length - vcn, (length - have + inCluster + _clusterSize - 1) / _clusterSize);
                var buffer = new byte[clusters * _clusterSize];
                int read = ReadAligned(volume, buffer, (run.Lcn + (vcn - run.Vcn)) * _clusterSize);
                int take = Math.Min(length - have, read - inCluster);
                if (take <= 0) return null;
                buffer.AsSpan(inCluster, take).CopyTo(result.AsSpan(have));
                have += take;
            }
            return result;
        }

        private const uint SddlParts = 1 | 2 | 4; // owner, group, DACL: what the Security section compares

        private static string? Sddl(byte[] descriptor)
        {
            fixed (byte* d = descriptor)
            {
                if (!ConvertSecurityDescriptorToStringSecurityDescriptor((nint)d, 1, SddlParts, out nint text, out _)) return null;
                try { return Marshal.PtrToStringUni(text); }
                finally { LocalFree(text); }
            }
        }

        /// <summary>The owner, group, and DACL Windows reports for the item, as a self-relative descriptor.</summary>
        private byte[]? LiveDescriptor()
        {
            int error = GetSecurityInfo(_item, 1 /* SE_FILE_OBJECT */, SddlParts, out _, out _, out _, out _, out nint descriptor);
            if (error != 0) return null;
            try
            {
                int length = GetSecurityDescriptorLength(descriptor);
                return length <= 0 ? null : new ReadOnlySpan<byte>((void*)descriptor, length).ToArray();
            }
            finally { LocalFree(descriptor); }
        }

        private InspectionSection? SecureSection()
        {
            if (!_ntfs || !privileged || _record is null) return null;
            if (_secure is not { } s) return _secureProblem is null ? null : new InspectionSection("Security descriptor in $Secure", []) { Lines = Wrap(_secureProblem) };
            bool differs = s.Comparison is { Differences.Count: > 0 };
            int marked = s.Comparison?.MarkedInherited ?? 0;
            var fields = new List<(string, string)>
            {
                ("Security ID", s.Entry.SecurityId.ToString(CultureInfo.CurrentCulture)),
                ("In $SDS", $"at offset {s.Entry.Offset:N0} (0x{s.Entry.Offset:X}), {s.Entry.Length:N0} bytes with its header"),
                ("Hash", $"0x{s.Entry.Hash:X8}: " + (s.HashMatches ? "matches the descriptor" : "does NOT match the descriptor stored there")),
                ("Mirror copy", s.MirrorMatches switch { true => "the same (256 KiB further on)", false => "DIFFERS from it (256 KiB further on)", null => "not read" }),
                ("As Windows reports", s.Comparison switch
                {
                    null => "not compared",
                    { Differences.Count: > 0 } c => "DIFFERENT: " + string.Join(", ", c.Differences) + " (both below)",
                    _ when marked > 0 => $"the same owner, group, and DACL; Windows reports {marked:N0} of its entries as inherited (see below)",
                    _ => "the same owner, group, and DACL",
                }),
                ("On this volume", $"{s.Descriptors:N0} distinct descriptors, each stored once; files point to them by ID"),
            };
            if (!s.HashMatches || s.MirrorMatches == false || differs)
                _warnings.Add("$Secure: its stored descriptor does not agree with its hash, its mirror, or Windows (see Security descriptor in $Secure).");
            var lines = new List<string>();
            if (s.Sddl is not null)
            {
                lines.Add("As stored:");
                lines.AddRange(AccessText.SddlLines(s.Sddl).Select(l => "  " + l));
            }
            if (differs && s.LiveSddl is not null)
            {
                lines.Add("As Windows reports:");
                lines.AddRange(AccessText.SddlLines(s.LiveSddl).Select(l => "  " + l));
            }
            else if (marked > 0)
                lines.AddRange(Wrap("Its DACL is stored without inheritance marks: it is not marked auto-inherited, the way ACLs "
                    + "were set before Windows 2000. Windows works out which entries came from the folder by comparing them with the "
                    + $"folder's inheritable ones, and reports {(marked == 1 ? "1 entry" : $"{marked:N0} entries")} as inherited (see Security).", "• "));
            return new InspectionSection("Security descriptor in $Secure", fields) { Lines = lines };
        }
    }
}
