using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// A volume's USN change journal, read through a volume handle opened only to read (administrator rights): what the
/// journal holds (FSCTL_QUERY_USN_JOURNAL) and its entries in order (FSCTL_READ_USN_JOURNAL, versions 2 and 3).
/// Reading changes nothing, and entries overwritten while it reads are skipped from the new oldest one.
/// </summary>
internal static unsafe partial class UsnJournalReader
{
    private const uint GenericRead = 0x8000_0000, FileReadAttributes = 0x80, Synchronize = 0x0010_0000;
    private const uint FsctlQueryUsnJournal = 0x0009_00F4, FsctlReadUsnJournal = 0x0009_00BB;
    public const int ErrorJournalNotActive = 1179, ErrorJournalEntryDeleted = 1181;

    /// <summary>The volume that holds <paramref name="root"/> ("C:\", "C:\Mount\Data\"), opened to read.</summary>
    public static SafeFileHandle OpenVolume(string root)
    {
        var buffer = new char[64];
        string device;
        fixed (char* b = buffer)
            device = GetVolumeNameForVolumeMountPoint(root, b, (uint)buffer.Length) ? new string(b).TrimEnd('\0').TrimEnd('\\') : @"\\.\" + root.TrimEnd('\\');
        var handle = CreateFile(device, GenericRead, 3, 0, 3, 0, 0);
        if (!handle.IsInvalid) return handle;
        int error = Marshal.GetLastPInvokeError();
        handle.Dispose();
        if (error == 5) throw new UnauthorizedAccessException($"The volume {root} cannot be read directly: reading it needs administrator rights.");
        throw new IOException($"The volume {root} cannot be read directly: {new Win32Exception(error).Message}");
    }

    /// <summary>What the journal holds, or null with the error (1179: the journal is off).</summary>
    public static UsnJournalInfo? Query(SafeFileHandle volume, out int error)
    {
        var info = new byte[80];
        error = Ioctl(volume, FsctlQueryUsnJournal, [], info, out int got);
        return error == 0 ? UsnJournalInfo.Parse(info.AsSpan(0, got)) : null;
    }

    /// <summary>
    /// The entries from <paramref name="start"/> to the journal's end as it was when asked, in batches as the file system
    /// hands them out (about a megabyte each).
    /// </summary>
    public static void Read(SafeFileHandle volume, UsnJournalInfo journal, long start, CancellationToken ct, Action<List<UsnRecord>> batch)
    {
        var input = new byte[48];
        var output = new byte[1 << 20];
        var info = new byte[80];
        bool restarted = false;
        while (start < journal.NextUsn)
        {
            ct.ThrowIfCancellationRequested();
            BinaryPrimitives.WriteInt64LittleEndian(input, start);
            BinaryPrimitives.WriteUInt32LittleEndian(input.AsSpan(8), 0xFFFF_FFFF); // every reason
            BinaryPrimitives.WriteUInt64LittleEndian(input.AsSpan(32), journal.JournalId);
            BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(40), 2);
            BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(42), 3);
            int error = Ioctl(volume, FsctlReadUsnJournal, input, output, out int got);
            if (error == ErrorJournalEntryDeleted && !restarted)
            {
                // The oldest entries were overwritten while this read: go on from the new oldest.
                restarted = true;
                if (Ioctl(volume, FsctlQueryUsnJournal, [], info, out int length) != 0 || UsnJournalInfo.Parse(info.AsSpan(0, length)) is not { } again) break;
                start = again.FirstUsn;
                continue;
            }
            if (error != 0) throw new IOException(new Win32Exception(error).Message) { HResult = error };
            if (got <= 8) break;
            long next = BinaryPrimitives.ReadInt64LittleEndian(output);
            var records = UsnRecord.ParseAll(output.AsSpan(0, got), 8);
            if (records.Count > 0) batch(records);
            if (next <= start) break;
            start = next;
        }
    }

    /// <summary>
    /// Paths of folders by their file ID, opened through a handle on the volume (so a folder that was renamed or moved is
    /// found where it is now); folders that are gone are named from the journal's own entries when it saw them.
    /// </summary>
    public sealed class Folders(SafeFileHandle hint, string root, bool refs) : IDisposable
    {
        /// <summary>A handle on the volume to open items by ID from.</summary>
        public SafeFileHandle Hint => hint;

        /// <summary>The volume's root.</summary>
        public string Root => root;

        private readonly Dictionary<UInt128, string?> _paths = [];
        private readonly Dictionary<UInt128, (string Name, UInt128 Parent)> _seen = [];

        /// <summary>Remembers a folder's name and parent from an entry about it.</summary>
        public void Learn(UsnRecord record)
        {
            if ((record.Attributes & 0x10) != 0 && (record.Reasons & 0x1000 /* old name */) == 0) _seen[record.FileId] = (record.Name, record.ParentId);
        }

        /// <summary>The folder's path now, or for a folder that is gone, its name as the journal saw it (each answered once).</summary>
        public string Path(UInt128 id)
        {
            if (_paths.TryGetValue(id, out var known) && known is not null) return known;
            return _paths[id] = PathOf(hint, id, refs) ?? Gone(id);
        }

        private string Gone(UInt128 id)
        {
            // Its name as the journal last saw it, under the nearest folder that still exists (a few levels at most).
            var names = new List<string>();
            var current = id;
            for (int depth = 0; depth < 32 && _seen.TryGetValue(current, out var entry); depth++)
            {
                names.Add(entry.Name);
                string? parentPath = _paths.TryGetValue(entry.Parent, out var cached) ? cached : PathOf(hint, entry.Parent, refs);
                if (parentPath is not null && !parentPath.EndsWith(" (gone)", StringComparison.Ordinal))
                    return string.Join('\\', [parentPath.TrimEnd('\\'), .. Enumerable.Reverse(names)]) + " (gone)";
                current = entry.Parent;
            }
            return refs ? $"folder 0x{id:X} (gone)" : $"folder record {UsnRecord.RecordOf(id):N0} (gone)";
        }

        public void Dispose() => hint.Dispose();

        public static Folders Open(string root, bool refs)
        {
            var handle = CreateFile(root, FileReadAttributes | Synchronize, 7, 0, 3, 0x0200_0000 /* backup semantics */, 0);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new IOException($"{root} cannot be opened: {new Win32Exception(error).Message}");
            }
            return new Folders(handle, root, refs);
        }
    }

    /// <summary>Where the item with this file ID is now (its full path), or null when it is gone or cannot be opened.</summary>
    public static string? PathOf(SafeFileHandle hint, UInt128 id, bool refs)
    {
        Span<byte> descriptor = stackalloc byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(descriptor, 24);
        bool extended = refs || id >> 64 != 0;
        BinaryPrimitives.WriteInt32LittleEndian(descriptor[4..], extended ? 2 : 0);
        if (extended) BinaryPrimitives.WriteUInt128LittleEndian(descriptor[8..], id);
        else BinaryPrimitives.WriteUInt64LittleEndian(descriptor[8..], (ulong)id);
        SafeFileHandle handle;
        fixed (byte* d = descriptor) handle = OpenFileById(hint, d, FileReadAttributes | Synchronize, 7, 0, 0x0200_0000 | 0x0020_0000);
        using (handle)
        {
            if (handle.IsInvalid) return null;
            var buffer = new char[1024];
            uint length;
            fixed (char* b = buffer) length = GetFinalPathNameByHandle(handle, b, (uint)buffer.Length, 0);
            if (length == 0 || length >= buffer.Length) return null;
            string final = new(buffer, 0, (int)length);
            if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + final[8..];
            return final.StartsWith(@"\\?\", StringComparison.Ordinal) ? final[4..] : final;
        }
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

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeFileHandle OpenFileById(SafeFileHandle volumeHint, byte* id, uint access, uint share, nint security, uint flags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeHandle device, uint code, void* input, uint inputSize, void* output, uint outputSize, uint* returned, nint overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeNameForVolumeMountPointW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeNameForVolumeMountPoint(string mountPoint, char* name, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint size, uint flags);
}

/// <summary>One change-journal entry as a row: what happened (Kind), in which folder (Details); Enter goes to the item, F3 shows the whole entry.</summary>
public sealed class JournalEntryTag : IDisplayDetails, ILocatableEntry, IReportedEntry
{
    private readonly SafeFileHandle _hint;
    private readonly bool _refs;

    internal JournalEntryTag(UsnRecord record, string folder, SafeFileHandle hint, bool refs)
    {
        Record = record;
        DetailsText = folder;
        _hint = hint;
        _refs = refs;
    }

    public UsnRecord Record { get; }

    public string KindText => UsnRecord.ReasonsShort(Record.Reasons);

    /// <summary>The folder the entry names, as its path is now (or as the journal last saw it, when it is gone).</summary>
    public string DetailsText { get; }

    public (string Folder, string Name)? Locate()
    {
        string? path;
        try { path = UsnJournalReader.PathOf(_hint, Record.FileId, _refs); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException) { return null; }
        if (path is null) return null;
        string trimmed = path.TrimEnd('\\');
        return System.IO.Path.GetDirectoryName(trimmed) is { } folder ? (folder, System.IO.Path.GetFileName(trimmed)) : null;
    }

    public string ReportTitle => Record.Name;

    public string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Change journal entry");
        sb.AppendLine();
        void Line(string name, string value) => sb.Append("  ").Append(name.PadRight(14)).Append("  ").AppendLine(value);
        Line("Name", Record.Name);
        Line("Time", RecordText.Time(Record.Time));
        Line("What happened", $"{UsnRecord.ReasonsText(Record.Reasons)} (0x{Record.Reasons:X8})");
        if (UsnRecord.SourceText(Record.SourceInfo) is { } source) Line("Made by", source);
        Line("USN", Record.Usn.ToString("N0", CultureInfo.CurrentCulture));
        Line("Attributes", RecordText.Attributes(Record.Attributes));
        Line("File", Id(Record.FileId));
        Line("Folder", Id(Record.ParentId) + " · " + DetailsText);
        if (Record.SecurityId != 0) Line("Security ID", Record.SecurityId.ToString(CultureInfo.CurrentCulture));
        Line("Entry version", Record.Version.ToString(CultureInfo.InvariantCulture));
        var now = Locate();
        sb.AppendLine();
        sb.AppendLine(now is { } at
            ? $"  The item is now {System.IO.Path.Join(at.Folder, at.Name)} (Enter in the list goes there)."
            : "  The item no longer exists under this ID: it was deleted, or its record was reused.");
        return sb.ToString();
    }

    private string Id(UInt128 id) => _refs || id >> 64 != 0
        ? $"ID 0x{id:X}"
        : $"record {UsnRecord.RecordOf(id).ToString("N0", CultureInfo.CurrentCulture)}, sequence {UsnRecord.SequenceOf(id)}";
}

/// <summary>
/// A volume's change journal as a place (D-56): its entries as rows, newest first, with the time to the second, what
/// happened, the item's name, and its folder; Enter goes to the item where it is now, F3 shows the whole entry. Reading
/// the journal needs administrator rights.
/// </summary>
public sealed class UsnJournalProvider : ResourceProvider
{
    // One folder cache and volume handle per volume, kept across refreshes (their rows use it to find items).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, UsnJournalReader.Folders> FoldersByRoot = new(StringComparer.OrdinalIgnoreCase);

    public override string Scheme => Schemes.Journal;

    /// <summary>The change journal of the volume whose root is <paramref name="root"/> ("C:\").</summary>
    public static Location Of(string root) => new(Schemes.Journal, root);

    /// <summary>The change journal of the volume that holds <paramref name="path"/> (its own, for a volume mounted in a folder).</summary>
    public static Location ForPath(string path) => Of(WindowsFileRecords.VolumeRoot(path));

    /// <summary>Why the volume that holds <paramref name="path"/> has no change journal to list, or null when it may have one.</summary>
    public static string? WhyNot(string path)
    {
        if (PathUtil.IsUncPath(path)) return "A network share's change journal stays on its server.";
        try
        {
            string format = new DriveInfo(WindowsFileRecords.VolumeRoot(path)).DriveFormat;
            return format is "NTFS" or "ReFS" ? null : $"{format} keeps no change journal: only NTFS and ReFS do.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return ex.Message; }
    }

    public override string GetDisplayPath(Location location) => $"{PathUtil.WithUpperDrive(location.Path)} › change journal";

    public override string GetDisplayName(Location location) => $"{PathUtil.WithUpperDrive(location.Path).TrimEnd('\\')} change journal";

    public override Location? GetParent(Location location) => Location.FileSystem(location.Path);

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(location.Path);

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) =>
        "The change journal is read only: Enter goes to an entry's item, F3 shows the whole entry.";

    public override Location? GetChildLocation(Location parent, in EntryData entry) => null;

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct) => Task.Run(() =>
    {
        if (!Environment.IsPrivilegedProcess)
            throw new UnauthorizedAccessException("Reading a volume's change journal needs administrator rights: run FileCat as administrator.");
        string root = location.Path;
        using var volume = UsnJournalReader.OpenVolume(root);
        var journal = UsnJournalReader.Query(volume, out int error);
        if (journal is null)
            throw new IOException(error == UsnJournalReader.ErrorJournalNotActive
                ? $"The change journal is off on {root}: nothing records its changes."
                : $"The change journal of {root} cannot be read: {new System.ComponentModel.Win32Exception(error).Message}");
        bool refs = new DriveInfo(root).DriveFormat == "ReFS";
        var folders = FoldersByRoot.AddOrUpdate(root, _ => UsnJournalReader.Folders.Open(root, refs), (_, existing) => existing);
        UsnJournalReader.Read(volume, journal, journal.FirstUsn, ct, records =>
        {
            var batch = new EntryData[records.Count];
            for (int i = 0; i < records.Count; i++)
            {
                var r = records[i];
                folders.Learn(r);
                batch[i] = new EntryData(r.Name, (r.Attributes & 0x10) != 0 ? EntryKind.Directory : EntryKind.File, -1,
                    RecordText.ToUtc(r.Time)?.Ticks ?? 0)
                {
                    Attributes = r.Attributes,
                    Tag = new JournalEntryTag(r, folders.Path(r.ParentId), folders.Hint, refs),
                };
            }
            sink.AddBatch(batch);
        });
    }, ct);
}
