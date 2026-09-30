using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// What NTFS, ReFS, FAT, and exFAT record about a file or folder (D-56), read through a handle that asks only to read
/// attributes, so nothing changes: all four times to 100 ns, IDs and parent, hard links, the 8.3 name, the latest
/// change-journal entry, object ID, reparse data, clusters on the volume, compression, sparse ranges, security, and for
/// files on SMB shares the connection. As administrator on NTFS also the MFT record ($FILE_NAME times that programs
/// cannot set, every attribute, the record's slack) and the item's history in the change journal.
/// </summary>
public sealed unsafe partial class WindowsFileRecords : IFileRecords
{
    private const uint FileReadData = 0x1, FileReadAttributes = 0x80, ReadControl = 0x0002_0000, Synchronize = 0x0010_0000, GenericRead = 0x8000_0000;
    private const uint ShareAll = 7, OpenExisting = 3, BackupSemantics = 0x0200_0000, OpenReparsePoint = 0x0020_0000;
    private const uint FsctlReadFileUsnData = 0x0009_00EB, FsctlGetObjectId = 0x0009_009C, FsctlGetReparsePoint = 0x0009_00A8,
        FsctlGetRetrievalPointers = 0x0009_0073, FsctlQueryAllocatedRanges = 0x0009_40CF, FsctlGetNtfsVolumeData = 0x0009_0064,
        FsctlGetNtfsFileRecord = 0x0009_0068;
    private const int ErrorHandleEof = 38, ErrorInvalidParameter = 87, ErrorMoreData = 234;
    private const int MaxExtents = 100_000, ExtentsShown = 200, HistoryKept = 2000, HistoryShown = 400;

    public bool IsSupported => true;

    /// <summary>Tests: read as a process without administrator rights would, even when this one has them.</summary>
    internal bool AssumeNotPrivileged { get; init; }

    /// <summary>How much of the change journal is read for one item's history (its newest part when larger).</summary>
    internal static long JournalReadLimit { get; set; } = 2L << 30;

    public InspectionReport Read(string path, CancellationToken ct) =>
        new Reader(Path.GetFullPath(path), !AssumeNotPrivileged && Environment.IsPrivilegedProcess, ct).Run();

    private sealed class Reader(string path, bool privileged, CancellationToken ct)
    {
        private readonly List<InspectionSection> _sections = [];
        private readonly List<string> _warnings = [];
        private SafeFileHandle _item = null!;
        private string _fileSystem = "";
        private bool _ntfs, _refs, _remote;
        private long _created, _accessed, _modified, _changed;
        private uint _attributes;
        private long _size = -1, _allocated = -1;
        private bool _isFolder;
        private int _links;
        private UInt128? _id;
        private UsnRecord? _latest;
        private NtfsRecord? _record;
        private UsnJournalInfo? _journal;
        private string? _journalProblem;
        private List<UsnRecord> _history = [];
        private List<UsnRecord> _sameName = [];
        private DateTime? _formatted;
        private int _clusterSize;
        private uint _reparseTag;

        public InspectionReport Run()
        {
            using var item = OpenItem(path);
            _item = item;
            string root = VolumeRoot(path);
            var (label, serial, fileSystem) = VolumeInformation(item);
            _fileSystem = fileSystem;
            _ntfs = fileSystem == "NTFS";
            _refs = fileSystem == "ReFS";
            var remote = RemoteProtocol(item);
            _remote = remote is not null || PathUtil.IsUncPath(path);
            ReadBasics();
            _clusterSize = _remote ? 0 : ClusterSize(root);
            _latest = FileUsn(item);
            if ((_ntfs || _refs) && !_remote) _formatted = RootCreated(root);

            if (privileged && (_ntfs || _refs) && !_remote) ReadPrivileged(root);

            _sections.Add(ItemSection(root, label, serial));
            // Said before the parts it would add, so a partial report is known to be one without scrolling.
            if ((_ntfs || _refs) && !_remote && !privileged)
                _sections.Add(new InspectionSection("Needs administrator rights", [])
                {
                    Lines = Wrap(_ntfs
                        ? "Run FileCat as administrator to add the MFT record — the $FILE_NAME times that the strongest timestamp checks compare, every attribute, and the record's slack — and this item's history in the change journal."
                        : "Run FileCat as administrator to add this item's history in the change journal."),
                });
            _sections.Add(TimesSection());
            if (_record is { Names.Count: > 0 } record) _sections.Add(NamesSection(record));
            _sections.Add(ChecksSection());
            if (!_remote && (_ntfs || _refs)) _sections.Add(JournalSection());
            if (ObjectIdSection() is { } objectId) _sections.Add(objectId);
            if (ReparseSection() is { } reparse) _sections.Add(reparse);
            _sections.Add(LayoutSection());
            if (SecuritySection() is { } security) _sections.Add(security);
            if (remote is not null) _sections.Add(remote);
            if (_record is not null) _sections.Add(RecordSection(_record));
            return new InspectionReport($"File-system record · {(_remote ? "network file on " : "")}{fileSystem}", _sections, _warnings);
        }

        // ---- Reading ----------------------------------------------------------------------------------------

        private void ReadBasics()
        {
            Span<byte> basic = stackalloc byte[40];
            if (Info(_item, 0 /* FileBasicInfo */, basic))
            {
                _created = BinaryPrimitives.ReadInt64LittleEndian(basic);
                _accessed = BinaryPrimitives.ReadInt64LittleEndian(basic[8..]);
                _modified = BinaryPrimitives.ReadInt64LittleEndian(basic[16..]);
                _changed = BinaryPrimitives.ReadInt64LittleEndian(basic[24..]);
                _attributes = BinaryPrimitives.ReadUInt32LittleEndian(basic[32..]);
            }
            else _warnings.Add("Its times and attributes could not be read: " + LastError());
            Span<byte> standard = stackalloc byte[24];
            if (Info(_item, 1 /* FileStandardInfo */, standard))
            {
                _allocated = BinaryPrimitives.ReadInt64LittleEndian(standard);
                _size = BinaryPrimitives.ReadInt64LittleEndian(standard[8..]);
                _links = (int)BinaryPrimitives.ReadUInt32LittleEndian(standard[16..]);
                _isFolder = standard[21] != 0;
            }
            else _isFolder = (_attributes & 0x10) != 0;
            Span<byte> id = stackalloc byte[24];
            if (Info(_item, 18 /* FileIdInfo */, id)) _id = BinaryPrimitives.ReadUInt128LittleEndian(id[8..]);
            Span<byte> tag = stackalloc byte[8];
            if ((_attributes & 0x400) != 0 && Info(_item, 9 /* FileAttributeTagInfo */, tag)) _reparseTag = BinaryPrimitives.ReadUInt32LittleEndian(tag[4..]);
        }

        private void ReadPrivileged(string root)
        {
            SafeFileHandle volume;
            try { volume = UsnJournalReader.OpenVolume(root); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _warnings.Add(ex.Message);
                return;
            }
            using (volume)
            {
                if (_ntfs && _id is { } id)
                {
                    try
                    {
                        _record = MftRecord(volume, UsnRecord.RecordOf(id));
                        if (MftRecord(volume, 0)?.Standard is { } mft) _formatted = RecordText.ToUtc(mft.Created) ?? _formatted;
                    }
                    catch (IOException ex) { _warnings.Add("The MFT record could not be read: " + ex.Message); }
                    if (_record is not null)
                    {
                        if (_record.Sequence != UsnRecord.SequenceOf(id))
                            _warnings.Add($"The MFT record's sequence number ({_record.Sequence}) is not the item's ({UsnRecord.SequenceOf(id)}): it changed while being read.");
                        foreach (var problem in _record.Problems) _warnings.Add("MFT record: " + problem);
                    }
                }
                try { ReadJournal(volume); }
                catch (IOException ex) { _journalProblem = "The change journal could not be read: " + ex.Message; }
            }
        }

        /// <summary>The record and its extension records, with an attribute list kept outside the record read from its clusters.</summary>
        private NtfsRecord? MftRecord(SafeFileHandle volume, long number)
        {
            var volumeData = new byte[128];
            int recordSize = Ioctl(volume, FsctlGetNtfsVolumeData, [], volumeData, out int got) == 0 && got >= 48
                ? (int)BinaryPrimitives.ReadUInt32LittleEndian(volumeData.AsSpan(44)) : 1024;
            if (recordSize is < 256 or > 65536) recordSize = 1024;
            var raw = RecordBytes(volume, number, recordSize);
            if (raw is null) return null;
            var record = NtfsRecord.Parse(raw, number, live: true);
            if (record.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeTypes.AttributeList && !a.Resident) is { } list && _clusterSize > 0)
                record.AddAttributeList(ReadRuns(volume, list, 1 << 20));
            foreach (long extension in record.AttributeList.Select(e => e.Record).Where(r => r != number).Distinct().Take(64))
            {
                ct.ThrowIfCancellationRequested();
                if (RecordBytes(volume, extension, recordSize) is { } more) record.AddExtension(more, extension);
                else record.Problems.Add($"Extension record {extension:N0} could not be read.");
            }
            return record;
        }

        private static byte[]? RecordBytes(SafeFileHandle volume, long number, int recordSize)
        {
            Span<byte> input = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(input, number);
            var output = new byte[12 + recordSize];
            int error = Ioctl(volume, FsctlGetNtfsFileRecord, input, output, out int got);
            if (error != 0) throw new IOException(new Win32Exception(error).Message) { HResult = error };
            if (got < 12) return null;
            // A record not in use is answered with the nearest one below it that is.
            long answered = BinaryPrimitives.ReadInt64LittleEndian(output) & 0xFFFF_FFFF_FFFF;
            int length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(output.AsSpan(8)), (uint)(got - 12));
            return answered == number ? output.AsSpan(12, length).ToArray() : null;
        }

        private byte[] ReadRuns(SafeFileHandle volume, NtfsAttribute attribute, int max)
        {
            var data = new byte[(int)Math.Min(Math.Max(0, attribute.Size), max)];
            foreach (var run in attribute.Runs)
            {
                long start = run.Vcn * _clusterSize;
                if (run.IsSparse || start >= data.Length) continue;
                int count = (int)Math.Min(run.Length * _clusterSize, data.Length - start);
                // Volume reads must be whole sectors: read whole clusters, then keep what belongs to the value.
                var clusters = new byte[(count + _clusterSize - 1) / _clusterSize * _clusterSize];
                int read = RandomAccess.Read(volume, clusters, run.Lcn * _clusterSize);
                clusters.AsSpan(0, Math.Min(count, read)).CopyTo(data.AsSpan((int)start));
            }
            return data;
        }

        private void ReadJournal(SafeFileHandle volume)
        {
            _journal = UsnJournalReader.Query(volume, out int error);
            if (error == UsnJournalReader.ErrorJournalNotActive)
            {
                _journalProblem = "The change journal is off on this volume: nothing records its changes.";
                return;
            }
            if (error != 0) throw new IOException(new Win32Exception(error).Message);
            if (_journal is null || _id is not { } id) return;
            string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            UInt128? parent = _latest?.ParentId;
            var mine = new Queue<UsnRecord>();
            var others = new Queue<UsnRecord>();
            UsnJournalReader.Read(volume, _journal, Math.Max(_journal.FirstUsn, _journal.NextUsn - JournalReadLimit), ct, records =>
            {
                foreach (var r in records)
                {
                    if (r.FileId == id) Keep(mine, r);
                    else if (parent is { } p && r.ParentId == p && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)) Keep(others, r);
                }
            });
            _history = [.. mine];
            _sameName = [.. others];

            static void Keep(Queue<UsnRecord> kept, UsnRecord record)
            {
                kept.Enqueue(record);
                if (kept.Count > HistoryKept) kept.Dequeue();
            }
        }

        // ---- Sections ---------------------------------------------------------------------------------------

        private InspectionSection ItemSection(string root, string label, uint serial)
        {
            var fields = new List<(string, string)> { ("Path", path) };
            if (FinalPath(_item) is { } real && !string.Equals(real, path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && !string.Equals(real, path, StringComparison.OrdinalIgnoreCase))
                fields.Add(("Real path", real));
            fields.Add(("Kind", Kind()));
            fields.Add(("Volume", $"{root} · {_fileSystem}{(label.Length > 0 ? $" “{label}”" : "")} · serial {serial >> 16:X4}-{serial & 0xFFFF:X4}"));
            if (_id is { } id)
            {
                if (_ntfs && !_remote)
                {
                    fields.Add(("MFT record", $"{UsnRecord.RecordOf(id):N0} (0x{UsnRecord.RecordOf(id):X}), sequence {UsnRecord.SequenceOf(id)}"));
                    fields.Add(("File ID", $"0x{(ulong)id:X16}"));
                }
                else fields.Add(("File ID", id >> 64 == 0 ? $"0x{(ulong)id:X16}" : $"0x{id:X32}"));
            }
            // A file with several names has a parent for each: the names below (or its $FILE_NAMEs) say them.
            if (_latest is { } usn && !_remote && _links <= 1)
                fields.Add(("Parent", (_ntfs ? $"record {UsnRecord.RecordOf(usn.ParentId):N0}, sequence {UsnRecord.SequenceOf(usn.ParentId)}" : $"ID 0x{usn.ParentId:X}")
                    + (PathOfId(usn.ParentId) is { } parentPath ? " · " + parentPath : "")));
            fields.Add(("Attributes", RecordText.Attributes(_attributes)));
            if (ShortName() is { } alias) fields.Add(("8.3 name", alias));
            var lines = new List<string>();
            if (_links > 1)
            {
                fields.Add(("Hard links", $"{_links} names, all of them this same file:"));
                lines.AddRange(Links(root));
            }
            else if (_links == 1) fields.Add(("Hard links", "1 (this name only)"));
            return new InspectionSection("Item", fields) { Lines = lines };
        }

        private string Kind()
        {
            string what = _isFolder ? "folder" : "file";
            if ((_attributes & 0x400) == 0) return what;
            return _reparseTag switch
            {
                0xA000_000C => $"symbolic link ({what})",
                0xA000_0003 => "junction (mount point)",
                _ => $"{what} with a reparse point",
            };
        }

        private InspectionSection TimesSection()
        {
            bool fat = _fileSystem is "FAT" or "FAT32" or "FAT12" or "FAT16";
            var fields = new List<(string, string)> { ("Created", RecordText.Time(_created)), ("Modified", RecordText.Time(_modified)) };
            if (_ntfs || _refs) fields.Add((_ntfs ? "MFT changed" : "Changed", RecordText.Time(_changed)));
            fields.Add(("Accessed", RecordText.Time(_accessed)));
            var offset = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
            string zone = $"UTC{(offset < TimeSpan.Zero ? "−" : "+")}{offset:hh\\:mm}";
            string note = fat ? $"UTC; this computer is {zone}. FAT keeps modification times to 2 seconds, creation times to 10 ms, and access as a date only."
                : _fileSystem == "exFAT" ? $"UTC; this computer is {zone}. exFAT keeps times to 10 ms (access to 2 seconds) with the time zone they were written in."
                : $"UTC, to the 100 ns the file system keeps; this computer is {zone}." +
                  (_ntfs ? " These are the times Windows shows, and programs can set them; when one sets the others, NTFS moves the MFT change time to the present unless it sets that too." : "");
            return new InspectionSection(_ntfs ? "Times ($STANDARD_INFORMATION)" : "Times", fields) { Lines = Wrap(note) };
        }

        private InspectionSection NamesSection(NtfsRecord record)
        {
            var children = new List<InspectionSection>();
            foreach (var name in record.Names.OrderBy(n => n.IsDosAlias))
            {
                var fields = new List<(string, string)>
                {
                    ("Created", RecordText.Time(name.Created)),
                    ("Modified", RecordText.Time(name.Modified)),
                    ("MFT changed", RecordText.Time(name.Changed)),
                    ("Accessed", RecordText.Time(name.Accessed)),
                    ("Size as noted", $"{name.Size:N0} bytes, {name.Allocated:N0} allocated"),
                    ("Flags", RecordText.NtfsAttributes(name.Flags)),
                };
                if ((name.Flags & 0x400) != 0 && name.ReparseOrEa != 0) fields.Add(("Reparse tag", RecordText.ReparseTag(name.ReparseOrEa)));
                else if (name.ReparseOrEa != 0) fields.Add(("Extended data", $"0x{name.ReparseOrEa:X8} (with EAs: their size, as NTFS noted it)"));
                string folder = PathOfId((UInt128)((ulong)name.ParentSequence << 48 | (ulong)name.ParentRecord)) ?? $"folder record {name.ParentRecord:N0}";
                children.Add(new InspectionSection($"“{name.Name}” · {name.NamespaceText} · in {folder}", fields));
            }
            return new InspectionSection("Names ($FILE_NAME)", [])
            {
                Lines = Wrap("NTFS writes these when a name is made or changed; programs cannot set them. It updates the sizes lazily, so they may be out of date."),
                Children = children,
            };
        }

        private InspectionSection ChecksSection()
        {
            var names = _record?.Names ?? [];
            var findings = TimestampChecks.Check(_created, _modified, _ntfs || _refs ? _changed : 0, _accessed, names, _history, _formatted, DateTime.UtcNow);
            var lines = new List<string>();
            foreach (var finding in findings) lines.AddRange(Wrap(finding.Text, finding.Strong ? "⚠ " : "• "));
            if (findings.Count == 0) lines.Add("Nothing unusual in what can be compared here.");
            if (_ntfs && names.Count == 0 && !_remote)
                lines.AddRange(Wrap("Without the MFT record (administrator rights), the times programs cannot set ($FILE_NAME) are not compared: the strongest checks did not run."));
            int strong = findings.Count(f => f.Strong);
            if (strong > 0) _warnings.Insert(0, $"Timestamp checks: {strong} sign{(strong == 1 ? "" : "s")} that its times were set rather than kept (see below).");
            return new InspectionSection("Timestamp checks", []) { Lines = lines };
        }

        private InspectionSection JournalSection()
        {
            var fields = new List<(string, string)>();
            var lines = new List<string>();
            var children = new List<InspectionSection>();
            if (_journalProblem is not null && _journal is null)
            {
                lines.AddRange(Wrap(_journalProblem));
            }
            else if (_latest is { } usn)
            {
                if (usn.Usn == 0)
                    lines.AddRange(Wrap(_journal is null
                        ? "No change of it is journaled: the journal is off, or it has not changed since the journal began."
                        : "No change of it is journaled since the journal began."));
                else
                {
                    // The item's own USN names its latest journal entry; the entry itself (time, reasons) is in the journal.
                    var entry = _history.LastOrDefault(r => r.Usn == usn.Usn);
                    fields.Add(("Latest USN", usn.Usn.ToString("N0", CultureInfo.CurrentCulture) +
                        (entry is not null ? $" · {RecordText.Time(entry.Time)} · {UsnRecord.ReasonsText(entry.Reasons)}" : "")));
                    if (entry is not null && UsnRecord.SourceText(entry.SourceInfo) is { } source) fields.Add(("Made by", source));
                    if (entry is null && _journal is { } current && usn.Usn < current.FirstUsn)
                        lines.AddRange(Wrap("Its latest change is older than the journal's oldest entry, so the journal no longer says what it was."));
                    else if (entry is null && _journal is null && !privileged)
                        lines.AddRange(Wrap("The journal's entries (when and what) need administrator rights to read."));
                }
            }
            else lines.Add("Its latest journal entry could not be read.");
            if (_journal is { } j)
            {
                fields.Add(("Journal", $"ID 0x{j.JournalId:X16} · USNs {j.FirstUsn:N0} to {j.NextUsn:N0}"));
                fields.Add(("Journal size", $"keeps about {RecordText.Short(j.MaximumSize)}, trimmed {RecordText.Short(j.AllocationDelta)} at a time" +
                    (j.RangeTracking ? " · range tracking on" : "")));
                if (_history.Count > 0)
                {
                    var rows = _history.TakeLast(HistoryShown).Select(r => new[] { RecordText.Time(r.Time), r.Usn.ToString("N0", CultureInfo.CurrentCulture), UsnRecord.ReasonsText(r.Reasons), r.Name }).ToList();
                    children.Add(new InspectionSection("History of this item (oldest first)", [])
                    {
                        Table = new InspectionTable(["Time", "USN", "What happened", "Name"], rows)
                        {
                            More = _history.Count > HistoryShown ? $"{_history.Count - HistoryShown:N0} earlier entries are not listed." : null,
                        },
                    });
                }
                else lines.AddRange(Wrap("The journal holds nothing about this item: its entries were overwritten (the journal keeps only the newest), or it has not changed since the journal began."));
                if (_sameName.Count > 0)
                {
                    var rows = _sameName.TakeLast(HistoryShown).Select(r => new[]
                    {
                        RecordText.Time(r.Time), _ntfs ? $"{UsnRecord.RecordOf(r.FileId):N0}/{UsnRecord.SequenceOf(r.FileId)}" : $"0x{r.FileId:X}",
                        UsnRecord.ReasonsText(r.Reasons),
                    }).ToList();
                    children.Add(new InspectionSection("Other items that had this name in this folder", [])
                    {
                        Lines = Wrap("Earlier files by this name: programs that save by writing a new file and renaming it over the old one leave these, as do deleted and replaced files."),
                        Table = new InspectionTable(["Time", _ntfs ? "Record/sequence" : "File ID", "What happened"], rows),
                    });
                }
            }
            else if (_journalProblem is not null && _journal is not null) lines.AddRange(Wrap(_journalProblem));
            return new InspectionSection("Change journal ($UsnJrnl)", fields) { Lines = lines, Children = children };
        }

        private InspectionSection? ObjectIdSection()
        {
            var output = new byte[64];
            if (Ioctl(_item, FsctlGetObjectId, [], output, out int got) != 0 || got < 16) return null;
            var ids = output.AsSpan();
            var fields = new List<(string, string)>();
            void Add(string name, ReadOnlySpan<byte> bytes)
            {
                var guid = new Guid(bytes);
                if (guid == Guid.Empty) return;
                fields.Add((name, guid.ToString("B") + (RecordText.GuidOrigin(guid) is { } origin ? " · " + origin : "")));
            }
            Add("Object ID", ids[..16]);
            if (got >= 64)
            {
                Add("Birth volume", ids.Slice(16, 16));
                Add("Birth object", ids.Slice(32, 16));
                Add("Domain", ids.Slice(48, 16));
            }
            return new InspectionSection("Object ID ($OBJECT_ID)", fields)
            {
                Lines = Wrap("Windows gives an item an object ID when a shortcut or link tracking needs to find it after it moves; the birth IDs say where it first got one."),
            };
        }

        private InspectionSection? ReparseSection()
        {
            if ((_attributes & 0x400) == 0) return null;
            var output = new byte[16 * 1024];
            if (Ioctl(_item, FsctlGetReparsePoint, [], output, out int got) != 0 || got < 8)
                return new InspectionSection("Reparse point", [("Tag", RecordText.ReparseTag(_reparseTag))]) { Lines = ["Its data could not be read: " + LastError()] };
            uint tag = BinaryPrimitives.ReadUInt32LittleEndian(output);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(4));
            int start = (tag & 0x8000_0000) != 0 ? 8 : 24;
            var data = output.AsSpan(start, Math.Max(0, Math.Min(length, got - start)));
            var fields = new List<(string, string)> { ("Tag", RecordText.ReparseTag(tag)) };
            if (start == 24) fields.Add(("Vendor", new Guid(output.AsSpan(8, 16)).ToString("B")));
            if (RecordText.ReparseTarget(tag, data) is { } target) fields.Add(("Says", target));
            fields.Add(("Data", $"{length:N0} bytes"));
            return new InspectionSection("Reparse point", fields) { Lines = RecordText.HexLines(data, 128) };
        }

        private InspectionSection LayoutSection()
        {
            var fields = new List<(string, string)>();
            if (!_isFolder) fields.Add(("Size", RecordText.Bytes(_size)));
            fields.Add(("Allocated", RecordText.Bytes(_allocated)));
            Span<byte> compression = stackalloc byte[16];
            if (!_isFolder && Info(_item, 8 /* FileCompressionInfo */, compression))
            {
                long onDisk = BinaryPrimitives.ReadInt64LittleEndian(compression);
                ushort format = BinaryPrimitives.ReadUInt16LittleEndian(compression[8..]);
                if (format != 0)
                {
                    fields.Add(("Compression", format switch { 2 => "LZNT1 (NTFS compression)", 3 => "XPRESS", 4 => "XPRESS Huffman", 5 => "LZNT1 standard", _ => $"format {format}" } +
                        $", in units of {RecordText.Bytes(1L << compression[10])}"));
                    fields.Add(("On disk", RecordText.Bytes(onDisk)));
                }
            }
            if (_clusterSize > 0) fields.Add(("Cluster size", RecordText.Bytes(_clusterSize)));
            if ((_attributes & 0x200) != 0 && !_isFolder) fields.Add(("Sparse", SparseRanges()));
            var data = _record?.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeTypes.Data && a.Name.Length == 0 && a.StartVcn == 0);
            if (data is { Resident: true })
                fields.Add(("Resident", $"yes: its {data.Size:N0} bytes are kept inside its MFT record, in no cluster of their own"));
            var lines = new List<string>();
            InspectionTable? table = null;
            if (!_remote)
            {
                var (extents, error) = Extents();
                if (extents.Count > 0)
                {
                    int fragments = CountFragments(extents);
                    fields.Add((_isFolder ? "Index clusters" : "Fragments", $"{fragments:N0} ({extents.Count:N0} extent{(extents.Count == 1 ? "" : "s")} on the volume)"));
                    var rows = extents.Take(ExtentsShown).Select(e => new[]
                    {
                        e.Vcn.ToString("N0", CultureInfo.CurrentCulture), e.IsSparse ? "—" : e.Lcn.ToString("N0", CultureInfo.CurrentCulture),
                        e.Length.ToString("N0", CultureInfo.CurrentCulture),
                        e.IsSparse ? (_record is null && (_attributes & 0x800) != 0 ? "compressed away" : "sparse (nothing stored)") : _clusterSize > 0 ? $"at byte {e.Lcn * _clusterSize:N0}" : "",
                    }).ToList();
                    table = new InspectionTable(["VCN", "LCN", "Clusters", "Where"], rows)
                    {
                        More = extents.Count > ExtentsShown ? $"{extents.Count - ExtentsShown:N0} more extents are not listed." : null,
                    };
                }
                else if (error == ErrorHandleEof || error == 0)
                {
                    if (_isFolder) fields.Add(("Index", _ntfs ? "small enough to be kept inside its MFT record ($INDEX_ROOT): no clusters of its own" : "no clusters of its own"));
                    else if (data is null && _ntfs && _size > 0)
                        fields.Add(("Resident", "probably: no cluster holds it, so its content is kept inside its MFT record"));
                    else if (_size == 0) fields.Add(("Clusters", "none"));
                }
                else if (error is not (1 /* not supported */ or 50)) lines.Add("Its clusters could not be listed: " + new Win32Exception(error).Message);
            }
            if (_isFolder && table is not null) lines.AddRange(Wrap("A folder's clusters hold its index of names ($I30)."));
            return new InspectionSection("Layout on disk", fields) { Table = table, Lines = lines };
        }

        private static int CountFragments(List<NtfsRun> extents)
        {
            int fragments = 0;
            long end = -1;
            foreach (var e in extents)
            {
                if (e.IsSparse) continue;
                if (e.Lcn != end) fragments++;
                end = e.Lcn + e.Length;
            }
            return fragments;
        }

        private (List<NtfsRun> Extents, int Error) Extents()
        {
            var runs = new List<NtfsRun>();
            var output = new byte[64 * 1024];
            Span<byte> input = stackalloc byte[8];
            long vcn = 0;
            while (runs.Count < MaxExtents)
            {
                BinaryPrimitives.WriteInt64LittleEndian(input, vcn);
                int error = Ioctl(_item, FsctlGetRetrievalPointers, input, output, out int got);
                if (error is not (0 or ErrorMoreData)) return (runs, runs.Count > 0 ? 0 : error);
                if (got < 16) break;
                int count = (int)BinaryPrimitives.ReadUInt32LittleEndian(output);
                long previous = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(8));
                for (int i = 0; i < count && 16 + i * 16 + 16 <= got; i++)
                {
                    long next = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(16 + i * 16));
                    long lcn = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(24 + i * 16));
                    runs.Add(new NtfsRun(previous, lcn, next - previous));
                    previous = next;
                }
                if (error == 0 || count == 0) break;
                vcn = previous;
            }
            return (runs, 0);
        }

        private string SparseRanges()
        {
            using var handle = CreateFile(WindowsFileOperations.Long(path), FileReadData | Synchronize, ShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
            if (handle.IsInvalid) return "yes (its stored ranges cannot be read: " + LastError() + ")";
            var ranges = new List<(long Offset, long Length)>();
            Span<byte> input = stackalloc byte[16];
            var output = new byte[16 * 1024];
            long offset = 0;
            while (offset < _size && ranges.Count < 100_000)
            {
                BinaryPrimitives.WriteInt64LittleEndian(input, offset);
                BinaryPrimitives.WriteInt64LittleEndian(input[8..], _size - offset);
                int error = Ioctl(handle, FsctlQueryAllocatedRanges, input, output, out int got);
                if (error is not (0 or ErrorMoreData)) return "yes (its stored ranges cannot be read: " + new Win32Exception(error).Message + ")";
                for (int at = 0; at + 16 <= got; at += 16)
                    ranges.Add((BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(at)), BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(at + 8))));
                if (error == 0 || got < 16) break;
                offset = ranges[^1].Offset + ranges[^1].Length;
            }
            long stored = ranges.Sum(r => r.Length);
            string list = string.Join(", ", ranges.Take(8).Select(r => $"{r.Offset:N0}–{r.Offset + r.Length:N0}"));
            return $"{stored:N0} of {_size:N0} bytes stored, in {ranges.Count:N0} range{(ranges.Count == 1 ? "" : "s")}" + (ranges.Count > 0 ? ": " + list + (ranges.Count > 8 ? ", …" : "") : "");
        }

        private InspectionSection? SecuritySection()
        {
            const uint Owner = 1, Group = 2, Dacl = 4, Label = 0x10;
            int error = GetSecurityInfo(_item, 1 /* SE_FILE_OBJECT */, Owner | Group | Dacl | Label, out nint owner, out nint group, out _, out _, out nint descriptor);
            if (error != 0)
                return new InspectionSection("Security", []) { Lines = [$"Its security descriptor cannot be read: {new Win32Exception(error).Message}"] };
            try
            {
                var fields = new List<(string, string)>();
                if (owner != 0) fields.Add(("Owner", Account(owner)));
                if (group != 0) fields.Add(("Group", Account(group)));
                if (_record?.Standard is { HasExtendedFields: true } si) fields.Add(("Security ID", $"{si.SecurityId} (its descriptor's entry in $Secure)"));
                var children = new List<InspectionSection>();
                if (ConvertSecurityDescriptorToStringSecurityDescriptor(descriptor, 1, Owner | Group | Dacl | Label, out nint text, out _))
                {
                    try
                    {
                        var names = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                        string? Name(string sid)
                        {
                            if (!names.TryGetValue(sid, out var name)) names[sid] = name = AccountName(sid);
                            return name;
                        }
                        string sddl = Marshal.PtrToStringUni(text) ?? "";
                        var access = AccessText.Describe(sddl, _isFolder, Name);
                        if (access.Dacl is not null) fields.Add(("Permissions", access.Dacl));
                        foreach (var finding in access.Findings.Where(f => f.Strong)) _warnings.Add("Permissions: " + finding.Text);
                        if (access.Entries.Count > 0)
                        {
                            // Who last: an account's name or an unknown SID can be long, and the other columns stay in view.
                            string[] columns = _isFolder ? ["Type", "Rights", "Applies to", "Inherited", "Who"] : ["Type", "Rights", "Inherited", "Who"];
                            var rows = access.Entries.Select(e => _isFolder
                                ? new[] { e.Type, e.Rights, e.AppliesTo, e.Inherited ? "yes" : "", e.Who }
                                : new[] { e.Type, e.Rights, e.Inherited ? "yes" : "", e.Who }).ToList();
                            var lines = new List<string>();
                            foreach (var finding in access.Findings) lines.AddRange(Wrap(finding.Text, finding.Strong ? "⚠ " : "• "));
                            children.Add(new InspectionSection("Access", []) { Table = new InspectionTable(columns, rows), Lines = lines });
                        }
                        children.Add(new InspectionSection("SDDL", []) { Lines = AccessText.SddlLines(sddl) });
                    }
                    finally { LocalFree(text); }
                }
                return new InspectionSection("Security", fields) { Children = children };
            }
            finally { LocalFree(descriptor); }
        }

        private static string Account(nint sid)
        {
            var id = new SecurityIdentifier(sid);
            return AccountName(id.Value) is { } name ? $"{name} ({id.Value})" : id.Value + " (no account by this ID here)";
        }

        /// <summary>The account a SID string or SDDL alias ("BA") names, or null.</summary>
        private static string? AccountName(string sid)
        {
            try { return new SecurityIdentifier(sid).Translate(typeof(NTAccount)).Value; }
            catch (Exception ex) when (ex is ArgumentException or SystemException) { return null; }
        }

        private InspectionSection RecordSection(NtfsRecord record)
        {
            var state = new List<string> { record.InUse ? "in use" : "free" };
            if (record.IsDirectory) state.Add("folder");
            if (record.InExtend) state.Add("in $Extend");
            if (record.HasViewIndex) state.Add("has a view index");
            var fields = new List<(string, string)>
            {
                ("Record", $"{record.Number:N0}, sequence {record.Sequence}"),
                ("State", string.Join(", ", state)),
                ("Links", record.LinkCount.ToString(CultureInfo.CurrentCulture)),
                ("$LogFile LSN", $"{record.Lsn:N0} (0x{record.Lsn:X}): where its last change is logged"),
                ("Space", $"{record.UsedSize:N0} of {record.AllocatedSize:N0} bytes used"),
                ("Next attribute ID", record.NextAttributeId.ToString(CultureInfo.CurrentCulture)),
            };
            if (record.Standard is { HasExtendedFields: true } si)
            {
                fields.Add(("USN (in $STANDARD_INFORMATION)", si.Usn.ToString("N0", CultureInfo.CurrentCulture)));
                if (si.OwnerId != 0) fields.Add(("Quota owner ID", si.OwnerId.ToString(CultureInfo.CurrentCulture)));
                if (si.QuotaCharged != 0) fields.Add(("Quota charged", RecordText.Bytes(si.QuotaCharged)));
            }
            if (record.Standard is { } s && (s.MaxVersions != 0 || s.Version != 0 || s.ClassId != 0))
                fields.Add(("Undocumented", $"0x{s.MaxVersions:X8} 0x{s.Version:X8} 0x{s.ClassId:X8} (once versions and class ID)"));
            if (record.EaInformation is { } ea) fields.Add(("EAs", $"{ea.PackedSize:N0} bytes packed, {ea.UnpackedSize:N0} unpacked, {ea.NeedEa} needed to open it"));
            var rows = record.Attributes.Select(a => new[]
            {
                a.Id.ToString(CultureInfo.CurrentCulture), a.TypeName, a.Name, a.Resident ? "resident" : $"runs from VCN {a.StartVcn:N0}",
                a.Size.ToString("N0", CultureInfo.CurrentCulture), a.Allocated.ToString("N0", CultureInfo.CurrentCulture), AttributeFlags(a),
                a.InRecord == record.Number ? "" : a.InRecord.ToString("N0", CultureInfo.CurrentCulture),
            }).ToList();
            var children = new List<InspectionSection>();
            if (record.AttributeList.Count > 0)
                children.Add(new InspectionSection("Attribute list", [])
                {
                    Table = new InspectionTable(["Type", "Name", "From VCN", "In record", "ID"], record.AttributeList.Select(e => new[]
                    {
                        NtfsAttributeTypes.Name(e.Type), e.Name, e.StartVcn.ToString("N0", CultureInfo.CurrentCulture),
                        $"{e.Record:N0}/{e.RecordSequence}", e.Id.ToString(CultureInfo.CurrentCulture),
                    }).ToList()),
                });
            if (record.Attributes.FirstOrDefault(a => a.Type == NtfsAttributeTypes.Data && a.Name.Length == 0 && a.Resident) is { Value.Length: > 0 } resident)
                children.Add(new InspectionSection($"Resident content ({resident.Size:N0} bytes)", []) { Lines = RecordText.HexLines(resident.Value, 256) });
            int nonzero = record.Slack.Count(b => b != 0);
            if (nonzero > 0)
                children.Add(new InspectionSection($"Record slack ({record.Slack.Length:N0} bytes after its attributes, {nonzero:N0} not zero)", [])
                {
                    Lines = [.. Wrap("What earlier, longer versions of this record left behind: parts of old names, attributes, or small files' content."), .. RecordText.HexLines(record.Slack, 256)],
                });
            return new InspectionSection("MFT record", fields)
            {
                Table = new InspectionTable(["ID", "Type", "Name", "Form", "Size", "Allocated", "Flags", "In record"], rows),
                Children = children,
            };
        }

        private static string AttributeFlags(NtfsAttribute a)
        {
            var flags = new List<string>();
            if (a.IsCompressed) flags.Add("compressed");
            if (a.IsEncrypted) flags.Add("encrypted");
            if (a.IsSparse) flags.Add("sparse");
            if (a.Indexed) flags.Add("indexed");
            return string.Join(", ", flags);
        }

        // ---- Helpers ----------------------------------------------------------------------------------------

        private string? PathOfId(UInt128 id)
        {
            Span<byte> descriptor = stackalloc byte[24];
            BinaryPrimitives.WriteInt32LittleEndian(descriptor, 24);
            bool extended = id >> 64 != 0 || _refs;
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[4..], extended ? 2 : 0);
            if (extended) BinaryPrimitives.WriteUInt128LittleEndian(descriptor[8..], id);
            else BinaryPrimitives.WriteUInt64LittleEndian(descriptor[8..], (ulong)id);
            SafeFileHandle handle;
            fixed (byte* d = descriptor) handle = OpenFileById(_item, d, FileReadAttributes | Synchronize, ShareAll, 0, BackupSemantics | OpenReparsePoint);
            using (handle) return handle.IsInvalid ? null : FinalPath(handle);
        }

        private string? ShortName()
        {
            if (Path.GetPathRoot(path) is { } root && string.Equals(root.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return null;
            var data = new byte[592];
            nint find;
            fixed (byte* d = data) find = FindFirstFile(WindowsFileOperations.Long(path.TrimEnd('\\')), d);
            if (find == -1) return null;
            FindClose(find);
            string alias = MemoryMarshal.Cast<byte, char>(data.AsSpan(564, 28)).ToString().Split('\0')[0];
            return alias.Length > 0 ? alias : "none (its name fits 8.3, or 8.3 names are off on this volume)";
        }

        private List<string> Links(string root)
        {
            // Each name comes relative to the volume's root ("\Folder\file.txt").
            var links = new List<string>();
            char* buffer = stackalloc char[1024];
            uint length = 1024;
            nint find = FindFirstFileName(WindowsFileOperations.Long(path), 0, &length, buffer);
            if (find == -1) return [$"(its names could not be listed: {LastError()})"];
            try
            {
                do
                {
                    links.Add(root.TrimEnd('\\') + new string(buffer));
                    length = 1024;
                } while (links.Count < 1024 && FindNextFileName(find, &length, buffer));
            }
            finally { FindClose(find); }
            return links;
        }

        private InspectionSection? RemoteProtocol(SafeFileHandle handle)
        {
            Span<byte> info = stackalloc byte[116];
            if (!Info(handle, 13 /* FileRemoteProtocolInfo */, info)) return null;
            uint protocol = BinaryPrimitives.ReadUInt32LittleEndian(info[4..]);
            int major = BinaryPrimitives.ReadUInt16LittleEndian(info[8..]), minor = BinaryPrimitives.ReadUInt16LittleEndian(info[10..]), revision = BinaryPrimitives.ReadUInt16LittleEndian(info[12..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(info[16..]);
            string name = protocol switch
            {
                0x0002_0000 => "SMB",
                0x0042_0000 => "NFS",
                0x002E_0000 => "WebDAV",
                0x0036_0000 => "Remote Desktop drive redirection",
                0x0048_0000 => "Plan 9 (WSL)",
                0x0026_0000 => "Offline Files",
                _ => $"network provider 0x{protocol:X8}",
            };
            string version = revision > 0 ? $"{major}.{minor}.{revision}" : $"{major}.{minor}";
            var fields = new List<(string, string)> { ("Protocol", $"{name} {version}") };
            var connection = Words(flags, [(1, "loopback (this computer)"), (2, "offline"), (4, "persistent handle"), (8, "encrypted"), (0x10, "signed"), (0x20, "mutually authenticated")]);
            if (connection.Length > 0) fields.Add(("Connection", connection));
            if (protocol == 0x0002_0000 && major >= 2)
            {
                string server = Words(BinaryPrimitives.ReadUInt32LittleEndian(info[52..]), [(1, "DFS"), (2, "leasing"), (4, "large MTU"), (8, "multichannel"), (0x10, "persistent handles"), (0x20, "directory leasing"), (0x40, "encryption"), (0x80, "notifications")]);
                string share = Words(BinaryPrimitives.ReadUInt32LittleEndian(info[56..]), [(8, "DFS"), (0x10, "continuous availability"), (0x20, "scale-out"), (0x40, "cluster"), (0x80, "asymmetric"), (0x100, "redirect to owner")]);
                if (server.Length > 0) fields.Add(("Server can", server));
                if (share.Length > 0) fields.Add(("Share is", share));
            }
            return new InspectionSection("Network file", fields);
        }

        private static string Words(uint value, (uint Flag, string Text)[] names) =>
            string.Join(", ", names.Where(n => (value & n.Flag) != 0).Select(n => n.Text));

        private static List<string> Wrap(string text, string first = "", int width = 110) => RecordText.Wrap(text, width, first);
    }

    // ---- Handles and volumes ----------------------------------------------------------------------------------

    private static SafeFileHandle OpenItem(string path)
    {
        string name = WindowsFileOperations.Long(path);
        // Reading attributes only: opening changes no time. READ_CONTROL adds the security descriptor when allowed.
        var handle = CreateFile(name, FileReadAttributes | ReadControl | Synchronize, ShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        if (handle.IsInvalid && Marshal.GetLastPInvokeError() == 5)
        {
            handle.Dispose();
            handle = CreateFile(name, FileReadAttributes | Synchronize, ShareAll, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
        }
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        throw error switch
        {
            2 or 3 => new FileNotFoundException($"{path} does not exist.", path),
            5 => new UnauthorizedAccessException($"{path} cannot be opened: access is denied."),
            _ => new IOException($"{path} cannot be opened: {new Win32Exception(error).Message}") { HResult = error },
        };
    }

    /// <summary>The root of the volume holding the path ("C:\", "C:\Mount\Data\", "\\server\share\").</summary>
    internal static string VolumeRoot(string path)
    {
        var buffer = new char[1024];
        fixed (char* b = buffer)
            if (GetVolumePathName(WindowsFileOperations.Long(path), b, (uint)buffer.Length))
            {
                string root = new string(b);
                if (root.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + root[8..];
                if (root.StartsWith(@"\\?\", StringComparison.Ordinal) && root.Length > 5 && root[5] == ':') root = root[4..];
                return root;
            }
        return Path.GetPathRoot(path) ?? path;
    }

    private static (string Label, uint Serial, string FileSystem) VolumeInformation(SafeFileHandle handle)
    {
        char* label = stackalloc char[261];
        char* fileSystem = stackalloc char[261];
        if (!GetVolumeInformationByHandle(handle, label, 261, out uint serial, out _, out _, fileSystem, 261)) return ("", 0, "unknown file system");
        return (new string(label), serial, new string(fileSystem));
    }

    private static int ClusterSize(string root) =>
        GetDiskFreeSpace(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _) ? (int)Math.Min((long)sectorsPerCluster * bytesPerSector, int.MaxValue) : 0;

    /// <summary>When the volume was formatted, as its root folder's creation time says.</summary>
    private static DateTime? RootCreated(string root)
    {
        using var handle = CreateFile(root, FileReadAttributes | Synchronize, ShareAll, 0, OpenExisting, BackupSemantics, 0);
        Span<byte> basic = stackalloc byte[40];
        return !handle.IsInvalid && Info(handle, 0, basic) ? RecordText.ToUtc(BinaryPrimitives.ReadInt64LittleEndian(basic)) : null;
    }

    private static UsnRecord? FileUsn(SafeFileHandle handle)
    {
        Span<byte> versions = [2, 0, 3, 0];
        var output = new byte[4096];
        int error = Ioctl(handle, FsctlReadFileUsnData, versions, output, out int got);
        if (error == ErrorInvalidParameter) error = Ioctl(handle, FsctlReadFileUsnData, [], output, out got); // before Windows 8
        return error == 0 && UsnRecord.TryParse(output.AsSpan(0, got), out var record) ? record : null;
    }

    private static string? FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[1024];
        uint length;
        fixed (char* b = buffer) length = GetFinalPathNameByHandle(handle, b, (uint)buffer.Length, 0);
        if (length == 0 || length >= buffer.Length) return null;
        string final = new(buffer, 0, (int)length);
        if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + final[8..];
        return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
    }

    private static bool Info(SafeFileHandle handle, int informationClass, Span<byte> buffer)
    {
        fixed (byte* b = buffer) return GetFileInformationByHandleEx(handle, informationClass, b, (uint)buffer.Length);
    }

    private static int Ioctl(SafeHandle handle, uint code, ReadOnlySpan<byte> input, Span<byte> output, out int returned)
    {
        uint got = 0;
        bool ok;
        fixed (byte* i = input)
        fixed (byte* o = output)
            ok = DeviceIoControl(handle, code, input.IsEmpty ? null : i, (uint)input.Length, output.IsEmpty ? null : o, (uint)output.Length, &got, 0);
        returned = (int)got;
        return ok ? 0 : Marshal.GetLastPInvokeError();
    }

    private static string LastError() => new Win32Exception(Marshal.GetLastPInvokeError()).Message;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle OpenFileById(SafeFileHandle volumeHint, byte* id, uint access, uint share, nint security, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, byte* buffer, uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, uint* returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationByHandle(SafeFileHandle handle, char* label, uint labelSize, out uint serial, out uint maxComponent, out uint flags, char* fileSystem, uint fileSystemSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumePathName(string path, char* root, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint size, uint flags);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindFirstFile(string path, byte* data);

    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstFileNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindFirstFileName(string path, uint flags, uint* length, char* name);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextFileNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindNextFileName(nint find, uint* length, char* name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FindClose(nint find);

    [LibraryImport("advapi32.dll")]
    private static partial int GetSecurityInfo(SafeFileHandle handle, int objectType, uint information, out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);

    [LibraryImport("advapi32.dll", EntryPoint = "ConvertSecurityDescriptorToStringSecurityDescriptorW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertSecurityDescriptorToStringSecurityDescriptor(nint descriptor, uint revision, uint information, out nint text, out uint length);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
